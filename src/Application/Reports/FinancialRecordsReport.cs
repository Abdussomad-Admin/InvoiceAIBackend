using Application.Organizations;

namespace Application.Reports;

public sealed record FinancialRecordsReportQuery(DateOnly? From, DateOnly? To);

public sealed record FinancialRecordsReportDto(
    string OrganizationName, string CurrencyCode, DateOnly From, DateOnly To, DateTimeOffset GeneratedAt,
    decimal Income, decimal Expenses, decimal EstimatedProfit, decimal CashReceived, decimal CashPaidOut,
    decimal NetCashFlow, int InvoiceCount, int BillCount,
    decimal Receivables, decimal OverdueReceivables, decimal Payables, decimal OverduePayables,
    IReadOnlyList<MonthlyFinancialDto> Monthly, IReadOnlyList<string> Notes);

public sealed class GetFinancialRecordsReportHandler(
    GetFinancialSummaryHandler summary, GetAgingHandler receivables,
    GetPayablesAgingHandler payables, GetOrganizationHandler organizations, TimeProvider clock)
{
    public async Task<FinancialRecordsReportDto> Handle(FinancialRecordsReportQuery query, CancellationToken ct)
    {
        var financial = await summary.Handle(new FinancialSummaryQuery(query.From, query.To), ct);
        var customers = await receivables.Handle(ct);
        var suppliers = await payables.Handle(ct);
        var organization = await organizations.Handle(ct);
        var period = financial.Period;

        return new FinancialRecordsReportDto(
            organization.Name, financial.CurrencyCode, period.From, period.To, clock.GetUtcNow(),
            period.Income, period.Expenses, period.EstimatedProfit, period.CashReceived, period.CashPaidOut,
            period.NetCashFlow, period.InvoiceCount, period.BillCount,
            customers.TotalOutstanding, customers.TotalOverdue, suppliers.TotalOutstanding, suppliers.TotalOverdue,
            financial.Monthly,
            financial.Caveats.Concat([
                "Outstanding customer and supplier balances are current balances as of the report generation date, not balances limited to the selected period.",
                "This report contains records saved in Invoice AI only. It is not a credit score, credit decision, or guarantee of loan eligibility.",
                "Download saves a copy to your device. Invoice AI does not send this report to a lender or other third party."
            ]).ToArray());
    }
}

public sealed record FinancialRecordsPdfFile(byte[] Content, string FileName);

public interface IFinancialRecordsPdfRenderer
{
    byte[] Render(FinancialRecordsReportDto report);
}

public sealed class DownloadFinancialRecordsReportHandler(
    GetFinancialRecordsReportHandler report, IFinancialRecordsPdfRenderer renderer)
{
    public async Task<FinancialRecordsPdfFile> Handle(FinancialRecordsReportQuery query, CancellationToken ct)
    {
        var model = await report.Handle(query, ct);
        return new FinancialRecordsPdfFile(renderer.Render(model),
            $"financial-records-{model.From:yyyyMMdd}-{model.To:yyyyMMdd}.pdf");
    }
}
