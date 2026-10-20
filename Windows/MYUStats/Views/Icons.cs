using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace MYUStats.Views;

/// <summary>
/// Icons for the metrics. CPU, disk and battery come from Segoe Fluent Icons; the GPU, memory,
/// network, thermals and Claude Code are drawn here, since the icon font has no glyph that reads as them.
/// Drawn on a 16-unit grid with a round-capped stroke, like the font's own line weight.
/// </summary>
internal static class Icons
{
    public static FrameworkElement Glyph(string glyph, double size, Brush brush) => new FontIcon
    {
        Glyph = glyph,
        FontSize = size,
        Foreground = brush,
    };

    public static FrameworkElement Metric(StatMetric metric, double size, Brush brush, BatteryState? battery = null) => metric switch
    {
        StatMetric.Cpu => Glyph(Glyphs.Cpu, size, brush),
        StatMetric.Disk => Glyph(Glyphs.Disk, size, brush),
        StatMetric.Battery => Glyph(Glyphs.Battery(battery?.Percent ?? 75, battery?.IsCharging == true), size, brush),
        StatMetric.Gpu => Drawn(size, brush, Cube()),
        StatMetric.Memory => Drawn(size, brush, Memory()),
        StatMetric.Network => Drawn(size, brush, Network()),
        StatMetric.Thermals => Drawn(size, brush, Thermometer()),
        _ => Drawn(size, brush, Asterisk()),
    };

    private static FrameworkElement Drawn(double size, Brush brush, Geometry geometry) => new Viewbox
    {
        Width = size,
        Height = size,
        Child = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
        },
    };

    private static LineGeometry Line(double x1, double y1, double x2, double y2) =>
        new() { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };

    private static GeometryGroup Group(params Geometry[] parts)
    {
        var group = new GeometryGroup();
        foreach (var part in parts) group.Children.Add(part);
        return group;
    }

    /// <summary>A cube, for 3D graphics, as the macOS GPU ring shows.</summary>
    private static Geometry Cube() => Group(
        Line(8, 1.8, 14, 5), Line(14, 5, 14, 11), Line(14, 11, 8, 14.2), Line(8, 14.2, 2, 11), Line(2, 11, 2, 5), Line(2, 5, 8, 1.8),
        Line(2, 5, 8, 8.2), Line(8, 8.2, 14, 5), Line(8, 8.2, 8, 14.2));

    /// <summary>A memory module: the board, two chips and its contacts.</summary>
    private static Geometry Memory() => Group(
        new RectangleGeometry { Rect = new Rect(1.5, 4, 13, 7) },
        new RectangleGeometry { Rect = new Rect(3.5, 6, 3, 3) },
        new RectangleGeometry { Rect = new Rect(9.5, 6, 3, 3) },
        Line(4, 11, 4, 13.5), Line(8, 11, 8, 13.5), Line(12, 11, 12, 13.5));

    /// <summary>Up and down arrows, like the macOS ring's.</summary>
    private static Geometry Network() => Group(
        Line(5, 13.5, 5, 2.5), Line(2.3, 5.2, 5, 2.5), Line(7.7, 5.2, 5, 2.5),
        Line(11, 2.5, 11, 13.5), Line(8.3, 10.8, 11, 13.5), Line(13.7, 10.8, 11, 13.5));

    private static Geometry Thermometer() => Group(
        new EllipseGeometry { Center = new Point(8, 12), RadiusX = 2.6, RadiusY = 2.6 },
        Line(6.6, 9.8, 6.6, 3.2), Line(9.4, 9.8, 9.4, 3.2), Line(6.6, 3.2, 9.4, 3.2),
        Line(8, 11.6, 8, 6));

    /// <summary>Claude Code's mark, reduced to six spokes.</summary>
    private static Geometry Asterisk() => Group(
        Line(8, 1.8, 8, 14.2), Line(2.6, 4.9, 13.4, 11.1), Line(2.6, 11.1, 13.4, 4.9));
}
