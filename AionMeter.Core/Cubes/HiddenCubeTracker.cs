using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.PetMaps;

namespace AionMeter.Core.Cubes;

public sealed record HiddenCubeTarget(uint Id, double Metres, double Bearing, double HeightMetres);
public sealed record HiddenCubeNavigation(HiddenCubeTarget Target, PositionState State, double? DistanceMetres,
    double Confidence, long PositionAgeMilliseconds);

// Shares Aurum's event stream and exact local-player position. No second capture session.
public sealed class HiddenCubeTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, (double X, double Y, double Z)> _cubes = new();
    private uint _self;
    private int _map;
    private HiddenCubeTarget? _historical;
    private int _historicalMap;
    public int Count { get { lock (_gate) return _cubes.Count; } }
    public void Raw(long time, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (opcode == 0x2136 || opcode == 0x2336 && body.Length >= 18 && body[0] == 0)
            lock (_gate) { _cubes.Clear(); _historical = null; }
    }
    public void Process(GameEvent ev)
    {
        lock (_gate)
        {
            switch (ev)
            {
                case LocalIdentityLostEvent: _self = 0; _cubes.Clear(); _historical = null; break;
                case ZoneChangedEvent zone: _cubes.Clear(); _historical = null; _map = zone.MapId; break;
                case SelfIdentifiedEvent self:
                    if (_self != 0 && self.ActorId != _self) { _cubes.Clear(); _historical = null; }
                    _self = self.ActorId; break;
                case WorldObjectSeenEvent cube when GameData.HiddenCubeCodes.Contains(cube.Code):
                    if (Valid(cube.X) && Valid(cube.Y) && Valid(cube.Z) && (cube.X != 0 || cube.Y != 0 || cube.Z != 0))
                    {
                        if (_cubes.Count >= 512 && !_cubes.ContainsKey(cube.ActorId)) _cubes.Clear();
                        _cubes[cube.ActorId] = (cube.X / 100d, cube.Y / 100d, cube.Z / 100d);
                    }
                    break;
                case WorldObjectGoneEvent gone:
                    _cubes.Remove(gone.ActorId);
                    if (_historical?.Id == gone.ActorId) _historical = null;
                    break;
            }
        }
    }
    private static bool Valid(float value) => float.IsFinite(value) && Math.Abs(value) < 2_000_000;
    public HiddenCubeTarget? Nearest(PlayerMapPosition? player, long now, double nearbyMetres = 100)
    {
        if (player == null || !player.Fresh(now) || player.At > now + 1000) return null;
        lock (_gate)
        {
            if (_map != 0 && _map != player.MapId) return null;
            return FindNearest(player, nearbyMetres);
        }
    }
    public HiddenCubeNavigation? Navigate(PositionTrackingSnapshot tracking, double nearbyMetres = 100)
    {
        lock (_gate)
        {
            if (tracking.Position is not { } player || _map != 0 && _map != player.MapId)
            { _historical = null; return null; }
            if (tracking.State != PositionState.Stale)
            {
                var nearest = FindNearest(player, nearbyMetres);
                // An uncertain extrapolation crossing the proximity boundary must not
                // erase a cube that was already found from a confirmed nearby fix.
                if (nearest == null && tracking.State == PositionState.Estimated && _historicalMap == player.MapId &&
                    _historical != null && _cubes.TryGetValue(_historical.Id, out var known))
                    nearest = Target(_historical.Id, known, player);
                _historical = nearest; _historicalMap = player.MapId;
            }
            else
            {
                if (_historicalMap != player.MapId) _historical = null;
                if (_historical == null) { _historical = FindNearest(player, nearbyMetres); _historicalMap = player.MapId; }
                if (_historical != null && !_cubes.ContainsKey(_historical.Id)) _historical = null;
            }
            return _historical == null ? null : new(_historical, tracking.State,
                tracking.State == PositionState.Stale ? null : _historical.Metres,
                tracking.State == PositionState.Stale ? 0 : player.Confidence, tracking.AgeMilliseconds);
        }
    }
    private HiddenCubeTarget? FindNearest(PlayerMapPosition player, double nearbyMetres)
    {
            HiddenCubeTarget? nearest = null;
            foreach (var (id, cube) in _cubes)
            {
                var target = Target(id, cube, player);
                if (target.Metres > nearbyMetres || nearest != null && nearest.Metres <= target.Metres) continue;
                nearest = target;
            }
            return nearest;
    }
    private static HiddenCubeTarget Target(uint id, (double X, double Y, double Z) cube, PlayerMapPosition player)
    {
        var dx = cube.X - player.XMetres; var dy = cube.Y - player.YMetres;
        return new(id, Math.Sqrt(dx * dx + dy * dy), Navigation.Normalize(Math.Atan2(dx, -dy) * 180 / Math.PI), cube.Z - player.HeightMetres);
    }
}
