using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MYUStats.Native;

namespace MYUStats.Sampling;

/// <summary>CPU load from two readings of cumulative times, as Windows reports them.</summary>
public static partial class CpuMath
{
    /// <summary>Whole-machine load between two GetSystemTimes readings; kernel time includes idle time.</summary>
    public static CpuUsage? Usage((long Idle, long Kernel, long User) before, (long Idle, long Kernel, long User) now)
    {
        var idle = now.Idle - before.Idle;
        var kernel = now.Kernel - before.Kernel;
        var user = now.User - before.User;
        var total = kernel + user;
        if (total <= 0 || idle < 0 || user < 0) return null;
        var system = Math.Max(0, kernel - idle);
        return new CpuUsage(Math.Clamp(user / (double)total, 0, 1), Math.Clamp(system / (double)total, 0, 1));
    }

    /// <summary>Busy share of one logical processor between two readings.</summary>
    public static double Busy((long Idle, long Kernel, long User) before, (long Idle, long Kernel, long User) now)
    {
        var total = now.Kernel - before.Kernel + (now.User - before.User);
        var idle = now.Idle - before.Idle;
        return total > 0 ? Math.Clamp((total - idle) / (double)total, 0, 1) : 0;
    }

    /// <summary>Performance and efficiency core counts: on a hybrid chip the fastest efficiency class is
    /// the performance cores; a chip with one class has no split.</summary>
    public static (int Performance, int Efficiency, int Physical) CoreSplit(IReadOnlyList<(byte EfficiencyClass, int Logical)> cores)
    {
        if (cores.Count == 0) return (0, 0, 0);
        var fastest = cores.Max(core => core.EfficiencyClass);
        var performance = cores.Count(core => core.EfficiencyClass == fastest);
        return performance == cores.Count ? (0, 0, cores.Count) : (performance, cores.Count - performance, cores.Count);
    }

    /// <summary>Intel pads its brand string with runs of spaces.</summary>
    public static string CleanBrand(string brand) => Spaces().Replace(brand.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

[SupportedOSPlatform("windows")]
internal sealed class CpuSampler
{
    private (long Idle, long Kernel, long User)? previous;

    public CpuUsage? Sample()
    {
        if (!Kernel32.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var current = (idle, kernel, user);
        var before = previous;
        previous = current;
        return before is { } earlier ? CpuMath.Usage(earlier, current) : null;
    }
}

/// <summary>Load of each logical processor, from per-processor time deltas.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class PerCoreSampler
{
    private readonly byte[] buffer = new byte[Math.Max(1, Environment.ProcessorCount) * NativeParsers.ProcessorTimesSize];
    private List<(long Idle, long Kernel, long User)> previous = [];

    public IReadOnlyList<double> Sample()
    {
        int status;
        uint written;
        fixed (byte* pointer = buffer)
        {
            status = Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessorPerformanceInformation, pointer, (uint)buffer.Length, out written);
        }
        if (status != 0) return [];
        var current = NativeParsers.ProcessorTimes(buffer.AsSpan(0, (int)Math.Min(written, (uint)buffer.Length)));
        var before = previous;
        previous = current;
        if (before.Count != current.Count) return [];
        return [.. current.Select((now, index) => CpuMath.Busy(before[index], now))];
    }
}

[SupportedOSPlatform("windows")]
internal static unsafe class CpuInfoReader
{
    public static CpuInfo Read()
    {
        var brand = "";
        try
        {
            brand = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string ?? "";
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Sampling.Notice($"Processor name unavailable: {error.Message}");
        }
        var (performance, efficiency, physical) = CpuMath.CoreSplit(Cores());
        return new CpuInfo(CpuMath.CleanBrand(brand), performance, efficiency, physical, Environment.ProcessorCount);
    }

    private static List<(byte EfficiencyClass, int Logical)> Cores()
    {
        uint length = 0;
        Kernel32.GetLogicalProcessorInformationEx(NativeParsers.RelationProcessorCore, null, ref length);
        if (length == 0 || length > 1 << 20) return [];
        var buffer = new byte[length];
        fixed (byte* pointer = buffer)
        {
            if (!Kernel32.GetLogicalProcessorInformationEx(NativeParsers.RelationProcessorCore, pointer, ref length)) return [];
        }
        return NativeParsers.Cores(buffer.AsSpan(0, (int)Math.Min(length, (uint)buffer.Length)));
    }
}
