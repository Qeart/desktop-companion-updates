using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AionMeter.Core.Gear;

public static class OcrSkillNames
{
    public static string? Find(string observed, IReadOnlyCollection<string> knownNames)
    {
        var candidates = knownNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var variants = new List<string> { Clean(observed) };
        // Tiny effect icons sometimes become one/two OCR letters before the name.
        var icon = Regex.Match(observed, @"^\s*[\p{P}\p{S}]*[\p{L}\p{N}]{1,2}[\p{P}\p{S}]*\s+(.+)$");
        if (icon.Success) variants.Add(Clean(icon.Groups[1].Value));
        var exact = candidates.Where(name => variants.Contains(Clean(name))).ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1) return null;
        // Complete-name comparison, with only specified glyph confusions. No edit
        // distance, substring or guessed completion of unreadable names is used.
        var visual = candidates.Where(name => variants.Any(value => SameGlyphs(value, Clean(name)))).ToArray();
        return visual.Length == 1 ? visual[0] : null;
    }
    private static string Clean(string value)
    {
        var result = new StringBuilder();
        foreach (var letter in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(letter))
                result.Append(char.ToLowerInvariant(letter));
        return result.ToString();
    }
    private static bool SameGlyphs(string observed, string expected)
    {
        if (observed.Length != expected.Length || observed.Length < 5) return false;
        static bool Vertical(char letter) => letter is 'i' or 'l' or '1';
        for (var i = 0; i < observed.Length; i++)
        {
            var a = observed[i]; var b = expected[i];
            if (a == b || Vertical(a) && Vertical(b) || a == 'ł' && (b == 't' || Vertical(b))
                || a == '0' && b == 'o') continue;
            return false;
        }
        return true;
    }
}
