using Application.Customers;
using Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CustomerRepository(AppDbContext context) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Customer> Items, int Total)> SearchAsync(
        string? search, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Customers.AsNoTracking().AsQueryable();

        if (!includeArchived)
            query = query.Where(c => !c.IsArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var escaped = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern) ||
                (c.Email != null && EF.Functions.ILike(c.Email, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Name).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(Customer customer) => context.Customers.Add(customer);

    public async Task<IReadOnlyList<Customer>> ExportAsync(CancellationToken cancellationToken = default)
        => await context.Customers.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Name)
            .Take(50_001).ToListAsync(cancellationToken);

    public async Task<HashSet<string>> ExistingEmailsAsync(
        IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default)
    {
        var normalized = emails.Select(e => e.Trim().ToLower()).Distinct().ToArray();
        return (await context.Customers.AsNoTracking()
            .Where(c => c.Email != null && normalized.Contains(c.Email.ToLower()))
            .Select(c => c.Email!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void AddRange(IReadOnlyCollection<Customer> customers) => context.Customers.AddRange(customers);
}
