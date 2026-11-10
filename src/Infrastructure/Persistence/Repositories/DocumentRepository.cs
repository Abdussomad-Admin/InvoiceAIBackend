using Application.Documents;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DocumentRepository(AppDbContext context) : IDocumentRepository
{
    public Task<SourceDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.SourceDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<SourceDocument?> GetActiveByHashAsync(string sha256, CancellationToken cancellationToken = default)
        => context.SourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Sha256 == sha256 && d.Status != DocumentStatus.Rejected, cancellationToken);

    public async Task<long> GetStoredBytesAsync(CancellationToken cancellationToken = default)
        => await context.SourceDocuments.AsNoTracking()
            .Where(d => d.Status != DocumentStatus.Rejected)
            .SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0L;

    public async Task<(IReadOnlyList<DocumentSummaryDto> Items, int Total)> SearchAsync(
        DocumentStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.SourceDocuments.AsNoTracking().AsQueryable();

        if (status is { } wanted)
            query = query.Where(d => d.Status == wanted);

        var total = await query.CountAsync(cancellationToken);

        // The extracted text is deliberately not selected here.
        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(skip).Take(take)
            .Select(d => new { d.Id, d.FileName, d.Kind, d.SizeBytes, d.PageCount, d.HasTextLayer, d.Status, d.CreatedAt })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new DocumentSummaryDto(
            r.Id, r.FileName, r.Kind, DocumentRules.ContentTypeOf(r.Kind), r.SizeBytes, r.PageCount,
            r.HasTextLayer, r.Status, r.CreatedAt)).ToList();

        return (items, total);
    }

    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The filtered query first, so another organization's document is never even locked.
        if (!await context.SourceDocuments.AsNoTracking().AnyAsync(d => d.Id == id, cancellationToken))
            return false;

        await context.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "SourceDocuments" WHERE "Id" = {id} FOR UPDATE""", cancellationToken);
        return true;
    }

    public Task<SourceDocument?> GetByBillIdAsync(Guid billId, CancellationToken cancellationToken = default)
        => context.SourceDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.BillId == billId, cancellationToken);

    public void Add(SourceDocument document) => context.SourceDocuments.Add(document);
}