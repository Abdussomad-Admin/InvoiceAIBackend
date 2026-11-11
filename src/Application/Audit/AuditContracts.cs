using Application.Common;

using Domain.Audit;

namespace Application.Audit;

public sealed record ListAuditQuery(
    string? EntityType, Guid? EntityId, Guid? UserId, DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize);

public sealed record AuditSearch(
    string? EntityType, Guid? EntityId, Guid? UserId, DateTimeOffset? From, DateTimeOffset? To);

public sealed record AuditEntryDto(
    Guid Id, DateTimeOffset CreatedAt, Guid? UserId, string? UserName,
    string Action, string EntityType, Guid EntityId, string? Details);

public interface IAuditRepository
{
    void Add(AuditEntry entry);

    Task<(IReadOnlyList<AuditEntryDto> Items, int Total)> SearchAsync(
        AuditSearch filter, int skip, int take, CancellationToken cancellationToken = default);
}

public sealed class ListAuditHandler(IAuditRepository audit)
{
    public async Task<PagedResult<AuditEntryDto>> Handle(ListAuditQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await audit.SearchAsync(
            new AuditSearch(query.EntityType, query.EntityId, query.UserId, query.From, query.To),
            (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<AuditEntryDto>(items, total, page, pageSize);
    }
}
