using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;

namespace Aurum;

internal static class PetTrackerText
{
    internal static string Count(int value, string one, string few, string many)
        => value.ToString("N0") + " " + (value == 1 ? one : value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? few : many);
}

public sealed class PetTrackerRow : INotifyPropertyChanged
{
    public PetTrackerEntry Entry { get; private set; }
    public string Name => Entry.Pet.Name;
    public ImageSource? Portrait { get; }
    public Brush GenusBrush { get; }
    public string Levels => new string('◆', Entry.State.Level) + new string('◇', 3 - Entry.State.Level);
    public string Counter => ShopPetMatcher.Counter(Entry.State);
    public string Remaining => Entry.Remaining + " do MAX";
    public double Fraction => PetAppearance.Fraction(Entry.State);
    public string Distance { get; private set; } = "—";
    public string Tip => Name + "\n" + Counter + " · " + PetTrackerText.Count(Entry.Remaining, "dusza", "dusze", "dusz") + " do MAX\n" +
        (Entry.Distance == null ? "Brak aktualnej pozycji lub miejsca występowania w tej krainie."
            : "Dystans do znanego miejsca występowania; strzałka względem północy." + (Entry.Distance.Estimated ? " Pozycja gracza szacowana." : "")) +
        (Entry.MapId > 0 ? "\nKliknij, aby otworzyć na mapie." : "");
    public event PropertyChangedEventHandler? PropertyChanged;
    public PetTrackerRow(PetTrackerEntry entry)
    {
        Entry = entry; Portrait = PetAppearance.Portrait(entry.Pet.Id);
        GenusBrush = PetAppearance.Ink(entry.Pet.Genus switch { "cogni" => "#B28CDA", "fera" => "#D88B72", "natura" => "#83C49A", "varian" => "#91A8E0", _ => "#E1BE77" });
        Apply(entry);
    }
    public void Apply(PetTrackerEntry entry)
    {
        var direction = entry.Distance is { } d ? new[] { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" }[(int)Math.Round(d.Bearing / 45) % 8] : "";
        var distance = entry.Distance is { } value ? direction + " ≈ " + (value.Metres >= 1000 ? (value.Metres / 1000).ToString("0.0") + " km" : value.Metres.ToString("0") + " m") : "—";
        var changed = Entry.State != entry.State || Entry.MapId != entry.MapId || Distance != distance || Entry.Distance?.Estimated != entry.Distance?.Estimated;
        Entry = entry; Distance = distance;
        if (changed) PropertyChanged?.Invoke(this, new(null));
    }
}

public partial class PetTrackerWindow : Window, IDisposable
{
    private sealed record RegionChoice(int Id, string Name);
    private sealed record Choice(PetDefinition Pet, string Counter, bool Selected, ImageSource? Portrait);
    private readonly PetProgress _progress;
    private readonly UserSettings _settings;
    private readonly PetMapCatalog _maps;
    private readonly PetTracker _tracker;
    private readonly Func<int> _map;
    private readonly Func<long, PositionTrackingSnapshot> _position;
    private readonly ObservableCollection<PetTrackerRow> _rows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _placement = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Popup _chooser = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom };
    private readonly TextBox _search = new() { Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 7, 0, 8), Background = PetAppearance.Ink("#10151B"), Foreground = PetAppearance.Ink("#E9DFC9"), BorderBrush = PetAppearance.Ink("#6B5635") };
    private readonly ListBox _choices = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _pickerMissing = new() { Content = "Tylko poniżej MAX", IsChecked = true, Foreground = PetAppearance.Muted, FontSize = 10, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _selectionCount = new() { Foreground = PetAppearance.Muted, FontSize = 10, Margin = new Thickness(0, 7, 0, 0) };
    private readonly bool _preview;
    private bool _updating, _disposed;
    private int _region = -1, _pickerVersion = -1;
    private PetTrackerView? _view;
    public event Action? HideRequested;
    public event Action<int, int>? MapRequested;
    public object DiagnosticState => new { Requested = _settings.ShowPetTracker, Visible = IsVisible, Automatic = _settings.PetTrackerAutomatic,
        Region = _view?.Region, _view?.Synced, _view?.FromCache, Count = _view?.Entries.Count, SoulsRemaining = _view?.Remaining,
        Selected = _settings.TrackedPetIds, PositionState = _position(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).State.ToString() };

    public PetTrackerWindow(PetProgress progress, UserSettings settings, Func<int> map, Func<long, PositionTrackingSnapshot> position, bool preview = false)
    {
        _progress = progress; _settings = settings; _map = map; _position = position; _preview = preview;
        _settings.TrackedPetIds ??= [];
        _maps = PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json"));
        var pets = JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"))) ?? [];
        _tracker = new(pets, _maps);
        _updating = true; InitializeComponent(); Icon = AurumBrand.Image; Brand.Source = AurumBrand.Image; PetList.ItemsSource = _rows;
        _pickerMissing.Style = (Style)FindResource("TrackerSwitch");
        Automatic.IsChecked = settings.PetTrackerAutomatic;
        var area = SystemParameters.WorkArea;
        Width = double.IsFinite(settings.PetTrackerWidth) ? Math.Clamp(settings.PetTrackerWidth, MinWidth, 620) : 326;
        Height = double.IsFinite(settings.PetTrackerHeight) ? Math.Clamp(settings.PetTrackerHeight, MinHeight, Math.Max(MinHeight, area.Height)) : 468;
        Left = Coordinate(settings.PetTrackerLeft ?? area.Right - Width - 24, area.Left, area.Right - Width);
        Top = Coordinate(settings.PetTrackerTop ?? area.Top + 285, area.Top, area.Bottom - Height);
        ApplyLock(); BuildChooser(); _updating = false;
        _placement.Tick += (_, _) => { _placement.Stop(); SavePlacement(); };
        LocationChanged += (_, _) => QueuePlacement(); SizeChanged += (_, _) => QueuePlacement();
        Closing += (_, e) => { if (!_disposed) { e.Cancel = true; RequestHide(); } };
        _timer.Tick += (_, _) => Refresh(); if (!preview) _timer.Start();
        Refresh();
    }
    private static double Coordinate(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, Math.Max(min, max)) : min;
    private void BuildChooser()
    {
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(11) };
        var top = new StackPanel(); top.Children.Add(new TextBlock { Text = "WŁASNA LISTA", Foreground = PetAppearance.Gold, FontWeight = FontWeights.SemiBold, FontSize = 12 });
        top.Children.Add(new TextBlock { Text = "Szukaj po nazwie peta lub potwora", Foreground = PetAppearance.Muted, FontSize = 9, Margin = new Thickness(0, 5, 0, 0) }); top.Children.Add(_search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var all = new Button { Content = "Zaznacz znalezione", Padding = new Thickness(6, 4, 6, 4), FontSize = 10 };
        var clear = new Button { Content = "Wyczyść", Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(6, 0, 0, 0), FontSize = 10 };
        all.Click += (_, _) => { var ids = _settings.TrackedPetIds.ToList(); foreach (var pet in FilteredChoices().Where(p => _progress.State(p.Id)?.Level != 3)) if (!ids.Contains(pet.Id)) ids.Add(pet.Id); SetSelection(ids); RebuildChooser(); };
        clear.Click += (_, _) => { SetSelection([]); RebuildChooser(); };
        buttons.Children.Add(all); buttons.Children.Add(clear); top.Children.Add(buttons); top.Children.Add(_pickerMissing); top.Children.Add(_selectionCount);
        DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var note = new TextBlock { Text = "Wybór przełącza tracker na własną listę.\nPety na MAX znikają z trackera automatycznie.", Foreground = PetAppearance.Muted, FontSize = 9, Margin = new Thickness(0, 7, 0, 0) };
        DockPanel.SetDock(note, Dock.Bottom); body.Children.Add(note); body.Children.Add(_choices);
        _chooser.PlacementTarget = ChooseButton;
        _chooser.Child = new Border { Width = 318, Height = 452, CornerRadius = new CornerRadius(9), Background = PetAppearance.Ink("#10151B"), BorderBrush = PetAppearance.Ink("#A58348"), BorderThickness = new Thickness(1), Child = body };
        ((Grid)((Border)Content).Child).Children.Add(_chooser);
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetValue(StyleProperty, MapFilterAppearance.Style(false));
        check.SetValue(MarginProperty, new Thickness(0, 2, 0, 2)); check.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Selected") { Mode = System.Windows.Data.BindingMode.OneWay });
        check.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ChoiceClick));
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        var portrait = new FrameworkElementFactory(typeof(Image)); portrait.SetValue(WidthProperty, 27d); portrait.SetValue(HeightProperty, 27d); portrait.SetValue(MarginProperty, new Thickness(0, 0, 8, 0)); portrait.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Portrait")); grid.AppendChild(portrait);
        var counter = new FrameworkElementFactory(typeof(TextBlock)); counter.SetValue(DockPanel.DockProperty, Dock.Right); counter.SetValue(ForegroundProperty, PetAppearance.Muted); counter.SetValue(FontSizeProperty, 10d); counter.SetValue(MarginProperty, new Thickness(8, 0, 0, 0)); counter.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Counter")); grid.AppendChild(counter);
        var caption = new FrameworkElementFactory(typeof(TextBlock)); caption.SetValue(ForegroundProperty, PetAppearance.Ink("#F4ECDC")); caption.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); caption.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Pet.Name")); grid.AppendChild(caption);
        check.AppendChild(grid); _choices.ItemTemplate = new DataTemplate { VisualTree = check };
        _choices.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        if (TryFindResource("AurumVerticalScrollBar") is Style scroll) { _choices.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scroll); PetList.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scroll); }
        _search.TextChanged += (_, _) => RebuildChooser();
        _pickerMissing.Checked += (_, _) => RebuildChooser(); _pickerMissing.Unchecked += (_, _) => RebuildChooser();
    }
    private IEnumerable<PetDefinition> FilteredChoices()
    {
        var query = _search.Text.Trim();
        return _tracker.Pets.Where(p => _pickerMissing.IsChecked != true || _progress.State(p.Id)?.Level != 3 || _settings.TrackedPetIds.Contains(p.Id))
            .Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Drops.Any(d => d.NpcName.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
    private void RebuildChooser()
    {
        var selected = _settings.TrackedPetIds.ToHashSet();
        _choices.ItemsSource = FilteredChoices().Select(p => new Choice(p, ShopPetMatcher.Counter(_progress.State(p.Id)), selected.Contains(p.Id), PetAppearance.Portrait(p.Id))).ToArray();
        _selectionCount.Text = "Wybrano: " + selected.Count;
        _pickerVersion = _progress.Version;
    }
    private void ChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: Choice choice } check) return;
        var selected = _settings.TrackedPetIds.ToList();
        if (check.IsChecked == true) { if (!selected.Contains(choice.Pet.Id)) selected.Add(choice.Pet.Id); }
        else selected.Remove(choice.Pet.Id);
        SetSelection(selected); e.Handled = true;
    }
    private void SetSelection(IEnumerable<int> ids)
    {
        _settings.TrackedPetIds = ids.Where(id => _tracker.Pets.Any(p => p.Id == id)).Distinct().Take(512).ToArray();
        _settings.PetTrackerAutomatic = false;
        _updating = true; Automatic.IsChecked = false; _updating = false;
        _selectionCount.Text = "Wybrano: " + _settings.TrackedPetIds.Length; Save(); Refresh();
    }
    private void ChooseClick(object sender, RoutedEventArgs e) { RebuildChooser(); _chooser.IsOpen = !_chooser.IsOpen; if (_chooser.IsOpen) _search.Focus(); }
    private void AutomaticChanged(object sender, RoutedEventArgs e)
    { if (_updating) return; _settings.PetTrackerAutomatic = Automatic.IsChecked == true; Save(); Refresh(); }
    private void RegionChanged(object sender, SelectionChangedEventArgs e)
    { if (_updating || RegionPicker.SelectedItem is not RegionChoice choice) return; _settings.PetTrackerRegion = choice.Id; Save(); Refresh(); }
    private void UpdateRegions(int map)
    {
        if (_region == map) return; _region = map; _updating = true;
        RegionPicker.ItemsSource = new[] { new RegionChoice(0, "Aktualnie: " + (_maps.Map(map)?.Name ?? "nieznana kraina")), new RegionChoice(-1, "Wszystkie krainy") }
            .Concat(_maps.Maps.Select(m => new RegionChoice(m.Id, m.Name))).ToArray();
        RegionPicker.SelectedItem = ((IEnumerable<RegionChoice>)RegionPicker.ItemsSource).FirstOrDefault(r => r.Id == _settings.PetTrackerRegion)
            ?? ((IEnumerable<RegionChoice>)RegionPicker.ItemsSource).First();
        _updating = false;
    }
    internal void Refresh()
    {
        if (_disposed) return;
        if (!_preview && (!_settings.ShowPetTracker || !GameWindowAccess.GameOrAurumForeground()))
        { _chooser.IsOpen = false; if (IsVisible) Hide(); return; }
        var map = _map(); UpdateRegions(map);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var position = _position(now);
        var collection = _progress.Snapshot(); var region = _settings.PetTrackerRegion > 0 ? _settings.PetTrackerRegion : map;
        _view = _tracker.Build(collection, new(_settings.PetTrackerAutomatic, _settings.PetTrackerRegion != -1, _settings.TrackedPetIds), region, position);
        var ids = _view.Entries.Select(r => r.Pet.Id).ToArray();
        if (!_rows.Select(r => r.Entry.Pet.Id).SequenceEqual(ids))
        {
            var previous = _rows.ToDictionary(r => r.Entry.Pet.Id); _rows.Clear();
            foreach (var entry in _view.Entries) _rows.Add(previous.GetValueOrDefault(entry.Pet.Id) ?? new(entry));
        }
        for (var i = 0; i < _rows.Count; i++) _rows[i].Apply(_view.Entries[i]);
        Automatic.Content = _settings.PetTrackerAutomatic ? "Wszystkie poniżej MAX" : "Własna lista petów";
        Summary.Text = _view.Synced ? PetTrackerText.Count(_rows.Count, "pet", "pety", "petów") + " · " + PetTrackerText.Count(_view.Remaining ?? 0, "dusza", "dusze", "dusz") + " do MAX" : "Czekam na dane kolekcji";
        Empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = !_view.Synced ? "Czekam na odczyt Twojej kolekcji petów."
            : !_settings.PetTrackerAutomatic && _settings.TrackedPetIds.Length == 0 ? "Kliknij +, aby wybrać pety."
            : _settings.PetTrackerRegion != -1 && !_view.HasSpawnData ? "Brak danych występowania w tej krainie.\nMożesz wybrać wszystkie krainy."
            : !_settings.PetTrackerAutomatic ? "Brak nieukończonych petów z Twojej listy w tym widoku."
            : "W tym widoku wszystkie pety mają MAX.";
        Footnote.Text = position.State == PositionState.Stale && _rows.Count > 0 ? "Kliknij peta → mapa · brak aktualnej pozycji" : "Kliknij peta → mapa · kierunki względem północy";
        if (_chooser.IsOpen && _pickerVersion != _progress.Version) RebuildChooser();
        if (!_preview && !IsVisible) Show();
    }
    private void PetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PetTrackerEntry entry })
        {
            if (entry.MapId <= 0) { Footnote.Text = "Brak znanych miejsc występowania tego peta."; return; }
            _chooser.IsOpen = false; MapRequested?.Invoke(entry.Pet.Id, entry.MapId);
        }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (_settings.PetTrackerLocked || e.ChangedButton != MouseButton.Left) return;
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase) return;
        DragMove(); QueuePlacement();
    }
    private void LockClick(object sender, RoutedEventArgs e) { _settings.PetTrackerLocked = !_settings.PetTrackerLocked; ApplyLock(); Save(); }
    private void ApplyLock() { ResizeMode = _settings.PetTrackerLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip; LockButton.Background = _settings.PetTrackerLocked ? PetAppearance.Ink("#6C542A") : Brushes.Transparent; }
    private void HideClick(object sender, RoutedEventArgs e) => RequestHide();
    private void RequestHide() { _settings.ShowPetTracker = false; _chooser.IsOpen = false; Hide(); Save(); HideRequested?.Invoke(); }
    private void QueuePlacement() { if (_updating || _disposed || _preview) return; _placement.Stop(); _placement.Start(); }
    private void SavePlacement()
    { _settings.PetTrackerLeft = Left; _settings.PetTrackerTop = Top; _settings.PetTrackerWidth = Width; _settings.PetTrackerHeight = Height; Save(); }
    private void Save() { if (!_preview) _settings.Save(); }
    internal void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory); Refresh();
        var content = (FrameworkElement)Content; content.Measure(new Size(Width, Height)); content.Arrange(new Rect(0, 0, Width, Height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, "pet-tracker.png")); encoder.Save(file);
        File.WriteAllText(Path.Combine(directory, "tracker-state.json"), JsonSerializer.Serialize(DiagnosticState));
        RebuildChooser(); var picker = (FrameworkElement)_chooser.Child;
        picker.Measure(new Size(318, 452)); picker.Arrange(new Rect(0, 0, 318, 452)); picker.UpdateLayout();
        var pickerImage = new RenderTargetBitmap(318, 452, 96, 96, PixelFormats.Pbgra32); pickerImage.Render(picker);
        var pickerEncoder = new PngBitmapEncoder(); pickerEncoder.Frames.Add(BitmapFrame.Create(pickerImage)); using var pickerFile = File.Create(Path.Combine(directory, "pet-selection.png")); pickerEncoder.Save(pickerFile);
    }
    public void Dispose() { if (_disposed) return; _placement.Stop(); if (!_preview) SavePlacement(); _disposed = true; _timer.Stop(); _chooser.IsOpen = false; Close(); }
}
