using System.Text.RegularExpressions;
namespace AionMeter.Core.Pets;

public static class ShopPetMatcher
{
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static PetDefinition? Match(string title, IEnumerable<PetDefinition> pets)
    {
        // Polish Windows OCR reads the crossed stroke of English 't' as 'ł'.
        title = title.Replace('ł', 't').Replace('Ł', 'T');
        var marker = Regex.Match(title, @"\b[PV]et(?:\s*[:;.]\s*|\s+)(.+)", RegexOptions.IgnoreCase);
        var name = Regex.Split(marker.Success ? marker.Groups[1].Value : title, @"\bOwned\b", RegexOptions.IgnoreCase)[0].Trim();
        var shortened = Regex.IsMatch(name, @"(?:\.|…)\s*$");
        name = Normalize(name.TrimEnd('.', '…', ' '));
        var all = pets.ToArray();
        if (!shortened)
        {
            var exact = all.Where(p => Normalize(p.Name) == name).ToArray();
            if (exact.Length == 1) return exact[0];
            // Without Pet: or a visible ellipsis, a bare fragment is insufficient.
            if (!marker.Success || name.Length < 7) return null;
        }
        if (name.Length < (marker.Success ? 4 : 6)) return null;
        // OCR can lose Pet: or its colon and can read an ellipsis as one/two dots.
        // Only a unique catalogue prefix identifies a clipped card. In particular,
        // Dratona... must not choose Dratona when Dratona Tinkerer also fits.
        var candidates = all.Where(p => Normalize(p.Name).StartsWith(name)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
    public static string Counter(PetState? state)
        => state == null ? "?" : state.Level >= 3 ? "FULL" : $"{state.Points}/{PetState.Costs[state.Level]}";
}
