using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionMeter.Core.Bosses;

public enum BossSignalKind { Pull, Cast, Hit, Hp, Death, Reset, Phase, CastEnd, Mark }
public sealed record BossSignal(long At, BossSignalKind Kind, uint Actor, int NpcCode)
{
    public string BossName { get; init; } = "";
    public int MapId { get; init; }
    public int SkillId { get; init; }
    public string SkillName { get; init; } = "";
    public double? HpPercent { get; init; }
    public int Phase { get; init; }
    public bool PullConfirmed { get; init; }
    public bool Synthetic { get; init; }
    public string Source { get; init; } = "Aurum";
    public double Confidence { get; init; } = 1;
}
public sealed record BossMechanic
{
    public string Id { get; init; } = "";
    public int SkillId { get; init; }
    public string Name { get; init; } = "";
    public string Reaction { get; init; } = "";
    public string Category { get; init; } = "warning";
    public string[] Roles { get; init; } = ["all"];
    public int? Phase { get; init; }
    public double? FirstSeconds { get; init; }
    public double? CooldownSeconds { get; init; }
    public int? AfterSkillId { get; init; }
    public double? AfterSeconds { get; init; }
    public double? CastSeconds { get; init; }
    public double? HpBelowPercent { get; init; }
    public int? SetPhase { get; init; }
    public double[] WarningSeconds { get; init; } = [10, 5, 3];
    public bool AlertOnCast { get; init; } = true;
    public double Confidence { get; init; } = .8;
    public int Samples { get; init; }
    public string Evidence { get; init; } = "";
}
public sealed record BossProfile
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "";
    public int[] NpcCodes { get; init; } = [];
    public int[] Maps { get; init; } = [];
    public bool Synthetic { get; init; }
    public string Evidence { get; init; } = "Local";
    public string Source { get; init; } = "";
    public double ResetAfterSeconds { get; init; } = 45;
    public BossMechanic[] Mechanics { get; init; } = [];
}
public sealed record BossEngineOptions
{
    public string Role { get; init; } = "dps";
    public bool CommunityTimers { get; init; } = true;
    public bool LearnedTimers { get; init; }
    public bool ShowObservedSkills { get; init; } = true;
    public double[]? WarningSeconds { get; init; }
    public IReadOnlySet<string> DisabledMechanics { get; init; } = new HashSet<string>();
}
public sealed record BossAlert(string Key, string Text, string Category, long At, bool Sound, bool Predicted, bool Synthetic);
public sealed record BossObservedSkill(string Key, uint Actor, int SkillId, string Name, string Boss, long At, bool Synthetic);
public sealed record BossBarSnapshot(string Key, uint Actor, int SkillId, string Boss, string Label, string Reaction, string Category,
    string Kind, long StartedAt, long DueAt, double Seconds, double Fraction, double Confidence, int Samples, string Evidence);
public sealed record BossFightSnapshot(Guid SessionId, uint Actor, int NpcCode, string Name, int MapId, int Phase, double? HpPercent, long StartedAt, bool PullConfirmed);
public sealed record BossAssistantSnapshot(IReadOnlyList<BossFightSnapshot> Fights, IReadOnlyList<BossBarSnapshot> Bars,
    IReadOnlyList<BossAlert> Alerts, long At)
{
    public IReadOnlyList<BossObservedSkill> ObservedSkills { get; init; } = [];
}
public sealed record BossLogRecord(int SchemaVersion, Guid SessionId, BossSignal Signal);
public sealed record BossTimingProposal(int NpcCode, int SkillId, int Phase, string Skill, string Type, int? AfterSkillId,
    double MedianSeconds, double MadSeconds, double InlierRatio, int Samples, int Fights, double Confidence, bool Eligible);

public static class BossJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { WriteIndented = false, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
}
