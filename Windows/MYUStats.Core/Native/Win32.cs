using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MYUStats.Native;

[SupportedOSPlatform("windows")]
internal static unsafe partial class Kernel32
{
    internal const int ErrorInsufficientBuffer = 122;
    internal const int LowMemoryResourceNotification = 0;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 1;
    internal const uint FileShareWrite = 2;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x80;
    internal static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    /// <summary>PERFORMANCE_INFORMATION: sizes are in pages of <see cref="PageSize"/> bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    /// <summary>Idle, kernel (idle included) and user time of all processors together, as FILETIMEs.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPerformanceInfo(out PerformanceInformation information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateMemoryResourceNotification(int type);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryMemoryResourceNotification(nint handle, [MarshalAs(UnmanagedType.Bool)] out bool state);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        nint device, uint ioControlCode, void* inBuffer, uint inBufferSize,
        void* outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class Ntdll
{
    internal const int SystemProcessInformation = 5;
    internal const int SystemProcessorPerformanceInformation = 8;
    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQuerySystemInformation(int informationClass, void* information, uint length, out uint returnLength);
}

[SupportedOSPlatform("windows")]
internal static partial class Iphlpapi
{
    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetIfTable2(out nint table);

    [LibraryImport("iphlpapi.dll")]
    internal static partial void FreeMibTable(nint memory);
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class Pdh
{
    internal const uint FormatDouble = 0x00000200;
    internal const uint FormatNoCap100 = 0x00008000;
    internal const uint MoreData = 0x800007D2;

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    /// <summary>Takes the English counter path, so it works on every display language.</summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    internal static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* items);

    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCloseQuery(nint query);
}

/// <summary>DXGI, for the list of GPUs: their names, memory and LUIDs, as Task Manager lists them.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Dxgi
{
    internal static readonly Guid Factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    internal const int NotFound = unchecked((int)0x887A0002);
    // Vtable slots: IUnknown takes 0-2, IDXGIObject 3-6, IDXGIFactory 7-11, then IDXGIFactory1's EnumAdapters1.
    internal const int ReleaseSlot = 2;
    internal const int EnumAdapters1Slot = 12;
    // IDXGIAdapter takes 7-9 after IDXGIObject, then IDXGIAdapter1's GetDesc1.
    internal const int GetDesc1Slot = 10;

    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    internal static int EnumAdapters1(nint factory, uint index, out nint adapter)
    {
        var call = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)factory)[EnumAdapters1Slot];
        nint result;
        var status = call(factory, index, &result);
        adapter = result;
        return status;
    }

    internal static int GetDesc1(nint adapter, byte* description)
    {
        var call = (delegate* unmanaged<nint, byte*, int>)(*(nint**)adapter)[GetDesc1Slot];
        return call(adapter, description);
    }

    internal static void Release(nint unknown)
    {
        if (unknown == 0) return;
        var call = (delegate* unmanaged<nint, uint>)(*(nint**)unknown)[ReleaseSlot];
        call(unknown);
    }
}

/// <summary>The kernel graphics interface Task Manager reads GPU temperatures and fan speeds from. Only the
/// calls are marked Windows-only, so the tests can check the structs' layouts on any platform.</summary>
internal static unsafe partial class D3dkmt
{
    internal const int AdapterPerfData = 62;

    [StructLayout(LayoutKind.Sequential)]
    internal struct OpenAdapterFromLuid
    {
        public uint LuidLow;
        public uint LuidHigh;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* Data;
        public uint DataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CloseAdapter
    {
        public uint Adapter;
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTOpenAdapterFromLuid")]
    internal static partial int OpenAdapter(ref OpenAdapterFromLuid arguments);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTQueryAdapterInfo")]
    internal static partial int Query(ref QueryAdapterInfo arguments);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTCloseAdapter")]
    internal static partial int Close(ref CloseAdapter arguments);
}

/// <summary>The shell's file operations, for moving caches to the Recycle Bin. Only the call is marked
/// Windows-only, so the tests can check the struct's layout on any platform.</summary>
internal static unsafe partial class Shell32
{
    private const uint Delete = 3;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoErrorUi = 0x0400;
    /// <summary>Asks before deleting for good what is too big for the Recycle Bin, instead of doing it quietly.</summary>
    private const ushort WantNukeWarning = 0x4000;

    /// <summary>SHFILEOPSTRUCTW, naturally aligned on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileOperation
    {
        public nint Window;
        public uint Function;
        public char* From;
        public char* To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public char* ProgressTitle;
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int Run(ref FileOperation operation);

    /// <summary>Moves the files and folders to the Recycle Bin in one operation.</summary>
    [SupportedOSPlatform("windows")]
    internal static void Recycle(IReadOnlyList<string> paths)
    {
        // Each path ends in a null, and the list in one more.
        var list = string.Concat(paths.Select(path => path + '\0')) + '\0';
        fixed (char* from = list)
        {
            var operation = new FileOperation
            {
                Function = Delete,
                From = from,
                Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi | WantNukeWarning,
            };
            var status = Run(ref operation);
            if (status != 0) throw new IOException($"SHFileOperation failed with 0x{status:X}");
            if (operation.AnyOperationsAborted != 0) throw new OperationCanceledException("Moving to the Recycle Bin was cancelled.");
        }
    }
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class SetupApi
{
    internal const uint DigcfPresent = 0x02;
    internal const uint DigcfDeviceInterface = 0x10;

    /// <summary>SP_DEVICE_INTERFACE_DATA.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nuint Reserved;
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static partial nint SetupDiGetClassDevs(in Guid classGuid, nint enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet, nint deviceInfoData, in Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet, ref DeviceInterfaceData data, void* detail, uint detailSize, out uint requiredSize, nint deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
