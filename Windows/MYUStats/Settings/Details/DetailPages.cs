using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MYUStats.Edge;
using MYUStats.Sampling;
using MYUStats.Views;

namespace MYUStats.Settings;

/// <summary>A metric's page: what the hover card shows, plus what does not fit in it.</summary>
internal sealed class DetailPage(UIElement root, Live live, Action? leave = null) : ISettingsPage
{
    public UIElement Root => root;

    public void Refresh() => live.Refresh();

    public void Leave() => leave?.Invoke();
}

internal static class DetailPages
{
    private const string ProcessClient = "details";

    public static ISettingsPage Create(StatMetric metric, SettingsContext context) => metric switch
    {
        StatMetric.Cpu => Cpu(context.Store, context.Settings),
        StatMetric.Gpu => Gpu(context.Store, context.Settings),
        StatMetric.Memory => Memory(context.Store, context.Settings),
        StatMetric.Network => Network(context.Store),
        StatMetric.Disk => DiskPage.Create(context.Store, context.Storage),
        StatMetric.Thermals => Thermals(context.Store),
        StatMetric.Battery => Battery(context.Store),
        _ => Claude(context.Store.Claude),
    };

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static SettingsSection Section(StatMetric metric) => SettingsSection.Detail(metric);

    /// <summary>Busiest processes, updated in place, each with its process ID.</summary>
    private static UIElement Processes(Live live, AppSettings settings, string title, string footer,
        Func<IReadOnlyList<ProcessUsage>> processes, Func<ProcessUsage, string> value)
    {
        var measuring = UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("Measuring…"), 14, color: SettingsKit.Secondary)),
            () => processes().Count == 0);
        var rows = UI.List(live, processes, (process, rowLive) =>
        {
            var name = UI.Text(rowLive, () => process().Name, 14);
            var pid = UI.Text(rowLive, () => process().Pid.ToString(CultureInfo.InvariantCulture), 12, color: () => SettingsKit.Tertiary);
            pid.VerticalAlignment = VerticalAlignment.Center;
            return SettingsKit.Row(UI.Stack(Orientation.Horizontal, 8, name, pid),
                UI.Text(rowLive, () => value(process()), 14, color: () => SettingsKit.Secondary));
        }, spacing: 0);
        return UI.Visible(live, SettingsKit.Section(title, footer, measuring, rows), () => settings.ShowProcesses);
    }

    private static ISettingsPage Cpu(StatsStore store, AppSettings settings)
    {
        var live = new Live();
        live.Add(() => store.SetProcessSampling(settings.ShowProcesses, ProcessClient));
        var cores = UI.List<(int Index, double Load)>(live,
            () => [.. store.PerCore.Select((load, index) => (Index: index, Load: load))],
            (core, rowLive) => SettingsKit.Meter(rowLive,
                () => L.F("CPU {0}", core().Index),
                () => Format.Percent(core().Load),
                () => core().Load,
                () => Level.ColorFor(core().Load)),
            spacing: 0);
        var root = SettingsKit.Page(Section(StatMetric.Cpu).Title, Section(StatMetric.Cpu).Subtitle,
            SettingsKit.Section(L.T("Usage"), null,
                SettingsKit.Meter(live, () => L.T("Total"), () => Format.Percent(store.Cpu.Total), () => store.Cpu.Total,
                    () => Level.ColorFor(store.Cpu.Total)),
                SettingsKit.Value(live, L.T("User"), () => Format.Percent(store.Cpu.User)),
                SettingsKit.Value(live, L.T("System"), () => Format.Percent(store.Cpu.System)),
                SettingsKit.Value(live, L.T("Idle"), () => Format.Percent(store.Cpu.Idle)),
                SettingsKit.History(live, () => store.CpuHistory, () => Level.ColorFor(store.Cpu.Total), 1, () => store.HistoryDuration)),
            SettingsKit.Section(L.T("Processors"), L.T("Each logical processor, numbered as Task Manager numbers them."), cores),
            SettingsKit.Section(L.T("System"), null,
                SettingsKit.Value(live, L.T("Processor"), () => store.CpuInfo.DisplayBrand),
                SettingsKit.Value(live, L.T("Cores"), () => L.F("{0} · {1} threads", store.CpuInfo.CoresDescription, store.CpuInfo.LogicalCores)),
                SettingsKit.Value(live, L.T("Processes"), () => Count(store.Counts.Processes)),
                SettingsKit.Value(live, L.T("Threads"), () => Count(store.Counts.Threads)),
                SettingsKit.Value(live, L.T("Handles"), () => Count(store.Counts.Handles)),
                SettingsKit.Value(live, L.T("Uptime"), () => Format.Uptime(Environment.TickCount64 / 1000.0))),
            Processes(live, settings, L.T("Top processes by CPU"),
                L.T("Every process on this PC, as Task Manager counts them: CPU is a share of all processors."),
                () => store.TopByCpu, process => Format.Percent(process.Cpu)));
        return new DetailPage(root, live, () => store.SetProcessSampling(false, ProcessClient));
    }

    private static ISettingsPage Gpu(StatsStore store, AppSettings settings)
    {
        var live = new Live();
        double Load() => store.Gpu?.Headline?.Utilization ?? 0;
        var adapters = UI.List(live, () => store.Gpu?.Adapters ?? [], (adapter, rowLive) => Adapter(rowLive, adapter), spacing: 18);
        var none = UI.Visible(live, SettingsKit.Section((string?)null, null,
            SettingsKit.Padded(UI.Text(L.T("No GPU counters could be read on this PC."), 14, color: SettingsKit.Secondary))),
            () => store.Gpu is null);
        var processes = UI.List(live, () => store.Gpu?.Processes ?? [], (process, rowLive) =>
        {
            var name = UI.Text(rowLive, () => process().Name, 14);
            var pid = UI.Text(rowLive, () => process().Pid.ToString(CultureInfo.InvariantCulture), 12, color: () => SettingsKit.Tertiary);
            pid.VerticalAlignment = VerticalAlignment.Center;
            return SettingsKit.Row(UI.Stack(Orientation.Horizontal, 8, name, pid),
                UI.Text(rowLive, () => Format.Percent(process().Load), 14, color: () => SettingsKit.Secondary));
        }, spacing: 0);
        var idle = UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("No process is using the GPU."), 14, color: SettingsKit.Secondary)),
            () => store.Gpu is { Processes.Count: 0 });
        var root = SettingsKit.Page(Section(StatMetric.Gpu).Title, Section(StatMetric.Gpu).Subtitle,
            none,
            UI.Visible(live, SettingsKit.Section(L.T("Usage"),
                    L.T("Load is the busiest engine's, as Task Manager's GPU graphs show it."),
                    SettingsKit.Meter(live, () => store.Gpu?.Headline?.Name ?? "", () => Format.Percent(Load()), Load, () => Level.ColorFor(Load())),
                    SettingsKit.History(live, () => store.GpuHistory, () => Level.ColorFor(Load()), 1, () => store.HistoryDuration)),
                () => store.Gpu is not null),
            adapters,
            UI.Visible(live, SettingsKit.Section(L.T("Top processes by GPU"),
                    L.T("Each process's share of the GPU engine it uses most, the figure Task Manager's GPU column shows."),
                    idle, processes),
                () => settings.ShowProcesses && store.Gpu is not null));
        return new DetailPage(root, live);
    }

    /// <summary>One GPU: its engines, memory, and the temperature and fan speed where the driver reports them.</summary>
    private static UIElement Adapter(Live live, Func<GpuAdapter> adapter)
    {
        var engines = UI.List(live, () => adapter().Engines, (engine, rowLive) => SettingsKit.Meter(rowLive,
            () => engine().Engine, () => Format.Percent(engine().Load), () => engine().Load, () => Level.ColorFor(engine().Load)), spacing: 0);
        var header = UI.Text(live, () => adapter().Name, 14, Palette.SemiBold);
        return SettingsKit.Section(header,
            L.T("Temperature and fan speed come from the driver; many integrated GPUs report neither."),
            engines,
            UI.Visible(live, SettingsKit.Meter(live, () => L.T("Dedicated memory"), () => DetailCards.VideoMemory(adapter()),
                    () => adapter() is { DedicatedUsed: { } used, DedicatedTotal: > 0 and var total } ? (double)used / total : 0,
                    () => Level.Accent),
                () => adapter() is { DedicatedUsed: not null, DedicatedTotal: > 0 }),
            UI.Visible(live, SettingsKit.Value(live, L.T("Shared memory"), () => Format.Bytes(adapter().SharedUsed ?? 0)),
                () => adapter().SharedUsed is not null),
            UI.Visible(live, SettingsKit.Value(live, L.T("Temperature"), () => Format.Temperature(adapter().TemperatureCelsius),
                    () => Palette.Brush(Level.TemperatureColor(adapter().TemperatureCelsius ?? 0))),
                () => adapter().TemperatureCelsius is not null),
            UI.Visible(live, SettingsKit.Value(live, L.T("Fan"), () => L.F("{0} rpm", adapter().FanRpm ?? 0)),
                () => adapter().FanRpm is not null));
    }

    private static ISettingsPage Memory(StatsStore store, AppSettings settings)
    {
        var live = new Live();
        live.Add(() => store.SetProcessSampling(settings.ShowProcesses, ProcessClient));
        var root = SettingsKit.Page(Section(StatMetric.Memory).Title, Section(StatMetric.Memory).Subtitle,
            SettingsKit.Section(L.T("Usage"), null,
                SettingsKit.Meter(live, () => L.T("In use"),
                    () => L.F("{0} of {1}", Format.Bytes(store.Memory.Used), Format.Bytes(store.Memory.Total)),
                    () => store.Memory.Fraction, () => Level.ColorFor(store.Memory.Pressure)),
                SettingsKit.Value(live, L.T("Available"), () => Format.Bytes(store.Memory.Available)),
                SettingsKit.Value(live, L.T("Pressure"), () => store.Memory.Pressure.Title(),
                    () => Palette.Brush(Level.ColorFor(store.Memory.Pressure))),
                SettingsKit.History(live, () => store.MemoryHistory, () => Level.ColorFor(store.Memory.Pressure), 1, () => store.HistoryDuration)),
            SettingsKit.Section(L.T("Breakdown"), L.T("Committed memory is what apps have asked for, backed by RAM and the page file together."),
                SettingsKit.Value(live, L.T("Committed"),
                    () => L.F("{0} of {1}", Format.Bytes(store.Memory.Committed), Format.Bytes(store.Memory.CommitLimit))),
                SettingsKit.Value(live, L.T("Cached"), () => Format.Bytes(store.Memory.Cached)),
                SettingsKit.Value(live, L.T("Paged pool"), () => Format.Bytes(store.Memory.PagedPool)),
                SettingsKit.Value(live, L.T("Non-paged pool"), () => Format.Bytes(store.Memory.NonPagedPool))),
            Processes(live, settings, L.T("Top processes by memory"),
                L.T("Each process's private working set, the figure Task Manager's Memory column shows."),
                () => store.TopByMemory, process => Format.Bytes(process.Memory)));
        return new DetailPage(root, live, () => store.SetProcessSampling(false, ProcessClient));
    }

    private static ISettingsPage Network(StatsStore store)
    {
        var live = new Live();
        var root = SettingsKit.Page(Section(StatMetric.Network).Title, Section(StatMetric.Network).Subtitle,
            SettingsKit.Section(L.T("Now"), null,
                SettingsKit.Value(live, L.T("Download"), () => Format.Rate(store.Network.DownBytesPerSecond)),
                SettingsKit.Value(live, L.T("Upload"), () => Format.Rate(store.Network.UpBytesPerSecond)),
                SettingsKit.Value(live, L.T("Peak"), () => Format.Rate(store.NetworkPeak)),
                SettingsKit.History(live, () => store.NetworkHistory, () => Level.Accent, null, () => store.HistoryDuration)),
            SettingsKit.Section(L.T("Connection"), null,
                SettingsKit.Value(live, L.T("Adapter"), () => store.NetworkInterface?.Name ?? L.T("Offline")),
                SettingsKit.Value(live, L.T("IPv4 address"), () => store.NetworkInterface?.Address ?? "–")),
            SettingsKit.Section(L.T("Totals"),
                L.T("Counted since each adapter last started. Hardware adapters only: VPN and virtual adapters are left out, so traffic is not counted twice."),
                SettingsKit.Value(live, L.T("Received"), () => Format.Bytes(store.NetworkTotals.Received)),
                SettingsKit.Value(live, L.T("Sent"), () => Format.Bytes(store.NetworkTotals.Sent))));
        return new DetailPage(root, live);
    }

    private static ISettingsPage Thermals(StatsStore store)
    {
        var live = new Live();
        var zones = UI.List(live, () => store.Thermals?.Zones ?? [], (zone, rowLive) => SettingsKit.Value(rowLive,
            () => zone().Name,
            () => Format.Temperature(zone().Celsius),
            () => Palette.Brush(Level.TemperatureColor(zone().Celsius))), spacing: 0);
        var none = UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("No temperature sensors could be read on this PC."), 14,
            color: SettingsKit.Secondary)), () => store.Thermals is null);
        var root = SettingsKit.Page(Section(StatMetric.Thermals).Title, Section(StatMetric.Thermals).Subtitle,
            SettingsKit.Section(L.T("Temperatures"),
                L.T("ACPI thermal zones, as Windows reports them. Many desktop boards report none, and some report a fixed value. Windows has no interface for fan speeds, so fans are not shown."),
                none, zones));
        return new DetailPage(root, live);
    }

    private static ISettingsPage Battery(StatsStore store)
    {
        var live = new Live();
        BatteryState? Battery() => store.Battery;
        var none = UI.Visible(live, SettingsKit.Section((string?)null, null,
            SettingsKit.Padded(UI.Text(L.T("This PC has no battery."), 14, color: SettingsKit.Secondary))), () => Battery() is null);
        var charge = SettingsKit.Section(L.T("Charge"), null,
            SettingsKit.Meter(live, () => L.T("Level"), () => $"{Battery()?.Percent ?? 0}%", () => (Battery()?.Percent ?? 0) / 100.0,
                () => Battery() is { } battery ? Level.BatteryColor(battery) : Level.Quiet),
            SettingsKit.Value(live, L.T("Status"), () => Battery() is { } battery ? Status(battery) : "–"),
            UI.Visible(live, SettingsKit.Value(live,
                    () => Battery()?.IsCharging == true ? L.T("Full in") : L.T("Remaining"),
                    () => Battery()?.MinutesRemaining is { } minutes ? Format.Duration(minutes) : "–"),
                () => Battery()?.MinutesRemaining is not null),
            SettingsKit.Value(live, L.T("Power source"), () => Battery() is { IsOnAC: true } ? L.T("Power adapter") : L.T("Battery")),
            UI.Visible(live, SettingsKit.Value(live,
                    () => Battery()?.RateWatts > 0 ? L.T("Charging at") : L.T("Drawing"),
                    () => DetailCards.Watts(Battery()?.RateWatts)),
                () => Battery()?.RateWatts is { } rate && rate != 0),
            SettingsKit.Value(live, L.T("Energy saver"), () => Battery()?.IsSaverOn == true ? L.T("On") : L.T("Off")));
        var health = SettingsKit.Section(L.T("Health"), null,
            UI.Visible(live, SettingsKit.Meter(live, () => L.T("Maximum capacity"), () => $"{Battery()?.HealthPercent}%",
                    () => (Battery()?.HealthPercent ?? 0) / 100.0,
                    () => Battery()?.HealthPercent < 80 ? Level.Caution : Level.Good),
                () => Battery()?.HealthPercent is not null),
            UI.Visible(live, SettingsKit.Value(live, L.T("Cycle count"), () => $"{Battery()?.CycleCount}"),
                () => Battery()?.CycleCount is not null),
            SettingsKit.Value(live, L.T("Temperature"), () => Battery()?.TemperatureCelsius is { } celsius
                ? celsius.ToString("0.0", CultureInfo.CurrentCulture) + " °C"
                : "–"));
        var root = SettingsKit.Page(Section(StatMetric.Battery).Title, Section(StatMetric.Battery).Subtitle,
            none,
            UI.Visible(live, charge, () => Battery() is not null),
            UI.Visible(live, health, () => Battery() is not null));
        return new DetailPage(root, live);
    }

    private static string Status(BatteryState battery)
    {
        if (battery.IsCharging) return L.T("Charging");
        return battery.IsOnAC ? L.T("Not charging") : L.T("On battery");
    }

    private static ISettingsPage Claude(ClaudeUsageStore claude)
    {
        var live = new Live();
        var windows = UI.List(live, () => claude.Usage?.Windows ?? [], (window, rowLive) => UI.Stack(0,
            SettingsKit.Meter(rowLive, () => window().Label, () => Format.Percent(window().UsedFraction),
                () => window().UsedFraction, () => Level.ColorFor(window().UsedFraction)),
            UI.Visible(rowLive, SettingsKit.Value(rowLive, L.T("Resets"), () => window().ResetsAt is { } resets ? ResetText(resets) : ""),
                () => window().ResetsAt is not null)), spacing: 0);
        var reading = UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("Reading Claude Code's limits…"), 14, color: SettingsKit.Secondary)),
            () => claude.Usage is null);

        var progress = new ProgressRing { Width = 18, Height = 18, IsActive = false };
        var refresh = SettingsKit.Button(L.T("Refresh"), () => _ = claude.RefreshAsync(force: true));
        live.Add(() =>
        {
            progress.IsActive = claude.IsRefreshing;
            progress.Visibility = claude.IsRefreshing ? Visibility.Visible : Visibility.Collapsed;
            refresh.IsEnabled = !claude.IsRefreshing;
        });

        ClaudeDayUsage? Today() => claude.Today;
        bool Used() => Today() is { Requests: > 0 };
        UIElement Count(string label, Func<TokenCounts, long> count) =>
            UI.Visible(live, SettingsKit.Value(live, label, () => Format.Tokens(Today() is { } today ? count(today.Tokens) : 0)), Used);
        var today = UI.Visible(live, SettingsKit.Section(L.T("Today"),
                L.T("Counted from Claude Code's session logs on this PC since midnight, subagents included. The price is what the same tokens would cost on the API at list prices, not what a subscription pays."),
                UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("Claude Code has not been used today."), 14, color: SettingsKit.Secondary)),
                    () => Today() is { Requests: 0 }),
                Count(L.T("Tokens"), tokens => tokens.Total),
                Count(L.T("Input"), tokens => tokens.Input),
                Count(L.T("Output"), tokens => tokens.Output),
                Count(L.T("Cache writes"), tokens => tokens.CacheWrite),
                Count(L.T("Cache reads"), tokens => tokens.CacheRead),
                UI.Visible(live, SettingsKit.Value(live, L.T("Requests"), () => (Today()?.Requests ?? 0).ToString("N0", CultureInfo.CurrentCulture)), Used),
                UI.Visible(live, SettingsKit.Value(live, L.T("At API prices"), () => Today()?.Cost is { } cost ? Format.Cost(cost) : "–"), Used)),
            () => Today() is not null);
        var projects = UI.List<ClaudeShare>(live, () => [.. (Today()?.Projects ?? []).Take(8)], Share, spacing: 0);
        var models = UI.List<ClaudeShare>(live, () => Today()?.Models ?? [], Share, spacing: 0);

        var root = SettingsKit.Page("Claude Code", Section(StatMetric.Claude).Subtitle,
            SettingsKit.Section(L.T("Limits"), L.T("The ring shows whichever limit is closest to running out."),
                reading, windows, SettingsKit.Warning(live, () => claude.Usage?.Message)),
            today,
            UI.Visible(live, SettingsKit.Section(L.T("Projects today"), null, projects), () => Today()?.Projects.Count > 0),
            UI.Visible(live, SettingsKit.Section(L.T("Models today"), null, models), () => Today()?.Models.Count > 0),
            SettingsKit.Section(L.T("Source"),
                L.T("Limits are asked of the installed claude command with /usage, the same figures Claude Code shows, and no prompt is sent to a model. Today's counts come from its session logs in .claude\\projects in your user folder: only the token counts are taken from them, what was said is not kept, and nothing leaves this PC. MYU STATS never reads Claude Code's sign-in. Hide the ring in Metrics, and turn its alert off, to stop the reads."),
                UI.Visible(live, SettingsKit.Value(live, L.T("Plan"), () => claude.Usage?.Plan ?? ""), () => claude.Usage?.Plan is not null),
                SettingsKit.Value(live, L.T("Last read"), () => claude.Usage?.UpdatedAt is { } updated
                    ? updated.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
                    : "–"),
                SettingsKit.Row(L.T("Read every 5 minutes while the ring is shown or its alert is on."), UI.Stack(Orientation.Horizontal, 10, progress, refresh))));
        claude.RefreshIfStale();
        return new DetailPage(root, live);
    }

    /// <summary>A project or model: its name with its tokens under it, and what they would cost.</summary>
    private static UIElement Share(Func<ClaudeShare> share, Live live)
    {
        var name = UI.Stack(2,
            UI.Text(live, () => share().Name, 14),
            UI.Text(live, () => L.F("{0} tokens", Format.Tokens(share().Tokens)), 12, color: () => SettingsKit.Secondary));
        return SettingsKit.Row(name, UI.Text(live, () => share().Cost is { } cost ? Format.Cost(cost) : "–", 14, color: () => SettingsKit.Secondary));
    }

    /// <summary>"Fri 06:00, in 2d 5h".</summary>
    private static string ResetText(DateTimeOffset resets)
    {
        var when = resets.ToLocalTime().ToString("ddd HH:mm", CultureInfo.CurrentCulture);
        return L.F("{0}, in {1}", when, Format.TimeUntil(resets, DateTimeOffset.Now));
    }
}
