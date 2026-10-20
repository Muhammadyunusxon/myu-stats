namespace MYUStats;

/// <summary>One ring on the edge pill.</summary>
public enum StatMetric
{
    Cpu,
    Gpu,
    Memory,
    Network,
    Disk,
    Thermals,
    Battery,
    /// <summary>Claude Code's usage limits; see <see cref="ClaudeUsageStore"/>.</summary>
    Claude,
}

public static class StatMetrics
{
    /// <summary>Every metric, in the default ring order.</summary>
    public static IReadOnlyList<StatMetric> All { get; } = Enum.GetValues<StatMetric>();

    /// <summary>The name saved in settings, the same one the macOS app uses.</summary>
    public static string Id(this StatMetric metric) => metric switch
    {
        StatMetric.Cpu => "cpu",
        StatMetric.Gpu => "gpu",
        StatMetric.Memory => "memory",
        StatMetric.Network => "network",
        StatMetric.Disk => "disk",
        StatMetric.Thermals => "thermals",
        StatMetric.Battery => "battery",
        StatMetric.Claude => "claude",
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    public static StatMetric? FromId(string? id)
    {
        foreach (var metric in All)
        {
            if (metric.Id() == id) return metric;
        }
        return null;
    }

    public static string Title(this StatMetric metric) => metric switch
    {
        StatMetric.Cpu => L.T("CPU"),
        StatMetric.Gpu => L.T("GPU"),
        StatMetric.Memory => L.T("Memory"),
        StatMetric.Network => L.T("Network"),
        StatMetric.Disk => L.T("Disk"),
        StatMetric.Thermals => L.T("Thermals"),
        StatMetric.Battery => L.T("Battery"),
        // A product name, the same in every language.
        StatMetric.Claude => "Claude Code",
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}

public enum ScreenEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

public static class ScreenEdges
{
    public static IReadOnlyList<ScreenEdge> All { get; } = Enum.GetValues<ScreenEdge>();

    public static bool IsVertical(this ScreenEdge edge) => edge is ScreenEdge.Left or ScreenEdge.Right;

    public static string Id(this ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => "left",
        ScreenEdge.Right => "right",
        ScreenEdge.Top => "top",
        ScreenEdge.Bottom => "bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    public static ScreenEdge? FromId(string? id)
    {
        foreach (var edge in All)
        {
            if (edge.Id() == id) return edge;
        }
        return null;
    }

    public static string Title(this ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => L.T("Left"),
        ScreenEdge.Right => L.T("Right"),
        ScreenEdge.Top => L.T("Top"),
        ScreenEdge.Bottom => L.T("Bottom"),
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };
}

/// <summary>How the pill and cards are painted.</summary>
public enum SurfaceStyle
{
    Acrylic,
    DarkAcrylic,
    Solid,
}

public static class SurfaceStyles
{
    public static IReadOnlyList<SurfaceStyle> All { get; } = Enum.GetValues<SurfaceStyle>();

    public static string Id(this SurfaceStyle style) => style switch
    {
        SurfaceStyle.Acrylic => "acrylic",
        SurfaceStyle.DarkAcrylic => "darkAcrylic",
        SurfaceStyle.Solid => "solid",
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    public static SurfaceStyle? FromId(string? id)
    {
        foreach (var style in All)
        {
            if (style.Id() == id) return style;
        }
        return null;
    }

    public static string Title(this SurfaceStyle style) => style switch
    {
        SurfaceStyle.Acrylic => L.T("Acrylic"),
        SurfaceStyle.DarkAcrylic => L.T("Dark acrylic"),
        SurfaceStyle.Solid => L.T("Solid black"),
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };

    public static string Explanation(this SurfaceStyle style) => style switch
    {
        SurfaceStyle.Acrylic => L.T("Blurs what is behind it, like the Windows flyouts."),
        SurfaceStyle.DarkAcrylic => L.T("The same blur under a darker tint: easier to read over bright windows."),
        SurfaceStyle.Solid => L.T("Opaque. Best contrast, and what Windows shows when transparency effects are off."),
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    };
}
