using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionMeter.Core.Bosses;
using Microsoft.Win32;

namespace Aurum;

internal sealed class BossSettingsWindow : Window
{
    private readonly BossAssistantController _controller;
    private readonly UserSettings _settings;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = PetAppearance.Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 14) };
    private readonly StackPanel _mechanics = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    internal BossSettingsWindow(BossAssistantController controller, UserSettings settings)
    {
        _controller = controller; _settings = settings; Title = "Aurum • Asystent bossów"; Icon = AurumBrand.Image;
        Width = 620; Height = 760; MinWidth = 470; MinHeight = 430; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = PetAppearance.Ink("#10151B"); Foreground = PetAppearance.Ink("#EFE7D6");
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(new TextBlock { Text = "ASYSTENT BOSSÓW", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = PetAppearance.Gold });
        root.Children.Add(new TextBlock { Text = "Ataki, odliczanie i ostrzeżenia podczas walki.", FontSize = 12, Foreground = PetAppearance.Muted, Margin = new Thickness(0, 4, 0, 8) });
        root.Children.Add(_status);
        Toggle(root, "Włącz asystenta bossów", settings.BossAssistantEnabled, v => settings.BossAssistantEnabled = v);
        var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 6) };
        Button(actions, "▶ Demonstracja", () => controller.Demo()); Button(actions, "Powrót do gry", () => controller.Live());
        Button(actions, "Odtwórz walkę…", () => {
            var open = new OpenFileDialog { Title = "Dziennik walki Aurum", Filter = "Dziennik bossów (*.jsonl)|*.jsonl", InitialDirectory = controller.JournalDirectory };
            if (open.ShowDialog(this) == true) Safe(() => controller.Replay(open.FileName));
        }); root.Children.Add(actions);
        Note(root, "DEMO zawiera fikcyjnego bossa. W grze odczytany atak i przewidywany timer mają osobne oznaczenia. Paski ≈ z profili społeczności mogą różnić się na obecnym patchu.");
        Heading(root, "POŁOŻENIE I WYGLĄD");
        Toggle(root, "Zablokuj nakładki · kliknięcia przechodzą do gry", settings.BossOverlayLocked, v => settings.BossOverlayLocked = v);
        Note(root, "Odblokuj, aby osobno przeciągnąć paski i duży komunikat ostrzeżenia. Oba elementy są bez ramki, jak w DBM.");
        Slider(root, "Rozmiar", .7, 1.6, settings.BossScale, v => settings.BossScale = v, v => $"{v:P0}");
        Slider(root, "Szerokość", 280, 560, settings.BossWidth, v => settings.BossWidth = v, v => $"{v:0} px");
        Slider(root, "Widoczność", .35, 1, settings.BossOpacity, v => settings.BossOpacity = v, v => $"{v:P0}");
        Heading(root, "OSTRZEŻENIA");
        Toggle(root, "Dźwięk ostrzeżenia", settings.BossSounds, v => settings.BossSounds = v);
        Slider(root, "Głośność", 0, 100, settings.BossVolume, v => settings.BossVolume = v, v => $"{v:0}%");
        var sound = new WrapPanel(); Button(sound, "Test dźwięku", controller.TestSound); root.Children.Add(sound);
        var thresholds = new WrapPanel { Margin = new Thickness(0, 8, 0, 2) };
        foreach (var seconds in new[] { 10d, 5, 3 }) Toggle(thresholds, seconds + " s", settings.BossWarningSeconds.Contains(seconds), on => {
            settings.BossWarningSeconds = (on ? settings.BossWarningSeconds.Append(seconds) : settings.BossWarningSeconds.Where(x => x != seconds)).Distinct().OrderDescending().ToArray();
        }); root.Children.Add(thresholds);
        var roles = new ComboBox { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6), Background = PetAppearance.Ink("#292D33"), Foreground = Brushes.Black };
        foreach (var (id, label) in new[] { ("dps", "Rola: DPS"), ("tank", "Rola: Tank"), ("healer", "Rola: Healer") }) roles.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        roles.SelectedIndex = settings.BossRole switch { "tank" => 1, "healer" => 2, _ => 0 };
        roles.SelectionChanged += (_, _) => { settings.BossRole = (string)((ComboBoxItem)roles.SelectedItem).Tag; controller.SaveOptions(); }; root.Children.Add(roles);
        Note(root, "Filtr roli działa dla mechanik przypisanych do danej roli w profilu. Obecne pomiary społeczności są wspólne dla wszystkich ról.");
        Heading(root, "PROFILE I OBSERWACJE");
        Toggle(root, "Timery z pomiarów społeczności", settings.BossCommunityTimers, v => settings.BossCommunityTimers = v);
        Toggle(root, "Timery wyuczone z moich walk · eksperymentalne", settings.BossLearnedTimers, v => settings.BossLearnedTimers = v);
        Toggle(root, "Zapisuj lokalne dzienniki walk z bossami", settings.BossJournal, v => settings.BossJournal = v);
        Note(root, "Dzienniki nie zawierają nicków, adresów ani pakietów. Analiza wymaga co najmniej dwóch walk i czterech stabilnych odstępów. Propozycja pomiaru nie jest potwierdzeniem mechaniki.");
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var analyze = new Button { Content = "Analizuj moje walki", Margin = new Thickness(0, 0, 7, 7) };
        analyze.Click += async (_, _) => { analyze.IsEnabled = false; try { await controller.Analyze(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = e.Message; } finally { analyze.IsEnabled = true; } }; tools.Children.Add(analyze);
        Button(tools, "Oznacz ostatni atak", controller.MarkLastCast);
        Button(tools, "Folder profili", () => Safe(() => { Directory.CreateDirectory(controller.ProfileDirectory); Process.Start(new ProcessStartInfo(controller.ProfileDirectory) { UseShellExecute = true }); }));
        Button(tools, "Folder dzienników", () => Safe(() => { Directory.CreateDirectory(controller.JournalDirectory); Process.Start(new ProcessStartInfo(controller.JournalDirectory) { UseShellExecute = true }); }));
        Button(tools, "Wczytaj profile ponownie", () => { controller.ReloadProfiles(); BuildMechanics(); }); root.Children.Add(tools);
        Heading(root, "MECHANIKI"); Note(root, "Odznacz mechanikę, aby wyłączyć jej pasek i ostrzeżenia. Własne profile JSON możesz dodać w folderze profili.");
        root.Children.Add(_mechanics); BuildMechanics();
        _timer.Tick += (_, _) => RefreshStatus(); _timer.Start(); Closed += (_, _) => _timer.Stop(); RefreshStatus();
    }
    private void RefreshStatus()
    {
        var state = _controller.State;
        _status.Text = $"{_controller.Mode} · {_controller.Catalog.Profiles.Count(p => !p.Synthetic)} profili · {state.Fights.Count} bossów · {state.Bars.Count} pasków\n" + _controller.Message;
        if (_controller.Catalog.Errors.Count > 0) _status.Text += "\nBłędy profili: " + string.Join("; ", _controller.Catalog.Errors.Take(3));
    }
    private void BuildMechanics()
    {
        _mechanics.Children.Clear();
        foreach (var profile in _controller.Catalog.Profiles.Where(p => !p.Synthetic).OrderBy(p => p.Name)) {
            var body = new StackPanel { Margin = new Thickness(12, 2, 8, 8) };
            foreach (var mechanic in profile.Mechanics) {
                var key = BossAssistantEngine.MechanicKey(profile, mechanic);
                Toggle(body, mechanic.Name + $" · {mechanic.Confidence:P0}", !_settings.DisabledBossMechanics.Contains(key), on => {
                    _settings.DisabledBossMechanics = (on ? _settings.DisabledBossMechanics.Where(k => k != key) : _settings.DisabledBossMechanics.Append(key)).Distinct().ToArray();
                });
            }
            _mechanics.Children.Add(new Expander { Header = profile.Name, Foreground = PetAppearance.Gold, Content = body, Margin = new Thickness(0, 5, 0, 5) });
        }
    }
    private void Toggle(Panel parent, string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Foreground = Foreground, FontSize = 12, Margin = new Thickness(0, 5, 14, 5), VerticalContentAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => { set(box.IsChecked == true); _controller.SaveOptions(); }; parent.Children.Add(box);
    }
    private void Slider(Panel parent, string label, double min, double max, double value, Action<double> set, Func<double, string> format)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 5) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(105) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(65) });
        row.Children.Add(new TextBlock { Text = label, Foreground = PetAppearance.Muted, FontSize = 11 });
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(double.IsFinite(value) ? value : min, min, max), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var text = new TextBlock { Text = format(slider.Value), Foreground = PetAppearance.Gold, FontSize = 11, TextAlignment = TextAlignment.Right }; Grid.SetColumn(text, 2); row.Children.Add(text);
        slider.ValueChanged += (_, _) => { set(slider.Value); text.Text = format(slider.Value); _controller.ApplyOptions(); };
        slider.PreviewMouseLeftButtonUp += (_, _) => _controller.SaveOptions(); slider.PreviewKeyUp += (_, _) => _controller.SaveOptions(); parent.Children.Add(row);
    }
    private static void Heading(Panel parent, string text) => parent.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = PetAppearance.Gold, FontSize = 11, Margin = new Thickness(0, 20, 0, 8) });
    private static void Note(Panel parent, string text) => parent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = PetAppearance.Muted, FontSize = 11, LineHeight = 17, Margin = new Thickness(0, 6, 0, 4) });
    private static void Button(Panel parent, string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(0, 0, 7, 7) }; button.Click += (_, _) => action(); parent.Children.Add(button); }
    private void Safe(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { System.Windows.MessageBox.Show(this, e.Message, "Aurum", MessageBoxButton.OK, MessageBoxImage.Information); } }
}
