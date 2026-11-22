using System.IO;
using Aurum.Distribution;
using Velopack.Locators;

namespace Aurum;

internal static class AppUserData
{
    internal static readonly string Directory = UserDataMigration.DataDirectory(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), VelopackLocator.Current.CurrentlyInstalledVersion != null);

    internal static void ImportLegacy()
    {
        var old = Path.Combine(AppContext.BaseDirectory, "user-data");
        var result = UserDataMigration.Import(old, Directory);
        if (result.Errors.Count > 0)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllLines(Path.Combine(Directory, "data-import-errors.txt"), result.Errors);
        }
    }
}
