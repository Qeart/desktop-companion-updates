using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aurum.Distribution;

namespace Aurum;

internal sealed class UpdatesWindow : Window
{
    private readonly DesktopUpdates _updates;
    private readonly UserSettings _settings;
    private readonly TextBlock _version = Label("", 12), _status = Label("", 14);
    private readonly ProgressBar _progress = new() { Height = 5, Maximum = 100, Margin = new Thickness(0, 13, 0, 16) };
    private readonly Button _check = Button("Sprawdź"), _download = Button("Pobierz"), _restart = Button("Uruchom ponownie");
    private readonly ComboBox _channel = new() { MinWidth = 150, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Action _restartApp;
    private bool _closed;

    internal UpdatesWindow(DesktopUpdates updates, UserSettings settings, Action restartApp)
    {
        _updates = updates; _settings = settings; _restartApp = restartApp;
        Title = "Aurum • aktualizacje"; Width = 465; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        Background = PetAppearance.Ink("#101419"); Foreground = PetAppearance.Ink("#F1E8D5"); FontFamily = new FontFamily("Segoe UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(Label("A U R U M  /  AKTUALIZACJE", 17, "#E8C77F"));
        _version.Margin = new Thickness(0, 9, 0, 18); panel.Children.Add(_version);
        var channelRow = new StackPanel { Orientation = Orientation.Horizontal };
        channelRow.Children.Add(Label("Kanał wydań", 12)); _channel.ItemsSource = new[] { "Stabilny", "Testowy" };
        _channel.SelectedIndex = updates.Channel == "beta" ? 1 : 0; channelRow.Children.Add(_channel); panel.Children.Add(channelRow);
        _channel.SelectionChanged += async (_, _) => {
            var channel = _channel.SelectedIndex == 1 ? "beta" : "stable";
            if (!_updates.ChangeChannel(channel)) return;
            _settings.UpdateChannel = channel; _settings.Save(); await _updates.Check();
        };
        var automatic = new CheckBox { Content = "Sprawdzaj automatycznie", IsChecked = settings.CheckUpdatesAutomatically,
            Foreground = Foreground, Margin = new Thickness(0, 14, 0, 0) };
        automatic.Checked += (_, _) => SaveAutomatic(true); automatic.Unchecked += (_, _) => SaveAutomatic(false); panel.Children.Add(automatic);
        _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(0, 20, 0, 0); panel.Children.Add(_status); panel.Children.Add(_progress);
        var buttons = new WrapPanel(); buttons.Children.Add(_check); buttons.Children.Add(_download); buttons.Children.Add(_restart); panel.Children.Add(buttons);
        _check.Click += async (_, _) => await _updates.Check(); _download.Click += async (_, _) => await _updates.Download();
        _restart.Click += (_, _) => _restartApp();
        var notes = Button("Opis wydań na GitHubie"); notes.Margin = new Thickness(0, 18, 0, 0); notes.HorizontalAlignment = HorizontalAlignment.Left;
        notes.Click += (_, _) => Process.Start(new ProcessStartInfo(ReleaseChannels.Repository + "/releases") { UseShellExecute = true }); panel.Children.Add(notes);
        var import = Button("Importuj dane ze starszej wersji…"); import.Margin = new Thickness(0, 8, 0, 0); import.HorizontalAlignment = HorizontalAlignment.Left;
        import.Click += (_, _) => Import(); panel.Children.Add(import);
        var footer = Label("Pobranie nie przerywa gry. Podmianę wykonasz po zakończeniu walki.", 10, "#A79D8B");
        footer.Margin = new Thickness(0, 16, 0, 0); footer.TextWrapping = TextWrapping.Wrap; panel.Children.Add(footer);
        _updates.Changed += OnChanged; Closed += (_, _) => { _closed = true; _updates.Changed -= OnChanged; };
        Refresh(updates.Status);
    }
    private static TextBlock Label(string text, double size, string color = "#EEE4D0") => new() { Text = text, FontSize = size, Foreground = PetAppearance.Ink(color), VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 7, 7),
        Background = PetAppearance.Ink("#30291D"), Foreground = PetAppearance.Ink("#F2D38F"), BorderBrush = PetAppearance.Ink("#79623A") };
    private void SaveAutomatic(bool value) { _settings.CheckUpdatesAutomatically = value; _settings.Save(); }
    private void OnChanged(UpdateStatus state) => Dispatcher.BeginInvoke(() => { if (!_closed) Refresh(state); });
    private void Refresh(UpdateStatus state)
    {
        _version.Text = "Wersja: " + state.CurrentVersion + (state.NewVersion == null ? "" : "  →  " + state.NewVersion);
        _status.Text = state.Message; _progress.Value = state.Progress;
        _progress.IsIndeterminate = state.Stage == UpdateStage.Checking;
        _check.IsEnabled = !_updates.Busy && state.Stage != UpdateStage.NotInstalled;
        _download.IsEnabled = !_updates.Busy && state.NewVersion != null && state.Stage is UpdateStage.Available or UpdateStage.Error;
        _restart.IsEnabled = !_updates.Busy && state.Stage == UpdateStage.Ready;
        foreach (var button in new[] { _check, _download, _restart }) button.Opacity = button.IsEnabled ? 1 : .45;
        _channel.IsEnabled = !_updates.Busy && state.Stage is not (UpdateStage.Ready or UpdateStage.NotInstalled);
    }
    private void Import()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Wybierz katalog user-data starszej wersji Aurum", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var result = UserDataMigration.Import(picker.FolderName, UserSettings.DataDirectory);
            MessageBox.Show(this, $"Skopiowano plików: {result.Copied}.\nIstniejące pliki zachowano: {result.Existing}.\nBłędy: {result.Errors.Count}.\n\nZaimportowane dane zostaną odczytane po ponownym uruchomieniu programu.", "Import danych");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { MessageBox.Show(this, error.Message, "Nie udało się zaimportować danych"); }
    }
    internal void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        var body = (FrameworkElement)Content; Content = null;
        var root = new Border { Background = Background, Child = body };
        foreach (var state in new[] {
            new UpdateStatus(UpdateStage.Available, "Nowa wersja jest dostępna. Możesz pobrać ją w dogodnym momencie.", "0.2.0", "0.2.1"),
            new UpdateStatus(UpdateStage.Ready, "Aktualizacja pobrana i sprawdzona. Możesz uruchomić program ponownie.", "0.2.0", "0.2.1", 100) })
        {
            Refresh(state);
            root.Measure(new Size(465, double.PositiveInfinity)); var height = (int)Math.Ceiling(root.DesiredSize.Height);
            root.Arrange(new Rect(0, 0, 465, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(465, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, "updates-" + state.Stage + ".png")); encoder.Save(file);
        }
        root.Child = null; Content = body;
        Close();
    }
}
