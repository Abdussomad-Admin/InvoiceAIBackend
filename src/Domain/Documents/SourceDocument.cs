using System.Text.RegularExpressions;
using Domain.Common;

namespace Domain.Documents;

public enum DocumentStatus
{
    Uploaded = 0,
    Rejected = 1,
    ReadyForReview = 2,
    Failed = 3
}

// Metadata about an uploaded supplier document. The file's bytes live in DocumentBlob, never here.
public sealed class SourceDocument : BaseEntity, ITenantOwned
{
    public const int MaxExtractionAttempts = 5;

    private SourceDocument() { }

    public Guid OrganizationId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string FileName { get; private set; } = null!;
    public DocumentKind Kind { get; private set; }
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public int PageCount { get; private set; }
    public bool HasTextLayer { get; private set; }
    public string? ExtractedText { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }

    public string? ExtractedData { get; private set; }     // the latest validated extraction, as JSON
    public int ExtractionAttempts { get; private set; }
    public string? LastError { get; private set; }

    public string ContentType => DocumentRules.ContentTypeOf(Kind);

    public static SourceDocument Create(
        Guid organizationId, Guid uploadedByUserId, DocumentKind kind, string? fileName,
        long sizeBytes, string sha256, int pageCount, string? extractedText)
    {
        if (organizationId == Guid.Empty) throw new DomainException("OrganizationId is required.");
        if (uploadedByUserId == Guid.Empty) throw new DomainException("The uploading user is required.");
        if (!Enum.IsDefined(kind)) throw new DomainException("File type is not valid.");
        if (sizeBytes <= 0 || sizeBytes > DocumentRules.MaxBytes) throw new DomainException("File size is not valid.");
        if (!Regex.IsMatch(sha256 ?? "", "^[0-9a-f]{64}$")) throw new DomainException("File hash is not valid.");
        if (pageCount < 1 || pageCount > DocumentRules.MaxPdfPages) throw new DomainException("Page count is not valid.");

        var text = extractedText is { Length: > DocumentRules.MaxExtractedChars }
            ? extractedText[..DocumentRules.MaxExtractedChars]
            : extractedText;

        var readableCharacters = text?.Count(c => !char.IsWhiteSpace(c)) ?? 0;

        return new SourceDocument
        {
            OrganizationId = organizationId,
            UploadedByUserId = uploadedByUserId,
            FileName = DocumentRules.SafeFileName(fileName),
            Kind = kind,
            SizeBytes = sizeBytes,
            Sha256 = sha256!,
            PageCount = pageCount,
            ExtractedText = text,
            HasTextLayer = kind == DocumentKind.Pdf && readableCharacters >= DocumentRules.MinTextCharsForTextLayer,
            Status = DocumentStatus.Uploaded
        };
    }

    public void EnsureCanExtract()
    {
        if (Status is not (DocumentStatus.Uploaded or DocumentStatus.ReadyForReview or DocumentStatus.Failed))
            throw new DomainException("This document can no longer be extracted.");

        if (ExtractionAttempts >= MaxExtractionAttempts)
            throw new DomainException(
                $"This document has used all {MaxExtractionAttempts} extraction attempts. " +
                "Reject it and upload it again, or enter the bill by hand.");
    }

    public void RecordExtraction(string extractedJson)
    {
        EnsureCanExtract();

        ExtractedData = Guard.NotBlank(extractedJson, nameof(ExtractedData), 200_000);
        ExtractionAttempts++;
        LastError = null;
        Status = DocumentStatus.ReadyForReview;
    }

    public void RecordFailure(string error)
    {
        EnsureCanExtract();

        var message = error.Length > 500 ? error[..500] : error;
        ExtractionAttempts++;
        LastError = Guard.NotBlank(message, nameof(LastError), 500);

        // An earlier good extraction stays reviewable even when a retry fails.
        Status = ExtractedData is null ? DocumentStatus.Failed : DocumentStatus.ReadyForReview;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        if (Status is not (DocumentStatus.Uploaded or DocumentStatus.ReadyForReview or DocumentStatus.Failed))
            throw new DomainException("This document can no longer be rejected.");

        RejectionReason = Guard.NotBlank(reason, nameof(RejectionReason), 500);
        RejectedAt = now;
        Status = DocumentStatus.Rejected;
    }
}