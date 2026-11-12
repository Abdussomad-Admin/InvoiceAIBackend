namespace Application.Reports;

public enum AgingBucket
{
    Current = 0,
    Days1To30 = 1,
    Days31To60 = 2,
    Days61To90 = 3,
    Over90 = 4
}

public static class Aging
{
    // Due today still counts as "not yet due", matching Invoice.IsOverdue (overdue means due BEFORE today).
    public static AgingBucket Classify(DateOnly dueDate, DateOnly today)
    {
        var daysOverdue = today.DayNumber - dueDate.DayNumber;
        return daysOverdue switch
        {
            <= 0 => AgingBucket.Current,
            <= 30 => AgingBucket.Days1To30,
            <= 60 => AgingBucket.Days31To60,
            <= 90 => AgingBucket.Days61To90,
            _ => AgingBucket.Over90
        };
    }

    public static string Label(AgingBucket bucket) => bucket switch
    {
        AgingBucket.Current => "Not yet due",
        AgingBucket.Days1To30 => "1-30 days overdue",
        AgingBucket.Days31To60 => "31-60 days overdue",
        AgingBucket.Days61To90 => "61-90 days overdue",
        _ => "Over 90 days overdue"
    };
}

public sealed record OpenInvoiceRow(
    Guid InvoiceId, string? Number, Guid CustomerId, string CustomerName, DateOnly DueDate,
    string CurrencyCode, decimal Balance, decimal BaseBalance);

public sealed record CustomerTotals(decimal Invoiced, decimal Paid);

public sealed record CustomerSummaryDto(
    Guid CustomerId, string CustomerName, string CurrencyCode,
    decimal TotalInvoiced, decimal TotalPaid, decimal Outstanding, decimal Overdue,
    int OpenInvoices, int OverdueInvoices);

public sealed record AgingBucketDto(AgingBucket Bucket, string Label, int InvoiceCount, decimal Balance);

public sealed record CustomerAgingDto(
    Guid CustomerId, string CustomerName,
    decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);

public sealed record AgingReportDto(
    DateOnly AsOf, string CurrencyCode, decimal TotalOutstanding, decimal TotalOverdue,
    IReadOnlyList<AgingBucketDto> Buckets, IReadOnlyList<CustomerAgingDto> Customers);

public interface IReceivablesRepository
{
    Task<IReadOnlyList<OpenInvoiceRow>> GetOpenInvoicesAsync(Guid? customerId, CancellationToken cancellationToken = default);
    Task<CustomerTotals> GetCustomerTotalsAsync(Guid customerId, CancellationToken cancellationToken = default);
}
