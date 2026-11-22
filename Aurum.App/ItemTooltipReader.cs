using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AionMeter.Core.Gear;
using AionMeter.Core.Pets;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;

namespace Aurum;

internal sealed record ItemTooltipReading(ItemTooltip Item, CvRect Bounds, IReadOnlyList<ShopText> Rows, long Milliseconds, string Mode, bool ComparisonRight = false);

internal sealed class ItemTooltipReader
{
    private sealed record Stamp(CvRect Bounds, byte[] Pixels)
    {
        internal static Stamp Create(Mat gray, CvRect bounds)
        {
            using var crop = new Mat(gray, bounds).Clone(); var bytes = new byte[bounds.Width * bounds.Height];
            Marshal.Copy(crop.Data, bytes, 0, bytes.Length); return new(bounds, bytes);
        }
        internal bool Matches(Mat gray)
        {
            if (Bounds.X < 0 || Bounds.Y < 0 || Bounds.Right > gray.Width || Bounds.Bottom > gray.Height) return false;
            var current = Create(gray, Bounds).Pixels; long difference = 0; var changed = 0;
            for (var i = 0; i < current.Length; i++) { var delta = Math.Abs(current[i] - Pixels[i]); difference += delta; if (delta > 40) changed++; }
            return difference <= current.Length * 7L && changed < current.Length * .10;
        }
    }
    private sealed record Cache(ItemTooltipReading Reading, Stamp[] Stamps, int Width, int Height, int HoverX, int HoverY, int Generation, long At);
    private readonly WindowsTextReader _ocr = new();
    private Cache? _cache;
    private int _cacheHits, _reads;
    private int _generation;
    public bool Available => _ocr.Available;
    internal int OcrReads => _reads;
    internal void Invalidate() => Interlocked.Increment(ref _generation);
    public object DiagnosticState => new { Available, CacheHits = _cacheHits, OcrReads = _reads, LastSlot = _cache?.Reading.Item.Slot.ToString(), ComparisonRight = _cache?.Reading.ComparisonRight };
    internal async Task<ItemTooltipReading?> Read(GameViewport game, int cursorX, int cursorY)
    {
        var clock = Stopwatch.StartNew(); var scale = game.Height / 1080d; var generation = Volatile.Read(ref _generation);
        if (_cache is { } saved && saved.Generation == generation && saved.Width == game.Width && saved.Height == game.Height
            && (saved.Reading.ComparisonRight || Environment.TickCount64 - saved.At < 250)
            && Math.Abs(saved.HoverX - cursorX) <= 32 * scale && Math.Abs(saved.HoverY - cursorY) <= 32 * scale)
        {
            using var current = Capture(game, saved.Reading.Bounds); using var gray = Gray(current);
            if (saved.Stamps.Length >= 2 && saved.Stamps.All(s => s.Matches(gray)))
            { _cacheHits++; return saved.Reading with { Milliseconds = clock.ElapsedMilliseconds, Mode = "ImageCache" }; }
        }
        var width = Math.Min(game.Width, (int)(1540 * scale));
        var left = Math.Clamp(cursorX - (int)(1200 * scale), 0, game.Width - width);
        var region = new CvRect(left, 0, width, game.Height);
        using var frame = Capture(game, region);
        var reading = await ReadFrame(frame, cursorX - region.X, cursorY, scale);
        if (reading == null) { _cache = null; return null; }
        var absolute = reading.Bounds with { X = reading.Bounds.X + region.X };
        var result = reading with { Bounds = absolute, Milliseconds = clock.ElapsedMilliseconds };
        _cache = new(result, MakeStamps(frame, reading), game.Width, game.Height, cursorX, cursorY, generation, Environment.TickCount64);
        return result;
    }
    internal async Task<ItemTooltipReading?> ReadFile(string path)
    {
        using var color = Cv2.ImRead(path, ImreadModes.Color); using var image = new Mat(); Cv2.CvtColor(color, image, ColorConversionCodes.BGR2BGRA);
        return await ReadFrame(image, image.Width - 80, image.Height * 3 / 4, 1);
    }
    private async Task<ItemTooltipReading?> ReadFrame(Mat frame, int cursorX, int cursorY, double scale)
    {
        var clock = Stopwatch.StartNew();
        var candidates = Candidates(frame, cursorX, cursorY, scale);
        var preferred = candidates.Where(r => HasCloseCross(frame, r, scale)).ToArray();
        if (preferred.Length > 0) candidates = preferred;
        var choices = TooltipSelection.Order(candidates.Select(r => new TooltipBox(r.X, r.Y, r.Width, r.Height)).ToArray(), cursorX, cursorY, scale);
        var comparison = choices.Any(c => c.ComparisonRight);
        foreach (var choice in choices.Take(5))
        {
            var bounds = candidates[choice.Index];
            using var crop = new Mat(frame, bounds);
            _reads++;
            var rows = await _ocr.Read(crop, Math.Clamp(1.6 / scale, .6, 3));
            if (choice.ComparisonRight && ItemTooltipParser.Parse(rows) == null) rows = await _ocr.Read(crop, Math.Clamp(2.0 / scale, .7, 3));
            if (ItemTooltipParser.Parse(rows) is not { } item) continue;
            return new(item, bounds, rows, clock.ElapsedMilliseconds, choice.ComparisonRight ? "OCR-ComparisonRight" : "OCR", choice.ComparisonRight);
        }
        // A clipped border may not form a contour. Split the OCR at Item Level headings,
        // apply the same comparison selection, and never merge both items' bindings.
        _reads++;
        var allRows = await _ocr.Read(frame, Math.Clamp(1.25 / scale, .5, 2.5));
        var fallback = new List<CvRect>();
        foreach (var level in allRows.Where(r => ItemTooltipParser.IsItemLevel(r.Text)))
        {
            var left = Math.Max(0, (int)(level.X - 18 * scale)); var top = Math.Max(0, (int)(level.Y - 78 * scale));
            var width = Math.Min(frame.Width - left, (int)(365 * scale));
            var bottom = allRows.Where(r => r.Y > level.Y && r.X >= left && r.X < left + width && ItemTooltipParser.Normalize(r.Text).StartsWith("requiredlevel", StringComparison.Ordinal))
                .Select(r => r.Y + 42 * scale).DefaultIfEmpty(frame.Height).Min();
            var bounds = new CvRect(left, top, Math.Max(1, width), Math.Max(1, Math.Min(frame.Height - top, (int)(bottom - top))));
            if (Distance(bounds, cursorX, cursorY) > 950 * scale) continue;
            fallback.Add(bounds);
        }
        var fallbackChoices = TooltipSelection.Order(fallback.Select(r => new TooltipBox(r.X, r.Y, r.Width, r.Height)).ToArray(), cursorX, cursorY, scale);
        foreach (var choice in fallbackChoices)
        {
            var bounds = fallback[choice.Index];
            if (comparison && !choice.ComparisonRight) continue;
            var rows = allRows.Where(r => r.X >= bounds.X && r.X + r.Width <= bounds.Right + 8 * scale && r.Y >= bounds.Y && r.Y < bounds.Bottom)
                .Select(r => r with { X = r.X - bounds.X, Y = r.Y - bounds.Y }).ToArray();
            if (ItemTooltipParser.Parse(rows) is { } item) return new(item, bounds, rows, clock.ElapsedMilliseconds, choice.ComparisonRight ? "OCR-Fallback-ComparisonRight" : "OCR-Fallback", choice.ComparisonRight);
        }
        return null;
    }
    private static IReadOnlyList<CvRect> Candidates(Mat frame, int cursorX, int cursorY, double scale)
    {
        using var gray = Gray(frame); using var edges = new Mat(); Cv2.Canny(gray, edges, 35, 110);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);
        Cv2.FindContours(edges, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
        var boxes = contours.Select(Cv2.BoundingRect).Where(r => r.Width >= 220 * scale && r.Width <= 500 * scale && r.Height >= 170 * scale
            && r.Height <= frame.Height && r.Width < r.Height * 1.8 && Distance(r, cursorX, cursorY) <= 950 * scale)
            .OrderBy(r => Distance(r, cursorX, cursorY) + Math.Abs(r.Width - 355 * scale) * .15).ToArray();
        var result = new List<CvRect>();
        foreach (var box in boxes)
        {
            if (result.Any(old => Intersection(old, box) > Math.Max(old.Width * old.Height, box.Width * box.Height) * .86)) continue;
            var x = Math.Max(0, box.X - 2); var y = Math.Max(0, box.Y - 2);
            result.Add(new(x, y, Math.Min(frame.Width - x, box.Width + 4), Math.Min(frame.Height - y, box.Height + 4)));
        }
        return result;
    }
    private static double Distance(CvRect box, int x, int y)
    {
        var dx = Math.Max(0, Math.Max(box.X - x, x - box.Right)); var dy = Math.Max(0, Math.Max(box.Y - y, y - box.Bottom));
        return Math.Sqrt(dx * dx + dy * dy) + (box.Contains(x, y) ? 150 : 0);
    }
    private static double Intersection(CvRect a, CvRect b) => Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
    private static bool HasCloseCross(Mat frame, CvRect bounds, double scale)
    {
        var cx = (int)Math.Round(bounds.Right - 22 * scale); var cy = (int)Math.Round(bounds.Y + 22 * scale);
        var r = Math.Max(12, (int)Math.Ceiling(16 * scale));
        var x = Math.Max(0, cx - r); var y = Math.Max(0, cy - r);
        var rect = new CvRect(x, y, Math.Min(frame.Width - x, r * 2 + 1), Math.Min(frame.Height - y, r * 2 + 1));
        if (rect.Width < 10 || rect.Height < 10) return false;
        using var crop = new Mat(frame, rect); using var gray = Gray(crop); using var packed = gray.Clone();
        var width = rect.Width; var height = rect.Height; var bytes = new byte[width * height]; Marshal.Copy(packed.Data, bytes, 0, bytes.Length);
        int Pixel(int px, int py) => px < 0 || py < 0 || px >= width || py >= height ? 0 : bytes[py * width + px];
        int Bright(int px, int py)
        {
            var best = 0;
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) best = Math.Max(best, Pixel(px + dx, py + dy));
            return best;
        }
        var diagonalA = 0; var diagonalB = 0; var samples = 0; var reach = Math.Max(6, (int)(9 * scale));
        var localX = cx - rect.X; var localY = cy - rect.Y; var offset = Math.Max(3, (int)(4 * scale));
        for (var i = -reach; i <= reach; i++)
        {
            if (Math.Abs(i) < 2) continue; samples++;
            var a = Bright(localX + i, localY + i); var b = Bright(localX + i, localY - i);
            if (a > 130 && a - Pixel(localX + i + offset, localY + i) > 25) diagonalA++;
            if (b > 130 && b - Pixel(localX + i + offset, localY - i) > 25) diagonalB++;
        }
        return diagonalA >= samples * .45 && diagonalB >= samples * .45;
    }
    private static Stamp[] MakeStamps(Mat frame, ItemTooltipReading reading)
    {
        using var crop = new Mat(frame, reading.Bounds); using var gray = Gray(crop);
        var itemLevel = reading.Rows.FirstOrDefault(r => ItemTooltipParser.IsItemLevel(r.Text));
        return reading.Rows.Where(r => r.Y <= (itemLevel?.Y ?? 100) || reading.Item.BindingLines.Contains(r.Text))
            .Where(r => r.Width >= 15 && r.Height >= 5).Take(18).Select(row => {
                var x = Math.Clamp((int)row.X - 1, 0, gray.Width - 1); var y = Math.Clamp((int)row.Y - 1, 0, gray.Height - 1);
                return Stamp.Create(gray, new(x, y, Math.Clamp((int)Math.Ceiling(row.Width) + 14, 1, gray.Width - x), Math.Clamp((int)Math.Ceiling(row.Height) + 3, 1, gray.Height - y)));
            }).ToArray();
    }
    private static Mat Gray(Mat frame) { var gray = new Mat(); Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY); return gray; }
    private static Mat Capture(GameViewport game, CvRect region)
    {
        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(game.X + region.X, game.Y + region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { using var pixels = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, data.Scan0, data.Stride); return pixels.Clone(); }
        finally { bitmap.UnlockBits(data); }
    }
}
