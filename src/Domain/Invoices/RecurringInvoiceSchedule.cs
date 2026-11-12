using Domain.Common;
using System.Text.Json;

namespace Domain.Invoices;

public enum RecurrenceFrequency { Weekly = 1, Monthly = 2, Quarterly = 3, Yearly = 4 }

public sealed class RecurringInvoiceSchedule : BaseEntity, ITenantOwned
{
    private RecurringInvoiceSchedule() { }
    public Guid OrganizationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Description { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public string BaseCurrencyCode { get; private set; } = null!;
    public decimal ExchangeRateToBase { get; private set; } = 1m;
    public string LinesJson { get; private set; } = null!;
    public RecurrenceFrequency Frequency { get; private set; }
    public DateOnly NextRunOn { get; private set; }
    public int PaymentTermsDays { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastGeneratedAt { get; private set; }

    public static RecurringInvoiceSchedule Create(Guid organizationId, Guid customerId, string description,
        string currencyCode, string baseCurrencyCode, decimal exchangeRateToBase,
        IReadOnlyList<LineInput> lines, RecurrenceFrequency frequency,
        DateOnly firstRunOn, int paymentTermsDays)
    {
        if (organizationId == Guid.Empty || customerId == Guid.Empty) throw new DomainException("Choose a customer.");
        if (lines.Count is < 1 or > Invoice.MaxLines) throw new DomainException("Add between 1 and 200 invoice lines.");
        if (firstRunOn < DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainException("The first run date cannot be in the past.");
        if (!Enum.IsDefined(frequency)) throw new DomainException("Choose a valid recurrence frequency.");
        if (paymentTermsDays is < 0 or > 365) throw new DomainException("Payment terms must be between 0 and 365 days.");
        var currency = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode));
        var baseCurrency = Guard.IsoCode(baseCurrencyCode, 3, nameof(BaseCurrencyCode));
        if (currency == baseCurrency) exchangeRateToBase = 1m;
        else if (exchangeRateToBase <= 0m || exchangeRateToBase > 1_000_000m) throw new DomainException("Enter a valid exchange rate to workspace currency.");
        return new RecurringInvoiceSchedule { OrganizationId = organizationId, CustomerId = customerId,
            Description = Guard.NotBlank(description, nameof(Description), 200), CurrencyCode = currency,
            BaseCurrencyCode = baseCurrency, ExchangeRateToBase = decimal.Round(exchangeRateToBase, 8),
            LinesJson = JsonSerializer.Serialize(lines), Frequency = frequency, NextRunOn = firstRunOn,
            PaymentTermsDays = paymentTermsDays, IsActive = true };
    }

    public IReadOnlyList<LineInput> Lines() => JsonSerializer.Deserialize<List<LineInput>>(LinesJson) ?? [];
    public void Pause() => IsActive = false;
    public void Resume() => IsActive = true;
    public DateOnly Advance()
    {
        var generatedOn = NextRunOn;
        NextRunOn = Frequency switch
        {
            RecurrenceFrequency.Weekly => NextRunOn.AddDays(7),
            RecurrenceFrequency.Monthly => NextRunOn.AddMonths(1),
            RecurrenceFrequency.Quarterly => NextRunOn.AddMonths(3),
            RecurrenceFrequency.Yearly => NextRunOn.AddYears(1),
            _ => throw new DomainException("Invalid recurrence frequency.")
        };
        LastGeneratedAt = DateTimeOffset.UtcNow;
        return generatedOn;
    }
}
