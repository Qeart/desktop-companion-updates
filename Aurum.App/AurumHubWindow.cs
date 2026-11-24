using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace Aurum;

internal enum HubCommand { Meter, Map, Cube, Souls, Shop, ItemAdvice, Updates, PetTracker }
internal sealed record HubState(bool MeterVisible, bool MapVisible, bool CubeEnabled, bool SoulsEnabled, bool ShopEnabled, string CubeStatus,
    bool ItemAdviceEnabled = true, string UpdateStatus = "Sprawdź dostępną wersję", bool UpdateAvailable = false,
    string MeterStatus = "Licznik walki party i force", bool PetTrackerVisible = false);

internal sealed class AurumOrbVisual : FrameworkElement
{
    internal bool Highlight { get; set; }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawDrawing(Highlight ? AurumBrand.Highlighted : AurumBrand.Normal);
    }
}

internal sealed class AurumHubWindow : Window
{
    private const double OrbSize = 64, PanelWidth = 290, PanelHeight = 558, Gap = 10;
    private readonly Canvas _canvas = new();
    private readonly AurumOrbVisual _orbVisual = new() { Width = 64, Height = 64 };
    private readonly Border _orb;
    private readonly Border _panel;
    private readonly UserSettings _settings;
    private readonly Func<HubState> _state;
    private readonly Action<HubCommand> _command;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<HubCommand, (TextBlock Description, TextBlock? Action, Border State)> _rows = new();
    private HubState? _lastState;
    private Point? _dragPoint;
    private double _anchorLeft, _anchorTop, _startLeft, _startTop;
    private bool _dragged, _wasExpanded, _expanded, _disposed;
    private nint _handle;
    public object DiagnosticState => new { Visible = IsVisible, Expanded = _expanded, Left = _anchorLeft, Top = _anchorTop };
    private static readonly ControlTemplate RowTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Body" Property="Background" Value="#252A30"/><Setter TargetName="Body" Property="BorderBrush" Value="#655332"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    internal AurumHubWindow(UserSettings settings, Func<HubState> state, Action<HubCommand> command, bool preview = false)
    {
        _settings = settings; _state = state; _command = command;
        Title = "Aurum • menu"; Width = Height = OrbSize; Content = _canvas;
        AllowsTransparency = true; WindowStyle = WindowStyle.None; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Focusable = false;
        _orb = new Border { Width = OrbSize, Height = OrbSize, Background = Brushes.Transparent, Child = _orbVisual,
            Cursor = Cursors.Hand, ToolTip = "Menu Aurum · kliknij lub przeciągnij" };
        AutomationProperties.SetName(_orb, "Otwórz menu Aurum lub przeciągnij ikonę");
        _canvas.Children.Add(_orb);
        _panel = BuildPanel(); _panel.Visibility = Visibility.Collapsed; _canvas.Children.Add(_panel);
        _orb.MouseEnter += (_, _) => { _orbVisual.Highlight = true; _orbVisual.InvalidateVisual(); };
        _orb.MouseLeave += (_, _) => { _orbVisual.Highlight = _expanded; _orbVisual.InvalidateVisual(); };
        _orb.MouseLeftButtonDown += OrbDown; _orb.MouseMove += OrbMove; _orb.MouseLeftButtonUp += OrbUp;
        _orb.LostMouseCapture += (_, _) => { if (_dragPoint != null) EndDrag(false); };
        var area = VirtualArea();
        _anchorLeft = ClampCoordinate(settings.LauncherLeft ?? area.Left + 18, area.Left, area.Right - OrbSize);
        _anchorTop = ClampCoordinate(settings.LauncherTop ?? area.Top + 300, area.Top, area.Bottom - OrbSize);
        SetExpanded(false); RefreshState();
        SourceInitialized += (_, _) => {
            _handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(_handle, -20, Native.GetWindowLongPtr(_handle, -20) | 0x08000000 | 0x80);
        };
        Closed += (_, _) => { _disposed = true; _timer.Stop(); };
        if (!preview) { _timer.Tick += (_, _) => Refresh(); _timer.Start(); Refresh(); }
    }
    private static double ClampCoordinate(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, Math.Max(min, max)) : min;
    private static Rect VirtualArea() => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
    private Border BuildPanel()
    {
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 9) };
        var header = new Grid { Height = 43 }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = "A U R U M", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = PetAppearance.Gold });
        name.Children.Add(new TextBlock { Text = "Twoje dodatki do Aion 2", FontSize = 10, Foreground = PetAppearance.Muted, Margin = new Thickness(0, 3, 0, 0) }); header.Children.Add(name);
        var close = new Button { Content = "×", Width = 25, Height = 25, Padding = new Thickness(0), FontSize = 18, ToolTip = "Zwiń menu", Focusable = false };
        close.Click += (_, _) => SetExpanded(false); Grid.SetColumn(close, 1); header.Children.Add(close); body.Children.Add(header);
        body.Children.Add(new Border { Height = 1, Background = PetAppearance.Ink("#4F422C"), Margin = new Thickness(0, 7, 0, 7) });
        AddRow(body, HubCommand.Meter, "DPS / HEAL", "Licznik walki party i force", false, "M2,20 L2,12 M8,20 L8,4 M14,20 L14,9 M20,20 L20,1");
        AddRow(body, HubCommand.Map, "Mapa petów", "Występowanie i Twoja kolekcja", false, "M1,4 L7,1 L14,4 L21,1 L21,18 L14,21 L7,18 L1,21 Z M7,1 L7,18 M14,4 L14,21");
        AddRow(body, HubCommand.PetTracker, "Tracker petów", "Poniżej MAX lub własna lista", false, "M2,3 L7,3 L7,8 L2,8 Z M11,5 L22,5 M2,12 L7,12 L7,17 L2,17 Z M11,14 L22,14 M2,21 L7,21 M11,23 L22,23");
        AddRow(body, HubCommand.ItemAdvice, "BiS sprzętu i arkan", "Sprzęt: Ctrl · Arkana: auto", true, "M12,1 L21,5 L21,13 C21,18 17,22 12,25 C7,22 3,18 3,13 L3,5 Z M7,12 L11,16 L17,8");
        AddRow(body, HubCommand.Cube, "Hidden Cube", "Złoty kompas do skrzynki", true, "M11,1 L21,7 L21,18 L11,24 L1,18 L1,7 Z M1,7 L11,13 L21,7 M11,13 L11,24");
        AddRow(body, HubCommand.Souls, "Zdobywane dusze", "Postęp po podniesieniu soula", true, "M12,1 C17,6 22,9 22,14 C22,20 18,24 12,24 C6,24 2,20 2,14 C2,9 7,6 12,1 Z M12,8 L12,19 M7,14 L17,14");
        AddRow(body, HubCommand.Shop, "Cloud Market", "Liczniki na kartach petów", true, "M3,8 L21,8 L22,24 L2,24 Z M8,9 L8,4 C8,-1 16,-1 16,4 L16,9");
        AddRow(body, HubCommand.Updates, "Aktualizacje", "Sprawdź dostępną wersję", false, "M19,8 A9,9 0 1 0 21,17 M19,1 L19,8 L12,8 M11,12 L11,21 M7,17 L11,21 L15,17");
        body.Children.Add(new TextBlock { Text = "Przeciągnij ikonę  ·  Ctrl+Shift+A", Foreground = PetAppearance.Ink("#9D9483"), FontSize = 9,
            Margin = new Thickness(4, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        return new Border { Width = PanelWidth, Height = PanelHeight, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = PetAppearance.Ink("#9C7C45"), Background = new LinearGradientBrush(Color.FromRgb(26, 29, 34), Color.FromRgb(12, 15, 19), 90), Child = body };
    }
    private void AddRow(Panel parent, HubCommand command, string title, string description, bool toggle, string path)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(31) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = new Border { Width = 30, Height = 32, CornerRadius = new CornerRadius(7), Background = PetAppearance.Ink("#29271F"), BorderBrush = PetAppearance.Ink("#53462D"), BorderThickness = new Thickness(1),
            Child = new Path { Data = Geometry.Parse(path), Stroke = PetAppearance.Gold, StrokeThickness = 1.4, Stretch = Stretch.Uniform, Width = 17, Height = 20, StrokeLineJoin = PenLineJoin.Round } };
        grid.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 5, 0) }; Grid.SetColumn(text, 1); grid.Children.Add(text);
        text.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = PetAppearance.Ink("#F6EFDD") });
        var detail = new TextBlock { Text = description, FontSize = 9, Foreground = PetAppearance.Muted, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }; text.Children.Add(detail);
        var state = new Border { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(state, 2); grid.Children.Add(state);
        TextBlock? action = null;
        if (!toggle) { action = new TextBlock { Foreground = PetAppearance.Gold, FontSize = 8, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; state.Child = action; }
        var button = new Button { Content = grid, Height = 52, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(5, 0, 6, 0),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Template = RowTemplate, HorizontalContentAlignment = HorizontalAlignment.Stretch, Focusable = false, Cursor = Cursors.Hand };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => { _command(command); RefreshState(); if (command is HubCommand.Meter or HubCommand.Map or HubCommand.Updates or HubCommand.PetTracker) SetExpanded(false); };
        parent.Children.Add(button); _rows[command] = (detail, action, state);
    }
    private static FrameworkElement Switch(bool enabled)
    {
        var track = new Canvas { Width = 28, Height = 16 };
        track.Children.Add(new Border { Width = 28, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = PetAppearance.Ink(enabled ? "#A4874D" : "#555A62"), Background = PetAppearance.Ink(enabled ? "#6F572A" : "#282D33") });
        var thumb = new Ellipse { Width = 10, Height = 10, Fill = PetAppearance.Ink(enabled ? "#F6D58E" : "#90949A") };
        Canvas.SetLeft(thumb, enabled ? 15 : 3); Canvas.SetTop(thumb, 3); track.Children.Add(thumb); return track;
    }
    private void RefreshState()
    {
        var state = _state(); if (state == _lastState) return; _lastState = state;
        if (IsVisible && _handle != 0) Native.SetWindowPos(_handle, -1, 0, 0, 0, 0, 0x13);
        _rows[HubCommand.Meter].Action!.Text = state.MeterVisible ? "UKRYJ" : "OTWÓRZ";
        _rows[HubCommand.Meter].Description.Text = state.MeterStatus;
        _rows[HubCommand.Meter].Description.ToolTip = state.MeterStatus;
        _rows[HubCommand.Updates].Action!.Text = state.UpdateAvailable ? "NOWA" : "OTWÓRZ";
        _rows[HubCommand.Updates].Description.Text = state.UpdateStatus;
        _rows[HubCommand.Updates].Description.ToolTip = state.UpdateStatus;
        _rows[HubCommand.Map].Action!.Text = state.MapVisible ? "ZAMKNIJ" : "OTWÓRZ";
        _rows[HubCommand.PetTracker].Action!.Text = state.PetTrackerVisible ? "UKRYJ" : "OTWÓRZ";
        _rows[HubCommand.Cube].State.Child = Switch(state.CubeEnabled);
        _rows[HubCommand.Cube].Description.Text = state.CubeEnabled ? state.CubeStatus : "Wskaźnik wyłączony";
        _rows[HubCommand.Souls].State.Child = Switch(state.SoulsEnabled);
        _rows[HubCommand.Shop].State.Child = Switch(state.ShopEnabled);
        _rows[HubCommand.ItemAdvice].State.Child = Switch(state.ItemAdviceEnabled);
    }
    internal void ToggleMenu() { if (_disposed) return; if (!IsVisible) Show(); RefreshState(); SetExpanded(!_expanded); }
    private void SetExpanded(bool expanded)
    {
        _expanded = expanded; _orbVisual.Highlight = expanded; _orbVisual.InvalidateVisual();
        _panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (!expanded) { Width = Height = OrbSize; Left = _anchorLeft; Top = _anchorTop; Canvas.SetLeft(_orb, 0); Canvas.SetTop(_orb, 0); return; }
        var area = VirtualArea(); var openLeft = _anchorLeft + OrbSize + Gap + PanelWidth > area.Right;
        var orbX = openLeft ? PanelWidth + Gap : 0;
        Width = OrbSize + Gap + PanelWidth; Height = PanelHeight;
        Left = _anchorLeft - orbX; Top = ClampCoordinate(_anchorTop, area.Top, area.Bottom - Height);
        Canvas.SetLeft(_orb, orbX); Canvas.SetTop(_orb, _anchorTop - Top);
        Canvas.SetLeft(_panel, openLeft ? 0 : OrbSize + Gap); Canvas.SetTop(_panel, 0);
    }
    private void OrbDown(object sender, MouseButtonEventArgs e)
    {
        _wasExpanded = _expanded; _dragged = false; _dragPoint = PointToScreen(e.GetPosition(this)); _startLeft = _anchorLeft; _startTop = _anchorTop;
        if (!_orb.CaptureMouse()) _dragPoint = null; e.Handled = true;
    }
    private void OrbMove(object sender, MouseEventArgs e)
    {
        if (_dragPoint is not { } origin) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(false); return; }
        var current = PointToScreen(e.GetPosition(this)); var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (current.X - origin.X) / dpi.DpiScaleX; var dy = (current.Y - origin.Y) / dpi.DpiScaleY;
        if (!_dragged && Math.Abs(dx) + Math.Abs(dy) < 4) return;
        _dragged = true; var area = VirtualArea();
        _anchorLeft = ClampCoordinate(_startLeft + dx, area.Left, area.Right - OrbSize);
        _anchorTop = ClampCoordinate(_startTop + dy, area.Top, area.Bottom - OrbSize);
        SetExpanded(false); e.Handled = true;
    }
    private void OrbUp(object sender, MouseButtonEventArgs e) { if (_dragPoint == null) return; EndDrag(true); e.Handled = true; }
    private void EndDrag(bool click)
    {
        _dragPoint = null; if (_orb.IsMouseCaptured) _orb.ReleaseMouseCapture();
        if (_dragged) { _settings.LauncherLeft = _anchorLeft; _settings.LauncherTop = _anchorTop; _settings.Save(); }
        else if (click) SetExpanded(!_wasExpanded);
    }
    private void Refresh()
    {
        if (_disposed) return;
        if (!GameWindowAccess.GameOrAurumForeground()) { if (IsVisible) { SetExpanded(false); Hide(); } return; }
        if (!IsVisible) Show(); RefreshState();
        if (_expanded && _dragPoint == null && (Native.GetAsyncKeyState(1) & 0x8001) != 0 && Native.GetCursorPos(out var point)
            && Native.GetWindowRect(_handle, out var rect) && (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)) SetExpanded(false);
    }
    internal void RenderPreview(string directory)
    {
        SetExpanded(true); _canvas.Measure(new Size(Width, Height)); _canvas.Arrange(new Rect(0, 0, Width, Height));
        SaveVisual(_canvas, (int)Width, (int)Height, System.IO.Path.Combine(directory, "aurum-menu.png"));
        SetExpanded(false); _canvas.Measure(new Size(Width, Height)); _canvas.Arrange(new Rect(0, 0, Width, Height));
        SaveVisual(_canvas, 64, 64, System.IO.Path.Combine(directory, "aurum-orb.png")); Close();
    }
    private static void SaveVisual(Visual visual, int width, int height, string output)
    {
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(output); encoder.Save(file);
    }
}
