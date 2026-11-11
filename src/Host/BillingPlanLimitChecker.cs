using Application.Billing;
using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host;

public sealed class BillingPlanLimitChecker(
    AppDbContext db, ICurrentOrganization currentOrganization, BillingSettings billing,
    TimeProvider clock) : IPlanLimitChecker
{
    private Guid OrganizationId => currentOrganization.OrganizationId
        ?? throw new InvalidOperationException("No active organization is selected.");

    private async Task<BillingPlanSettings?> EffectivePlanAsync(CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(ct);
        if (subscription is not null && subscription.Status != Domain.Billing.SubscriptionStatus.Canceled
            && subscription.Status != Domain.Billing.SubscriptionStatus.PastDue
            && (subscription.Status != Domain.Billing.SubscriptionStatus.NonRenewing
                || subscription.CurrentPeriodEnd is { } end && end > clock.GetUtcNow())
            && (subscription.CurrentPeriodEnd is null || subscription.CurrentPeriodEnd > clock.GetUtcNow()))
            return billing.Plans.FirstOrDefault(p => p.Code == subscription.PlanCode);
        if (subscription?.Status == Domain.Billing.SubscriptionStatus.PastDue)
            return billing.Plans.FirstOrDefault(p => p.IsFree);
        return billing.Plans.FirstOrDefault(p => p.IsFree);
    }

    private async Task<(BillingPlanSettings? Plan, DateTimeOffset Start)> MonthlyAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (await EffectivePlanAsync(ct), start);
    }

    public async Task EnsureInvoiceCapacityAsync(CancellationToken cancellationToken = default)
    {
        var (plan, start) = await MonthlyAsync(cancellationToken);
        if (plan?.InvoiceLimit is not { } limit) return;
        var used = await db.Invoices.CountAsync(i => i.CreatedAt >= start, cancellationToken);
        if (used >= limit) throw LimitExceeded(limit, "invoices");
    }

    public async Task EnsureMemberCapacityAsync(string email, CancellationToken cancellationToken = default)
    {
        var plan = await EffectivePlanAsync(cancellationToken);
        if (plan?.MemberLimit is not { } limit) return;
        var active = await db.Memberships.CountAsync(m => m.IsActive, cancellationToken);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var pending = await db.Invitations.CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null
            && i.ExpiresAt > clock.GetUtcNow() && i.Email != normalizedEmail, cancellationToken);
        if (active + pending >= limit) throw LimitExceeded(limit, "members");
    }

    public async Task EnsureAiExtractionCapacityAsync(CancellationToken cancellationToken = default)
    {
        var (plan, start) = await MonthlyAsync(cancellationToken);
        if (plan?.AiExtractionLimit is not { } limit) return;
        var used = await db.DocumentExtractions.CountAsync(e => e.CreatedAt >= start, cancellationToken);
        if (used >= limit) throw LimitExceeded(limit, "AI extractions");
    }

    public async Task EnsureStorageCapacityAsync(long bytesToAdd, long serverLimitBytes, CancellationToken cancellationToken = default)
    {
        var plan = await EffectivePlanAsync(cancellationToken);
        var planLimit = plan?.StorageLimitBytes;
        var limit = planLimit is null ? serverLimitBytes : Math.Min(serverLimitBytes, planLimit.Value);
        var used = await db.SourceDocuments.Where(d => d.Status != Domain.Documents.DocumentStatus.Rejected)
            .SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0L;
        if (bytesToAdd > limit - used)
            throw new ConflictException("Your plan's document storage limit has been reached. Upgrade your plan or remove old documents.");
    }

    private static ConflictException LimitExceeded(int limit, string plural)
        => new($"Your plan allows {limit} {plural} per billing period. Upgrade your plan to continue.");
}
