using Application.Suppliers;
using Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SupplierRepository(AppDbContext context) : ISupplierRepository
{
    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Supplier> Items, int Total)> SearchAsync(
        string? search, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Suppliers.AsNoTracking().AsQueryable();

        if (!includeArchived)
            query = query.Where(s => !s.IsArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern) ||
                (s.Email != null && EF.Functions.ILike(s.Email, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(s => s.Name).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(Supplier supplier) => context.Suppliers.Add(supplier);

    public async Task<IReadOnlyList<SupplierCandidate>> ListForMatchingAsync(
    CancellationToken cancellationToken = default)
    => await context.Suppliers.AsNoTracking()
        .Where(s => !s.IsArchived)
        .OrderBy(s => s.Name)
        .Take(1000)
        .Select(s => new SupplierCandidate(s.Id, s.Name, s.TaxIdentificationNumber))
        .ToListAsync(cancellationToken);
}