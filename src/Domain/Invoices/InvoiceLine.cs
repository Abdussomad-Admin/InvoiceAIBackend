using Domain.Common;

namespace Domain.Invoices;

public sealed record LineInput(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent = 0m, decimal TaxRatePercent = 0m);

public sealed class InvoiceLine : BaseEntity, ITenantOwned
{
    private InvoiceLine() { }

    public Guid InvoiceId { get; private set; }
    public int Position { get; private set; }
    public Guid OrganizationId { get; private set; }
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

    internal static InvoiceLine Create(Guid organizationId, Guid invoiceId, int position, LineInput input)
    {
        var description = Guard.NotBlank(input.Description, nameof(Description), 500);

        if (input.Quantity <= 0 || input.Quantity > 100_000m)
            throw new DomainException("Quantity must be greater than 0 and at most 100,000.");
        if (decimal.Round(input.Quantity, 4) != input.Quantity)
            throw new DomainException("Quantity can have at most 4 decimal places.");

        if (input.UnitPrice < 0 || input.UnitPrice > 100_000_000m)
            throw new DomainException("Unit price must be between 0 and 100,000,000.");
        if (decimal.Round(input.UnitPrice, 4) != input.UnitPrice)
            throw new DomainException("Unit price can have at most 4 decimal places.");

        if (input.DiscountPercent is < 0 or > 100)
            throw new DomainException("Discount must be between 0 and 100 percent.");
        if (input.TaxRatePercent is < 0 or > 100)
            throw new DomainException("Tax rate must be between 0 and 100 percent.");

        var subtotal = MoneyMath.Round(input.Quantity * input.UnitPrice);
        var discount = MoneyMath.Percent(subtotal, input.DiscountPercent);
        var net = subtotal - discount;
        var tax = MoneyMath.Percent(net, input.TaxRatePercent);

        return new InvoiceLine
        {
            OrganizationId = organizationId,
            InvoiceId = invoiceId,
            Position = position,
            Description = description,
            Quantity = input.Quantity,
            UnitPrice = input.UnitPrice,
            DiscountPercent = input.DiscountPercent,
            TaxRatePercent = input.TaxRatePercent,
            LineSubtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            LineTotal = net + tax,
        };
    }
}