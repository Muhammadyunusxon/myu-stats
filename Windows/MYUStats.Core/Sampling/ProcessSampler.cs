using System.Diagnostics;
using System.Runtime.Versioning;
using MYUStats.Native;

namespace MYUStats.Sampling;

public static class ProcessMath
{
    /// <summary>Task Manager shows image names without ".exe"; the kernel's own processes have none.</summary>
    public static string DisplayName(int pid, string imageName)
    {
        if (imageName.Length == 0) return pid == 4 ? "System" : $"pid {pid}";
        return imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;
    }

    /// <summary>Share of all processors a process used between two readings of its CPU time (100 ns ticks).</summary>
    public static double CpuShare(long before, long now, double elapsedTicks, int processors) =>
        elapsedTicks > 0 && now >= before && processors > 0
            ? Math.Clamp((now - before) / elapsedTicks / processors, 0, 1)
            : 0;
}

/// <summary>
/// Every process with its CPU and memory, from one NtQuerySystemInformation call. That covers other
/// users' and system processes too, without administrator rights and without opening each process.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class ProcessSampler
{
    private const int MaxBuffer = 64 << 20;
    private byte[] buffer = new byte[512 << 10];
    private Dictionary<int, long> previousTimes = [];
    private long previousTimestamp;

    public void Reset()
    {
        previousTimes = [];
        previousTimestamp = 0;
    }

    public IReadOnlyList<ProcessUsage> Sample()
    {
        List<NativeParsers.RawProcess> raw;
        while (true)
        {
            int status;
            uint written;
            fixed (byte* pointer = buffer)
            {
                status = Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessInformation, pointer, (uint)buffer.Length, out written);
                raw = status == 0
                    ? NativeParsers.Processes(new ReadOnlySpan<byte>(pointer, (int)Math.Min(written == 0 ? (uint)buffer.Length : written, (uint)buffer.Length)), (long)pointer)
                    : [];
            }
            if (status == Ntdll.StatusInfoLengthMismatch && buffer.Length < MaxBuffer)
            {
                // Processes start between the calls, so leave some room.
                buffer = new byte[Math.Min(MaxBuffer, Math.Max((int)written + (64 << 10), buffer.Length * 2))];
                continue;
            }
            if (status != 0) return [];
            break;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = previousTimestamp > 0 ? Stopwatch.GetElapsedTime(previousTimestamp, now).Ticks : 0;
        var processors = Environment.ProcessorCount;
        var times = new Dictionary<int, long>(raw.Count);
        var result = new List<ProcessUsage>(raw.Count);
        foreach (var process in raw)
        {
            // Pid 0 is the idle process: its "CPU" is the time nothing ran.
            if (process.Pid == 0) continue;
            times[process.Pid] = process.CpuTime;
            var cpu = previousTimes.TryGetValue(process.Pid, out var before)
                ? ProcessMath.CpuShare(before, process.CpuTime, elapsedTicks, processors)
                : 0;
            result.Add(new ProcessUsage(process.Pid, ProcessMath.DisplayName(process.Pid, process.Name), cpu, process.PrivateWorkingSet));
        }
        previousTimes = times;
        previousTimestamp = now;
        return result;
    }
}
