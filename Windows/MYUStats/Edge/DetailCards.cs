using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MYUStats.Views;

namespace MYUStats.Edge;

/// <summary>What the hover card shows for each metric.</summary>
internal static class DetailCards
{
    public static UIElement Build(StatMetric metric, StatsStore store, AppSettings settings, Live live) => metric switch
    {
        StatMetric.Cpu => Cpu(store, settings, live),
        StatMetric.Gpu => Gpu(store, settings, live),
        StatMetric.Memory => Memory(store, settings, live),
        StatMetric.Network => Network(store, live),
        StatMetric.Disk => Disk(store, live),
        StatMetric.Thermals => Thermals(store, live),
        StatMetric.Battery => Battery(store, live),
        _ => Claude(store.Claude, live),
    };

    private static FrameworkElement Icon(StatMetric metric) => Icons.Metric(metric, 17, Palette.White());

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static IReadOnlyList<ProcessUsage> Top(IReadOnlyList<ProcessUsage> processes) => [.. processes.Take(4)];

    private static UIElement Spark(Live live, Func<IReadOnlyList<double>> values, Func<Rgb> color, double? ceiling, double height)
    {
        var spark = new Sparkline(height);
        live.Add(() => spark.Update(values(), color(), ceiling));
        return spark;
    }

    private static UIElement Cpu(StatsStore store, AppSettings settings, Live live) => UI.Stack(9,
        CardKit.Header(Icon(StatMetric.Cpu), live, () => L.T("CPU"), () => store.CpuInfo.DisplayBrand),
        CardKit.Usage(live,
            () => L.T("Usage"),
            () => Format.Percent(store.Cpu.Total),
            () => store.Cpu.Total,
            () => Level.ColorFor(store.Cpu.Total),
            () => L.F("User {0} · System {1} · Idle {2}", Format.Percent(store.Cpu.User), Format.Percent(store.Cpu.System), Format.Percent(store.Cpu.Idle))),
        Spark(live, () => store.CpuHistory, () => Level.ColorFor(store.Cpu.Total), 1, 26),
        CardKit.Divider(),
        CardKit.Detail(live, L.T("Processes"), () => Count(store.Counts.Processes)),
        CardKit.Detail(live, L.T("Threads"), () => Count(store.Counts.Threads)),
        CardKit.Detail(live, L.T("Cores"), () => store.CpuInfo.CoresDescription),
        CardKit.Detail(live, L.T("Uptime"), () => Format.Uptime(Environment.TickCount64 / 1000.0)),
        UI.Visible(live, UI.Stack(9,
                CardKit.Divider(),
                CardKit.Processes(live, L.T("Top processes"), () => Top(store.TopByCpu), process => Format.Percent(process.Cpu))),
            () => settings.ShowProcesses));

    private static UIElement Gpu(StatsStore store, AppSettings settings, Live live)
    {
        GpuAdapter? Busiest() => store.Gpu?.Headline;
        double Load() => Busiest()?.Utilization ?? 0;
        var others = UI.List<GpuAdapter>(live,
            () => store.Gpu is { } gpu && Busiest() is { } busiest ? [.. gpu.Adapters.Where(adapter => adapter.Id != busiest.Id)] : [],
            (adapter, rowLive) => CardKit.Detail(rowLive, () => adapter().Name, () => Format.Percent(adapter().Utilization)),
            spacing: 4);
        return UI.Stack(9,
            CardKit.Header(Icon(StatMetric.Gpu), live, () => L.T("GPU"), () => Busiest()?.Name ?? L.T("Unavailable")),
            UI.Visible(live, UI.Stack(9,
                    CardKit.Usage(live, () => L.T("Usage"), () => Format.Percent(Load()), Load, () => Level.ColorFor(Load()),
                        () => EngineCaption(Busiest())),
                    Spark(live, () => store.GpuHistory, () => Level.ColorFor(Load()), 1, 26),
                    CardKit.Divider(),
                    UI.Visible(live, CardKit.Detail(live, L.T("Dedicated memory"), () => VideoMemory(Busiest())),
                        () => Busiest() is { DedicatedUsed: not null, DedicatedTotal: > 0 }),
                    UI.Visible(live, CardKit.Detail(live, L.T("Shared memory"), () => Format.Bytes(Busiest()?.SharedUsed ?? 0)),
                        () => Busiest()?.SharedUsed is not null),
                    UI.Visible(live, CardKit.Detail(live, L.T("Temperature"), () => Format.Temperature(Busiest()?.TemperatureCelsius),
                            () => Palette.Brush(Level.TemperatureColor(Busiest()?.TemperatureCelsius ?? 0))),
                        () => Busiest()?.TemperatureCelsius is not null),
                    UI.Visible(live, CardKit.Detail(live, L.T("Fan"), () => L.F("{0} rpm", Busiest()?.FanRpm ?? 0)),
                        () => Busiest()?.FanRpm is not null),
                    others,
                    UI.Visible(live, UI.Stack(9,
                            CardKit.Divider(),
                            CardKit.Processes(live, L.T("Top processes"), () => [.. (store.Gpu?.Processes ?? []).Take(4)])),
                        () => settings.ShowProcesses && store.Gpu?.Processes.Count > 0)),
                () => Busiest() is not null));
    }

    /// <summary>The two busiest kinds of engine, as "3D 42% · Copy 3%".</summary>
    public static string EngineCaption(GpuAdapter? adapter) =>
        adapter is null || adapter.Engines.Count == 0 || adapter.Engines[0].Load <= 0
            ? L.T("Idle")
            : string.Join(" · ", adapter.Engines.Take(2).Select(engine => $"{engine.Engine} {Format.Percent(engine.Load)}"));

    public static string VideoMemory(GpuAdapter? adapter) =>
        adapter is { DedicatedUsed: { } used, DedicatedTotal: { } total }
            ? L.F("{0} of {1}", Format.Bytes(used), Format.Bytes(total))
            : "–";

    private static UIElement Memory(StatsStore store, AppSettings settings, Live live) => UI.Stack(9,
        CardKit.Header(Icon(StatMetric.Memory), live, () => L.T("Memory"),
            () => L.F("{0} · pressure {1}", Format.Bytes(store.Memory.Total), store.Memory.Pressure.Title())),
        CardKit.Usage(live,
            () => L.T("In use"),
            () => Format.Bytes(store.Memory.Used),
            () => store.Memory.Fraction,
            () => Level.ColorFor(store.Memory.Pressure),
            () => L.F("{0} used · {1} available", Format.Percent(store.Memory.Fraction), Format.Bytes(store.Memory.Available))),
        CardKit.Divider(),
        CardKit.Detail(live, L.T("Committed"),
            () => L.F("{0} of {1}", Format.Bytes(store.Memory.Committed), Format.Bytes(store.Memory.CommitLimit)),
            () => store.Memory.CommitLimit > 0 && store.Memory.Committed > store.Memory.CommitLimit * 0.9
                ? Palette.Brush(Level.Caution)
                : Palette.White(0.6)),
        CardKit.Detail(live, L.T("Cached"), () => Format.Bytes(store.Memory.Cached)),
        CardKit.Detail(live, L.T("Paged pool"), () => Format.Bytes(store.Memory.PagedPool)),
        CardKit.Detail(live, L.T("Non-paged pool"), () => Format.Bytes(store.Memory.NonPagedPool)),
        UI.Visible(live, UI.Stack(9,
                CardKit.Divider(),
                CardKit.Processes(live, L.T("Top processes"), () => Top(store.TopByMemory), process => Format.Bytes(process.Memory))),
            () => settings.ShowProcesses));

    private static UIElement Network(StatsStore store, Live live)
    {
        var tiles = new Grid { ColumnSpacing = 8 };
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var upload = RateTile(live, Glyphs.Upload, L.T("Upload"), () => Format.Rate(store.Network.UpBytesPerSecond));
        Grid.SetColumn(upload, 1);
        tiles.Children.Add(RateTile(live, Glyphs.Download, L.T("Download"), () => Format.Rate(store.Network.DownBytesPerSecond)));
        tiles.Children.Add(upload);

        return UI.Stack(9,
            CardKit.Header(Icon(StatMetric.Network), live, () => L.T("Network"),
                () => store.NetworkInterface is { } adapter ? $"{adapter.Name} · {adapter.Address}" : L.T("Offline")),
            tiles,
            Spark(live, () => store.NetworkHistory, () => Level.Accent, null, 28),
            CardKit.Divider(),
            CardKit.Detail(live, () => L.F("Peak ({0} s)", (int)store.HistoryDuration), () => Format.Rate(store.NetworkPeak)),
            CardKit.Detail(live, L.T("Received"), () => Format.Bytes(store.NetworkTotals.Received)),
            CardKit.Detail(live, L.T("Sent"), () => Format.Bytes(store.NetworkTotals.Sent)));
    }

    private static FrameworkElement RateTile(Live live, string glyph, string label, Func<string> value)
    {
        var caption = UI.Stack(Orientation.Horizontal, 4,
            Icons.Glyph(glyph, 10, Palette.White(0.6)),
            UI.Text(label, 10.5, Palette.Medium, Palette.White(0.6)));
        return new Border
        {
            Background = Palette.White(0.07),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = UI.Stack(3, caption, UI.Text(live, value, 13.5, Palette.SemiBold, () => Palette.Accent)),
        };
    }

    private static UIElement Disk(StatsStore store, Live live)
    {
        double Used() => store.Disk?.UsedFraction ?? 0;
        return UI.Stack(9,
            CardKit.Header(Icon(StatMetric.Disk), live, () => L.T("Disk"), () => store.Disk?.Name ?? L.T("Unavailable")),
            UI.Visible(live, UI.Stack(9,
                    CardKit.Usage(live,
                        () => L.T("Used"),
                        () => store.Disk is { } disk ? L.F("{0} of {1}", Format.Bytes(disk.Used), Format.Bytes(disk.Total)) : "",
                        Used,
                        () => Level.ColorFor(Used()),
                        () => store.Disk is { } disk ? L.F("{0} used · {1} free", Format.Percent(disk.UsedFraction), Format.Bytes(disk.Free)) : ""),
                    CardKit.Divider(),
                    CardKit.Detail(live, L.T("Free"), () => Format.Bytes(store.Disk?.Free ?? 0), () => Palette.Brush(Level.ColorFor(Used()))),
                    CardKit.Detail(live, L.T("Capacity"), () => Format.Bytes(store.Disk?.Total ?? 0)),
                    CardKit.Warning(live, () => Used() >= 0.9 ? L.T("Low disk space: Windows may slow down") : null)),
                () => store.Disk is not null));
    }

    private static UIElement Thermals(StatsStore store, Live live)
    {
        double Hottest() => store.Thermals?.Hottest ?? 0;
        var zones = UI.List(live, () => store.Thermals?.Zones ?? [], (zone, rowLive) => CardKit.Detail(rowLive,
            () => zone().Name,
            () => Format.Temperature(zone().Celsius),
            () => Palette.Brush(Level.TemperatureColor(zone().Celsius))), spacing: 4);
        return UI.Stack(9,
            CardKit.Header(Icon(StatMetric.Thermals), live, () => L.T("Thermals"), () => store.Thermals is { } thermals
                ? L.F("{0} thermal zones", thermals.Zones.Count)
                : L.T("Sensors unavailable")),
            UI.Visible(live, UI.Stack(9,
                    CardKit.Usage(live,
                        () => L.T("Hottest"),
                        () => Format.Temperature(store.Thermals?.Hottest),
                        () => Hottest() / 110,
                        () => Level.TemperatureColor(Hottest()),
                        () => L.F("Average {0}", Format.Temperature(store.Thermals?.Average))),
                    CardKit.Divider(),
                    zones,
                    UI.Text(L.T("Windows does not report fan speeds."), 10.5, color: Palette.White(0.5))),
                () => store.Thermals is not null));
    }

    private static UIElement Battery(StatsStore store, Live live)
    {
        var icon = new FontIcon { FontSize = 17, Foreground = Palette.White() };
        live.Add(() => icon.Glyph = store.Battery is { } battery ? Glyphs.Battery(battery.Percent, battery.IsCharging) : Glyphs.Plug);
        return UI.Stack(9,
            CardKit.Header(icon, live,
                () => store.Battery is null ? L.T("Power") : L.T("Battery"),
                () => store.Battery is { } battery ? PowerSource(battery) : L.T("No battery")),
            UI.Visible(live, UI.Stack(9,
                    CardKit.Usage(live,
                        () => L.T("Charge"),
                        () => $"{store.Battery?.Percent ?? 0}%",
                        () => (store.Battery?.Percent ?? 0) / 100.0,
                        () => store.Battery is { } battery ? Level.BatteryColor(battery) : Level.Quiet,
                        () => store.Battery is { } battery ? TimeCaption(battery) : ""),
                    CardKit.Divider(),
                    UI.Visible(live, CardKit.Detail(live, L.T("Health"),
                        () => $"{store.Battery?.HealthPercent}%",
                        () => store.Battery?.HealthPercent < 80 ? Palette.Brush(Level.Caution) : Palette.White(0.6)),
                        () => store.Battery?.HealthPercent is not null),
                    UI.Visible(live, CardKit.Detail(live, L.T("Cycle count"), () => $"{store.Battery?.CycleCount}"),
                        () => store.Battery?.CycleCount is not null),
                    UI.Visible(live, CardKit.Detail(live,
                        () => store.Battery?.RateWatts > 0 ? L.T("Charging at") : L.T("Drawing"),
                        () => Watts(store.Battery?.RateWatts)),
                        () => store.Battery?.RateWatts is { } rate && rate != 0),
                    UI.Visible(live, CardKit.Detail(live, L.T("Temperature"),
                        () => (store.Battery?.TemperatureCelsius ?? 0).ToString("0.0", CultureInfo.CurrentCulture) + " °C"),
                        () => store.Battery?.TemperatureCelsius is not null),
                    UI.Visible(live, CardKit.Detail(live, L.T("Energy saver"), () => L.T("On"), () => Palette.Brush(Level.Caution)),
                        () => store.Battery?.IsSaverOn == true)),
                () => store.Battery is not null));
    }

    public static string Watts(double? watts) =>
        watts is { } value ? Math.Abs(value).ToString("0.0", CultureInfo.CurrentCulture) + " W" : "–";

    public static string PowerSource(BatteryState battery) =>
        battery.IsOnAC ? L.T("Power adapter") : L.T("On battery");

    public static string TimeCaption(BatteryState battery)
    {
        if (battery.IsCharging)
        {
            return battery.MinutesRemaining is { } minutes ? L.F("Charging · full in {0}", Format.Duration(minutes)) : L.T("Charging");
        }
        if (battery.IsOnAC) return L.T("Not charging");
        return battery.MinutesRemaining is { } left ? L.F("{0} remaining", Format.Duration(left)) : L.T("Estimating…");
    }

    private static UIElement Claude(ClaudeUsageStore claude, Live live)
    {
        var windows = UI.List(live, () => claude.Usage?.Windows ?? [], (window, rowLive) => CardKit.Usage(rowLive,
            () => window().Label,
            () => Format.Percent(window().UsedFraction),
            () => window().UsedFraction,
            () => Level.ColorFor(window().UsedFraction),
            () => window().ResetsAt is { } resets
                ? L.F("Resets in {0}", Format.TimeUntil(resets, DateTimeOffset.Now))
                : L.T("No reset time given")), spacing: 9);
        var today = UI.Visible(live, UI.Stack(9,
                CardKit.Divider(),
                CardKit.SectionTitle(L.T("Today")),
                CardKit.Detail(live, L.T("Tokens"), () => Format.Tokens(claude.Today?.Tokens.Total ?? 0)),
                UI.Visible(live, CardKit.Detail(live, L.T("At API prices"), () => Format.Cost(claude.Today?.Cost ?? 0)),
                    () => claude.Today?.Cost is not null),
                UI.Visible(live, CardKit.NameDetail(live, L.T("Busiest project"), () => claude.Today?.Projects.FirstOrDefault()?.Name ?? ""),
                    () => claude.Today?.Projects.Count > 0)),
            () => claude.Today is { Requests: > 0 });
        return UI.Stack(9,
            CardKit.Header(Icon(StatMetric.Claude), live, () => "Claude Code", () => ClaudeSubtitle(claude.Usage)),
            UI.Visible(live, UI.Text(L.T("Reading Claude Code's limits…"), 12, color: Palette.White(0.6)), () => claude.Usage is null),
            windows,
            today,
            UI.Visible(live, CardKit.Divider(), () => claude.Usage?.Message is not null),
            CardKit.Warning(live, () => claude.Usage?.Message));
    }

    public static string ClaudeSubtitle(ClaudeUsage? usage)
    {
        if (usage?.UpdatedAt is not { } updated) return L.T("Session and weekly limits");
        var ago = Format.UpdatedAgo(updated, DateTimeOffset.Now);
        return usage.Plan is { } plan ? $"{plan} · {ago}" : ago;
    }
}
