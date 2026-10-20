using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using MYUStats.Native;
using MYUStats.Sampling;

namespace MYUStats.Tests;

public class GpuMathTests
{
    private const string Nvidia = "0x00000000_0x0000C3D4";
    private const string Intel = "0x00000000_0x0000A1B2";

    [Fact]
    public void EngineInstancesAreParsed()
    {
        var engine = GpuMath.ParseEngine("pid_1234_luid_0x00000000_0x0000c3d4_phys_0_eng_3_engtype_VideoDecode");
        Assert.Equal(new GpuMath.EngineInstance(1234, Nvidia, 0, 3, "VideoDecode"), engine);
        Assert.Equal("Compute_0", GpuMath.ParseEngine("pid_8_luid_0x00000000_0x0000C3D4_phys_0_eng_5_engtype_Compute_0")?.Type);
        Assert.Null(GpuMath.ParseEngine("luid_0x00000000_0x0000C3D4_phys_0"));
        Assert.Null(GpuMath.ParseEngine("pid_x_luid_0x00000000_0x0000C3D4_phys_0_eng_0_engtype_3D"));
        Assert.Equal(Nvidia, GpuMath.ParseMemory("luid_0x00000000_0x0000C3D4_phys_0"));
        Assert.Null(GpuMath.ParseMemory("_Total"));
        Assert.Equal(Nvidia, GpuMath.AdapterKey(0, 0xC3D4));
    }

    [Fact]
    public void EngineNamesReadAsTaskManagerLabelsThem()
    {
        Assert.Equal("3D", GpuMath.EngineName("3D"));
        Assert.Equal("Video Decode", GpuMath.EngineName("VideoDecode"));
        Assert.Equal("Compute 0", GpuMath.EngineName("Compute_0"));
        Assert.Equal("Other", GpuMath.EngineName(""));
    }

    [Fact]
    public void AnAdapterIsAsBusyAsItsBusiestEngineAndEachEngineSumsItsProcesses()
    {
        (string, double)[] items =
        [
            ($"pid_100_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 30),
            ($"pid_200_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 25),
            ($"pid_200_luid_{Nvidia}_phys_0_eng_3_engtype_VideoDecode", 40),
            ($"pid_300_luid_{Intel}_phys_0_eng_0_engtype_3D", 5),
            ($"pid_300_luid_{Intel}_phys_0_eng_1_engtype_Copy", 180),
            ("garbage", 99),
        ];
        var (adapters, processes) = GpuMath.Engines(items);
        Assert.Equal(0.55, adapters[Nvidia].Utilization, 6);
        Assert.Equal(["3D", "Video Decode"], adapters[Nvidia].Engines.Select(engine => engine.Engine));
        // A counter over 100 % is a glitch, held to 100 %.
        Assert.Equal(1, adapters[Intel].Utilization, 6);
        Assert.Equal([300, 200, 100], processes.Select(process => process.Pid));
        Assert.Equal(0.4, processes[1].Load, 6);
    }

    [Fact]
    public void MemoryAddsUpThePhysicalAdaptersOfALinkedOne()
    {
        var memory = GpuMath.Memory([($"luid_{Nvidia}_phys_0", 1_000), ($"luid_{Nvidia}_phys_1", 500), ($"luid_{Intel}_phys_0", 7)]);
        Assert.Equal(1_500UL, memory[Nvidia]);
        Assert.Equal(7UL, memory[Intel]);
    }
}

/// <summary>The GPU structs, as dxgi.h and d3dkmthk.h declare them, against the offsets the parsers read.</summary>
public unsafe class GpuLayoutTests
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [Fact]
    public void AdapterDescriptionOffsets()
    {
        Assert.Equal(NativeParsers.AdapterDescSize, Marshal.SizeOf<AdapterDesc1>());
        Assert.Equal(NativeParsers.AdapterDescVendor, (int)Marshal.OffsetOf<AdapterDesc1>(nameof(AdapterDesc1.VendorId)));
        Assert.Equal(NativeParsers.AdapterDescDedicatedVideo, (int)Marshal.OffsetOf<AdapterDesc1>(nameof(AdapterDesc1.DedicatedVideoMemory)));
        Assert.Equal(NativeParsers.AdapterDescSharedSystem, (int)Marshal.OffsetOf<AdapterDesc1>(nameof(AdapterDesc1.SharedSystemMemory)));
        Assert.Equal(NativeParsers.AdapterDescLuid, (int)Marshal.OffsetOf<AdapterDesc1>(nameof(AdapterDesc1.LuidLow)));
        Assert.Equal(NativeParsers.AdapterDescFlags, (int)Marshal.OffsetOf<AdapterDesc1>(nameof(AdapterDesc1.Flags)));
    }

    [Fact]
    public void PerfDataOffsets()
    {
        Assert.Equal(NativeParsers.AdapterPerfSize, Marshal.SizeOf<AdapterPerfData>());
        Assert.Equal(NativeParsers.AdapterPerfFan, (int)Marshal.OffsetOf<AdapterPerfData>(nameof(AdapterPerfData.FanRPM)));
        Assert.Equal(NativeParsers.AdapterPerfTemperature, (int)Marshal.OffsetOf<AdapterPerfData>(nameof(AdapterPerfData.Temperature)));
    }

    [Fact]
    public void TheDeclaredCallStructsMatchTheHeaders()
    {
        // D3DKMT_QUERYADAPTERINFO: handle, type, then an 8-byte pointer and its size.
        Assert.Equal(24, Marshal.SizeOf<D3dkmt.QueryAdapterInfo>());
        Assert.Equal(8, (int)Marshal.OffsetOf<D3dkmt.QueryAdapterInfo>(nameof(D3dkmt.QueryAdapterInfo.Data)));
        Assert.Equal(12, Marshal.SizeOf<D3dkmt.OpenAdapterFromLuid>());
        // SHFILEOPSTRUCTW is naturally aligned on 64-bit Windows.
        Assert.Equal(56, Marshal.SizeOf<Shell32.FileOperation>());
        Assert.Equal(32, (int)Marshal.OffsetOf<Shell32.FileOperation>(nameof(Shell32.FileOperation.Flags)));
        Assert.Equal(36, (int)Marshal.OffsetOf<Shell32.FileOperation>(nameof(Shell32.FileOperation.AnyOperationsAborted)));
        Assert.Equal(48, (int)Marshal.OffsetOf<Shell32.FileOperation>(nameof(Shell32.FileOperation.ProgressTitle)));
    }

    [Fact]
    public void AnAdapterDescriptionReadsBack()
    {
        var buffer = new byte[NativeParsers.AdapterDescSize];
        Encoding.Unicode.GetBytes("NVIDIA GeForce RTX 3060\0").CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(NativeParsers.AdapterDescVendor), 0x10DE);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(NativeParsers.AdapterDescDedicatedVideo), 12UL << 30);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(NativeParsers.AdapterDescLuid), 0xC3D4);
        var description = NativeParsers.ReadAdapterDescription(buffer);
        Assert.Equal("NVIDIA GeForce RTX 3060", description.Name);
        Assert.Equal(12UL << 30, description.DedicatedVideoMemory);
        Assert.Equal(0xC3D4U, description.LuidLow);
        Assert.True(description.IsHardware);

        // The Basic Render Driver is software, and Microsoft's.
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(NativeParsers.AdapterDescVendor), 0x1414);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(NativeParsers.AdapterDescFlags), 2);
        Assert.False(NativeParsers.ReadAdapterDescription(buffer).IsHardware);

        var perf = new byte[NativeParsers.AdapterPerfSize];
        BinaryPrimitives.WriteUInt32LittleEndian(perf.AsSpan(NativeParsers.AdapterPerfTemperature), 612);
        BinaryPrimitives.WriteUInt32LittleEndian(perf.AsSpan(NativeParsers.AdapterPerfFan), 1_450);
        Assert.Equal((1_450U, 612U), NativeParsers.ReadAdapterPerf(perf));
    }
}
