using System.Runtime.InteropServices;

namespace AionMeter.Core.Capture;

// Official ABI: Microsoft Learn /windows/win32/pktmon/packetmonitor/.
// Fixed name/string limits (64/128) additionally checked against emmachase/pktmon's API header.
internal static class PktmonNative
{
    private const string Dll = "PktMonApi.dll";
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void DataCallback(nint context, nint descriptor);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void EventCallback(nint context, nint information, int kind);

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamConfig
    {
        public nint Context, EventCallback, DataCallback;
        public ushort BufferMultiplier, TruncationSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DataDescriptor
    {
        public nint Data;
        public uint DataSize, MetadataOffset, PacketOffset, PacketLength, MissedWrites, MissedReads;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Metadata
    {
        public ulong GroupId;
        public ushort PacketCount, AppearanceCount, Direction, PacketType, ComponentId, EdgeId, Reserved;
        public uint DropReason, DropLocation;
        public ushort Processor;
        public long Timestamp;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct IpUnion { [FieldOffset(0)] public ulong Low; [FieldOffset(8)] public ulong High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Constraint
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        public uint Present;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Mac1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Mac2;
        public ushort Vlan, EtherType, Dscp;
        public byte Protocol;
        public IpUnion Ip1, Ip2;
        public byte Prefix1, Prefix2;
        public ushort Port1, Port2;
        public byte TcpFlags;
        public uint EncapType;
        public ushort VxlanPort;
        public ulong Packets, Bytes;
        public static Constraint Game(ushort port) => new()
        {
            Name = "Aurum-Aion2-Inbound", Present = (1u << 5) | (1u << 11),
            Protocol = 6, Port1 = port, Mac1 = new byte[6], Mac2 = new byte[6],
        };
    }

    [DllImport(Dll)] internal static extern int PacketMonitorInitialize(uint version, nint reserved, out nint handle);
    [DllImport(Dll)] internal static extern int PacketMonitorUninitialize(nint handle);
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int PacketMonitorCreateLiveSession(nint handle, string name, out nint session);
    [DllImport(Dll)] internal static extern int PacketMonitorCloseSessionHandle(nint session);
    [DllImport(Dll)] internal static extern int PacketMonitorCreateRealtimeStream(nint handle, in StreamConfig config, out nint stream);
    [DllImport(Dll)] internal static extern int PacketMonitorCloseRealtimeStream(nint stream);
    [DllImport(Dll)] internal static extern int PacketMonitorAttachOutputToSession(nint session, nint stream);
    [DllImport(Dll)] internal static extern int PacketMonitorAddCaptureConstraint(nint session, in Constraint constraint);
    [DllImport(Dll)] internal static extern int PacketMonitorSetSessionActive(nint session, [MarshalAs(UnmanagedType.U1)] bool active);
    [DllImport(Dll)] internal static extern int PacketMonitorEnumDataSources(nint handle, int kind,
        [MarshalAs(UnmanagedType.U1)] bool hidden, nuint capacity, out nuint needed, nint buffer);
    [DllImport(Dll)] internal static extern int PacketMonitorAddSingleDataSourceToSession(nint session, nint source);
}
