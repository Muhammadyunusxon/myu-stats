using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using MYUStats.Native;
using MYUStats.Sampling;

namespace MYUStats.Tests;

/// <summary>
/// The offsets NativeParsers reads by, checked against the structs as the Windows SDK headers declare
/// them (the compiler lays these out with the same rules as MSVC on 64-bit Windows), and the parsers
/// run over hand-built buffers.
/// </summary>
public unsafe class NativeLayoutTests
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MibIfRow2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIfTable2
    {
        public uint NumEntries;
        public MibIfRow2 First;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public ushort ImageNameLength;
        public ushort ImageNameMaximumLength;
        public nint ImageNameBuffer;
        public int BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public nuint UniqueProcessKey;
        public nuint PeakVirtualSize;
        public nuint VirtualSize;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivatePageCount;
        public long ReadOperationCount;
        public long WriteOperationCount;
        public long OtherOperationCount;
        public long ReadTransferCount;
        public long WriteTransferCount;
        public long OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPerformance
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupAffinity
    {
        public nuint Mask;
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorCoreRecord
    {
        public int Relationship;
        public uint Size;
        public byte Flags;
        public byte EfficiencyClass;
        public fixed byte Reserved[20];
        public ushort GroupCount;
        public GroupAffinity GroupMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhCounterValueItem
    {
        public nint Name;
        public uint Status;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public fixed byte Reserved[3];
        public fixed byte Chemistry[4];
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [Fact]
    public void NetworkRowOffsets()
    {
        Assert.Equal(NativeParsers.IfRowSize, Marshal.SizeOf<MibIfRow2>());
        Assert.Equal(NativeParsers.IfTableHeader, (int)Marshal.OffsetOf<MibIfTable2>(nameof(MibIfTable2.First)));
        Assert.Equal(NativeParsers.IfRowType, (int)Marshal.OffsetOf<MibIfRow2>(nameof(MibIfRow2.Type)));
        Assert.Equal(NativeParsers.IfRowFlags, (int)Marshal.OffsetOf<MibIfRow2>(nameof(MibIfRow2.InterfaceAndOperStatusFlags)));
        Assert.Equal(NativeParsers.IfRowOperStatus, (int)Marshal.OffsetOf<MibIfRow2>(nameof(MibIfRow2.OperStatus)));
        Assert.Equal(NativeParsers.IfRowInOctets, (int)Marshal.OffsetOf<MibIfRow2>(nameof(MibIfRow2.InOctets)));
        Assert.Equal(NativeParsers.IfRowOutOctets, (int)Marshal.OffsetOf<MibIfRow2>(nameof(MibIfRow2.OutOctets)));
    }

    [Fact]
    public void ProcessEntryOffsets()
    {
        Assert.Equal(NativeParsers.ProcessEntrySize, Marshal.SizeOf<SystemProcessInformation>());
        Assert.Equal(NativeParsers.ProcessWorkingSetPrivate, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.WorkingSetPrivateSize)));
        Assert.Equal(NativeParsers.ProcessUserTime, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.UserTime)));
        Assert.Equal(NativeParsers.ProcessKernelTime, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.KernelTime)));
        Assert.Equal(NativeParsers.ProcessNameLength, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.ImageNameLength)));
        Assert.Equal(NativeParsers.ProcessNameBuffer, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.ImageNameBuffer)));
        Assert.Equal(NativeParsers.ProcessId, (int)Marshal.OffsetOf<SystemProcessInformation>(nameof(SystemProcessInformation.UniqueProcessId)));
    }

    [Fact]
    public void ProcessorOffsets()
    {
        Assert.Equal(NativeParsers.ProcessorTimesSize, Marshal.SizeOf<ProcessorPerformance>());
        Assert.Equal(48, Marshal.SizeOf<ProcessorCoreRecord>());
        Assert.Equal(9, (int)Marshal.OffsetOf<ProcessorCoreRecord>(nameof(ProcessorCoreRecord.EfficiencyClass)));
        Assert.Equal(30, (int)Marshal.OffsetOf<ProcessorCoreRecord>(nameof(ProcessorCoreRecord.GroupCount)));
        Assert.Equal(32, (int)Marshal.OffsetOf<ProcessorCoreRecord>(nameof(ProcessorCoreRecord.GroupMask)));
    }

    [Fact]
    public void PdhAndBatteryOffsets()
    {
        Assert.Equal(NativeParsers.PdhItemSize, Marshal.SizeOf<PdhCounterValueItem>());
        Assert.Equal(8, (int)Marshal.OffsetOf<PdhCounterValueItem>(nameof(PdhCounterValueItem.Status)));
        Assert.Equal(16, (int)Marshal.OffsetOf<PdhCounterValueItem>(nameof(PdhCounterValueItem.Value)));
        Assert.Equal(NativeParsers.BatteryInformationSize, Marshal.SizeOf<BatteryInformation>());
        Assert.Equal(12, (int)Marshal.OffsetOf<BatteryInformation>(nameof(BatteryInformation.DesignedCapacity)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BatteryInformation>(nameof(BatteryInformation.CycleCount)));
        Assert.Equal(NativeParsers.BatteryStatusSize, Marshal.SizeOf<BatteryStatus>());
    }
}

public class NativeParsingTests
{
    private static byte[] IfTable(int declared, params (bool Hardware, bool Filter, uint Status, uint Type, ulong In, ulong Out)[] rows)
    {
        var buffer = new byte[NativeParsers.IfTableHeader + rows.Length * NativeParsers.IfRowSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)declared);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = buffer.AsSpan(NativeParsers.IfTableHeader + index * NativeParsers.IfRowSize, NativeParsers.IfRowSize);
            row[NativeParsers.IfRowFlags] = (byte)((rows[index].Hardware ? 1 : 0) | (rows[index].Filter ? 2 : 0));
            BinaryPrimitives.WriteUInt32LittleEndian(row[NativeParsers.IfRowOperStatus..], rows[index].Status);
            BinaryPrimitives.WriteUInt32LittleEndian(row[NativeParsers.IfRowType..], rows[index].Type);
            BinaryPrimitives.WriteUInt64LittleEndian(row[NativeParsers.IfRowInOctets..], rows[index].In);
            BinaryPrimitives.WriteUInt64LittleEndian(row[NativeParsers.IfRowOutOctets..], rows[index].Out);
        }
        return buffer;
    }

    [Fact]
    public void OnlyHardwareAdaptersThatAreUpAreCounted()
    {
        var table = IfTable(5,
            (true, false, 1, 71, 1_000, 400), // Wi-Fi
            (true, true, 1, 71, 1_000, 400), // its QoS filter, the same counters again
            (false, false, 1, 53, 9_000, 9_000), // a VPN adapter
            (true, false, 2, 6, 5_000, 5_000), // Ethernet, unplugged
            (true, false, 1, 6, 20, 10)); // Ethernet
        Assert.Equal(new NetworkTotals(1_020, 410), NativeParsers.NetworkTotals(table));
    }

    [Fact]
    public void ATableShorterThanItsCountStopsAtTheEnd()
    {
        var table = IfTable(9, (true, false, 1, 71, 1_000, 400));
        Assert.Equal(new NetworkTotals(1_000, 400), NativeParsers.NetworkTotals(table));
        Assert.Equal(default, NativeParsers.NetworkTotals([1, 0]));
    }

    [Fact]
    public void ProcessesAreWalkedAlongTheirNextOffsets()
    {
        const int entry = 300;
        const long address = 0x7FF0_0000_0000;
        var buffer = new byte[entry * 3 + 64];
        var nameOffset = entry * 3;
        Encoding.Unicode.GetBytes("chrome.exe").CopyTo(buffer, nameOffset);

        void Write(int index, uint next, long pid, long user, long kernel, ulong privateSet, int nameBytes, long nameAddress)
        {
            var span = buffer.AsSpan(index * entry);
            BinaryPrimitives.WriteUInt32LittleEndian(span, next);
            BinaryPrimitives.WriteUInt64LittleEndian(span[NativeParsers.ProcessWorkingSetPrivate..], privateSet);
            BinaryPrimitives.WriteInt64LittleEndian(span[NativeParsers.ProcessUserTime..], user);
            BinaryPrimitives.WriteInt64LittleEndian(span[NativeParsers.ProcessKernelTime..], kernel);
            BinaryPrimitives.WriteUInt16LittleEndian(span[NativeParsers.ProcessNameLength..], (ushort)nameBytes);
            BinaryPrimitives.WriteInt64LittleEndian(span[NativeParsers.ProcessNameBuffer..], nameAddress);
            BinaryPrimitives.WriteInt64LittleEndian(span[NativeParsers.ProcessId..], pid);
        }
        Write(0, entry, 0, 0, 900_000, 0, 0, 0);
        Write(1, entry, 4, 10, 20, 256, 0, 0);
        Write(2, 0, 1234, 100, 50, 4096, 20, address + nameOffset);

        var processes = NativeParsers.Processes(buffer, address);
        Assert.Equal([0, 4, 1234], processes.Select(process => process.Pid));
        Assert.Equal("chrome.exe", processes[2].Name);
        Assert.Equal(150, processes[2].CpuTime);
        Assert.Equal(4096UL, processes[2].PrivateWorkingSet);
        Assert.Equal("", processes[1].Name);
    }

    [Fact]
    public void AProcessEntryPointingOutsideTheBufferEndsTheWalk()
    {
        var buffer = new byte[NativeParsers.ProcessEntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 4096);
        Assert.Single(NativeParsers.Processes(buffer, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 8);
        Assert.Single(NativeParsers.Processes(buffer, 0));
    }

    [Fact]
    public void ProcessorTimes()
    {
        var buffer = new byte[NativeParsers.ProcessorTimesSize * 2];
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(48), 7);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(56), 9);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(64), 3);
        var times = NativeParsers.ProcessorTimes(buffer);
        Assert.Equal(2, times.Count);
        Assert.Equal((7L, 9L, 3L), times[1]);
    }

    [Fact]
    public void CoresOfAHybridChip()
    {
        var records = new List<byte[]>();
        byte[] Core(byte efficiencyClass, ulong mask)
        {
            var record = new byte[48];
            BinaryPrimitives.WriteInt32LittleEndian(record, NativeParsers.RelationProcessorCore);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 48);
            record[9] = efficiencyClass;
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(30), 1);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), mask);
            return record;
        }
        records.Add(Core(1, 0b11));
        records.Add(Core(1, 0b1100));
        var cache = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(cache, 2);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(4), 56);
        records.Add(cache);
        for (var index = 0; index < 4; index++) records.Add(Core(0, 1UL << (4 + index)));

        var cores = NativeParsers.Cores([.. records.SelectMany(record => record)]);
        Assert.Equal(6, cores.Count);
        Assert.Equal(((byte)1, 2), (cores[0].EfficiencyClass, cores[0].Logical));
        Assert.Equal((2, 4, 6), CpuMath.CoreSplit(cores));
        Assert.Equal((0, 0, 3), CpuMath.CoreSplit([(0, 2), (0, 2), (0, 2)]));
    }

    [Fact]
    public void PdhItemsCarryTheirInstanceNames()
    {
        const long address = 0x1000;
        var buffer = new byte[NativeParsers.PdhItemSize * 2 + 64];
        var names = NativeParsers.PdhItemSize * 2;
        Encoding.Unicode.GetBytes("\\_TZ.CPUZ\0\\_TZ.TZ00\0").CopyTo(buffer, names);
        void Item(int index, long nameAddress, uint status, double value)
        {
            var span = buffer.AsSpan(index * NativeParsers.PdhItemSize);
            BinaryPrimitives.WriteInt64LittleEndian(span, nameAddress);
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], status);
            BinaryPrimitives.WriteDoubleLittleEndian(span[16..], value);
        }
        Item(0, address + names, 0, 3_331);
        Item(1, address + names + 20, 0xC0000BC6, 1);
        var items = NativeParsers.PdhItems(buffer, address, 2);
        Assert.Single(items);
        Assert.Equal(("\\_TZ.CPUZ", 3_331d), items[0]);
    }

    [Fact]
    public void BatteryStructs()
    {
        var information = new byte[NativeParsers.BatteryInformationSize];
        BinaryPrimitives.WriteUInt32LittleEndian(information, 0x80000000);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(12), 57_000);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(16), 50_160);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(32), 312);
        var info = NativeParsers.ReadBatteryInformation(information);
        Assert.Equal((57_000U, 50_160U, 312U, false), (info.DesignedCapacity, info.FullChargedCapacity, info.CycleCount, info.IsRelative));

        var status = new byte[NativeParsers.BatteryStatusSize];
        BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(4), 25_000);
        BinaryPrimitives.WriteInt32LittleEndian(status.AsSpan(12), -8_400);
        Assert.Equal(-8_400, NativeParsers.ReadBatteryStatus(status).Rate);
        Assert.Equal(25_000U, NativeParsers.ReadBatteryStatus(status).Capacity);
    }
}

public class SamplingMathTests
{
    [Fact]
    public void CpuUsageFromSystemTimes()
    {
        // Kernel time includes idle: 1000 ticks total, 600 idle, 150 kernel work, 250 user.
        var usage = CpuMath.Usage((0, 0, 0), (600, 750, 250));
        Assert.Equal(0.25, usage!.Value.User, 6);
        Assert.Equal(0.15, usage.Value.System, 6);
        Assert.Null(CpuMath.Usage((5, 5, 5), (5, 5, 5)));
        Assert.Equal(0.4, CpuMath.Busy((0, 0, 0), (600, 750, 250)), 6);
    }

    [Fact]
    public void CleanBrand() => Assert.Equal(
        "Intel(R) Core(TM) i7-1165G7 @ 2.80GHz",
        CpuMath.CleanBrand("  Intel(R) Core(TM) i7-1165G7   @  2.80GHz  "));

    [Fact]
    public void NetworkRates()
    {
        Assert.Equal(new NetworkRate(500, 100), NetworkMath.Rate(new(1_000, 500), new(2_000, 700), 2));
        Assert.Equal(new NetworkRate(0, 100), NetworkMath.Rate(new(9_000, 500), new(2_000, 700), 2));
        Assert.Equal(default, NetworkMath.Rate(new(0, 0), new(10, 10), 0));
    }

    [Fact]
    public void ProcessNamesAndShares()
    {
        Assert.Equal("chrome", ProcessMath.DisplayName(10, "chrome.exe"));
        Assert.Equal("System", ProcessMath.DisplayName(4, ""));
        Assert.Equal("Registry", ProcessMath.DisplayName(120, "Registry"));
        Assert.Equal("pid 77", ProcessMath.DisplayName(77, ""));
        // One core busy for the whole second on a four-core machine.
        Assert.Equal(0.25, ProcessMath.CpuShare(0, 10_000_000, 10_000_000, 4), 6);
        Assert.Equal(0, ProcessMath.CpuShare(50, 10, 10_000_000, 4));
    }

    [Fact]
    public void ThermalZones()
    {
        Assert.Equal("CPUZ", ThermalMath.ZoneName("\\_TZ.CPUZ"));
        Assert.Equal("TZ00", ThermalMath.ZoneName("TZ00"));
        var zones = ThermalMath.Zones([("\\_TZ.CPUZ", 3_331), ("\\_TZ.BAT", 0), ("\\_TZ.X", 5_000)], 0.1);
        Assert.Single(zones);
        Assert.Equal(59.95, zones[0].Celsius, 6);
    }

    [Fact]
    public void DriveTitles()
    {
        Assert.Equal("Local Disk (C:)", DiskNames.DriveTitle("", "C:"));
        Assert.Equal("Data (D:)", DiskNames.DriveTitle("Data", "D:"));
    }
}

public class PowerStatusTests
{
    [Fact]
    public void ADesktopHasNoBattery()
    {
        Assert.Null(PowerStatus.FromPowerStatus(1, 128, 255, 0, uint.MaxValue));
        Assert.Null(PowerStatus.FromPowerStatus(1, 255, 50, 0, uint.MaxValue));
    }

    [Fact]
    public void OnBatteryWithAnEstimate()
    {
        var state = PowerStatus.FromPowerStatus(0, 1, 76, 1, 7_260)!;
        Assert.Equal(76, state.Percent);
        Assert.False(state.IsOnAC);
        Assert.False(state.IsCharging);
        Assert.Equal(121, state.MinutesRemaining);
        Assert.True(state.IsSaverOn);
    }

    [Fact]
    public void ChargingHasNoWindowsEstimate()
    {
        var state = PowerStatus.FromPowerStatus(1, 8 | 1, 40, 0, uint.MaxValue)!;
        Assert.True(state.IsCharging);
        Assert.True(state.IsOnAC);
        Assert.Null(state.MinutesRemaining);
    }

    [Fact]
    public void DetailsAddHealthRateAndTimeToFull()
    {
        var charging = new BatteryState(40, IsCharging: true, IsOnAC: true);
        var details = new BatteryDetails(Design: 57_000, Full: 50_160, Current: 20_064, RateMilliwatts: 30_096, CycleCount: 312, TemperatureCelsius: 31.2, IsRelative: false);
        var state = PowerStatus.WithDetails(charging, details);
        Assert.Equal(88, state.HealthPercent);
        Assert.Equal(312, state.CycleCount);
        Assert.Equal(30.096, state.RateWatts!.Value, 6);
        Assert.Equal(60, state.MinutesRemaining);
        Assert.Equal(31.2, state.TemperatureCelsius);
    }

    [Fact]
    public void RelativeCapacitiesGiveNoHealthOrRate()
    {
        var state = PowerStatus.WithDetails(new BatteryState(40, false, false),
            new BatteryDetails(100, 90, 40, 5, 0, null, IsRelative: true));
        Assert.Null(state.HealthPercent);
        Assert.Null(state.RateWatts);
        Assert.Null(state.CycleCount);
    }

    [Fact]
    public void TwoBatteriesReadAsOne()
    {
        var combined = new BatteryDetails(40_000, 36_000, 10_000, -5_000, 100, null, false)
            .Combine(new BatteryDetails(20_000, 19_000, 5_000, -2_000, 80, 30, false));
        Assert.Equal(new BatteryDetails(60_000, 55_000, 15_000, -7_000, 100, 30, false), combined);
    }
}
