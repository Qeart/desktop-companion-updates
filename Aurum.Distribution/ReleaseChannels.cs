namespace Aurum.Distribution;

public static class ReleaseChannels
{
    public const string Repository = "https://github.com/Qeart/desktop-companion-updates";
    public static string Normalize(string? channel) => channel == "beta" ? "beta" : "stable";
    public static string Feed(string? channel) => "win-" + Normalize(channel);
}
