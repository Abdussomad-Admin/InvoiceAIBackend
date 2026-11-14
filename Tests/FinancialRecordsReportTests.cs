using Application.Reports;
using Infrastructure.Pdf;
using UglyToad.PdfPig;

namespace Tests;

public sealed class FinancialRecordsReportTests
{
    [Fact]
    public void Pdf_IncludesBusinessProfilePaymentHistoryAndReconciliationCoverage()
    {
        var report = new FinancialRecordsReportDto(
            Business: new FinancialRecordBusinessDetailsDto(
                "Abeokuta Tailoring", "owner@example.com", "+2348000000000", "NG", "14 Market Road", "Abeokuta", "Ogun"),
            CurrencyCode: "NGN", From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 31),
            GeneratedAt: new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
            Income: 50000m, Expenses: 12000m, EstimatedProfit: 38000m,
            CashReceived: 25000m, CashPaidOut: 5000m, NetCashFlow: 20000m,
            InvoiceCount: 3, BillCount: 2, Receivables: 25000m, OverdueReceivables: 0m,
            Payables: 7000m, OverduePayables: 0m,
            Monthly: [new MonthlyFinancialDto("Oct 2026", 50000m, 12000m, 38000m, 25000m, 5000m, 20000m)],
            Payments: [new FinancialRecordPaymentDto(new DateOnly(2026, 10, 12), "Received", "Amina Yusuf",
                "INV-0012", 25000m, "NGN", "BankTransfer", "BANK-REF-12", "Recorded")],
            TotalPaymentRecords: 1,
            Coverage: new FinancialRecordCoverageDto(
                [new FinancialRecordCoverageItemDto("Bank statement review", "Review needed", "1 unresolved statement line.")],
                1, 0, 0, 1),
            Notes: ["This report contains only records saved in Invoice AI."]);

        var bytes = new QuestPdfFinancialRecordsRenderer().Render(report);
        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join("\n", pdf.GetPages().Select(page => page.Text));

        Assert.Contains("Abeokuta Tailoring", text);
        Assert.Contains("Amina Yusuf", text);
        Assert.Contains("INV-0012", text);
        Assert.Contains("BANK-REF-12", text);
        Assert.Contains("Financial record completeness", text);
        Assert.Contains("Review needed", text);
    }
}
