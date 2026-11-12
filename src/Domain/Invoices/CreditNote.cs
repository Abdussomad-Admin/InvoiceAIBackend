using Domain.Common;

namespace Domain.Invoices;

public sealed class CreditNote : BaseEntity, ITenantOwned
{
    private CreditNote() { }
    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string Number { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public string BaseCurrencyCode { get; private set; } = null!;
    public decimal ExchangeRateToBase { get; private set; } = 1m;
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateOnly IssuedOn { get; private set; }

    public static CreditNote Create(Guid organizationId, Guid invoiceId, string number,
        string currencyCode, string baseCurrencyCode, decimal exchangeRateToBase,
        decimal amount, string reason, DateOnly issuedOn)
    {
        if (organizationId == Guid.Empty || invoiceId == Guid.Empty) throw new DomainException("Invoice is required.");
        if (amount <= 0 || MoneyMath.Round(amount) != amount) throw new DomainException("Credit amount must be greater than zero and use at most 2 decimals.");
        var note = new CreditNote { OrganizationId = organizationId, InvoiceId = invoiceId,
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode)),
            BaseCurrencyCode = Guard.IsoCode(baseCurrencyCode, 3, nameof(BaseCurrencyCode)),
            ExchangeRateToBase = decimal.Round(exchangeRateToBase, 8), Amount = amount,
            Reason = Guard.NotBlank(reason, nameof(Reason), 500), IssuedOn = issuedOn };
        note.Number = Guard.NotBlank(number, nameof(Number), 50);
        return note;
    }
}
