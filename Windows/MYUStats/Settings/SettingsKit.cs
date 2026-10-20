using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MYUStats.Views;

namespace MYUStats.Settings;

/// <summary>A page of the Settings window.</summary>
internal interface ISettingsPage
{
    UIElement Root { get; }

    /// <summary>Called after every sample while the page is shown.</summary>
    void Refresh();

    /// <summary>Called when another page takes its place or the window closes.</summary>
    void Leave();
}

/// <summary>Builds pages in the style of Windows 11 Settings: grouped cards of rows, a caption under each.</summary>
internal static class SettingsKit
{
    public static Brush Secondary => Palette.Resource("TextFillColorSecondaryBrush", Palette.White(0.62));
    public static Brush Tertiary => Palette.Resource("TextFillColorTertiaryBrush", Palette.White(0.45));
    private static Brush CardBackground => Palette.Resource("CardBackgroundFillColorDefaultBrush", Palette.White(0.05));
    private static Brush CardStroke => Palette.Resource("CardStrokeColorDefaultBrush", Palette.White(0.08));
    private static Brush Divider => Palette.Resource("DividerStrokeColorDefaultBrush", Palette.White(0.08));

    /// <summary>The page frame: title, subtitle and the sections, in a scroller.</summary>
    public static ScrollViewer Page(string title, string subtitle, params UIElement?[] sections)
    {
        var header = UI.Stack(3,
            UI.Text(title, 22, Palette.SemiBold),
            UI.Paragraph(subtitle, 13, Secondary));
        header.Margin = new Thickness(0, 0, 0, 6);
        var stack = UI.Stack(18, [header, .. sections]);
        stack.Padding = new Thickness(28, 20, 28, 28);
        stack.MaxWidth = 820;
        stack.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>A titled card of rows, with an optional caption underneath.</summary>
    public static UIElement Section(string? header, string? footer, params UIElement?[] rows) =>
        Section(header is null ? null : UI.Text(header, 14, Palette.SemiBold), footer, rows);

    public static UIElement Section(UIElement? header, string? footer, params UIElement?[] rows)
    {
        var card = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            if (row is null) continue;
            if (!first) card.Children.Add(new Border { Height = 1, Background = Divider });
            first = false;
            card.Children.Add(row);
        }
        var box = new Border
        {
            Background = CardBackground,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = card,
        };
        return UI.Stack(8, header, box, footer is null ? null : Caption(footer));
    }

    public static TextBlock Caption(string text) => UI.Paragraph(text, 12, Secondary);

    /// <summary>A row: label (and an optional description) on the left, a control on the right.</summary>
    public static Grid Row(string label, UIElement? control, string? description = null) =>
        Row(UI.Text(label, 14), control, description);

    public static Grid Row(UIElement label, UIElement? control, string? description = null)
    {
        var left = UI.Stack(2, label, description is null ? null : Caption(description));
        left.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 16, MinHeight = 48 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 1);
            grid.Children.Add(element);
        }
        return grid;
    }

    /// <summary>A label with a value that changes with each sample.</summary>
    public static Grid Value(Live live, string label, Func<string> value, Func<Brush>? color = null) =>
        Value(live, () => label, value, color);

    public static Grid Value(Live live, Func<string> label, Func<string> value, Func<Brush>? color = null) =>
        Row(UI.Text(live, label, 14), UI.Text(live, value, 14, color: color ?? (() => Secondary)));

    /// <summary>A labelled bar, as the macOS Details pages draw each meter.</summary>
    public static UIElement Meter(Live live, Func<string> label, Func<string> value, Func<double> fraction, Func<Rgb> color)
    {
        var bar = new BarGauge(5);
        live.Add(() => bar.Update(fraction(), color()));
        var stack = UI.Stack(8, UI.Row(UI.Text(live, label, 14), UI.Text(live, value, 14, color: () => Secondary)), bar);
        stack.Padding = new Thickness(16, 12, 16, 14);
        return stack;
    }

    /// <summary>A chart row with the span it covers underneath.</summary>
    public static UIElement History(Live live, Func<IReadOnlyList<double>> values, Func<Rgb> color, double? ceiling, Func<double> seconds)
    {
        var chart = new Sparkline(64);
        live.Add(() => chart.Update(values(), color(), ceiling));
        var stack = UI.Stack(6, chart, UI.Text(live, () => L.F("Last {0} seconds", (int)seconds()), 12, color: () => Secondary));
        stack.Padding = new Thickness(16, 12, 16, 12);
        return stack;
    }

    /// <summary>Any content, padded like a row.</summary>
    public static UIElement Padded(UIElement content)
    {
        var border = new Border { Padding = new Thickness(16, 12, 16, 12), Child = content };
        return border;
    }

    public static ToggleSwitch Toggle(bool isOn, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = isOn, OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    public static ComboBox Choice(IReadOnlyList<string> items, int selected, Action<int> changed)
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var item in items) combo.Items.Add(item);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex);
        };
        return combo;
    }

    /// <summary>A slider with its value written beside it.</summary>
    public static UIElement Slider(double minimum, double maximum, double step, double value, Func<double, string> format, Action<double> changed, out Slider slider)
    {
        var control = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = step,
            Value = value,
            Width = 200,
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = UI.Text(format(value), 13, color: Secondary);
        label.MinWidth = 48;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        control.ValueChanged += (_, args) =>
        {
            label.Text = format(args.NewValue);
            changed(args.NewValue);
        };
        slider = control;
        return UI.Stack(Orientation.Horizontal, 12, control, label);
    }

    public static Button Button(string title, Action clicked)
    {
        var button = new Button { Content = title };
        button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>A small round icon button, for reordering and the like.</summary>
    public static Button IconButton(string glyph, string tip, Action clicked)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>An amber warning line inside a section.</summary>
    public static UIElement Warning(Live live, Func<string?> message, Func<Rgb>? color = null)
    {
        var icon = new FontIcon { Glyph = Glyphs.Warning, FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.WrapWholeWords };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        row.Children.Add(icon);
        row.Children.Add(text);
        var padded = Padded(row);
        live.Add(() =>
        {
            var next = message();
            padded.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
            text.Text = next ?? "";
            var brush = Palette.Brush(color?.Invoke() ?? Level.Caution);
            icon.Foreground = brush;
            text.Foreground = brush;
        });
        return padded;
    }
}
