using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Assistant;
using Application.Common.Exceptions;
using Application.Documents;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Assistant;

public sealed class GemmaDocumentExtractionModel(
    HttpClient http, AssistantSettings settings, ILogger<GemmaDocumentExtractionModel> logger) : IDocumentExtractionModel
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const string Unavailable = "The assistant is temporarily unavailable. Please try again later.";

    public async Task<string> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken)
    {
        var body = BuildRequest(request, settings.Thinking);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{settings.Model}:generateContent")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            message.Headers.Add("x-goog-api-key", settings.ApiKey);

            using var response = await http.SendAsync(message, timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                if (json.Length > 300) json = json[..300];
                logger.LogError("The extraction model returned HTTP {Status}: {Detail}", (int)response.StatusCode, json);
                throw new AssistantUnavailableException(Unavailable);
            }

            return ParseText(json);
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
            // The invoice content and the answer are never logged, only the failure.
            logger.LogError(ex, "The extraction model call failed");
            throw new AssistantUnavailableException(Unavailable);
        }
    }

    public static JsonObject BuildRequest(ExtractionRequest request, string thinkingLevel)
    {
        // The invoice text sits between markers and is declared as data. The closing marker is removed
        // from the text itself, so a document cannot "close" the data section and speak as the instructions.
        var text = request.DocumentText is null
            ? request.Instructions
            : $"{request.Instructions}\n\n<invoice_text>\n{request.DocumentText.Replace("</invoice_text>", "")}\n</invoice_text>";

        var parts = new JsonArray(new JsonObject { ["text"] = text });

        if (request.Image is { Length: > 0 } image)
        {
            parts.Add(new JsonObject
            {
                ["inline_data"] = new JsonObject
                {
                    ["mime_type"] = request.ImageContentType,
                    ["data"] = Convert.ToBase64String(image)
                }
            });
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt })
            },
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.0,
                ["maxOutputTokens"] = 4096,
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = thinkingLevel }
            }
        };
    }

    public static string ParseText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0 ||
                !candidates[0].TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                throw new AssistantUnavailableException("The assistant returned no answer.");

            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;

                if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                    text.Append(value.GetString());
            }

            return text.Length > 0
                ? text.ToString()
                : throw new AssistantUnavailableException("The assistant returned no answer.");
        }
        catch (JsonException)
        {
            throw new AssistantUnavailableException("The assistant returned an unreadable answer.");
        }
    }
}