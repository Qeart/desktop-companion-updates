using System.Runtime.InteropServices;
using AionMeter.Core.Pets;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Aurum;

// Windows OCR shared by the shop and Ctrl item tooltip readers. Each reader has its
// own engine, so a tooltip scan cannot wait behind the shop's recognition task.
internal sealed class WindowsTextReader
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages()
        ?? OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(e => e != null);
    internal bool Available => _engine != null;
    internal async Task<IReadOnlyList<ShopText>> Read(Mat source, double requestedFactor)
    {
        if (_engine == null) return [];
        var factor = Math.Min(requestedFactor, OcrEngine.MaxImageDimension / (double)Math.Max(source.Width, source.Height));
        using var image = new Mat(); Cv2.Resize(source, image, new Size(Math.Max(1, (int)(source.Width * factor)), Math.Max(1, (int)(source.Height * factor))), 0, 0, InterpolationFlags.Cubic);
        var bytes = new byte[image.Width * image.Height * 4]; Marshal.Copy(image.Data, bytes, 0, bytes.Length);
        using var writer = new Windows.Storage.Streams.DataWriter(); writer.WriteBytes(bytes);
        using var software = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(software);
        return ShopTextGrouping.Group(result.Lines.SelectMany(l => l.Words).Select(word =>
            new ShopText(word.Text, word.BoundingRect.X / factor, word.BoundingRect.Y / factor,
                word.BoundingRect.Width / factor, word.BoundingRect.Height / factor)));
    }
}
