namespace Domain.Common;

public static class MoneyMath
{
    public const int Decimals = 2;

    // Everyday rounding: 0.125 becomes 0.13 (never "banker's rounding").
    public static decimal Round(decimal value)
        => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    public static decimal Percent(decimal amount, decimal percent)
        => Round(amount * percent / 100m);
}