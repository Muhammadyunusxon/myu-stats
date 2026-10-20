using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using MYUStats.Native;

namespace MYUStats.Sampling;

public static class GpuMath
{
    /// <summary>One "GPU Engine" counter instance: the process, adapter and engine it measures.</summary>
    public readonly record struct EngineInstance(int Pid, string Adapter, int Physical, int Index, string Type);

    /// <summary>The counters name an adapter by its LUID, high part first: "0x00000000_0x0000D1F6".</summary>
    public static string AdapterKey(uint luidHigh, uint luidLow) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{luidHigh:X8}_0x{luidLow:X8}");

    /// <summary>"pid_1234_luid_0x00000000_0x0000D1F6_phys_0_eng_3_engtype_VideoDecode".</summary>
    public static EngineInstance? ParseEngine(string instance)
    {
        var parts = instance.Split('_');
        if (parts.Length < 11 || parts[0] != "pid" || parts[2] != "luid" || parts[5] != "phys" || parts[7] != "eng" || parts[9] != "engtype")
        {
            return null;
        }
        if (!int.TryParse(parts[1], CultureInfo.InvariantCulture, out var pid)
            || Adapter(parts[3], parts[4]) is not { } adapter
            || !int.TryParse(parts[6], CultureInfo.InvariantCulture, out var physical)
            || !int.TryParse(parts[8], CultureInfo.InvariantCulture, out var index))
        {
            return null;
        }
        // Types can hold underscores of their own, as in "Compute_0".
        return new EngineInstance(pid, adapter, physical, index, string.Join('_', parts[10..]));
    }

    /// <summary>"luid_0x00000000_0x0000D1F6_phys_0", as the "GPU Adapter Memory" counters name their instances.</summary>
    public static string? ParseMemory(string instance)
    {
        var parts = instance.Split('_');
        return parts.Length >= 5 && parts[0] == "luid" && parts[3] == "phys" ? Adapter(parts[1], parts[2]) : null;
    }

    private static string? Adapter(string high, string low) =>
        Hex(high) is { } highPart && Hex(low) is { } lowPart ? AdapterKey(highPart, lowPart) : null;

    private static uint? Hex(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>"VideoDecode" → "Video Decode", "Compute_0" → "Compute 0", as Task Manager labels its graphs.</summary>
    public static string EngineName(string type)
    {
        if (type.Length == 0) return L.T("Other");
        var name = new StringBuilder(type.Length + 4);
        for (var index = 0; index < type.Length; index++)
        {
            var character = type[index];
            if (character == '_')
            {
                name.Append(' ');
                continue;
            }
            if (index > 0 && char.IsUpper(character) && char.IsLower(type[index - 1])) name.Append(' ');
            name.Append(character);
        }
        return name.ToString();
    }

    /// <summary>
    /// Loads from the "GPU Engine" counters, which come per process and engine. An engine's load is the sum
    /// over the processes using it; an adapter's is its busiest engine's, as Task Manager draws it; a
    /// process's is its share of the engine it uses most.
    /// </summary>
    public static (IReadOnlyDictionary<string, (double Utilization, IReadOnlyList<GpuEngineLoad> Engines)> Adapters,
        IReadOnlyList<(int Pid, double Load)> Processes) Engines(IEnumerable<(string Name, double Value)> items)
    {
        var engines = new Dictionary<(string Adapter, int Physical, int Index), (string Type, double Load)>();
        var processes = new Dictionary<int, double>();
        foreach (var (name, value) in items)
        {
            if (ParseEngine(name) is not { } engine) continue;
            var load = Math.Clamp(value, 0, 100) / 100;
            var key = (engine.Adapter, engine.Physical, engine.Index);
            engines[key] = (engine.Type, (engines.TryGetValue(key, out var sum) ? sum.Load : 0) + load);
            if (engine.Pid > 0) processes[engine.Pid] = Math.Max(processes.GetValueOrDefault(engine.Pid), load);
        }
        var adapters = engines
            .GroupBy(entry => entry.Key.Adapter)
            .ToDictionary(group => group.Key, group =>
            {
                IReadOnlyList<GpuEngineLoad> types =
                [
                    .. group
                        .GroupBy(entry => entry.Value.Type)
                        .Select(type => new GpuEngineLoad(EngineName(type.Key), Math.Min(1, type.Max(entry => entry.Value.Load))))
                        .OrderByDescending(type => type.Load),
                ];
                return (types.Count > 0 ? types[0].Load : 0, types);
            });
        IReadOnlyList<(int, double)> busiest = [.. processes.Select(entry => (entry.Key, Math.Min(1, entry.Value))).OrderByDescending(entry => entry.Item2)];
        return (adapters, busiest);
    }

    /// <summary>Bytes per adapter from a "GPU Adapter Memory" counter, the physical adapters of a linked one together.</summary>
    public static IReadOnlyDictionary<string, ulong> Memory(IEnumerable<(string Name, double Value)> items)
    {
        var result = new Dictionary<string, ulong>();
        foreach (var (name, value) in items)
        {
            if (ParseMemory(name) is not { } adapter || !(value >= 0)) continue;
            result[adapter] = result.GetValueOrDefault(adapter) + (ulong)value;
        }
        return result;
    }
}

/// <summary>
/// Load and memory of each GPU from the performance counters Task Manager's GPU page reads, which need
/// no administrator rights; names and sizes from DXGI; temperatures and fan speeds, where the driver
/// reports them, from the kernel graphics interface.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class GpuSampler : IDisposable
{
    private const int TopProcessCount = 10;
    /// <summary>Samples between looks at which GPUs are attached (an external GPU, a driver update).</summary>
    private const int AdapterRefreshEvery = 30;

    private sealed record Adapter(string Key, string Name, ulong DedicatedTotal, uint LuidLow, uint LuidHigh)
    {
        /// <summary>The kernel handle perf data is asked through; 0 when the adapter could not be opened.</summary>
        public uint Handle { get; set; }
        /// <summary>Cleared once the driver turns out not to report temperatures.</summary>
        public bool ReportsPerf { get; set; } = true;
    }

    private readonly nint query;
    private readonly nint engineCounter;
    private readonly nint dedicatedCounter;
    private readonly nint sharedCounter;
    private readonly Dictionary<int, string> names = [];
    private List<Adapter> adapters;
    private byte[] buffer = new byte[64 << 10];
    private int samples;

    private GpuSampler(nint query, nint engineCounter, nint dedicatedCounter, nint sharedCounter, List<Adapter> adapters)
    {
        this.query = query;
        this.engineCounter = engineCounter;
        this.dedicatedCounter = dedicatedCounter;
        this.sharedCounter = sharedCounter;
        this.adapters = adapters;
    }

    /// <summary>Null without a hardware GPU, or on Windows builds without the GPU counters.</summary>
    public static GpuSampler? Open()
    {
        var adapters = Enumerate();
        if (adapters.Count == 0) return null;
        if (Pdh.PdhOpenQuery(null, 0, out var query) != 0)
        {
            CloseHandles(adapters);
            return null;
        }
        if (Pdh.PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", 0, out var engines) != 0)
        {
            Pdh.PdhCloseQuery(query);
            CloseHandles(adapters);
            return null;
        }
        // Memory is a bonus: without these counters the ring still has its load.
        if (Pdh.PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out var dedicated) != 0) dedicated = 0;
        if (Pdh.PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Shared Usage", 0, out var shared) != 0) shared = 0;
        // Loads are rates: this first collection only sets the baseline the next one is measured from.
        Pdh.PdhCollectQueryData(query);
        return new GpuSampler(query, engines, dedicated, shared, adapters);
    }

    public GpuState? Sample()
    {
        if (++samples % AdapterRefreshEvery == 0) RefreshAdapters();
        if (Pdh.PdhCollectQueryData(query) != 0) return null;
        var (loads, processes) = GpuMath.Engines(Read(engineCounter));
        var dedicated = dedicatedCounter != 0 ? GpuMath.Memory(Read(dedicatedCounter)) : new Dictionary<string, ulong>();
        var shared = sharedCounter != 0 ? GpuMath.Memory(Read(sharedCounter)) : new Dictionary<string, ulong>();

        var readings = new List<GpuAdapter>(adapters.Count);
        foreach (var adapter in adapters)
        {
            // An adapter no process uses has no engine counters: it is idle.
            loads.TryGetValue(adapter.Key, out var load);
            var (fan, temperature) = Perf(adapter);
            readings.Add(new GpuAdapter(adapter.Key, adapter.Name)
            {
                Utilization = load.Utilization,
                Engines = load.Engines ?? [],
                DedicatedUsed = dedicated.TryGetValue(adapter.Key, out var used) ? used : null,
                DedicatedTotal = adapter.DedicatedTotal > 0 ? adapter.DedicatedTotal : null,
                SharedUsed = shared.TryGetValue(adapter.Key, out var sharedUsed) ? sharedUsed : null,
                TemperatureCelsius = temperature,
                FanRpm = fan,
            });
        }
        var top = processes.Take(TopProcessCount).Select(process => new GpuProcess(process.Pid, Name(process.Pid), process.Load)).ToList();
        foreach (var pid in names.Keys.Except(top.Select(process => process.Pid)).ToList()) names.Remove(pid);
        return new GpuState(readings, top);
    }

    /// <summary>The GPUs DXGI lists, less software renderers and Microsoft's remote and basic display adapters.</summary>
    private static List<Adapter> Enumerate()
    {
        var result = new List<Adapter>();
        if (Dxgi.CreateDXGIFactory1(Dxgi.Factory1, out var factory) < 0 || factory == 0) return result;
        try
        {
            var description = stackalloc byte[NativeParsers.AdapterDescSize];
            for (uint index = 0; index < 16; index++)
            {
                if (Dxgi.EnumAdapters1(factory, index, out var handle) < 0 || handle == 0) break;
                try
                {
                    if (Dxgi.GetDesc1(handle, description) < 0) continue;
                    var desc = NativeParsers.ReadAdapterDescription(new ReadOnlySpan<byte>(description, NativeParsers.AdapterDescSize));
                    if (!desc.IsHardware) continue;
                    var key = GpuMath.AdapterKey(desc.LuidHigh, desc.LuidLow);
                    // A GPU driving several outputs is still one adapter.
                    if (result.Any(adapter => adapter.Key == key)) continue;
                    var adapter = new Adapter(key, desc.Name.Length > 0 ? desc.Name : L.T("GPU"), desc.DedicatedVideoMemory, desc.LuidLow, desc.LuidHigh);
                    var open = new D3dkmt.OpenAdapterFromLuid { LuidLow = desc.LuidLow, LuidHigh = desc.LuidHigh };
                    if (D3dkmt.OpenAdapter(ref open) == 0) adapter.Handle = open.Adapter;
                    result.Add(adapter);
                }
                finally
                {
                    Dxgi.Release(handle);
                }
            }
        }
        finally
        {
            Dxgi.Release(factory);
        }
        return result;
    }

    private void RefreshAdapters()
    {
        var fresh = Enumerate();
        if (fresh.Select(adapter => adapter.Key).SequenceEqual(adapters.Select(adapter => adapter.Key)))
        {
            CloseHandles(fresh);
            return;
        }
        Log.Sampling.Info($"GPUs changed: {string.Join(", ", fresh.Select(adapter => adapter.Name))}");
        CloseHandles(adapters);
        adapters = fresh;
    }

    /// <summary>Fan speed and temperature from drivers that report them (WDDM 2.4 and later, mostly
    /// dedicated GPUs); an integrated GPU usually reports zero, which means unknown.</summary>
    private static (int? FanRpm, double? Celsius) Perf(Adapter adapter)
    {
        if (adapter.Handle == 0 || !adapter.ReportsPerf) return (null, null);
        var data = stackalloc byte[NativeParsers.AdapterPerfSize];
        new Span<byte>(data, NativeParsers.AdapterPerfSize).Clear();
        var arguments = new D3dkmt.QueryAdapterInfo
        {
            Adapter = adapter.Handle,
            Type = D3dkmt.AdapterPerfData,
            Data = data,
            DataSize = NativeParsers.AdapterPerfSize,
        };
        if (D3dkmt.Query(ref arguments) != 0)
        {
            adapter.ReportsPerf = false;
            return (null, null);
        }
        var (fan, deciCelsius) = NativeParsers.ReadAdapterPerf(new ReadOnlySpan<byte>(data, NativeParsers.AdapterPerfSize));
        return (fan > 0 ? (int)fan : null, deciCelsius is > 0 and < 1500 ? deciCelsius / 10.0 : null);
    }

    private string Name(int pid)
    {
        if (names.TryGetValue(pid, out var cached)) return cached;
        string name;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            name = $"pid {pid}";
        }
        names[pid] = name;
        return name;
    }

    private List<(string Name, double Value)> Read(nint counter)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var size = (uint)buffer.Length;
            uint count;
            uint status;
            List<(string Name, double Value)> items;
            fixed (byte* pointer = buffer)
            {
                status = Pdh.PdhGetFormattedCounterArray(counter, Pdh.FormatDouble | Pdh.FormatNoCap100, ref size, out count, pointer);
                items = status == 0
                    ? NativeParsers.PdhItems(new ReadOnlySpan<byte>(pointer, (int)Math.Min(size, (uint)buffer.Length)), (long)pointer, count)
                    : [];
            }
            if (status == Pdh.MoreData && size > buffer.Length && size < 16 << 20)
            {
                buffer = new byte[size];
                continue;
            }
            return items;
        }
        return [];
    }

    private static void CloseHandles(IEnumerable<Adapter> list)
    {
        foreach (var adapter in list)
        {
            if (adapter.Handle == 0) continue;
            var close = new D3dkmt.CloseAdapter { Adapter = adapter.Handle };
            D3dkmt.Close(ref close);
            adapter.Handle = 0;
        }
    }

    public void Dispose()
    {
        Pdh.PdhCloseQuery(query);
        CloseHandles(adapters);
    }
}
