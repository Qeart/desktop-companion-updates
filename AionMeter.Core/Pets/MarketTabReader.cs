using AionMeter.Core.Events;
using AionMeter.Core.Protocol;
namespace AionMeter.Core.Pets;
public sealed record MarketTabEvent(long TimeMs, byte Tab) : GameEvent(TimeMs);
public static class MarketTabReader
{
    // 91 56: tab byte, varint goods count, >=34 bytes per goods, refresh timestamp.
    public static MarketTabEvent? Read(ReadOnlySpan<byte> body, long time)
    {
        if (body.Length < 2 || body[0] is not (0x65 or 0x68)) return null;
        var offset = 1;
        if (!Wire.TryVarint(body, ref offset, out var count) || count is < 1 or > 64
            || body.Length - offset < count * 34L + 9) return null;
        return new(time, body[0]);
    }
}
