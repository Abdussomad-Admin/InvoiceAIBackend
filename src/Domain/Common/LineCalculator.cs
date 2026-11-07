namespace Domain.Common;

internal sealed record LineAmounts(
    string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRatePercent,
    decimal Subtotal, decimal Discount, decimal Tax, decimal Total);

// The one place where a line's money is calculated. Invoices and supplier bills both use it.
internal static class LineCalculator
{
    public static LineAmounts Calculate(
        string? description, decimal quantity, decimal unitPrice, decimal discountPercent, decimal taxRatePercent)
    {
        var text = Guard.NotBlank(description, "Description", 500);

        if (quantity <= 0 || quantity > 100_000m)
            throw new DomainException("Quantity must be greater than 0 and at most 100,000.");
        if (decimal.Round(quantity, 4) != quantity)
            throw new DomainException("Quantity can have at most 4 decimal places.");

        if (unitPrice < 0 || unitPrice > 100_000_000m)
            throw new DomainException("Unit price must be between 0 and 100,000,000.");
        if (decimal.Round(unitPrice, 4) != unitPrice)
            throw new DomainException("Unit price can have at most 4 decimal places.");

        if (discountPercent is < 0 or > 100)
            throw new DomainException("Discount must be between 0 and 100 percent.");
        if (taxRatePercent is < 0 or > 100)
            throw new DomainException("Tax rate must be between 0 and 100 percent.");

        var subtotal = MoneyMath.Round(quantity * unitPrice);
        var discount = MoneyMath.Percent(subtotal, discountPercent);
        var net = subtotal - discount;
        var tax = MoneyMath.Percent(net, taxRatePercent);

        return new LineAmounts(
            text, quantity, unitPrice, discountPercent, taxRatePercent, subtotal, discount, tax, net + tax);
    }
}