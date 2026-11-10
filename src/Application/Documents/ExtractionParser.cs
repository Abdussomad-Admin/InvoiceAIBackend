using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Common;

namespace Application.Documents;

// Turns the model's answer into typed values. It is deliberately strict: a value that cannot be read
// exactly becomes null, because a guessed amount is worse than a blank one.
public static class ExtractionParser
{
    private const int MaxLines = 200;
    private const string Unreadable = "The assistant's answer could not be read as an invoice. You can try again.";

    private static readonly Regex PlainNumber = new(@"^-?\d{1,15}(\.\d{1,6})?$", RegexOptions.Compiled);
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public static ExtractedBill Parse(string? modelText)
    {
        var json = ExtractObject(modelText) ?? throw new DomainException(Unreadable);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new DomainException(Unreadable);

            // Identifiers and codes are never shortened. A cut-off value looks plausible but is wrong,
            // so one that does not fit becomes null and the reviewer fills it in.
            var currency = Text(root, "currency", 20, truncate: false)?.ToUpperInvariant();

            return new ExtractedBill(
                Text(root, "supplierName", 200),
                Text(root, "supplierTaxId", 50, truncate: false),
                Text(root, "invoiceNumber", 100, truncate: false),
                Date(root, "invoiceDate"), Date(root, "dueDate"),
                currency is not null && CurrencyCode.IsMatch(currency) ? currency : null,
                Number(root, "subtotal"), Number(root, "taxTotal"), Number(root, "total"), Lines(root));
        }
        catch (JsonException)
        {
            throw new DomainException(Unreadable);
        }
    }

    // Models often wrap JSON in markdown fences or add a sentence around it, so take the outermost braces.
    private static string? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000) return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? Text(JsonElement root, string name, int max, bool truncate = true)
    {
        if (!root.TryGetProperty(name, out var element)) return null;

        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value)) return null;

        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (value.Length == 0) return null;

        if (value.Length > max)
            return truncate ? value[..max] : null;

        return value;
    }

    private static DateOnly? Date(JsonElement root, string name)
    {
        var text = Text(root, name, 20, truncate: false);
        return text is not null &&
               DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) ? Number(element) : null;

    private static decimal? Number(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDecimal(out var number) && Math.Abs(number) <= 1_000_000_000_000m ? number : null;

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()?.Trim();
            if (text is not null && PlainNumber.IsMatch(text) &&
                decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return null;
    }

    private static IReadOnlyList<ExtractedLine> Lines(JsonElement root)
    {
        if (!root.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array)
            return [];

        var lines = new List<ExtractedLine>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var line = new ExtractedLine(
                Text(item, "description", 500), Number(item, "quantity"), Number(item, "unitPrice"),
                Number(item, "discountPercent"), Number(item, "taxRatePercent"));

            if (line.Description is null && line.Quantity is null && line.UnitPrice is null) continue;

            lines.Add(line);
            if (lines.Count == MaxLines) break;
        }

        return lines;
    }
}