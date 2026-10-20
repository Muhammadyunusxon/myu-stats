using System.Globalization;

namespace MYUStats;

public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] ByteUnits = ["KB", "MB", "GB", "TB"];

    public static string Percent(double fraction) =>
        string.Create(Invariant, $"{(long)Math.Round(fraction * 100, MidpointRounding.AwayFromZero)}%");

    /// <summary>Binary units, like Task Manager and Explorer: "512 MB", "15.7 GB".</summary>
    public static string Bytes(ulong value)
    {
        if (value < 1024) return string.Format(CultureInfo.CurrentCulture, "{0} B", value);
        double scaled = value;
        var unit = -1;
        while (scaled >= 1024 && unit < ByteUnits.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }
        var pattern = unit == 0 || scaled >= 100 ? "0" : "0.0";
        return scaled.ToString(pattern, CultureInfo.CurrentCulture) + " " + ByteUnits[unit];
    }

    public static string Bytes(long value) => Bytes((ulong)Math.Max(0, value));

    /// <summary>Throughput in decimal units: "340 KB/s", "1.2 MB/s".</summary>
    public static string Rate(double bytesPerSecond)
    {
        var culture = CultureInfo.CurrentCulture;
        if (bytesPerSecond < 1e6) return Math.Round(bytesPerSecond / 1e3).ToString("0", culture) + " KB/s";
        if (bytesPerSecond < 1e9) return (bytesPerSecond / 1e6).ToString("0.0", culture) + " MB/s";
        return (bytesPerSecond / 1e9).ToString("0.00", culture) + " GB/s";
    }

    /// <summary>Compact form for the narrow ring label: "840K", "12M", "1.2G". A trickle below 1 KB/s reads "&lt;1K".</summary>
    public static string ShortRate(double bytesPerSecond)
    {
        (double Scale, string Suffix)[] units = [(1e9, "G"), (1e6, "M"), (1e3, "K")];
        foreach (var (scale, suffix) in units)
        {
            if (bytesPerSecond < scale) continue;
            var value = bytesPerSecond / scale;
            // One decimal below 10, unless it would round up to "10.0".
            if (Math.Round(value * 10, MidpointRounding.AwayFromZero) < 100) return value.ToString("0.0", Invariant) + suffix;
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Invariant) + suffix;
        }
        return bytesPerSecond > 0 ? "<1K" : "0K";
    }

    /// <summary>A token count, as compact as the rates: "845", "12K", "3.4M", "1.2B".</summary>
    public static string Tokens(long count)
    {
        (double Scale, string Suffix)[] units = [(1e9, "B"), (1e6, "M"), (1e3, "K")];
        for (var index = 0; index < units.Length; index++)
        {
            var (scale, suffix) = units[index];
            if (count < scale * 0.9995) continue;
            var scaled = count / scale;
            // One decimal below 10, unless it would round up to "10.0".
            if (Math.Round(scaled * 10, MidpointRounding.AwayFromZero) < 100) return scaled.ToString("0.0", Invariant) + suffix;
            var whole = Math.Round(scaled, MidpointRounding.AwayFromZero);
            // 999.6K is 1.0M, not 1000K.
            if (whole >= 1000 && index > 0) return (count / units[index - 1].Scale).ToString("0.0", Invariant) + units[index - 1].Suffix;
            return whole.ToString("0", Invariant) + suffix;
        }
        return count.ToString(Invariant);
    }

    /// <summary>Dollars, as API prices are quoted: "$18.20", or "&lt;$0.01" for a trace.</summary>
    public static string Cost(double dollars) =>
        dollars is > 0 and < 0.005 ? "<$0.01" : "$" + dollars.ToString("N2", CultureInfo.CurrentCulture);

    public static string Temperature(double? celsius) =>
        celsius is { } value ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Invariant) + " °C" : "–";

    public static string Duration(int minutes)
    {
        var hours = minutes / 60;
        return hours > 0 ? HoursMinutes(hours, minutes % 60) : L.F("{0}m", minutes);
    }

    public static string Uptime(double seconds)
    {
        var minutes = (int)(seconds / 60);
        var days = minutes / (60 * 24);
        var hours = minutes / 60 % 24;
        return days > 0 ? L.F("{0}d {1}h", days, hours) : HoursMinutes(hours, minutes % 60);
    }

    /// <summary>Time left until <paramref name="date"/>, as compact as <see cref="Uptime"/>: "45m", "3h 20m", "2d 5h".</summary>
    public static string TimeUntil(DateTimeOffset date, DateTimeOffset now)
    {
        var minutes = Math.Max(0, (int)((date - now).TotalMinutes));
        return minutes >= 60 * 24 ? Uptime(minutes * 60.0) : Duration(minutes);
    }

    /// <summary>How long ago a reading was taken.</summary>
    public static string UpdatedAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var minutes = (int)((now - date).TotalMinutes);
        if (minutes < 1) return L.T("Updated just now");
        if (minutes < 60) return L.F("Updated {0} min ago", minutes);
        return L.F("Updated {0} h ago", minutes / 60);
    }

    /// <summary>How long ago, as compact as the rest: "5 min ago", "3 h ago", "12 d ago", "4 mo ago", "2 y ago".</summary>
    public static string Ago(DateTimeOffset date, DateTimeOffset now)
    {
        var elapsed = now - date;
        if (elapsed.TotalHours < 1) return L.F("{0} min ago", Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed.TotalDays < 1) return L.F("{0} h ago", (int)elapsed.TotalHours);
        if (elapsed.TotalDays < 60) return L.F("{0} d ago", (int)elapsed.TotalDays);
        if (elapsed.TotalDays < 730) return L.F("{0} mo ago", (int)(elapsed.TotalDays / 30.44));
        return L.F("{0} y ago", (int)(elapsed.TotalDays / 365.25));
    }

    private static string HoursMinutes(int hours, int minutes) => L.F("{0}h {1}m", hours, minutes);
}

/// <summary>An sRGB colour, kept free of any UI framework so the level rules can be tested anywhere.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromUnit(double red, double green, double blue) =>
        new(ToByte(red), ToByte(green), ToByte(blue));

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);
}

public static class Level
{
    public static Rgb Good { get; } = Rgb.FromUnit(0.36, 0.84, 0.52);
    public static Rgb Caution { get; } = Rgb.FromUnit(1.0, 0.76, 0.28);
    public static Rgb Bad { get; } = Rgb.FromUnit(1.0, 0.38, 0.36);
    public static Rgb Accent { get; } = Rgb.FromUnit(0.42, 0.66, 1.0);
    /// <summary>For a ring with nothing to show yet.</summary>
    public static Rgb Quiet { get; } = Rgb.FromUnit(0.56, 0.56, 0.6);

    /// <summary>Green, amber, red, for anything where higher is worse.</summary>
    public static Rgb ColorFor(double fraction) => fraction < 0.6 ? Good : fraction < 0.85 ? Caution : Bad;

    public static Rgb ColorFor(MemoryPressure pressure) => pressure switch
    {
        MemoryPressure.Normal => Good,
        MemoryPressure.Warning => Caution,
        _ => Bad,
    };

    public static Rgb BatteryColor(BatteryState battery) =>
        battery.IsCharging || battery.IsOnAC ? Good : ColorFor(1 - battery.Percent / 100.0);

    /// <summary>Laptop thermal zones sit around 40–55 °C at rest and throttle in the high 90s.</summary>
    public static Rgb TemperatureColor(double celsius) => celsius < 70 ? Good : celsius < 90 ? Caution : Bad;
}
