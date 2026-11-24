using System.Text.Json;
using System.Threading.Channels;

namespace AionMeter.Core.Bosses;

public sealed class BossJournal : IDisposable
{
    private readonly Channel<BossLogRecord> _queue = Channel.CreateBounded<BossLogRecord>(new BoundedChannelOptions(2048) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _writer;
    private long _dropped;
    public long Dropped => Interlocked.Read(ref _dropped);
    public string DirectoryPath { get; }
    public BossJournal(string directory)
    {
        DirectoryPath = directory; _writer = Task.Run(Write);
    }
    public void Add(BossLogRecord record) { if (!_queue.Writer.TryWrite(record)) Interlocked.Increment(ref _dropped); }
    private async Task Write()
    {
        var streams = new Dictionary<Guid, (StreamWriter Writer, int Count)>();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            foreach (var file in new DirectoryInfo(DirectoryPath).EnumerateFiles("boss-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Skip(99))
                try { file.Delete(); } catch (IOException) { }
            await foreach (var record in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    if (!streams.TryGetValue(record.SessionId, out var entry))
                    {
                        if (streams.Count >= 16) { Interlocked.Increment(ref _dropped); continue; }
                        entry = (new StreamWriter(new FileStream(Path.Combine(DirectoryPath, "boss-" + record.SessionId.ToString("N") + ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read, 16384, true)), 0);
                        streams[record.SessionId] = entry;
                    }
                    if (entry.Count < 12000) { await entry.Writer.WriteLineAsync(JsonSerializer.Serialize(record, BossJson.Options)); entry.Count++; }
                    else Interlocked.Increment(ref _dropped);
                    if (entry.Count % 16 == 0) await entry.Writer.FlushAsync();
                    if (record.Signal.Kind is BossSignalKind.Death or BossSignalKind.Reset) { await entry.Writer.DisposeAsync(); streams.Remove(record.SessionId); }
                    else streams[record.SessionId] = entry;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref _dropped);
                    if (streams.Remove(record.SessionId, out var failed)) try { await failed.Writer.DisposeAsync(); } catch (IOException) { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref _dropped); }
        finally { foreach (var entry in streams.Values) try { await entry.Writer.DisposeAsync(); } catch (IOException) { } }
    }
    public static IReadOnlyList<BossLogRecord> Read(string file, bool allowIncompleteTail = false)
    {
        if (new FileInfo(file).Length > 8_000_000) throw new InvalidDataException("Dziennik jest zbyt duży");
        var rows = new List<BossLogRecord>(); var line = 0;
        // A running journal is shared with the background writer. Never wait on capture.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } text)
        {
            line++; if (line > 20000 || text.Length > 8192) throw new InvalidDataException("Przekroczono limit dziennika");
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var row = JsonSerializer.Deserialize<BossLogRecord>(text, BossJson.Options);
                if (row is not { SchemaVersion: 1, Signal: { At: >= 0, Actor: > 0 } signal } || row.SessionId == Guid.Empty || !Enum.IsDefined(signal.Kind) ||
                    signal.NpcCode == 0 || signal.BossName == null || signal.SkillName == null || signal.BossName.Length > 160 || signal.SkillName.Length > 160) throw new JsonException();
                rows.Add(row);
            }
            catch (JsonException) { if (allowIncompleteTail && reader.EndOfStream) break; throw new InvalidDataException("Nieprawidłowy dziennik, wiersz " + line); }
        }
        return rows.OrderBy(r => r.Signal.At).ToArray();
    }
    public void Dispose() { _queue.Writer.TryComplete(); try { _writer.Wait(2000); } catch (AggregateException) { } }
}

public sealed class BossReplay
{
    private readonly BossAssistantEngine _engine;
    private readonly BossSignal[] _events;
    private int _next;
    public long Now { get; private set; }
    public long StartedAt { get; }
    public long EndsAt { get; }
    public bool Complete => _next == _events.Length && Now >= EndsAt;
    public BossReplay(BossAssistantEngine engine, IEnumerable<BossSignal> signals)
    {
        _engine = engine; _events = signals.OrderBy(s => s.At).Take(20000).ToArray();
        StartedAt = _events.FirstOrDefault()?.At ?? 0; Now = StartedAt; EndsAt = (_events.LastOrDefault()?.At ?? Now) + 5000;
        _engine.Reset(Now, "Replay start");
    }
    public void AdvanceTo(long time)
    {
        if (time < Now) throw new ArgumentOutOfRangeException(nameof(time), "Replay nie cofa zegara");
        while (_next < _events.Length && _events[_next].At <= time)
        { var next = _events[_next++]; _engine.Tick(next.At); _engine.Process(next); }
        Now = time; _engine.Tick(Now);
    }
}
