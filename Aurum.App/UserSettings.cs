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
    public bool BossAssistantEnabled { get; set; } = true;
    public bool BossOverlayLocked { get; set; } = true;
    public bool BossSounds { get; set; } = true;
    public bool BossShowObservedSkills { get; set; } = true;
    public bool BossCommunityTimers { get; set; } = true;
    public bool BossLearnedTimers { get; set; }
    public bool BossJournal { get; set; } = true;
    public string BossRole { get; set; } = "dps";
    public double BossVolume { get; set; } = 45;
    public double BossScale { get; set; } = 1;
    public double BossOpacity { get; set; } = .95;
    public double BossWidth { get; set; } = 360;
    public double? BossLeft { get; set; }
    public double? BossTop { get; set; }
    public double? BossWarningLeft { get; set; }
    public double? BossWarningTop { get; set; }
    public double[] BossWarningSeconds { get; set; } = [10, 5, 3];
    public string[] DisabledBossMechanics { get; set; } = [];
    public string CaptureBackend { get; set; } = "Pktmon";
    public string UpdateChannel { get; set; } = "stable";
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public bool ShowPetProgress { get; set; } = true;
    public bool ShowShopPetProgress { get; set; } = true;
    public bool ShowPetMap { get; set; } = true;
    public bool ShowPetTracker { get; set; }
    public bool PetTrackerAutomatic { get; set; } = true;
    public int PetTrackerRegion { get; set; }
    public int[] TrackedPetIds { get; set; } = [];
    public bool PetTrackerLocked { get; set; }
    public double? PetTrackerLeft { get; set; }
    public double? PetTrackerTop { get; set; }
    public double PetTrackerWidth { get; set; } = 326;
    public double PetTrackerHeight { get; set; } = 468;
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
