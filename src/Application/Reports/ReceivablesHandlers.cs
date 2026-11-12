using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Invoices;
using Application.Organizations;

namespace Application.Reports;

public sealed class GetCustomerSummaryHandler(
    ICustomerRepository customers, IReceivablesRepository receivables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<CustomerSummaryDto> Handle(Guid customerId, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");

        var today = InvoiceLookups.Today(organization, clock);
        var totals = await receivables.GetCustomerTotalsAsync(customerId, cancellationToken);
        var open = await receivables.GetOpenInvoicesAsync(customerId, cancellationToken);
        var overdue = open.Where(r => r.DueDate < today).ToList();

        return new CustomerSummaryDto(
            customer.Id, customer.Name, organization.BaseCurrencyCode,
            totals.Invoiced, totals.Paid, open.Sum(r => r.BaseBalance), overdue.Sum(r => r.BaseBalance),
            open.Count, overdue.Count);
    }
}

public sealed class GetAgingHandler(
    IReceivablesRepository receivables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<AgingReportDto> Handle(CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        var rows = await receivables.GetOpenInvoicesAsync(null, cancellationToken);

        var buckets = Enum.GetValues<AgingBucket>().Select(bucket =>
        {
            var inBucket = rows.Where(r => Aging.Classify(r.DueDate, today) == bucket).ToList();
            return new AgingBucketDto(bucket, Aging.Label(bucket), inBucket.Count, inBucket.Sum(r => r.BaseBalance));
        }).ToList();

        decimal Sum(IEnumerable<OpenInvoiceRow> source, AgingBucket bucket)
            => source.Where(r => Aging.Classify(r.DueDate, today) == bucket).Sum(r => r.BaseBalance);

        var customerRows = rows
            .GroupBy(r => new { r.CustomerId, r.CustomerName })
            .Select(g => new CustomerAgingDto(
                g.Key.CustomerId, g.Key.CustomerName,
                Sum(g, AgingBucket.Current), Sum(g, AgingBucket.Days1To30), Sum(g, AgingBucket.Days31To60),
                Sum(g, AgingBucket.Days61To90), Sum(g, AgingBucket.Over90), g.Sum(r => r.BaseBalance)))
            .OrderByDescending(c => c.Total)
            .ToList();

        var overdue = buckets.Where(b => b.Bucket != AgingBucket.Current).Sum(b => b.Balance);

        return new AgingReportDto(
            today, organization.BaseCurrencyCode, rows.Sum(r => r.BaseBalance), overdue, buckets, customerRows);
    }
}
