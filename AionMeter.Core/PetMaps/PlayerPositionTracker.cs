using System.Buffers.Binary;
using AionMeter.Core.Events;
using AionMeter.Core.Protocol;

namespace AionMeter.Core.PetMaps;

public sealed record PlayerMapPosition(int MapId, double XMetres, double YMetres, double HeightMetres, long At, string Source,
    double Confidence = 1, bool IsEstimated = false)
{
    public long Age(long now) => Math.Max(0, now - At);
    public bool Fresh(long now) => At <= now && Age(now) <= 3000;
}

public enum PositionState { Live, Estimated, Stale }
public sealed record PositionTrackingOptions
{
    public int LiveMilliseconds { get; init; } = 1000;
    public int StaleMilliseconds { get; init; } = 3000;
    public int MaxPredictionMilliseconds { get; init; } = 2000;
    public int MinVelocitySampleMilliseconds { get; init; } = 100;
    public int MaxVelocitySampleMilliseconds { get; init; } = 2000;
    public double MaxSpeedMetresPerSecond { get; init; } = 35;
}
public sealed record PositionTrackingSnapshot(PlayerMapPosition? Confirmed, PlayerMapPosition? Position,
    PositionState State, long LastReliableAt, long AgeMilliseconds, double VelocityX, double VelocityY,
    bool HasVelocity, int PredictionMilliseconds);

public sealed class PlayerPositionTracker
{
    private readonly object _gate = new();
    private uint _self;
    private int _map;
    private PlayerMapPosition? _position;
    private long _stateAt;
    private double _vx, _vy;
    private bool _hasVelocity;
    private long _accepted, _rejected;
    private string? _lastRejection;
    private readonly Dictionary<string, long> _sources = new();
    private readonly PositionTrackingOptions _options;
    public PlayerPositionTracker(PositionTrackingOptions? options = null)
    {
        var value = options ?? new();
        var live = Math.Clamp(value.LiveMilliseconds, 0, 2000);
        _options = value with { LiveMilliseconds = live, StaleMilliseconds = Math.Clamp(value.StaleMilliseconds, live + 1, 10_000),
            MaxPredictionMilliseconds = Math.Clamp(value.MaxPredictionMilliseconds, 0, 2000),
            MinVelocitySampleMilliseconds = Math.Clamp(value.MinVelocitySampleMilliseconds, 50, 1000),
            MaxVelocitySampleMilliseconds = Math.Clamp(value.MaxVelocitySampleMilliseconds, 1000, 3000),
            MaxSpeedMetresPerSecond = double.IsFinite(value.MaxSpeedMetresPerSecond) ? Math.Clamp(value.MaxSpeedMetresPerSecond, 1, 60) : 35 };
    }
    public PlayerMapPosition? Snapshot() { lock (_gate) return _position; }
    public PositionTrackingSnapshot Tracking(long now)
    {
        lock (_gate)
        {
            var age = _position?.Age(now) ?? long.MaxValue;
            var validTime = _position != null && _position.At <= now;
            var state = validTime && age <= _options.LiveMilliseconds ? PositionState.Live
                : validTime && age <= _options.StaleMilliseconds && _hasVelocity && _options.MaxPredictionMilliseconds > 0
                    ? PositionState.Estimated : PositionState.Stale;
            // Total displacement is capped at two seconds from the LAST confirmed fix, not two
            // seconds added after LIVE. Between the cap and STALE the estimate stays frozen.
            var horizon = validTime && _hasVelocity ? (int)Math.Min(age, _options.MaxPredictionMilliseconds) : 0;
            // When moving, predict from the first render after the fix, rather than jumping
            // by one second of velocity at the LIVE boundary. Every such prediction is
            // labelled ESTIMATED, even inside the normal 0–1 s freshness window.
            if (state == PositionState.Live && horizon > 0 && (_vx != 0 || _vy != 0)) state = PositionState.Estimated;
            if (state == PositionState.Live) horizon = 0;
            var point = validTime ? _position : null;
            if (point != null && horizon > 0 && state != PositionState.Live)
                point = point with { XMetres = point.XMetres + _vx * horizon / 1000d, YMetres = point.YMetres + _vy * horizon / 1000d,
                    Source = "DeadReckoning:" + point.Source, IsEstimated = true,
                    Confidence = state == PositionState.Stale ? 0 : point.Confidence * Math.Max(0, 1 - age / (double)_options.StaleMilliseconds) };
            else if (point != null && state == PositionState.Stale) point = point with { Confidence = 0 };
            return new(_position, point, state, _position?.At ?? 0, age, _vx, _vy, _hasVelocity, horizon);
        }
    }
    public object Diagnostics(long now)
    {
        lock (_gate) return new { Tracking = Tracking(now), State = Tracking(now).State.ToString(), Accepted = _accepted,
            Rejected = _rejected, LastRejection = _lastRejection, Sources = new Dictionary<string, long>(_sources), Options = _options };
    }
    public void SeedIdentity(uint actor, int map)
    {
        lock (_gate) { if (_map != map || _self != actor) Reset(); _self = actor; _map = map; }
    }
    private void Reset() { _position = null; StopVelocity(); }
    private void StopVelocity() { _hasVelocity = false; _vx = _vy = 0; }
    public void Process(GameEvent ev)
    {
        if (ev is not (SelfIdentifiedEvent or ZoneChangedEvent or LocalIdentityLostEvent)) return;
        lock (_gate)
        {
            if (ev.TimeMs < _stateAt) return;
            _stateAt = ev.TimeMs;
            if (ev is LocalIdentityLostEvent)
            {
                _self = 0; StopVelocity();
                // Parser.Tap runs before MapLoad emits identity-lost + zone events. Keep the
                // validated map position from that same frame, never one from an old character.
                if (_position is not { Source: "MapLoad" } || _position.At != ev.TimeMs) _position = null;
            }
            if (ev is SelfIdentifiedEvent self)
            {
                if (_self != 0 && self.ActorId != _self) Reset();
                _self = self.ActorId;
            }
            if (ev is ZoneChangedEvent zone)
            {
                if (_map != zone.MapId || zone.IsTeleport && _position?.At != zone.TimeMs) Reset();
                if (zone.IsTeleport) StopVelocity();
                _map = zone.MapId;
            }
        }
    }
    public void Raw(long at, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (opcode is not (0x2136 or 0x2336 or 0x2A38 or 0x2B38)) return;
        lock (_gate)
        {
            if (at < _stateAt || _position?.At > at) { Reject("OlderFrame"); return; }
            if (opcode == 0x2136)
            {
                if (body.Length < 28 || ReadPoint(body, 16) == null) return;
                var map = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                if (map is <= 0 or > 9_999_999) return;
                Reset(); _self = 0; _stateAt = at;
                _map = map; Place(body, 16, at, "MapLoad");
            }
            else if (opcode == 0x2336 && body.Length >= 18 && body[0] == 0)
            {
                if (ReadPoint(body, 6) is { } teleport)
                    Observe(new(_map, teleport.X / 100d, teleport.Y / 100d, teleport.Z / 100d, at, "Teleport"), discontinuity: true);
            }
            else if (ReadEffect(opcode, body, _self) is { } point)
                Set(point.X, point.Y, point.Z, at, "OwnEffect");
        }
    }
    private void Place(ReadOnlySpan<byte> body, int offset, long at, string source)
    {
        if (ReadPoint(body, offset) is { } point) Set(point.X, point.Y, point.Z, at, source);
    }
    private void Set(float x, float y, float z, long at, string source)
    {
        Observe(new(_map, x / 100d, y / 100d, z / 100d, at, source));
    }
    private bool Reject(string reason) { _rejected++; _lastRejection = reason; return false; }
    /// <summary>Only confirmed, decoded fixes enter here. Predictions are never fed back as measurements.</summary>
    public bool Observe(PlayerMapPosition point, bool discontinuity = false)
    {
        lock (_gate)
        {
            if (point.IsEstimated || string.IsNullOrWhiteSpace(point.Source) || !double.IsFinite(point.Confidence) || point.Confidence is <= 0 or > 1 || point.At < 0 ||
                !double.IsFinite(point.XMetres) || !double.IsFinite(point.YMetres) || !double.IsFinite(point.HeightMetres) ||
                Math.Abs(point.XMetres) >= 20_000 || Math.Abs(point.YMetres) >= 20_000 || Math.Abs(point.HeightMetres) >= 20_000)
                return Reject("InvalidFix");
            if (_map <= 0 || point.MapId != _map) return Reject("MapMismatch");
            if (point.At < _stateAt || _position?.At > point.At) return Reject("OlderFix");
            if (discontinuity) { StopVelocity(); _stateAt = point.At; }
            else if (_position is { } previous)
            {
                var dt = point.At - previous.At;
                if (dt == 0) return point == previous; // no same-timestamp velocity or alternate overwrite
                var dx = point.XMetres - previous.XMetres; var dy = point.YMetres - previous.YMetres;
                var dz = point.HeightMetres - previous.HeightMetres;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Sqrt(distance * distance + dz * dz) > _options.MaxSpeedMetresPerSecond * dt / 1000d + 2) return Reject("ImplausibleSpeed");
                StopVelocity();
                if (dt >= _options.MinVelocitySampleMilliseconds && dt <= _options.MaxVelocitySampleMilliseconds &&
                    Math.Min(point.Confidence, previous.Confidence) >= .8 &&
                    distance / (dt / 1000d) <= _options.MaxSpeedMetresPerSecond)
                { _vx = dx * 1000d / dt; _vy = dy * 1000d / dt; _hasVelocity = true; }
            }
            _position = point; _accepted++; _sources[point.Source] = _sources.GetValueOrDefault(point.Source) + 1;
            return true;
        }
    }
    // Layouts from A2Tools-Plugins pet-souls and the tested CubeArrow decoder (GPL-3.0).
    public static (float X, float Y, float Z)? ReadEffect(ushort opcode, ReadOnlySpan<byte> body, uint self)
    {
        if (self == 0 || opcode is not (0x2A38 or 0x2B38)) return null;
        var o = 0;
        if (!Wire.TryVarint(body, ref o, out var source) || source != self) return null;
        if (opcode == 0x2A38)
        {
            if (o + 2 > body.Length || body[o++] != 1) return null;
            var kind = body[o++];
            if (!Wire.TryVarint(body, ref o, out _)) return null;
            o += 20;
            if (o >= body.Length || !Wire.TryVarint(body, ref o, out var target) || target != self || o + 2 > body.Length || body[o++] != 1) return null;
            if (kind == 0x13) o += 4; else if (kind != 0x11) return null;
            if (o >= body.Length || body[o++] != 0) return null;
        }
        else
        {
            if (o >= body.Length) return null;
            var kind = body[o++];
            if (kind is not (0x11 or 0x13) || !Wire.TryVarint(body, ref o, out _)) return null;
            o += 20;
            if (o >= body.Length || !Wire.TryVarint(body, ref o, out var target) || target != self) return null;
            o += kind == 0x13 ? 5 : 1;
            if (o >= body.Length || body[o++] != 2) return null;
        }
        return ReadPoint(body, o);
    }
    private static (float X, float Y, float Z)? ReadPoint(ReadOnlySpan<byte> body, int offset)
    {
        if (offset < 0 || offset + 12 > body.Length) return null;
        var x = BinaryPrimitives.ReadSingleLittleEndian(body[offset..]);
        var y = BinaryPrimitives.ReadSingleLittleEndian(body[(offset + 4)..]);
        var z = BinaryPrimitives.ReadSingleLittleEndian(body[(offset + 8)..]);
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z)
            && Math.Abs(x) < 2_000_000 && Math.Abs(y) < 2_000_000 && Math.Abs(z) < 2_000_000 && (x != 0 || y != 0 || z != 0) ? (x, y, z) : null;
    }
}
