using Domain.Common;

namespace Domain.Billing;

public enum InvoicePaymentCheckoutStatus { Pending = 0, Succeeded = 1, Failed = 2 }

public sealed class InvoicePaymentCheckout : BaseEntity, ITenantOwned
{
    private InvoicePaymentCheckout() { }

    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string Reference { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public long AmountMinor { get; private set; }
    public InvoicePaymentCheckoutStatus Status { get; private set; }
    public string? AuthorizationUrl { get; private set; }

    public static InvoicePaymentCheckout Create(Guid organizationId, Guid invoiceId, string reference,
        string currencyCode, long amountMinor)
    {
        if (organizationId == Guid.Empty || invoiceId == Guid.Empty || amountMinor <= 0)
            throw new DomainException("A valid invoice and payment amount are required.");
        return new InvoicePaymentCheckout
        {
            OrganizationId = organizationId,
            InvoiceId = invoiceId,
            Reference = Guard.NotBlank(reference, nameof(reference), 100),
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(currencyCode)),
            AmountMinor = amountMinor,
            Status = InvoicePaymentCheckoutStatus.Pending
        };
    }

    public void SetAuthorizationUrl(string url)
        => AuthorizationUrl = Guard.NotBlank(url, nameof(url), 1000);

    public void MarkSucceeded() => Status = InvoicePaymentCheckoutStatus.Succeeded;
    public void MarkFailed() => Status = InvoicePaymentCheckoutStatus.Failed;
}
