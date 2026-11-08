using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Domain.Common;
using Domain.Invoices;

namespace Application.Reports;

public sealed class GetDashboardHandler(
    IDashboardRepository dashboard,
    IReceivablesRepository receivables,
    IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization,
    TimeProvider clock)
{
    private static readonly IReadOnlyList<MetricDefinitionDto> Definitions =
    [
        new("totalInvoiced", "Sum of invoice totals, INCLUDING tax, for invoices issued within the period. Drafts and voided invoices are excluded."),
        new("netInvoiced", "The same invoices, before tax and after discounts. This is the figure that counts as sales income."),
        new("taxInvoiced", "Tax charged on those invoices."),
        new("totalCollected", "Sum of payments dated within the period. Reversed payments are excluded."),
        new("outstandingReceivables", "Balance still owed on all issued and partially paid invoices right now. Not limited to the period."),
        new("overdueReceivables", "The part of the outstanding balance on invoices whose due date is before today, in the organization's time zone."),
        new("monthly", "Invoiced amounts (including tax) are grouped by the month of issue and collected amounts by the month of payment."),
        new("topCustomers", "The five customers with the largest outstanding balance right now."),
        new("invoiceStatuses", "All-time number of invoices in each status.")
    ];

    public async Task<DashboardDto> Handle(DashboardQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        var from = query.From ?? DashboardMath.DefaultFrom(today);
        var to = query.To ?? today;

        if (from > to)
            throw new DomainException("'from' must not be after 'to'.");

        var months = DashboardMath.Months(from, to);
        if (months.Count > DashboardMath.MaxMonths)
            throw new DomainException($"The date range can span at most {DashboardMath.MaxMonths} months.");

        var sales = await dashboard.GetSalesAsync(from, to, cancellationToken);
        var collected = await dashboard.GetCollectedAsync(from, to, cancellationToken);
        var open = await receivables.GetOpenInvoicesAsync(null, cancellationToken);
        var statuses = await dashboard.GetStatusCountsAsync(cancellationToken);

        var overdue = open.Where(r => r.DueDate < today).ToList();

        var monthly = months.Select(month =>
        {
            var invoicedInMonth = sales.Where(x => DashboardMath.MonthKey(x.Date) == month).ToList();
            var collectedInMonth = collected.Where(x => DashboardMath.MonthKey(x.Date) == month).ToList();

            return new MonthlyPointDto(
                month,
                invoicedInMonth.Sum(x => x.Gross), invoicedInMonth.Count,
                collectedInMonth.Sum(x => x.Amount), collectedInMonth.Count);
        }).ToList();

        var topCustomers = open
            .GroupBy(r => new { r.CustomerId, r.CustomerName })
            .Select(g => new TopCustomerDto(g.Key.CustomerId, g.Key.CustomerName, g.Sum(r => r.Balance), g.Count()))
            .OrderByDescending(c => c.Outstanding)
            .Take(5)
            .ToList();

        int CountOf(InvoiceStatus status) => statuses.TryGetValue(status, out var n) ? n : 0;

        return new DashboardDto(
            from, to, organization.BaseCurrencyCode,
            sales.Sum(x => x.Gross), sales.Count, sales.Sum(x => x.Net), sales.Sum(x => x.Tax),
            collected.Sum(x => x.Amount), collected.Count,
            open.Sum(r => r.Balance), open.Count,
            overdue.Sum(r => r.Balance), overdue.Count,
            new StatusCountsDto(
                CountOf(InvoiceStatus.Draft), CountOf(InvoiceStatus.Issued), CountOf(InvoiceStatus.PartiallyPaid),
                CountOf(InvoiceStatus.Paid), CountOf(InvoiceStatus.Void)),
            monthly, topCustomers, Definitions);
    }
}