using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;

namespace Aurum;

public partial class App : System.Windows.Application
{
    private Mutex? _single;
    private MainWindow? _window;
    private EventWaitHandle? _stopSignal;
    private RegisteredWaitHandle? _stopWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--render-bis-catalog", var bisDirectory])
        {
            // Diagnostic only: no capture, updater, settings writes or game connection.
            try { BisCataloguePreview.Render(bisDirectory); }
            catch (Exception error) { Directory.CreateDirectory(bisDirectory); File.WriteAllText(Path.Combine(bisDirectory, "error.txt"), error.ToString()); }
            Shutdown(); return;
        }
        if (e.Args is ["--distribution-check", var distributionReport])
        {
            var locator = Velopack.Locators.VelopackLocator.Current;
            File.WriteAllText(distributionReport, JsonSerializer.Serialize(new {
                Installed = locator.CurrentlyInstalledVersion != null,
                Version = locator.CurrentlyInstalledVersion?.ToString(), locator.Channel, locator.IsPortable,
                AppDirectory = AppContext.BaseDirectory, DataDirectory = UserSettings.DataDirectory,
                Feed = Aurum.Distribution.ReleaseChannels.Repository
            }, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(); return;
        }
        if (e.Args is ["--render-updates", var updatesDirectory])
        {
            using var updates = new Aurum.Distribution.DesktopUpdates("stable");
            new UpdatesWindow(updates, new UserSettings(), () => { }).RenderPreview(updatesDirectory);
            Shutdown(); return;
        }
        if (e.Args is ["--render-arcana-advice", var arcanaImage, var arcanaDirectory])
        {
            _ = RenderArcanaAdvice(arcanaImage, arcanaDirectory); return;
        }
        if (e.Args is ["--render-item-advice", var itemImage, var itemDirectory])
        {
            _ = RenderItemAdvice(itemImage, itemDirectory); return;
        }
        if (e.Args is ["--check-cube-camera", var cameraImage, var cameraReport])
        {
            File.WriteAllText(cameraReport, JsonSerializer.Serialize(new CubeCameraReader().ReadFile(cameraImage)));
            Shutdown(); return;
        }
        if (e.Args is ["--render-hub", var hubDirectory])
        {
            Directory.CreateDirectory(hubDirectory);
            new AurumHubWindow(new UserSettings(), () => new HubState(true, false, true, true, true, "Skrzynka 18 m od Ciebie"), _ => { }, preview: true).RenderPreview(hubDirectory);
            new CubeCompassVisual().RenderSample(Path.Combine(hubDirectory, "hidden-cube.png"));
            new CubeCompassVisual().RenderSample(Path.Combine(hubDirectory, "hidden-cube-estimated.png"), AionMeter.Core.PetMaps.PositionState.Estimated);
            new CubeCompassVisual().RenderSample(Path.Combine(hubDirectory, "hidden-cube-stale.png"), AionMeter.Core.PetMaps.PositionState.Stale);
            new CubeCompassVisual().RenderSample(Path.Combine(hubDirectory, "hidden-cube-no-camera.png"), cameraFresh: false);
            Shutdown(); return;
        }
        if (e.Args is ["--render-pet-upgrade", var shopBackground, var petCache, var previewDirectory])
        {
            _ = RenderPetUpgrade(shopBackground, petCache, previewDirectory); return;
        }
        if (e.Args is ["--render-owned-map", var ownedCache, var ownedDirectory])
        {
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            if (new AionMeter.Core.Pets.PetStateStore(ownedCache).Load() is { } saved) progress.Restore(saved);
            var maps = AionMeter.Core.PetMaps.PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json"));
            var map = maps.Map(1110)!; var centreX = (map.Bounds[0] + map.Bounds[2]) / 200d; var centreY = (map.Bounds[1] + map.Bounds[3]) / 200d;
            var player = new AionMeter.Core.PetMaps.PlayerMapPosition(1110, centreX, centreY, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "Preview");
            new OwnedPetMapWindow(progress, new UserSettings { SelectedMapPetId = 1202, MapPetGenera = ["cogni"] }, () => player).RenderPreview(ownedDirectory);
            Shutdown(); return;
        }
        if (e.Args is ["--check-map-renderer", var rendererReport])
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            var settings = new UserSettings { ShowPetMap = true, SelectedMapPetId = 1051 };
            var renderer = new PetMapController(progress, settings, () => 1110);
            var check = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            check.Tick += (_, _) => {
                check.Stop(); File.WriteAllText(rendererReport, JsonSerializer.Serialize(renderer.DiagnosticState)); renderer.Dispose(); Shutdown();
            }; check.Start(); return;
        }
        if (e.Args is ["--render-map-picker", var pickerCache, var pickerImage])
        {
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            if (new AionMeter.Core.Pets.PetStateStore(pickerCache).Load() is { } saved) progress.Restore(saved);
            new PetMapPicker(progress, AionMeter.Core.PetMaps.PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json")),
                new UserSettings { SelectedMapPetId = 1051 }).RenderSample(pickerImage);
            Shutdown(); return;
        }
        if (e.Args is ["--check-pet-map", var mapImage, var mapReport])
        {
            _ = CheckPetMap(mapImage, mapReport); return;
        }
        if (e.Args is ["--preview-pet-map", var petMapCache])
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            if (new AionMeter.Core.Pets.PetStateStore(petMapCache).Load() is { } saved) progress.Restore(saved);
            var settings = new UserSettings { ShowPetMap = true, SelectedMapPetId = 1051 };
            var controller = new PetMapController(progress, settings, () => 1110);
            var endPreview = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            endPreview.Tick += (_, _) => { endPreview.Stop(); controller.Dispose(); Shutdown(); }; endPreview.Start(); return;
        }
        if (e.Args is ["--probe-shop-now", var probeReport])
        {
            _ = ProbeShopNow(probeReport); return;
        }
        if (e.Args is ["--read-shop-image", var shopImage, var shopReport])
        {
            _ = ReadShopImage(shopImage, shopReport); return;
        }
        if (e.Args is ["--preview-shop", var savedPets])
        {
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            if (new AionMeter.Core.Pets.PetStateStore(savedPets).Load() is { } saved) progress.Restore(saved);
            var shop = new ShopPetOverlay(progress); shop.Arm();
            var previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            var previewClock = Stopwatch.StartNew();
            previewTimer.Tick += (_, _) => { shop.Refresh(true); if (previewClock.Elapsed.TotalSeconds > 40) { previewTimer.Stop(); shop.Stop(); shop.Close(); Shutdown(); } };
            previewTimer.Start(); return;
        }
        if (e.Args is ["--render-pet", var petImage])
        {
            new PetToastWindow().RenderSample(petImage); Shutdown(); return;
        }
        if (e.Args is ["--stop"] or ["--stop-pktmon"])
        {
            var stopName = e.Args[0] == "--stop-pktmon" ? "AurumMeter.Stop.Pktmon" : "AurumMeter.Stop";
            if (EventWaitHandle.TryOpenExisting(stopName, out var signal))
                using (signal) signal.Set();
            Shutdown(); return;
        }
        if (e.Args is ["--probe-pktmon", var report, var secondsText] && int.TryParse(secondsText, out var seconds))
        {
            _ = ProbePktmon(report, Math.Clamp(seconds, 1, 180));
            return;
        }
        if (e.Args is [var renderMode, var directory] && renderMode is "--render-demo" or "--render-narrow")
        {
            Directory.CreateDirectory(directory);
            _window = new MainWindow(preview: true);
            _window.RenderDemo(directory, narrow: renderMode == "--render-narrow");
            Shutdown();
            return;
        }
        AppUserData.ImportLegacy();
        var pktmon = e.Args.Contains("--pktmon") || UserSettings.Load().CaptureBackend.Equals("Pktmon", StringComparison.OrdinalIgnoreCase);
        if (pktmon && !IsAdministrator())
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--pktmon") { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory }); }
            catch (System.ComponentModel.Win32Exception) { }
            Shutdown(); return;
        }
        _single = new Mutex(true, pktmon ? "AurumMeter.Local.Pktmon" : "AurumMeter.Local", out var first);
        if (!first) { Shutdown(); return; }
        _stopSignal = new EventWaitHandle(false, EventResetMode.AutoReset, pktmon ? "AurumMeter.Stop.Pktmon" : "AurumMeter.Stop");
        _stopWait = ThreadPool.RegisterWaitForSingleObject(_stopSignal,
            (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, Timeout.Infinite, true);
        DispatcherUnhandledException += (_, error) =>
        {
            Directory.CreateDirectory(UserSettings.DataDirectory);
            File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "error.txt"), error.Exception.ToString());
            error.Handled = true;
        };
        _window = new MainWindow(captureBackend: pktmon ? "Pktmon" : "Npcap");
        MainWindow = _window;
        _window.Show();
    }
    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private async Task RenderArcanaAdvice(string image, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            var reader = new ArcanaScreenReader(); var check = await reader.CheckFile(image); var panel = reader.LearnedPanel;
            var catalog = AionMeter.Core.Gear.ArcanaAdviceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "arcana-advice.json"));
            var advice = panel == null ? null : catalog.For(GameClass.Chanter, panel);
            File.WriteAllText(Path.Combine(directory, "arcana-check.json"), JsonSerializer.Serialize(new { check, advice }, new JsonSerializerOptions { WriteIndented = true }));
            if (panel != null && advice != null) new ArcanaAdviceWindow().RenderPreview(panel, advice, image, Path.Combine(directory, "arcana-advice.png"));
            reader.Dispose();
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString()); }
        Shutdown();
    }
    private async Task RenderItemAdvice(string image, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            var reader = new ItemTooltipReader(); var reading = await reader.ReadFile(image);
            var catalog = AionMeter.Core.Gear.GearAdviceCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "gear-advice.json"));
            var advice = reading == null ? null : catalog.For(GameClass.Chanter, reading.Item);
            File.WriteAllText(Path.Combine(directory, "item-reading.json"), JsonSerializer.Serialize(new { reading, advice }, new JsonSerializerOptions { WriteIndented = true }));
            if (advice != null) new ItemAdviceWindow().RenderPreview(advice, image, directory, reading!.ComparisonRight);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString()); }
        Shutdown();
    }
    private async Task RenderPetUpgrade(string background, string cache, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            new PetToastWindow().RenderSample(Path.Combine(directory, "soul-pickup.png"));
            var progress = AionMeter.Core.Pets.PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
            if (new AionMeter.Core.Pets.PetStateStore(cache).Load() is { } saved) progress.Restore(saved);
            using var reader = new ShopScreenReader();
            var check = await reader.CheckCache(background);
            File.WriteAllText(Path.Combine(directory, "shop-check.json"), JsonSerializer.Serialize(check, new JsonSerializerOptions { WriteIndented = true }));
            var reading = await reader.ReadFileFrame(background);
            var shop = new ShopPetOverlay(progress); shop.RenderSample(background, reading.Cards, Path.Combine(directory, "cloud-market.png")); shop.Close();
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "error.txt"), error.ToString()); }
        Shutdown();
    }
    private async Task CheckPetMap(string image, string report)
    {
        await Task.Run(() =>
        {
            try
            {
                var catalog = AionMeter.Core.PetMaps.PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json"));
                using var matcher = new PetMapMatcher(catalog, Path.Combine(AppContext.BaseDirectory, "data", "pet-maps"));
                var result = matcher.ReadFile(image, 1110);
                File.WriteAllText(report, JsonSerializer.Serialize(result));
            }
            catch (Exception error) { File.WriteAllText(report, error.ToString()); }
        });
        Shutdown();
    }
    private async Task ReadShopImage(string image, string report)
    {
        try
        {
            var pets = JsonSerializer.Deserialize<AionMeter.Core.Pets.PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"))) ?? [];
            var rows = await new ShopScreenReader().ReadFile(image);
            File.WriteAllText(report, JsonSerializer.Serialize(rows.Select(row => new { row.Text, row.X, row.Y,
                Pet = AionMeter.Core.Pets.ShopPetMatcher.Match(row.Text, pets)?.Name })));
        }
        catch (Exception error) { File.WriteAllText(report, error.ToString()); }
        Shutdown();
    }
    private async Task ProbeShopNow(string report)
    {
        await Task.Run(async () =>
        {
            var reader = new ShopScreenReader();
            try
            {
                var hwnd = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(hwnd, out var id);
                using var process = Process.GetProcessById((int)id);
                Native.GetClientRect(hwnd, out var bounds); var point = new Native.Point(); Native.ClientToScreen(hwnd, ref point);
                var result = await reader.Read(point.X, point.Y, bounds.Right, bounds.Bottom).WaitAsync(TimeSpan.FromSeconds(8));
                File.WriteAllText(report, JsonSerializer.Serialize(new { Foreground = process.ProcessName, Result = result }));
            }
            catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { reader.Stage, Error = error.ToString() })); }
        });
        Shutdown();
    }
    private async Task ProbePktmon(string report, int seconds)
    {
        try
        {
            var data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "data"));
            var tracker = new CombatTracker(data, new MeterOptions { TargetMode = TargetMode.All });
            using var capture = new PktmonCapture(data);
            long damageEvents = 0, healingEvents = 0;
            capture.EventDecoded += ev =>
            {
                tracker.Process(ev);
                if (ev is DamageEvent) Interlocked.Increment(ref damageEvents);
                if (ev is HealEvent) Interlocked.Increment(ref healingEvents);
            };
            capture.Start();
            for (var i = 0; i < seconds; i++)
            {
                await Task.Delay(1000);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                tracker.Tick(now);
                var snapshot = tracker.Snapshot(null, now);
                var npcLibraryLoaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                    .Any(m => string.Equals(m.ModuleName, "wpcap.dll", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.ModuleName, "Packet.dll", StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(report, JsonSerializer.Serialize(new
                {
                    Administrator = IsAdministrator(), ElapsedSeconds = i + 1, NpcapLibraryLoaded = npcLibraryLoaded,
                    Capture = capture.Statistics(), DamageEvents = Interlocked.Read(ref damageEvents),
                    HealingEvents = Interlocked.Read(ref healingEvents), Summary = snapshot,
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (capture.Status.State == CaptureState.Error) break;
            }
        }
        catch (Exception ex)
        { File.WriteAllText(report, JsonSerializer.Serialize(new { Error = ex.ToString() })); }
        finally { Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _window?.Dispose();
        _stopWait?.Unregister(null);
        _stopSignal?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}


