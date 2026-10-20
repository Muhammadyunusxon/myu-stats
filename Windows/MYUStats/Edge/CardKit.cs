using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MYUStats.Views;

namespace MYUStats.Edge;

/// <summary>The building blocks of a hover card, in the macOS card's proportions.</summary>
internal static class CardKit
{
    public const double Padding = 14;
    public const double ContentWidth = EdgeLayout.CardWidth - 2 * Padding;

    /// <summary>Icon, bold title and a quieter subtitle.</summary>
    public static UIElement Header(FrameworkElement icon, Live live, Func<string> title, Func<string> subtitle)
    {
        var titles = UI.Stack(1,
            UI.Text(live, title, 14.5, Palette.Bold),
            UI.Text(live, subtitle, 11, color: () => Palette.White(0.6)));
        icon.Width = 22;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnSpacing = 9 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(titles, 1);
        row.Children.Add(icon);
        row.Children.Add(titles);
        return row;
    }

    /// <summary>A labelled bar with a caption underneath.</summary>
    public static UIElement Usage(Live live, Func<string> title, Func<string> trailing, Func<double> fraction, Func<Rgb> color, Func<string> caption)
    {
        var bar = new BarGauge();
        live.Add(() => bar.Update(fraction(), color()));
        return UI.Stack(6,
            UI.Row(UI.Text(live, title, 12, Palette.Medium), UI.Text(live, trailing, 12, Palette.Medium, () => Palette.White(0.6))),
            bar,
            UI.Text(live, caption, 11, color: () => Palette.White(0.75)));
    }

    public static UIElement Detail(Live live, string label, Func<string> value, Func<Brush>? valueColor = null, double size = 12) =>
        Detail(live, () => label, value, valueColor, size);

    public static UIElement Detail(Live live, Func<string> label, Func<string> value, Func<Brush>? valueColor = null, double size = 12) =>
        UI.Row(
            UI.Text(live, label, size),
            UI.Text(live, value, size, color: valueColor ?? (() => Palette.White(0.6))));

    /// <summary>A row whose value is a name rather than a number: the value shortens instead of the label.</summary>
    public static UIElement NameDetail(Live live, string label, Func<string> value)
    {
        var name = UI.Text(live, value, 12, color: () => Palette.White(0.6));
        name.TextAlignment = TextAlignment.Right;
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(name, 1);
        row.Children.Add(UI.Text(label, 12));
        row.Children.Add(name);
        return row;
    }

    public static UIElement Divider() => new Border { Height = 1, Background = Palette.White(0.12) };

    public static UIElement SectionTitle(string text) => new TextBlock
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture),
        FontSize = 10,
        FontWeight = Palette.SemiBold,
        Foreground = Palette.White(0.45),
        CharacterSpacing = 50,
    };

    /// <summary>The busiest processes, or a note while the first reading is still being taken.</summary>
    public static UIElement Processes(Live live, string title, Func<IReadOnlyList<ProcessUsage>> processes, Func<ProcessUsage, string> value) =>
        Rows(live, title, processes, process => process.Name, value);

    /// <summary>The processes using the GPU most, each with its share of its busiest engine.</summary>
    public static UIElement Processes(Live live, string title, Func<IReadOnlyList<GpuProcess>> processes) =>
        Rows(live, title, processes, process => process.Name, process => Format.Percent(process.Load));

    private static UIElement Rows<T>(Live live, string title, Func<IReadOnlyList<T>> items, Func<T, string> name, Func<T, string> value)
    {
        var measuring = UI.Visible(live, UI.Text(L.T("Measuring…"), 12, color: Palette.White(0.5)), () => items().Count == 0);
        var rows = UI.List(live, items, (item, rowLive) =>
            Detail(rowLive, () => name(item()), () => value(item()), size: 11), spacing: 3);
        return UI.Stack(4, SectionTitle(title), measuring, rows);
    }

    /// <summary>A warning line in amber, shown only while there is something to say.</summary>
    public static UIElement Warning(Live live, Func<string?> message)
    {
        var icon = Icons.Glyph(Glyphs.Warning, 12, Palette.Brush(Level.Caution));
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 0, 0);
        var text = new TextBlock
        {
            FontSize = 11,
            FontWeight = Palette.Medium,
            Foreground = Palette.Brush(Level.Caution),
            TextWrapping = TextWrapping.WrapWholeWords,
        };
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        row.Children.Add(icon);
        row.Children.Add(text);
        live.Add(() =>
        {
            var next = message();
            row.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
            text.Text = next ?? "";
        });
        return row;
    }
}
