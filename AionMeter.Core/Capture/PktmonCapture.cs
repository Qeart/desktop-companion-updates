using System.Runtime.InteropServices;
using System.Threading.Channels;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Capture;

/// <summary>Private live PktMon session. No pktmon.exe commands, ETL files or pcap DLL calls.</summary>
public sealed class PktmonCapture : IEventSource
{
    private readonly PacketPipeline _pipeline;
    private readonly ushort _port;
    private readonly PktmonNative.DataCallback _dataCallback;
    private readonly PktmonNative.EventCallback _eventCallback;
    private readonly object _lifecycle = new();
    private Channel<(long Time, ushort Type, byte[] Packet)>? _queue;
    private nint _monitor, _session, _stream;
    private CancellationTokenSource? _cancel;
    private Task? _consumer;
    private volatile bool _accepting;
    private readonly object _pipeGate = new();
    private CaptureStatus _status = CaptureStatus.Stopped;
    private long _packets, _events, _missed, _unsupported, _lastPacket;
    private long _inbound, _outbound, _lagMs, _maxLagMs;
    private long _streamWarnings, _oversizePackets;
    private int _lastStreamWarning;
    private int _lastStreamError;
    public PacketPipeline Pipeline => _pipeline;
    public long MissedPackets => Interlocked.Read(ref _missed);
    public long UnsupportedPackets => Interlocked.Read(ref _unsupported);
    public int StreamError => Volatile.Read(ref _lastStreamError);
    public object Statistics()
    {
        lock (_pipeGate) return new
        {
            Backend = "PktMon API", Status, MissedPackets, UnsupportedPackets, StreamError,
            GameFlows = _pipeline.GameFlows, _pipeline.GameBytes, _pipeline.Gaps,
            DamageRecords = _pipeline.Parser.DamageRecords, Heartbeats = _pipeline.Parser.Heartbeats,
            InboundSegments = _inbound, OutboundSegments = _outbound,
            ConsumerLagMilliseconds = Interlocked.Read(ref _lagMs), MaxConsumerLagMilliseconds = Interlocked.Read(ref _maxLagMs),
            TruncationSize = 9000,
            Pipeline = _pipeline.Diagnostics,
            StreamWarnings = Interlocked.Read(ref _streamWarnings), OversizePackets = Interlocked.Read(ref _oversizePackets),
            LastStreamWarning = Volatile.Read(ref _lastStreamWarning),
        };
    }
    public CaptureStatus Status
    {
        get
        {
            var status = _status with { Packets = Interlocked.Read(ref _packets), Events = Interlocked.Read(ref _events) };
            return status.State == CaptureState.Capturing && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _pipeline.LastGameDataMs > 10000
                ? status with { State = CaptureState.WaitingForGame, Message = "PktMon: czekam na ruch gry." } : status;
        }
    }
    public event Action<GameEvent>? EventDecoded;
    // Both TCP directions, after IP/TCP validation and before the server-only application decoder.
    // Diagnostic subscribers must be bounded and nonblocking. Outbound bytes are never assumed plaintext.
    public event Action<SegmentReader.Segment>? SegmentObserved;
    public event Action<CaptureStatus>? StatusChanged;

    public PktmonCapture(GameData data, ushort gamePort = PacketPipeline.GamePort)
    {
        _port = gamePort;
        _pipeline = new PacketPipeline(data, e => { Interlocked.Increment(ref _events); EventDecoded?.Invoke(e); }) { LockThreshold = 3 };
        _dataCallback = OnData; _eventCallback = OnStreamEvent;
    }

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_monitor != 0) return;
            SetStatus(CaptureState.Starting, "PktMon: uruchamianie własnej sesji…");
            try
            {
                Check(PktmonNative.PacketMonitorInitialize(0x00010000, 0, out _monitor), "Initialize");
                Check(PktmonNative.PacketMonitorCreateLiveSession(_monitor, "Aurum-" + Guid.NewGuid().ToString("N"), out _session), "CreateSession");
                var filter = PktmonNative.Constraint.Game(_port);
                Check(PktmonNative.PacketMonitorAddCaptureConstraint(_session, in filter), "SetFilter");
                AddSources();
                _queue = Channel.CreateBounded<(long, ushort, byte[])>(new BoundedChannelOptions(8192)
                    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
                _cancel = new CancellationTokenSource();
                _consumer = Task.Run(() => Consume(_cancel.Token));
                var config = new PktmonNative.StreamConfig
                {
                    DataCallback = Marshal.GetFunctionPointerForDelegate(_dataCallback),
                    EventCallback = Marshal.GetFunctionPointerForDelegate(_eventCallback),
                    BufferMultiplier = 10, TruncationSize = 9000,
                };
                Check(PktmonNative.PacketMonitorCreateRealtimeStream(_monitor, in config, out _stream), "CreateStream");
                Check(PktmonNative.PacketMonitorAttachOutputToSession(_session, _stream), "AttachStream");
                _accepting = true;
                Check(PktmonNative.PacketMonitorSetSessionActive(_session, true), "Activate");
                SetStatus(CaptureState.WaitingForGame, $"PktMon: nasłuch TCP {_port} (bez Npcap)");
            }
            catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                CleanupNative();
                _queue?.Writer.TryComplete(); _cancel?.Cancel();
                var denied = ex.HResult == unchecked((int)0x80070005);
                SetStatus(CaptureState.Error, denied ? "PktMon wymaga uruchomienia jako administrator (UAC)."
                    : "PktMon: " + ex.Message);
            }
        }
    }

    private void AddSources()
    {
        _ = PktmonNative.PacketMonitorEnumDataSources(_monitor, 0, false, 0, out var needed, 0);
        if (needed is 0 or > 4_194_304) throw new InvalidOperationException("Nieprawidłowa lista źródeł PktMon.");
        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            Check(PktmonNative.PacketMonitorEnumDataSources(_monitor, 0, false, needed, out _, buffer), "EnumSources");
            var count = Marshal.ReadInt32(buffer);
            var start = IntPtr.Size; // UINT32 followed by pointer alignment
            if (count < 1 || count > 4096 || (long)start + count * IntPtr.Size > (long)needed)
                throw new InvalidOperationException("Nieprawidłowe źródła PktMon.");
            var added = 0;
            for (var i = 0; i < count; i++)
            {
                var source = Marshal.ReadIntPtr(buffer, start + i * IntPtr.Size);
                if (source != 0 && PktmonNative.PacketMonitorAddSingleDataSourceToSession(_session, source) >= 0) added++;
            }
            if (added == 0) throw new InvalidOperationException("Żadne źródło PktMon nie jest dostępne.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void OnData(nint context, nint pointer)
    {
        // Never let managed exceptions cross the native callback boundary.
        try
        {
            if (!_accepting) return;
            var descriptor = Marshal.PtrToStructure<PktmonNative.DataDescriptor>(pointer);
            Interlocked.Add(ref _missed, descriptor.MissedWrites + (long)descriptor.MissedReads);
            if (descriptor.Data == 0 || descriptor.PacketLength is 0 or > 9000 ||
                descriptor.PacketOffset > descriptor.DataSize || descriptor.PacketLength > descriptor.DataSize - descriptor.PacketOffset ||
                descriptor.MetadataOffset > descriptor.DataSize || Marshal.SizeOf<PktmonNative.Metadata>() > descriptor.DataSize - descriptor.MetadataOffset)
            { Interlocked.Increment(ref _unsupported); return; }
            var metadata = Marshal.PtrToStructure<PktmonNative.Metadata>(descriptor.Data + checked((int)descriptor.MetadataOffset));
            if (metadata.DropReason != 0 || metadata.PacketType is not (1 or 3)) return;
            var packet = new byte[descriptor.PacketLength];
            Marshal.Copy(descriptor.Data + checked((int)descriptor.PacketOffset), packet, 0, packet.Length);
            var unix = metadata.Timestamp / 10_000 - 11_644_473_600_000L;
            var time = unix is > 1_600_000_000_000 and < 3_000_000_000_000 ? unix : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_queue?.Writer.TryWrite((time, metadata.PacketType, packet)) != true) Interlocked.Increment(ref _missed);
        }
        catch (Exception) { Interlocked.Increment(ref _unsupported); }
    }

    private void OnStreamEvent(nint context, nint information, int kind)
    {
        try
        {
            if (kind == 2) // PACKETMONITOR_STREAM_STOP_INFO_OUT: fatal byte, DWORD reason
            {
                var code = information == 0 ? -1 : Marshal.ReadInt32(information, 4);
                Volatile.Write(ref _lastStreamError, code);
                SetStatus(CaptureState.Error, $"PktMon: błąd strumienia ({code}).");
            }
            else if (kind == 3)
            {
                var code = information == 0 ? -1 : Marshal.ReadInt32(information, 4);
                Interlocked.Increment(ref _streamWarnings); Volatile.Write(ref _lastStreamWarning, code);
                // PROCESS_INFO is a warning/error, not a count of missed packets. Code 122
                // specifically reports packets above the API's 9000-byte internal buffer.
                if (code == 122) Interlocked.Increment(ref _oversizePackets);
            }
        }
        catch (Exception) { Volatile.Write(ref _lastStreamError, -1); }
    }

    private async Task Consume(CancellationToken token)
    {
        try
        {
            await foreach (var item in _queue!.Reader.ReadAllAsync(token))
            {
                Interlocked.Increment(ref _packets);
                if (!RawTcpReader.TryRead(item.Packet, item.Type == 1, item.Time, out var segment))
                { Interlocked.Increment(ref _unsupported); continue; }
                if (segment.Key.SrcPort != _port && segment.Key.DstPort != _port) continue;
                var lag = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - segment.TimeMs);
                Interlocked.Exchange(ref _lagMs, lag);
                if (lag > Interlocked.Read(ref _maxLagMs)) Interlocked.Exchange(ref _maxLagMs, lag);
                if (segment.Key.SrcPort == _port) Interlocked.Increment(ref _inbound);
                else Interlocked.Increment(ref _outbound);
                SegmentObserved?.Invoke(segment);
                if (segment.Key.SrcPort != _port) continue;
                lock (_pipeGate)
                {
                _pipeline.OnSegment(segment.TimeMs, segment.Key, segment.Seq, segment.Syn, segment.FinOrRst, segment.Payload);
                if (item.Time - _lastPacket >= 500)
                {
                    _lastPacket = item.Time;
                    if (StreamError == 0)
                        SetStatus(_pipeline.GameFlows > 0 ? CaptureState.Capturing : CaptureState.WaitingForGame,
                            $"PktMon · {_pipeline.GameFlows} połączeń gry · utracone {MissedPackets}");
                }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus(CaptureState.Error, "PktMon: " + ex.Message); }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0) throw new COMException($"{operation}: 0x{result:X8}", result);
    }
    private void SetStatus(CaptureState state, string message)
    {
        _status = new(state, message); StatusChanged?.Invoke(Status);
    }
    private void CleanupNative()
    {
        _accepting = false;
        if (_session != 0) _ = PktmonNative.PacketMonitorSetSessionActive(_session, false);
        if (_stream != 0) { _ = PktmonNative.PacketMonitorCloseRealtimeStream(_stream); _stream = 0; }
        if (_session != 0) { _ = PktmonNative.PacketMonitorCloseSessionHandle(_session); _session = 0; }
        if (_monitor != 0) { _ = PktmonNative.PacketMonitorUninitialize(_monitor); _monitor = 0; }
        GC.KeepAlive(_dataCallback); GC.KeepAlive(_eventCallback);
    }
    public void Stop()
    {
        lock (_lifecycle)
        {
            CleanupNative(); _queue?.Writer.TryComplete(); _cancel?.Cancel();
            try { _consumer?.Wait(2000); } catch (AggregateException) { }
            _consumer = null;
            SetStatus(CaptureState.Stopped, "PktMon: własna sesja zamknięta.");
        }
    }
    public void Dispose() { Stop(); _cancel?.Dispose(); }
}
