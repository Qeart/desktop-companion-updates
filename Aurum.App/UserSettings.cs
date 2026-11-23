using System.IO;
using System.Text.Json;

namespace Aurum;

public sealed class UserSettings
{
    private readonly object _saveGate = new();
    public double Left { get; set; } = 65;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 450;
    public double Height { get; set; } = 470;
    public bool HealMode { get; set; }
    public string CaptureBackend { get; set; } = "Pktmon";
    public string UpdateChannel { get; set; } = "stable";
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public bool ShowPetProgress { get; set; } = true;
    public bool ShowShopPetProgress { get; set; } = true;
    public bool ShowPetMap { get; set; } = true;
    public bool ShowHiddenCube { get; set; } = true;
    public bool ShowItemAdvice { get; set; } = true;
    public double CubeNearbyMetres { get; set; } = 100;
    public int CubePositionLiveMilliseconds { get; set; } = 1000;
    public int CubePositionStaleMilliseconds { get; set; } = 3000;
    public int CubePredictionMilliseconds { get; set; } = 2000;
    public int CubeCameraFreshMilliseconds { get; set; } = 400;
    public double? LauncherLeft { get; set; }
    public double? LauncherTop { get; set; }
    public bool UseOwnPetMap { get; set; } = true;
    public int OwnMapRegion { get; set; } = 1110;
    public int SelectedMapPetId { get; set; }
    public string[] MapPetGenera { get; set; } = ["cogni", "fera", "natura", "varian", "special"];
    public static string DataDirectory => AppUserData.Directory;
    public static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public static UserSettings Load()
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save()
    {
        lock (_saveGate)
        {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, overwrite: true);
        }
    }
}
