using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aurum;

// Original vector mark: the same geometry drives the launcher and the Windows icon.
internal static class AurumBrand
{
    private const string Letter = "F0 M31.8,11.5 L49,48 L39.4,44 L35.8,35.6 L27.8,35.6 L24.2,44 L14.6,48 Z M29.4,29.8 L34.2,29.8 L31.8,24 Z";
    internal static readonly DrawingGroup Normal = Draw(false), Highlighted = Draw(true);
    internal static readonly BitmapSource Image = Bitmap(64);
    private static Brush Gold => new LinearGradientBrush(new GradientStopCollection {
        new(Color.FromRgb(255, 238, 180), 0), new(Color.FromRgb(229, 186, 92), .38),
        new(Color.FromRgb(164, 113, 37), .67), new(Color.FromRgb(255, 222, 145), 1) }, new Point(0, 0), new Point(1, 1));
    private static DrawingGroup Draw(bool highlight, bool detailed = true)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, 3, 5, 8)), null, new Point(32, 33), 31, 31);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(highlight ? (byte)65 : (byte)38, 34, 25), Color.FromRgb(9, 13, 18)),
                new Pen(Gold, 1.7), new Point(32, 31.5), 28, 28);
            if (detailed)
            {
                dc.DrawEllipse(null, new Pen(PetAppearance.Ink("#76603A"), .65), new Point(32, 31.5), 24.2, 24.2);
                for (var i = 0; i < 4; i++)
                {
                    var angle = i * Math.PI / 2; var x = 32 + Math.Sin(angle) * 28; var y = 31.5 - Math.Cos(angle) * 28;
                    dc.DrawGeometry(Gold, null, Geometry.Parse(FormattableString.Invariant($"M{x},{y - 2} L{x + 1.5},{y} L{x},{y + 2} L{x - 1.5},{y} Z")));
                }
            }
            dc.DrawGeometry(Gold, new Pen(PetAppearance.Ink("#FFE7B1"), .35), Geometry.Parse(Letter));
            dc.DrawLine(new Pen(Gold, 1), new Point(25, 51), new Point(39, 51));
        }
        group.Freeze(); return group;
    }
    private static BitmapSource Bitmap(int size)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        { dc.PushTransform(new ScaleTransform(size / 64d, size / 64d)); dc.DrawDrawing(size >= 48 ? Normal : Draw(false, false)); dc.Pop(); }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("/AurumMeter;component/Assets/aurum.ico", UriKind.Relative))!.Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32); return (System.Drawing.Icon)icon.Clone();
    }
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var images = sizes.Select(size => {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(size)));
            using var bytes = new MemoryStream(); encoder.Save(bytes); return bytes.ToArray();
        }).ToArray();
        using (var file = File.Create(Path.Combine(directory, "aurum.ico")))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            var offset = 6 + 16 * sizes.Length;
            for (var i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
        for (var i = 0; i < sizes.Length; i++) File.WriteAllBytes(Path.Combine(directory, $"aurum-{sizes[i]}.png"), images[i]);
    }
}
