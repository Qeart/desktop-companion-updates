using AionMeter.Core.Cubes;
using AionMeter.Core.PetMaps;

namespace AionMeter.Core.Pets;

public sealed record PetTrackerOptions(bool Automatic = true, bool CurrentRegionOnly = true, IReadOnlyList<int>? SelectedPetIds = null);
public sealed record PetTrackerDistance(double Metres, double Bearing, bool Estimated);
public sealed record PetTrackerEntry(PetDefinition Pet, PetState State, int MapId, PetTrackerDistance? Distance)
{
    public int Remaining => State.Remaining;
}
public sealed record PetTrackerView(bool Synced, bool FromCache, int MapId, string Region, bool HasSpawnData,
    IReadOnlyList<PetTrackerEntry> Entries, int? Remaining);

/// <summary>Projects the shared pet collection into a live, shrinking farming list. No independent soul counter.</summary>
public sealed class PetTracker
{
    private readonly PetDefinition[] _pets;
    private readonly PetMapCatalog _maps;
    private readonly Dictionary<(int Map, int Pet), (double X, double Y)[]> _points = new();
    public IReadOnlyList<PetDefinition> Pets => _pets;
    public PetTracker(IEnumerable<PetDefinition> pets, PetMapCatalog maps)
    {
        _pets = pets.GroupBy(p => p.Id).Select(g => g.First()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _maps = maps;
        foreach (var map in maps.Maps)
        foreach (var pet in _pets)
        {
            var points = new HashSet<(double X, double Y)>();
            foreach (var code in pet.Drops.Select(d => d.NpcCode).Distinct())
                if (map.Spawns.TryGetValue(code, out var coordinates))
                    for (var i = 0; i + 1 < coordinates.Length; i += 2)
                    {
                        var x = coordinates[i]; var y = coordinates[i + 1];
                        if (double.IsFinite(x) && double.IsFinite(y) && x * 100 >= map.Bounds[0] && x * 100 <= map.Bounds[2]
                            && y * 100 >= map.Bounds[1] && y * 100 <= map.Bounds[3]) points.Add((x, y));
                    }
            if (points.Count > 0) _points[(map.Id, pet.Id)] = points.ToArray();
        }
    }
    public bool HasSpawns(int map, int pet) => _points.ContainsKey((map, pet));
    public PetTrackerView Build(PetProgressSnapshot collection, PetTrackerOptions options, int mapId, PositionTrackingSnapshot? tracking = null)
    {
        var map = _maps.Map(mapId); var region = map?.Name ?? "Nieznana kraina";
        var hasSpawns = _points.Keys.Any(k => k.Map == mapId);
        if (!collection.Synced) return new(false, collection.FromCache, mapId, region, hasSpawns, [], null);
        var selected = (options.SelectedPetIds ?? []).Distinct().Take(512).ToArray();
        var order = selected.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var choices = _pets.Where(p => options.Automatic || order.ContainsKey(p.Id))
            .Where(p => !options.CurrentRegionOnly || HasSpawns(mapId, p.Id));
        if (!options.Automatic) choices = choices.OrderBy(p => order[p.Id]);
        var rows = new List<PetTrackerEntry>();
        foreach (var pet in choices)
        {
            var state = collection.Pets.GetValueOrDefault(pet.Id, new(0, 0));
            if (state.Level is < 0 or >= 3 || state.Points < 0 || state.Points >= PetState.Costs[state.Level]) continue;
            // Distances are to catalogued spawn points, not live monster entities. A stale
            // player point must not turn into a seemingly current navigation reading.
            PetTrackerDistance? distance = null;
            if (tracking is { State: not PositionState.Stale, Position: { } player } && player.MapId == mapId &&
                _points.TryGetValue((mapId, pet.Id), out var points))
            {
                var nearest = points.MinBy(p => (p.X - player.XMetres) * (p.X - player.XMetres) + (p.Y - player.YMetres) * (p.Y - player.YMetres));
                var dx = nearest.X - player.XMetres; var dy = nearest.Y - player.YMetres;
                distance = new(Math.Sqrt(dx * dx + dy * dy), Navigation.Normalize(Math.Atan2(dx, -dy) * 180 / Math.PI), tracking.State == PositionState.Estimated);
            }
            var destination = HasSpawns(mapId, pet.Id) ? mapId : _maps.Maps.FirstOrDefault(m => HasSpawns(m.Id, pet.Id))?.Id ?? 0;
            rows.Add(new(pet, state, destination, distance));
        }
        return new(true, collection.FromCache, mapId, region, hasSpawns, rows, rows.Sum(r => r.Remaining));
    }
}
