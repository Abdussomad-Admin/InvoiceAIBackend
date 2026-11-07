using Domain.Common;
using Domain.Invoices;

namespace Domain.Bills;

public sealed class BillLine : BaseEntity, ITenantOwned
{
    private BillLine() { }

    public Guid OrganizationId { get; private set; }
    public Guid BillId { get; private set; }
    public int Position { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    public decimal NetAmount => LineSubtotal - DiscountAmount;

    // Reuses LineInput from invoices: a line is a line, whichever document it sits on.
    internal static BillLine Create(Guid organizationId, Guid billId, int position, LineInput input)
    {
        var a = LineCalculator.Calculate(
            input.Description, input.Quantity, input.UnitPrice, input.DiscountPercent, input.TaxRatePercent);

        return new BillLine
        {
            OrganizationId = organizationId,
            BillId = billId,
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