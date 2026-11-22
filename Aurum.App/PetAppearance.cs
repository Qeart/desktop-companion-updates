using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.Core.Pets;

namespace Aurum;

internal static class PetAppearance
{
    internal const double ShopBadgeWidth = 104, ShopBadgeHeight = 34;
    private static readonly Dictionary<int, BitmapSource?> Portraits = new();
    internal static Brush Ink(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze(); return brush;
    }
    internal static readonly Brush Gold = Ink("#EDCB83"), Muted = Ink("#B9AD95");
    internal static BitmapSource? Portrait(int id)
    {
        lock (Portraits)
        {
            if (Portraits.TryGetValue(id, out var known)) return known;
            var path = Path.Combine(AppContext.BaseDirectory, "data", "pet-icons", id + ".png");
            BitmapSource? image = null;
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path); bitmap.DecodePixelWidth = 64; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
            }
            Portraits[id] = image; return image;
        }
    }
    internal static double Fraction(PetState? state) => state == null ? 0 : state.Level >= 3 ? 1
        : Math.Clamp(state.Points / (double)PetState.Costs[Math.Clamp(state.Level, 0, 2)], 0, 1);

    internal static FrameworkElement ProgressLine(PetState? state, double width, double height = 2)
    {
        var track = new Grid { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Border { Background = Ink("#403A2D"), CornerRadius = new CornerRadius(height / 2) });
        track.Children.Add(new Border { Width = width * Fraction(state), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(Color.FromRgb(159, 118, 47), Color.FromRgb(244, 213, 147), 0), CornerRadius = new CornerRadius(height / 2) });
        return track;
    }
    internal static Border ShopBadge(PetState? state)
    {
        var full = state?.Level >= 3;
        var body = new Grid { Margin = new Thickness(9, 3, 9, 4) };
        body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = new GridLength(2) });
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        line.Children.Add(new TextBlock { Text = full ? "✓" : "◆", Foreground = Gold, FontSize = full ? 13 : 9,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        line.Children.Add(new TextBlock { Text = state == null ? "—" : ShopPetMatcher.Counter(state), Foreground = Ink("#FFE7AC"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = full ? 14 : 16, FontWeight = FontWeights.Bold });
        body.Children.Add(line);
        var bar = ProgressLine(state, 78, 2); bar.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(bar, 1); body.Children.Add(bar);
        return new Border { Width = ShopBadgeWidth, Height = ShopBadgeHeight, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = Ink("#C69D4F"), Background = Ink("#08090C"), Child = body,
            SnapsToDevicePixels = true, UseLayoutRounding = true };
    }
    internal static Border SoulNotice(PetNotice notice)
    {
        var state = notice.Synced ? new PetState(notice.Level, notice.Points) : null;
        var row = new Grid { Margin = new Thickness(6, 5, 9, 5) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var portrait = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(4), ClipToBounds = true,
            BorderBrush = Ink("#8E7140"), BorderThickness = new Thickness(1), Background = Ink("#24241F"), HorizontalAlignment = HorizontalAlignment.Left };
        if (Portrait(notice.PetId) is { } image) portrait.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
        else portrait.Child = new TextBlock { Text = "◆", Foreground = Gold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(portrait);
        var detail = new Grid(); detail.RowDefinitions.Add(new() { Height = new GridLength(17) }); detail.RowDefinitions.Add(new() { Height = new GridLength(17) });
        detail.RowDefinitions.Add(new() { Height = new GridLength(3) }); Grid.SetColumn(detail, 1); row.Children.Add(detail);
        detail.Children.Add(new TextBlock { Text = notice.PetName, Foreground = Muted, FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var counter = new TextBlock { Text = state == null ? "Brak danych" : ShopPetMatcher.Counter(state), Foreground = Gold,
            FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(counter, 1); detail.Children.Add(counter);
        var bar = ProgressLine(state, 149); Grid.SetRow(bar, 2); detail.Children.Add(bar);
        if (notice.Gained > 0)
        {
            var gain = new TextBlock { Text = "+" + notice.Gained, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Gold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(gain, 2); row.Children.Add(gain);
        }
        return new Border { Height = 49, BorderThickness = new Thickness(1), BorderBrush = Ink("#8A714535"),
            CornerRadius = new CornerRadius(6), Background = new LinearGradientBrush(Color.FromArgb(237, 23, 24, 25), Color.FromArgb(230, 13, 15, 18), 0), Child = row };
    }
}
