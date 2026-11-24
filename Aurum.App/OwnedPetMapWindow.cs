using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;

namespace Aurum;

public sealed class OwnedPetMapWindow : Window
{
    private sealed record Choice(PetDefinition Pet, string Caption, ImageSource? Portrait);
    private readonly PetProgress _progress;
    private readonly UserSettings _settings;
    private readonly PetMapCatalog _catalog;
    private readonly PetDefinition[] _pets;
    private readonly Func<PlayerMapPosition?> _position;
    private readonly OwnedMapSurface _surface = new();
    private readonly ComboBox _regions = new() { Width = 230, FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly CheckBox _missing = new() { Content = "Poniżej MAX", IsChecked = true }, _local = new() { Content = "W tej krainie", IsChecked = true };
    private readonly CheckBox _follow = new() { Content = "Śledź gracza" };
    private readonly Dictionary<string, CheckBox> _genera = new();
    private readonly Dictionary<string, TextBlock> _genusCounts = new();
    private readonly Dictionary<int, ImageSource?> _portraits = new();
    private readonly Button _allTypes = new();
    private bool _filterBatch;
    private readonly TextBlock _playerText = new(), _zoomText = new(), _petText = new(), _areasText = new(), _countText = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private int _version = -1;
    private bool _rebuilding, _disposing;
    private long _lastStatus;
    public event Action? HideRequested;
    private static Brush Ink(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Text(string value, int size = 12, string color = "#B5AE9E") => new() { Text = value, FontSize = size, Foreground = Ink(color), TextWrapping = TextWrapping.Wrap };
    public object DiagnosticState => new { Mode = "OwnedMap", Visible = IsVisible, Surface = _surface.DiagnosticState,
        Player = _position(), SelectedPet = (_list.SelectedItem as Choice)?.Pet.Name };
    public OwnedPetMapWindow(PetProgress progress, UserSettings settings, Func<PlayerMapPosition?> position)
    {
        _progress = progress; _settings = settings; _position = position;
        _catalog = PetMapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-map-spawns.json"));
        _pets = JsonSerializer.Deserialize<PetDefinition[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pets.json"))) ?? [];
        Title = "Aurum • Mapa petów"; Width = 1400; Height = 860; MinWidth = 900; MinHeight = 580;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; Background = Ink("#101216");
        Foreground = Ink("#F5EEDC"); FontFamily = new FontFamily("Segoe UI"); ShowInTaskbar = false; Topmost = true;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(72) }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(35) });
        var header = new Grid { Background = Ink("#16191D"), Margin = new Thickness(1, 1, 1, 0) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Margin = new Thickness(22, 12, 12, 10) };
        brand.Children.Add(Text("A U R U M   /   ATLAS PETÓW", 18, "#E6BE6A")); brand.Children.Add(Text("Miejsca występowania · Twoja kolekcja", 11)); header.Children.Add(brand);
        _regions.Background = Ink("#24272D"); _regions.Foreground = Foreground; _regions.BorderBrush = Ink("#5C4B2E");
        _regions.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
              <Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#24272D" BorderBrush="#655335" BorderThickness="1" CornerRadius="5"><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="26"/></Grid.ColumnDefinitions>
                  <TextBlock Margin="10,7,4,7" Foreground="#F5EEDC" TextTrimming="CharacterEllipsis" Text="{Binding SelectedItem.Name, RelativeSource={RelativeSource AncestorType=ComboBox}}"/><TextBlock Grid.Column="1" Text="▾" Foreground="#E6BE6A" VerticalAlignment="Center"/>
                </Grid></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton><Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="#202328" BorderBrush="#655335" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ComboBox}}" MaxHeight="350"><ScrollViewer CanContentScroll="True"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid>
            </ControlTemplate>
            """);
        _regions.ItemsSource = _catalog.Maps.OrderBy(m => m.Name); _regions.DisplayMemberPath = "Name"; _regions.Margin = new Thickness(0, 0, 18, 0); _regions.VerticalAlignment = VerticalAlignment.Center;
        var itemStyle = new Style(typeof(ComboBoxItem)); itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Ink("#202328"))); itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Foreground)); _regions.ItemContainerStyle = itemStyle;
        Grid.SetColumn(_regions, 1); header.Children.Add(_regions);
        var close = new Button { Content = "✕", Margin = new Thickness(0, 16, 18, 16), Padding = new Thickness(12, 5, 12, 5), ToolTip = "Zamknij mapę · Esc" };
        close.Click += (_, _) => HideRequested?.Invoke(); Grid.SetColumn(close, 2); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) DragMove(); }; root.Children.Add(header);
        var main = new Grid { Margin = new Thickness(1, 0, 1, 0) }; main.ColumnDefinitions.Add(new()); main.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); Grid.SetRow(main, 1); root.Children.Add(main);
        var viewport = new Grid { ClipToBounds = true }; viewport.Children.Add(_surface); main.Children.Add(viewport);
        var controls = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14) };
        AddButton(controls, "+", () => _surface.Zoom(1.25)); AddButton(controls, "−", () => _surface.Zoom(.8)); AddButton(controls, "Cała mapa", () => { _surface.FollowPlayer = false; _surface.Fit(); });
        AddButton(controls, "Do peta", () => _surface.FrameAreas());
        AddButton(controls, "Na mnie", () => _surface.CenterPlayer()); viewport.Children.Add(controls);
        var compass = Text("N\n↑", 16, "#E6BE6A"); compass.TextAlignment = TextAlignment.Center; compass.HorizontalAlignment = HorizontalAlignment.Left; compass.VerticalAlignment = VerticalAlignment.Top; compass.Margin = new Thickness(20, 18, 0, 0); viewport.Children.Add(compass);
        var badge = new Border { Background = Ink("#E614171C"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16) };
        var badgeStack = new StackPanel(); _petText.FontSize = 14; _petText.Foreground = Ink("#F1CE7D"); badgeStack.Children.Add(_petText); _areasText.Foreground = Ink("#B5AE9E"); _areasText.FontSize = 11; badgeStack.Children.Add(_areasText); badge.Child = badgeStack; viewport.Children.Add(badge);
        var sidebar = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 16, 16, 12) }; var sideBackground = new Border { Background = Ink("#16191D"), BorderBrush = Ink("#3E382C"), BorderThickness = new Thickness(1, 0, 0, 0), Child = sidebar }; Grid.SetColumn(sideBackground, 1); main.Children.Add(sideBackground);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); sidebar.Children.Add(top);
        top.Children.Add(Text("TWOJA KOLEKCJA", 11, "#E6BE6A")); _countText.Margin = new Thickness(0, 3, 0, 14); _countText.FontSize = 11; _countText.Foreground = Ink("#B5AE9E"); top.Children.Add(_countText);
        _search.Background = Ink("#0E1115"); _search.Foreground = Foreground; _search.BorderBrush = Ink("#51452F"); _search.Padding = new Thickness(9, 7, 9, 7); _search.FontSize = 13; _search.ToolTip = "Szukaj peta";
        top.Children.Add(Text("Szukaj peta", 11)); _search.Margin = new Thickness(0, 5, 0, 10); top.Children.Add(_search); _search.TextChanged += (_, _) => Rebuild();
        var pillStyle = MapFilterAppearance.Style(false); var tileStyle = MapFilterAppearance.Style(true);
        _missing.Content = "Do zdobycia"; _missing.ToolTip = "Pety poniżej maksymalnego poziomu"; _local.Content = "Ta kraina";
        var general = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 17) };
        foreach (var check in new[] { _missing, _local }) { check.Style = pillStyle; check.Margin = new Thickness(0, 0, check == _missing ? 6 : 0, 0); check.Checked += (_, _) => Rebuild(); check.Unchecked += (_, _) => Rebuild(); general.Children.Add(check); } top.Children.Add(general);
        var typeHeading = new DockPanel(); _allTypes.Content = "Wszystkie"; _allTypes.Padding = new Thickness(7, 2, 7, 2); _allTypes.FontSize = 10; _allTypes.Background = Brushes.Transparent; _allTypes.BorderThickness = new Thickness(0); DockPanel.SetDock(_allTypes, Dock.Right); typeHeading.Children.Add(_allTypes); typeHeading.Children.Add(Text("RODZAJE PETÓW", 10, "#8D97A6")); top.Children.Add(typeHeading);
        _allTypes.Click += (_, _) => { var select = _genera.Values.Any(c => c.IsChecked != true); _filterBatch = true; foreach (var check in _genera.Values) check.IsChecked = select; _filterBatch = false; SaveFilter(); };
        var types = new Grid { Margin = new Thickness(0, 8, 0, 16) }; types.ColumnDefinitions.Add(new()); types.ColumnDefinitions.Add(new());
        for (var row = 0; row < 3; row++) types.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var chosen = new HashSet<string>(settings.MapPetGenera ?? [], StringComparer.OrdinalIgnoreCase);
        var typeIndex = 0;
        foreach (var genus in PetGenusFilter.Genera)
        {
            var count = new TextBlock { Text = "0" }; _genusCounts[genus] = count;
            var check = new CheckBox { Content = MapFilterAppearance.TileContent(genus, count), IsChecked = chosen.Contains(genus), Style = tileStyle, Margin = new Thickness(0, 0, typeIndex % 2 == 0 && typeIndex != 4 ? 6 : 0, 6), ToolTip = "Filtruj: " + genus };
            Grid.SetColumn(check, typeIndex % 2); Grid.SetRow(check, typeIndex / 2); if (typeIndex == 4) Grid.SetColumnSpan(check, 2);
            _genera[genus] = check; check.Checked += (_, _) => SaveFilter(); check.Unchecked += (_, _) => SaveFilter(); types.Children.Add(check); typeIndex++;
        }
        top.Children.Add(types);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        _follow.Style = pillStyle; _follow.Checked += (_, _) => { _surface.FollowPlayer = true; _surface.CenterPlayer(); }; _follow.Unchecked += (_, _) => _surface.FollowPlayer = false; bottom.Children.Add(_follow);
        _playerText.FontSize = 11; _playerText.Foreground = Ink("#B5AE9E"); _playerText.TextWrapping = TextWrapping.Wrap; _playerText.Margin = new Thickness(0, 7, 0, 0); bottom.Children.Add(_playerText);
        _list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid ToolTip="{Binding Caption}"><Grid.ColumnDefinitions><ColumnDefinition Width="42"/><ColumnDefinition/></Grid.ColumnDefinitions><Border Width="34" Height="34" HorizontalAlignment="Left" CornerRadius="5" Background="#171B22" BorderBrush="#5A4B33" BorderThickness="1"><Image Source="{Binding Portrait}" Width="32" Height="32" Stretch="UniformToFill"/></Border><TextBlock Grid.Column="1" Text="{Binding Caption}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/></Grid></DataTemplate>
            """);
        _list.Background = Ink("#101318"); _list.Foreground = Foreground; _list.BorderBrush = Ink("#38382F"); _list.FontSize = 12; _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var rows = new Style(typeof(ListBoxItem)); rows.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5))); rows.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); _list.ItemContainerStyle = rows;
        rows.Setters.Add(new Setter(Control.TemplateProperty, System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem"><Border Name="Row" Padding="{TemplateBinding Padding}" BorderThickness="2,0,0,0" BorderBrush="Transparent" Background="Transparent"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="#342A1B"/><Setter TargetName="Row" Property="BorderBrush" Value="#E6BE6A"/><Setter Property="Foreground" Value="#FFE0A1"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Row" Property="Background" Value="#252930"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
            """)));
        _list.Resources[SystemColors.HighlightBrushKey] = Ink("#5A4827"); _list.Resources[SystemColors.HighlightTextBrushKey] = Foreground;
        if (Application.Current.TryFindResource("AurumVerticalScrollBar") is Style scroll) _list.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scroll);
        _list.SelectionChanged += (_, _) => { if (!_rebuilding) { if (_list.SelectedItem is Choice selected) { _settings.SelectedMapPetId = selected.Pet.Id; _settings.Save(); } SelectPet(); } }; sidebar.Children.Add(_list);
        var footer = new DockPanel { Background = Ink("#101216"), Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        _zoomText.FontSize = 11; _zoomText.Foreground = Ink("#D9B873"); DockPanel.SetDock(_zoomText, Dock.Right); footer.Children.Add(_zoomText);
        footer.Children.Add(Text("Przeciągnij mapę · Rolka: przybliżenie · Shift + klik: kotwica testowa · Esc: zamknij", 11)); Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { BorderBrush = Ink("#7C6540"), BorderThickness = new Thickness(1), Child = root };
        _regions.SelectionChanged += (_, _) => { if (_regions.SelectedItem is PetWorldMap map) { _surface.SetMap(map); _settings.OwnMapRegion = map.Id; _settings.Save(); Rebuild(); } };
        _surface.CameraChanged += () => { _zoomText.Text = $"{_surface.Camera.Scale * 100:0}%"; if (_follow.IsChecked != _surface.FollowPlayer) _follow.IsChecked = _surface.FollowPlayer; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { HideRequested?.Invoke(); e.Handled = true; } };
        Closing += (_, e) => { if (!_disposing) { e.Cancel = true; HideRequested?.Invoke(); } };
        _refresh.Tick += (_, _) => RefreshData(); IsVisibleChanged += (_, _) => { if (IsVisible) { _refresh.Start(); RefreshData(); } else _refresh.Stop(); };
    }
    private static void AddButton(Panel panel, string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(10, 6, 10, 6), MinWidth = 38 };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    public void Open(int region)
    {
        var map = _catalog.Map(region) ?? _catalog.Map(_settings.OwnMapRegion) ?? _catalog.Map(1110) ?? _catalog.Maps.First();
        _regions.SelectedItem = map;
        if (!IsVisible) Show(); Activate(); RefreshData();
    }
    public void OpenPet(int petId, int mapId)
    {
        var pet = _pets.FirstOrDefault(p => p.Id == petId); if (pet == null) return;
        _rebuilding = true; _filterBatch = true;
        try
        {
            _search.Text = "";
            if (_progress.State(petId)?.Level >= 3) _missing.IsChecked = false;
            if (_genera.TryGetValue(pet.Genus, out var genus)) genus.IsChecked = true;
        }
        finally { _rebuilding = false; _filterBatch = false; }
        Open(mapId); Rebuild();
        _list.SelectedItem = (_list.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault(p => p.Pet.Id == petId);
        _settings.SelectedMapPetId = petId; _settings.MapPetGenera = _genera.Where(g => g.Value.IsChecked == true).Select(g => g.Key).ToArray(); _settings.Save();
        _surface.UpdateLayout(); _surface.FrameAreas();
    }
    private void SaveFilter() { if (_filterBatch) return; _settings.MapPetGenera = _genera.Where(g => g.Value.IsChecked == true).Select(g => g.Key).ToArray(); _settings.Save(); Rebuild(); }
    private void Rebuild()
    {
        if (_rebuilding || _surface.MapId == 0) return;
        _rebuilding = true;
        try
        {
            var selected = (_list.SelectedItem as Choice)?.Pet.Id ?? _settings.SelectedMapPetId; var query = _search.Text.Trim();
            var available = _pets.Where(p => (query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                && (_missing.IsChecked != true || (_progress.State(p.Id)?.Level ?? 0) < 3)
                && (_local.IsChecked != true || _catalog.HasSpawns(_surface.MapId, p))).ToArray();
            foreach (var genus in _genusCounts.Keys) _genusCounts[genus].Text = available.Count(p => p.Genus == genus).ToString();
            _allTypes.Content = _genera.Values.All(c => c.IsChecked == true) ? "Wyczyść" : "Wszystkie";
            var choices = available.Where(p => PetGenusFilter.Includes(p, _genera.Where(g => g.Value.IsChecked == true).Select(g => g.Key)))
                .OrderBy(p => p.Name).Select(p => new Choice(p, p.Name + "  ·  " + ShopPetMatcher.Counter(_progress.State(p.Id)), Portrait(p.Id))).ToArray();
            _list.ItemsSource = choices; _list.SelectedItem = choices.FirstOrDefault(p => p.Pet.Id == selected) ?? choices.FirstOrDefault();
            _countText.Text = $"{choices.Length} pasujących petów";
        }
        finally { _rebuilding = false; }
        SelectPet();
    }
    private void SelectPet()
    {
        var pet = (_list.SelectedItem as Choice)?.Pet; var areas = pet == null ? [] : _catalog.Areas(_surface.MapId, pet);
        _surface.SetAreas(areas); _petText.Text = pet?.Name ?? "Wybierz peta";
        _areasText.Text = pet == null ? "Zmień filtry, aby wyświetlić pety." : areas.Count == 0 ? "Brak znanych miejsc występowania w tej krainie." : $"{areas.Sum(a => a.Points)} miejsc w {areas.Count} obszarach";
    }
    private ImageSource? Portrait(int petId)
    {
        if (_portraits.TryGetValue(petId, out var cached)) return cached;
        var file = Path.Combine(AppContext.BaseDirectory, "data", "pet-icons", petId + ".png");
        ImageSource? result = null;
        if (File.Exists(file))
            try
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 40; image.UriSource = new Uri(file, UriKind.Absolute); image.EndInit(); image.Freeze(); result = image;
            }
            catch (Exception error) when (error is IOException or NotSupportedException) { }
        _portraits[petId] = result; return result;
    }
    private void RefreshData()
    {
        if (_version != _progress.Version) { _version = _progress.Version; Rebuild(); }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var player = _position(); _surface.SetPlayer(player, now);
        _playerText.Text = player == null ? "Pozycja gracza: oczekiwanie na dane.\nUżyj umiejętności lub mantry."
            : player.MapId != _surface.MapId ? "Gracz znajduje się w innej krainie."
            : player.Fresh(now) ? "● Twoja pozycja · przed chwilą"
            : $"◌ Ostatnia pozycja · {player.Age(now) / 1000} s temu\nUżyj umiejętności, aby ją odświeżyć.";
        if (now - _lastStatus < 1000) return; _lastStatus = now;
        var json = JsonSerializer.Serialize(new { UpdatedAt = DateTimeOffset.UtcNow, Scene = DiagnosticState });
        _ = Task.Run(() => { try { Directory.CreateDirectory(UserSettings.DataDirectory); File.WriteAllText(Path.Combine(UserSettings.DataDirectory, "owned-map-status.json"), json); } catch (IOException) { } });
    }
    public void DisposeWindow() { _disposing = true; _refresh.Stop(); Close(); }
    public void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory); Width = 1320; Height = 820;
        _regions.SelectedItem = _catalog.Map(1110); var content = (FrameworkElement)Content;
        content.Measure(new Size(1320, 820)); content.Arrange(new Rect(0, 0, 1320, 820)); content.UpdateLayout(); _surface.Fit(); RefreshData();
        var anchor = _surface.Camera.Unproject(_surface.ActualWidth * .52, _surface.ActualHeight * .48); _surface.SetTestAnchor(anchor.X, anchor.Y);
        void Save(string name)
        {
            content.UpdateLayout(); var image = new RenderTargetBitmap(1320, 820, 96, 96, PixelFormats.Pbgra32); image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
        }
        Save("before"); _surface.Camera.Pan(160, -90); _surface.InvalidateVisual(); Save("panned");
        _surface.Camera.Zoom(1.3, 450, 340); _surface.InvalidateVisual(); Save("zoomed");
        _surface.Camera.Zoom(1 / 1.3, 450, 340); _surface.Camera.Pan(-160, 90); _surface.InvalidateVisual(); Save("returned");
        File.WriteAllText(Path.Combine(directory, "scene.json"), JsonSerializer.Serialize(DiagnosticState));
        DisposeWindow();
    }
}
