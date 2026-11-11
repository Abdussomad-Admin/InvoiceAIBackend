namespace Host.Background;

public sealed class ReminderSettings
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 8;
    public int LeaseMinutes { get; set; } = 10;
}
