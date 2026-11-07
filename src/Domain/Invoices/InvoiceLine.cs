using Domain.Common;

namespace Domain.Invoices;

// What the caller supplies for one line. Everything else is calculated.
public sealed record LineInput(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent = 0m, decimal TaxRatePercent = 0m);

public sealed class InvoiceLine : BaseEntity, ITenantOwned
{
    private InvoiceLine() { }

    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public int Position { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRatePercent { get; private set; }

    // Calculated when the line is created.
    public decimal LineSubtotal { get; private set; }   // quantity x unit price, before discount
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }      // subtotal - discount + tax

    public decimal NetAmount => LineSubtotal - DiscountAmount;

    internal static InvoiceLine Create(Guid organizationId, Guid invoiceId, int position, LineInput input)
    {
        var a = LineCalculator.Calculate(
            input.Description, input.Quantity, input.UnitPrice, input.DiscountPercent, input.TaxRatePercent);

        return new InvoiceLine
        {
            OrganizationId = organizationId,
            InvoiceId = invoiceId,
            Position = position,
            Description = a.Description,
            Quantity = a.Quantity,
            UnitPrice = a.UnitPrice,
            DiscountPercent = a.DiscountPercent,
            TaxRatePercent = a.TaxRatePercent,
            LineSubtotal = a.Subtotal,
            DiscountAmount = a.Discount,
            TaxAmount = a.Tax,
            LineTotal = a.Total
        };
    }
}