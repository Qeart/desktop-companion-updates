namespace AionMeter.Core.Pets;
public sealed record ShopText(string Text, double X, double Y, double Width, double Height);
public static class ShopTextGrouping
{
    public static IReadOnlyList<ShopText> Group(IEnumerable<ShopText> words)
    {
        var rows = new List<List<ShopText>>();
        foreach (var word in words.OrderBy(w => w.Y))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r.Average(w => w.Y + w.Height / 2) - (word.Y + word.Height / 2)) <= Math.Max(3, word.Height * .55));
            if (row == null) rows.Add([word]); else row.Add(word);
        }
        var result = new List<ShopText>();
        foreach (var row in rows)
        {
            var cluster = new List<ShopText>();
            foreach (var word in row.OrderBy(w => w.X))
            {
                if (cluster.Count > 0 && word.X - (cluster[^1].X + cluster[^1].Width) > Math.Max(15, word.Height * 2.2))
                { result.Add(Combine(cluster)); cluster.Clear(); }
                cluster.Add(word);
            }
            if (cluster.Count > 0) result.Add(Combine(cluster));
        }
        return result;
    }
    private static ShopText Combine(IReadOnlyList<ShopText> words)
    {
        var x = words.Min(w => w.X); var y = words.Min(w => w.Y);
        return new(string.Join(" ", words.Select(w => w.Text)), x, y, words.Max(w => w.X + w.Width) - x, words.Max(w => w.Y + w.Height) - y);
    }
    public static bool IsShopHeader(string text)
    {
        var clean = text.Replace('ł', 't').Replace('Ł', 'T');
        return new[] { "Pets", "Special", "Skins" }.All(word => System.Text.RegularExpressions.Regex.IsMatch(clean,
            @"\b" + word + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }
}
