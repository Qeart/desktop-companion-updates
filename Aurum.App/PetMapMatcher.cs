using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AionMeter.Core.PetMaps;
using OpenCvSharp;
using Drawing = System.Drawing;
using Size = OpenCvSharp.Size;

namespace Aurum;

public sealed record MapTrackingDiagnostics(string Method, double DeltaX, double DeltaY, double ZoomRatio,
    double Confidence, double ResidualPixels, double ForwardBackwardError, string CalibrationState,
    bool CalibrationBusy, long CalibrationMilliseconds, double CalibrationErrorPixels, double CorrectionPixels,
    int MotionEpoch, int RejectedCalibrations, int FailedCalibrations, string? CalibrationError);
public sealed record PetMapMatch(PetMapAlignment? Alignment, long Milliseconds, int FrameFeatures, bool Tracked = false,
    long CaptureMilliseconds = 0, long TotalMilliseconds = 0, string CaptureBackend = "File", bool NewFrame = true,
    MapTrackingDiagnostics? Diagnostics = null, double SourceFrameAgeMilliseconds = 0, string? CaptureError = null);

public sealed class PetMapMatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly PetMapCalibrator _calibrator;
    private readonly PetMapOpticalTracker _tracker = new();
    private MapPoseStabilizer _pose = new(1920, 1080);
    private Task<MapCalibration>? _calibration;
    private MapMotion _calibrationMotion = MapMotion.Identity;
    private int _session = -1, _jobSession, _motionEpoch, _jobEpoch, _rejected, _failedCalibrations;
    private int _width, _height;
    private long _nextCalibration, _lastFrame;
    private MapCalibration? _lastCalibration;
    private MapFlow? _lastFlow;
    private DxgiMapCapture? _gpuCapture;
    private long _gpuRetryAfter;
    private string? _captureError;
    private bool _disposed;
    public PetMapMatcher(PetMapCatalog catalog, string pictureDirectory)
    {
        Cv2.SetNumThreads(2); _calibrator = new PetMapCalibrator(catalog, pictureDirectory);
    }
    public PetMapMatch Read(int x, int y, int width, int height, int preferredMap, int session = 0, IReadOnlyList<Rect>? exclusions = null)
    {
        lock (_gate)
        {
            if (_disposed) return new(null, 0, 0);
            EnsureSession(width, height, session);
            if (Environment.TickCount64 >= _gpuRetryAfter)
            {
                var watch = Stopwatch.StartNew();
                Mat? gray = null; var fresh = false;
                try
                {
                    if (_gpuCapture != null && (_gpuCapture.X != x || _gpuCapture.Y != y || _gpuCapture.Width != width || _gpuCapture.Height != height))
                    { _gpuCapture.Dispose(); _gpuCapture = null; Reset(); }
                    _gpuCapture ??= new DxgiMapCapture(x, y, width, height);
                    gray = _gpuCapture.Read(out fresh); _captureError = null;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    _captureError = error.ToString();
                    _gpuCapture?.Dispose(); _gpuCapture = null; _gpuRetryAfter = Environment.TickCount64 + 3000;
                }
                if (gray != null)
                    using (gray)
                    {
                        var captureMs = watch.ElapsedMilliseconds;
                        var fit = Match(gray, preferredMap, 1, fresh, exclusions);
                        return fit with { CaptureMilliseconds = captureMs, TotalMilliseconds = watch.ElapsedMilliseconds, CaptureBackend = "DirectX", NewFrame = fresh,
                            SourceFrameAgeMilliseconds = _gpuCapture!.FrameTimestamp > 0 ? Math.Max(0, Stopwatch.GetElapsedTime(_gpuCapture.FrameTimestamp).TotalMilliseconds) : 0 };
                    }
            }
            return ReadGdi(x, y, width, height, preferredMap, exclusions) with { CaptureError = _captureError };
        }
    }
    private void EnsureSession(int width, int height, int session)
    {
        if (session == _session && width == _width && height == _height) return;
        _session = session; _width = width; _height = height; _pose = new(width, height); Reset();
    }
    private void Reset()
    {
        _tracker.Reset(); _pose.Reset(); _motionEpoch++; _nextCalibration = 0;
        _lastFrame = 0; _lastFlow = null; _failedCalibrations = 0;
    }
    private PetMapMatch ReadGdi(int x, int y, int width, int height, int preferredMap, IReadOnlyList<Rect>? exclusions)
    {
        var watch = Stopwatch.StartNew();
        using var bitmap = CaptureForTracking(x, y, width, height);
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        using var gray = new Mat();
        try
        {
            using var view = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
            Cv2.CvtColor(view, gray, ColorConversionCodes.BGRA2GRAY);
        }
        finally { bitmap.UnlockBits(data); }
        var captureMs = watch.ElapsedMilliseconds;
        var result = Match(gray, preferredMap, bitmap.Width / (double)width, true, exclusions);
        return result with { CaptureMilliseconds = captureMs, TotalMilliseconds = watch.ElapsedMilliseconds, CaptureBackend = "GDI" };
    }
    // Synchronous first acquisition is available only for offline diagnostic tools.
    // Live Read always starts calibration in the background and returns immediately.
    public PetMapMatch ReadFile(string file, int preferredMap, bool waitForCalibration = true, int session = 0)
    {
        lock (_gate)
        {
            using var gray = Cv2.ImRead(file, ImreadModes.Grayscale);
            EnsureSession(gray.Width, gray.Height, session);
            return Match(gray, preferredMap, 1, true, null, waitForCalibration);
        }
    }
    public PetMapMatch AnalyzeFrame(Mat grayscale, int preferredMap, bool waitForCalibration = false, int session = 0, IReadOnlyList<Rect>? exclusions = null)
    {
        lock (_gate)
        {
            if (_disposed) return new(null, 0, 0);
            EnsureSession(grayscale.Width, grayscale.Height, session);
            return Match(grayscale, preferredMap, 1, true, exclusions, waitForCalibration);
        }
    }
    private PetMapMatch Match(Mat full, int preferredMap, double inputScale, bool fresh, IReadOnlyList<Rect>? exclusions, bool synchronousFirst = false)
    {
        var watch = Stopwatch.StartNew();
        var roi = new Rect((int)(full.Width * .18), (int)(full.Height * .105), (int)(full.Width * .64), (int)(full.Height * .84));
        var resize = Math.Min(1.0, 960.0 / full.Width); var factor = resize * inputScale;
        var offsetX = roi.X / inputScale; var offsetY = roi.Y / inputScale;
        using var crop = new Mat(full, roi); using var frame = new Mat();
        Cv2.Resize(crop, frame, new Size(), resize, resize, InterpolationFlags.Area);
        using var mask = new Mat(frame.Size(), MatType.CV_8UC1, Scalar.White);
        Cv2.Rectangle(mask, new Rect(0, 0, frame.Width, frame.Height), Scalar.Black, 24);
        // Exclude bright map annotations/icons; UI outside the map was cropped out.
        using var annotations = new Mat(); Cv2.InRange(frame, new Scalar(215), new Scalar(255), annotations);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7));
        Cv2.Dilate(annotations, annotations, kernel); mask.SetTo(Scalar.Black, annotations);
        if (exclusions != null)
            foreach (var rectangle in exclusions)
            {
                var rect = new Rect((int)((rectangle.X - offsetX) * factor), (int)((rectangle.Y - offsetY) * factor),
                    Math.Max(1, (int)Math.Ceiling(rectangle.Width * factor)), Math.Max(1, (int)Math.Ceiling(rectangle.Height * factor)));
                rect = rect.Intersect(new Rect(0, 0, frame.Width, frame.Height));
                if (rect.Width > 0 && rect.Height > 0) Cv2.Rectangle(mask, rect, Scalar.Black, -1);
            }
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastFrame == 0 ? .016 : Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        var motion = MapMotion.Identity;
        if (fresh)
        {
            var ready = _tracker.Ready;
            _lastFlow = _tracker.Track(frame, mask);
            if (_lastFlow != null)
            {
                var local = _lastFlow.Motion;
                motion = new(local.Scale, local.X / factor + offsetX * (1 - local.Scale), local.Y / factor + offsetY * (1 - local.Scale));
                _calibrationMotion = _calibrationMotion.Follow(motion);
                _tracker.Commit(frame, mask, _lastFlow);
            }
            else
            {
                if (ready) { _pose.Reset(); _motionEpoch++; _nextCalibration = 0; }
                _tracker.Seed(frame, mask);
            }
        }
        _pose.Advance(motion, elapsed);
        ConsumeCalibration();
        if (_calibration == null && Environment.TickCount64 >= _nextCalibration)
        {
            if (synchronousFirst && _pose.Current == null)
            {
                _lastCalibration = _calibrator.Match(frame, mask, preferredMap, factor, offsetX, offsetY);
                if (_lastCalibration.Alignment != null) _pose.Calibrate(_lastCalibration.Alignment);
                _nextCalibration = Environment.TickCount64 + 2500;
            }
            else StartCalibration(frame, mask, preferredMap, factor, offsetX, offsetY);
        }
        var diagnostics = new MapTrackingDiagnostics(fresh ? _lastFlow?.Method ?? "Acquiring" : "NoNewFrame",
            motion.X, motion.Y, motion.Scale, _lastFlow?.Confidence ?? 0, _lastFlow?.ResidualPixels ?? 0,
            _lastFlow?.ForwardBackwardError ?? 0, _pose.CalibrationState, _calibration != null,
            _lastCalibration?.Milliseconds ?? 0, Finite(_pose.CalibrationErrorPixels), _pose.CorrectionPixels,
            _motionEpoch, _rejected, _failedCalibrations, _lastCalibration?.Error);
        return new(_pose.Current, watch.ElapsedMilliseconds, _lastFlow?.Points.Length ?? 0, _lastFlow != null,
            NewFrame: fresh, Diagnostics: diagnostics);
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : -1;
    private void StartCalibration(Mat frame, Mat mask, int preferredMap, double factor, double offsetX, double offsetY)
    {
        var snapshot = frame.Clone(); var savedMask = mask.Clone();
        _calibrationMotion = MapMotion.Identity; _jobEpoch = _motionEpoch; _jobSession = _session;
        _calibration = Task.Factory.StartNew(() =>
        {
            using (snapshot) using (savedMask)
                try {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                    return _calibrator.Match(snapshot, savedMask, preferredMap, factor, offsetX, offsetY);
                }
                catch (Exception error) { return new MapCalibration(null, 0, 0, error.ToString()); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    private void ConsumeCalibration()
    {
        if (_calibration is not { IsCompletedSuccessfully: true }) return;
        _lastCalibration = _calibration.Result; _calibration = null;
        if (_jobEpoch != _motionEpoch || _jobSession != _session)
        { _rejected++; _nextCalibration = 0; return; }
        if (_lastCalibration.Alignment != null)
        {
            _pose.Calibrate(_calibrationMotion.Apply(_lastCalibration.Alignment)); _failedCalibrations = 0;
        }
        else if (++_failedCalibrations >= 2) { _pose.Reset(); _tracker.Reset(); _motionEpoch++; }
        _nextCalibration = Environment.TickCount64 + (_pose.Current == null || _pose.CalibrationState == "AwaitingConfirmation" ? 150 : 2500);
    }
    private static Drawing.Bitmap CaptureForTracking(int x, int y, int width, int height)
    {
        var scale = Math.Min(1.0, 960.0 / width);
        var target = new Drawing.Bitmap(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)), PixelFormat.Format32bppArgb);
        var copied = false;
        try
        {
            using var graphics = Drawing.Graphics.FromImage(target);
            var screen = GetDC(0); var destination = graphics.GetHdc();
            try
            {
                if (screen != 0)
                {
                    SetStretchBltMode(destination, 4); SetBrushOrgEx(destination, 0, 0, 0);
                    copied = StretchBlt(destination, 0, 0, target.Width, target.Height, screen, x, y, width, height, 0x00CC0020);
                }
            }
            finally { graphics.ReleaseHdc(destination); if (screen != 0) ReleaseDC(0, screen); }
        }
        catch { target.Dispose(); throw; }
        if (copied) return target;
        target.Dispose();
        var fallback = new Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
        try { using var graphics = Drawing.Graphics.FromImage(fallback); graphics.CopyFromScreen(x, y, 0, 0, fallback.Size); return fallback; }
        catch { fallback.Dispose(); throw; }
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(nint dc, int x, int y, nint oldPoint);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            _calibration?.GetAwaiter().GetResult();
            _calibrator.Dispose(); _tracker.Dispose(); _gpuCapture?.Dispose();
        }
    }
}
