using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AionMeter.Core.Pets;

namespace AionMeter.Core.Gear;

public enum GearSlot { Unknown, Weapon, OffHand, Helmet, Shoulders, Chest, Legs, Gloves, Boots, Cloak, Necklace, Earrings, Ring, Bracelet }
public sealed record ItemTooltip(string Title, GearSlot Slot, bool BindingRead, IReadOnlyList<string> BindingLines);

public static class ItemTooltipParser
{
    private static readonly (GearSlot Slot, string Pattern)[] Slots = [
        (GearSlot.Helmet, @"\b(hel[mn]|helmet|hood|headgear|hat|cap)\b"),
        (GearSlot.Shoulders, @"\b(shoulders?|shoulder\s*guards?|pauldrons?|epaulets?)\b"),
        (GearSlot.Gloves, @"\b(gloves?|hand\s*guards?|gauntlets?)\b"),
        (GearSlot.Boots, @"\b(boots?|shoes?|sabatons?)\b"),
        (GearSlot.Legs, @"\b(legs?|pants?|leggings?|greaves|cuisses?|leg\s*guards?)\b"),
        (GearSlot.Cloak, @"\b(cloak|cape|mantle)\b"),
        (GearSlot.Necklace, @"\b(necklace|necklet)\b"),
        (GearSlot.Earrings, @"\b(earrings?|ear\s*ornaments?)\b"),
        (GearSlot.Ring, @"\brings?\b"),
        (GearSlot.Bracelet, @"\bbracelets?\b"),
        (GearSlot.Chest, @"\b(armor|armour|breastplate|robe|tunic|jacket|hauberk|chest)\b"),
        (GearSlot.OffHand, @"\b(shield|guard|off.hand)\b"),
        (GearSlot.Weapon, @"\b(staff|mace|scepter|sceptre|great\s*sword|long\s*sword|dagger|sword|long\s*bow|bow|spell\s*book|tome|orb|weapon)\b")
    ];
    public static string Normalize(string value)
    {
        var clean = new StringBuilder();
        foreach (var ch in value.Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch)) clean.Append(char.ToLowerInvariant(ch));
        return clean.ToString();
    }
    public static bool IsItemLevel(string text)
    {
        var clean = Normalize(text);
        return new[] { "itemlevel", "ilemlevel", "ltemlevel", "llemlevel", "1temlevel", "iternlevel" }.Any(clean.Contains);
    }
    public static ItemTooltip? Parse(IReadOnlyList<ShopText> rows)
    {
        var ordered = rows.OrderBy(r => r.Y).ThenBy(r => r.X).ToArray();
        var level = ordered.FirstOrDefault(r => IsItemLevel(r.Text));
        if (level == null) return null;
        var header = ordered.Where(r => r.Y < level.Y).ToArray();
        // Prefer the rarity/type line to the name: a Swordmaster's Ring is a ring, not a sword.
        var type = header.LastOrDefault(r => Regex.IsMatch(r.Text, @"\b(Common|Uncommon|Rare|Epic|Unique|Legendary|Mythic|Heroic)\b", RegexOptions.IgnoreCase));
        var slot = Classify(type?.Text ?? "");
        if (slot == GearSlot.Unknown) slot = Classify(string.Join(" ", header.Select(r => r.Text)));
        if (slot == GearSlot.Unknown) return null;
        var title = header.FirstOrDefault(r => !ReferenceEquals(r, type) && r.Text.Length >= 5)?.Text ?? type?.Text ?? slot.ToString();
        var binding = ordered.FirstOrDefault(r => Normalize(r.Text).Contains("soulbindingrate", StringComparison.Ordinal));
        if (binding == null) return new(title, slot, false, []);
        var stop = ordered.Where(r => r.Y > binding.Y).FirstOrDefault(r => IsBindingEnd(r.Text));
        var lines = new List<string>(); double? previousY = null;
        foreach (var row in ordered.Where(r => r.Y > binding.Y + binding.Height * .5 && (stop == null || r.Y < stop.Y)))
        {
            // A large section gap is also a boundary when OCR misses the Soul Fuse heading.
            // This prevents a socket's Attack+10 from being counted as a Soul Binding roll.
            if (previousY is { } y && row.Y - y > Math.Max(28, row.Height * 2.15)) break;
            lines.Add(row.Text); previousY = row.Y;
        }
        return new(title, slot, true, lines);
    }
    public static GearSlot Classify(string header)
    {
        var text = header.Replace('ł', 'l').Replace('Ł', 'L');
        foreach (var (slot, pattern) in Slots) if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return slot;
        return GearSlot.Unknown;
    }
    private static bool IsBindingEnd(string text)
    {
        var clean = Normalize(text);
        return new[] { "soulfuse", "souifuse", "fusephilosopher", "fusephiiosopher", "skininfo", "requiredlevel", "manastone", "sockets", "socketinfo", "seteffect" }.Any(clean.Contains);
    }
    public static bool HasStat(ItemTooltip item, string stat)
    {
        if (!item.BindingRead) return false;
        var key = Normalize(stat);
        return item.BindingLines.Any(line => {
            var text = Normalize(line);
            if (!text.StartsWith(key, StringComparison.Ordinal)) return false;
            // Do not count Attack increase / Attack Preparation as flat Attack, nor any
            // Tolerance line as Boost. Gem/base-stat lines never enter BindingLines.
            return text.Length == key.Length || char.IsDigit(text[key.Length]) || text[key.Length..].StartsWith("lv", StringComparison.Ordinal);
        });
    }
    public static bool HasSkill(ItemTooltip item, string skill) => item.BindingRead && item.BindingLines.Any(line => {
        var key = Normalize(skill);
        if (Normalize(line).StartsWith(key, StringComparison.Ordinal)) return true;
        var firstSpace = line.IndexOf(' ');
        return firstSpace is > 0 and <= 2 && Normalize(line[(firstSpace + 1)..]).StartsWith(key, StringComparison.Ordinal);
    });
}
