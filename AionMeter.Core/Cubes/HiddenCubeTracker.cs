using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.PetMaps;

namespace AionMeter.Core.Cubes;

public sealed record HiddenCubeTarget(uint Id, double Metres, double Bearing, double HeightMetres);

// Shares Aurum's event stream and exact local-player position. No second capture session.
public sealed class HiddenCubeTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, (double X, double Y, double Z)> _cubes = new();
    private uint _self;
    private int _map;
    public int Count { get { lock (_gate) return _cubes.Count; } }
    public void Raw(long time, ushort opcode, ReadOnlySpan<byte> body)
    {
        if (opcode == 0x2136 || opcode == 0x2336 && body.Length >= 18 && body[0] == 0)
            lock (_gate) _cubes.Clear();
    }
    public void Process(GameEvent ev)
    {
        lock (_gate)
        {
            switch (ev)
            {
                case ZoneChangedEvent zone: _cubes.Clear(); _map = zone.MapId; break;
                case SelfIdentifiedEvent self:
                    if (_self != 0 && self.ActorId != _self) _cubes.Clear();
                    _self = self.ActorId; break;
                case WorldObjectSeenEvent cube when GameData.HiddenCubeCodes.Contains(cube.Code):
                    if (Valid(cube.X) && Valid(cube.Y) && Valid(cube.Z) && (cube.X != 0 || cube.Y != 0 || cube.Z != 0))
                    {
                        if (_cubes.Count >= 512 && !_cubes.ContainsKey(cube.ActorId)) _cubes.Clear();
                        _cubes[cube.ActorId] = (cube.X / 100d, cube.Y / 100d, cube.Z / 100d);
                    }
                    break;
                case WorldObjectGoneEvent gone: _cubes.Remove(gone.ActorId); break;
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
            HiddenCubeTarget? nearest = null;
            foreach (var (id, cube) in _cubes)
            {
                var dx = cube.X - player.XMetres; var dy = cube.Y - player.YMetres;
                var metres = Math.Sqrt(dx * dx + dy * dy);
                if (metres > nearbyMetres || nearest != null && nearest.Metres <= metres) continue;
                nearest = new(id, metres, Navigation.Normalize(Math.Atan2(dx, -dy) * 180 / Math.PI), cube.Z - player.HeightMetres);
            }
            return nearest;
        }
    }
}
