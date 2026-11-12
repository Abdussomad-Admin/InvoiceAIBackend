using System.Globalization;
using Application.Invoices;
using Domain.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Pdf;

public static class PdfFormat
{
    public static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    public static string Quantity(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Percent(decimal percent)
        => percent == 0m ? "-" : percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string Date(DateOnly? date)
        => date is { } value ? value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "-";
}

public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    static QuestPdfInvoiceRenderer()
    {
        // QuestPDF Community License: free for individuals and organisations under USD 1M annual revenue.
        // See the README before this product grows past that limit.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(InvoicePdfModel model)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(42);
                page.MarginVertical(34);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));

                page.Header().Element(c => ComposeHeader(c, model));
                page.Content().PaddingVertical(12).Element(c => ComposeContent(c, model));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                    text.Span("  ·  INVOICE AI");
                });
            });
        }).GeneratePdf();
    }

    private static (string Text, string Color)? Stamp(InvoicePdfModel m) => m.Status switch
    {
        InvoiceStatus.Draft => ("DRAFT", Colors.Grey.Darken1),
        InvoiceStatus.Void => ("VOID", Colors.Red.Darken2),
        InvoiceStatus.Paid => ("PAID", Colors.Green.Darken2),
        InvoiceStatus.PartiallyPaid when m.IsOverdue => ("PARTIALLY PAID - OVERDUE", Colors.Red.Darken2),
        InvoiceStatus.PartiallyPaid => ("PARTIALLY PAID", Colors.Orange.Darken2),
        _ when m.IsOverdue => ("OVERDUE", Colors.Red.Darken2),
        _ => null
    };

    private static void ComposeHeader(IContainer container, InvoicePdfModel m)
    {
        container.BorderTop(5).BorderColor(Colors.Purple.Darken2).PaddingTop(16).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("INVOICE AI").FontSize(9).Bold().FontColor(Colors.Purple.Darken2);
                column.Item().PaddingTop(8).Text(m.Seller.Name).FontSize(17).Bold().FontColor(Colors.Grey.Darken4);
                foreach (var line in m.Seller.AddressLines) column.Item().Text(line).FontColor(Colors.Grey.Darken2);
                if (!string.IsNullOrWhiteSpace(m.Seller.Email)) column.Item().Text(m.Seller.Email).FontColor(Colors.Grey.Darken2);
                if (!string.IsNullOrWhiteSpace(m.Seller.Phone)) column.Item().Text(m.Seller.Phone).FontColor(Colors.Grey.Darken2);
                if (!string.IsNullOrWhiteSpace(m.Seller.TaxId)) column.Item().Text($"Tax ID: {m.Seller.TaxId}").FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(205).AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text("INVOICE").FontSize(24).Bold().FontColor(Colors.Purple.Darken2);
                column.Item().PaddingTop(3).AlignRight().Text(m.Number ?? "Draft invoice").SemiBold().FontColor(Colors.Grey.Darken3);

                if (Stamp(m) is { } stamp)
                    column.Item().PaddingTop(8).AlignRight().Text(stamp.Text).FontSize(8).Bold().FontColor(stamp.Color);
            });
        });
    }

    private static void ComposeContent(IContainer container, InvoicePdfModel m)
    {
        container.Column(column =>
        {
            column.Spacing(18);

            column.Item().Row(row =>
            {
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).Padding(14).Column(buyer =>
                {
                    buyer.Spacing(2);
                    buyer.Item().Text("BILL TO").FontSize(8).Bold().FontColor(Colors.Purple.Darken2);
                    buyer.Item().PaddingTop(4).Text(m.Buyer.Name).FontSize(11).SemiBold().FontColor(Colors.Grey.Darken4);
                    foreach (var line in m.Buyer.AddressLines) buyer.Item().Text(line).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(m.Buyer.Email)) buyer.Item().Text(m.Buyer.Email).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(m.Buyer.TaxId)) buyer.Item().Text($"Tax ID: {m.Buyer.TaxId}").FontColor(Colors.Grey.Darken2);
                });

                row.ConstantItem(205).PaddingLeft(18).AlignMiddle().Column(details =>
                {
                    details.Spacing(9);
                    Detail(details, "Issue date", PdfFormat.Date(m.IssueDate));
                    Detail(details, "Due date", PdfFormat.Date(m.DueDate));
                    Detail(details, "Currency", m.CurrencyCode);
                });
            });

            column.Item().Element(c => ComposeTable(c, m));
            column.Item().AlignRight().Element(c => ComposeTotals(c, m));

            if (m.Status == InvoiceStatus.Void && !string.IsNullOrWhiteSpace(m.VoidReason))
                column.Item().Text($"Void reason: {m.VoidReason}").FontColor(Colors.Red.Darken2);

            if (m.Status == InvoiceStatus.Draft)
                column.Item().Text("This is a draft. It has not been issued and has no invoice number.")
                    .FontColor(Colors.Grey.Darken1);

            if (!string.IsNullOrWhiteSpace(m.Notes))
            {
                column.Item().Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold();
                    notes.Item().Text(m.Notes);
                });
            }
        });
    }

    private static void Detail(ColumnDescriptor column, string label, string value)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontColor(Colors.Grey.Darken2);
            row.ConstantItem(105).AlignRight().Text(value).SemiBold();
        });
    }

    private static IContainer HeaderCell(IContainer container)
        => container.Background(Colors.Purple.Darken2).PaddingVertical(9).PaddingHorizontal(7)
            .DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(Colors.White));

    private static IContainer BodyCell(IContainer container)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(9).PaddingHorizontal(7);

    private static void ComposeTable(IContainer container, InvoicePdfModel m)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(27);
                columns.RelativeColumn(4.5f);
                columns.RelativeColumn(0.9f);
                columns.RelativeColumn(1.7f);
                columns.RelativeColumn(0.9f);
                columns.RelativeColumn(0.8f);
                columns.RelativeColumn(1.8f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("#");
                header.Cell().Element(HeaderCell).Text("Description");
                header.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                header.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                header.Cell().Element(HeaderCell).AlignRight().Text("Disc.");
                header.Cell().Element(HeaderCell).AlignRight().Text("Tax");
                header.Cell().Element(HeaderCell).AlignRight().Text("Total");
            });

            foreach (var line in m.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.Position.ToString(CultureInfo.InvariantCulture)).FontColor(Colors.Grey.Darken2);
                table.Cell().Element(BodyCell).Text(line.Description).FontColor(Colors.Grey.Darken4);
                table.Cell().Element(BodyCell).AlignRight().Text(PdfFormat.Quantity(line.Quantity));
                table.Cell().Element(BodyCell).AlignRight().Text(PdfFormat.Money(line.UnitPrice));
                table.Cell().Element(BodyCell).AlignRight().Text(PdfFormat.Percent(line.DiscountPercent));
                table.Cell().Element(BodyCell).AlignRight().Text(PdfFormat.Percent(line.TaxRatePercent));
                table.Cell().Element(BodyCell).AlignRight().Text(PdfFormat.Money(line.LineTotal));
            }
        });
    }

    private static void ComposeTotals(IContainer container, InvoicePdfModel m)
    {
        var showPayments = m.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid;

        container.Width(270).Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).Padding(14).Column(column =>
        {
            column.Spacing(2);
            TotalRow(column, "Subtotal", m.Subtotal, m.CurrencyCode);
            if (m.DiscountTotal > 0) TotalRow(column, "Discount", m.DiscountTotal, m.CurrencyCode, negative: true);
            TotalRow(column, "Tax", m.TaxTotal, m.CurrencyCode);
            column.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Grey.Lighten2);
            TotalRow(column, "Total", m.Total, m.CurrencyCode, bold: true);

            if (showPayments)
            {
                if (m.AmountPaid > 0) TotalRow(column, "Amount paid", m.AmountPaid, m.CurrencyCode, negative: true);
                column.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Purple.Lighten2);
                column.Item().PaddingTop(5).Background(Colors.Purple.Lighten5).Padding(8)
                    .Column(balance => TotalRow(balance, "Balance due", m.Balance, m.CurrencyCode, bold: true));
            }
        });
    }

    private static void TotalRow(
        ColumnDescriptor column, string label, decimal amount, string currency,
        bool bold = false, bool negative = false)
    {
        column.Item().PaddingVertical(4).Row(row =>
        {
            var left = row.RelativeItem().Text(label).FontColor(bold ? Colors.Purple.Darken3 : Colors.Grey.Darken2);
            var right = row.ConstantItem(120).AlignRight()
                .Text($"{(negative ? "-" : "")}{currency} {PdfFormat.Money(amount)}").FontColor(Colors.Grey.Darken4);

            if (bold)
            {
                left.SemiBold();
                right.SemiBold();
            }
        });
    }
}
