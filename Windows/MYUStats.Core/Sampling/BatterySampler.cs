using System.Runtime.Versioning;
using MYUStats.Native;

namespace MYUStats.Sampling;

/// <summary>What the battery driver reports beyond the charge level. Capacities in mWh (or relative
/// units when <see cref="IsRelative"/>), the rate in mW, positive while charging.</summary>
public sealed record BatteryDetails(uint Design, uint Full, uint? Current, int? RateMilliwatts, uint CycleCount, double? TemperatureCelsius, bool IsRelative)
{
    /// <summary>Two batteries (some laptops have them) read as one.</summary>
    public BatteryDetails Combine(BatteryDetails other) => new(
        Design + other.Design,
        Full + other.Full,
        Current is { } current && other.Current is { } otherCurrent ? current + otherCurrent : Current ?? other.Current,
        RateMilliwatts is { } rate && other.RateMilliwatts is { } otherRate ? rate + otherRate : RateMilliwatts ?? other.RateMilliwatts,
        Math.Max(CycleCount, other.CycleCount),
        TemperatureCelsius is { } temperature && other.TemperatureCelsius is { } otherTemperature
            ? Math.Max(temperature, otherTemperature)
            : TemperatureCelsius ?? other.TemperatureCelsius,
        IsRelative || other.IsRelative);
}

public static class PowerStatus
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;
    private const byte ChargingFlag = 8;
    private const uint UnknownTime = uint.MaxValue;

    /// <summary>SYSTEM_POWER_STATUS as a battery reading; null on a PC without one.</summary>
    public static BatteryState? FromPowerStatus(byte acLineStatus, byte batteryFlag, byte lifePercent, byte systemStatusFlag, uint lifeTimeSeconds)
    {
        if (batteryFlag == UnknownStatus || (batteryFlag & NoSystemBattery) != 0 || lifePercent > 100) return null;
        var onAC = acLineStatus == 1;
        return new BatteryState(lifePercent, (batteryFlag & ChargingFlag) != 0, onAC)
        {
            MinutesRemaining = !onAC && lifeTimeSeconds != UnknownTime && lifeTimeSeconds > 0 ? (int)(lifeTimeSeconds / 60) : null,
            IsSaverOn = systemStatusFlag == 1,
        };
    }

    /// <summary>Adds health, cycles, rate and temperature, and works out the time to full while charging,
    /// which Windows does not estimate itself.</summary>
    public static BatteryState WithDetails(BatteryState state, BatteryDetails details)
    {
        var absolute = !details.IsRelative;
        int? health = absolute && details.Design > 0 && details.Full > 0
            ? Math.Min(100, (int)Math.Round(details.Full * 100.0 / details.Design))
            : null;
        var minutes = state.MinutesRemaining;
        if (state.IsCharging && absolute && details.RateMilliwatts is int rate && rate > 0
            && details.Current is uint current && details.Full > current)
        {
            minutes = (int)Math.Round((details.Full - current) * 60.0 / rate);
        }
        return state with
        {
            HealthPercent = health,
            CycleCount = details.CycleCount > 0 ? (int)details.CycleCount : null,
            RateWatts = absolute && details.RateMilliwatts is int watts ? watts / 1000.0 : null,
            TemperatureCelsius = details.TemperatureCelsius,
            MinutesRemaining = minutes,
        };
    }
}

[SupportedOSPlatform("windows")]
internal static class BatterySampler
{
    public static BatteryState? Sample()
    {
        if (!Kernel32.GetSystemPowerStatus(out var status)) return null;
        var state = PowerStatus.FromPowerStatus(status.ACLineStatus, status.BatteryFlag, status.BatteryLifePercent,
            status.SystemStatusFlag, status.BatteryLifeTime);
        if (state is null) return null;
        return BatteryDevices.Read() is { } details ? PowerStatus.WithDetails(state, details) : state;
    }
}

/// <summary>
/// Asks each battery's driver directly (IOCTL_BATTERY_QUERY_*), which needs no administrator rights.
/// Any step that fails leaves those details out rather than the whole reading.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class BatteryDevices
{
    private static readonly Guid BatteryClass = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private const uint QueryTag = 0x294040;
    private const uint QueryInformation = 0x294044;
    private const uint QueryStatus = 0x29404C;
    private const int LevelInformation = 0;
    private const int LevelTemperature = 2;
    /// <summary>cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on 64-bit Windows.</summary>
    private const uint DetailHeaderSize = 8;

    public static BatteryDetails? Read()
    {
        var set = SetupApi.SetupDiGetClassDevs(in BatteryClass, 0, 0, SetupApi.DigcfPresent | SetupApi.DigcfDeviceInterface);
        if (set == Kernel32.InvalidHandle) return null;
        try
        {
            BatteryDetails? total = null;
            for (uint index = 0; index < 4; index++)
            {
                var data = new SetupApi.DeviceInterfaceData { Size = (uint)sizeof(SetupApi.DeviceInterfaceData) };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, 0, in BatteryClass, index, ref data)) break;
                if (DevicePath(set, ref data) is not { } path || Query(path) is not { } details) continue;
                total = total is null ? details : total.Combine(details);
            }
            return total;
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? DevicePath(nint set, ref SetupApi.DeviceInterfaceData data)
    {
        SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref data, null, 0, out var required, 0);
        if (required <= DetailHeaderSize || required > 4096) return null;
        var buffer = new byte[required];
        fixed (byte* pointer = buffer)
        {
            *(uint*)pointer = DetailHeaderSize;
            if (!SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref data, pointer, required, out _, 0)) return null;
            return new string((char*)(pointer + 4));
        }
    }

    private static BatteryDetails? Query(string path)
    {
        var device = Kernel32.CreateFile(path, Kernel32.GenericRead | Kernel32.GenericWrite,
            Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, Kernel32.FileAttributeNormal, 0);
        if (device == Kernel32.InvalidHandle) return null;
        try
        {
            uint wait = 0;
            uint tag = 0;
            if (!Kernel32.DeviceIoControl(device, QueryTag, &wait, sizeof(uint), &tag, sizeof(uint), out _, 0) || tag == 0) return null;

            // BATTERY_QUERY_INFORMATION: tag, level, rate.
            var query = stackalloc int[3];
            query[0] = (int)tag;
            query[1] = LevelInformation;
            var information = stackalloc byte[NativeParsers.BatteryInformationSize];
            if (!Kernel32.DeviceIoControl(device, QueryInformation, query, 12, information,
                    NativeParsers.BatteryInformationSize, out _, 0))
            {
                return null;
            }
            var info = NativeParsers.ReadBatteryInformation(new ReadOnlySpan<byte>(information, NativeParsers.BatteryInformationSize));

            // BATTERY_WAIT_STATUS with no wait: the status right now.
            var waitStatus = stackalloc uint[5];
            waitStatus[0] = tag;
            var statusBuffer = stackalloc byte[NativeParsers.BatteryStatusSize];
            NativeParsers.BatteryStatus? status = Kernel32.DeviceIoControl(device, QueryStatus, waitStatus, 20, statusBuffer,
                NativeParsers.BatteryStatusSize, out _, 0)
                ? NativeParsers.ReadBatteryStatus(new ReadOnlySpan<byte>(statusBuffer, NativeParsers.BatteryStatusSize))
                : null;

            // Tenths of a kelvin, when the battery has a sensor; most do not report it.
            query[1] = LevelTemperature;
            uint tenthsKelvin = 0;
            double? temperature = Kernel32.DeviceIoControl(device, QueryInformation, query, 12, &tenthsKelvin, sizeof(uint), out _, 0)
                && tenthsKelvin > 0
                    ? tenthsKelvin / 10.0 - 273.15
                    : null;
            if (temperature is < 0 or > 90) temperature = null;

            return new BatteryDetails(
                info.DesignedCapacity,
                info.FullChargedCapacity,
                status is { Capacity: not NativeParsers.UnknownCapacity } known ? known.Capacity : null,
                status is { Rate: not NativeParsers.UnknownRate } measured ? measured.Rate : null,
                info.CycleCount,
                temperature,
                info.IsRelative);
        }
        finally
        {
            Kernel32.CloseHandle(device);
        }
    }
}
