using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MYUStats.Native;

/// <summary>
/// Reads the buffers Windows fills in, by field offset, for 64-bit Windows (x64 and ARM64 share these
/// layouts). Kept free of any Windows call so the offsets are tested on every platform.
/// </summary>
internal static class NativeParsers
{
    // MARK: - GetIfTable2 (MIB_IF_TABLE2 of MIB_IF_ROW2)

    /// <summary>sizeof(MIB_IF_ROW2).</summary>
    internal const int IfRowSize = 1352;
    /// <summary>The rows follow NumEntries, aligned to 8 bytes.</summary>
    internal const int IfTableHeader = 8;
    internal const int IfRowType = 1128;
    internal const int IfRowFlags = 1152;
    internal const int IfRowOperStatus = 1156;
    internal const int IfRowInOctets = 1208;
    internal const int IfRowOutOctets = 1280;

    private const byte HardwareInterface = 0x01;
    private const byte FilterInterface = 0x02;
    private const uint OperStatusUp = 1;
    private const uint SoftwareLoopback = 24;

    /// <summary>
    /// Sums the byte counters of interfaces that are real hardware and up. Filter drivers (the Wi-Fi
    /// "QoS Packet Scheduler" and friends) repeat their adapter's counters, and VPN adapters carry
    /// traffic already counted on the hardware, so both are left out.
    /// </summary>
    internal static NetworkTotals NetworkTotals(ReadOnlySpan<byte> table)
    {
        if (table.Length < IfTableHeader) return default;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        ulong received = 0;
        ulong sent = 0;
        for (var index = 0L; index < count; index++)
        {
            var start = IfTableHeader + index * IfRowSize;
            if (start + IfRowSize > table.Length) break;
            var row = table.Slice((int)start, IfRowSize);
            var flags = row[IfRowFlags];
            if ((flags & HardwareInterface) == 0 || (flags & FilterInterface) != 0) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(row[IfRowOperStatus..]) != OperStatusUp) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(row[IfRowType..]) == SoftwareLoopback) continue;
            received += BinaryPrimitives.ReadUInt64LittleEndian(row[IfRowInOctets..]);
            sent += BinaryPrimitives.ReadUInt64LittleEndian(row[IfRowOutOctets..]);
        }
        return new NetworkTotals(received, sent);
    }

    // MARK: - NtQuerySystemInformation(SystemProcessInformation)

    /// <summary>sizeof(SYSTEM_PROCESS_INFORMATION) before its thread array.</summary>
    internal const int ProcessEntrySize = 256;
    internal const int ProcessWorkingSetPrivate = 8;
    internal const int ProcessUserTime = 40;
    internal const int ProcessKernelTime = 48;
    internal const int ProcessNameLength = 56;
    internal const int ProcessNameBuffer = 64;
    internal const int ProcessId = 80;

    internal readonly record struct RawProcess(int Pid, string Name, long CpuTime, ulong PrivateWorkingSet);

    /// <param name="buffer">The filled buffer.</param>
    /// <param name="address">The buffer's address: image names point into it.</param>
    internal static List<RawProcess> Processes(ReadOnlySpan<byte> buffer, long address)
    {
        var result = new List<RawProcess>();
        var offset = 0;
        while (offset >= 0 && offset + ProcessEntrySize <= buffer.Length)
        {
            var entry = buffer[offset..];
            var pid = (int)BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessId..]);
            var cpuTime = BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessUserTime..])
                + BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessKernelTime..]);
            var privateSet = BinaryPrimitives.ReadUInt64LittleEndian(entry[ProcessWorkingSetPrivate..]);
            var nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(entry[ProcessNameLength..]);
            var nameAddress = BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessNameBuffer..]);
            var name = nameAddress == 0 ? "" : Utf16(buffer, nameAddress - address, nameBytes / 2);
            result.Add(new RawProcess(pid, name, cpuTime, privateSet));

            var next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (next == 0) break;
            if (next < ProcessEntrySize) break;
            offset += (int)next;
        }
        return result;
    }

    // MARK: - NtQuerySystemInformation(SystemProcessorPerformanceInformation)

    /// <summary>sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION).</summary>
    internal const int ProcessorTimesSize = 48;

    /// <summary>Idle, kernel (idle included) and user time of each logical processor, in 100 ns ticks.</summary>
    internal static List<(long Idle, long Kernel, long User)> ProcessorTimes(ReadOnlySpan<byte> buffer)
    {
        var result = new List<(long, long, long)>();
        for (var offset = 0; offset + ProcessorTimesSize <= buffer.Length; offset += ProcessorTimesSize)
        {
            var entry = buffer[offset..];
            result.Add((
                BinaryPrimitives.ReadInt64LittleEndian(entry),
                BinaryPrimitives.ReadInt64LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt64LittleEndian(entry[16..])));
        }
        return result;
    }

    // MARK: - GetLogicalProcessorInformationEx(RelationProcessorCore)

    internal const int RelationProcessorCore = 0;

    /// <summary>Each physical core: its efficiency class (higher is faster on hybrid chips) and how many
    /// logical processors it runs.</summary>
    internal static List<(byte EfficiencyClass, int Logical)> Cores(ReadOnlySpan<byte> buffer)
    {
        var result = new List<(byte, int)>();
        var offset = 0;
        while (offset + 8 <= buffer.Length)
        {
            var relationship = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(buffer[(offset + 4)..]);
            if (size < 8 || offset + size > buffer.Length) break;
            if (relationship == RelationProcessorCore && size >= 48)
            {
                var record = buffer.Slice(offset, size);
                var efficiencyClass = record[9];
                var groups = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
                var logical = 0;
                for (var group = 0; group < groups && 32 + (group + 1) * 16 <= size; group++)
                {
                    logical += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(record[(32 + group * 16)..]));
                }
                result.Add((efficiencyClass, Math.Max(1, logical)));
            }
            offset += size;
        }
        return result;
    }

    // MARK: - PdhGetFormattedCounterArray (PDH_FMT_COUNTERVALUE_ITEM_W, double values)

    internal const int PdhItemSize = 24;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;

    /// <param name="address">The buffer's address: instance names point into it.</param>
    internal static List<(string Name, double Value)> PdhItems(ReadOnlySpan<byte> buffer, long address, uint count)
    {
        var result = new List<(string, double)>();
        for (var index = 0; index < count && (index + 1) * PdhItemSize <= buffer.Length; index++)
        {
            var item = buffer[(index * PdhItemSize)..];
            var status = BinaryPrimitives.ReadUInt32LittleEndian(item[8..]);
            if (status is not (PdhValidData or PdhNewData)) continue;
            var nameAddress = BinaryPrimitives.ReadInt64LittleEndian(item);
            var name = nameAddress == 0 ? "" : Utf16(buffer, nameAddress - address, maxChars: 256);
            result.Add((name, BinaryPrimitives.ReadDoubleLittleEndian(item[16..])));
        }
        return result;
    }

    // MARK: - Battery IOCTLs

    internal const int BatteryInformationSize = 36;
    internal const int BatteryStatusSize = 16;
    internal const uint CapacityRelative = 0x40000000;
    internal const uint UnknownCapacity = 0xFFFFFFFF;
    internal const int UnknownRate = unchecked((int)0x80000000);

    /// <summary>BATTERY_INFORMATION: capacities in mWh, unless the capabilities say they are relative.</summary>
    internal readonly record struct BatteryInformation(uint Capabilities, uint DesignedCapacity, uint FullChargedCapacity, uint CycleCount)
    {
        public bool IsRelative => (Capabilities & CapacityRelative) != 0;
    }

    /// <summary>BATTERY_STATUS: capacity in mWh and the rate in mW, positive while charging.</summary>
    internal readonly record struct BatteryStatus(uint PowerState, uint Capacity, uint Voltage, int Rate);

    internal static BatteryInformation ReadBatteryInformation(ReadOnlySpan<byte> buffer) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(buffer),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]));

    internal static BatteryStatus ReadBatteryStatus(ReadOnlySpan<byte> buffer) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(buffer),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]),
        BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]));

    // MARK: - DXGI_ADAPTER_DESC1

    internal const int AdapterDescSize = 312;
    internal const int AdapterDescVendor = 256;
    internal const int AdapterDescDedicatedVideo = 272;
    internal const int AdapterDescSharedSystem = 288;
    internal const int AdapterDescLuid = 296;
    internal const int AdapterDescFlags = 304;
    private const uint AdapterFlagSoftware = 2;
    /// <summary>Microsoft's own adapters: the Basic Render Driver, the Basic Display Adapter, remote displays.</summary>
    private const uint MicrosoftVendor = 0x1414;

    internal readonly record struct AdapterDescription(string Name, uint Vendor, ulong DedicatedVideoMemory, ulong SharedSystemMemory,
        uint LuidLow, uint LuidHigh, bool IsSoftware)
    {
        /// <summary>A GPU worth a ring: real hardware, not a software renderer or a remote display.</summary>
        public bool IsHardware => !IsSoftware && Vendor != MicrosoftVendor;
    }

    internal static AdapterDescription ReadAdapterDescription(ReadOnlySpan<byte> buffer) => new(
        Utf16(buffer, 0, maxChars: 128).Trim(),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[AdapterDescVendor..]),
        BinaryPrimitives.ReadUInt64LittleEndian(buffer[AdapterDescDedicatedVideo..]),
        BinaryPrimitives.ReadUInt64LittleEndian(buffer[AdapterDescSharedSystem..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[AdapterDescLuid..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[(AdapterDescLuid + 4)..]),
        (BinaryPrimitives.ReadUInt32LittleEndian(buffer[AdapterDescFlags..]) & AdapterFlagSoftware) != 0);

    // MARK: - D3DKMT_ADAPTER_PERFDATA (WDDM 2.4)

    internal const int AdapterPerfSize = 64;
    internal const int AdapterPerfFan = 48;
    internal const int AdapterPerfTemperature = 56;

    /// <summary>Fan speed in rpm and temperature in tenths of a degree; zero where the driver does not say.</summary>
    internal static (uint FanRpm, uint DeciCelsius) ReadAdapterPerf(ReadOnlySpan<byte> buffer) => (
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[AdapterPerfFan..]),
        BinaryPrimitives.ReadUInt32LittleEndian(buffer[AdapterPerfTemperature..]));

    // MARK: - Strings

    /// <summary>UTF-16 text at <paramref name="offset"/>, up to <paramref name="maxChars"/> or the first null.</summary>
    private static string Utf16(ReadOnlySpan<byte> buffer, long offset, int maxChars)
    {
        if (offset < 0 || offset >= buffer.Length) return "";
        var bytes = buffer[(int)offset..];
        var length = Math.Min(maxChars * 2, bytes.Length & ~1);
        var text = bytes[..length];
        for (var index = 0; index + 1 < text.Length; index += 2)
        {
            if (text[index] == 0 && text[index + 1] == 0)
            {
                text = text[..index];
                break;
            }
        }
        return Encoding.Unicode.GetString(text);
    }
}
