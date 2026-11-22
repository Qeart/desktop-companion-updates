using AionMeter.Core.Events;

namespace AionMeter.Core.Pets;

public sealed record PetValue(int PetId, int Value);
public sealed record PetCollectionEvent(long TimeMs, IReadOnlyList<PetValue> Levels, IReadOnlyList<PetValue> Points) : GameEvent(TimeMs);
public sealed record PetSoulsEvent(long TimeMs, IReadOnlyList<PetValue> Gains) : GameEvent(TimeMs);
public sealed record PetLevelEvent(long TimeMs, int PetId, int Level, bool Unlock) : GameEvent(TimeMs);

public static class PetPacketReader
{
    public static GameEvent? Read(ushort opcode, ReadOnlySpan<byte> body, long time)
    {
        var offset = 0;
        if (opcode == 0x0090)
        {
            offset = 4;
            var levels = Values(body, ref offset, true, true);
            if (levels is null) return null;
            var points = Values(body, ref offset, true, false);
            if (points is null) return null;
            return new PetCollectionEvent(time, levels, points);
        }
        if (opcode == 0x0D90)
        {
            var gains = Values(body, ref offset, false, false);
            return gains is null ? null : new PetSoulsEvent(time, gains);
        }
        if (opcode is 0x1490 or 0x0990 && body.Length >= 10)
        {
            var pet = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body[2..]);
            var level = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body[6..]);
            if (pet is > 0 and < 1_000_000 && level is >= 0 and <= 3)
                return new PetLevelEvent(time, pet, level, opcode == 0x0990);
        }
        return null;
    }

    private static IReadOnlyList<PetValue>? Values(ReadOnlySpan<byte> body, ref int offset, bool varint, bool level)
    {
        if (offset >= body.Length) return null;
        uint count;
        if (varint)
        {
            if (!Protocol.Wire.TryVarint(body, ref offset, out count)) return null;
        }
        else count = body[offset++];
        var size = level ? 12 : 8;
        if (count > 4096 || (long)offset + count * size > body.Length) return null;
        var values = new List<PetValue>((int)count);
        for (var i = 0; i < count; i++)
        {
            var id = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
            var value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body[(offset + (level ? 8 : 4))..]);
            if (id is <= 0 or >= 1_000_000 || value < 0 || value > (level ? 3 : 1_000_000)) return null;
            values.Add(new(id, value)); offset += size;
        }
        return values;
    }
}
