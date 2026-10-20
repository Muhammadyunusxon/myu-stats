using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace MYUStats.Interop;

/// <summary>Turns a WinUI window into the pill's kind: borderless, always on top, never in the taskbar
/// or Alt+Tab, and never taking focus from the app you are working in.</summary>
internal static unsafe class ToolWindow
{
    public static nint Configure(Window window)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        var handle = WindowNative.GetWindowHandle(window);
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        style |= (nint)(NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, style);
        NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);

        // Rounded corners on Windows 11 (ignored on Windows 10), without the accent-coloured frame line.
        var corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaWindowCornerPreference, &corner, sizeof(int));
        var border = NativeMethods.DwmwaColorNone;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaBorderColor, &border, sizeof(uint));
        return handle;
    }

    /// <summary>Shows the window where asked without activating it.</summary>
    public static void Place(Window window, RectInt32 bounds)
    {
        var appWindow = window.AppWindow;
        if (appWindow.Position.X != bounds.X || appWindow.Position.Y != bounds.Y
            || appWindow.Size.Width != bounds.Width || appWindow.Size.Height != bounds.Height)
        {
            appWindow.MoveAndResize(bounds);
        }
        if (!appWindow.IsVisible) appWindow.Show(activateWindow: false);
    }
}

/// <summary>
/// The pill's surface: acrylic over whatever is behind it, tinted by the Darkness setting, or plain black
/// for the Solid style. The acrylic is kept in its active look: a tool window that never takes focus
/// would otherwise always show the flat inactive colour.
/// </summary>
internal sealed partial class SurfaceBackdrop : SystemBackdrop
{
    private static readonly Color Fallback = ColorHelper.FromArgb(255, 22, 22, 26);
    private DesktopAcrylicController? controller;
    private float tintOpacity;

    public SurfaceBackdrop(float tintOpacity) => this.tintOpacity = tintOpacity;

    public float TintOpacity
    {
        get => tintOpacity;
        set
        {
            tintOpacity = value;
            if (controller is not null) controller.TintOpacity = value;
        }
    }

    /// <summary>The tint for a style and darkness; the macOS app's darkness runs from 0 to 0.6.</summary>
    public static float TintFor(SurfaceStyle style, double darkness) =>
        (float)Math.Clamp(0.35 + darkness + (style == SurfaceStyle.DarkAcrylic ? 0.2 : 0), 0, 0.95);

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported())
        {
            connectedTarget.SystemBackdrop = null;
            return;
        }
        controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = ColorHelper.FromArgb(255, 12, 12, 14),
            TintOpacity = tintOpacity,
            LuminosityOpacity = 0.85f,
            FallbackColor = Fallback,
        };
        controller.AddSystemBackdropTarget(connectedTarget);
        controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark,
        });
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (controller is null) return;
        controller.RemoveSystemBackdropTarget(disconnectedTarget);
        controller.Dispose();
        controller = null;
    }
}
