namespace AionMeter.Core.Bosses;

public static class BossFightAnalyzer
{
    public static IReadOnlyList<BossTimingProposal> Analyze(IEnumerable<BossLogRecord> records, bool includeSynthetic = false)
    {
        var casts = records.Where(r => r.SchemaVersion == 1 && r.Signal.Kind == BossSignalKind.Cast &&
            (includeSynthetic || !r.Signal.Synthetic)).OrderBy(r => r.Signal.At).ToArray();
        var output = new List<BossTimingProposal>();
        foreach (var group in casts.GroupBy(r => (r.Signal.NpcCode, r.Signal.SkillId, r.Signal.Phase)))
        {
            var gaps = new List<double>(); var fights = 0;
            foreach (var pull in group.GroupBy(r => r.SessionId))
            {
                var points = pull.Select(r => r.Signal.At).Distinct().Order().ToArray(); var count = gaps.Count;
                for (var i = 1; i < points.Length; i++) { var delta = (points[i] - points[i - 1]) / 1000d; if (delta is >= 1 and <= 600) gaps.Add(delta); }
                if (gaps.Count > count) fights++;
            }
            if (gaps.Count == 0) continue;
            output.Add(Estimate(group.Key.NpcCode, group.Key.SkillId, group.Key.Phase, group.First().Signal.SkillName, "cooldown", null, gaps, fights));
        }
        return output.OrderByDescending(p => p.Confidence).ToArray();
    }
    public static BossTimingProposal Estimate(int npc, int skill, int phase, string name, string type, int? after, IReadOnlyList<double> values, int fights)
    {
        var median = Median(values); var mad = Median(values.Select(x => Math.Abs(x - median)));
        var ratio = values.Count == 0 ? 0 : values.Count(x => Math.Abs(x - median) <= Math.Max(.6, median * .12)) / (double)values.Count;
        var confidence = ratio * Math.Max(0, 1 - 2 * mad / Math.Max(1, median)) * Math.Min(1, values.Count / 4d) * Math.Min(1, fights / 2d);
        return new(npc, skill, phase, name, type, after, median, mad, ratio, values.Count, fights, confidence,
            values.Count >= 4 && fights >= 2 && median is >= 3 and <= 300 && ratio >= .8 && confidence >= .8);
    }
    private static double Median(IEnumerable<double> values)
    { var a = values.Where(double.IsFinite).Order().ToArray(); return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }
}
