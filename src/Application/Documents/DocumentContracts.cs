using Domain.Documents;

namespace Application.Documents;

public sealed record UploadDocumentCommand(string? FileName, byte[] Content);
public sealed record RejectDocumentCommand(string Reason);
public sealed record ListDocumentsQuery(DocumentStatus? Status, int Page, int PageSize);

public sealed record DocumentSummaryDto(
    Guid Id, string FileName, DocumentKind Kind, string ContentType, long SizeBytes, int PageCount,
    bool HasTextLayer, DocumentStatus Status, DateTimeOffset UploadedAt);

public sealed record DocumentDto(
    Guid Id, string FileName, DocumentKind Kind, string ContentType, long SizeBytes, string Sha256,
    int PageCount, bool HasTextLayer, DocumentStatus Status, string? RejectionReason,
    Guid UploadedByUserId, DateTimeOffset UploadedAt, string? TextPreview, string? Advice,
    int ExtractionAttempts, string? LastError, Guid? BillId);

public sealed record DocumentFile(byte[] Content, string FileName, string ContentType);

public sealed record PdfTextResult(int PageCount, string Text);

public sealed class DocumentSettings
{
    public int MaxStoredMegabytes { get; set; } = 200;

    public void Validate()
    {
        if (MaxStoredMegabytes is < 1 or > 5000)
            throw new InvalidOperationException("Documents:MaxStoredMegabytes must be between 1 and 5000.");
    }
}

public interface IPdfTextExtractor
{
    // Throws DomainException when the PDF is damaged, password-protected, or too long.
    PdfTextResult Extract(byte[] content);
}

public interface IDocumentStorage
{
    // These only prepare changes. The handler saves once, so the record and its bytes commit together.
    Task SaveAsync(Guid organizationId, Guid documentId, byte[] content, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);
}

public interface IDocumentRepository
{
    Task<Domain.Documents.SourceDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Domain.Documents.SourceDocument?> GetActiveByHashAsync(string sha256, CancellationToken cancellationToken = default);
    Task<long> GetStoredBytesAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DocumentSummaryDto> Items, int Total)> SearchAsync(
        DocumentStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    // Locks the document row until the current transaction ends. False if it is not visible to this organization.
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Domain.Documents.SourceDocument?> GetByBillIdAsync(Guid billId, CancellationToken cancellationToken = default);
    void Add(Domain.Documents.SourceDocument document);
}