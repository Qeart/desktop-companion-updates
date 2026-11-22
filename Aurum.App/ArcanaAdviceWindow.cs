using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Gear;

namespace Aurum;

internal sealed class ArcanaAdviceWindow : Window
{
    private readonly StackPanel _body = new() { Margin = new Thickness(12, 10, 12, 10) };
    private readonly Border _frame;
    private string _key = "";
    internal ArcanaAdviceWindow()
    {
        Title = "Aurum • BiS arkan"; Width = 380; Height = 1;
        AllowsTransparency = true; WindowStyle = WindowStyle.None; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = false; Focusable = false;
        _frame = new Border { BorderBrush = PetAppearance.Ink("#987B43"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Background = PetAppearance.Ink("#0D1116"), Child = _body }; Content = _frame;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            Native.SetWindowDisplayAffinity(handle, 0x11);
        };
    }
    private static TextBlock Text(string value, double size, string color = "#BEB6A5", FontWeight? weight = null) => new()
    { Text = value, FontSize = size, Foreground = PetAppearance.Ink(color), FontWeight = weight ?? FontWeights.Normal, FontFamily = new FontFamily("Segoe UI"), TextWrapping = TextWrapping.Wrap };
    private void Fill(ArcanaPanel panel, ArcanaAdvice advice)
    {
        var key = advice.ClassLabel + advice.Build + advice.Plan + advice.GoalCard + advice.MainMatches + panel.Title + panel.MainStat
            + string.Join("|", advice.Skills.Concat(advice.SecondarySkills ?? []).Select(s => s.Name + s.Present + s.Bonus));
        if (key == _key) return; _key = key; _body.Children.Clear();
        var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Text("A U R U M  /  ARKANA", 10, "#E6C580", FontWeights.SemiBold));
        var mode = Text(advice.ClassLabel + " · PvE DPS", 10, "#F0D69C"); Grid.SetColumn(mode, 1); heading.Children.Add(mode); _body.Children.Add(heading);
        var main = Text((advice.MainMatches ? "✓  " : "Cel: ") + advice.MainStat + " · " + advice.Effect, 13,
            advice.MainMatches ? "#B0E6BD" : "#F5D68E", FontWeights.SemiBold); main.Margin = new Thickness(0, 9, 0, 2); _body.Children.Add(main);
        _body.Children.Add(Text(advice.MainMatches ? advice.GoalCard + " · masz docelową statystykę" : "Dla planu: " + advice.GoalCard + "  |  masz: " + (panel.MainStat ?? "nie odczytano"), 9, "#AFA693"));
        if (!string.IsNullOrEmpty(advice.Build)) _body.Children.Add(Text(advice.Build, 9, "#96B2BE"));
        var caption = Text("SKILLE · OD NAJWAŻNIEJSZEGO", 8.5, "#B6A27B", FontWeights.SemiBold); caption.Margin = new Thickness(0, 8, 0, 4); _body.Children.Add(caption);
        foreach (var skill in advice.Skills)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Text((skill.Present ? "✓  " : "·  ") + skill.Name, 12, skill.Present ? "#B0E6BD" : "#EEE3C9", FontWeights.SemiBold));
            var bonus = Text(skill.Present ? skill.Bonus is { } value ? "+" + value : "masz" : "nie potwierdzono", 11, skill.Present ? "#B0E6BD" : "#A99D84");
            Grid.SetColumn(bonus, 1); row.Children.Add(bonus); _body.Children.Add(row);
        }
        if (advice.SecondarySkills is { Count: > 0 }) {
            var extra = Text("Dalej / warunkowo: " + string.Join(" · ", advice.SecondarySkills.Select(s => (s.Present ? "✓ " : "") + s.Name)), 9, "#A7B6BB");
            extra.Margin = new Thickness(0, 5, 0, 0); _body.Children.Add(extra);
        }
        var alternative = Text("Alternatywa: " + advice.Alternative, 9, "#A7B6BB"); alternative.Margin = new Thickness(0, 8, 0, 0); _body.Children.Add(alternative);
        var plan = Text(advice.Plan, 9, "#A89C84"); plan.Margin = new Thickness(0, 7, 0, 0); _body.Children.Add(plan);
        if (!string.IsNullOrEmpty(advice.Note)) { var note = Text(advice.Note, 8.5, "#A89C84"); note.Margin = new Thickness(0, 5, 0, 0); _body.Children.Add(note); }
    }
    internal void Present(ArcanaPanel panel, ArcanaAdvice advice, GameViewport game)
    {
        Fill(panel, advice); var dpi = VisualTreeHelper.GetDpi(this); var scale = game.Height / 1080d / dpi.DpiScaleY;
        _frame.LayoutTransform = new ScaleTransform(scale, scale); Width = 380 * scale;
        _frame.Measure(new Size(Width, double.PositiveInfinity)); Height = _frame.DesiredSize.Height;
        Left = (game.X + game.Width - 410 * game.Height / 1080d) / dpi.DpiScaleX;
        Top = (game.Y + panel.SetBottom) / dpi.DpiScaleY + 10 * scale;
        if (Top + Height > (game.Y + game.Height * .92) / dpi.DpiScaleY) { Hide(); return; }
        if (!IsVisible) Show();
    }
    internal void RenderPreview(ArcanaPanel panel, ArcanaAdvice advice, string background, string output)
    {
        Fill(panel, advice); _frame.Measure(new Size(380, double.PositiveInfinity)); Height = _frame.DesiredSize.Height;
        _frame.Arrange(new Rect(0, 0, 380, Height));
        var rendered = new RenderTargetBitmap(380, (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32); rendered.Render(_frame);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.GetFullPath(background)); image.EndInit(); image.Freeze();
        var scale = image.PixelHeight / 1080d; var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawImage(rendered, new Rect(image.PixelWidth - 410 * scale, panel.SetBottom + 10 * scale, 380 * scale, rendered.PixelHeight * scale));
        }
        var composed = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); composed.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(composed)); using var file = File.Create(output); encoder.Save(file); Close();
    }
    internal double RenderPanelPreview(ArcanaPanel panel, ArcanaAdvice advice, string output)
    {
        Fill(panel, advice); _frame.Measure(new Size(380, double.PositiveInfinity)); Height = _frame.DesiredSize.Height;
        _frame.Arrange(new Rect(0, 0, 380, Height));
        var bitmap = new RenderTargetBitmap(380, (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(_frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(output); encoder.Save(file);
        var height = Height; Close(); return height;
    }
}
