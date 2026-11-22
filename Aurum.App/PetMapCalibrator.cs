using System.Diagnostics;
using System.IO;
using AionMeter.Core.PetMaps;
using OpenCvSharp;

namespace Aurum;

public sealed record MapCalibration(PetMapAlignment? Alignment, long Milliseconds, int Features, string? Error = null);

// Used only by one background task. Its detector and FLANN indices are never
// shared with the per-frame optical tracker.
public sealed class PetMapCalibrator : IDisposable
{
    private sealed class Reference : IDisposable
    {
        public PetWorldMap Map { get; }
        public KeyPoint[] Keys { get; }
        public Mat Descriptors { get; } = new();
        public FlannBasedMatcher Matcher { get; } = new();
        public Reference(PetWorldMap map, string path, Feature2D detector)
        {
            Map = map; using var image = Cv2.ImRead(path, ImreadModes.Grayscale);
            detector.DetectAndCompute(image, null, out var keys, Descriptors); Keys = keys;
            if (!Descriptors.Empty()) { Matcher.Add([Descriptors]); Matcher.Train(); }
        }
        public void Dispose() { Matcher.Dispose(); Descriptors.Dispose(); }
    }
    private readonly OpenCvSharp.Features2D.SIFT _detector;
    private readonly Reference[] _references;
    public PetMapCalibrator(PetMapCatalog catalog, string directory)
    {
        _detector = OpenCvSharp.Features2D.SIFT.Create(8000, contrastThreshold: .015);
        _references = catalog.Maps.Where(m => m.Spawns.Count > 0 && File.Exists(Path.Combine(directory, m.Id + ".webp")))
            .Select(m => new Reference(m, Path.Combine(directory, m.Id + ".webp"), _detector)).ToArray();
    }
    public MapCalibration Match(Mat frame, Mat mask, int preferredMap, double factor, double offsetX, double offsetY)
    {
        var watch = Stopwatch.StartNew();
        using var descriptors = new Mat(); _detector.DetectAndCompute(frame, mask, out var keys, descriptors);
        if (descriptors.Empty()) return new(null, watch.ElapsedMilliseconds, keys.Length);
        foreach (var reference in _references.OrderBy(r => r.Map.Id == preferredMap ? 0 : 1))
        {
            if (reference.Descriptors.Empty()) continue;
            var pairs = reference.Matcher.KnnMatch(descriptors, 2);
            var good = pairs.Where(p => p.Length == 2 && p[0].Distance < .72 * p[1].Distance).Select(p => p[0])
                .GroupBy(p => p.TrainIdx).Select(g => g.MinBy(m => m.Distance)).ToArray();
            if (good.Length < 24) continue;
            using var from = Mat.FromArray(good.Select(m => reference.Keys[m.TrainIdx].Pt).ToArray());
            using var to = Mat.FromArray(good.Select(m => keys[m.QueryIdx].Pt).ToArray());
            using var inliers = new Mat();
            using var transform = Cv2.EstimateAffinePartial2D(from, to, inliers, RobustEstimationAlgorithms.RANSAC, 2, 2500, .995, 10);
            if (transform == null || transform.Empty() || inliers.Empty()) continue;
            var confirmed = Enumerable.Range(0, good.Length).Where(i => inliers.At<byte>(i) != 0).ToArray();
            if (confirmed.Length < 24 || confirmed.Length < good.Length * .4) continue;
            var points = confirmed.Select(i => keys[good[i].QueryIdx].Pt).ToArray();
            if (points.Max(p => p.X) - points.Min(p => p.X) < frame.Width * .18
                || points.Max(p => p.Y) - points.Min(p => p.Y) < frame.Height * .18) continue;
            // Same non-rotating map model as the fast path; zoom pivot is encoded
            // in translation, so it need not be guessed from the mouse position.
            if (Math.Abs(Math.Atan2(transform.At<double>(1, 0), transform.At<double>(0, 0))) > .02) continue;
            var px = confirmed.Average(i => reference.Keys[good[i].TrainIdx].Pt.X);
            var py = confirmed.Average(i => reference.Keys[good[i].TrainIdx].Pt.Y);
            var qx = points.Average(p => p.X); var qy = points.Average(p => p.Y);
            var denominator = confirmed.Sum(i => Math.Pow(reference.Keys[good[i].TrainIdx].Pt.X - px, 2) + Math.Pow(reference.Keys[good[i].TrainIdx].Pt.Y - py, 2));
            if (denominator < 1) continue;
            var scale = confirmed.Sum(i => (reference.Keys[good[i].TrainIdx].Pt.X - px) * (keys[good[i].QueryIdx].Pt.X - qx)
                + (reference.Keys[good[i].TrainIdx].Pt.Y - py) * (keys[good[i].QueryIdx].Pt.Y - qy)) / denominator;
            var fit = new PetMapAlignment(reference.Map.Id, scale / factor, 0,
                (qx - scale * px) / factor + offsetX, (qy - scale * py) / factor + offsetY, confirmed.Length, good.Length);
            if (fit.IsPlausible) return new(fit, watch.ElapsedMilliseconds, keys.Length);
        }
        return new(null, watch.ElapsedMilliseconds, keys.Length);
    }
    public void Dispose() { foreach (var reference in _references) reference.Dispose(); _detector.Dispose(); }
}
