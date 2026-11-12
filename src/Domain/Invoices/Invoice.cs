using Domain.Common;

using System.Security.Cryptography;

namespace Domain.Invoices;

public sealed class Invoice : BaseEntity, ITenantOwned
{
    public const int MaxLines = 200;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice() { }

    public Guid OrganizationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string? Number { get; private set; }          // assigned when issued
    public string CurrencyCode { get; private set; } = null!;
    public string BaseCurrencyCode { get; private set; } = null!;
    public decimal ExchangeRateToBase { get; private set; } = 1m;
    public InvoiceStatus Status { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public int PaymentTermsDays { get; private set; }
    public string? Notes { get; private set; }

    public decimal Subtotal { get; private set; }        // before discounts
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }
    public decimal AmountCredited { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public DateTimeOffset? LastSentAt { get; private set; }
    public string? LastSentTo { get; private set; }
    public int SentCount { get; private set; }
    public string? PublicPaymentToken { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public decimal Balance => Math.Max(0m, Total - AmountPaid - AmountCredited);

    public string EnsurePublicPaymentToken()
        => PublicPaymentToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static Invoice CreateDraft(
        Guid organizationId, Guid customerId, string currencyCode, int paymentTermsDays, string? notes,
        string? baseCurrencyCode = null, decimal exchangeRateToBase = 1m)
    {
        if (organizationId == Guid.Empty) throw new DomainException("OrganizationId is required.");
        if (customerId == Guid.Empty) throw new DomainException("A customer is required.");

        var currency = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode));
        var baseCurrency = Guard.IsoCode(baseCurrencyCode ?? currency, 3, nameof(BaseCurrencyCode));
        if (currency == baseCurrency) exchangeRateToBase = 1m;
        else if (exchangeRateToBase <= 0m || exchangeRateToBase > 1_000_000m)
            throw new DomainException("Enter a valid exchange rate from invoice currency to workspace currency.");
        return new Invoice
        {
            OrganizationId = organizationId,
            CustomerId = customerId,
            CurrencyCode = currency,
            BaseCurrencyCode = baseCurrency,
            ExchangeRateToBase = decimal.Round(exchangeRateToBase, 8),
            PaymentTermsDays = ValidTerms(paymentTermsDays),
            Notes = Guard.OptionalText(notes, nameof(Notes), 2000),
            Status = InvoiceStatus.Draft
        };
    }

    public void UpdateDetails(Guid customerId, int paymentTermsDays, string? notes, string? currencyCode = null,
        decimal? exchangeRateToBase = null)
    {
        EnsureDraft();
        if (customerId == Guid.Empty) throw new DomainException("A customer is required.");

        CustomerId = customerId;
        PaymentTermsDays = ValidTerms(paymentTermsDays);
        Notes = Guard.OptionalText(notes, nameof(Notes), 2000);
        if (currencyCode is not null)
        {
            var currency = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode));
            var rate = currency == BaseCurrencyCode ? 1m : exchangeRateToBase ?? 0m;
            if (rate <= 0m || rate > 1_000_000m) throw new DomainException("Enter a valid exchange rate from invoice currency to workspace currency.");
            CurrencyCode = currency;
            ExchangeRateToBase = decimal.Round(rate, 8);
        }
    }

    // Replaces ALL lines and recalculates every total.
    public void ReplaceLines(IEnumerable<LineInput> inputs)
    {
        EnsureDraft();

        var list = inputs.ToList();
        if (list.Count > MaxLines)
            throw new DomainException($"An invoice can have at most {MaxLines} lines.");

        var lines = list.Select((input, index) => InvoiceLine.Create(OrganizationId, Id, index + 1, input)).ToList();

        _lines.Clear();
        _lines.AddRange(lines);
        Recalculate();
    }

    public void Issue(string number, DateOnly issueDate, DateOnly? dueDate = null)
    {
        EnsureDraft();

        if (_lines.Count == 0)
            throw new DomainException("Add at least one line before issuing the invoice.");
        if (Total <= 0)
            throw new DomainException("An invoice must have a total greater than zero.");

        var due = dueDate ?? issueDate.AddDays(PaymentTermsDays);
        if (due < issueDate)
            throw new DomainException("The due date cannot be before the issue date.");

        Number = Guard.NotBlank(number, nameof(Number), 50);
        EnsurePublicPaymentToken();
        IssueDate = issueDate;
        DueDate = due;
        Status = InvoiceStatus.Issued;
    }

    public void ApplyPayment(decimal amount)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid))
            throw new DomainException("Payments can only be recorded against issued invoices with a balance.");

        ValidateMoney(amount);
        if (amount > Balance)
            throw new DomainException("The payment is more than the outstanding balance.");

        AmountPaid += amount;
        RefreshSettlementStatus();
    }

    public void ReversePayment(decimal amount)
    {
        if (Status is not (InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid))
            throw new DomainException("There is no payment to reverse on this invoice.");

        ValidateMoney(amount);
        if (amount > AmountPaid)
            throw new DomainException("The reversal is more than the amount paid.");

        AmountPaid -= amount;
        RefreshSettlementStatus();
    }

    public void ApplyCredit(decimal amount)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid))
            throw new DomainException("Credits can only be applied to an issued invoice.");
        ValidateMoney(amount);
        if (amount > Total - AmountCredited)
            throw new DomainException("The credit exceeds the invoice amount that has not already been credited.");
        AmountCredited += amount;
        RefreshSettlementStatus();
    }

    private void RefreshSettlementStatus()
    {
        var remaining = Total - AmountPaid - AmountCredited;
        Status = remaining <= 0m
            ? (AmountCredited > 0m && AmountPaid == 0m ? InvoiceStatus.Credited : InvoiceStatus.Paid)
            : (AmountPaid > 0m || AmountCredited > 0m ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued);
    }

    public void Void(string reason, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Issued || AmountPaid != 0m)
            throw new DomainException(
                "Only issued invoices with no payments can be voided. Reverse any payments first.");

        VoidReason = Guard.NotBlank(reason, nameof(VoidReason), 500);
        VoidedAt = now;
        Status = InvoiceStatus.Void;
    }

    public void EnsureSendable()
    {
        if (Status is InvoiceStatus.Draft or InvoiceStatus.Void)
            throw new DomainException("Only issued invoices can be sent. Issue the invoice first.");
    }

    public void MarkSent(string recipient, DateTimeOffset now)
    {
        EnsureSendable();
        EnsurePublicPaymentToken();
        LastSentTo = Guard.NotBlank(recipient, nameof(LastSentTo), 254);
        LastSentAt = now;
        SentCount++;
    }

    // Overdue is calculated from the due date, never stored, so it can't go stale.
    public bool IsOverdue(DateOnly today)
        => Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid
           && DueDate is { } due && due < today;

    public int DaysOverdue(DateOnly today)
        => IsOverdue(today) ? today.DayNumber - DueDate!.Value.DayNumber : 0;

    private void Recalculate()
    {
        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.DiscountAmount);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        Total = _lines.Sum(l => l.LineTotal);
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException("Only draft invoices can be changed.");
    }

    private static int ValidTerms(int days)
    {
        if (days is < 0 or > 365)
            throw new DomainException("Payment terms must be between 0 and 365 days.");
        return days;
    }

    private static void ValidateMoney(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("The amount must be greater than zero.");
        if (MoneyMath.Round(amount) != amount)
            throw new DomainException("The amount can have at most 2 decimal places.");
    }
}
