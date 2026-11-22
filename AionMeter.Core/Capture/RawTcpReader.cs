using System.Buffers.Binary;
using System.Net;

namespace AionMeter.Core.Capture;

/// <summary>Ethernet or raw IP to TCP, without a pcap library or native capture driver.</summary>
public static class RawTcpReader
{
    public static bool TryRead(ReadOnlySpan<byte> bytes, bool ethernet, long timeMs, out SegmentReader.Segment segment)
    {
        segment = default;
        var offset = 0;
        if (ethernet)
        {
            if (bytes.Length < 14) return false;
            var type = BinaryPrimitives.ReadUInt16BigEndian(bytes[12..]); offset = 14;
            for (var tags = 0; type is 0x8100 or 0x88A8 && tags < 2; tags++)
            {
                if (offset + 4 > bytes.Length) return false;
                type = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]); offset += 4;
            }
            if (type is not (0x0800 or 0x86DD)) return false;
        }
        if (offset >= bytes.Length) return false;
        var ip = bytes[offset..];
        IPAddress source, destination;
        int tcpStart, end;
        if (ip[0] >> 4 == 4)
        {
            if (ip.Length < 20 || ip[9] != 6) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
            tcpStart = (ip[0] & 15) * 4;
            // No fabricated partial TCP segments: fragmented or truncated packets are rejected.
            if (tcpStart < 20 || length < tcpStart + 20 || length > ip.Length ||
                (BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x3FFF) != 0) return false;
            source = new IPAddress(ip.Slice(12, 4)); destination = new IPAddress(ip.Slice(16, 4));
            end = length;
        }
        else if (ip[0] >> 4 == 6)
        {
            if (ip.Length < 40) return false;
            end = 40 + BinaryPrimitives.ReadUInt16BigEndian(ip[4..]);
            if (end > ip.Length) return false;
            source = new IPAddress(ip.Slice(8, 16)); destination = new IPAddress(ip.Slice(24, 16));
            var next = ip[6]; tcpStart = 40;
            for (var i = 0; next != 6 && i < 8; i++)
            {
                // Do not attempt IP fragment reassembly, ESP or unknown extension layouts.
                if (next is not (0 or 43 or 60 or 51) || tcpStart + 2 > end) return false;
                var size = next == 51 ? (ip[tcpStart + 1] + 2) * 4 : (ip[tcpStart + 1] + 1) * 8;
                next = ip[tcpStart]; tcpStart += size;
            }
            if (next != 6) return false;
        }
        else return false;
        if (tcpStart + 20 > end) return false;
        var tcp = ip[tcpStart..end];
        var header = (tcp[12] >> 4) * 4;
        if (header < 20 || header > tcp.Length) return false;
        segment = new(timeMs, new FlowKey(source, BinaryPrimitives.ReadUInt16BigEndian(tcp),
            destination, BinaryPrimitives.ReadUInt16BigEndian(tcp[2..])),
            BinaryPrimitives.ReadUInt32BigEndian(tcp[4..]), (tcp[13] & 2) != 0, (tcp[13] & 5) != 0, tcp[header..].ToArray());
        return true;
    }
}
