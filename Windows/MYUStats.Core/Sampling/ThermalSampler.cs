using System.Runtime.Versioning;
using MYUStats.Native;

namespace MYUStats.Sampling;

public static class ThermalMath
{
    /// <summary>ACPI zone names come as "\_TZ.CPUZ"; the part after the scope is what the firmware calls it.</summary>
    public static string ZoneName(string instance)
    {
        var dot = instance.LastIndexOf('.');
        var name = dot >= 0 ? instance[(dot + 1)..] : instance.TrimStart('\\');
        return name.Length > 0 ? name : instance;
    }

    /// <summary>Kelvin to Celsius, dropping zones that report nothing or nonsense.</summary>
    public static IReadOnlyList<ThermalZone> Zones(IEnumerable<(string Name, double Value)> items, double scale) =>
    [
        .. items
            .Select(item => new ThermalZone(ZoneName(item.Name), item.Value * scale - 273.15))
            .Where(zone => zone.Celsius > 0 && zone.Celsius < 125),
    ];
}

/// <summary>
/// Reads the ACPI thermal zones through the "Thermal Zone Information" performance counters, which,
/// unlike the WMI class, need no administrator rights. Fan speeds have no Windows API at all; they
/// live behind vendor drivers, so the Windows app shows temperatures only.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class ThermalSampler : IDisposable
{
    private readonly nint query;
    private readonly nint counter;
    /// <summary>The high-precision counter is in tenths of a kelvin, the plain one in kelvins.</summary>
    private readonly double scale;
    private byte[] buffer = new byte[4096];

    private ThermalSampler(nint query, nint counter, double scale)
    {
        this.query = query;
        this.counter = counter;
        this.scale = scale;
    }

    /// <summary>Null when this PC reports no thermal zones, which is common on desktops.</summary>
    public static ThermalSampler? Open()
    {
        (string Path, double Scale)[] counters =
        [
            (@"\Thermal Zone Information(*)\High Precision Temperature", 0.1),
            (@"\Thermal Zone Information(*)\Temperature", 1),
        ];
        foreach (var (path, scale) in counters)
        {
            if (Pdh.PdhOpenQuery(null, 0, out var query) != 0) return null;
            if (Pdh.PdhAddEnglishCounter(query, path, 0, out var counter) == 0)
            {
                var sampler = new ThermalSampler(query, counter, scale);
                if (sampler.Sample() is not null) return sampler;
            }
            Pdh.PdhCloseQuery(query);
        }
        return null;
    }

    public ThermalState? Sample()
    {
        if (Pdh.PdhCollectQueryData(query) != 0) return null;
        for (var attempt = 0; attempt < 3; attempt++)
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
            if (status == Pdh.MoreData && size > buffer.Length && size < 1 << 20)
            {
                buffer = new byte[size];
                continue;
            }
            if (status != 0) return null;
            var zones = ThermalMath.Zones(items, scale);
            return zones.Count > 0 ? new ThermalState(zones) : null;
        }
        return null;
    }

    public void Dispose() => Pdh.PdhCloseQuery(query);
}
