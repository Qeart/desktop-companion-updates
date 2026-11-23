namespace AionMeter.Core.Cubes;

/// <summary>Camera freshness is independent of player coordinates. Times refer to image acquisition.</summary>
public sealed class CameraOrientationTracker
{
    public CameraReading? Reading { get; private set; }
    public long At { get; private set; }
    public void Clear() { Reading = null; At = 0; }
    public bool Observe(CameraReading? reading, long acquiredAt)
    {
        if (acquiredAt < At) return false;
        At = acquiredAt;
        Reading = reading is { } r && double.IsFinite(r.Heading) && double.IsFinite(r.Confidence) && r.Confidence >= .12
            ? r with { Heading = Navigation.Normalize(r.Heading) } : null;
        return Reading != null;
    }
    public bool TryHeading(long now, int maxAgeMilliseconds, out double heading)
    {
        heading = Reading?.Heading ?? 0;
        return Reading != null && At <= now && now - At <= maxAgeMilliseconds;
    }
}
