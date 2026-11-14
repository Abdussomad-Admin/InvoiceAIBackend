using Application.Organizations;
using Application.Banking;
using Application.Payments;
using Application.Bills;

namespace Application.Reports;

public sealed record FinancialRecordsReportQuery(DateOnly? From, DateOnly? To);
public sealed record FinancialRecordBusinessDetailsDto(
    string Name, string? Email, string? Phone, string CountryCode, string? Address, string? City, string? Region);
public sealed record FinancialRecordCoverageItemDto(string Label, string Status, string Detail);
public sealed record FinancialRecordCoverageDto(
    IReadOnlyList<FinancialRecordCoverageItemDto> Items,
    int ImportedStatementLines, int MatchedStatementLines, int IgnoredStatementLines, int UnresolvedStatementLines);
public sealed record FinancialRecordPaymentDto(
    DateOnly PaidOn, string Direction, string Party, string DocumentNumber, decimal Amount,
    string CurrencyCode, string Method, string? Reference, string Status);

public sealed record FinancialRecordsReportDto(
    FinancialRecordBusinessDetailsDto Business, string CurrencyCode, DateOnly From, DateOnly To, DateTimeOffset GeneratedAt,
    decimal Income, decimal Expenses, decimal EstimatedProfit, decimal CashReceived, decimal CashPaidOut,
    decimal NetCashFlow, int InvoiceCount, int BillCount,
    decimal Receivables, decimal OverdueReceivables, decimal Payables, decimal OverduePayables,
    IReadOnlyList<MonthlyFinancialDto> Monthly, IReadOnlyList<FinancialRecordPaymentDto> Payments,
    int TotalPaymentRecords, FinancialRecordCoverageDto Coverage, IReadOnlyList<string> Notes);

public sealed class GetFinancialRecordsReportHandler(
    GetFinancialSummaryHandler summary, GetAgingHandler receivables,
    GetPayablesAgingHandler payables, GetOrganizationHandler organizations,
    IBankStatementRepository bankStatements, ListPaymentsHandler customerPayments,
    ListBillPaymentsHandler supplierPayments, TimeProvider clock)
{
    public async Task<FinancialRecordsReportDto> Handle(FinancialRecordsReportQuery query, CancellationToken ct)
    {
        var financial = await summary.Handle(new FinancialSummaryQuery(query.From, query.To), ct);
        var customers = await receivables.Handle(ct);
        var suppliers = await payables.Handle(ct);
        var organization = await organizations.Handle(ct);
        var bankCoverage = await bankStatements.GetCoverageAsync(ct);
        var period = financial.Period;
        var customerPaymentPage = await customerPayments.Handle(
            new ListPaymentsQuery(null, null, period.From, period.To, true, 1, 100), ct);
        var supplierPaymentPage = await supplierPayments.Handle(
            new ListBillPaymentsQuery(null, null, period.From, period.To, true, 1, 100), ct);
        var paymentHistory = customerPaymentPage.Items.Select(payment => new FinancialRecordPaymentDto(
                payment.PaidOn, "Received", payment.CustomerName, payment.InvoiceNumber ?? "Invoice",
                payment.Amount, payment.CurrencyCode, payment.Method.ToString(), payment.Reference,
                payment.IsReversed ? "Reversed" : "Recorded"))
            .Concat(supplierPaymentPage.Items.Select(payment => new FinancialRecordPaymentDto(
                payment.PaidOn, "Paid out", payment.SupplierName, payment.BillNumber,
                payment.Amount, payment.CurrencyCode, payment.Method.ToString(), payment.Reference,
                payment.IsReversed ? "Reversed" : "Recorded")))
            .OrderByDescending(payment => payment.PaidOn).Take(100).ToArray();
        var totalPaymentRecords = customerPaymentPage.Total + supplierPaymentPage.Total;
        var contact = !string.IsNullOrWhiteSpace(organization.Email) || !string.IsNullOrWhiteSpace(organization.Phone);
        var hasCashActivity = totalPaymentRecords > 0;
        var coverage = new FinancialRecordCoverageDto(
        [
            new("Business contact details", contact ? "Recorded" : "Not added",
                contact ? "At least one business contact method is saved." : "Add a business email or phone number if you want it shown on this report."),
            new("Sales invoices", period.InvoiceCount > 0 ? "Recorded" : "No records",
                $"{period.InvoiceCount} issued invoice(s) included in the selected period."),
            new("Supplier bills", period.BillCount > 0 ? "Recorded" : "No records",
                $"{period.BillCount} finalized supplier bill(s) included in the selected period."),
            new("Payment records", hasCashActivity ? "Recorded" : "No records",
                $"{totalPaymentRecords} customer and supplier payment record(s) in the selected period. Customer payments received: {period.CashReceived:N2} {financial.CurrencyCode}; supplier payments made: {period.CashPaidOut:N2} {financial.CurrencyCode}."),
            new("Bank statement review", bankCoverage.ImportedLines == 0 ? "Not started" : bankCoverage.UnresolvedLines > 0 ? "Review needed" : "Reviewed",
                bankCoverage.ImportedLines == 0
                    ? "No statement lines have been imported into this workspace."
                    : $"{bankCoverage.MatchedLines} matched, {bankCoverage.IgnoredLines} marked reviewed without a match, and {bankCoverage.UnresolvedLines} still unresolved across imported statements.")
        ], bankCoverage.ImportedLines, bankCoverage.MatchedLines, bankCoverage.IgnoredLines, bankCoverage.UnresolvedLines);

        return new FinancialRecordsReportDto(
            new FinancialRecordBusinessDetailsDto(organization.Name, organization.Email, organization.Phone,
                organization.CountryCode, string.Join(", ", new[] { organization.AddressLine1, organization.AddressLine2 }.Where(x => !string.IsNullOrWhiteSpace(x))),
                organization.City, organization.Region), financial.CurrencyCode, period.From, period.To, clock.GetUtcNow(),
            period.Income, period.Expenses, period.EstimatedProfit, period.CashReceived, period.CashPaidOut,
            period.NetCashFlow, period.InvoiceCount, period.BillCount,
            customers.TotalOutstanding, customers.TotalOverdue, suppliers.TotalOutstanding, suppliers.TotalOverdue,
            financial.Monthly, paymentHistory, totalPaymentRecords, coverage,
            financial.Caveats.Concat([
                totalPaymentRecords > paymentHistory.Length
                    ? $"Payment history lists the latest {paymentHistory.Length} of {totalPaymentRecords} records for the selected period. Use Invoice AI's payment pages for the complete history."
                    : "Payment history includes recorded and reversed customer and supplier payment entries for the selected period.",
                "Outstanding customer and supplier balances are current balances as of the report generation date, not balances limited to the selected period.",
                "This report contains records saved in Invoice AI only. It is not a credit score, credit decision, or guarantee of loan eligibility.",
                "The completeness checklist shows what is recorded in Invoice AI; it is not a score or a credit decision.",
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
