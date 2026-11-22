namespace AionMeter.Core.Cubes;

public readonly record struct CameraReading(double Heading, double Confidence);

public static class Navigation
{
    public static double Normalize(double angle) => (angle % 360 + 360) % 360;
    public static double Relative(double bearing, double camera) => (Normalize(bearing - camera) + 180) % 360 - 180;

    // Match the two bright radial edges of the camera's gold sector. Radial sampling
    // excludes the character marker and its central disc; local contrast rejects terrain.
    public static double[] EdgeScores(Func<int, int, (byte R, byte G, byte B)> pixel,
        double cx, double cy, double inner, double outer)
    {
        double Gold(double a, double radius)
        {
            var t = a * Math.PI / 180;
            var (r, g, b) = pixel((int)Math.Round(cx + Math.Sin(t) * radius),
                (int)Math.Round(cy - Math.Cos(t) * radius));
            return Math.Max(0, Math.Min(r - b, g - b * 0.85) - 18) * Math.Clamp((g - 100) / 100.0, 0, 1);
        }
        var scores = new double[360];
        for (var a = 0; a < 360; a++)
        {
            double sum = 0;
            for (var radius = inner; radius <= outer; radius += 1.5)
            {
                var line = Math.Max(Gold(a - .7, radius), Math.Max(Gold(a, radius), Gold(a + .7, radius)));
                var side = (Gold(a - 5, radius) + Gold(a + 5, radius)) / 2;
                sum += Math.Max(0, line - side);
            }
            scores[a] = sum / ((outer - inner) / 1.5 + 1);
        }
        return scores;
    }

    public static CameraReading? DetectCamera(double[] scores, double expectedWidth = 110)
    {
        var candidates = new List<(double Score, int A, int B)>();
        for (var a = 0; a < 360; a++)
            for (var width = (int)expectedWidth - 15; width <= expectedWidth + 15; width++)
            {
                var b = (a + width) % 360;
                var balance = Math.Min(scores[a], scores[b]);
                var score = balance * 2 + Math.Max(scores[a], scores[b]) * .25;
                candidates.Add((score, a, b));
            }
        var best = candidates.MaxBy(c => c.Score);
        if (Math.Min(scores[best.A], scores[best.B]) < 2.5) return null;
        var heading = Normalize(best.A + Normalize(best.B - best.A) / 2);
        var competitor = candidates.Where(c => Math.Abs(Relative(
            Normalize(c.A + Normalize(c.B - c.A) / 2), heading)) > 25).MaxBy(c => c.Score);
        var confidence = (best.Score - competitor.Score) / Math.Max(1, best.Score);
        return confidence < .12 ? null : new(heading, confidence);
    }
}

