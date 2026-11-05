using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Organizations;
using Domain.Invoices;

namespace Application.Invoices;

public sealed record PdfParty(
    string Name, IReadOnlyList<string> AddressLines, string? Email, string? Phone, string? TaxId);

public sealed record PdfLine(
    int Position, string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRatePercent, decimal LineTotal);

public sealed record InvoicePdfModel(
    PdfParty Seller, PdfParty Buyer, string? Number, InvoiceStatus Status, string CurrencyCode,
    DateOnly? IssueDate, DateOnly? DueDate, string? Notes, IReadOnlyList<PdfLine> Lines,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total,
    decimal AmountPaid, decimal Balance, bool IsOverdue, string? VoidReason);

public sealed record InvoicePdfFile(byte[] Content, string FileName);

public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfModel model);
}

internal static class PdfAddress
{
    public static IReadOnlyList<string> Lines(
        string? line1, string? line2, string? city, string? region, string? postalCode, string? countryCode)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(line1)) lines.Add(line1);
        if (!string.IsNullOrWhiteSpace(line2)) lines.Add(line2);

        var locality = string.Join(", ", new[] { city, region }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var cityLine = string.Join(" ", new[] { locality, postalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (cityLine.Length > 0) lines.Add(cityLine);

        if (!string.IsNullOrWhiteSpace(countryCode)) lines.Add(countryCode);
        return lines;
    }
}

public sealed class GetInvoicePdfHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IInvoicePdfRenderer renderer, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<InvoicePdfFile> Handle(Guid id, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var invoice = await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");
        var customer = await customers.GetByIdAsync(invoice.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");

        var today = InvoiceLookups.Today(organization, clock);

        var seller = new PdfParty(
            organization.Name,
            PdfAddress.Lines(organization.AddressLine1, organization.AddressLine2, organization.City,
                organization.Region, organization.PostalCode, organization.CountryCode),
            organization.Email, organization.Phone, organization.TaxIdentificationNumber);

        var buyer = new PdfParty(
            customer.Name,
            PdfAddress.Lines(customer.AddressLine1, customer.AddressLine2, customer.City,
                customer.Region, customer.PostalCode, customer.CountryCode),
            customer.Email, customer.Phone, customer.TaxIdentificationNumber);

        var lines = invoice.Lines.OrderBy(l => l.Position)
            .Select(l => new PdfLine(
                l.Position, l.Description, l.Quantity, l.UnitPrice,
                l.DiscountPercent, l.TaxRatePercent, l.LineTotal))
            .ToList();

        var model = new InvoicePdfModel(
            seller, buyer, invoice.Number, invoice.Status, invoice.CurrencyCode,
            invoice.IssueDate, invoice.DueDate, invoice.Notes, lines,
            invoice.Subtotal, invoice.DiscountTotal, invoice.TaxTotal, invoice.Total,
            invoice.AmountPaid, invoice.Balance, invoice.IsOverdue(today), invoice.VoidReason);

        var fileName = invoice.Number is not null
            ? $"{invoice.Number}.pdf"
            : $"invoice-draft-{invoice.Id.ToString("N")[..8]}.pdf";

        return new InvoicePdfFile(renderer.Render(model), fileName);
    }
}