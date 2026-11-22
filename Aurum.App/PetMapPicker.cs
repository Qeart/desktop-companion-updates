using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;

namespace Aurum;
public sealed class PetMapPicker : Window
{
    private sealed record Choice(PetDefinition Pet, string Caption);
    private readonly PetProgress _progress;
    private readonly PetMapCatalog _maps;
    private readonly PetDefinition[] _pets;
    private readonly UserSettings _settings;
    private readonly TextBox _search = new();
    private readonly CheckBox _missing = new() { Content = "Poniżej MAX", IsChecked = true };
    private readonly CheckBox _inMap = new() { Content = "W tej krainie", IsChecked = true };
    private readonly ListBox _list = new();
    private readonly TextBlock _region = new(), _note = new();
    private readonly Dictionary<string, CheckBox> _genera = new();
    private int _mapId, _version = -1;
    private bool _updating;
    public PetDefinition? SelectedPet => (_list.SelectedItem as Choice)?.Pet;
    public event Action? SelectionChanged;
    public event Action? HideRequested;
    private static Brush Brush(string colour) => (Brush)new BrushConverter().ConvertFromString(colour)!;
    public PetMapPicker(PetProgress progress, PetMapCatalog maps, UserSettings settings)
    {
        _progress = progress; _maps = maps; _settings = settings;
        _pets = System.Text.Json.JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"))) ?? [];
        Title = "Aurum • wybierz peta"; Width = 294; Height = 410; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Foreground = Brush("#F6F0E5"); FontFamily = new FontFamily("Segoe UI");
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        var panel = new DockPanel { LastChildFill = true };
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = "AURUM • PETY NA MAPIE", Foreground = Brush("#E6BE6A"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Padding = new Thickness(6, 0, 6, 2), FontSize = 16 };
        close.Click += (_, _) => HideRequested?.Invoke(); Grid.SetColumn(close, 1); heading.Children.Add(close); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        _region.FontSize = 11; _region.Margin = new Thickness(0, 0, 0, 8); _region.Foreground = Brush("#B7AE9A"); DockPanel.SetDock(_region, Dock.Top); panel.Children.Add(_region);
        var searchLabel = new TextBlock { Text = "Szukaj peta", FontSize = 11, Margin = new Thickness(0, 0, 0, 3) }; DockPanel.SetDock(searchLabel, Dock.Top); panel.Children.Add(searchLabel);
        _search.Background = Brush("#191A1D"); _search.Foreground = Foreground; _search.BorderBrush = Brush("#665438"); _search.Padding = new Thickness(6, 4, 6, 4);
        _search.TextChanged += (_, _) => Rebuild(); DockPanel.SetDock(_search, Dock.Top); panel.Children.Add(_search);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var check in new[] { _missing, _inMap })
        {
            check.Foreground = Foreground; check.FontSize = 11; check.Margin = new Thickness(0, 0, 10, 0);
            check.Checked += (_, _) => Rebuild(); check.Unchecked += (_, _) => Rebuild(); filters.Children.Add(check);
        }
        DockPanel.SetDock(filters, Dock.Top); panel.Children.Add(filters);
        var typeLabel = new TextBlock { Text = "Rodzaje petów", FontSize = 11, Margin = new Thickness(0, 0, 0, 5) };
        DockPanel.SetDock(typeLabel, Dock.Top); panel.Children.Add(typeLabel);
        var types = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var chosen = new HashSet<string>(_settings.MapPetGenera ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var genus in PetGenusFilter.Genera)
        {
            var check = new CheckBox { Content = char.ToUpperInvariant(genus[0]) + genus[1..], IsChecked = chosen.Contains(genus),
                Foreground = Foreground, FontSize = 11, Width = 86, Margin = new Thickness(0, 0, 0, 5) };
            _genera[genus] = check;
            check.Checked += (_, _) => SaveGenusFilter(); check.Unchecked += (_, _) => SaveGenusFilter(); types.Children.Add(check);
        }
        DockPanel.SetDock(types, Dock.Top); panel.Children.Add(types);
        _note.Text = "Wybierz peta, aby zaznaczyć obszary."; _note.TextWrapping = TextWrapping.Wrap; _note.FontSize = 10; _note.Foreground = Brush("#B7AE9A"); _note.Margin = new Thickness(0, 7, 0, 0);
        DockPanel.SetDock(_note, Dock.Bottom); panel.Children.Add(_note);
        _list.Background = Brush("#17181B"); _list.Foreground = Foreground; _list.BorderBrush = Brush("#443B2C"); _list.DisplayMemberPath = "Caption";
        _list.FontSize = 11; _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _list.Resources[SystemColors.HighlightBrushKey] = Brush("#554222"); _list.Resources[SystemColors.HighlightTextBrushKey] = Foreground;
        if (Application.Current.TryFindResource("AurumVerticalScrollBar") is Style scroll) _list.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scroll);
        _list.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            if (SelectedPet is { } pet) { _settings.SelectedMapPetId = pet.Id; _settings.Save(); }
            UpdateNote(); SelectionChanged?.Invoke();
        };
        panel.Children.Add(_list);
        Content = new Border { Background = Brush("#F5111214"), BorderBrush = Brush("#776047"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = panel };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, Native.GetWindowLongPtr(hwnd, -20) | 0x80);
        };
    }
    public void SetContext(int mapId)
    {
        if (_mapId == mapId && _version == _progress.Version) return;
        _mapId = mapId; _version = _progress.Version; _region.Text = _maps.Map(mapId)?.Name ?? "Nieznana kraina"; Rebuild();
    }
    private void Rebuild()
    {
        if (_updating || _list == null) return;
        _updating = true;
        try
        {
            var selected = SelectedPet?.Id ?? _settings.SelectedMapPetId;
            var query = _search.Text.Trim();
            var choices = _pets.Where(p => (query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                && (_missing.IsChecked != true || (_progress.State(p.Id)?.Level ?? 0) < 3)
                && PetGenusFilter.Includes(p, _genera.Where(g => g.Value.IsChecked == true).Select(g => g.Key))
                && (_inMap.IsChecked != true || _maps.HasSpawns(_mapId, p)))
                .OrderBy(p => p.Name).Select(p => new Choice(p, p.Name + " · " + ShopPetMatcher.Counter(_progress.State(p.Id)))).ToArray();
            _list.ItemsSource = choices;
            _list.SelectedItem = choices.FirstOrDefault(p => p.Pet.Id == selected) ?? choices.FirstOrDefault();
            UpdateNote();
        }
        finally { _updating = false; }
        SelectionChanged?.Invoke();
    }
    private void UpdateNote()
    {
        if (SelectedPet is not { } pet) { _note.Text = "Brak petów pasujących do filtrów."; return; }
        var areas = _maps.Areas(_mapId, pet);
        _note.Text = areas.Count == 0 ? "Brak znanych spawnów w tej krainie."
            : $"{areas.Sum(a => a.Points)} miejsc w {areas.Count} obszarach. Złote okręgi wskazują występowanie mobków.";
    }
    private void SaveGenusFilter()
    {
        _settings.MapPetGenera = _genera.Where(g => g.Value.IsChecked == true).Select(g => g.Key).ToArray();
        _settings.Save(); Rebuild();
    }
    public void RenderSample(string path)
    {
        SetContext(1110); Show(); UpdateLayout();
        var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        target.Render((Visual)Content); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file); Close();
    }
}
