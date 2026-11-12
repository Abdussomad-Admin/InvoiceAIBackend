namespace Host;

public sealed class BillingSettings
{
    public List<BillingPlanSettings> Plans { get; set; } = [];
}

public sealed class BillingPlanSettings
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string PaystackPlanCode { get; set; } = "";
    public string CurrencyCode { get; set; } = "NGN";
    public long AmountMinor { get; set; }
    public bool IsFree { get; set; }
    public string Interval { get; set; } = "monthly";
    public int? InvoiceLimit { get; set; }
    public int? MemberLimit { get; set; }
    public int? AiExtractionLimit { get; set; }
    public long? StorageLimitBytes { get; set; }
}

public sealed class PaystackSettings
{
    public bool Enabled { get; set; }
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
    public decimal PlatformPercentageCharge { get; set; }
}
