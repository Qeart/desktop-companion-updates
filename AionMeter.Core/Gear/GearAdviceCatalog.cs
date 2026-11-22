using System.Text.Json;
using AionMeter.Core.Events;

namespace AionMeter.Core.Gear;

public sealed record GearSlotAdvice(string Slot, string Label, string[] Targets, string[] Useful, string SkillKind,
    string[]? Skills = null, string Note = "");
public sealed record GearClassAdvice(string Class, string Label, string[] Active, string[] Passive, string Note, string Source,
    GearSlotAdvice[]? Slots = null, string Build = "", string[]? Priorities = null, string[]? Sources = null);
public sealed record GearAdviceData(string VerifiedOn, string Mode, string[] Sources, GearSlotAdvice[] Slots, GearClassAdvice[] Classes);
public sealed record GearAdviceTarget(string Name, bool Present);
public sealed record GearAdvice(string ClassLabel, string SlotLabel, string VerifiedOn, string Title, string Mode,
    IReadOnlyList<GearAdviceTarget> Targets, IReadOnlyList<GearAdviceTarget> Skills, IReadOnlyList<string> Useful, string Note, bool BindingRead,
    string Build = "");

public sealed class GearAdviceCatalog
{
    private readonly GearAdviceData _data;
    public GearAdviceCatalog(GearAdviceData data) => _data = data;
    public static GearAdviceCatalog Load(string path) => new(JsonSerializer.Deserialize<GearAdviceData>(File.ReadAllText(path)) ?? throw new InvalidDataException("Brak profili wyposażenia."));
    public GearAdvice? For(GameClass gameClass, ItemTooltip item)
    {
        var player = _data.Classes.FirstOrDefault(c => c.Class == gameClass.ToString());
        // An explicit class profile is complete. A missing slot must never silently
        // borrow a generic/another class's recommendation. Legacy catalogues still load.
        var slot = (player?.Slots ?? _data.Slots).FirstOrDefault(s => s.Slot == item.Slot.ToString());
        if (player == null || slot == null) return null;
        var skills = slot.Skills ?? (slot.SkillKind switch { "Active" => player.Active, "Passive" => player.Passive, _ => [] });
        return new(player.Label, slot.Label, _data.VerifiedOn, item.Title, _data.Mode,
            slot.Targets.Select(t => new GearAdviceTarget(t, ItemTooltipParser.HasStat(item, t))).ToArray(),
            skills.Select(t => new GearAdviceTarget(t, ItemTooltipParser.HasSkill(item, t))).ToArray(),
            slot.Useful, string.Join(" ", new[] { slot.Note, player.Note }.Where(n => !string.IsNullOrWhiteSpace(n))), item.BindingRead, player.Build);
    }
}
