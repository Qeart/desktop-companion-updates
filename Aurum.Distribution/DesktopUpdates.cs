using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Aurum.Distribution;

public enum UpdateStage { Idle, Checking, Current, Available, Downloading, Ready, Error, NotInstalled }
public sealed record UpdateStatus(UpdateStage Stage, string Message, string CurrentVersion, string? NewVersion = null, int Progress = 0, string? Error = null);

public sealed class DesktopUpdates : IDisposable
{
    private UpdateManager _manager;
    private UpdateInfo? _offer;
    private VelopackAsset? _ready;
    private int _busy;
    private readonly CancellationTokenSource _cancel = new();
    private bool _disposed;
    public string Channel { get; private set; }
    public UpdateStatus Status { get; private set; }
    public bool Busy => Volatile.Read(ref _busy) != 0;
    public event Action<UpdateStatus>? Changed;

    public DesktopUpdates(string? channel)
    {
        Channel = ReleaseChannels.Normalize(channel); _manager = CreateManager(Channel);
        Status = new(_manager.IsInstalled ? UpdateStage.Idle : UpdateStage.NotInstalled,
            _manager.IsInstalled ? "Gotowy do sprawdzenia aktualizacji." : "Aktualizacje są dostępne po instalacji programu z pliku Setup.exe.",
            _manager.CurrentVersion?.ToString() ?? "wersja lokalna");
        if (_manager.IsInstalled && _manager.UpdatePendingRestart is { } downloaded)
        { _ready = downloaded; Set(UpdateStage.Ready, "Aktualizacja jest już pobrana. Uruchom ponownie program, gdy zakończysz walkę.", 100); }
    }
    private static UpdateManager CreateManager(string channel) => new(
        new GithubSource(ReleaseChannels.Repository, accessToken: null, prerelease: channel == "beta"),
        new UpdateOptions { ExplicitChannel = ReleaseChannels.Feed(channel), AllowVersionDowngrade = false });

    public bool ChangeChannel(string channel)
    {
        if (Busy || _disposed || _ready != null) return false;
        Channel = ReleaseChannels.Normalize(channel); _manager = CreateManager(Channel); _offer = null;
        Set(UpdateStage.Idle, "Zmieniono kanał. Sprawdź dostępne aktualizacje."); return true;
    }
    private void Set(UpdateStage stage, string message, int progress = 0, string? error = null)
    {
        Status = new(stage, message, _manager.CurrentVersion?.ToString() ?? "wersja lokalna",
            (_ready ?? _offer?.TargetFullRelease)?.Version.ToString(), progress, error);
        if (!_disposed) Changed?.Invoke(Status);
    }
    public async Task Check()
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            if (!_manager.IsInstalled) { Set(UpdateStage.NotInstalled, "Zainstaluj program z pliku Setup.exe, aby korzystać z aktualizacji."); return; }
            if (_ready != null) { Set(UpdateStage.Ready, "Aktualizacja pobrana. Uruchom program ponownie, gdy zakończysz walkę.", 100); return; }
            Set(UpdateStage.Checking, "Sprawdzanie dostępnych wersji…");
            _offer = await _manager.CheckForUpdatesAsync();
            Set(_offer == null ? UpdateStage.Current : UpdateStage.Available,
                _offer == null ? "Masz najnowszą wersję w tym kanale." : "Nowa wersja jest dostępna. Możesz pobrać ją w dogodnym momencie.");
        }
        catch (Exception error) { Set(UpdateStage.Error, "Nie udało się sprawdzić aktualizacji. Spróbuj ponownie później.", error: error.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); if (!_disposed) Changed?.Invoke(Status); }
    }
    public async Task Download()
    {
        if (_disposed || _offer == null || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            Set(UpdateStage.Downloading, "Pobieranie aktualizacji…");
            await _manager.DownloadUpdatesAsync(_offer, value => Set(UpdateStage.Downloading, "Pobieranie aktualizacji…", value), _cancel.Token);
            _ready = _offer.TargetFullRelease;
            Set(UpdateStage.Ready, "Aktualizacja pobrana i sprawdzona. Możesz uruchomić program ponownie.", 100);
        }
        catch (OperationCanceledException) { if (!_disposed) Set(UpdateStage.Available, "Pobieranie przerwane."); }
        catch (Exception error) { Set(UpdateStage.Error, "Pobieranie nie powiodło się. Bieżąca wersja nadal działa.", error: error.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); if (!_disposed) Changed?.Invoke(Status); }
    }
    public void ScheduleRestart()
    {
        if (_disposed || Busy || _ready == null || Status.Stage != UpdateStage.Ready) throw new InvalidOperationException("Brak sprawdzonej aktualizacji do zastosowania.");
        // Return to WPF for graceful disposal/saving before the updater replaces files.
        _manager.WaitExitThenApplyUpdates(_ready, silent: false, restart: true, restartArgs: ["--pktmon"]);
    }
    public void Dispose() { _disposed = true; _cancel.Cancel(); Changed = null; }
}
