using System.Globalization;
using Application.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Pdf;

public sealed class QuestPdfCreditNoteRenderer : ICreditNotePdfRenderer
{
    public byte[] Render(CreditNotePdfModel model)
        => Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(model.Seller.Name).FontSize(17).SemiBold();
                    foreach (var line in model.Seller.AddressLines) column.Item().Text(line);
                    if (!string.IsNullOrWhiteSpace(model.Seller.Email)) column.Item().Text(model.Seller.Email);
                    if (!string.IsNullOrWhiteSpace(model.Seller.Phone)) column.Item().Text(model.Seller.Phone);
                    if (!string.IsNullOrWhiteSpace(model.Seller.TaxId)) column.Item().Text($"Tax ID: {model.Seller.TaxId}");
                });
                row.ConstantItem(220).AlignRight().Column(column =>
                {
                    column.Item().AlignRight().Text("CREDIT NOTE").FontSize(21).SemiBold().FontColor(Colors.Blue.Darken3);
                    column.Item().AlignRight().Text(model.CreditNoteNumber).SemiBold();
                    column.Item().AlignRight().Text($"Issued {model.IssuedOn.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}");
                });
            });
            page.Content().PaddingVertical(30).Column(column =>
            {
                column.Spacing(18);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(buyer =>
                    {
                        buyer.Item().Text("Issued to").FontColor(Colors.Grey.Darken1);
                        buyer.Item().Text(model.Buyer.Name).SemiBold();
                        foreach (var line in model.Buyer.AddressLines) buyer.Item().Text(line);
                        if (!string.IsNullOrWhiteSpace(model.Buyer.Email)) buyer.Item().Text(model.Buyer.Email);
                        if (!string.IsNullOrWhiteSpace(model.Buyer.TaxId)) buyer.Item().Text($"Tax ID: {model.Buyer.TaxId}");
                    });
                    row.ConstantItem(220).Column(details =>
                    {
                        details.Item().Text("Original invoice").FontColor(Colors.Grey.Darken1);
                        details.Item().Text(model.InvoiceNumber).SemiBold();
                        details.Item().PaddingTop(8).Text("Currency").FontColor(Colors.Grey.Darken1);
                        details.Item().Text(model.CurrencyCode).SemiBold();
                    });
                });
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(16).Column(reason =>
                {
                    reason.Item().Text("Reason for credit").FontColor(Colors.Grey.Darken1);
                    reason.Item().Text(model.Reason);
                });
                column.Item().AlignRight().Width(270).BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(12).Row(row =>
                {
                    row.RelativeItem().Text("Credit amount").SemiBold();
                    row.ConstantItem(150).AlignRight().Text($"{model.CurrencyCode} {model.Amount.ToString("N2", CultureInfo.InvariantCulture)}").FontSize(14).SemiBold();
                });
                column.Item().PaddingTop(12).Text("This credit note reduces the amount due on the original invoice. Any refund is handled separately.")
                    .FontColor(Colors.Grey.Darken1);
            });
            page.Footer().AlignCenter().Text("Invoice AI · Credit note").FontSize(8).FontColor(Colors.Grey.Darken1);
        })).GeneratePdf();
}
