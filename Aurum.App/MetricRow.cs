using System.ComponentModel;
using AionMeter.Core.Combat;

namespace Aurum;

public sealed class MetricRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public uint ActorId { get; private set; }
    public string Rank { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Class { get; private set; } = "";
    public string ClassAndCp { get; private set; } = "";
    public string Rate { get; private set; } = "";
    public string Amount { get; private set; } = "";
    public string Tip { get; private set; } = "";
    public double Fill { get; private set; }
    public void Apply(CombatantSnapshot player, int rank, long best, long total, double seconds, bool healing)
    {
        var amount = healing ? player.Healing : player.Damage;
        ActorId = player.ActorId; Rank = rank.ToString();
        Name = player.Name + (player.IsSelf ? "  · TY" : ""); Class = player.Class.ToString();
        ClassAndCp = Class + " · " + (player.CombatPower > 0
            ? player.CombatPower >= 1000
                ? Math.Round(player.CombatPower / 1000.0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "k"
                : player.CombatPower.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—") + " CP";
        Rate = Compact(amount / seconds) + (healing ? " HPS" : " DPS");
        Amount = Compact(amount) + $"  ·  {(total > 0 ? amount * 100.0 / total : 0):0.#}%";
        Fill = (double)amount / Math.Max(1, best);
        Tip = $"{player.Name}\n{(healing ? "Leczenie" : "Obrażenia")}: {amount:N0}\nKliknij, aby zobaczyć umiejętności.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public static string Compact(double value) => value >= 1_000_000 ? $"{value / 1_000_000:0.##}M"
        : value >= 1000 ? $"{value / 1000:0.#}K" : $"{value:0}";
}
