using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Gear;
using CvRect = OpenCvSharp.Rect;

namespace Aurum;

internal sealed class ItemAdviceWindow : Window
{
    private readonly StackPanel _body = new() { Margin = new Thickness(14, 12, 14, 12) };
    private readonly Border _frame;
    private string _key = "";
    internal ItemAdviceWindow()
    {
        Title = "Aurum • BiS przedmiotów"; Width = 310; Height = 1;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = false; Focusable = false;
        _frame = new Border { BorderBrush = PetAppearance.Ink("#B29253"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush(Color.FromRgb(25, 29, 34), Color.FromRgb(11, 14, 18), 90), Child = _body };
        Content = _frame;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
            Native.SetWindowDisplayAffinity(handle, 0x11);
        };
    }
    private static TextBlock Text(string value, double size, string color = "#C6BEAD", FontWeight? weight = null) => new()
    { Text = value, FontSize = size, Foreground = PetAppearance.Ink(color), FontWeight = weight ?? FontWeights.Normal, FontFamily = new FontFamily("Segoe UI"), TextWrapping = TextWrapping.Wrap };
    private void Fill(GearAdvice advice, bool comparisonRight = false)
    {
        var key = advice.ClassLabel + advice.Title + advice.SlotLabel + advice.BindingRead + comparisonRight + string.Join("|", advice.Targets.Concat(advice.Skills).Select(t => t.Name + t.Present));
        if (_key == key) return; _key = key; _body.Children.Clear();
        var brand = new Grid(); brand.ColumnDefinitions.Add(new()); brand.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brand.Children.Add(Text("A U R U M  /  BiS", 11, "#E8C77F", FontWeights.SemiBold));
        var mode = new Border { Background = PetAppearance.Ink("#3A301C"), BorderBrush = PetAppearance.Ink("#715B33"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
            Child = Text("PvE DPS", 9, "#F0D48E", FontWeights.SemiBold) }; Grid.SetColumn(mode, 1); brand.Children.Add(mode); _body.Children.Add(brand);
        var title = Text(advice.ClassLabel + "  ·  " + advice.SlotLabel, 16, "#F7F0DC", FontWeights.SemiBold); title.Margin = new Thickness(0, 11, 0, 3); _body.Children.Add(title);
        var item = Text(advice.Title, 11, "#BAAF98"); item.TextTrimming = TextTrimming.CharacterEllipsis; item.TextWrapping = TextWrapping.NoWrap; _body.Children.Add(item);
        if (comparisonRight)
        {
            var source = Text("PRZEDMIOT Z INVENTORY · PRAWY TOOLTIP", 8.5, "#9EB8BF"); source.Margin = new Thickness(0, 6, 0, 0); _body.Children.Add(source);
        }
        _body.Children.Add(new Border { Height = 1, Background = PetAppearance.Ink("#51432D"), Margin = new Thickness(0, 11, 0, 11) });
        _body.Children.Add(Text("DOCELOWE BONUSY SLOTU", 9, "#B6A682", FontWeights.SemiBold));
        foreach (var target in advice.Targets)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 0) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(21) }); row.ColumnDefinitions.Add(new());
            var mark = Text(target.Present ? "✓" : "◆", target.Present ? 13 : 8, target.Present ? "#9AE0B2" : "#C6A45E", FontWeights.SemiBold); mark.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(mark);
            var name = Text(target.Name, 13, target.Present ? "#B1E7C2" : "#F0D69C", FontWeights.SemiBold); Grid.SetColumn(name, 1); row.Children.Add(name); _body.Children.Add(row);
        }
        var recognised = advice.BindingRead ? $"Rozpoznane w Soul Binding: {advice.Targets.Count(t => t.Present)}/{advice.Targets.Count}" : "Bonusy Soul Binding nie zostały odczytane";
        var status = Text(recognised, 9.5, "#9EAAAB"); status.Margin = new Thickness(0, 8, 0, 0); _body.Children.Add(status);
        if (advice.Skills.Count > 0)
        {
            var caption = Text("BONUS +1 DLA TWOJEJ KLASY", 9, "#B6A682", FontWeights.SemiBold); caption.Margin = new Thickness(0, 14, 0, 5); _body.Children.Add(caption);
            foreach (var skill in advice.Skills)
            {
                var line = Text((skill.Present ? "✓  " : "·  ") + skill.Name, 12, skill.Present ? "#B1E7C2" : "#E7DDC5"); line.Margin = new Thickness(0, 2, 0, 0); _body.Children.Add(line);
            }
        }
        if (advice.Useful.Count > 0)
        {
            var alternatives = Text("Warto też: " + string.Join(" · ", advice.Useful), 10, "#B9C7C7"); alternatives.Margin = new Thickness(0, 12, 0, 0); _body.Children.Add(alternatives);
        }
        if (!string.IsNullOrEmpty(advice.Note))
        {
            var note = Text(advice.Note, 10, "#ABA594"); note.Margin = new Thickness(0, 12, 0, 0); _body.Children.Add(note);
        }
        var verified = DateOnly.TryParse(advice.VerifiedOn, CultureInfo.InvariantCulture, out var date) ? date.ToString("dd.MM.yyyy") : advice.VerifiedOn;
        var footer = Text("Puść Ctrl, aby ukryć  ·  Baza: " + verified, 8.5, "#897F6E"); footer.Margin = new Thickness(0, 12, 0, 0); _body.Children.Add(footer);
    }
    internal void Present(GearAdvice advice, CvRect tooltip, GameViewport game, bool comparisonRight = false)
    {
        Fill(advice, comparisonRight);
        var dpi = VisualTreeHelper.GetDpi(this); var scale = Math.Clamp(game.Height / 1080d, .75, 1.5) / dpi.DpiScaleY;
        _frame.LayoutTransform = new ScaleTransform(scale, scale); Width = 310 * scale;
        _frame.Measure(new Size(Width, double.PositiveInfinity)); Height = _frame.DesiredSize.Height;
        var gap = 9 * scale; var left = (game.X + tooltip.Right) / dpi.DpiScaleX + gap;
        var gameLeft = game.X / dpi.DpiScaleX; var gameRight = (game.X + game.Width) / dpi.DpiScaleX;
        if (left + Width > gameRight) left = (game.X + tooltip.X) / dpi.DpiScaleX - Width - gap;
        Left = Math.Clamp(left, gameLeft, Math.Max(gameLeft, gameRight - Width));
        var top = (game.Y + tooltip.Y) / dpi.DpiScaleY;
        Top = Math.Clamp(top, game.Y / dpi.DpiScaleY, Math.Max(game.Y / dpi.DpiScaleY, (game.Y + game.Height) / dpi.DpiScaleY - Height));
        if (!IsVisible) Show();
    }
    internal void RenderPreview(GearAdvice advice, string background, string directory, bool comparisonRight = false)
    {
        Fill(advice, comparisonRight); _frame.Measure(new Size(310, double.PositiveInfinity)); Height = _frame.DesiredSize.Height;
        _frame.Arrange(new Rect(0, 0, 310, Height));
        var panel = new RenderTargetBitmap(310, (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32); panel.Render(_frame);
        Save(panel, Path.Combine(directory, "item-advice.png"));
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.GetFullPath(background)); image.EndInit(); image.Freeze();
        var width = image.PixelWidth + 334; var height = Math.Max(image.PixelHeight, panel.PixelHeight + 24);
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(PetAppearance.Ink("#0C1116"), null, new Rect(0, 0, width, height));
            context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            context.DrawImage(panel, new Rect(image.PixelWidth + 12, 12, panel.PixelWidth, panel.PixelHeight));
        }
        var composite = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); composite.Render(drawing); Save(composite, Path.Combine(directory, "item-advice-example.png")); Close();
    }
    private static void Save(BitmapSource image, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
}
