using System.Security.Cryptography;
using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Common;
using Domain.Documents;

namespace Application.Documents;

internal static class DocumentMapping
{
    public static DocumentDto ToDto(this SourceDocument d) => new(
        d.Id, d.FileName, d.Kind, d.ContentType, d.SizeBytes, d.Sha256, d.PageCount, d.HasTextLayer,
        d.Status, d.RejectionReason, d.UploadedByUserId, d.CreatedAt,
        d.ExtractedText is { Length: > 1000 } text ? text[..1000] : d.ExtractedText,
        d.Kind == DocumentKind.Pdf && !d.HasTextLayer
            ? "This PDF has no readable text, so it may be a scan. Upload a photo or image of the invoice instead."
            : null,
        d.ExtractionAttempts, d.LastError);
}

public sealed class UploadDocumentHandler(
    IDocumentRepository documents, IDocumentStorage storage, IPdfTextExtractor pdf, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser, DocumentSettings settings)
{
    public async Task<DocumentDto> Handle(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentOrganization.RequireId();
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");

        var content = command.Content ?? [];
        if (content.Length == 0)
            throw new DomainException("The file is empty.");
        if (content.Length > DocumentRules.MaxBytes)
            throw new DomainException($"The file is larger than {DocumentRules.MaxBytes / (1024 * 1024)} MB.");

        var kind = DocumentRules.Detect(content)
            ?? throw new DomainException("Only PDF, JPEG and PNG files are supported.");

        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var existing = await documents.GetActiveByHashAsync(sha256, cancellationToken);
        if (existing is not null)
            throw new ConflictException($"This file was already uploaded as '{existing.FileName}'.");

        var stored = await documents.GetStoredBytesAsync(cancellationToken);
        if (stored + content.Length > settings.MaxStoredMegabytes * 1024L * 1024L)
            throw new DomainException("Your document storage is full. Reject documents you no longer need.");

        var pageCount = 1;
        string? text = null;

        if (kind == DocumentKind.Pdf)
        {
            try
            {
                // Untrusted input, so reading is bounded in time as well as size.
                var extracted = await Task.Run(() => pdf.Extract(content), cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                pageCount = extracted.PageCount;
                text = extracted.Text;
            }
            catch (TimeoutException)
            {
                throw new DomainException("This PDF took too long to read. Try a simpler file.");
            }
        }

        var document = SourceDocument.Create(
            organizationId, userId, kind, command.FileName, content.Length, sha256, pageCount, text);

        documents.Add(document);
        await storage.SaveAsync(organizationId, document.Id, content, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return document.ToDto();
    }
}

public sealed class ListDocumentsHandler(IDocumentRepository documents)
{
    public async Task<PagedResult<DocumentSummaryDto>> Handle(ListDocumentsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await documents.SearchAsync(
            query.Status, (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<DocumentSummaryDto>(items, total, page, pageSize);
    }
}

public sealed class GetDocumentHandler(IDocumentRepository documents)
{
    public async Task<DocumentDto> Handle(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Document not found.");
        return document.ToDto();
    }
}

public sealed class DownloadDocumentHandler(IDocumentRepository documents, IDocumentStorage storage)
{
    public async Task<DocumentFile> Handle(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        if (document.Status == DocumentStatus.Rejected)
            throw new NotFoundException("The content of a rejected document is not kept.");

        var content = await storage.ReadAsync(document.Id, cancellationToken)
            ?? throw new NotFoundException("The document content is not available.");

        return new DocumentFile(content, document.FileName, document.ContentType);
    }
}

public sealed class RejectDocumentHandler(
    IDocumentRepository documents, IDocumentStorage storage, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<DocumentDto> Handle(Guid id, RejectDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        document.Reject(command.Reason, clock.GetUtcNow());

        // The record and its hash stay for the history. The file itself is removed, freeing space.
        await storage.DeleteAsync(document.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return document.ToDto();
    }
}