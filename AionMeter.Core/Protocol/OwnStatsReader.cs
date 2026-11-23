namespace AionMeter.Core.Protocol;

public static class OwnStatsReader
{
    // actor varint, count u8, count * (stat u16, value i32), eight-byte tail.
    // Count zero is valid: the user's live Global session sends these repeatedly.
    // Layout cross-checked against A2Tools/Daevalog; validate the complete record,
    // rather than accepting an actor-shaped prefix in an arbitrary packet.
    public static uint? Actor(ReadOnlySpan<byte> body)
    {
        var offset = 0;
        if (!Wire.TryVarint(body, ref offset, out var actor) || actor is 0 or > 9_999_999 || offset >= body.Length) return null;
        var count = body[offset++];
        return body.Length == offset + count * 6 + 8 ? actor : null;
    }
}
