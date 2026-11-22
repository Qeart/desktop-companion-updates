namespace AionMeter.Core.Gear;

public readonly record struct TooltipBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
public sealed record TooltipChoice(int Index, bool ComparisonRight);

public static class TooltipSelection
{
    public static IReadOnlyList<TooltipChoice> Order(IReadOnlyList<TooltipBox> boxes, double cursorX, double cursorY, double scale = 1)
    {
        var right = new HashSet<int>(); var left = new HashSet<int>();
        for (var a = 0; a < boxes.Count; a++)
        for (var b = 0; b < boxes.Count; b++)
        {
            if (a == b) continue;
            var first = boxes[a]; var second = boxes[b]; var gap = second.X - first.Right;
            var overlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y);
            if (second.X <= first.X || gap < -14 * scale || gap > 55 * scale || Math.Abs(first.Y - second.Y) > 45 * scale
                || Math.Min(first.Width, second.Width) < Math.Max(first.Width, second.Width) * .72
                || overlap < Math.Min(first.Height, second.Height) * .65) continue;
            left.Add(a); right.Add(b);
        }
        // In a comparison the left card is the equipped item. If the hovered/right
        // card cannot be read, do not silently replace it with the equipped one.
        if (right.Count > 0)
            return right.Where(i => !left.Contains(i)).OrderBy(i => Distance(boxes[i], cursorX, cursorY)).Select(i => new TooltipChoice(i, true)).ToArray();
        return Enumerable.Range(0, boxes.Count).OrderBy(i => Distance(boxes[i], cursorX, cursorY)).Select(i => new TooltipChoice(i, false)).ToArray();
    }
    private static double Distance(TooltipBox box, double x, double y)
    {
        var dx = Math.Max(0, Math.Max(box.X - x, x - box.Right)); var dy = Math.Max(0, Math.Max(box.Y - y, y - box.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
