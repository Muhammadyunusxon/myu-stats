using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.UI.Text;

namespace MYUStats.Views;

/// <summary>
/// Values that change with every sample. Each view registers how to bring itself up to date, and
/// <see cref="Refresh"/> runs them all, so elements are updated in place: nothing under the pointer is
/// rebuilt mid-click, and scroll positions stay put.
/// </summary>
internal sealed class Live
{
    private readonly List<Action> updates = [];

    public void Add(Action update)
    {
        updates.Add(update);
        update();
    }

    public void Refresh()
    {
        foreach (var update in updates) update();
    }
}

/// <summary>Circular gauge: a faint track and an arc from the top, clockwise.</summary>
internal sealed partial class RingView : Grid
{
    private readonly double thickness;
    private readonly Path arc;
    private readonly PathFigure figure = new() { IsClosed = false, IsFilled = false };
    private readonly ArcSegment segment = new() { SweepDirection = SweepDirection.Clockwise };

    public RingView(double size, double thickness, UIElement? center = null)
    {
        this.thickness = thickness;
        Width = size;
        Height = size;
        Children.Add(new Ellipse
        {
            Stroke = Palette.White(0.16),
            StrokeThickness = thickness,
            Margin = new Thickness(thickness / 2),
        });
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        arc = new Path
        {
            Data = geometry,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        Children.Add(arc);
        if (center is not null) Children.Add(center);
    }

    public void Update(double fraction, Rgb color)
    {
        var radius = Width / 2 - thickness / 2;
        var middle = Width / 2;
        // A full circle cannot be drawn as one arc; stop just short of it.
        var sweep = Math.Clamp(fraction, 0.001, 0.9999) * 2 * Math.PI;
        figure.StartPoint = new Point(middle, middle - radius);
        segment.Point = new Point(middle + radius * Math.Sin(sweep), middle - radius * Math.Cos(sweep));
        segment.Size = new Size(radius, radius);
        segment.IsLargeArc = sweep > Math.PI;
        arc.Stroke = Palette.Brush(color);
    }
}

/// <summary>A thin horizontal bar: a faint track and a coloured fill.</summary>
internal sealed partial class BarGauge : Grid
{
    private readonly Border fill;

    public BarGauge(double height = 4)
    {
        Height = height;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var track = new Border { Background = Palette.White(0.14), CornerRadius = new CornerRadius(height / 2) };
        SetColumnSpan(track, 2);
        Children.Add(track);
        fill = new Border { CornerRadius = new CornerRadius(height / 2) };
        Children.Add(fill);
    }

    public void Update(double fraction, Rgb color)
    {
        // A sliver stays visible at zero, as on macOS.
        var share = Math.Clamp(fraction, 0.015, 1);
        ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);
        fill.Background = Palette.Brush(color);
    }
}

/// <summary>Filled line chart; values are scaled to the largest sample, or to <c>ceiling</c>.</summary>
internal sealed partial class Sparkline : Grid
{
    private readonly Path area = new();
    private readonly Polyline line = new() { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
    private IReadOnlyList<double> values = [];
    private double? ceiling;
    private Rgb color = Level.Accent;

    public Sparkline(double height)
    {
        Height = height;
        Children.Add(area);
        Children.Add(line);
        SizeChanged += (_, _) => Draw();
    }

    public void Update(IReadOnlyList<double> values, Rgb color, double? ceiling)
    {
        this.values = values;
        this.color = color;
        this.ceiling = ceiling;
        Draw();
    }

    private void Draw()
    {
        var width = ActualWidth;
        var height = Height;
        var points = new PointCollection();
        var outline = new PointCollection();
        if (width > 0 && values.Count > 0)
        {
            var top = Math.Max(ceiling ?? values.Max(), double.Epsilon);
            for (var index = 0; index < values.Count; index++)
            {
                var x = values.Count > 1 ? width * index / (values.Count - 1) : 0;
                var y = height * (1 - Math.Min(values[index] / top, 1));
                points.Add(new Point(x, y));
                outline.Add(new Point(x, y));
            }
            points.Add(new Point(outline[^1].X, height));
            points.Add(new Point(outline[0].X, height));
        }
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        if (points.Count > 0)
        {
            figure.StartPoint = points[0];
            var segment = new PolyLineSegment();
            foreach (var point in points.Skip(1)) segment.Points.Add(point);
            figure.Segments.Add(segment);
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        area.Data = geometry;
        area.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Palette.ColorOf(color, 0.35), Offset = 0 },
                new GradientStop { Color = Palette.ColorOf(color, 0.02), Offset = 1 },
            },
        };
        line.Points = outline;
        line.Stroke = Palette.Brush(color);
    }
}

/// <summary>Small builders shared by the cards and the Settings pages.</summary>
internal static class UI
{
    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? Palette.Regular,
        Foreground = color ?? Palette.White(),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };

    public static TextBlock Text(Live live, Func<string> text, double size, FontWeight? weight = null, Func<Brush>? color = null)
    {
        var block = Text("", size, weight);
        live.Add(() =>
        {
            block.Text = text();
            if (color is not null) block.Foreground = color();
        });
        return block;
    }

    /// <summary>Text that wraps, for captions and explanations.</summary>
    public static TextBlock Paragraph(string text, double size, Brush color) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color,
        TextWrapping = TextWrapping.WrapWholeWords,
    };

    public static StackPanel Stack(double spacing, params UIElement?[] children) => Stack(Orientation.Vertical, spacing, children);

    public static StackPanel Stack(Orientation orientation, double spacing, params UIElement?[] children)
    {
        var stack = new StackPanel { Orientation = orientation, Spacing = spacing };
        foreach (var child in children)
        {
            if (child is not null) stack.Children.Add(child);
        }
        return stack;
    }

    /// <summary>A label on the left, filling the space, and something on the right.</summary>
    public static Grid Row(FrameworkElement left, FrameworkElement right, double spacing = 8)
    {
        var grid = new Grid { ColumnSpacing = spacing };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Shows <paramref name="element"/> only while <paramref name="visible"/> holds.</summary>
    public static T Visible<T>(Live live, T element, Func<bool> visible) where T : UIElement
    {
        live.Add(() => element.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed);
        return element;
    }

    /// <summary>A list that keeps its rows and only adds or removes the difference each time.</summary>
    public static StackPanel List<T>(Live live, Func<IReadOnlyList<T>> items, Func<Func<T>, Live, UIElement> row, double spacing)
    {
        var panel = new StackPanel { Spacing = spacing };
        var rows = new List<Live>();
        var current = new List<T>();
        live.Add(() =>
        {
            var next = items();
            while (rows.Count < next.Count)
            {
                var index = rows.Count;
                var rowLive = new Live();
                current.Add(next[index]);
                panel.Children.Add(row(() => current[index], rowLive));
                rows.Add(rowLive);
            }
            while (rows.Count > next.Count)
            {
                rows.RemoveAt(rows.Count - 1);
                current.RemoveAt(current.Count - 1);
                panel.Children.RemoveAt(panel.Children.Count - 1);
            }
            for (var index = 0; index < next.Count; index++)
            {
                current[index] = next[index];
                rows[index].Refresh();
            }
        });
        return panel;
    }
}
