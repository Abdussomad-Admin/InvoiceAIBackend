using Application.Customers;
using Application.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Pdf;

public sealed class QuestPdfCustomerStatementRenderer : ICustomerStatementPdfRenderer
{
    static QuestPdfCustomerStatementRenderer()
        => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(CustomerStatementPdfModel model)
        => Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(9));
                page.Header().Element(c => ComposeHeader(c, model));
                page.Content().PaddingVertical(14).Element(c => ComposeContent(c, model));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();

    private static void ComposeHeader(IContainer container, CustomerStatementPdfModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(model.Seller.Name).FontSize(15).SemiBold();
                foreach (var line in model.Seller.AddressLines) column.Item().Text(line);
                if (!string.IsNullOrWhiteSpace(model.Seller.Email)) column.Item().Text(model.Seller.Email);
                if (!string.IsNullOrWhiteSpace(model.Seller.Phone)) column.Item().Text(model.Seller.Phone);
            });
            row.ConstantItem(250).Column(column =>
            {
                column.Item().AlignRight().Text("STATEMENT OF ACCOUNT").FontSize(18).SemiBold();
                column.Item().AlignRight().Text($"{PdfFormat.Date(model.From)} – {PdfFormat.Date(model.To)}");
                column.Item().AlignRight().Text($"Currency: {model.CurrencyCode}");
            });
        });
    }

    private static void ComposeContent(IContainer container, CustomerStatementPdfModel model)
    {
        container.Column(column =>
        {
            column.Spacing(12);
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(customer =>
                {
                    customer.Item().Text("Statement for").FontColor(Colors.Grey.Darken1);
                    customer.Item().Text(model.Buyer.Name).SemiBold();
                    foreach (var line in model.Buyer.AddressLines) customer.Item().Text(line);
                    if (!string.IsNullOrWhiteSpace(model.Buyer.Email)) customer.Item().Text(model.Buyer.Email);
                });
                row.ConstantItem(230).Column(totals =>
                {
                    TotalRow(totals, "Opening balance", model.OpeningBalance, model.CurrencyCode);
                    TotalRow(totals, "Closing balance", model.ClosingBalance, model.CurrencyCode, bold: true);
                });
            });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(75);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(3.5f);
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(1.5f);
                });
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Date");
                    header.Cell().Element(HeaderCell).Text("Reference");
                    header.Cell().Element(HeaderCell).Text("Description");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Debit");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Credit");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Balance");
                });

                table.Cell().Element(BodyCell).Text(PdfFormat.Date(model.From));
                table.Cell().Element(BodyCell).Text("—");
                table.Cell().Element(BodyCell).Text("Opening balance");
                table.Cell().Element(BodyCell).AlignRight().Text("—");
                table.Cell().Element(BodyCell).AlignRight().Text("—");
                table.Cell().Element(BodyCell).AlignRight().Text(Money(model.OpeningBalance, model.CurrencyCode));

                foreach (var entry in model.Entries)
                {
                    table.Cell().Element(BodyCell).Text(PdfFormat.Date(entry.Date));
                    table.Cell().Element(BodyCell).Text(entry.Reference);
                    table.Cell().Element(BodyCell).Text(entry.Description);
                    table.Cell().Element(BodyCell).AlignRight().Text(entry.Debit == 0m ? "—" : Money(entry.Debit, model.CurrencyCode));
                    table.Cell().Element(BodyCell).AlignRight().Text(entry.Credit == 0m ? "—" : Money(entry.Credit, model.CurrencyCode));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(entry.Balance, model.CurrencyCode));
                }
            });

            column.Item().AlignRight().Width(250).PaddingTop(6).Row(row =>
            {
                var label = row.RelativeItem().Text("Closing balance");
                var amount = row.ConstantItem(130).AlignRight().Text(Money(model.ClosingBalance, model.CurrencyCode));
                label.SemiBold();
                amount.SemiBold();
            });
            if (model.Entries.Count == 0)
                column.Item().Text("No invoice or payment activity during this statement period.")
                    .FontColor(Colors.Grey.Darken1);
        });
    }

    private static IContainer HeaderCell(IContainer container)
        => container.Background(Colors.Grey.Lighten3).Padding(5).DefaultTextStyle(x => x.SemiBold());

    private static IContainer BodyCell(IContainer container)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);

    private static void TotalRow(ColumnDescriptor column, string label, decimal amount, string currency, bool bold = false)
    {
        column.Item().PaddingVertical(3).Row(row =>
        {
            var left = row.RelativeItem().Text(label);
            var right = row.ConstantItem(130).AlignRight().Text(Money(amount, currency));
            if (bold) { left.SemiBold(); right.SemiBold(); }
        });
    }

    private static string Money(decimal amount, string currency)
        => $"{currency} {PdfFormat.Money(amount)}";
}
