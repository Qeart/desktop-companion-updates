using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionMeter.Core.Bosses;

public sealed class BossProfileCatalog
{
    public IReadOnlyList<BossProfile> Profiles { get; }
    public IReadOnlyList<string> Errors { get; }
    public BossProfileCatalog(IEnumerable<BossProfile> profiles, IEnumerable<string>? errors = null)
    {
        var valid = new List<BossProfile>(); var issues = new List<string>(errors ?? []);
        foreach (var profile in profiles)
        {
            if (Validate(profile) is { } error) issues.Add(profile.Id + ": " + error); else valid.Add(profile);
        }
        Profiles = valid.GroupBy(p => p.Id, StringComparer.Ordinal).Select(g => g.OrderBy(p => p.Version).Last()).ToArray(); Errors = issues;
    }
    public BossProfile? Find(int npc, int map, bool synthetic)
        => Profiles.FirstOrDefault(p => p.Synthetic == synthetic && p.NpcCodes.Contains(npc) && (p.Maps.Length == 0 || p.Maps.Contains(map)));
    public static BossProfileCatalog Load(params string[] directories)
    {
        var profiles = new List<BossProfile>(); var errors = new List<string>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) continue;
            string[] files;
            try { files = Directory.GetFiles(directory, "*.json").Order().Take(128).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(Path.GetFileName(directory) + ": " + ex.Message); continue; }
            foreach (var file in files)
                try
                {
                    if (new FileInfo(file).Length > 512_000) throw new InvalidDataException("Za duży plik");
                    profiles.Add(JsonSerializer.Deserialize<BossProfile>(File.ReadAllText(file), BossJson.Options) ?? throw new InvalidDataException("Pusty profil"));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        return new(profiles, errors);
    }
    public static string? Validate(BossProfile p)
    {
        static bool Id(string? s) => s != null && Regex.IsMatch(s, "^[a-zA-Z0-9_.-]{1,64}$");
        static bool Time(double? t) => t == null || double.IsFinite(t.Value) && t is > 0 and <= 3600;
        if (p.SchemaVersion != 1 || !Id(p.Id) || p.Version < 1 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || p.Evidence == null || p.Source == null) return "Nieprawidłowa wersja, ID lub nazwa";
        if (p.NpcCodes is not { Length: > 0 and <= 32 } || p.NpcCodes.Any(n => p.Synthetic ? n >= 0 : n is <= 0 or > 9_999_999)) return "Nieprawidłowy kod NPC";
        if (p.Maps == null || p.Maps.Length > 32 || p.Maps.Any(n => n <= 0) || !Time(p.ResetAfterSeconds)) return "Nieprawidłowa mapa/reset";
        if (p.Mechanics is not { Length: <= 64 } || p.Mechanics.Any(m => m == null) || p.Mechanics.Select(m => m.Id).Distinct().Count() != p.Mechanics.Length) return "Nieprawidłowa lista mechanik";
        foreach (var m in p.Mechanics)
        {
            if (!Id(m.Id) || string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 120 || m.Reaction == null || m.Reaction.Length > 160) return "Nieprawidłowa nazwa mechaniki";
            if (m.SkillId == 0 && m.HpBelowPercent == null || m.SkillId != 0 && (p.Synthetic ? m.SkillId > 0 : m.SkillId < 0)) return "Nieprawidłowy skill";
            if (!Time(m.FirstSeconds) || !Time(m.CooldownSeconds) || !Time(m.AfterSeconds) || !Time(m.CastSeconds)) return "Nieprawidłowy czas";
            if (m.AfterSkillId.HasValue != m.AfterSeconds.HasValue || m.AfterSkillId == 0 || m.AfterSkillId is { } after && (p.Synthetic ? after > 0 : after < 0) || m.Evidence == null) return "Niepełna sekwencja";
            if (!double.IsFinite(m.Confidence) || m.Confidence is < 0 or > 1 || m.Samples < 0 || m.WarningSeconds is not { Length: <= 8 } || m.WarningSeconds.Any(w => !double.IsFinite(w) || w is <= 0 or > 60)) return "Nieprawidłowa pewność/ostrzeżenia";
            if (m.HpBelowPercent is { } hp && (!double.IsFinite(hp) || hp is <= 0 or >= 100) || m.SetPhase is < 1 or > 20 || m.Phase is < 1 or > 20) return "Nieprawidłowa faza";
            if (m.Roles is not { Length: > 0 and <= 4 } || m.Roles.Any(r => r is not ("all" or "dps" or "tank" or "healer")) || m.Category is not ("danger" or "warning" or "info" or "support")) return "Nieprawidłowa rola/kategoria";
        }
        return null;
    }
}
