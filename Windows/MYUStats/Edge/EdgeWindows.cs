using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MYUStats.Interop;
using MYUStats.Views;
using Windows.Foundation;
using Windows.Graphics;

namespace MYUStats.Edge;

/// <summary>A borderless, always-on-top window that never takes focus, painted with the pill's surface.</summary>
internal class SurfaceWindow
{
    private SurfaceBackdrop? backdrop;
    private double lastScale;

    protected SurfaceWindow()
    {
        Window = new Window { Title = "MYU STATS" };
        Root = new Grid { Background = Palette.Transparent };
        Window.Content = Root;
        Handle = ToolWindow.Configure(Window);
        Root.Loaded += (_, _) =>
        {
            lastScale = Root.XamlRoot.RasterizationScale;
            Root.XamlRoot.Changed += (_, _) =>
            {
                // Moving to a display at another scale: Windows resizes the window by its own rule, so
                // the owner puts it back where it belongs.
                if (Root.XamlRoot is not { } root || Math.Abs(root.RasterizationScale - lastScale) < 0.001) return;
                lastScale = root.RasterizationScale;
                ScaleChanged?.Invoke();
            };
        };
    }

    protected Window Window { get; }
    protected Grid Root { get; }
    public nint Handle { get; }
    public bool IsVisible => Window.AppWindow.IsVisible;

    public event Action? ScaleChanged;

    public void Place(RectInt32 bounds) => ToolWindow.Place(Window, bounds);

    public void Hide()
    {
        if (Window.AppWindow.IsVisible) Window.AppWindow.Hide();
    }

    public void ApplySurface(SurfaceStyle style, double darkness)
    {
        if (style == SurfaceStyle.Solid || !Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
        {
            Window.SystemBackdrop = null;
            backdrop = null;
            Root.Background = Palette.SolidSurface;
            return;
        }
        Root.Background = Palette.Transparent;
        var tint = SurfaceBackdrop.TintFor(style, darkness);
        if (backdrop is null)
        {
            backdrop = new SurfaceBackdrop(tint);
            Window.SystemBackdrop = backdrop;
        }
        else
        {
            backdrop.TintOpacity = tint;
        }
    }

    public void Close() => Window.Close();
}

/// <summary>The pill itself.</summary>
internal sealed class PillWindow : SurfaceWindow
{
    public PillWindow()
    {
        Root.PointerEntered += (_, _) => PointerActivity?.Invoke();
        Root.PointerMoved += (_, _) => PointerActivity?.Invoke();
        Root.PointerExited += (_, _) => PointerActivity?.Invoke();
        Root.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
    }

    public PillView? View { get; private set; }

    /// <summary>Any pointer movement over the pill; the controller works out what is under it.</summary>
    public event Action? PointerActivity;

    public PillView Build(IReadOnlyList<StatMetric> metrics, ScreenEdge edge)
    {
        View = new PillView(metrics, edge);
        Root.Children.Clear();
        Root.Children.Add(View);
        return View;
    }

    /// <summary>The pill fades while a drop on another edge is armed, so it reads as leaving.</summary>
    public bool IsLeaving
    {
        set => Root.Opacity = value ? 0.4 : 1;
    }
}

/// <summary>The detail card beside a ring.</summary>
internal sealed class CardWindow : SurfaceWindow
{
    private readonly StackPanel body = new() { Spacing = 9 };
    private readonly Grid details;
    private readonly ContentControl content = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
    private Live live = new();

    public CardWindow()
    {
        details = DetailsButton();
        body.Padding = new Thickness(CardKit.Padding);
        body.Width = EdgeLayout.CardWidth;
        body.Children.Add(content);
        body.Children.Add(CardKit.Divider());
        body.Children.Add(details);
        Root.Children.Add(body);
        Root.PointerEntered += (_, _) => PointerActivity?.Invoke();
        Root.PointerMoved += (_, _) => PointerActivity?.Invoke();
        Root.PointerExited += (_, _) => PointerActivity?.Invoke();
    }

    public StatMetric? Metric { get; private set; }

    public event Action? PointerActivity;

    /// <summary>Clicked "Details": open this metric's page in Settings.</summary>
    public event Action<StatMetric>? DetailsRequested;

    public void Show(StatMetric metric, StatsStore store, AppSettings settings)
    {
        var switching = Metric is not null && IsVisible;
        Metric = metric;
        live = new Live();
        var view = DetailCards.Build(metric, store, settings, live);
        content.Content = view;
        if (switching) Motion.Crossfade(view);
    }

    public void Refresh() => live.Refresh();

    /// <summary>The card's height as laid out at its fixed width.</summary>
    public double Measure()
    {
        body.Measure(new Size(EdgeLayout.CardWidth, double.PositiveInfinity));
        return Math.Ceiling(body.DesiredSize.Height);
    }

    public void Appear(ScreenEdge edge) => Motion.Appear(body, Motion.Outward(edge, 16));

    public new void Hide()
    {
        Metric = null;
        base.Hide();
    }

    /// <summary>The row at the foot of the card that opens the metric's page in Settings.</summary>
    private Grid DetailsButton()
    {
        var chevron = Icons.Glyph(Glyphs.ChevronRight, 9, Palette.White(0.75));
        var row = new Grid
        {
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(7),
            Background = Palette.White(0.06),
            ColumnSpacing = 6,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Glyph(Glyphs.Chart, 11, Palette.White(0.75));
        var label = UI.Text(L.T("Details"), 12, Palette.SemiBold, Palette.White(0.75));
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(chevron);
        row.PointerEntered += (_, _) =>
        {
            row.Background = Palette.White(0.14);
            label.Foreground = Palette.White();
        };
        row.PointerExited += (_, _) =>
        {
            row.Background = Palette.White(0.06);
            label.Foreground = Palette.White(0.75);
        };
        row.Tapped += (_, _) =>
        {
            if (Metric is { } metric) DetailsRequested?.Invoke(metric);
        };
        return row;
    }
}

/// <summary>A slot on another edge while the pill is dragged; the one the cursor heads for lights up.</summary>
internal sealed class DropZoneWindow : SurfaceWindow
{
    private readonly Rectangle outline;
    private readonly FontIcon arrow;

    public DropZoneWindow(ScreenEdge edge)
    {
        outline = new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Margin = new Thickness(6),
            StrokeThickness = 1.5,
        };
        arrow = new FontIcon
        {
            Glyph = Glyphs.Edge(edge),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Root.Children.Add(outline);
        Root.Children.Add(arrow);
        IsArmed = false;
    }

    public bool IsArmed
    {
        set
        {
            outline.Stroke = value ? Palette.Accent : Palette.White(0.45);
            outline.StrokeThickness = value ? 2 : 1.5;
            outline.StrokeDashArray = value ? [] : [4, 3];
            outline.Fill = Palette.White(value ? 0.14 : 0.04);
            arrow.Foreground = value ? Palette.Accent : Palette.White(0.6);
        }
    }
}
