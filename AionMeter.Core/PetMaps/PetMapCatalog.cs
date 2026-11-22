using System.Text.Json;
using AionMeter.Core.Pets;
namespace AionMeter.Core.PetMaps;

public sealed record PetWorldMap(int Id, string Name, int Width, int Height, double[] Bounds, Dictionary<int, double[]> Spawns)
{
    public (double X, double Y) WorldToImage(double xMetres, double yMetres)
        => ((xMetres * 100 - Bounds[0]) / (Bounds[2] - Bounds[0]) * Width,
            (yMetres * 100 - Bounds[1]) / (Bounds[3] - Bounds[1]) * Height);
}
public sealed record PetSpawnArea(double XMetres, double YMetres, double RadiusMetres, int Points);
public sealed record PetMapAlignment(int MapId, double A, double B, double Tx, double Ty, int Inliers, int Matches)
{
    public double Scale => Math.Sqrt(A * A + B * B);
    public (double X, double Y) Project(double x, double y) => (A * x - B * y + Tx, B * x + A * y + Ty);
    public PetMapAlignment Follow(double a, double b, double tx, double ty, int inliers, int matches)
        => new(MapId, a * A - b * B, b * A + a * B, a * Tx - b * Ty + tx, b * Tx + a * Ty + ty, inliers, matches);
    public bool IsPlausible => Inliers >= 18 && Matches >= Inliers && Inliers / (double)Math.Max(1, Matches) >= .30
        && double.IsFinite(Scale) && Scale is > .08 and < 30
        && Math.Abs(Math.Atan2(B, A)) < .05 && double.IsFinite(Tx) && double.IsFinite(Ty);
}
public sealed class PetMapCatalog
{
    public IReadOnlyList<PetWorldMap> Maps { get; }
    private readonly Dictionary<int, PetWorldMap> _maps;
    public PetMapCatalog(IEnumerable<PetWorldMap> maps)
    {
        Maps = maps.Where(m => m.Width > 0 && m.Height > 0 && m.Bounds.Length == 4
            && m.Bounds[2] > m.Bounds[0] && m.Bounds[3] > m.Bounds[1]).ToArray();
        _maps = Maps.ToDictionary(m => m.Id);
    }
    public static PetMapCatalog Load(string path) => new(JsonSerializer.Deserialize<PetWorldMap[]>(File.ReadAllText(path)) ?? []);
    public PetWorldMap? Map(int id) => _maps.GetValueOrDefault(id);
    public bool HasSpawns(int mapId, PetDefinition pet) => _maps.TryGetValue(mapId, out var map)
        && pet.Drops.Any(d => map.Spawns.TryGetValue(d.NpcCode, out var points) && points.Length >= 2);
    public IReadOnlyList<PetSpawnArea> Areas(int mapId, PetDefinition pet)
    {
        if (!_maps.TryGetValue(mapId, out var map)) return [];
        var points = new HashSet<(double X, double Y)>();
        foreach (var npc in pet.Drops.Select(d => d.NpcCode).Distinct())
            if (map.Spawns.TryGetValue(npc, out var coordinates))
                for (var i = 0; i + 1 < coordinates.Length; i += 2)
                    if (double.IsFinite(coordinates[i]) && double.IsFinite(coordinates[i + 1])) points.Add((coordinates[i], coordinates[i + 1]));
        // Bound each circle to a local area instead of linking distant groups into one huge circle.
        return points.GroupBy(p => ((int)Math.Floor(p.X / 160), (int)Math.Floor(p.Y / 160))).Select(group =>
        {
            var x = group.Average(p => p.X); var y = group.Average(p => p.Y);
            var radius = Math.Max(30, group.Max(p => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y))) + 20);
            return new PetSpawnArea(x, y, radius, group.Count());
        }).OrderByDescending(a => a.Points).ToArray();
    }
}
