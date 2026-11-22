using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AionMeter.Core.Capture;

namespace Aurum.Tests;

public class PktmonTests
{
    private static byte[] Ipv4(byte[] payload)
    {
        var b = new byte[40 + payload.Length]; b[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), (ushort)b.Length);
        b[9] = 6; b[12] = 193; b[15] = 1; b[16] = 192; b[19] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20), 13328);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22), 50000);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(24), 123456);
        b[32] = 0x50; b[33] = 0x12;
        payload.CopyTo(b, 40); return b;
    }

    [Fact]
    public void Raw_ip_preserves_tcp_sequence_flags_addresses_payload_and_time()
    {
        var frame = Ipv4([1, 2, 3]);
        Assert.True(RawTcpReader.TryRead(frame, false, 1234, out var segment));
        Assert.Equal(1234, segment.TimeMs); Assert.Equal(13328, segment.Key.SrcPort);
        Assert.Equal(50000, segment.Key.DstPort); Assert.Equal(123456u, segment.Seq);
        Assert.True(segment.Syn); Assert.Equal(new byte[] { 1, 2, 3 }, segment.Payload);
        Assert.Equal("193.0.0.1", segment.Key.Src.ToString());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Ethernet_and_vlan_deliver_identical_tcp_payloads(bool vlan)
    {
        var ip = Ipv4([9, 8, 7]); var ethernet = new byte[ip.Length + (vlan ? 18 : 14)];
        BinaryPrimitives.WriteUInt16BigEndian(ethernet.AsSpan(12), vlan ? (ushort)0x8100 : (ushort)0x0800);
        if (vlan) BinaryPrimitives.WriteUInt16BigEndian(ethernet.AsSpan(16), 0x0800);
        ip.CopyTo(ethernet, vlan ? 18 : 14);
        Assert.True(RawTcpReader.TryRead(ethernet, true, 0, out var segment));
        Assert.Equal(new byte[] { 9, 8, 7 }, segment.Payload);
    }

    [Fact]
    public void Truncation_fragments_non_tcp_and_invalid_headers_are_rejected()
    {
        var valid = Ipv4([1, 2, 3]);
        for (var length = 0; length < valid.Length; length++)
            Assert.False(RawTcpReader.TryRead(valid.AsSpan(0, length), false, 0, out _));
        var fragmented = valid.ToArray(); fragmented[6] = 0x20;
        Assert.False(RawTcpReader.TryRead(fragmented, false, 0, out _));
        var udp = valid.ToArray(); udp[9] = 17;
        Assert.False(RawTcpReader.TryRead(udp, false, 0, out _));
        var badTcp = valid.ToArray(); badTcp[32] = 0x10;
        Assert.False(RawTcpReader.TryRead(badTcp, false, 0, out _));
    }

    [Fact]
    public void Ipv6_with_extension_header_is_read_without_pcap()
    {
        var bytes = new byte[71]; bytes[0] = 0x60; bytes[5] = 31; bytes[6] = 0;
        bytes[23] = 1; bytes[39] = 2; bytes[40] = 6; // hop-by-hop header -> TCP
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(48), 13328);
        bytes[60] = 0x50; bytes[68] = 1; bytes[69] = 2; bytes[70] = 3;
        Assert.True(RawTcpReader.TryRead(bytes, false, 0, out var segment));
        Assert.Equal("::1", segment.Key.Src.ToString()); Assert.Equal(new byte[] { 1, 2, 3 }, segment.Payload);
    }

    [Fact]
    public void Native_struct_layouts_match_the_windows_x64_abi()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(32, Marshal.SizeOf<PktmonNative.StreamConfig>());
        Assert.Equal(32, Marshal.SizeOf<PktmonNative.DataDescriptor>());
        Assert.Equal(48, Marshal.SizeOf<PktmonNative.Metadata>());
        Assert.Equal(40, Marshal.OffsetOf<PktmonNative.Metadata>(nameof(PktmonNative.Metadata.Timestamp)).ToInt32());
        Assert.Equal(216, Marshal.SizeOf<PktmonNative.Constraint>());
        Assert.Equal(186, Marshal.OffsetOf<PktmonNative.Constraint>(nameof(PktmonNative.Constraint.Port1)).ToInt32());
    }
}
