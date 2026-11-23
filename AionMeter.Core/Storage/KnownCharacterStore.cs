using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace AionMeter.Core.Storage;

/// <summary>Stable name/server/class only. No actor ids, zone ids or party membership.</summary>
public sealed class KnownCharacterStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    public KnownCharacter[] Load()
    {
        try {
            if (!File.Exists(path) || new FileInfo(path).Length > 64_000) return [];
            return (JsonSerializer.Deserialize<KnownCharacter[]>(File.ReadAllText(path), Json) ?? [])
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name) && c.ServerId is >= 1000 and < 3000
                    && c.Class != GameClass.Unknown && Enum.IsDefined(c.Class)).Take(64).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public void Save(IEnumerable<KnownCharacter> characters)
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(characters.Take(64).ToArray(), Json));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
