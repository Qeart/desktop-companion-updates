namespace AionMeter.Core.PetMaps;

// Motion is applied in screen pixels, before the next calibration correction.
public readonly record struct MapMotion(double Scale, double X, double Y)
{
    public static MapMotion Identity => new(1, 0, 0);
    public MapMotion Follow(MapMotion next) => new(Scale * next.Scale, X * next.Scale + next.X, Y * next.Scale + next.Y);
    public PetMapAlignment Apply(PetMapAlignment pose) => pose.Follow(Scale, 0, X, Y, pose.Inliers, pose.Matches);
}

public sealed class MapPoseStabilizer
{
    public PetMapAlignment? Current { get; private set; }
    public string CalibrationState { get; private set; } = "Uncalibrated";
    public double CalibrationErrorPixels { get; private set; }
    public double CorrectionPixels { get; private set; }
    private PetMapAlignment? _target, _confirmation;
    private readonly double _width, _height;

    public MapPoseStabilizer(double width, double height) { _width = width; _height = height; }
    public void Reset()
    {
        Current = _target = _confirmation = null;
        CalibrationState = "Uncalibrated"; CalibrationErrorPixels = CorrectionPixels = 0;
    }
    public void Advance(MapMotion motion, double elapsedSeconds)
    {
        CorrectionPixels = 0;
        if (Current == null) return;
        Current = motion.Apply(Current);
        if (_target != null) _target = motion.Apply(_target);
        if (_confirmation != null) _confirmation = motion.Apply(_confirmation);
        if (_target == null) return;
        var error = Distance(Current, _target);
        // Only absolute calibration is eased. Measured map motion is never delayed.
        var fraction = error < .08 ? 1 : Math.Min(1, 24 * Math.Clamp(elapsedSeconds, 0, .05) / error);
        Current = Blend(Current, _target, fraction); CorrectionPixels = error * fraction;
        if (fraction >= 1) { _target = null; CalibrationState = "Aligned"; }
    }
    public bool Calibrate(PetMapAlignment candidate)
    {
        if (!candidate.IsPlausible || candidate.Inliers < 24 || candidate.Inliers < candidate.Matches * .4)
        { CalibrationState = "RejectedWeak"; return false; }
        if (Current == null)
        {
            Current = candidate; _target = _confirmation = null; CalibrationState = "Acquired"; return true;
        }
        CalibrationErrorPixels = candidate.MapId == Current.MapId ? Distance(Current, candidate) : double.PositiveInfinity;
        if (CalibrationErrorPixels > 12)
        {
            if (_confirmation == null || _confirmation.MapId != candidate.MapId || Distance(_confirmation, candidate) > 3)
            { _confirmation = candidate; CalibrationState = "AwaitingConfirmation"; return false; }
            _confirmation = null;
            if (candidate.MapId != Current.MapId || CalibrationErrorPixels > 80)
            {
                // Do not slide markers across an unrelated map. Hide and acquire again.
                Current = _target = null; CalibrationState = "ReacquireRequired"; return false;
            }
        }
        else _confirmation = null;
        if (CalibrationErrorPixels < .15) { CalibrationState = "Aligned"; _target = null; return true; }
        _target = candidate; CalibrationState = "Correcting"; return true;
    }
    public double Distance(PetMapAlignment from, PetMapAlignment to)
    {
        var largest = 0d; var divisor = from.A * from.A + from.B * from.B;
        if (divisor <= 0) return double.PositiveInfinity;
        foreach (var (x, y) in new[] { (_width * .2, _height * .2), (_width * .8, _height * .2), (_width * .5, _height * .8) })
        {
            var dx = x - from.Tx; var dy = y - from.Ty;
            var point = to.Project((from.A * dx + from.B * dy) / divisor, (-from.B * dx + from.A * dy) / divisor);
            largest = Math.Max(largest, Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2)));
        }
        return largest;
    }
    private static PetMapAlignment Blend(PetMapAlignment from, PetMapAlignment to, double amount) => new(from.MapId,
        from.A + (to.A - from.A) * amount, from.B + (to.B - from.B) * amount,
        from.Tx + (to.Tx - from.Tx) * amount, from.Ty + (to.Ty - from.Ty) * amount, to.Inliers, to.Matches);
}
