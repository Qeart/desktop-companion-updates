using AionMeter.Core.PetMaps;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace Aurum;

public sealed record MapFlow(MapMotion Motion, Point2f[] Points, double Confidence, double ResidualPixels,
    double ForwardBackwardError, string Method);

public sealed class PetMapOpticalTracker : IDisposable
{
    private Mat _previous = new();
    private Point2f[] _points = [];
    private int _frames;
    public bool Ready => !_previous.Empty();
    public void Reset() { _previous.Dispose(); _previous = new(); _points = []; _frames = 0; }
    public void Seed(Mat frame, Mat mask, Point2f[]? verified = null)
    {
        frame.CopyTo(_previous); _frames++;
        _points = verified ?? [];
        if (_points.Length < 120 || _frames % 15 == 0) Replenish(frame, mask);
    }
    public MapFlow? Track(Mat frame, Mat mask)
    {
        if (!Ready || _previous.Size() != frame.Size() || _points.Length < 18) return null;
        var next = _points.ToArray();
        Cv2.CalcOpticalFlowPyrLK(_previous, frame, _points, ref next, out var status, out var errors,
            new Size(25, 25), 3, new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 20, .01));
        var back = _points.ToArray();
        Cv2.CalcOpticalFlowPyrLK(frame, _previous, next, ref back, out var backStatus, out _,
            new Size(25, 25), 3, new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 15, .02));
        var indices = Enumerable.Range(0, _points.Length).Where(i => status[i] != 0 && backStatus[i] != 0 && errors[i] < 24
            && Inside(next[i], mask) && Length(back[i], _points[i]) < .8).ToArray();
        if (indices.Length < 18) return TryFeatures(frame, mask);
        var result = Estimate(indices.Select(i => _points[i]).ToArray(), indices.Select(i => next[i]).ToArray(), frame.Size(), "OpticalFlow");
        if (result == null) return TryFeatures(frame, mask);
        return result with { ForwardBackwardError = indices.Average(i => Length(back[i], _points[i])) };
    }
    private MapFlow? TryFeatures(Mat frame, Mat mask)
    {
        // Rapid zoom can exceed LK's convergence range. This inexpensive local match
        // bridges it without waiting for a match against the world-map reference.
        using var orb = ORB.Create(1400);
        using var before = new Mat(); using var after = new Mat();
        orb.DetectAndCompute(_previous, mask, out var oldKeys, before);
        orb.DetectAndCompute(frame, mask, out var keys, after);
        if (before.Empty() || after.Empty()) return null;
        using var matcher = new BFMatcher(NormTypes.Hamming);
        var matches = matcher.KnnMatch(before, after, 2).Where(p => p.Length == 2 && p[0].Distance < .72 * p[1].Distance)
            .Select(p => p[0]).GroupBy(p => p.TrainIdx).Select(g => g.MinBy(p => p.Distance)).ToArray();
        return Estimate(matches.Select(m => oldKeys[m.QueryIdx].Pt).ToArray(), matches.Select(m => keys[m.TrainIdx].Pt).ToArray(), frame.Size(), "LocalFeatures");
    }
    private static MapFlow? Estimate(Point2f[] from, Point2f[] to, Size size, string method)
    {
        if (from.Length < 18) return null;
        using var source = Mat.FromArray(from); using var target = Mat.FromArray(to); using var inliers = new Mat();
        using var fit = Cv2.EstimateAffinePartial2D(source, target, inliers, RobustEstimationAlgorithms.RANSAC, 1.25, 800, .995, 5);
        if (fit == null || fit.Empty() || inliers.Empty()) return null;
        var indices = Enumerable.Range(0, from.Length).Where(i => inliers.At<byte>(i) != 0).ToArray();
        if (indices.Length < 18 || indices.Length < from.Length * (method == "OpticalFlow" ? .65 : .45)) return null;
        var retained = indices.Select(i => to[i]).ToArray();
        if (retained.Max(p => p.X) - retained.Min(p => p.X) < size.Width * .20
            || retained.Max(p => p.Y) - retained.Min(p => p.Y) < size.Height * .20
            || retained.Select(p => ((int)(p.X * 4 / size.Width), (int)(p.Y * 3 / size.Height))).Distinct().Count() < 4) return null;
        if (Math.Abs(Math.Atan2(fit.At<double>(1, 0), fit.At<double>(0, 0))) > .02) return null;
        // The game map does not rotate. Refit uniform scale and translation to remove
        // the small spurious rotation that otherwise accumulates during long drags.
        var px = indices.Average(i => from[i].X); var py = indices.Average(i => from[i].Y);
        var qx = indices.Average(i => to[i].X); var qy = indices.Average(i => to[i].Y);
        var denominator = indices.Sum(i => Math.Pow(from[i].X - px, 2) + Math.Pow(from[i].Y - py, 2));
        if (denominator < 1) return null;
        var scale = indices.Sum(i => (from[i].X - px) * (to[i].X - qx) + (from[i].Y - py) * (to[i].Y - qy)) / denominator;
        var tx = qx - scale * px; var ty = qy - scale * py;
        var residual = indices.Average(i => Math.Sqrt(Math.Pow(scale * from[i].X + tx - to[i].X, 2) + Math.Pow(scale * from[i].Y + ty - to[i].Y, 2)));
        if (scale is < .45 or > 2.2 || residual > .8 || Math.Abs(tx) > size.Width || Math.Abs(ty) > size.Height) return null;
        var centreMovement = Math.Sqrt(Math.Pow((scale - 1) * size.Width / 2 + tx, 2) + Math.Pow((scale - 1) * size.Height / 2 + ty, 2));
        var motion = new MapMotion(scale, tx, ty);
        if (Math.Abs(scale - 1) < .00005 && centreMovement < .04) { motion = MapMotion.Identity; method = "Stationary"; }
        return new(motion, retained, indices.Length / (double)from.Length, residual, 0, method);
    }
    public void Commit(Mat frame, Mat mask, MapFlow flow)
    {
        // Retain the image anchor for subpixel stationary noise. Real slow motion
        // therefore accumulates against the anchor instead of being discarded.
        if (flow.Method != "Stationary") Seed(frame, mask, flow.Points);
    }
    private void Replenish(Mat frame, Mat mask)
    {
        using var available = mask.Clone();
        foreach (var point in _points) if (Inside(point, available)) Cv2.Circle(available, (Point)point, 9, Scalar.Black, -1);
        var extra = Cv2.GoodFeaturesToTrack(frame, 240, .015, 10, available, 5, false, .04);
        _points = _points.Concat(extra).Take(240).ToArray();
    }
    private static bool Inside(Point2f p, Mat mask) => p.X >= 14 && p.Y >= 14 && p.X < mask.Width - 14 && p.Y < mask.Height - 14 && mask.At<byte>((int)p.Y, (int)p.X) != 0;
    private static double Length(Point2f a, Point2f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    public void Dispose() => _previous.Dispose();
}
