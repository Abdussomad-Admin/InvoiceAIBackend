namespace Domain.Documents;

public enum DocumentKind
{
    Pdf = 0,
    Jpeg = 1,
    Png = 2
}

public static class DocumentRules
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPdfPages = 20;
    public const int MaxExtractedChars = 100_000;
    public const int MinTextCharsForTextLayer = 40;

    // The kind comes from the file's own first bytes, never from its name or the type the client claims.
    // A PDF must start with its header at byte zero, which is stricter than some readers, on purpose.
    public static DocumentKind? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith("%PDF-"u8)) return DocumentKind.Pdf;
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return DocumentKind.Jpeg;
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return DocumentKind.Png;
        return null;
    }

    public static string ContentTypeOf(DocumentKind kind) => kind switch
    {
        DocumentKind.Pdf => "application/pdf",
        DocumentKind.Jpeg => "image/jpeg",
        DocumentKind.Png => "image/png",
        _ => "application/octet-stream"
    };

    // The uploaded name is only ever displayed, never used as a path. This strips folders and odd characters.
    public static string SafeFileName(string? name)
    {
        var leaf = Path.GetFileName((name ?? "").Replace('\\', '/'));

        var cleaned = new string(leaf
            .Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' or '\\'))
            .ToArray()).Trim();

        if (cleaned.Length == 0) cleaned = "document";
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }
}