using System.Text.RegularExpressions;

namespace Application.Assistant;

public sealed class AssistantSettings
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemma-4-26b-a4b-it";
    public string Thinking { get; set; } = "minimal";   // "minimal" (off) or "high" (on)
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxToolRounds { get; set; } = 3;
    public int MaxQuestionLength { get; set; } = 500;
    public int QuestionsPerHour { get; set; } = 30;

    // Called at startup. Nothing is checked while the assistant is switched off.
    public void Validate()
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Assistant:ApiKey is required when Assistant:Enabled is true.");
        if (!Regex.IsMatch(Model ?? "", "^[A-Za-z0-9._-]{1,80}$"))
            throw new InvalidOperationException("Assistant:Model contains characters that are not allowed.");
        if (Thinking is not ("minimal" or "high"))
            throw new InvalidOperationException("Assistant:Thinking must be 'minimal' or 'high'.");
        if (TimeoutSeconds is < 5 or > 120)
            throw new InvalidOperationException("Assistant:TimeoutSeconds must be between 5 and 120.");
        if (MaxToolRounds is < 1 or > 5)
            throw new InvalidOperationException("Assistant:MaxToolRounds must be between 1 and 5.");
        if (MaxQuestionLength is < 20 or > 2000)
            throw new InvalidOperationException("Assistant:MaxQuestionLength must be between 20 and 2000.");
        if (QuestionsPerHour is < 1 or > 1000)
            throw new InvalidOperationException("Assistant:QuestionsPerHour must be between 1 and 1000.");
    }
}