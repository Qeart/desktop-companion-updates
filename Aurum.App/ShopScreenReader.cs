using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using AionMeter.Core.Pets;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;

namespace Aurum;
public sealed record ShopReading(bool IsShop, IReadOnlyList<ShopText> Cards, long Milliseconds, string Header, string Mode = "OCR");

// Image checks run independently of OCR. An OCR result is usable only when its header
// and every recognised card title still match a fresh frame, including after reopening.
public sealed class ShopScreenReader : IDisposable
{
    private sealed record Patch(CvRect Bounds, byte[] Pixels, byte InkThreshold)
    {
        internal static Patch From(Mat gray, CvRect bounds)
        {
            using var crop = new Mat(gray, bounds).Clone();
            var pixels = new byte[bounds.Width * bounds.Height]; Marshal.Copy(crop.Data, pixels, 0, pixels.Length);
            return new(bounds, pixels, (byte)Math.Clamp(pixels.Max() * .72, 45, 150));
        }
        internal bool Matches(Mat gray, double allowedInkChange)
        {
            if (Bounds.Right > gray.Width || Bounds.Bottom > gray.Height) return false;
            var current = From(gray, Bounds).Pixels; long difference = 0; var changedInk = 0; var ink = 0;
            for (var i = 0; i < current.Length; i++)
            {
                difference += Math.Abs(current[i] - Pixels[i]);
                var before = Pixels[i] >= InkThreshold; var after = current[i] >= InkThreshold;
                if (before || after) ink++;
                if (before != after) changedInk++;
            }
            return ink > 0 && difference <= current.Length * 10L && changedInk <= ink * allowedInkChange;
        }
    }
    private sealed record Layout(int Width, int Height, Patch[] HeaderSamples, Patch[] Titles,
        ShopText[] Cards, string Header, long At, bool Partial);
    private readonly WindowsTextReader _ocr = new();
    private readonly PetDefinition[] _pets;
    private readonly object _captureLock = new();
    private volatile Layout? _layout;
    private DxgiMapCapture? _capture;
    private long _captureRetryAt, _nextOcrAt;
    private int _ocrBusy, _cacheHits, _headerReads, _cardReads;
    private int _scanRequest;
    private long _lastPollMs, _lastOcrMs;
    private volatile bool _disposed;
    private string _backend = "Idle", _captureFailure = "", _ocrError = "";
    public string Stage { get; private set; } = "Idle";
    public string OcrStage { get; private set; } = "Idle";
    public bool Available => _ocr.Available;
    public event Action? Updated;
    public object DiagnosticState => new { Stage, OcrStage, Backend = _backend, OcrBusy = Volatile.Read(ref _ocrBusy) != 0,
        PollMilliseconds = Interlocked.Read(ref _lastPollMs), OcrMilliseconds = Interlocked.Read(ref _lastOcrMs),
        CacheHits = _cacheHits, HeaderReads = _headerReads, CardReads = _cardReads,
        CachedCards = _layout?.Titles.Length ?? 0, CaptureFailure = _captureFailure, OcrError = _ocrError };
    public ShopScreenReader(IEnumerable<PetDefinition>? pets = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "pets.json");
        _pets = pets?.ToArray() ?? (File.Exists(path) ? JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(path)) ?? [] : []);
    }
    public void RequestScan() { Interlocked.Increment(ref _scanRequest); Interlocked.Exchange(ref _nextOcrAt, 0); }

    public ShopReading Poll(int x, int y, int width, int height)
    {
        var watch = Stopwatch.StartNew();
        using var frame = Capture(x, y, width, height);
        var result = PollFrame(frame, true);
        Interlocked.Exchange(ref _lastPollMs, watch.ElapsedMilliseconds);
        return result with { Milliseconds = watch.ElapsedMilliseconds };
    }
    private ShopReading PollFrame(Mat frame, bool allowOcr)
    {
        using var gray = Gray(frame);
        var saved = _layout;
        var headerMatches = saved != null && saved.Width == frame.Width && saved.Height == frame.Height
            && saved.HeaderSamples.Length > 0 && saved.HeaderSamples.All(p => p.Matches(gray, .12));
        var titlesMatch = headerMatches && saved!.Titles.Length > 0 && saved.Titles.All(p => p.Matches(gray, .04));
        if (titlesMatch)
        {
            Stage = "ImageCache"; Interlocked.Increment(ref _cacheHits);
            if (allowOcr && saved!.Partial && Environment.TickCount64 - saved.At > 1500) QueueOcr(frame);
            return new(true, saved!.Cards, 0, saved.Header, "ImageCache");
        }
        Stage = headerMatches ? "CardsChanged" : "NotShopOrOpening";
        if (allowOcr) QueueOcr(frame);
        return new(headerMatches, [], 0, headerMatches ? saved!.Header : "", "ImageCheck");
    }
    private void QueueOcr(Mat frame)
    {
        if (_disposed || !_ocr.Available || Environment.TickCount64 < Interlocked.Read(ref _nextOcrAt)
            || Interlocked.CompareExchange(ref _ocrBusy, 1, 0) != 0) return;
        var copy = frame.Clone();
        var request = Volatile.Read(ref _scanRequest);
        _ = Task.Run(async () =>
        {
            try
            {
                using (copy) await Learn(copy, true);
                _ocrError = "";
            }
            catch (Exception error) { _ocrError = error.GetType().Name + ": " + error.Message; }
            finally
            {
                Interlocked.Exchange(ref _nextOcrAt, request == Volatile.Read(ref _scanRequest) ? Environment.TickCount64 + 250 : 0);
                Interlocked.Exchange(ref _ocrBusy, 0);
                if (!_disposed) Updated?.Invoke();
            }
        });
    }
    public async Task<ShopReading> Read(int x, int y, int width, int height, bool readCards = true)
    {
        using var frame = Capture(x, y, width, height); return await Learn(frame, readCards);
    }
    private async Task<ShopReading> Learn(Mat frame, bool readCards, double cardsFactor = 0)
    {
        var watch = Stopwatch.StartNew();
        var headerArea = HeaderArea(frame.Width, frame.Height);
        using var header = new Mat(frame, headerArea);
        OcrStage = "ReadHeader"; Interlocked.Increment(ref _headerReads);
        var headerRows = await Recognize(header, Math.Clamp(2160d / frame.Height, .5, 3));
        var text = string.Join(" ", headerRows.Select(r => r.Text));
        if (!ShopTextGrouping.IsShopHeader(text))
        {
            OcrStage = "NotShop"; Interlocked.Exchange(ref _lastOcrMs, watch.ElapsedMilliseconds);
            return new(false, [], watch.ElapsedMilliseconds, text);
        }
        if (!readCards) return new(true, [], watch.ElapsedMilliseconds, text);
        var cardsArea = new CvRect((int)(frame.Width * .045), (int)(frame.Height * .20), (int)(frame.Width * .92), (int)(frame.Height * .65));
        using var cards = new Mat(frame, cardsArea);
        OcrStage = "ReadCards"; Interlocked.Increment(ref _cardReads);
        if (cardsFactor <= 0) cardsFactor = Math.Clamp(1350d / frame.Height, .6, 2.5);
        var rows = (await Recognize(cards, cardsFactor)).Select(r => r with { X = r.X + cardsArea.X, Y = r.Y + cardsArea.Y }).ToArray();
        var matched = rows.Where(r => ShopPetMatcher.Match(r.Text, _pets) != null).ToArray();
        using var gray = Gray(frame);
        var headerSamples = headerRows.Where(r => Regex.IsMatch(r.Text, @"\b(Special|Pets|Skins)\b", RegexOptions.IgnoreCase))
            .Select(r => Patch.From(gray, TextBounds(r with { X = r.X + headerArea.X, Y = r.Y + headerArea.Y }, frame.Width, frame.Height))).ToArray();
        var titles = matched.Select(r => Patch.From(gray, TextBounds(r, frame.Width, frame.Height))).ToArray();
        var candidates = Math.Max(rows.Count(r => Regex.IsMatch(r.Text, @"\b[PV]et\s*[:;.]", RegexOptions.IgnoreCase)),
            rows.Count(r => r.Text.Contains("Owned", StringComparison.OrdinalIgnoreCase)));
        if (!_disposed) _layout = new(frame.Width, frame.Height, headerSamples, titles, matched, text, Environment.TickCount64,
            matched.Length < candidates);
        OcrStage = "Complete"; Interlocked.Exchange(ref _lastOcrMs, watch.ElapsedMilliseconds);
        return new(true, matched, watch.ElapsedMilliseconds, text);
    }
    private static CvRect HeaderArea(int width, int height) => new((int)(width * .28), (int)(height * .045), (int)(width * .40), Math.Max(1, (int)(height * .06)));
    private static CvRect TextBounds(ShopText row, int width, int height)
    {
        var x = Math.Clamp((int)Math.Floor(row.X) - 1, 0, width - 1); var y = Math.Clamp((int)Math.Floor(row.Y) - 1, 0, height - 1);
        return new(x, y, Math.Clamp((int)Math.Ceiling(row.Width) + 3, 1, width - x), Math.Clamp((int)Math.Ceiling(row.Height) + 3, 1, height - y));
    }
    private static Mat Gray(Mat frame)
    {
        var gray = new Mat(); Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY); return gray;
    }
    private Mat Capture(int x, int y, int width, int height)
    {
        lock (_captureLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_capture != null && (_capture.X != x || _capture.Y != y || _capture.Width != width || _capture.Height != height))
            { _capture.Dispose(); _capture = null; _captureRetryAt = 0; }
            if (Environment.TickCount64 >= _captureRetryAt)
            {
                try
                {
                    _capture ??= new DxgiMapCapture(x, y, width, height, grayscale: false);
                    var frame = _capture.Read(out _); _backend = "DXGI"; _captureFailure = ""; return frame;
                }
                catch (Exception error)
                {
                    _capture?.Dispose(); _capture = null; _captureRetryAt = Environment.TickCount64 + 10000;
                    _captureFailure = error.GetType().Name + ": " + error.Message;
                }
            }
            _backend = "GDI";
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { using var pixels = Mat.FromPixelData(height, width, MatType.CV_8UC4, data.Scan0, data.Stride); return pixels.Clone(); }
            finally { bitmap.UnlockBits(data); }
        }
    }
    public async Task<IReadOnlyList<ShopText>> ReadFile(string path)
    {
        using var frame = LoadFrame(path); return await Recognize(frame, Math.Min(2, 2200d / Math.Max(frame.Width, frame.Height)));
    }
    internal async Task<ShopReading> ReadFileFrame(string path)
    {
        using var frame = LoadFrame(path); return await Learn(frame, true);
    }
    internal async Task<object> CheckCache(string path)
    {
        using var frame = LoadFrame(path); var first = await Learn(frame, true);
        var times = new List<double>(); ShopReading? cached = null;
        for (var i = 0; i < 12; i++) { var clock = Stopwatch.StartNew(); cached = PollFrame(frame, false); times.Add(clock.Elapsed.TotalMilliseconds); }
        using var closed = frame.Clone(); Cv2.Rectangle(closed, HeaderArea(frame.Width, frame.Height), Scalar.Black, -1);
        var afterClose = PollFrame(closed, false);
        using var changed = frame.Clone();
        if (_layout?.Titles.FirstOrDefault() is { } title) Cv2.Rectangle(changed, title.Bounds, Scalar.White, -1);
        var afterChange = PollFrame(changed, false); var reopened = PollFrame(frame, false);
        using var resized = new Mat(); Cv2.Resize(frame, resized, new OpenCvSharp.Size(frame.Width - 30, frame.Height - 20));
        var afterResize = PollFrame(resized, false);
        var factors = new List<object>();
        foreach (var factor in new[] { 1d, 1.25, 1.5, 1.8 })
        {
            var reading = await Learn(frame, true, factor);
            factors.Add(new { Factor = factor, reading.Milliseconds, Count = reading.Cards.Count, Titles = reading.Cards.Select(r => r.Text) });
        }
        return new { First = first, Cache = cached, CacheMilliseconds = times, ClosedHidden = !afterClose.IsShop && afterClose.Cards.Count == 0,
            ChangedCardsHidden = afterChange.Cards.Count == 0, ReopenedCount = reopened.Cards.Count, ResizedHidden = afterResize.Cards.Count == 0, Factors = factors, DiagnosticState };
    }
    private static Mat LoadFrame(string path)
    {
        using var image = Cv2.ImRead(path, ImreadModes.Color); var frame = new Mat(); Cv2.CvtColor(image, frame, ColorConversionCodes.BGR2BGRA); return frame;
    }
    private Task<IReadOnlyList<ShopText>> Recognize(Mat source, double requestedFactor) => _ocr.Read(source, requestedFactor);
    public void Dispose()
    {
        lock (_captureLock) { _disposed = true; Updated = null; _capture?.Dispose(); _capture = null; }
    }
}

