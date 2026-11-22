using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AionMeter.Core.Gear;
using AionMeter.Core.Pets;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;

namespace Aurum;

internal sealed class ArcanaScreenReader
{
    private sealed record Sample(CvRect Bounds, byte[] Pixels, byte Threshold)
    {
        internal static Sample From(Mat gray, CvRect bounds)
        {
            using var crop = new Mat(gray, bounds).Clone(); var bytes = new byte[bounds.Width * bounds.Height]; Marshal.Copy(crop.Data, bytes, 0, bytes.Length);
            return new(bounds, bytes, (byte)Math.Clamp(bytes.Max() * .72, 45, 150));
        }
        internal bool Matches(Mat gray)
        {
            if (Bounds.Right > gray.Width || Bounds.Bottom > gray.Height) return false;
            var current = From(gray, Bounds).Pixels; long delta = 0; var ink = 0; var changed = 0;
            for (var i = 0; i < current.Length; i++)
            { delta += Math.Abs(current[i] - Pixels[i]); var a = Pixels[i] >= Threshold; var b = current[i] >= Threshold; if (a || b) ink++; if (a != b) changed++; }
            return ink > 0 && delta <= current.Length * 10L && changed <= ink * .08;
        }
    }
    private sealed record Layout(int Width, int Height, Sample[] Header, Sample[] Body, ArcanaPanel? Panel, string HeaderText);
    private readonly WindowsTextReader _ocr = new();
    private volatile Layout? _layout;
    private int _ocrBusy, _cacheHits, _reads;
    private long _nextOcr, _lastOcrMs, _pollMs;
    private volatile bool _disposed;
    private string _stage = "Idle", _error = "";
    private IReadOnlyList<ShopText> _lastRows = [];
    internal event Action? Updated;
    internal object DiagnosticState => new { Stage = _stage, Available = _ocr.Available, OcrBusy = Volatile.Read(ref _ocrBusy) != 0,
        CacheHits = _cacheHits, OcrReads = _reads, OcrMilliseconds = _lastOcrMs, PollMilliseconds = _pollMs, Error = _error };
    internal ArcanaPanel? Poll(GameViewport game)
    {
        var clock = Stopwatch.StartNew(); var headerArea = HeaderArea(game.Width, game.Height);
        using var header = Capture(game, headerArea); using var grayHeader = Gray(header); var saved = _layout;
        var headerMatches = saved != null && saved.Width == game.Width && saved.Height == game.Height
            && saved.Header.Length > 0 && saved.Header.All(s => s.Matches(grayHeader));
        if (headerMatches)
        {
            using var body = Capture(game, BodyArea(game.Width, game.Height)); using var grayBody = Gray(body);
            if (saved!.Panel != null && saved.Body.Length > 0 && saved.Body.All(s => s.Matches(grayBody)))
            { _stage = "ImageCache"; _cacheHits++; _pollMs = clock.ElapsedMilliseconds; return saved.Panel; }
            _stage = "CardChanged"; Queue(game, header, body, saved!.HeaderText);
        }
        else { _stage = "NotArcanaOrOpening"; Queue(game, header, null, null); }
        _pollMs = clock.ElapsedMilliseconds; return null;
    }
    private void Queue(GameViewport game, Mat header, Mat? body, string? knownHeader)
    {
        if (_disposed || !_ocr.Available || Environment.TickCount64 < _nextOcr || Interlocked.CompareExchange(ref _ocrBusy, 1, 0) != 0) return;
        var headerCopy = header.Clone(); var bodyCopy = body?.Clone();
        _ = Task.Run(async () => {
            try
            {
                using (headerCopy) using (bodyCopy) await Learn(game.Width, game.Height, headerCopy, bodyCopy, knownHeader,
                    () => Capture(game, BodyArea(game.Width, game.Height)));
                _error = "";
            }
            catch (Exception error) { _error = error.GetType().Name + ": " + error.Message; }
            finally { _nextOcr = Environment.TickCount64 + 300; Interlocked.Exchange(ref _ocrBusy, 0); if (!_disposed) Updated?.Invoke(); }
        });
    }
    private async Task<ArcanaPanel?> Learn(int width, int height, Mat header, Mat? body, string? knownHeader, Func<Mat> captureBody)
    {
        var clock = Stopwatch.StartNew(); _reads++;
        var headerRows = await _ocr.Read(header, Math.Clamp(2160d / height, .5, 3));
        var headerText = string.Join(" ", headerRows.Select(r => r.Text));
        if (!ArcanaPanelParser.IsHeader(headerText)) { _lastOcrMs = clock.ElapsedMilliseconds; return null; }
        using var captured = body == null ? captureBody() : null;
        var source = body ?? captured!;
        IReadOnlyList<ShopText> rows = await _ocr.Read(source, Math.Clamp(1728d / height, .6, 3));
        var parsed = ArcanaPanelParser.Parse(rows);
        if (parsed == null)
        {
            rows = await _ocr.Read(source, Math.Clamp(2160d / height, .7, 3.5));
            parsed = ArcanaPanelParser.Parse(rows);
        }
        if (parsed?.Skills.Any(s => s.Bonus == null) == true)
        {
            var equipped = rows.First(r => ItemTooltipParser.Normalize(r.Text).Contains("equippedeffect") || ItemTooltipParser.Normalize(r.Text).Contains("eguippedeffect"));
            var set = rows.First(r => ItemTooltipParser.Normalize(r.Text).Contains("seteffect"));
            var x = (int)(source.Width * .62); var y = Math.Max(0, (int)(equipped.Y + equipped.Height + 5));
            var area = new CvRect(x, y, source.Width - x, Math.Max(1, (int)(set.Y - y)));
            using var column = new Mat(source, area); using var gray = Gray(column); using var binary = new Mat(); using var numeric = new Mat();
            Cv2.Threshold(gray, binary, 130, 255, ThresholdTypes.BinaryInv); Cv2.CvtColor(binary, numeric, ColorConversionCodes.GRAY2BGRA);
            var levels = await _ocr.Read(numeric, Math.Clamp(3240d / height, 1, 5));
            rows = rows.Concat(levels.Select(r => r with { X = r.X + area.X, Y = r.Y + area.Y })).ToArray();
            parsed = ArcanaPanelParser.Parse(rows);
        }
        _lastRows = rows;
        var panel = parsed == null ? null : parsed with { SetBottom = parsed.SetBottom + BodyArea(width, height).Y };
        using var grayHeader = Gray(header); using var grayBody = Gray(source);
        if (!_disposed) _layout = new(width, height, Samples(grayHeader, headerRows), Samples(grayBody, rows), panel, headerText);
        _lastOcrMs = clock.ElapsedMilliseconds; return panel;
    }
    private static Sample[] Samples(Mat gray, IReadOnlyList<ShopText> rows) => rows.Where(r => r.Width >= 8 && r.Height >= 5).Take(48).Select(r => {
        var x = Math.Clamp((int)r.X - 1, 0, gray.Width - 1); var y = Math.Clamp((int)r.Y - 1, 0, gray.Height - 1);
        return Sample.From(gray, new(x, y, Math.Clamp((int)Math.Ceiling(r.Width) + 8, 1, gray.Width - x), Math.Clamp((int)Math.Ceiling(r.Height) + 3, 1, gray.Height - y)));
    }).ToArray();
    internal static CvRect HeaderArea(int width, int height)
    {
        var scale = height / 1080d; var x = Math.Max(0, (int)(width / 2d - 400 * scale)); var y = (int)(48 * scale);
        return new(x, y, Math.Min(width - x, (int)(800 * scale)), Math.Min(height - y, (int)(64 * scale)));
    }
    internal static CvRect BodyArea(int width, int height)
    {
        var scale = height / 1080d; var x = Math.Max(0, width - (int)(430 * scale)); var y = (int)(140 * scale);
        return new(x, y, Math.Min(width - x, (int)(415 * scale)), Math.Min(height - y, (int)(790 * scale)));
    }
    private static Mat Capture(GameViewport game, CvRect area)
    {
        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(game.X + area.X, game.Y + area.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { using var pixels = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, data.Scan0, data.Stride); return pixels.Clone(); }
        finally { bitmap.UnlockBits(data); }
    }
    private static Mat Gray(Mat frame) { var gray = new Mat(); Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY); return gray; }
    internal async Task<object> CheckFile(string path)
    {
        using var image = Cv2.ImRead(path, ImreadModes.Color); using var frame = new Mat(); Cv2.CvtColor(image, frame, ColorConversionCodes.BGR2BGRA);
        using var header = new Mat(frame, HeaderArea(frame.Width, frame.Height)); using var body = new Mat(frame, BodyArea(frame.Width, frame.Height));
        var panel = await Learn(frame.Width, frame.Height, header, body, null, () => body.Clone());
        var saved = _layout; using var headerGray = Gray(header); using var bodyGray = Gray(body);
        var cached = saved != null && saved.Header.All(s => s.Matches(headerGray)) && saved.Body.All(s => s.Matches(bodyGray));
        using var closed = Mat.Zeros(headerGray.Size(), MatType.CV_8UC1).ToMat();
        var closedHidden = saved == null || !saved.Header.All(s => s.Matches(closed));
        using var changed = bodyGray.Clone();
        if (saved?.Body.FirstOrDefault() is { } title) Cv2.Rectangle(changed, title.Bounds, Scalar.White, -1);
        var changedHidden = saved == null || !saved.Body.All(s => s.Matches(changed));
        return new { Panel = panel, CacheMatches = cached, ClosedHidden = closedHidden, ChangedHidden = changedHidden, Rows = _lastRows, DiagnosticState };
    }
    internal ArcanaPanel? LearnedPanel => _layout?.Panel;
    internal void Dispose() { _disposed = true; Updated = null; }
}
