using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace MYUStats.Views;

/// <summary>Colours and type for the pill and cards: white at stepped opacities over a dark surface,
/// and the level colours shared with the macOS app.</summary>
internal static class Palette
{
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = [];

    public static Rgb WhiteRgb { get; } = new(255, 255, 255);

    public static Color ColorOf(Rgb rgb, double opacity = 1) =>
        ColorHelper.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), rgb.R, rgb.G, rgb.B);

    /// <summary>A shared brush per colour: brushes can be used by any number of elements.</summary>
    public static SolidColorBrush Brush(Rgb rgb, double opacity = 1)
    {
        var color = ColorOf(rgb, opacity);
        var key = (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
        if (!Brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            Brushes[key] = brush;
        }
        return brush;
    }

    public static SolidColorBrush White(double opacity = 1) => Brush(WhiteRgb, opacity);

    public static SolidColorBrush Accent => Brush(Level.Accent);

    public static SolidColorBrush Transparent => Brush(new Rgb(0, 0, 0), 0);

    /// <summary>The solid surface, and the fallback when acrylic is off.</summary>
    public static SolidColorBrush SolidSurface => Brush(new Rgb(18, 18, 20));

    /// <summary>A theme brush from the app's resources (the dark theme), or <paramref name="fallback"/>.</summary>
    public static Brush Resource(string key, Brush fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : fallback;

    public static FontWeight Regular => FontWeights.Normal;
    public static FontWeight Medium => FontWeights.Medium;
    public static FontWeight SemiBold => FontWeights.SemiBold;
    public static FontWeight Bold => FontWeights.Bold;

    /// <summary>Segoe UI Variable's display cut for the big numbers.</summary>
    public static FontFamily Display { get; } = new("Segoe UI Variable Display");
}

/// <summary>Segoe Fluent Icons code points (Segoe MDL2 Assets on Windows 10 has the same ones).</summary>
internal static class Glyphs
{
    public const string Settings = "";
    public const string Move = "";
    public const string Cpu = "";
    public const string Disk = "";
    public const string Chart = "";
    public const string ChevronRight = "";
    public const string ChevronLeft = "";
    public const string ChevronUp = "";
    public const string ChevronDown = "";
    public const string Warning = "";
    public const string Refresh = "";
    public const string Power = "";
    public const string Download = "";
    public const string Upload = "";
    public const string Folder = "";
    public const string File = "";
    public const string OpenFolder = "";
    public const string Plug = "";
    public const string Gauge = "";
    public const string Personalize = "";
    public const string ArrowLeft = "";
    public const string ArrowRight = "";
    public const string ArrowUp = "";
    public const string ArrowDown = "";
    public const string Bell = "";
    public const string Delete = "";

    private static readonly string[] Discharging =
        ["", "", "", "", "", "", "", "", "", "", ""];

    private static readonly string[] Charging =
        ["", "", "", "", "", "", "", "", "", "", ""];

    public static string Battery(int percent, bool charging)
    {
        var level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
        return charging ? Charging[level] : Discharging[level];
    }

    public static string Edge(ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => ArrowLeft,
        ScreenEdge.Right => ArrowRight,
        ScreenEdge.Top => ArrowUp,
        _ => ArrowDown,
    };
}
