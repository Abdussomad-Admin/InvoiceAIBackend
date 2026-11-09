using Application.Suppliers;

namespace Application.Documents;

public static class SupplierMatcher
{
    private static readonly HashSet<string> Ignored =
        ["ltd", "limited", "plc", "inc", "llc", "co", "company", "the", "and", "of"];

    // A strong match is a single supplier with the same tax ID or the same normalized name.
    // If two suppliers tie, nothing is chosen automatically.
    public static (IReadOnlyList<SupplierSuggestionDto> Suggestions, Guid? StrongMatchId) Match(
        string? name, string? taxId, IReadOnlyList<SupplierCandidate> candidates)
    {
        var wantedTax = Squash(taxId);
        var wantedTokens = Tokens(name);
        var scored = new List<(SupplierCandidate Candidate, double Score, string Reason)>();

        foreach (var candidate in candidates)
        {
            if (wantedTax.Length >= 4 && Squash(candidate.TaxIdentificationNumber) == wantedTax)
            {
                scored.Add((candidate, 1.0, "Same tax ID"));
                continue;
            }

            var tokens = Tokens(candidate.Name);
            if (wantedTokens.Count == 0 || tokens.Count == 0) continue;

            if (wantedTokens.SetEquals(tokens))
            {
                scored.Add((candidate, 0.95, "Same name"));
                continue;
            }

            var similarity = (double)wantedTokens.Intersect(tokens).Count() / wantedTokens.Union(tokens).Count();
            if (similarity >= 0.5)
                scored.Add((candidate, Math.Round(similarity, 2), "Similar name"));
        }

        var suggestions = scored
            .OrderByDescending(s => s.Score).ThenBy(s => s.Candidate.Name)
            .Take(3)
            .Select(s => new SupplierSuggestionDto(
                s.Candidate.Id, s.Candidate.Name, s.Candidate.TaxIdentificationNumber, s.Score, s.Reason))
            .ToList();

        var strong = scored.Where(s => s.Score >= 0.95).ToList();
        return (suggestions, strong.Count == 1 ? strong[0].Candidate.Id : null);
    }

    private static string Squash(string? value)
        => new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static HashSet<string> Tokens(string? name)
    {
        var text = new string((name ?? "").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray());
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Ignored.Contains(t)).ToHashSet();
    }
}