namespace AionMeter.Core.Combat;

public sealed record BossKillEstimate(double Seconds, double HpPerSecond, double SampleSeconds);

/// <summary>Short-window ETA from confirmed boss HP, independent of add damage and partial damage rankings.</summary>
public sealed class BossKillTimeEstimator
{
    private const long WindowMs = 10_000, MinimumMs = 3_000, StaleMs = 3_000;
    private readonly List<(long At, long Hp)> _samples = [];
    private long? _latestAt, _lastLossAt;
    private long _hp;

    public void Clear() { _samples.Clear(); _latestAt = _lastLossAt = null; _hp = 0; }

    public void Observe(long at, long hp)
    {
        if (at < 0 || hp < 0 || _latestAt is { } previous && at <= previous) return;
        if (_latestAt is { } last && (at - last > StaleMs || hp > _hp)) Clear(); // reconnect, healing or a new phase
        if (_latestAt != null && hp < _hp) _lastLossAt = at;
        _latestAt = at; _hp = hp;
        if (_samples.Count == 0 || at / 100 > _samples[^1].At / 100) _samples.Add((at, hp));
        else if (_samples.Count > 1) _samples[^1] = (at, hp);
        while (_samples.Count > 2 && _samples[1].At <= at - WindowMs) _samples.RemoveAt(0);
    }

    public BossKillEstimate? Estimate(long now)
    {
        if (_latestAt is not { } at || _lastLossAt is not { } loss || _hp <= 0 || now < at || now - at > StaleMs || now - loss > StaleMs || _samples.Count < 2) return null;
        var elapsed = at - _samples[0].At;
        var damage = _samples[0].Hp - _hp;
        if (elapsed < MinimumMs || damage <= 0) return null;
        var rate = damage * 1000d / elapsed;
        var seconds = _hp / rate;
        return double.IsFinite(seconds) && seconds is > 0 and <= 86_400 ? new(seconds, rate, elapsed / 1000d) : null;
    }
}
