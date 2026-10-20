using System.Runtime.InteropServices;

namespace MYUStats.Interop;

internal static unsafe partial class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExTopmost = 0x00000008;
    internal const long WsExToolWindow = 0x00000080;
    internal const long WsExLayered = 0x00080000;
    internal const long WsExNoActivate = 0x08000000;
    internal const uint WsPopup = 0x80000000;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;
    internal static readonly nint HwndTopmost = -1;

    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;

    internal const int VkLButton = 0x01;
    internal const int VkRButton = 0x02;
    internal const int SmSwapButton = 23;

    internal const uint WmSettingChange = 0x001A;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmDisplayChange = 0x007E;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmDpiChanged = 0x02E0;
    internal const int MaNoActivate = 3;

    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwcpRound = 2;
    internal const uint DwmwaColorNone = 0xFFFFFFFE;

    internal const int MdtEffectiveDpi = 0;
    internal const uint EddGetDeviceInterfaceName = 0x00000001;

    internal const uint UlwAlpha = 0x00000002;
    internal const byte AcSrcOver = 0x00;
    internal const byte AcSrcAlpha = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>MONITORINFOEXW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        public fixed char Device[32];
    }

    /// <summary>DISPLAY_DEVICEW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayDevice
    {
        public uint Size;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceId[128];
        public fixed char DeviceKey[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    /// <summary>BITMAPINFOHEADER, enough for a 32-bit top-down DIB.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    /// <summary>WNDCLASSEXW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    /// <summary>Lets any process bring its window forward; a second launch calls it before handing over.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    internal const uint AsfwAny = unchecked((uint)-1);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint window, int attribute, void* value, int size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(in WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateLayeredWindow(
        nint window, nint destinationDc, in Point destination, in Size size, nint sourceDc, in Point source,
        uint colorKey, in BlendFunction blend, uint flags);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(nint dc, in BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint dc);

    /// <summary>Whether the primary mouse button is down right now (the right one for a left-handed mouse).</summary>
    internal static bool IsPrimaryButtonDown()
    {
        var key = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }
}
