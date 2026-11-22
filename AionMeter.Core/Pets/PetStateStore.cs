using System.Text.Json;
namespace AionMeter.Core.Pets;

public sealed record PetProgressSnapshot(string Profile, uint CharacterId, bool Synced, int Version,
    Dictionary<int, PetState> Pets, DateTimeOffset SavedAt = default, bool FromCache = false);

public sealed class PetStateStore(string path)
{
    public PetProgressSnapshot? Load()
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<PetProgressSnapshot>(File.ReadAllText(path));
            if (snapshot == null || !IsValid(snapshot)) return null;
            return snapshot with { SavedAt = snapshot.SavedAt == default ? File.GetLastWriteTimeUtc(path) : snapshot.SavedAt, FromCache = true };
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public void Save(PetProgressSnapshot snapshot)
    {
        if (!IsValid(snapshot)) return; // An incomplete restart must not erase the last complete collection.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
        File.Move(temporary, path, true);
    }
    public static bool IsValid(PetProgressSnapshot snapshot)
    {
        var split = snapshot.Profile?.IndexOf(':') ?? -1;
        return snapshot.Synced && split > 0 && split < snapshot.Profile!.Length - 1
            && int.TryParse(snapshot.Profile.AsSpan(0, split), out var server) && server is >= 1000 and < 3000
            && snapshot.Pets is { Count: <= 4096 } && snapshot.Pets.All(p => p.Key is > 0 and < 1000000
                && p.Value is { Level: >= 0 and <= 3, Points: >= 0 and <= 1000000 });
    }
}
