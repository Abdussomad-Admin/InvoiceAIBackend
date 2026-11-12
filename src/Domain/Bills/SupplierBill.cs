using Domain.Common;
using Domain.Invoices;

namespace Domain.Bills;

public sealed class SupplierBill : BaseEntity, ITenantOwned
{
    public const int MaxLines = 200;

    private readonly List<BillLine> _lines = [];

    private SupplierBill() { }

    public Guid OrganizationId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string Number { get; private set; } = null!;       // the supplier's invoice number, as printed
    public string NumberKey { get; private set; } = null!;    // normalized, used to detect duplicates
    public string CurrencyCode { get; private set; } = null!;
    public string BaseCurrencyCode { get; private set; } = null!;
    public decimal ExchangeRateToBase { get; private set; } = 1m;
    public BillStatus Status { get; private set; }
    public DateOnly BillDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Notes { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public IReadOnlyList<BillLine> Lines => _lines;
    public decimal Balance => Total - AmountPaid;

    // "inv 001" and "INV001" are the same bill number as far as duplicate detection goes.
    public static string KeyFor(string? number)
    {
        var trimmed = Guard.NotBlank(number, nameof(Number), 100);
        return new string(trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }

    public static SupplierBill CreateDraft(
        Guid organizationId, Guid supplierId, string currencyCode, string number,
        DateOnly billDate, DateOnly dueDate, string? notes,
        string? baseCurrencyCode = null, decimal exchangeRateToBase = 1m)
    {
        if (organizationId == Guid.Empty) throw new DomainException("OrganizationId is required.");
        if (supplierId == Guid.Empty) throw new DomainException("A supplier is required.");
        if (dueDate < billDate) throw new DomainException("The due date cannot be before the bill date.");

        var currency = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode));
        var baseCurrency = Guard.IsoCode(baseCurrencyCode ?? currency, 3, nameof(BaseCurrencyCode));
        if (currency == baseCurrency) exchangeRateToBase = 1m;
        else if (exchangeRateToBase <= 0m || exchangeRateToBase > 1_000_000m)
            throw new DomainException("Enter a valid exchange rate from bill currency to workspace currency.");

        return new SupplierBill
        {
            OrganizationId = organizationId,
            SupplierId = supplierId,
            Number = Guard.NotBlank(number, nameof(Number), 100),
            NumberKey = KeyFor(number),
            CurrencyCode = currency,
            BaseCurrencyCode = baseCurrency,
            ExchangeRateToBase = decimal.Round(exchangeRateToBase, 8),
            BillDate = billDate,
            DueDate = dueDate,
            Notes = Guard.OptionalText(notes, nameof(Notes), 2000),
            Status = BillStatus.Draft
        };
    }

    public void UpdateDetails(Guid supplierId, string number, DateOnly billDate, DateOnly dueDate, string? notes,
        string? currencyCode = null, decimal? exchangeRateToBase = null)
    {
        EnsureDraft();
        if (supplierId == Guid.Empty) throw new DomainException("A supplier is required.");
        if (dueDate < billDate) throw new DomainException("The due date cannot be before the bill date.");

        SupplierId = supplierId;
        Number = Guard.NotBlank(number, nameof(Number), 100);
        NumberKey = KeyFor(number);
        BillDate = billDate;
        DueDate = dueDate;
        Notes = Guard.OptionalText(notes, nameof(Notes), 2000);
        if (currencyCode is not null)
        {
            var currency = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode));
            var rate = currency == BaseCurrencyCode ? 1m : exchangeRateToBase ?? 0m;
            if (rate <= 0m || rate > 1_000_000m)
                throw new DomainException("Enter a valid exchange rate from bill currency to workspace currency.");
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
            throw new DomainException($"A bill can have at most {MaxLines} lines.");

        var lines = list.Select((input, index) => BillLine.Create(OrganizationId, Id, index + 1, input)).ToList();

        _lines.Clear();
        _lines.AddRange(lines);

        Subtotal = _lines.Sum(l => l.LineSubtotal);
        DiscountTotal = _lines.Sum(l => l.DiscountAmount);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        Total = _lines.Sum(l => l.LineTotal);
    }

    public void Finalize(DateTimeOffset now)
    {
        EnsureDraft();

        if (_lines.Count == 0)
            throw new DomainException("Add at least one line before finalizing the bill.");
        if (Total <= 0)
            throw new DomainException("A bill must have a total greater than zero.");

        Status = BillStatus.Open;
        FinalizedAt = now;
    }

    public void ApplyPayment(decimal amount)
    {
        if (Status is not (BillStatus.Open or BillStatus.PartiallyPaid))
            throw new DomainException("Payments can only be recorded against open bills with a balance.");

        ValidateMoney(amount);
        if (amount > Balance)
            throw new DomainException("The payment is more than the outstanding balance.");

        AmountPaid += amount;
        Status = Balance == 0m ? BillStatus.Paid : BillStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        if (Status is not (BillStatus.PartiallyPaid or BillStatus.Paid))
            throw new DomainException("There is no payment to reverse on this bill.");

        ValidateMoney(amount);
        if (amount > AmountPaid)
            throw new DomainException("The reversal is more than the amount paid.");

        AmountPaid -= amount;
        Status = AmountPaid == 0m ? BillStatus.Open : BillStatus.PartiallyPaid;
    }

    // A draft entered by mistake can be voided too, which frees its number for re-entry.
    public void Void(string reason, DateTimeOffset now)
    {
        if (Status is not (BillStatus.Draft or BillStatus.Open) || AmountPaid != 0m)
            throw new DomainException(
                "Only draft or open bills with no payments can be voided. Reverse any payments first.");

        VoidReason = Guard.NotBlank(reason, nameof(VoidReason), 500);
        VoidedAt = now;
        Status = BillStatus.Void;
    }

    // Overdue is calculated from the due date, never stored.
    public bool IsOverdue(DateOnly today)
        => Status is BillStatus.Open or BillStatus.PartiallyPaid && DueDate < today;

    public int DaysOverdue(DateOnly today)
        => IsOverdue(today) ? today.DayNumber - DueDate.DayNumber : 0;

    private void EnsureDraft()
    {
        if (Status != BillStatus.Draft)
            throw new DomainException("Only draft bills can be changed.");
    }

    private static void ValidateMoney(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("The amount must be greater than zero.");
        if (MoneyMath.Round(amount) != amount)
            throw new DomainException("The amount can have at most 2 decimal places.");
    }
}
