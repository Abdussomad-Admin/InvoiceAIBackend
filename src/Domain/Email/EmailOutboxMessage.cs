using Domain.Common;

namespace Domain.Email;

public sealed class EmailOutboxMessage : BaseEntity, ITenantOwned
{
    private EmailOutboxMessage() { }

    public Guid OrganizationId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string To { get; private set; } = null!;
    public string? ReplyTo { get; private set; }
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ProcessingUntil { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public bool DeadLettered { get; private set; }

    public static EmailOutboxMessage Create(Guid organizationId, string idempotencyKey,
        string to, string? replyTo, string subject, string body, DateTimeOffset now)
        => new()
        {
            OrganizationId = organizationId,
            IdempotencyKey = idempotencyKey,
            To = to,
            ReplyTo = replyTo,
            Subject = subject,
            Body = body,
            AvailableAt = now
        };

    public void Claim(DateTimeOffset until)
    {
        ProcessingUntil = until;
        Attempts++;
    }

    public void MarkSent(DateTimeOffset now)
    {
        SentAt = now;
        ProcessingUntil = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset retryAt, bool deadLetter)
    {
        ProcessingUntil = null;
        LastError = error.Length <= 1000 ? error : error[..1000];
        AvailableAt = retryAt;
        DeadLettered = deadLetter;
    }
}
