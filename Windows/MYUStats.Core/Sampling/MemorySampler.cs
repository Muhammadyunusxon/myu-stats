using System.Runtime.Versioning;
using MYUStats.Native;

namespace MYUStats.Sampling;

/// <summary>Physical memory, the commit charge and the pools, as Task Manager's Memory page shows them.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class MemorySampler
{
    /// <summary>Signalled by the memory manager when available memory runs low.</summary>
    private readonly nint lowMemory = Kernel32.CreateMemoryResourceNotification(Kernel32.LowMemoryResourceNotification);

    public (MemoryUsage? Usage, SystemCounts? Counts) Sample()
    {
        var status = new Kernel32.MemoryStatusEx { Length = (uint)sizeof(Kernel32.MemoryStatusEx) };
        if (!Kernel32.GlobalMemoryStatusEx(ref status)) return (null, null);

        ulong committed = 0, limit = 0, cached = 0, paged = 0, nonPaged = 0;
        SystemCounts? counts = null;
        if (Kernel32.GetPerformanceInfo(out var performance, (uint)sizeof(Kernel32.PerformanceInformation)))
        {
            var page = (ulong)performance.PageSize;
            committed = (ulong)performance.CommitTotal * page;
            limit = (ulong)performance.CommitLimit * page;
            cached = (ulong)performance.SystemCache * page;
            paged = (ulong)performance.KernelPaged * page;
            nonPaged = (ulong)performance.KernelNonpaged * page;
            counts = new SystemCounts((int)performance.ProcessCount, (int)performance.ThreadCount, (int)performance.HandleCount);
        }

        var low = lowMemory != 0 && Kernel32.QueryMemoryResourceNotification(lowMemory, out var signalled) && signalled;
        var pressure = MemoryPressures.From(status.TotalPhys, status.AvailPhys, committed, limit, low);
        return (new MemoryUsage(status.TotalPhys, status.AvailPhys, committed, limit, cached, paged, nonPaged, pressure), counts);
    }
}
