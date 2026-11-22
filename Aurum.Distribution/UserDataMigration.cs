using System.Text.Json;

namespace Aurum.Distribution;

public sealed record DataImportResult(int Copied, int Existing, IReadOnlyList<string> Errors);

public static class UserDataMigration
{
    private static readonly string[] SettingsFiles = ["settings.json", "pets-cache.json", "pet-progress.json", "names-cache.json"];

    public static string DataDirectory(string appDirectory, string localAppData, bool installed)
        => Path.GetFullPath(installed ? Path.Combine(localAppData, "DesktopCompanionData") : Path.Combine(appDirectory, "user-data"));

    // Only application data is imported. Existing settings are never replaced, and
    // links, captures, executables and arbitrary files in the old directory are ignored.
    public static DataImportResult Import(string source, string destination)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), destination.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(source)) return new(0, 0, []);
        if (Linked(source)) return new(0, 0, ["Katalog źródłowy jest dowiązaniem."]);
        Directory.CreateDirectory(destination);
        var copied = 0; var existing = 0; var errors = new List<string>();
        void Copy(string relative, bool json)
        {
            var file = Path.Combine(source, relative); var target = Path.Combine(destination, relative);
            if (!File.Exists(file) || Linked(file)) return;
            if (File.Exists(target)) { existing++; return; }
            var temporary = target + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                if (json) { using var input = File.OpenRead(file); using var document = JsonDocument.Parse(input); }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, temporary, overwrite: false);
                File.Move(temporary, target, overwrite: false); copied++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { errors.Add(relative + ": " + e.Message); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (var file in SettingsFiles) Copy(file, true);
        var fights = Path.Combine(source, "fights"); var targetFights = Path.Combine(destination, "fights");
        // Import the history as a set so its index always belongs to those fight files.
        if (Directory.Exists(fights) && !Linked(fights) && !File.Exists(Path.Combine(targetFights, "index.jsonl")))
        {
            var before = errors.Count;
            foreach (var file in Directory.EnumerateFiles(fights, "*.json")) Copy(Path.Combine("fights", Path.GetFileName(file)), true);
            if (errors.Count == before) Copy(Path.Combine("fights", "index.jsonl"), false);
        }
        return new(copied, existing, errors);
    }
    private static bool Linked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
