using System.Globalization;
using Domain.Common;

namespace Application.Documents;

// Checks what was read. "Error" means needs your input, "Warning" means check this. Neither blocks
// anything here: the real gate is the normal bill rules when a person approves the final values.
public static class ExtractionValidator
{
    public static IReadOnlyList<ExtractionIssue> Validate(ExtractedBill bill, DateOnly today, string baseCurrency)
    {
        var issues = new List<ExtractionIssue>();
        void Add(string field, IssueSeverity severity, string message) => issues.Add(new(field, severity, message));

        if (bill.SupplierName is null)
            Add("supplierName", IssueSeverity.Warning, "The supplier name was not found. Choose the supplier yourself.");

        if (bill.InvoiceNumber is null)
            Add("invoiceNumber", IssueSeverity.Error, "The invoice number was not found.");

        if (bill.InvoiceDate is not { } date)
            Add("invoiceDate", IssueSeverity.Error, "The invoice date was not found or could not be read reliably.");
        else if (date > today)
            Add("invoiceDate", IssueSeverity.Error, "The invoice date is in the future.");
        else if (date.Year < 2000)
            Add("invoiceDate", IssueSeverity.Error, "The invoice date is implausibly old.");

        if (bill.DueDate is { } due && bill.InvoiceDate is { } invoiceDate && due < invoiceDate)
            Add("dueDate", IssueSeverity.Error, "The due date is before the invoice date.");

        if (bill.CurrencyCode is null)
            Add("currency", IssueSeverity.Warning, $"No currency was found. {baseCurrency} will be assumed.");
        else if (bill.CurrencyCode != baseCurrency)
            Add("currency", IssueSeverity.Error,
                $"The invoice appears to be in {bill.CurrencyCode}, but this business records {baseCurrency}. Other currencies are not supported yet.");

        if (bill.Total is not { } total)
            Add("total", IssueSeverity.Error, "The total amount was not found.");
        else if (total <= 0)
            Add("total", IssueSeverity.Error, "The total amount must be greater than zero.");

        if (bill.Lines.Count == 0)
            Add("lines", IssueSeverity.Warning, "No line items were read. Add them yourself.");

        for (var i = 0; i < bill.Lines.Count; i++)
        {
            var line = bill.Lines[i];
            var missing = new List<string>();
            if (line.Description is null) missing.Add("description");
            if (line.Quantity is not > 0) missing.Add("quantity");
            if (line.UnitPrice is not >= 0) missing.Add("unit price");

            if (missing.Count > 0)
                Add("lines", IssueSeverity.Warning, $"Line {i + 1} is incomplete (missing: {string.Join(", ", missing)}).");
        }

        CheckArithmetic(bill, Add);
        return issues;
    }

    public static ConfidenceLevel ConfidenceOf(IReadOnlyList<ExtractionIssue> issues)
        => issues.Any(i => i.Severity == IssueSeverity.Error) ? ConfidenceLevel.Low
         : issues.Count > 0 ? ConfidenceLevel.Medium
         : ConfidenceLevel.High;

    private static void CheckArithmetic(ExtractedBill bill, Action<string, IssueSeverity, string> add)
    {
        var complete = bill.Lines.Where(l => l.Quantity is > 0 && l.UnitPrice is >= 0).ToList();

        if (complete.Count > 0 && complete.Count == bill.Lines.Count)
        {
            // Same arithmetic as a real bill line: round the line, take the discount, keep the net.
            var linesNet = complete.Sum(l =>
            {
                var subtotal = MoneyMath.Round(l.Quantity!.Value * l.UnitPrice!.Value);
                return subtotal - MoneyMath.Percent(subtotal, l.DiscountPercent ?? 0m);
            });

            decimal? expected = bill.Subtotal
                ?? (bill.Total is { } t && bill.TaxTotal is { } x ? t - x : null);

            if (expected is { } e && Math.Abs(linesNet - e) > Tolerance(bill.Total))
                add("lines", IssueSeverity.Warning,
                    $"The lines add up to {Money(linesNet)} but the invoice says {Money(e)} before tax.");
        }

        if (bill.Subtotal is { } s && bill.TaxTotal is { } tax && bill.Total is { } total &&
            Math.Abs(s + tax - total) > Tolerance(total))
            add("total", IssueSeverity.Warning,
                $"The subtotal plus tax is {Money(s + tax)}, but the total says {Money(total)}.");
    }

    private static decimal Tolerance(decimal? total) => Math.Max(0.05m, Math.Abs(total ?? 0m) * 0.001m);

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}