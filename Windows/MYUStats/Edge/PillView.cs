using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using MYUStats.Views;

namespace MYUStats.Edge;

/// <summary>The pill's contents: the move handle, a ring per metric, and the settings button, laid out
/// along the edge with the sizes <see cref="EdgeLayout"/> hit-tests against.</summary>
internal sealed partial class PillView : Grid
{
    public PillView(IReadOnlyList<StatMetric> metrics, ScreenEdge edge)
    {
        var vertical = edge.IsVertical();
        var depth = vertical ? EdgeLayout.VerticalDepth : EdgeLayout.HorizontalDepth;
        var cellLength = vertical ? EdgeLayout.VerticalCellLength : EdgeLayout.HorizontalCellLength;
        (double Width, double Height) Box(double length) => vertical ? (depth, length) : (length, depth);

        var stack = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Padding = vertical
                ? new Thickness(0, EdgeLayout.BodyPadding, 0, EdgeLayout.BodyPadding)
                : new Thickness(EdgeLayout.BodyPadding, 0, EdgeLayout.BodyPadding, 0),
        };
        var button = Box(EdgeLayout.ButtonLength);
        MoveButton = new PillButtonView(PillButton.Move, button.Width, button.Height);
        SettingsButton = new PillButtonView(PillButton.Settings, button.Width, button.Height);
        var cell = Box(cellLength);
        Cells = [.. metrics.Select(metric => new MetricCell(metric, cell.Width, cell.Height))];

        stack.Children.Add(MoveButton);
        foreach (var metricCell in Cells) stack.Children.Add(metricCell);
        stack.Children.Add(SettingsButton);
        Children.Add(stack);
    }

    public PillButtonView MoveButton { get; }
    public PillButtonView SettingsButton { get; }
    public IReadOnlyList<MetricCell> Cells { get; }

    public void Update(StatsStore store)
    {
        foreach (var cell in Cells) cell.Update(store);
    }

    /// <summary>The rings and buttons come out of the edge one after another.</summary>
    public void Unfold(ScreenEdge edge)
    {
        var from = Motion.Outward(edge, 26);
        var order = 0;
        Motion.Unfold(MoveButton, order++, from);
        // The ring's content moves, not the cell: the cell's own scale is its hover effect.
        foreach (var cell in Cells) Motion.Unfold(cell.Body, order++, from);
        Motion.Unfold(SettingsButton, order, from);
    }
}

/// <summary>One ring: the gauge with the metric's icon inside and its value underneath.</summary>
internal sealed partial class MetricCell : Grid
{
    private readonly RingView ring;
    private readonly TextBlock value;
    private readonly FontIcon? batteryIcon;

    public MetricCell(StatMetric metric, double width, double height)
    {
        Metric = metric;
        Width = width;
        Height = height;
        // A transparent fill, not none, so the whole cell takes the pointer.
        Background = Palette.Transparent;

        FrameworkElement icon;
        if (metric == StatMetric.Battery)
        {
            batteryIcon = new FontIcon { FontSize = 13, Foreground = Palette.White(), Glyph = Glyphs.Battery(75, false) };
            icon = batteryIcon;
        }
        else
        {
            icon = Icons.Metric(metric, 13, Palette.White());
        }
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        ring = new RingView(34, 3.5, icon);
        value = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = Palette.Bold,
            FontFamily = Palette.Display,
            Foreground = Palette.White(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Body = UI.Stack(5, ring, value);
        Body.HorizontalAlignment = HorizontalAlignment.Center;
        Body.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Body);

        ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(220) };
        CenterPoint = new Vector3((float)width / 2, (float)height / 2, 0);
    }

    public StatMetric Metric { get; }

    /// <summary>The ring and value, which unfold out of the edge.</summary>
    public FrameworkElement Body { get; }

    public bool IsHovered
    {
        set => Scale = value ? new Vector3(1.08f, 1.08f, 1) : Vector3.One;
    }

    public void Update(StatsStore store)
    {
        var (fraction, color, text) = Reading(Metric, store);
        ring.Update(fraction, color);
        value.Text = text;
        if (batteryIcon is not null && store.Battery is { } battery)
        {
            batteryIcon.Glyph = Glyphs.Battery(battery.Percent, battery.IsCharging);
        }
    }

    /// <summary>How full the ring is, its colour, and the value under it.</summary>
    public static (double Fraction, Rgb Color, string Text) Reading(StatMetric metric, StatsStore store) => metric switch
    {
        StatMetric.Cpu => (store.Cpu.Total, Level.ColorFor(store.Cpu.Total), Format.Percent(store.Cpu.Total)),
        StatMetric.Gpu => store.Gpu?.Headline is { } gpu
            ? (gpu.Utilization, Level.ColorFor(gpu.Utilization), Format.Percent(gpu.Utilization))
            : (0, Level.Quiet, "–"),
        StatMetric.Memory => (store.Memory.Fraction, Level.ColorFor(store.Memory.Pressure), Format.Percent(store.Memory.Fraction)),
        // Relative to the recent peak, floored so an idle link does not look saturated.
        StatMetric.Network => (store.Network.Total / Math.Max(store.NetworkPeak, 250_000), Level.Accent, Format.ShortRate(store.Network.Total)),
        StatMetric.Disk => store.Disk is { } disk
            ? (disk.UsedFraction, Level.ColorFor(disk.UsedFraction), Format.Percent(disk.UsedFraction))
            : (0, Level.Quiet, "–"),
        StatMetric.Thermals => store.Thermals?.Headline is { } celsius
            ? (celsius / 110, Level.TemperatureColor(celsius), $"{Math.Round(celsius):0}°")
            : (0, Level.Quiet, "–"),
        StatMetric.Battery => store.Battery is { } battery
            ? (battery.Percent / 100.0, Level.BatteryColor(battery), $"{battery.Percent}%")
            : (0, Level.Quiet, "–"),
        _ => store.Claude.Usage?.Headline is { } window
            ? (window.UsedFraction, Level.ColorFor(window.UsedFraction), Format.Percent(window.UsedFraction))
            : (0, Level.Quiet, "–"),
    };
}

/// <summary>The move handle or the settings button at an end of the pill: a quiet glyph that brightens
/// on a disc when the pointer is on it.</summary>
internal sealed partial class PillButtonView : Grid
{
    private readonly Ellipse disc;
    private readonly FontIcon icon;

    public PillButtonView(PillButton kind, double width, double height)
    {
        Kind = kind;
        Width = width;
        Height = height;
        Background = Palette.Transparent;
        disc = new Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = Palette.White(0.14),
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) },
        };
        icon = new FontIcon
        {
            Glyph = kind == PillButton.Move ? Glyphs.Move : Glyphs.Settings,
            FontSize = 14,
            Width = 16,
            Height = 16,
            Foreground = Palette.White(),
            Opacity = 0.55,
            CenterPoint = new Vector3(8, 8, 0),
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) },
            RotationTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(550) },
        };
        Children.Add(disc);
        Children.Add(icon);
        ProtectedCursor = InputSystemCursor.Create(kind == PillButton.Move ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Hand);
    }

    public PillButton Kind { get; }

    public bool IsHighlighted
    {
        set
        {
            disc.Opacity = value ? 1 : 0;
            icon.Opacity = value ? 1 : 0.55;
        }
    }

    /// <summary>One turn of the gear on click, so the press is seen before Settings comes up.</summary>
    public void Spin() => icon.Rotation += 360;
}
