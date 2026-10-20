using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using MYUStats.Interop;
using MYUStats.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace MYUStats.Settings;

internal enum SettingsPane
{
    Metrics,
    Appearance,
    Alerts,
    General,
    Detail,
}

/// <summary>A page of the Settings window: one of the settings panes, or a metric's detail page.</summary>
internal readonly record struct SettingsSection(SettingsPane Pane, StatMetric Metric = StatMetric.Cpu)
{
    public static SettingsSection Metrics => new(SettingsPane.Metrics);
    public static SettingsSection Appearance => new(SettingsPane.Appearance);
    public static SettingsSection Alerts => new(SettingsPane.Alerts);
    public static SettingsSection General => new(SettingsPane.General);

    public static SettingsSection Detail(StatMetric metric) => new(SettingsPane.Detail, metric);

    public string Title => Pane switch
    {
        SettingsPane.Metrics => L.T("Metrics"),
        SettingsPane.Appearance => L.T("Appearance"),
        SettingsPane.Alerts => L.T("Alerts"),
        SettingsPane.General => L.T("General"),
        _ => Metric.Title(),
    };

    public string Subtitle => Pane switch
    {
        SettingsPane.Metrics => L.T("Choose which rings the pill shows, and in what order."),
        SettingsPane.Appearance => L.T("How the pill looks and where it sits."),
        SettingsPane.Alerts => L.T("Notifications when something needs your attention."),
        SettingsPane.General => L.T("Startup, language, performance and everything else."),
        _ => Metric switch
        {
            StatMetric.Cpu => L.T("Load per processor, history and the busiest processes."),
            StatMetric.Gpu => L.T("Load, memory and temperature of each graphics processor."),
            StatMetric.Memory => L.T("Where the memory goes and who is using it."),
            StatMetric.Network => L.T("Throughput, history and the active connection."),
            StatMetric.Disk => L.T("Space on every local drive, and what takes it."),
            StatMetric.Thermals => L.T("Every thermal zone Windows reports."),
            StatMetric.Battery => L.T("Charge, health and power source."),
            _ => L.T("Limits as /usage shows them, and today's use from Claude Code's logs."),
        },
    };
}

/// <summary>What the Settings pages work with.</summary>
internal sealed record SettingsContext(StatsStore Store, AppSettings Settings, StorageAnalyzer Storage, AlertMonitor Alerts,
    Notifier Notifier, Action Quit);

/// <summary>The Settings window: a sidebar of panes and detail pages, the chosen page beside it.</summary>
internal sealed class SettingsWindow
{
    private const double Width = 800;
    private const double Height = 580;

    private readonly SettingsContext context;
    private readonly Window window;
    private readonly NavigationView navigation;
    private readonly ContentControl host;
    private readonly NavigationViewItem quitItem;
    private readonly Dictionary<NavigationViewItem, SettingsSection> items = [];
    private ISettingsPage? page;
    private string detailSignature = "";
    private bool isSelecting;

    public SettingsWindow(SettingsContext context)
    {
        this.context = context;
        window = new Window
        {
            Title = L.T("MYU STATS Settings"),
            SystemBackdrop = new MicaBackdrop(),
            ExtendsContentIntoTitleBar = true,
        };

        var titleBar = new Grid { Height = 40, Background = Palette.Transparent };
        var title = UI.Text("MYU STATS", 12, color: SettingsKit.Secondary);
        title.Margin = new Thickness(16, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleBar.Children.Add(title);
        window.SetTitleBar(titleBar);
        var bar = window.AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x1F, 255, 255, 255);
        bar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x33, 255, 255, 255);

        host = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ContentTransitions = [new ContentThemeTransition { VerticalOffset = 12 }],
        };
        quitItem = new NavigationViewItem { Content = NavigationContent(Icons.Glyph(Glyphs.Power, 15, Palette.White(0.7)), L.T("Quit MYU STATS")), SelectsOnInvoked = false };
        navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            OpenPaneLength = 230,
            PaneHeader = Logo(),
            PaneFooter = Version(),
            Content = host,
        };
        navigation.FooterMenuItems.Add(quitItem);
        navigation.SelectionChanged += (_, args) =>
        {
            if (isSelecting) return;
            if (args.SelectedItemContainer is NavigationViewItem item && items.TryGetValue(item, out var section)) Navigate(section);
        };
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer == quitItem) context.Quit();
        };
        BuildMenu();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(navigation, 1);
        root.Children.Add(titleBar);
        root.Children.Add(navigation);
        window.Content = root;

        context.Settings.Changed += OnSettingsChanged;
        window.Closed += (_, _) =>
        {
            context.Settings.Changed -= OnSettingsChanged;
            page?.Leave();
            page = null;
            Closed?.Invoke();
        };
        window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SizeAndCentre();
    }

    public SettingsSection Selection { get; private set; } = SettingsSection.Metrics;

    public event Action? Closed;

    /// <summary>Brings the window forward, on <paramref name="section"/> when one is given.</summary>
    public void Show(SettingsSection? section)
    {
        if (section is { } wanted) Select(wanted);
        else if (page is null) Select(Selection);
        window.Activate();
        NativeMethods.SetForegroundWindow(WindowNative.GetWindowHandle(window));
    }

    public void Close() => window.Close();

    /// <summary>Called after every sample: updates the page, and the Details list when a sensor comes or goes.</summary>
    public void Refresh()
    {
        page?.Refresh();
        if (DetailSignature() == detailSignature) return;
        BuildMenu();
        if (Selection.Pane == SettingsPane.Detail && !context.Store.IsAvailable(Selection.Metric)) Select(SettingsSection.Metrics);
        else Mark(Selection);
    }

    /// <summary>After Reset every control on the page is out of date, so the page is built again.</summary>
    private void OnSettingsChanged(string name)
    {
        if (name == "*") window.DispatcherQueue.TryEnqueue(() => Navigate(Selection));
    }

    private string DetailSignature() =>
        string.Concat(StatMetrics.All.Select(metric => context.Store.IsAvailable(metric) ? '1' : '0'));

    private void BuildMenu()
    {
        detailSignature = DetailSignature();
        items.Clear();
        navigation.MenuItems.Clear();
        void Add(SettingsSection section, FrameworkElement icon)
        {
            var item = new NavigationViewItem { Content = NavigationContent(icon, section.Title) };
            items[item] = section;
            navigation.MenuItems.Add(item);
        }
        Add(SettingsSection.Metrics, Icons.Glyph(Glyphs.Chart, 15, Palette.White(0.8)));
        Add(SettingsSection.Appearance, Icons.Glyph(Glyphs.Personalize, 15, Palette.White(0.8)));
        Add(SettingsSection.Alerts, Icons.Glyph(Glyphs.Bell, 15, Palette.White(0.8)));
        Add(SettingsSection.General, Icons.Glyph(Glyphs.Settings, 15, Palette.White(0.8)));
        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = L.T("Details") });
        foreach (var metric in StatMetrics.All.Where(context.Store.IsAvailable))
        {
            Add(SettingsSection.Detail(metric), Icons.Metric(metric, 15, Palette.White(0.8), context.Store.Battery));
        }
    }

    /// <summary>Icon and title in one row. The icons are the app's own, so they go in the content
    /// rather than the item's icon slot, which only takes font and path icons.</summary>
    private static UIElement NavigationContent(FrameworkElement icon, string title)
    {
        icon.Width = 20;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var text = UI.Text(title, 14);
        text.VerticalAlignment = VerticalAlignment.Center;
        return UI.Stack(Orientation.Horizontal, 12, icon, text);
    }

    private void Select(SettingsSection section)
    {
        if (!items.ContainsValue(section)) section = SettingsSection.Metrics;
        Mark(section);
        Navigate(section);
    }

    /// <summary>Moves the sidebar's selection without navigating again.</summary>
    private void Mark(SettingsSection section)
    {
        var item = items.FirstOrDefault(pair => pair.Value == section).Key;
        if (item is null) return;
        isSelecting = true;
        navigation.SelectedItem = item;
        isSelecting = false;
    }

    private void Navigate(SettingsSection section)
    {
        page?.Leave();
        Selection = section;
        page = section.Pane switch
        {
            SettingsPane.Metrics => new MetricsPage(context.Store, context.Settings),
            SettingsPane.Appearance => new AppearancePage(context.Settings),
            SettingsPane.Alerts => new AlertsPage(context),
            SettingsPane.General => new GeneralPage(context.Settings),
            _ => DetailPages.Create(section.Metric, context),
        };
        host.Content = page.Root;
    }

    private static UIElement Logo()
    {
        var badge = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Palette.ColorOf(Level.Accent), Offset = 0 },
                    new GradientStop { Color = Palette.ColorOf(Level.Accent, 0.6), Offset = 1 },
                },
            },
            Child = Icons.Glyph(Glyphs.Gauge, 13, Palette.White()),
        };
        var name = UI.Text("MYU STATS", 15, Palette.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        var row = UI.Stack(Orientation.Horizontal, 10, badge, name);
        row.Margin = new Thickness(16, 4, 0, 14);
        return row;
    }

    private static UIElement Version()
    {
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        var text = UI.Text($"MYU STATS {version?.ToString(3) ?? "dev"}", 12, color: SettingsKit.Tertiary);
        text.Margin = new Thickness(18, 4, 0, 14);
        return text;
    }

    /// <summary>The macOS window's size, at this display's scale, in the middle of the work area.</summary>
    private void SizeAndCentre()
    {
        var handle = WindowNative.GetWindowHandle(window);
        var scale = Math.Max(1, NativeMethods.GetDpiForWindow(handle)) / 96.0;
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(Width * scale), area.Width);
        var height = Math.Min((int)(Height * scale), area.Height);
        window.AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(640 * scale);
            presenter.PreferredMinimumHeight = (int)(440 * scale);
        }
    }
}
