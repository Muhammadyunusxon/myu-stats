using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MYUStats.Interop;
using MYUStats.Views;

namespace MYUStats.Settings;

/// <summary>Which rings the pill shows, in what order, and how often they update.</summary>
internal sealed class MetricsPage : ISettingsPage
{
    private readonly StatsStore store;
    private readonly AppSettings settings;
    private readonly StackPanel rings = new();
    private string availability = "";

    public MetricsPage(StatsStore store, AppSettings settings)
    {
        this.store = store;
        this.settings = settings;
        BuildRings();
        var intervals = AppSettings.SampleIntervals;
        var interval = SettingsKit.Choice(
            [.. intervals.Select(seconds => L.F("{0} s", seconds))],
            Math.Max(0, intervals.ToList().IndexOf(settings.SampleInterval)),
            index => settings.SampleInterval = intervals[index]);
        Root = SettingsKit.Page(SettingsSection.Metrics.Title, SettingsSection.Metrics.Subtitle,
            SettingsKit.Section(L.T("Rings"), null, rings),
            SettingsKit.Section(L.T("Sampling"),
                L.F("Slower updates use less energy. Charts keep the last {0} samples.", StatsStore.HistoryLength),
                SettingsKit.Row(L.T("Update every"), interval)));
    }

    public UIElement Root { get; }

    /// <summary>The battery and thermal sensors, and Claude Code, can come and go while the page is open.</summary>
    public void Refresh()
    {
        if (Availability() != availability) BuildRings();
    }

    public void Leave()
    {
    }

    private string Availability() => string.Concat(StatMetrics.All.Select(metric => store.IsAvailable(metric) ? '1' : '0'));

    private void BuildRings()
    {
        availability = Availability();
        rings.Children.Clear();
        var order = settings.Order;
        for (var index = 0; index < order.Count; index++)
        {
            if (index > 0) rings.Children.Add(new Border { Height = 1, Background = Palette.Resource("DividerStrokeColorDefaultBrush", Palette.White(0.08)) });
            rings.Children.Add(Row(order[index], index, order.Count));
        }
    }

    private Grid Row(StatMetric metric, int index, int count)
    {
        var available = store.IsAvailable(metric);
        var icon = Icons.Metric(metric, 16, Palette.White(available ? 0.85 : 0.35), store.Battery);
        icon.Width = 20;
        var title = UI.Stack(1,
            UI.Text(metric.Title(), 14),
            available ? null : SettingsKit.Caption(L.T("Not available on this PC")));
        title.VerticalAlignment = VerticalAlignment.Center;
        var up = SettingsKit.IconButton(Glyphs.ChevronUp, L.T("Move up"), () => Move(index, index - 1));
        up.IsEnabled = index > 0;
        var down = SettingsKit.IconButton(Glyphs.ChevronDown, L.T("Move down"), () => Move(index, index + 1));
        down.IsEnabled = index < count - 1;
        var toggle = SettingsKit.Toggle(!settings.Hidden.Contains(metric), shown => SetShown(metric, shown));
        ToolTipService.SetToolTip(toggle, metric.Title());

        var grid = new Grid { Padding = new Thickness(16, 8, 12, 8), ColumnSpacing = 12, MinHeight = 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var controls = UI.Stack(Orientation.Horizontal, 2, up, down, toggle);
        controls.VerticalAlignment = VerticalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        Grid.SetColumn(controls, 2);
        grid.Children.Add(icon);
        grid.Children.Add(title);
        grid.Children.Add(controls);
        return grid;
    }

    private void SetShown(StatMetric metric, bool shown)
    {
        var hidden = new HashSet<StatMetric>(settings.Hidden);
        if (shown) hidden.Remove(metric);
        // At least one ring stays, or the pill would have nothing to show.
        else if (hidden.Count < StatMetrics.All.Count - 1) hidden.Add(metric);
        settings.Hidden = hidden;
        // The last ring's switch flipped but the ring stays: put the switch back, once its event is over.
        if (!shown && !hidden.Contains(metric)) rings.DispatcherQueue.TryEnqueue(BuildRings);
        // A hidden Claude ring is not read, so one that comes back is read straight away.
        if (metric == StatMetric.Claude && shown) _ = store.Claude.RefreshAsync();
    }

    private void Move(int from, int to)
    {
        var order = settings.Order.ToList();
        (order[from], order[to]) = (order[to], order[from]);
        settings.Order = order;
        // Rebuilt after the click is handled, not under it.
        rings.DispatcherQueue.TryEnqueue(BuildRings);
    }
}

/// <summary>How the pill looks and where it sits.</summary>
internal sealed class AppearancePage : ISettingsPage
{
    private readonly Live live = new();

    public AppearancePage(AppSettings settings)
    {
        var edges = ScreenEdges.All;
        var edge = SettingsKit.Choice([.. edges.Select(item => item.Title())], edges.ToList().IndexOf(settings.Edge), index =>
        {
            if (edges[index] == settings.Edge) return;
            // An offset along one edge means nothing on another; start the new edge centred.
            settings.Offset = 0;
            settings.Edge = edges[index];
        });
        // The pill can also be dragged to another edge while this page is open.
        live.Add(() => edge.SelectedIndex = edges.ToList().IndexOf(settings.Edge));

        var displays = Displays.All();
        UIElement? display = null;
        // Only worth showing with a second display, or to switch back once it is gone.
        if (displays.Count > 0 && (displays.Count > 1 || settings.Display.Length > 0))
        {
            var ids = displays.Select(item => item.Id).ToList();
            var names = new List<string> { L.T("Main display") };
            names.AddRange(displays.Skip(1).Select(item => item.Name));
            ids[0] = "";
            if (settings.Display.Length > 0 && !ids.Contains(settings.Display))
            {
                names.Add(L.T("Disconnected display"));
                ids.Add(settings.Display);
            }
            var selected = Math.Max(0, ids.IndexOf(settings.Display));
            display = SettingsKit.Row(L.T("Display"), SettingsKit.Choice(names, selected, index =>
            {
                if (ids[index] == settings.Display) return;
                settings.Offset = 0;
                settings.Display = ids[index];
            }), L.T("The main display is the one marked \"Make this my main display\" in Windows Settings, under System › Display."));
        }

        var recentre = SettingsKit.Button(L.T("Re-centre"), () => settings.Offset = 0);
        live.Add(() => recentre.IsEnabled = settings.Offset != 0);

        var styles = SurfaceStyles.All;
        var surface = SettingsKit.Choice([.. styles.Select(style => style.Title())], styles.ToList().IndexOf(settings.Surface),
            index => settings.Surface = styles[index]);
        var explanation = SettingsKit.Caption("");
        live.Add(() => explanation.Text = settings.Surface.Explanation());
        var darkness = SettingsKit.Slider(0, AppSettings.MaxDarkness, 0.01, settings.Darkness,
            value => Format.Percent(value / AppSettings.MaxDarkness), value => settings.Darkness = value, out var darknessSlider);
        live.Add(() => darknessSlider.IsEnabled = settings.Surface != SurfaceStyle.Solid);

        var autoHide = SettingsKit.Toggle(settings.AutoHide, value => settings.AutoHide = value);
        var hideAfter = SettingsKit.Slider(AppSettings.MinHideDelay, AppSettings.MaxHideDelay, 0.05, settings.HideDelay,
            value => value.ToString("0.00", CultureInfo.CurrentCulture) + " s", value => settings.HideDelay = value, out var delaySlider);
        live.Add(() => delaySlider.IsEnabled = settings.AutoHide);

        Root = SettingsKit.Page(SettingsSection.Appearance.Title, SettingsSection.Appearance.Subtitle,
            SettingsKit.Section(L.T("Pill"), null,
                SettingsKit.Row(L.T("Edge"), edge, L.T("Pick an edge no other app is using.")),
                display,
                SettingsKit.Row(L.T("Position along the edge"), recentre,
                    L.T("Drag the handle at the start of the pill to slide it along the edge, or onto another edge.")),
                SettingsKit.Row(UI.Text(L.T("Surface"), 14), surface),
                SettingsKit.Padded(explanation),
                SettingsKit.Row(L.T("Darkness"), darkness)),
            SettingsKit.Section(L.T("Behaviour"), null,
                SettingsKit.Row(L.T("Auto-hide"), autoHide,
                    L.T("When hidden, only a thin tab stays on the edge. Touch the edge next to it to open the pill.")),
                SettingsKit.Row(L.T("Hide after"), hideAfter)));
    }

    public UIElement Root { get; }

    public void Refresh() => live.Refresh();

    public void Leave()
    {
    }
}

/// <summary>Which alerts come as Windows notifications, and at what limits.</summary>
internal sealed class AlertsPage : ISettingsPage
{
    private readonly Live live = new();

    public AlertsPage(SettingsContext context)
    {
        var settings = context.Settings;
        var store = context.Store;
        var notifier = context.Notifier;
        bool AnyOn() => AlertSettings.From(settings).IsAnyOn;
        // With an alert already on, Windows' answer about notifications can be shown straight away.
        if (AnyOn()) notifier.Prepare();

        void Changed()
        {
            if (AnyOn()) notifier.Prepare();
            live.Refresh();
        }

        UIElement Toggle(string label, Func<bool> get, Action<bool> set) =>
            SettingsKit.Row(label, SettingsKit.Toggle(get(), value =>
            {
                set(value);
                Changed();
            }));

        UIElement Limit(string label, IReadOnlyList<double> choices, Func<double, string> title, Func<double> get, Action<double> set, Func<bool> isOn)
        {
            var nearest = choices.Select((choice, index) => (Distance: Math.Abs(choice - get()), Index: index)).Min().Index;
            var combo = SettingsKit.Choice([.. choices.Select(title)], nearest, index => set(choices[index]));
            live.Add(() => combo.IsEnabled = isOn());
            return SettingsKit.Row(label, combo);
        }

        var test = SettingsKit.Button(L.T("Send a test"), () =>
        {
            context.Alerts.SendTest();
            live.Refresh();
        });
        var open = SettingsKit.Button(L.T("Open notification settings"), () => Shell.OpenSettings("ms-settings:notifications"));
        var openRow = UI.Visible(live, SettingsKit.Row(L.T("Turn notifications back on for MYU STATS"), open), () => notifier.Problem is not null);

        Root = SettingsKit.Page(SettingsSection.Alerts.Title, SettingsSection.Alerts.Subtitle,
            SettingsKit.Section(L.T("Notifications"),
                L.T("A notification opens the page it is about. Each alert comes once, and again only after things have gone back to normal."),
                SettingsKit.Row(L.T("Send a test notification"), test),
                SettingsKit.Warning(live, () => notifier.Problem),
                openRow),
            SettingsKit.Section("Claude Code",
                store.Claude.IsInstalled
                    ? L.T("Once for the session and once for each weekly limit, until it resets. Read every 5 minutes.")
                    : L.T("Claude Code is not installed on this PC."),
                Toggle(L.T("When a limit gets close"), () => settings.AlertClaude, value => settings.AlertClaude = value),
                Limit(L.T("Limit used"), AppSettings.ClaudeThresholds, Format.Percent,
                    () => settings.AlertClaudeThreshold, value => settings.AlertClaudeThreshold = value, () => settings.AlertClaude)),
            SettingsKit.Section(L.T("Disk"), null,
                Toggle(L.T("When the Windows drive fills up"), () => settings.AlertDisk, value => settings.AlertDisk = value),
                Limit(L.T("Free space below"), AppSettings.DiskFreeChoices, gigabytes => L.F("{0} GB", gigabytes),
                    () => settings.AlertDiskFreeGB, value => settings.AlertDiskFreeGB = value, () => settings.AlertDisk)),
            SettingsKit.Section(L.T("Temperature"),
                store.IsAvailable(StatMetric.Thermals)
                    ? L.T("Only after a minute above the limit, so short spikes pass quietly.")
                    : L.T("No temperature sensors could be read on this PC."),
                Toggle(L.T("When the PC runs hot"), () => settings.AlertTemperature, value => settings.AlertTemperature = value),
                Limit(L.T("Above"), AppSettings.TemperatureChoices, celsius => Format.Temperature(celsius),
                    () => settings.AlertTemperatureLimit, value => settings.AlertTemperatureLimit = value, () => settings.AlertTemperature)),
            SettingsKit.Section(L.T("Processes"),
                L.T("Busy means 90% of a core or more, the way a stuck build or a runaway script behaves. Every process counts; checked every 30 seconds."),
                Toggle(L.T("When a process keeps a core busy"), () => settings.AlertProcess, value => settings.AlertProcess = value),
                Limit(L.T("For longer than"), AppSettings.ProcessMinuteChoices, minutes => L.F("{0} min", minutes),
                    () => settings.AlertProcessMinutes, value => settings.AlertProcessMinutes = value, () => settings.AlertProcess)),
            SettingsKit.Section(L.T("Memory"),
                L.T("After a minute with under 5% of memory free, or once Windows signals that memory is low."),
                Toggle(L.T("When memory runs out"), () => settings.AlertMemory, value => settings.AlertMemory = value)));
    }

    public UIElement Root { get; }

    public void Refresh() => live.Refresh();

    public void Leave()
    {
    }
}

/// <summary>Startup, language, performance and everything else.</summary>
internal sealed class GeneralPage : ISettingsPage
{
    public GeneralPage(AppSettings settings)
    {
        var loginError = SettingsKit.Caption("");
        loginError.Foreground = Palette.Brush(Level.Bad);
        loginError.Visibility = Visibility.Collapsed;
        ToggleSwitch? login = null;
        login = SettingsKit.Toggle(LaunchAtLogin.IsEnabled, enabled =>
        {
            try
            {
                LaunchAtLogin.Set(enabled);
                loginError.Visibility = Visibility.Collapsed;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                Log.Settings.Error($"Start with Windows could not be {(enabled ? "turned on" : "turned off")}: {error.Message}");
                loginError.Text = error.Message;
                loginError.Visibility = Visibility.Visible;
                if (login is not null) login.IsOn = LaunchAtLogin.IsEnabled;
            }
        });

        string[] languages = [L.System, L.English, L.Uzbek];
        var language = SettingsKit.Choice(
            [L.T("Same as Windows"), "English", "Oʻzbekcha"],
            Math.Max(0, Array.IndexOf(languages, settings.Language)),
            index => settings.Language = languages[index]);

        var processes = SettingsKit.Toggle(settings.ShowProcesses, value => settings.ShowProcesses = value);
        var logs = SettingsKit.Button(L.T("Open folder"), () => Shell.OpenFolder(Log.Folder ?? AppPaths.Logs));
        var reset = SettingsKit.Button(L.T("Reset"), settings.Reset);

        Root = SettingsKit.Page(SettingsSection.General.Title, SettingsSection.General.Subtitle,
            SettingsKit.Section((string?)null, null,
                SettingsKit.Row(L.T("Open MYU STATS when you sign in"), login),
                SettingsKit.Padded(loginError)),
            SettingsKit.Section(L.T("Language"), L.T("Changes take effect straight away."),
                SettingsKit.Row(L.T("Language"), language)),
            SettingsKit.Section(L.T("Performance"), null,
                SettingsKit.Row(L.T("Show top processes"), processes,
                    L.T("Reading processes costs a little CPU while the CPU or Memory card is open."))),
            SettingsKit.Section(L.T("Troubleshooting"), null,
                SettingsKit.Row(L.T("Log file"), logs, L.T("What MYU STATS did and what went wrong. Include it when you report a problem.")),
                SettingsKit.Row(L.T("Reset every setting to its default"), reset)));
    }

    public UIElement Root { get; }

    public void Refresh()
    {
    }

    public void Leave()
    {
    }
}
