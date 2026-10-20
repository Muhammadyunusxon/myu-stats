using System.Runtime.InteropServices;
using MYUStats.Interop;
using Windows.Graphics;

namespace MYUStats.Edge;

/// <summary>
/// The folded pill: a thin tab on the edge, inside a strip that runs along the pill's whole span.
/// </summary>
/// <remarks>
/// A plain layered Win32 window rather than a WinUI one, because it needs per-pixel transparency: the
/// tab is drawn, and the rest of the strip is painted with an alpha of 1/255. That is invisible, yet
/// still takes the pointer (fully clear pixels would let it through), so touching the edge anywhere
/// next to the pill opens it. Being a top-level window, it also hears about display changes.
/// </remarks>
internal sealed unsafe class RevealStrip : IDisposable
{
    private const string ClassName = "MYUStats.RevealStrip";
    private const uint BarelyThere = 0x01000000;
    private static RevealStrip? current;

    private readonly nint handle;
    private (RectInt32 Strip, RectInt32 Tab)? painted;

    public RevealStrip()
    {
        current = this;
        var instance = NativeMethods.GetModuleHandle(null);
        fixed (char* name = ClassName)
        {
            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)sizeof(NativeMethods.WindowClass),
                WindowProcedure = &WindowProcedure,
                Instance = instance,
                ClassName = name,
            };
            NativeMethods.RegisterClassEx(in windowClass);
        }
        handle = NativeMethods.CreateWindowEx(
            (uint)(NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost | NativeMethods.WsExNoActivate),
            ClassName, "MYU STATS", NativeMethods.WsPopup, 0, 0, 1, 1, 0, 0, instance, 0);
        if (handle == 0) Log.App.Error($"The edge strip could not be created: error {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>The pointer moved over the strip.</summary>
    public event Action? Hovered;

    /// <summary>Displays were added, removed, rearranged or rescaled, or the taskbar moved.</summary>
    public event Action? ScreenChanged;

    public void Show(RectInt32 strip, RectInt32 tab)
    {
        if (handle == 0) return;
        if (painted != (strip, tab))
        {
            Paint(strip, tab);
            painted = (strip, tab);
        }
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void Hide()
    {
        if (handle != 0) NativeMethods.ShowWindow(handle, NativeMethods.SwHide);
    }

    public void Dispose()
    {
        if (current == this) current = null;
        if (handle != 0) NativeMethods.DestroyWindow(handle);
    }

    private void Paint(RectInt32 strip, RectInt32 tab)
    {
        var width = Math.Max(1, strip.Width);
        var height = Math.Max(1, strip.Height);
        var header = new NativeMethods.BitmapInfoHeader
        {
            Size = (uint)sizeof(NativeMethods.BitmapInfoHeader),
            Width = width,
            Height = -height, // top-down
            Planes = 1,
            BitCount = 32,
        };
        var screen = NativeMethods.GetDC(0);
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var bitmap = NativeMethods.CreateDIBSection(screen, in header, 0, out var bits, 0, 0);
        if (bitmap == 0 || bits == 0)
        {
            NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(0, screen);
            return;
        }
        var previous = NativeMethods.SelectObject(memory, bitmap);
        DrawTab((uint*)bits, width, height, tab.X - strip.X, tab.Y - strip.Y, tab.Width, tab.Height);

        var destination = new NativeMethods.Point { X = strip.X, Y = strip.Y };
        var size = new NativeMethods.Size { Width = width, Height = height };
        var source = new NativeMethods.Point();
        var blend = new NativeMethods.BlendFunction
        {
            BlendOp = NativeMethods.AcSrcOver,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AcSrcAlpha,
        };
        if (!NativeMethods.UpdateLayeredWindow(handle, screen, in destination, in size, memory, in source, 0, in blend, NativeMethods.UlwAlpha))
        {
            Log.App.Error($"The edge strip could not be drawn: error {Marshal.GetLastPInvokeError()}");
        }

        NativeMethods.SelectObject(memory, previous);
        NativeMethods.DeleteObject(bitmap);
        NativeMethods.DeleteDC(memory);
        NativeMethods.ReleaseDC(0, screen);
    }

    /// <summary>A grey capsule with a light rim, readable on dark and light wallpapers alike, drawn
    /// with smooth edges into premultiplied BGRA.</summary>
    private static void DrawTab(uint* pixels, int width, int height, int tabX, int tabY, int tabWidth, int tabHeight)
    {
        var radius = Math.Min(tabWidth, tabHeight) / 2.0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = RoundedRectDistance(x + 0.5, y + 0.5, tabX, tabY, tabWidth, tabHeight, radius);
                var coverage = Math.Clamp(0.5 - distance, 0, 1);
                var index = y * width + x;
                if (coverage <= 0)
                {
                    pixels[index] = BarelyThere;
                    continue;
                }
                // Rim within a pixel of the edge, fill inside it.
                var rim = Math.Clamp(1.5 + distance, 0, 1);
                var gray = 150 * (1 - rim) + 235 * rim;
                var alpha = Math.Max(coverage * (0.78 * (1 - rim) + 0.9 * rim), 1 / 255.0);
                var a = (uint)Math.Round(alpha * 255);
                var c = (uint)Math.Round(gray * alpha);
                pixels[index] = a << 24 | c << 16 | c << 8 | c;
            }
        }
    }

    /// <summary>Signed distance from a point to a rounded rectangle: negative inside.</summary>
    private static double RoundedRectDistance(double px, double py, double x, double y, double width, double height, double radius)
    {
        var cx = Math.Clamp(px, x + radius, x + width - radius);
        var cy = Math.Clamp(py, y + radius, y + height - radius);
        var dx = px - cx;
        var dy = py - cy;
        return Math.Sqrt(dx * dx + dy * dy) - radius;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case NativeMethods.WmMouseActivate:
                    return NativeMethods.MaNoActivate;
                case NativeMethods.WmMouseMove:
                    current?.Hovered?.Invoke();
                    break;
                case NativeMethods.WmDisplayChange:
                case NativeMethods.WmSettingChange:
                case NativeMethods.WmDpiChanged:
                    current?.ScreenChanged?.Invoke();
                    break;
            }
        }
        catch (Exception error)
        {
            // Nothing may escape into Windows' own code.
            Log.App.Error($"Edge strip message {message:X} failed: {error}");
        }
        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }
}
