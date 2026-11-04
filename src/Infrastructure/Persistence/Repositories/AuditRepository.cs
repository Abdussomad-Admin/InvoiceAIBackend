using Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class AuditRepository(AppDbContext context) : IAuditRepository
{
    public async Task<(IReadOnlyList<AuditEntryDto> Items, int Total)> SearchAsync(
        AuditSearch filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query =
            from a in context.AuditEntries.AsNoTracking()
            join u in context.Users.AsNoTracking() on a.UserId equals (Guid?)u.Id into users
            from u in users.DefaultIfEmpty()
            select new { Entry = a, UserName = u == null ? null : u.FullName };

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(x => x.Entry.EntityType == filter.EntityType);
        if (filter.EntityId is { } entityId)
            query = query.Where(x => x.Entry.EntityId == entityId);
        if (filter.UserId is { } userId)
            query = query.Where(x => x.Entry.UserId == userId);
        if (filter.From is { } from)
            query = query.Where(x => x.Entry.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(x => x.Entry.CreatedAt <= to);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.Entry.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new AuditEntryDto(
            x.Entry.Id, x.Entry.CreatedAt, x.Entry.UserId, x.UserName,
            x.Entry.Action, x.Entry.EntityType, x.Entry.EntityId, x.Entry.Details)).ToList();

        return (items, total);
    }
}