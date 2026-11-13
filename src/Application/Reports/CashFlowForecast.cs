using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;

namespace Application.Reports;

public sealed record CashFlowForecastItemDto(
    DateOnly DueDate, string Direction, string Counterparty, string Reference, decimal Amount);

public sealed record CashFlowForecastDto(
    DateOnly AsOf, DateOnly Through, string CurrencyCode,
    decimal ExpectedIncoming, decimal ExpectedOutgoing, decimal ExpectedNetMovement,
    decimal OverdueIncoming, decimal OverdueOutgoing,
    IReadOnlyList<CashFlowForecastItemDto> ScheduledItems,
    IReadOnlyList<string> Assumptions);

public static class CashFlowForecastBuilder
{
    public static CashFlowForecastDto Build(
        DateOnly today, string currencyCode,
        IReadOnlyList<OpenInvoiceRow> invoices, IReadOnlyList<OpenBillRow> bills)
    {
        var through = today.AddDays(30);
        var overdueIncoming = invoices.Where(row => row.DueDate < today).Sum(row => row.BaseBalance);
        var overdueOutgoing = bills.Where(row => row.DueDate < today).Sum(row => row.Balance);

        var items = invoices
            .Where(row => row.DueDate >= today && row.DueDate <= through)
            .Select(row => new CashFlowForecastItemDto(row.DueDate, "Incoming", row.CustomerName,
                row.Number ?? "Invoice", row.BaseBalance))
            .Concat(bills
                .Where(row => row.DueDate >= today && row.DueDate <= through)
                .Select(row => new CashFlowForecastItemDto(row.DueDate, "Outgoing", row.SupplierName,
                    row.Number, row.Balance)))
            .OrderBy(row => row.DueDate)
            .ThenBy(row => row.Direction)
            .ToList();

        var incoming = items.Where(row => row.Direction == "Incoming").Sum(row => row.Amount);
        var outgoing = items.Where(row => row.Direction == "Outgoing").Sum(row => row.Amount);

        return new CashFlowForecastDto(today, through, currencyCode, incoming, outgoing, incoming - outgoing,
            overdueIncoming, overdueOutgoing, items,
            [
                "Scheduled amounts come from outstanding invoices and supplier bills due in the next 30 days. They are estimates; customers and suppliers may pay earlier or later.",
                "Overdue balances are shown separately because their payment dates are uncertain.",
                "This is expected cash movement, not a bank balance or a prediction of available cash. No opening balance, unrecorded costs, recurring expenses or historical income pattern is included."
            ]);
    }
}

public sealed class GetCashFlowForecastHandler(
    IReceivablesRepository receivables, IPayablesRepository payables,
    IOrganizationRepository organizations, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<CashFlowForecastDto> Handle(CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        var invoices = await receivables.GetOpenInvoicesAsync(null, cancellationToken);
        var bills = await payables.GetOpenBillsAsync(null, cancellationToken);
        return CashFlowForecastBuilder.Build(today, organization.BaseCurrencyCode, invoices, bills);
    }
}
