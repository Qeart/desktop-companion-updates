using System.Buffers.Binary;
using AionMeter.Core.Events;
using AionMeter.Core.Protocol;

namespace AionMeter.Core.PetMaps;

public sealed record PlayerMapPosition(int MapId, double XMetres, double YMetres, double HeightMetres, long At, string Source)
{
    public long Age(long now) => Math.Max(0, now - At);
    public bool Fresh(long now) => Age(now) <= 3000;
}

public sealed class PlayerPositionTracker
{
    private readonly object _gate = new();
    private uint _self;
    private int _map;
    private PlayerMapPosition? _position;
    public PlayerMapPosition? Snapshot() { lock (_gate) return _position; }
    public void SeedIdentity(uint actor, int map) { lock (_gate) { _self = actor; _map = map; } }
    public void Process(GameEvent ev)
    {
        if (ev is not (SelfIdentifiedEvent or ZoneChangedEvent or LocalIdentityLostEvent)) return;
        lock (_gate)
        {
            if (ev is LocalIdentityLostEvent) { _self = 0; _position = null; }
            if (ev is SelfIdentifiedEvent self)
            {
                if (_self != 0 && self.ActorId != _self) _position = null;
                _self = self.ActorId;
            }
            if (ev is ZoneChangedEvent zone)
            {
                if (_map != zone.MapId) _position = null;
                _map = zone.MapId;
            }
        }
    }
    public void Raw(long at, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (opcode is not (0x2136 or 0x2336 or 0x2A38 or 0x2B38)) return;
        lock (_gate)
        {
            if (opcode == 0x2136)
            {
                _position = null; _self = 0;
                if (body.Length < 28) return;
                var map = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                if (map <= 0) return;
                _map = map; Place(body, 16, at, "MapLoad");
            }
            else if (opcode == 0x2336 && body.Length >= 18 && body[0] == 0)
                Place(body, 6, at, "Teleport");
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
        if (_map <= 0 || _position?.At > at) return;
        _position = new(_map, x / 100d, y / 100d, z / 100d, at, source);
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
