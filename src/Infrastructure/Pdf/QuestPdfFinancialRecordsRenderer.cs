using Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Pdf;

public sealed class QuestPdfFinancialRecordsRenderer : IFinancialRecordsPdfRenderer
{
    static QuestPdfFinancialRecordsRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(FinancialRecordsReportDto report)
        => Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(38);
            page.DefaultTextStyle(style => style.FontSize(9).FontColor("#252044"));
            page.Header().Column(column =>
            {
                column.Item().Text(report.Business.Name).FontSize(17).SemiBold().FontColor("#24144F");
                column.Item().PaddingTop(6).Text("BUSINESS FINANCIAL RECORDS").FontSize(20).Bold().FontColor("#6738DC");
                if (!string.IsNullOrWhiteSpace(report.Business.Email)) column.Item().Text(report.Business.Email);
                if (!string.IsNullOrWhiteSpace(report.Business.Phone)) column.Item().Text(report.Business.Phone);
                var address = string.Join(", ", new[] { report.Business.Address, report.Business.City, report.Business.Region, report.Business.CountryCode }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(address)) column.Item().Text(address);
                column.Item().PaddingTop(3).Text($"Reporting period: {report.From:dd MMM yyyy} – {report.To:dd MMM yyyy}");
                column.Item().Text($"Prepared: {report.GeneratedAt:dd MMM yyyy HH:mm} UTC · Currency: {report.CurrencyCode}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().PaddingTop(12).LineHorizontal(1).LineColor("#E6E0F3");
            });
            page.Content().PaddingVertical(14).Column(column =>
            {
                column.Spacing(13);
                column.Item().Text("Recorded activity").FontSize(13).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(1); columns.RelativeColumn(1); });
                    Metric(table, "Income from invoices", report.Income, report.CurrencyCode);
                    Metric(table, "Supplier bill expenses", report.Expenses, report.CurrencyCode);
                    Metric(table, "Estimated profit", report.EstimatedProfit, report.CurrencyCode);
                    Metric(table, "Customer payments received", report.CashReceived, report.CurrencyCode);
                    Metric(table, "Supplier payments made", report.CashPaidOut, report.CurrencyCode);
                    Metric(table, "Net cash flow", report.NetCashFlow, report.CurrencyCode);
                    Metric(table, "Open customer balances · current", report.Receivables, report.CurrencyCode);
                    Metric(table, "Overdue customer balances · current", report.OverdueReceivables, report.CurrencyCode);
                    Metric(table, "Open supplier bills · current", report.Payables, report.CurrencyCode);
                    Metric(table, "Overdue supplier bills · current", report.OverduePayables, report.CurrencyCode);
                });
                column.Item().Text($"Period records: {report.InvoiceCount} invoices · {report.BillCount} supplier bills")
                    .FontColor(Colors.Grey.Darken1);
                column.Item().Text($"Payment history · latest {report.Payments.Count} of {report.TotalPaymentRecords} records")
                    .FontSize(13).SemiBold();
                column.Item().Text("Recorded entries include manually recorded payments. ‘Recorded’ does not independently confirm receipt of funds; reversed entries are marked separately.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(.85f); columns.RelativeColumn(.8f); columns.RelativeColumn(1.15f);
                        columns.RelativeColumn(1.1f); columns.RelativeColumn(1f); columns.RelativeColumn(.85f);
                    });
                    table.Header(header =>
                    {
                        Header(header.Cell(), "Paid on"); Header(header.Cell(), "Direction");
                        Header(header.Cell(), "Customer / supplier"); Header(header.Cell(), "Invoice / bill");
                        Header(header.Cell(), "Amount"); Header(header.Cell(), "Status");
                    });
                    foreach (var payment in report.Payments)
                    {
                        Body(table.Cell(), payment.PaidOn.ToString("dd MMM yyyy"));
                        Body(table.Cell(), payment.Direction);
                        Body(table.Cell(), payment.Party);
                        Body(table.Cell(), payment.DocumentNumber);
                        Body(table.Cell(), Money(payment.Amount, payment.CurrencyCode));
                        Body(table.Cell(), string.Join(" · ", new[] { payment.Status, payment.Method, payment.Reference }
                            .Where(value => !string.IsNullOrWhiteSpace(value))));
                    }
                    if (report.Payments.Count == 0)
                        Body(table.Cell().ColumnSpan(6), "No payment records in this period.");
                });
                column.Item().Text("Monthly activity").FontSize(13).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.3f); columns.RelativeColumn(1); columns.RelativeColumn(1);
                        columns.RelativeColumn(1); columns.RelativeColumn(1);
                    });
                    table.Header(header =>
                    {
                        Header(header.Cell(), "Month"); Header(header.Cell(), "Income");
                        Header(header.Cell(), "Expenses"); Header(header.Cell(), "Received"); Header(header.Cell(), "Paid out");
                    });
                    foreach (var month in report.Monthly)
                    {
                        Body(table.Cell(), month.Month);
                        Body(table.Cell(), Money(month.Income, report.CurrencyCode));
                        Body(table.Cell(), Money(month.Expenses, report.CurrencyCode));
                        Body(table.Cell(), Money(month.CashReceived, report.CurrencyCode));
                        Body(table.Cell(), Money(month.CashPaidOut, report.CurrencyCode));
                    }
                    if (report.Monthly.Count == 0)
                        Body(table.Cell().ColumnSpan(5), "No monthly activity recorded for this period.");
                });
                column.Item().Text("Financial record completeness").FontSize(13).SemiBold();
                column.Item().Text("This checklist shows which records are present. It is not a credit score or credit decision.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.1f); columns.RelativeColumn(.8f); columns.RelativeColumn(3f);
                    });
                    table.Header(header =>
                    {
                        Header(header.Cell(), "Record type"); Header(header.Cell(), "Status"); Header(header.Cell(), "Details");
                    });
                    foreach (var item in report.Coverage.Items)
                    {
                        Body(table.Cell(), item.Label); Body(table.Cell(), item.Status); Body(table.Cell(), item.Detail);
                    }
                });
                column.Item().Text("How to read this report").FontSize(13).SemiBold();
                foreach (var note in report.Notes)
                    column.Item().Text($"• {note}").FontSize(8).FontColor(Colors.Grey.Darken2);
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span("Invoice AI · Private financial records · Page ");
                text.CurrentPageNumber(); text.Span(" of "); text.TotalPages();
            });
        })).GeneratePdf();

    private static void Metric(TableDescriptor table, string label, decimal amount, string currency)
    {
        table.Cell().Element(Cell).Text(label);
        table.Cell().Element(Cell).AlignRight().Text(Money(amount, currency)).SemiBold();
    }

    private static void Header(IContainer cell, string value)
        => cell.Background("#F5F2FB").Padding(6).DefaultTextStyle(style => style.SemiBold().FontColor("#51427E")).Text(value);

    private static void Body(IContainer cell, string value)
        => cell.BorderBottom(1).BorderColor("#EEEAF5").Padding(6).Text(value);

    private static IContainer Cell(IContainer cell) => cell.BorderBottom(1).BorderColor("#EEEAF5").Padding(7);
    private static string Money(decimal amount, string currency) => $"{currency} {amount:N2}";
}
