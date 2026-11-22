namespace AionMeter.Core.PetMaps;

// All scene layers use this camera: map pixels, pet areas, player and test anchor.
public sealed class MapViewport
{
    public double Scale { get; private set; } = 1;
    public double X { get; private set; }
    public double Y { get; private set; }
    public (double X, double Y) Project(double x, double y) => (x * Scale + X, y * Scale + Y);
    public (double X, double Y) Unproject(double x, double y) => ((x - X) / Scale, (y - Y) / Scale);
    public void Pan(double dx, double dy) { if (double.IsFinite(dx) && double.IsFinite(dy)) { X += dx; Y += dy; } }
    public void Center(double imageX, double imageY, double width, double height) { X = width / 2 - imageX * Scale; Y = height / 2 - imageY * Scale; }
    public void Fit(double imageWidth, double imageHeight, double width, double height)
    {
        if (Math.Min(Math.Min(imageWidth, imageHeight), Math.Min(width, height)) <= 0) return;
        Scale = Math.Min(width / imageWidth, height / imageHeight) * .94;
        Center(imageWidth / 2, imageHeight / 2, width, height);
    }
    public void Zoom(double factor, double screenX, double screenY)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var anchor = Unproject(screenX, screenY);
        Scale = Math.Clamp(Scale * factor, .08, 12);
        X = screenX - anchor.X * Scale; Y = screenY - anchor.Y * Scale;
    }
}
