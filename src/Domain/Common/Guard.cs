namespace Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException($"{field} must be at most {maxLength} characters.");

        return trimmed;
    }

    public static string? OptionalText(string? value, string field, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : NotBlank(value, field, maxLength);

    public static string IsoCode(string? value, int length, string field)
    {
        var code = NotBlank(value, field, length).ToUpperInvariant();
        if (code.Length != length || !code.All(char.IsAsciiLetter))
            throw new DomainException($"{field} must be exactly {length} letters.");
        return code;
    }

    public static string TimeZone(string? value, string field)
    {
        var id = NotBlank(value, field, 100);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            throw new DomainException($"{field} '{id}' is not a recognised time zone.");
        return id;
    }
}