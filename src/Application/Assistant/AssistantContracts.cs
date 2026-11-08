using System.Text.Json;
using Domain.Organizations;

namespace Application.Assistant;

public sealed record AskAssistantCommand(string? Question);

public sealed record ToolParameter(string Name, string Type, string Description, bool Required);

public sealed record ToolDefinition(string Name, string Description, IReadOnlyList<ToolParameter> Parameters);

public sealed record ModelToolCall(string Name, IReadOnlyDictionary<string, JsonElement> Arguments, string? Id);

public sealed record ModelToolResponse(ModelToolCall Call, JsonElement Result);

// RawContent is the model's own turn exactly as it came back, replayed unchanged on the next request.
public sealed record ModelTurn(string? Text, IReadOnlyList<ModelToolCall> ToolCalls, JsonElement? RawContent);

public sealed record AssistantStep(ModelTurn Turn, IReadOnlyList<ModelToolResponse> Responses);

public sealed record AssistantConversation(
    string SystemPrompt, IReadOnlyList<ToolDefinition> Tools, string Question, IReadOnlyList<AssistantStep> Steps);

public sealed record AssistantSourceDto(string Tool, JsonElement Arguments, JsonElement Data, bool Succeeded);

public sealed record AssistantAnswerDto(string Answer, IReadOnlyList<AssistantSourceDto> Sources, string Notice);

public sealed record AssistantToolInfoDto(string Name, string Description);

public interface IAssistantModel
{
    Task<ModelTurn> GenerateAsync(AssistantConversation conversation, CancellationToken cancellationToken);
}

public interface IAssistantTool
{
    ToolDefinition Definition { get; }
    Permission RequiredPermission { get; }
    Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
}