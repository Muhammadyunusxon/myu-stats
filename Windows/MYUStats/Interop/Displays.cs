using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace MYUStats.Interop;

/// <summary>A display the pill can sit on: its work area (the screen minus the taskbar), in pixels and
/// in DIPs at the display's own scale.</summary>
internal sealed record DisplayInfo(string Id, string Name, RectInt32 WorkArea, double Scale, bool IsPrimary)
{
    public SizeD WorkAreaDips => new(WorkArea.Width / Scale, WorkArea.Height / Scale);

    public PointD ToDips(int x, int y) => new((x - WorkArea.X) / Scale, (y - WorkArea.Y) / Scale);

    /// <summary>A work-area rectangle in DIPs to screen pixels. Edges are rounded, not sizes, so a
    /// rectangle that touches the work area's edge still touches it after scaling.</summary>
    public RectInt32 ToPixels(RectD rect)
    {
        var left = WorkArea.X + (int)Math.Round(rect.X * Scale);
        var top = WorkArea.Y + (int)Math.Round(rect.Y * Scale);
        var right = WorkArea.X + (int)Math.Round(rect.Right * Scale);
        var bottom = WorkArea.Y + (int)Math.Round(rect.Bottom * Scale);
        return new RectInt32(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}

internal static unsafe class Displays
{
    /// <summary>Every display, the main one first.</summary>
    public static IReadOnlyList<DisplayInfo> All()
    {
        var result = new List<DisplayInfo>();
        var areas = DisplayArea.FindAll();
        // Indexed rather than enumerated: enumerating this collection has thrown in some releases.
        for (var index = 0; index < areas.Count; index++)
        {
            var area = areas[index];
            var monitor = Win32Interop.GetMonitorFromDisplayId(area.DisplayId);
            var (id, device, number) = Identify(monitor);
            var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpi, out _) == 0 && dpi > 0
                ? dpi / 96.0
                : 1.0;
            var bounds = area.OuterBounds;
            var name = device.Length > 0 && !device.Contains("Generic", StringComparison.OrdinalIgnoreCase)
                ? $"{device} · {bounds.Width} × {bounds.Height}"
                : L.F("Display {0} · {1} × {2}", number, bounds.Width, bounds.Height);
            result.Add(new DisplayInfo(id, name, area.WorkArea, scale, area.IsPrimary));
        }
        return [.. result.OrderByDescending(display => display.IsPrimary)];
    }

    /// <summary>The display chosen in Settings, or the main one while it is not connected.</summary>
    public static DisplayInfo? Chosen(string saved)
    {
        var all = All();
        return DisplayChoice.Index(saved, [.. all.Select(display => (string?)display.Id)]) is { } index ? all[index] : null;
    }

    /// <summary>
    /// A stable identifier (the monitor's device interface path, which survives restarts and
    /// reconnecting to the same port), the monitor's own name, and the number Windows gives it.
    /// </summary>
    private static (string Id, string Device, int Number) Identify(nint monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = (uint)sizeof(NativeMethods.MonitorInfoEx) };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return (monitor.ToString(), "", 1);
        var deviceName = new string(info.Device);
        var digits = new string(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        var number = int.TryParse(digits, out var parsed) ? parsed : 1;

        var device = new NativeMethods.DisplayDevice { Size = (uint)sizeof(NativeMethods.DisplayDevice) };
        if (!NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, NativeMethods.EddGetDeviceInterfaceName))
        {
            return (deviceName, "", number);
        }
        var interfacePath = new string(device.DeviceId);
        return (interfacePath.Length > 0 ? interfacePath : deviceName, new string(device.DeviceString), number);
    }
}
