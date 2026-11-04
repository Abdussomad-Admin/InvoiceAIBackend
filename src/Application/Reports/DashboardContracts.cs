using Domain.Invoices;

namespace Application.Reports;

public sealed record DashboardQuery(DateOnly? From, DateOnly? To);

public sealed record MonthlyPointDto(
    string Month, decimal Invoiced, int InvoiceCount, decimal Collected, int PaymentCount);

public sealed record TopCustomerDto(Guid CustomerId, string CustomerName, decimal Outstanding, int OpenInvoices);

public sealed record StatusCountsDto(int Draft, int Issued, int PartiallyPaid, int Paid, int Void);

public sealed record MetricDefinitionDto(string Name, string Meaning);

public sealed record DashboardDto(
    DateOnly From, DateOnly To, string CurrencyCode,
    decimal TotalInvoiced, int InvoiceCount,
    decimal TotalCollected, int PaymentCount,
    decimal OutstandingReceivables, int OpenInvoices,
    decimal OverdueReceivables, int OverdueInvoices,
    StatusCountsDto InvoiceStatuses,
    IReadOnlyList<MonthlyPointDto> Monthly,
    IReadOnlyList<TopCustomerDto> TopCustomers,
    IReadOnlyList<MetricDefinitionDto> Definitions);

public sealed record DatedAmount(DateOnly Date, decimal Amount);

public interface IDashboardRepository
{
    Task<IReadOnlyList<DatedAmount>> GetInvoicedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatedAmount>> GetCollectedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<InvoiceStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default);
}

public static class DashboardMath
{
    public const int MaxMonths = 24;

    public static string MonthKey(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    // Every calendar month from the month of `from` to the month of `to`, inclusive.
    public static IReadOnlyList<string> Months(DateOnly from, DateOnly to)
    {
        var months = new List<string>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var last = new DateOnly(to.Year, to.Month, 1);

        while (cursor <= last)
        {
            months.Add(MonthKey(cursor));
            cursor = cursor.AddMonths(1);
        }

        return months;
    }

    // The first day of the month five months back, so the default view covers six months.
    public static DateOnly DefaultFrom(DateOnly today)
        => new DateOnly(today.Year, today.Month, 1).AddMonths(-5);
}