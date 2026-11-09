using Application.Documents;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DocumentExtractionRepository(AppDbContext context) : IDocumentExtractionRepository
{
    public void Add(DocumentExtraction extraction) => context.DocumentExtractions.Add(extraction);

    public async Task<IReadOnlyList<ExtractionAttemptDto>> ListAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        // The raw model output is kept in the table but deliberately not returned in this list.
        var rows = await context.DocumentExtractions.AsNoTracking()
            .Where(e => e.DocumentId == documentId)
            .OrderByDescending(e => e.AttemptNumber)
            .Select(e => new { e.AttemptNumber, e.CreatedAt, e.Succeeded, e.Model, e.DurationMs, e.IssueCount, e.ErrorMessage })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ExtractionAttemptDto(
            r.AttemptNumber, r.CreatedAt, r.Succeeded, r.Model, r.DurationMs, r.IssueCount, r.ErrorMessage)).ToList();
    }
}