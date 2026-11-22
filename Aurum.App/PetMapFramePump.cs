using System.Diagnostics;
using OpenCvSharp;

namespace Aurum;

public sealed record MapFrameRequest(int X, int Y, int Width, int Height, int PreferredMap, int Session, Rect[] Exclusions);
public sealed record MapFrameSnapshot(long Sequence, MapFrameRequest Request, PetMapMatch? Match, Exception? Error,
    long StartedAt, long CompletedAt);

// The renderer only reads Latest. It never waits for capture, OpenCV, or the
// shared .NET thread pool, and old frames never queue up behind a UI update.
public sealed class PetMapFramePump : IDisposable
{
    private readonly Func<MapFrameRequest, PetMapMatch> _read;
    private readonly AutoResetEvent _changed = new(false);
    private readonly Thread _thread;
    private MapFrameRequest? _request;
    private MapFrameSnapshot? _latest;
    private volatile bool _stop;
    private long _sequence;
    public MapFrameSnapshot? Latest => Volatile.Read(ref _latest);
    public PetMapFramePump(Func<MapFrameRequest, PetMapMatch> read)
    {
        _read = read;
        _thread = new Thread(Run) { IsBackground = true, Name = "Aurum map capture", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    public void Request(MapFrameRequest request) { Volatile.Write(ref _request, request); _changed.Set(); }
    public void Pause() { Volatile.Write(ref _request, null); _changed.Set(); }
    private void Run()
    {
        while (!_stop)
        {
            var request = Volatile.Read(ref _request);
            if (request == null) { _changed.WaitOne(); continue; }
            var start = Stopwatch.GetTimestamp(); PetMapMatch? match = null; Exception? error = null;
            try { match = _read(request); } catch (Exception exception) { error = exception; }
            var end = Stopwatch.GetTimestamp();
            var current = Volatile.Read(ref _request);
            if (current != null && current.Session == request.Session && current.X == request.X && current.Y == request.Y
                && current.Width == request.Width && current.Height == request.Height)
                Volatile.Write(ref _latest, new(++_sequence, request, match, error, start, end));
            while (!_stop && Stopwatch.GetElapsedTime(start).TotalMilliseconds < 14)
            {
                var latest = Volatile.Read(ref _request);
                if (latest == null || latest.Session != request.Session) break;
                _changed.WaitOne(Math.Max(1, (int)Math.Ceiling(14 - Stopwatch.GetElapsedTime(start).TotalMilliseconds)));
            }
        }
    }
    public void Dispose()
    {
        _stop = true; _changed.Set(); _thread.Join(); _changed.Dispose();
    }
}
