using Domain.Common;

namespace Domain.Documents;

// One row per extraction attempt, successful or not. Append-only history.
public sealed class DocumentExtraction : BaseEntity, ITenantOwned
{
    private DocumentExtraction() { }

    public Guid OrganizationId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int AttemptNumber { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string Model { get; private set; } = null!;
    public bool Succeeded { get; private set; }
    public int DurationMs { get; private set; }
    public int IssueCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? RawOutput { get; private set; }

    public static DocumentExtraction Create(
        Guid organizationId, Guid documentId, int attemptNumber, Guid requestedByUserId, string model,
        bool succeeded, int durationMs, int issueCount, string? errorMessage, string? rawOutput)
    {
        if (organizationId == Guid.Empty || documentId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new DomainException("Organization, document and user are required.");
        if (attemptNumber < 1) throw new DomainException("Attempt number is not valid.");

        return new DocumentExtraction
        {
            OrganizationId = organizationId,
            DocumentId = documentId,
            AttemptNumber = attemptNumber,
            RequestedByUserId = requestedByUserId,
            Model = Guard.NotBlank(model, nameof(Model), 80),
            Succeeded = succeeded,
            DurationMs = Math.Max(0, durationMs),
            IssueCount = Math.Max(0, issueCount),
            ErrorMessage = errorMessage is { Length: > 500 } ? errorMessage[..500] : errorMessage,
            RawOutput = rawOutput is { Length: > 20_000 } ? rawOutput[..20_000] : rawOutput
        };
    }
}