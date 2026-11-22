using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Storage;
using AionMeter.Core.Pets;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Cubes;
using Aurum.Distribution;

namespace Aurum;

public partial class MainWindow : Window, IDisposable
{
    public ObservableCollection<MetricRow> Rows { get; } = new();
    private readonly UserSettings _settings;
    private readonly bool _preview;
    private readonly GameData _data;
    private readonly CombatTracker _tracker;
    private readonly IEventSource? _capture;
    private readonly string _backend;
    private readonly PetProgress _petProgress;
    private readonly PetStateStore _petStore;
    private PetToastWindow? _petToast;
    private ShopPetOverlay? _shopPets;
    private int _shopTabRequest;
    private int _gameMapId;
    private PetMapController? _petMap;
    private readonly PlayerPositionTracker _playerPosition = new();
    private readonly HiddenCubeTracker _hiddenCubes = new();
    private HiddenCubeController? _cube;
    private AurumHubWindow? _hub;
    private ItemAdviceController? _itemAdvice;
    private ArcanaAdviceController? _arcanaAdvice;
    private System.Windows.Forms.ToolStripMenuItem? _itemAdviceMenu;
    private System.Windows.Forms.ToolStripMenuItem? _cubeMenu, _petMenu, _shopMenu, _mapMenu;
    private int _savedPetVersion;
    private long _lastPetSave;
    private long _lastStatusSave;
    private readonly HistoryStore? _history;
    private readonly NameCache? _names;
    private int _savedNamesVersion;
    private long _lastNamesSave;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _shopTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Windows.Forms.NotifyIcon? _tray;
    private EncounterSnapshot? _snapshot;
    private FightRecord? _selected;
    private nint _hwnd;
    private bool _disposed;
    private Window? _details;
    private System.Windows.Point? _dragStart;
    private double _dragLeft, _dragTop;
    private readonly DispatcherTimer _placementTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(60) };
    private DesktopUpdates? _updates;
    private UpdatesWindow? _updatesWindow;
    private System.Windows.Forms.ToolStripMenuItem? _updateMenu;
    private string? _notifiedVersion;

    public MainWindow(bool preview = false, string? captureBackend = null)
    {
        _preview = preview;
        _settings = preview ? new() : UserSettings.Load();
        _backend = (captureBackend ?? _settings.CaptureBackend).Equals("Pktmon", StringComparison.OrdinalIgnoreCase) ? "PktMon" : "Npcap";
        _settings.CaptureBackend = _backend;
        _data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        _petProgress = PetProgress.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"));
        _petStore = new PetStateStore(Path.Combine(UserSettings.DataDirectory, "pets-cache.json"));
        _tracker = new CombatTracker(_data, new MeterOptions { TargetMode = TargetMode.All, GroupOnly = true });
        if (!preview)
        {
            _names = new NameCache(Path.Combine(UserSettings.DataDirectory, "names-cache.json"));
            string? expectedProfile = null;
            if (_names.Load() is { } cache)
            {
                _tracker.ImportCache(cache);
                _gameMapId = cache.MapId;
                _playerPosition.SeedIdentity(cache.SelfId ?? 0, cache.MapId);
                var own = cache.Players.FirstOrDefault(p => p.Id == cache.SelfId);
                if (own is { ServerId: > 0 } && !string.IsNullOrWhiteSpace(own.Name)) expectedProfile = own.ServerId + ":" + own.Name;
            }
            var petCache = _petStore.Load() ?? new PetStateStore(Path.Combine(UserSettings.DataDirectory, "pet-progress.json")).Load();
            if (petCache != null) _petProgress.Restore(petCache, expectedProfile);
            _savedNamesVersion = _tracker.CacheVersion;
        }
        InitializeComponent(); DataContext = this;
        _placementTimer.Tick += (_, _) => { _placementTimer.Stop(); PersistPlacement(); };
        Width = Math.Clamp(_settings.Width, MinWidth, 1000); Height = Math.Clamp(_settings.Height, MinHeight, 1000);
        Left = Math.Clamp(_settings.Left, SystemParameters.VirtualScreenLeft,
            Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(_settings.Top, SystemParameters.VirtualScreenTop,
            Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(_hwnd, -20, Native.GetWindowLongPtr(_hwnd, -20) | 0x08000000 | 0x80);
            if (!_preview)
            {
                Native.RegisterHotKey(_hwnd, 1, 2 | 4 | 0x4000, 0x4D);
                Native.RegisterHotKey(_hwnd, 2, 2 | 4 | 0x4000, 0x50);
                Native.RegisterHotKey(_hwnd, 3, 2 | 4 | 0x4000, 0x47);
                Native.RegisterHotKey(_hwnd, 4, 2 | 4 | 0x4000, 0x48);
                Native.RegisterHotKey(_hwnd, 5, 2 | 4 | 0x4000, 0x41);
                HwndSource.FromHwnd(_hwnd)?.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
                {
                    if (msg == 0x0312 && w == 1) { ToggleVisible(); handled = true; }
                    if (msg == 0x0312 && w == 2) { ArmShopPets(); handled = true; }
                    if (msg == 0x0312 && w == 3) { ArmPetMap(); handled = true; }
                    if (msg == 0x0312 && w == 4) { HubAction(HubCommand.Cube); handled = true; }
                    if (msg == 0x0312 && w == 5) { _hub?.ToggleMenu(); handled = true; }
                    return 0;
                });
            }
        };
        LocationChanged += (_, _) => SavePlacement();
        SizeChanged += (_, _) => { ApplyCompactLayout(); SavePlacement(); };
        Closing += (_, e) => { if (!_preview && !_disposed) { e.Cancel = true; Hide(); } };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _shopTimer.Tick += (_, _) => RefreshShop();
        if (!preview)
        {
            _history = new HistoryStore(Path.Combine(UserSettings.DataDirectory, "fights"));
            _tracker.EncounterFinished += record =>
            {
                try { _history.Save(record); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "history-error.txt"), ex.Message); }
            };
            _capture = _backend == "PktMon" ? new PktmonCapture(_data) : new LiveCapture(_data, new LiveCaptureOptions());
            _capture.EventDecoded += _tracker.Process;
            _capture.EventDecoded += _petProgress.Process;
            _capture.EventDecoded += _playerPosition.Process;
            _capture.EventDecoded += _hiddenCubes.Process;
            var positionPipeline = _capture switch { PktmonCapture pktmon => pktmon.Pipeline, LiveCapture live => live.Pipeline, _ => null };
            if (positionPipeline != null) { positionPipeline.Parser.Tap += _playerPosition.Raw; positionPipeline.Parser.Tap += _hiddenCubes.Raw; }
            _capture.EventDecoded += ev => { if (ev is MarketTabEvent market) Interlocked.Exchange(ref _shopTabRequest, market.Tab); };
            _capture.EventDecoded += ev => { if (ev is ZoneChangedEvent zone) Interlocked.Exchange(ref _gameMapId, zone.MapId); };
            _capture.Start();
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application, Text = "Aurum Meter • DPS / HEAL", Visible = true,
                ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
            };
            _tray.ContextMenuStrip.Items.Add("Pokaż / ukryj (Ctrl+Shift+M)", null, (_, _) => ToggleVisible());
            _tray.ContextMenuStrip.Items.Add("DPS", null, (_, _) => SetMode(false));
            _tray.ContextMenuStrip.Items.Add("HEAL", null, (_, _) => SetMode(true));
            _tray.ContextMenuStrip.Items.Add("Źródło danych: " + _backend).Enabled = false;
            var petsToggle = new System.Windows.Forms.ToolStripMenuItem("Postęp petów po zabiciu")
                { Checked = _settings.ShowPetProgress, CheckOnClick = true };
            _petMenu = petsToggle;
            petsToggle.CheckedChanged += (_, _) => { _settings.ShowPetProgress = petsToggle.Checked; _settings.Save(); if (!petsToggle.Checked) _petToast?.Refresh([], false); };
            _tray.ContextMenuStrip.Items.Add(petsToggle);
            var shopToggle = new System.Windows.Forms.ToolStripMenuItem("Liczniki petów w sklepie")
                { Checked = _settings.ShowShopPetProgress, CheckOnClick = true };
            _shopMenu = shopToggle;
            shopToggle.CheckedChanged += (_, _) => { _settings.ShowShopPetProgress = shopToggle.Checked; _settings.Save(); if (shopToggle.Checked) ArmShopPets(); else _shopPets?.Stop(); };
            _tray.ContextMenuStrip.Items.Add(shopToggle);
            _tray.ContextMenuStrip.Items.Add("Odczytaj karty sklepu (Ctrl+Shift+P)", null, (_, _) => ArmShopPets());
            if (_settings.ShowShopPetProgress) ArmShopPets();
            var mapToggle = new System.Windows.Forms.ToolStripMenuItem("Mapa Aurum") { Checked = _settings.ShowPetMap, CheckOnClick = true };
            _mapMenu = mapToggle;
            mapToggle.CheckedChanged += (_, _) => { _settings.ShowPetMap = mapToggle.Checked; _settings.Save(); if (mapToggle.Checked) ArmPetMap(); else _petMap?.Stop(); };
            _tray.ContextMenuStrip.Items.Add(mapToggle);
            _tray.ContextMenuStrip.Items.Add("Otwórz mapę Aurum (Ctrl+Shift+G)", null, (_, _) => ArmPetMap());
            if (_settings.ShowPetMap) { EnsurePetMap(); if (!_settings.UseOwnPetMap) _petMap!.Arm(); }
            _cubeMenu = new System.Windows.Forms.ToolStripMenuItem("Hidden Cube (Ctrl+Shift+H)") { Checked = _settings.ShowHiddenCube, CheckOnClick = true };
            _cubeMenu.CheckedChanged += (_, _) => { _settings.ShowHiddenCube = _cubeMenu.Checked; _settings.Save(); _cube?.RefreshEnabled(); };
            _tray.ContextMenuStrip.Items.Add(_cubeMenu);
            _itemAdviceMenu = new System.Windows.Forms.ToolStripMenuItem("BiS sprzętu i arkan") { Checked = _settings.ShowItemAdvice, CheckOnClick = true };
            _itemAdviceMenu.CheckedChanged += (_, _) => { _settings.ShowItemAdvice = _itemAdviceMenu.Checked; _settings.Save(); _itemAdvice?.RefreshEnabled(); _arcanaAdvice?.RefreshEnabled(); };
            _tray.ContextMenuStrip.Items.Add(_itemAdviceMenu);
            _tray.ContextMenuStrip.Items.Add("Menu Aurum (Ctrl+Shift+A)", null, (_, _) => _hub?.ToggleMenu());
            _tray.ContextMenuStrip.Items.Add("Powrót do bieżącej walki", null, (_, _) => { _selected = null; Refresh(); });
            _tray.ContextMenuStrip.Items.Add("Zamknij Aurum Meter", null, (_, _) => System.Windows.Application.Current.Shutdown());
            _tray.DoubleClick += (_, _) => ToggleVisible();
            _updates = new DesktopUpdates(_settings.UpdateChannel);
            _updateMenu = new System.Windows.Forms.ToolStripMenuItem("Aktualizacje…");
            _updateMenu.Click += (_, _) => OpenUpdates(); _tray.ContextMenuStrip.Items.Insert(0, _updateMenu);
            _updates.Changed += state => Dispatcher.BeginInvoke(() => UpdateState(state));
            _updateTimer.Tick += async (_, _) => { if (_settings.CheckUpdatesAutomatically) await _updates.Check(); };
            _updateTimer.Start();
            Loaded += async (_, _) => { await Task.Delay(12000); if (!_disposed && _settings.CheckUpdatesAutomatically) await _updates.Check(); };
            _cube = new HiddenCubeController(_settings, _hiddenCubes, _playerPosition.Snapshot,
                () => _petMap?.HasMapView == true || _shopPets?.HasShopView == true || _arcanaAdvice?.HasView == true);
            _itemAdvice = new ItemAdviceController(_settings, () => _tracker.SelfClass, () => _petMap?.HasMapView == true);
            _arcanaAdvice = new ArcanaAdviceController(_settings, () => _tracker.SelfClass, () => _petMap?.HasMapView == true);
            _hub = new AurumHubWindow(_settings, () => new HubState(IsVisible, _petMap?.HasMapView == true,
                _settings.ShowHiddenCube, _settings.ShowPetProgress, _settings.ShowShopPetProgress, _cube.Status, _settings.ShowItemAdvice), HubAction);
            _timer.Start();
            _shopTimer.Start();
        }
        SetMode(_settings.HealMode);
    }

    private void SavePlacement()
    {
        if (_preview || !IsInitialized) return;
        _placementTimer.Stop();
        _placementTimer.Start();
    }
    private void PersistPlacement()
    {
        if (_preview || !IsInitialized) return;
        _settings.Left = Left; _settings.Top = Top; _settings.Width = Width; _settings.Height = Height;
        _settings.Save();
    }
    private void ToggleVisible() { if (IsVisible) Hide(); else Show(); }
    private void HeaderDown(object sender, MouseButtonEventArgs e)
    {
        // DragMove relies on the native modal move loop, which is unreliable for
        // a non-activating game overlay. Capture the pointer and move explicitly.
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return;
            if (node == DragArea) break;
        }
        _dragStart = PointToScreen(e.GetPosition(this));
        _dragLeft = Left; _dragTop = Top;
        if (!DragArea.CaptureMouse()) _dragStart = null;
        e.Handled = true;
    }
    private void HeaderMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } origin) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndHeaderDrag(); return; }
        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _dragLeft + (current.X - origin.X) / dpi.DpiScaleX;
        Top = _dragTop + (current.Y - origin.Y) / dpi.DpiScaleY;
        e.Handled = true;
    }
    private void HeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        EndHeaderDrag(); e.Handled = true;
    }
    private void HeaderCaptureLost(object sender, MouseEventArgs e) => EndHeaderDrag();
    private void EndHeaderDrag()
    {
        if (_dragStart is null) return;
        _dragStart = null;
        if (DragArea.IsMouseCaptured) DragArea.ReleaseMouseCapture();
        _placementTimer.Stop(); PersistPlacement();
    }
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void ResetClick(object sender, RoutedEventArgs e) { _selected = null; _tracker.Reset(); Refresh(); }
    private void DpsClick(object sender, RoutedEventArgs e) => SetMode(false);
    private void HealClick(object sender, RoutedEventArgs e) => SetMode(true);
    private void SetMode(bool healing)
    {
        _settings.HealMode = healing;
        DpsButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(healing ? "#211D15" : "#66502D"));
        HealButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(healing ? "#66502D" : "#211D15"));
        MetricCaption.Text = healing ? "ODCZYTANE LECZENIE" : "OBRAŻENIA";
        ApplyCompactLayout();
        _details?.Close(); _details = null;
        if (!_preview) _settings.Save();
        Refresh();
    }
    private void ApplyCompactLayout()
    {
        var compact = Width < 330;
        Frame.Padding = compact ? new Thickness(10, 12, 10, 10) : new Thickness(18, 14, 18, 12);
        BrandSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Resources["RowRateFont"] = Width < 290 ? 13.0 : 16.0;
        FightTitle.FontSize = compact ? 13 : 16;
        FightState.FontSize = compact ? 9 : 10;
        ClockLabel.FontSize = compact ? 17 : 21;
        TotalLabel.FontSize = compact ? 16 : 18;
        MetricCaption.Text = _settings.HealMode ? compact ? "LECZENIE" : "ODCZYTANE LECZENIE" : "OBRAŻENIA";
        DpsButton.Padding = HealButton.Padding = compact ? new Thickness(8, 6, 8, 6) : new Thickness(12, 6, 12, 6);
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _tracker.Tick(now);
        if (!_preview)
        {
            if (now - _lastStatusSave >= 2000)
            {
                _lastStatusSave = now;
                var status = _capture?.Status;
                var json = System.Text.Json.JsonSerializer.Serialize(new {
                    UpdatedAt = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId,
                    ProgramDirectory = AppContext.BaseDirectory, PetDisplay = "PortraitCounterUnderTargetHp",
                    CombatScreenCapture = false, ShopOcrEnabled = _settings.ShowShopPetProgress, Backend = _backend, State = status?.State.ToString(),
                    status?.Message, status?.Packets, status?.Events, PetsSynced = _petProgress.Synced, PetsFromCache = _petProgress.FromCache,
                    ShopPollMs = _shopTimer.Interval.TotalMilliseconds, Shop = _shopPets?.DiagnosticState, PetMap = _petMap?.DiagnosticState,
                    PlayerPosition = _playerPosition.Snapshot(), PlayerScope = _tracker.InDungeon ? "DungeonParticipants" : "PartyOrForce", Group = _tracker.GroupState,
                    HiddenCube = _cube?.DiagnosticState, Hub = _hub?.DiagnosticState, ItemAdvice = _itemAdvice?.DiagnosticState, ArcanaAdvice = _arcanaAdvice?.DiagnosticState
                });
                try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "session-status.json"), json); }
                catch (IOException) { }
            }
            var notices = _petProgress.TakeNotices(now);
            if (notices.Count > 0) _petToast ??= new PetToastWindow();
            _petToast?.Refresh(notices, _settings.ShowPetProgress);
            if (_petProgress.Version != _savedPetVersion && now - _lastPetSave >= 1000)
            {
                var version = _petProgress.Version;
                File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "pet-progress.json"),
                    System.Text.Json.JsonSerializer.Serialize(_petProgress.DiagnosticState(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                _savedPetVersion = version; _lastPetSave = now;
                _petStore.Save(_petProgress.Snapshot());
            }
        }
        if (!_preview && _names is not null && _tracker.CacheVersion != _savedNamesVersion && now - _lastNamesSave >= 2000)
        {
            _lastNamesSave = now;
            var version = _tracker.CacheVersion;
            _names.Save(_tracker.ExportCache());
            _savedNamesVersion = version;
        }
        var live = _tracker.Snapshot(null, now);
        if (_selected is { } saved && live is { IsActive: true } && live.StartedAt > saved.Summary.StartedAt) _selected = null;
        Apply(_selected?.Summary ?? live);
        var unnamed = _snapshot?.Combatants.Count(p => !p.IsUnknownSummons && p.Name.StartsWith('#')) ?? 0;
        CaptureLabel.ToolTip = _capture?.Status.Message;
        CaptureLabel.Text = _preview ? "PODGLĄD • dane przykładowe"
            : _capture?.Status.State == CaptureState.Error ? _backend + ": " + _capture.Status.Message
            : unnamed > 0 ? "Brak nicków: zmień lokację lub wejdź ponownie na postać"
            : _capture?.Status.State == CaptureState.Capturing ? "● " + _backend + " · odczyt aktywny"
            : _backend + " · czekam na połączenie z Aion 2";
    }

    private void Apply(EncounterSnapshot? snapshot)
    {
        _snapshot = snapshot;
        var healing = _settings.HealMode;
        if (snapshot is null)
        {
            Rows.Clear(); FightTitle.Text = "Czekam na walkę"; ClockLabel.Text = "00:00";
            TotalLabel.Text = "0"; FightState.Text = "PARTY / FORCE · solo: tylko Ty";
            EmptyLabel.Text = healing ? "Leczenie pojawi się podczas walki.\nPrzełącznik nie zeruje wyniku DPS." : "Dane pojawią się podczas walki.";
            EmptyLabel.Visibility = Visibility.Visible; return;
        }
        var seconds = Math.Max(1, snapshot.CombatMs / 1000.0);
        var elapsed = TimeSpan.FromMilliseconds(snapshot.IsActive ? snapshot.ClockMs : snapshot.CombatMs);
        ClockLabel.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        FightTitle.Text = snapshot.Title;
        FightState.Text = _selected is not null ? "ZAPISANA WALKA • następna walka wróci do live"
            : (snapshot.IsActive ? "● WALKA TRWA" : "OSTATNIA WALKA") + " · PARTY / FORCE" + (healing ? "  · HPS = heal / czas walki" : "  · DPS = obrażenia / czas walki");
        var total = healing ? snapshot.TotalHealing : snapshot.TotalDamage;
        TotalLabel.Text = MetricRow.Compact(total);
        var players = snapshot.Combatants.Where(c => !c.IsUnknownSummons && (healing ? c.Healing : c.Damage) > 0)
            .OrderByDescending(c => healing ? c.Healing : c.Damage).Take(24).ToArray();
        var best = players.Length > 0 ? healing ? players[0].Healing : players[0].Damage : 1;
        while (Rows.Count > players.Length) Rows.RemoveAt(Rows.Count - 1);
        while (Rows.Count < players.Length) Rows.Add(new MetricRow());
        for (var i = 0; i < players.Length; i++) Rows[i].Apply(players[i], i + 1, best, total, seconds, healing);
        EmptyLabel.Visibility = players.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = healing ? "Brak odczytanego leczenia w tej walce." : "Brak odczytanych obrażeń.";
    }

    private void PlayerTooltipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not Button button || button.Tag is not uint id || _snapshot is not { } snapshot)
        { e.Handled = true; return; }
        var player = snapshot.Combatants.FirstOrDefault(c => c.ActorId == id);
        if (player is null) { e.Handled = true; return; }
        var healing = _settings.HealMode;
        var seconds = Math.Max(1, snapshot.CombatMs / 1000.0);
        var total = healing ? player.Healing : player.Damage;
        var detail = healing ? null : _selected?.Details.FirstOrDefault(c => c.ActorId == id) ?? _tracker.Detail(snapshot.Id, id);
        var skills = healing
            ? player.HealingSkills.Select(s => (s.Name, Amount: s.Amount))
            : detail?.Skills.Select(s => (s.Name, Amount: s.Damage)) ?? [];
        var ordered = skills.OrderByDescending(s => s.Amount).ToArray();
        var panel = new StackPanel();
        panel.Children.Add(TooltipText(player.Name + (healing ? "  · HEAL" : "  · DPS"), true));
        panel.Children.Add(new TextBlock
        {
            Text = $"{total:N0} {(healing ? "leczenia" : "obrażeń")}  ·  {MetricRow.Compact(total / seconds)} {(healing ? "HPS" : "DPS")}",
            Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 4, 0, 12),
        });
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        void Cell(int row, int column, string text, bool heading = false)
        {
            var block = TooltipText(text, heading);
            block.FontSize = heading ? 10 : 11;
            block.Margin = new Thickness(0, 0, column < 3 ? 10 : 0, 6);
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            if (column > 0) block.TextAlignment = TextAlignment.Right;
            Grid.SetRow(block, row); Grid.SetColumn(block, column); table.Children.Add(block);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(0, 0, "UMIEJĘTNOŚĆ", true); Cell(0, 1, healing ? "HEAL" : "OBRAŻENIA", true);
        Cell(0, 2, healing ? "HPS" : "DPS", true); Cell(0, 3, "UDZIAŁ", true);
        var count = Math.Min(8, ordered.Length);
        for (var i = 0; i < count; i++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var skill = ordered[i];
            Cell(i + 1, 0, skill.Name); Cell(i + 1, 1, MetricRow.Compact(skill.Amount));
            Cell(i + 1, 2, MetricRow.Compact(skill.Amount / seconds));
            Cell(i + 1, 3, $"{(total > 0 ? skill.Amount * 100.0 / total : 0):0.#}%");
        }
        panel.Children.Add(table);
        if (count == 0) panel.Children.Add(TooltipText("Brak odczytanych umiejętności."));
        panel.Children.Add(new TextBlock
        {
            Text = ordered.Length > count ? $"+ {ordered.Length - count} pozostałych · kliknij po pełne szczegóły"
                : "Kliknij gracza, aby otworzyć pełne szczegóły",
            FontSize = 10, Foreground = Brushes.DarkGray, Margin = new Thickness(0, 9, 0, 0),
        });
        button.ToolTip = new ToolTip
        {
            Content = panel, Background = new SolidColorBrush(Color.FromRgb(18, 19, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(141, 108, 48)), BorderThickness = new Thickness(1),
            Padding = new Thickness(15), FontFamily = new FontFamily("Segoe UI"),
            Placement = PlacementMode.Right, HorizontalOffset = 8,
        };
    }

    private static TextBlock TooltipText(string text, bool gold = false) => new()
    {
        Text = text, Foreground = new SolidColorBrush(gold ? Color.FromRgb(230, 190, 106) : Color.FromRgb(246, 240, 229)),
        FontSize = 13, FontWeight = gold ? FontWeights.SemiBold : FontWeights.Normal,
    };

    private void PlayerClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not uint id || _snapshot is not { } snap) return;
        var player = snap.Combatants.FirstOrDefault(c => c.ActorId == id);
        if (player is null) return;
        var healing = _settings.HealMode;
        IEnumerable<string> lines;
        if (healing)
            lines = player.HealingSkills.Select(s => $"{s.Name}\n{s.Amount:N0} leczenia  ·  {s.Events} zdarzeń");
        else
        {
            var detail = _selected?.Details.FirstOrDefault(c => c.ActorId == id) ?? _tracker.Detail(snap.Id, id);
            lines = detail?.Skills.Select(s => $"{s.Name}\n{s.Damage:N0} obrażeń  ·  {s.Hits + s.DotTicks} trafień") ?? [];
        }
        _details?.Close();
        _details = TextWindow(player.Name + (healing ? " • HEAL" : " • DPS"),
            string.Join("\n\n", lines) + (healing ? "\n\nLeczenie odczytane z pakietów. Nadleczenie nie jest osobno ustalone." : ""));
        _details.Show();
    }

    private static Window TextWindow(string title, string text)
    {
        return new Window
        {
            Title = title, Width = 470, Height = 500, Background = new SolidColorBrush(Color.FromRgb(18, 19, 22)),
            Foreground = new SolidColorBrush(Color.FromRgb(239, 218, 172)), FontFamily = new FontFamily("Segoe UI"),
            Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22), FontSize = 13 } },
        };
    }

    private void HistoryClick(object sender, RoutedEventArgs e)
    {
        if (_history is null) return;
        var entries = _history.List(100).ToList();
        var list = new ListBox { Margin = new Thickness(15), Background = Brushes.Transparent, Foreground = Brushes.White };
        foreach (var entry in entries) list.Items.Add($"{entry.StartedAt:dd.MM HH:mm}  ·  {entry.Title}");
        var window = new Window { Title = "Aurum • Historia", Width = 500, Height = 430, Background = new SolidColorBrush(Color.FromRgb(18, 19, 22)), Content = list };
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            _selected = _history.Load(entries[list.SelectedIndex].Path); Refresh(); window.Close();
        };
        window.Show();
    }

    private void AboutClick(object sender, RoutedEventArgs e) => TextWindow("Aurum Meter • informacje",
        "AURUM METER 0.2\n\nWłasny interfejs i tryb leczenia.\nParser i silnik walki: fork AionMeter.Core\ncyberbadger6969/aion2-dps-meter\nLicencja GPL-3.0 — patrz LICENSE i NOTICE.txt.\n\nDPS / HEAL przełącza prezentację tej samej walki.\nDane obejmują zdarzenia dostarczone przez serwer do tego klienta.\nHPS: odczytane leczenie / czas walki.\nNie ustalamy osobno nadleczenia.\n\nCtrl+Shift+M: pokaż/ukryj\nReset: zamknij walkę i zacznij nowy pomiar.\nZamknij program z ikony obok zegara.\nDane użytkownika: " + UserSettings.DataDirectory).Show();

    private void UpdatesClick(object sender, RoutedEventArgs e) => OpenUpdates();
    private void OpenUpdates()
    {
        if (_updates == null) return;
        if (_updatesWindow == null)
        {
            _updatesWindow = new UpdatesWindow(_updates, _settings, RestartAfterUpdate);
            _updatesWindow.Closed += (_, _) => _updatesWindow = null;
        }
        _updatesWindow.Show(); _updatesWindow.Activate();
        if (_updates.Status.Stage is UpdateStage.Idle or UpdateStage.NotInstalled) _ = _updates.Check();
    }
    private void UpdateState(UpdateStatus state)
    {
        if (_disposed) return;
        UpdateButton.Content = state.Stage is UpdateStage.Available or UpdateStage.Ready ? "↑" : "↻";
        UpdateButton.ToolTip = state.Message;
        if (_updateMenu != null) _updateMenu.Text = state.NewVersion == null ? "Aktualizacje…" : "Aktualizacja " + state.NewVersion + "…";
        if (state.Stage == UpdateStage.Available && state.NewVersion != _notifiedVersion)
        {
            _notifiedVersion = state.NewVersion;
            _tray?.ShowBalloonTip(6000, "Aurum — nowa wersja " + state.NewVersion, "Otwórz Aktualizacje z menu programu. Podmianę możesz odłożyć do końca walki.", System.Windows.Forms.ToolTipIcon.Info);
        }
        _ = Task.Run(() => { try { File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "update-status.json"), System.Text.Json.JsonSerializer.Serialize(state)); } catch (IOException) { } });
    }
    private void RestartAfterUpdate()
    {
        if (_updates == null) return;
        try { _updates.ScheduleRestart(); System.Windows.Application.Current.Shutdown(); }
        catch (Exception error) { MessageBox.Show((Window?)_updatesWindow ?? this, error.Message, "Nie udało się uruchomić aktualizacji"); }
    }

    public void RenderDemo(string directory, bool narrow = false)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _tracker.Process(new SelfIdentifiedEvent(now - 30000, 100, "Aurelia", 2314, GameClass.Cleric, CombatPower: 169660));
        _tracker.Process(new PlayerSeenEvent(now - 30000, 101, "Kael", 2314, GameClass.Gladiator));
        _tracker.Process(new PlayerSeenEvent(now - 30000, 102, "Nyra", 2314, GameClass.Sorcerer));
        _tracker.Process(new PlayerSeenEvent(now - 30000, 103, "Soren", 2314, GameClass.Chanter));
        _tracker.Process(new PartyRosterEvent(now - 30000, [
            new("Aurelia", 2314, 45, 0, 169660, GameClass.Cleric),
            new("Kael", 2314, 45, 0, 153210, GameClass.Gladiator),
            new("Nyra", 2314, 45, 0, 148900, GameClass.Sorcerer),
            new("Soren", 2314, 45, 0, 164210, GameClass.Chanter),
        ]));
        _tracker.Process(new NpcSeenEvent(now - 30000, 500, 2300243, 5_000_000, "Strażnik świątyni"));
        for (var i = 0; i <= 10; i++)
        {
            var at = now - 30000 + i * 3000;
            _tracker.Process(new DamageEvent(at, 101, 500, 11020000, 68000 + i * 2000, HitFlags.Critical));
            _tracker.Process(new DamageEvent(at, 102, 500, 15010000, 57000 + i * 2000, HitFlags.None));
            _tracker.Process(new DamageEvent(at, 100, 500, 17010000, 18000, HitFlags.None));
            _tracker.Process(new DamageEvent(at, 103, 500, 18010000, 37000, HitFlags.None));
            _tracker.Process(new HealEvent(at, 100, 101, 17010000, 32000, HitFlags.None));
            _tracker.Process(new HealEvent(at, 103, 101, 18010000, 16000, HitFlags.Dot));
        }
        Show();
        if (narrow) { Width = MinWidth; Height = 400; ApplyCompactLayout(); }
        foreach (var heal in new[] { false, true })
        {
            SetMode(heal); UpdateLayout();
            var target = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            target.Render((Visual)Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            var name = heal ? "aurum-heal" : "aurum-dps";
            using var output = File.Create(Path.Combine(directory, name + (narrow ? "-narrow.png" : "-demo.png")));
            encoder.Save(output);
        }
        Close();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _updateTimer.Stop(); _updatesWindow?.Close(); _updates?.Dispose();
        _hub?.Close(); _cube?.Dispose(); _itemAdvice?.Dispose(); _arcanaAdvice?.Dispose();
        _timer.Stop(); _shopTimer.Stop(); _capture?.Dispose();
        _petMap?.Dispose();
        if (!_preview) _petStore.Save(_petProgress.Snapshot());
        _placementTimer.Stop(); PersistPlacement();
        if (!_preview && _tracker.CacheVersion != _savedNamesVersion) _names?.Save(_tracker.ExportCache());
        if (!_preview) _tracker.Reset(); // save the active encounter on a graceful update/exit
        _tray?.Dispose();
        if (_hwnd != 0) Native.UnregisterHotKey(_hwnd, 1);
        if (_hwnd != 0) Native.UnregisterHotKey(_hwnd, 2);
        if (_hwnd != 0) Native.UnregisterHotKey(_hwnd, 3);
        if (_hwnd != 0) Native.UnregisterHotKey(_hwnd, 4);
        if (_hwnd != 0) Native.UnregisterHotKey(_hwnd, 5);
        _details?.Close();
        _petToast?.Close();
        _shopPets?.Stop(); _shopPets?.Close();
    }
    private void ArmShopPets()
    {
        if (!_settings.ShowShopPetProgress) return;
        _shopPets ??= new ShopPetOverlay(_petProgress); _shopPets.Arm(); _shopPets.Refresh(true);
    }
    private void HubAction(HubCommand command)
    {
        switch (command)
        {
            case HubCommand.Meter: ToggleVisible(); break;
            case HubCommand.Map:
                if (!_settings.ShowPetMap && _mapMenu != null) _mapMenu.Checked = true;
                else ArmPetMap();
                break;
            case HubCommand.Cube: if (_cubeMenu != null) _cubeMenu.Checked = !_cubeMenu.Checked; break;
            case HubCommand.Souls: if (_petMenu != null) _petMenu.Checked = !_petMenu.Checked; break;
            case HubCommand.Shop: if (_shopMenu != null) _shopMenu.Checked = !_shopMenu.Checked; break;
            case HubCommand.ItemAdvice: if (_itemAdviceMenu != null) _itemAdviceMenu.Checked = !_itemAdviceMenu.Checked; break;
        }
    }
    private void RefreshShop()
    {
        var shopTab = Interlocked.Exchange(ref _shopTabRequest, 0);
        if (shopTab != 0) _petMap?.Stop();
        if (shopTab == 0x68) ArmShopPets(); else if (shopTab != 0) _shopPets?.Stop();
        if (_petMap?.HasMapView == true) { _shopPets?.Refresh(false); return; }
        _shopPets?.Refresh(_settings.ShowShopPetProgress);
    }
    private void ArmPetMap()
    {
        if (!_settings.ShowPetMap) return;
        EnsurePetMap(); _petMap!.Arm();
    }
    private void EnsurePetMap() => _petMap ??= new PetMapController(_petProgress, _settings,
        () => Volatile.Read(ref _gameMapId), new WindowInteropHelper(this).Handle, _playerPosition.Snapshot);
}
