using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Assistant;
using Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Assistant;

// The ONLY class that talks to Google. Replace it to change provider.
public sealed class GemmaAssistantModel(
    HttpClient http, AssistantSettings settings, ILogger<GemmaAssistantModel> logger) : IAssistantModel
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    public async Task<ModelTurn> GenerateAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        var body = BuildRequest(conversation, settings.Thinking);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{settings.Model}:generateContent")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            // The key travels in a header, never in the URL, so it cannot end up in logs or exception messages.
            request.Headers.Add("x-goog-api-key", settings.ApiKey);

            using var response = await http.SendAsync(request, timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                if (json.Length > 300) json = json[..300];
                logger.LogError("The assistant model returned HTTP {Status}: {Detail}", (int)response.StatusCode, json);
                throw new AssistantUnavailableException("The assistant is temporarily unavailable. Please try again later.");
            }

            return ParseResponse(json);
        }
        catch (AssistantUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The question and the figures are never logged, only the failure.
            logger.LogError(ex, "The assistant model call failed");
            throw new AssistantUnavailableException("The assistant is temporarily unavailable. Please try again later.");
        }
    }

    public static JsonObject BuildRequest(AssistantConversation conversation, string thinkingLevel)
    {
        var contents = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = conversation.Question })
            }
        };

        foreach (var step in conversation.Steps)
        {
            // Replay the model's own turn unchanged, so ids and any signatures it returned stay intact.
            contents.Add(step.Turn.RawContent is { } raw
                ? JsonNode.Parse(raw.GetRawText())
                : ModelContentFrom(step.Turn));

            var parts = new JsonArray();
            foreach (var response in step.Responses)
            {
                var functionResponse = new JsonObject
                {
                    ["name"] = response.Call.Name,
                    ["response"] = JsonNode.Parse(response.Result.GetRawText())
                };
                if (response.Call.Id is not null) functionResponse["id"] = response.Call.Id;

                parts.Add(new JsonObject { ["functionResponse"] = functionResponse });
            }

            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = parts });
        }

        var root = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = conversation.SystemPrompt })
            },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel }
            }
        };

        if (conversation.Tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var tool in conversation.Tools)
            {
                var declaration = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description };

                if (tool.Parameters.Count > 0)
                {
                    var properties = new JsonObject();
                    var required = new JsonArray();
                    foreach (var p in tool.Parameters)
                    {
                        properties[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
                        if (p.Required) required.Add(p.Name);
                    }

                    var parameters = new JsonObject { ["type"] = "OBJECT", ["properties"] = properties };
                    if (required.Count > 0) parameters["required"] = required;
                    declaration["parameters"] = parameters;
                }

                declarations.Add(declaration);
            }

            root["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });
        }

        return root;
    }

    private static JsonNode ModelContentFrom(ModelTurn turn)
    {
        var parts = new JsonArray();
        foreach (var call in turn.ToolCalls)
        {
            var functionCall = new JsonObject
            {
                ["name"] = call.Name,
                ["args"] = JsonNode.Parse(JsonSerializer.Serialize(call.Arguments))
            };
            if (call.Id is not null) functionCall["id"] = call.Id;

            parts.Add(new JsonObject { ["functionCall"] = functionCall });
        }

        return new JsonObject { ["role"] = "model", ["parts"] = parts };
    }

    public static ModelTurn ParseResponse(string json)
    {
        try
        {
            return Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new AssistantUnavailableException("The assistant returned an unreadable answer.");
        }
    }

    private static ModelTurn Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new AssistantUnavailableException("The assistant returned no answer.");

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return new ModelTurn(null, [], null); // for example a blocked or malformed answer

        var text = new StringBuilder();
        var calls = new List<ModelToolCall>();

        foreach (var part in parts.EnumerateArray())
        {
            // The model's private reasoning is never shown to anyone.
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                continue;

            if (part.TryGetProperty("functionCall", out var functionCall))
            {
                var arguments = new Dictionary<string, JsonElement>();
                if (functionCall.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in args.EnumerateObject())
                        arguments[property.Name] = property.Value.Clone();
                }

                var id = functionCall.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()
                    : null;

                calls.Add(new ModelToolCall(functionCall.GetProperty("name").GetString() ?? "", arguments, id));
            }
            else if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                text.Append(textElement.GetString());
            }
        }

        var answer = text.ToString().Trim();
        return new ModelTurn(answer.Length > 0 ? answer : null, calls, content.Clone());
    }
}