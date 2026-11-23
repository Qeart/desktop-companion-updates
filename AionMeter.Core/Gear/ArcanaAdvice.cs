using System.Text.Json;
using System.Text.RegularExpressions;
using AionMeter.Core.Events;
using AionMeter.Core.Pets;

namespace AionMeter.Core.Gear;

public enum ArcanaSlot { Unknown, Chalice, Parchment, Compass, Bell, Mirror }
public sealed record ArcanaSkill(string Name, int? Bonus);
public sealed record ArcanaPanel(string Title, ArcanaSlot Slot, string Family, string? MainStat, IReadOnlyList<ArcanaSkill> Skills, double SetBottom);
public sealed record ArcanaCardAdvice(string Slot, string Label, string Family, string Stat, string Effect, string Alternative);
public sealed record ArcanaClassAdvice(string Class, string Label, string Source, Dictionary<string, string[]> Goals,
    ArcanaCardAdvice[]? Cards = null, string Plan = "", string Build = "", string Note = "", string[]? Sources = null,
    Dictionary<string, string[]>? SecondaryGoals = null, Dictionary<string, string[]>? Pools = null);
public sealed record ArcanaAdviceData(string VerifiedOn, string Mode, ArcanaCardAdvice[] Cards, ArcanaClassAdvice[] Classes);
public sealed record ArcanaSkillGoal(string Name, bool Present, int? Bonus);
public sealed record ArcanaAdvice(string ClassLabel, string CardLabel, string GoalCard, string MainStat, string Effect,
    bool MainMatches, string Alternative, IReadOnlyList<ArcanaSkillGoal> Skills, string Plan, string VerifiedOn,
    string Build = "", string Note = "", IReadOnlyList<ArcanaSkillGoal>? SecondarySkills = null);

public static class ArcanaPanelParser
{
    private static readonly string[] MainStats = ["Time [Siel]", "Space [Israphel]", "Life [Yustiel]", "Destiny [Marchutan]", "Freedom [Vaizel]", "Death [Triniel]", "Justice [Nezekan]", "Destruction [Zikel]", "Illusion [Kaisinel]", "Wisdom [Lumiel]"];
    private static string Clean(string value) => ItemTooltipParser.Normalize(value);
    // Advice windows are intentionally visible in screenshots. Their own text
    // must not extend the game's Set Effects section or enter image-cache probes.
    public static IReadOnlyList<ShopText> GameRows(IReadOnlyList<ShopText> rows)
    {
        var top = rows.Where(r => Clean(r.Text).StartsWith("aurumarkana", StringComparison.Ordinal))
            .Select(r => r.Y).DefaultIfEmpty(double.PositiveInfinity).Min();
        return double.IsFinite(top) ? rows.Where(r => r.Y < top).ToArray() : rows;
    }
    public static bool IsHeader(string text)
    {
        var value = Clean(text);
        return value.Contains("arcana") && (value.Contains("equip") || value.Contains("eguip")) && value.Contains("extract")
            && (value.Contains("transmute") || value.Contains("transrnute"));
    }
    public static ArcanaPanel? Parse(IReadOnlyList<ShopText> rows)
    {
        var ordered = GameRows(rows).OrderBy(r => r.Y).ThenBy(r => r.X).ToArray();
        var equipped = ordered.FirstOrDefault(r => Clean(r.Text).Contains("equippedeffect") || Clean(r.Text).Contains("eguippedeffect"));
        var set = ordered.FirstOrDefault(r => Clean(r.Text).Contains("seteffect"));
        if (equipped == null || set == null || set.Y <= equipped.Y) return null;
        var header = ordered.Where(r => r.Y < equipped.Y).ToArray();
        // The decorative name is ellipsised for long names such as Parchment of Vigor.
        // Advice depends on the card slot, which the smaller Item Level category also
        // identifies. Never require or invent an unreadable Vigor/Magic suffix.
        var categories = header.Where(r => ItemTooltipParser.IsItemLevel(r.Text)).ToArray();
        var categorySlots = categories.Select(r => ReadSlot(r.Text)).Where(s => s != ArcanaSlot.Unknown).Distinct().ToArray();
        var nameRows = header.Where(r => !ItemTooltipParser.IsItemLevel(r.Text)).ToArray();
        var nameSlots = nameRows.Select(r => ReadSlot(r.Text)).Where(s => s != ArcanaSlot.Unknown).Distinct().ToArray();
        if (categorySlots.Length > 1 || categorySlots.Length == 0 && nameSlots.Length != 1) return null;
        var slot = categorySlots.Length == 1 ? categorySlots[0] : nameSlots[0];
        var title = nameRows.FirstOrDefault(r => ReadSlot(r.Text) == slot)
            ?? nameRows.FirstOrDefault(r => Regex.IsMatch(r.Text, @"\bof\s+(Vigor|Magic)\b", RegexOptions.IgnoreCase));
        var family = Regex.Match(string.Join(" ", nameRows.Select(r => r.Text)), @"\bof\s+(Vigor|Magic)\b", RegexOptions.IgnoreCase);
        var effectRows = ordered.Where(r => r.Y > equipped.Y && r.Y < set.Y).ToArray();
        string? main = null;
        foreach (var row in effectRows)
        {
            main = MainStats.FirstOrDefault(s => {
                var key = Clean(s); var text = Clean(row.Text);
                return text.StartsWith(key, StringComparison.Ordinal) || Regex.IsMatch(row.Text, @"^\s*" + s.Split(' ')[0] + @"\s*[\[(]", RegexOptions.IgnoreCase);
            });
            if (main != null) break;
        }
        var skills = new List<ArcanaSkill>();
        foreach (var row in effectRows)
        {
            if (!Regex.IsMatch(row.Text, @"[A-Za-z]{3}") || main != null && Clean(row.Text).StartsWith(Clean(main.Split(' ')[0]), StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(row.Text, @"^\s*L[vw]", RegexOptions.IgnoreCase)) continue;
            var levelRows = effectRows.Where(other => Math.Abs(other.Y - row.Y) <= Math.Max(4, row.Height * .6)).Select(other => other.Text);
            var bonus = Regex.Match(string.Join(" ", levelRows), @"\+\s*([1-4])\b");
            var name = Regex.Replace(row.Text, @"\s*L[vw]\.?.*$", "", RegexOptions.IgnoreCase).Trim();
            skills.Add(new(name, bonus.Success ? int.Parse(bonus.Groups[1].Value) : null));
        }
        var bottom = set.Y + set.Height; var previous = set.Y;
        foreach (var row in ordered.Where(r => r.Y >= set.Y))
        {
            if (row.Y - previous > Math.Max(56, row.Height * 3)) break;
            bottom = Math.Max(bottom, row.Y + row.Height); previous = row.Y;
        }
        return new(title?.Text ?? slot.ToString(), slot, family.Success ? family.Groups[1].Value : "", main, skills, bottom);
    }
    private static ArcanaSlot ReadSlot(string text)
    {
        var type = Regex.Match(text, @"\b(Chal[ij]ce|Parch(?:ment|rnent)|Compass|Bell|Mirror)\b", RegexOptions.IgnoreCase);
        if (type.Success)
        {
            var value = type.Value.Replace("Chaljce", "Chalice", StringComparison.OrdinalIgnoreCase)
                .Replace("Parchrnent", "Parchment", StringComparison.OrdinalIgnoreCase);
            return Enum.TryParse<ArcanaSlot>(value, true, out var slot) ? slot : ArcanaSlot.Unknown;
        }
        var clipped = Regex.Match(text, @"^\s*(?:\+\s*\d+\s*)?([A-Za-z]{4,})(?:\.{1,3}|…)\s*$");
        if (!clipped.Success) return ArcanaSlot.Unknown;
        var choices = Enum.GetValues<ArcanaSlot>().Where(s => s != ArcanaSlot.Unknown
            && s.ToString().StartsWith(clipped.Groups[1].Value, StringComparison.OrdinalIgnoreCase)).ToArray();
        return choices.Length == 1 ? choices[0] : ArcanaSlot.Unknown;
    }
}

public sealed class ArcanaAdviceCatalog
{
    private readonly ArcanaAdviceData _data;
    private readonly string[] _skillNames;
    public ArcanaAdviceCatalog(ArcanaAdviceData data)
    {
        _data = data;
        _skillNames = data.Classes.SelectMany(c => c.Goals.Values.Concat(c.SecondaryGoals?.Values ?? Enumerable.Empty<string[]>())
            .Concat(c.Pools?.Values ?? Enumerable.Empty<string[]>())).SelectMany(names => names).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static ArcanaAdviceCatalog Load(string path) => new(JsonSerializer.Deserialize<ArcanaAdviceData>(File.ReadAllText(path)) ?? throw new InvalidDataException("Brak profili arkan."));
    public ArcanaAdvice? For(GameClass gameClass, ArcanaPanel panel)
    {
        var player = _data.Classes.FirstOrDefault(c => c.Class == gameClass.ToString());
        var card = (player?.Cards ?? _data.Cards).FirstOrDefault(c => c.Slot == panel.Slot.ToString());
        if (player == null || card == null || !player.Goals.TryGetValue(card.Slot, out var goals)) return null;
        // Resolve OCR once against the complete legal vocabulary, including skills
        // outside this build. A near-looking non-goal cannot become a false green tick.
        var observed = panel.Skills.Select(s => (Name: OcrSkillNames.Find(s.Name, _skillNames), Skill: s)).ToArray();
        ArcanaSkillGoal Goal(string goal) {
            var seen = observed.FirstOrDefault(s => s.Name == goal).Skill;
            return new(goal, seen != null, seen?.Bonus);
        }
        var secondary = player.SecondaryGoals?.GetValueOrDefault(card.Slot) ?? [];
        return new(player.Label, card.Label, card.Slot + " of " + card.Family, card.Stat, card.Effect,
            panel.MainStat == card.Stat && (panel.Family.Length == 0 || panel.Family.Equals(card.Family, StringComparison.OrdinalIgnoreCase)),
            card.Alternative, goals.Select(Goal).ToArray(), player.Plan, _data.VerifiedOn,
            player.Build, player.Note, secondary.Select(Goal).ToArray());
    }
}
