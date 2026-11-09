using Application.Documents;
using Domain.Documents;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Documents;

// Stores file bytes in PostgreSQL. To move to object storage later, replace this one class.
public class DatabaseDocumentStorage(AppDbContext context) : IDocumentStorage
{
    public Task SaveAsync(Guid organizationId, Guid documentId, byte[] content, CancellationToken cancellationToken = default)
    {
        context.DocumentBlobs.Add(DocumentBlob.Create(organizationId, documentId, content));
        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReadAsync(Guid documentId, CancellationToken cancellationToken = default)
        => await context.DocumentBlobs.AsNoTracking()
            .Where(b => b.DocumentId == documentId)
            .Select(b => b.Content)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var blob = await context.DocumentBlobs.FirstOrDefaultAsync(b => b.DocumentId == documentId, cancellationToken);
        if (blob is not null) context.DocumentBlobs.Remove(blob);
    }
}