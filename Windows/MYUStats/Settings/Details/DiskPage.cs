using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using MYUStats.Sampling;
using MYUStats.Views;

namespace MYUStats.Settings;

/// <summary>Space on the Windows drive, what takes it (measured on demand), and the other drives.</summary>
internal static class DiskPage
{
    private const int CollapsedCount = 20;

    private static readonly Rgb[] SegmentColors =
    [
        Rgb.FromUnit(0.04, 0.52, 1.0),
        Rgb.FromUnit(0.69, 0.32, 0.87),
        Rgb.FromUnit(1.0, 0.18, 0.33),
        Rgb.FromUnit(1.0, 0.58, 0.0),
        Rgb.FromUnit(0.19, 0.69, 0.78),
    ];

    public static ISettingsPage Create(StatsStore store, StorageAnalyzer storage)
    {
        var live = new Live();
        var showAll = false;
        DiskUsage? Disk() => store.Disk;
        double Used() => Disk()?.UsedFraction ?? 0;

        var drive = SettingsKit.Section(L.T("Windows drive"), null,
            SettingsKit.Meter(live,
                () => Disk()?.Name ?? "",
                () => Disk() is { } disk ? L.F("{0} of {1}", Format.Bytes(disk.Used), Format.Bytes(disk.Total)) : "",
                Used,
                () => Level.ColorFor(Used())),
            SettingsKit.Value(live, L.T("Free"), () => Format.Bytes(Disk()?.Free ?? 0)),
            UI.Visible(live, SettingsKit.Padded(Composition(live, storage, store)),
                () => storage.HasScanned && storage.Trail.Count == 0 && storage.MeasuredTotal > 0 && Disk() is not null),
            SettingsKit.Warning(live,
                () => Used() >= 0.9 ? L.T("Low disk space: Windows may slow down and updates may fail.") : null,
                () => Level.ColorFor(Used())));

        var analyze = UI.Visible(live, SettingsKit.Row(L.T("Find out which folders take the most space."),
            SettingsKit.Button(L.T("Analyze"), storage.ScanOverview)), () => !storage.HasScanned);
        var back = SettingsKit.IconButton(Glyphs.ChevronLeft, L.T("Back"), storage.Back);
        var path = UI.Text(live, () => string.Join("  ›  ", [L.T("Overview"), .. storage.Trail.Select(item => item.Name)]), 13,
            color: () => SettingsKit.Secondary);
        path.VerticalAlignment = VerticalAlignment.Center;
        var trail = UI.Visible(live, SettingsKit.Padded(UI.Stack(Orientation.Horizontal, 8, back, path)),
            () => storage.HasScanned && storage.Trail.Count > 0);
        var empty = UI.Visible(live, SettingsKit.Padded(UI.Text(L.T("This folder is empty or could not be read."), 14,
            color: SettingsKit.Secondary)), () => storage.HasScanned && !storage.IsScanning && storage.Items.Count == 0);
        var rows = UI.List(live,
            () => !storage.HasScanned ? [] : showAll ? storage.Items : [.. storage.Items.Take(CollapsedCount)],
            (item, rowLive) => Row(rowLive, storage, item, () => Largest(storage.Items), canOpen: true),
            spacing: 0);
        var more = SettingsKit.Button("", () =>
        {
            showAll = !showAll;
            live.Refresh();
        });
        live.Add(() => more.Content = showAll ? L.T("Show fewer") : L.F("Show all {0} items", storage.Items.Count));
        var moreRow = UI.Visible(live, SettingsKit.Padded(more), () => storage.HasScanned && storage.Items.Count > CollapsedCount);
        var space = SettingsKit.Section(Header(live, storage),
            L.T("Measured file by file, so large folders take a moment. Files kept only in OneDrive count as nothing, and junctions and links are not followed."),
            analyze, trail, empty, rows, moreRow);

        var caches = UI.List(live,
            () => storage.HasScanned ? storage.DeveloperCaches : [],
            (item, rowLive) => Row(rowLive, storage, item, () => Largest(storage.DeveloperCaches), canOpen: false),
            spacing: 0);
        var cachesSection = UI.Visible(live, SettingsKit.Section(L.T("Developer and app caches"),
                L.T("Clean moves a cache to the Recycle Bin, and its tool rebuilds what it needs. Items without it hold your own data, such as emulators and Docker's disk, or files in use."),
                caches),
            () => storage.HasScanned && storage.DeveloperCaches.Count > 0);

        var report = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.WrapWholeWords, Foreground = SettingsKit.Secondary };
        live.Add(() => report.Text = storage.CleanupReport ?? "");
        var reportSection = UI.Visible(live, SettingsKit.Section((string?)null, null, SettingsKit.Padded(report)),
            () => storage.CleanupReport is not null);

        var volumes = DiskSampler.OtherVolumes();
        UIElement[] volumeRows = volumes.Count == 0
            ? [SettingsKit.Padded(UI.Text(L.T("No other drives."), 14, color: SettingsKit.Secondary))]
            : [.. volumes.Select(volume => SettingsKit.Meter(live,
                () => volume.Name,
                () => L.F("{0} free of {1}", Format.Bytes(volume.Free), Format.Bytes(volume.Total)),
                () => volume.UsedFraction,
                () => Level.ColorFor(volume.UsedFraction)))];

        var section = SettingsSection.Detail(StatMetric.Disk);
        var root = SettingsKit.Page(section.Title, section.Subtitle,
            UI.Visible(live, drive, () => Disk() is not null),
            space,
            reportSection,
            cachesSection,
            Projects(live, storage),
            SettingsKit.Section(L.T("Other drives"), null, volumeRows));

        void OnChanged() => live.Refresh();
        storage.Changed += OnChanged;
        return new DetailPage(root, live, () =>
        {
            storage.Changed -= OnChanged;
            storage.Cancel();
        });
    }

    private static long Largest(IReadOnlyList<StorageItem> items) => Math.Max(1, items.Max(item => item.Size) ?? 1);

    /// <summary>Flutter projects with build output, and the ones untouched for a month that can go in one step.</summary>
    private static UIElement Projects(Live live, StorageAnalyzer storage)
    {
        IReadOnlyList<FlutterProject> Stale() => [.. storage.Projects.Where(project => project.IsStale(DateTimeOffset.Now) && project.Size is not null)];
        var title = UI.Text(L.T("Flutter projects"), 14, Palette.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        var cleanStale = new Button();
        cleanStale.Click += (_, _) =>
        {
            var stale = Stale();
            Confirm(cleanStale, L.F("Clean {0} unused projects?", stale.Count),
                L.F("Their build and .dart_tool folders move to the Recycle Bin ({0}). Each project builds them again on its next run.",
                    Format.Bytes(stale.Sum(project => project.Size ?? 0))),
                L.T("Move to Recycle Bin"), storage.CleanStaleProjectsAsync);
        };
        live.Add(() =>
        {
            var stale = Stale();
            cleanStale.Content = L.F("Clean unused ({0})", stale.Count);
            cleanStale.Visibility = stale.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            cleanStale.IsEnabled = !stale.Any(project => storage.Cleaning.Contains(project.Path));
        });
        var searching = UI.Visible(live, SettingsKit.Padded(UI.Stack(Orientation.Horizontal, 8,
                new ProgressRing { Width = 16, Height = 16, IsActive = true },
                UI.Text(L.T("Looking for Flutter projects…"), 14, color: SettingsKit.Secondary))),
            () => storage.IsFindingProjects && storage.Projects.Count == 0);
        var rows = UI.List(live, () => storage.Projects,
            (project, rowLive) => ProjectRow(rowLive, storage, project, () => Math.Max(1, storage.Projects.Max(item => item.Size) ?? 1)),
            spacing: 0);
        return UI.Visible(live, SettingsKit.Section(UI.Row(title, cleanStale),
                L.T("Clean moves only build and .dart_tool to the Recycle Bin, what flutter clean removes. A project counts as unused after 30 days without a build or an edit. Searched in folders such as Projects, source and Documents."),
                searching, rows),
            () => storage.HasScanned && (storage.IsFindingProjects || storage.Projects.Count > 0));
    }

    private static UIElement ProjectRow(Live live, StorageAnalyzer storage, Func<FlutterProject> project, Func<long> largest)
    {
        var icon = new FontIcon { FontSize = 16, Foreground = Palette.White(0.8), Glyph = Glyphs.Folder, VerticalAlignment = VerticalAlignment.Center };
        var name = UI.Text(live, () => project().Name, 14);
        var unused = UI.Visible(live, UI.Text(L.T("Unused"), 11, Palette.SemiBold, Palette.Brush(Level.Caution)),
            () => project().IsStale(DateTimeOffset.Now));
        unused.VerticalAlignment = VerticalAlignment.Center;
        var caption = UI.Text(live, () => L.F("{0} · used {1}", Where(project().Path), Format.Ago(project().LastUsed, DateTimeOffset.Now)), 12,
            color: () => SettingsKit.Secondary);
        var bar = new BarGauge(4);
        var size = UI.Text(live, () => project().Size is { } bytes ? Format.Bytes(bytes) : "", 13, color: () => SettingsKit.Secondary);
        size.VerticalAlignment = VerticalAlignment.Center;
        var reveal = SettingsKit.IconButton(Glyphs.OpenFolder, L.T("Show in Explorer"), () => Shell.Reveal(project().Path));
        var clean = CleanButton(live, () => storage.Cleaning.Contains(project().Path), () => project().Size is not null, button =>
            Confirm(button, L.F("Clean {0}?", project().Name),
                L.F("Its build and .dart_tool folders move to the Recycle Bin ({0}). The next run builds them again.", Format.Bytes(project().Size ?? 0)),
                L.T("Move to Recycle Bin"), () => storage.CleanAsync(project())));
        live.Add(() => bar.Update((double)(project().Size ?? 0) / Math.Max(largest(), 1), Level.Accent));

        var grid = new Grid { Padding = new Thickness(16, 8, 10, 8), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UI.Stack(4, UI.Row(UI.Stack(Orientation.Horizontal, 8, name, unused), size), caption, bar);
        text.VerticalAlignment = VerticalAlignment.Center;
        var actions = UI.Stack(Orientation.Horizontal, 0, reveal, clean);
        actions.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>The folder a project sits in, from the user folder down: "source\repos".</summary>
    private static string Where(string path)
    {
        var parent = System.IO.Path.GetDirectoryName(path) ?? path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return parent.StartsWith(home, StringComparison.OrdinalIgnoreCase) && parent.Length > home.Length
            ? parent[(home.Length + 1)..]
            : parent;
    }

    /// <summary>The trash can on a cleanable row, or a spinner in its place while it cleans.</summary>
    private static UIElement CleanButton(Live live, Func<bool> isCleaning, Func<bool> canClean, Action<Button> clicked)
    {
        Button? button = null;
        button = SettingsKit.IconButton(Glyphs.Delete, L.T("Clean"), () => clicked(button!));
        var spinner = new ProgressRing { Width = 16, Height = 16, Margin = new Thickness(8, 0, 8, 0) };
        live.Add(() =>
        {
            var cleaning = isCleaning();
            spinner.IsActive = cleaning;
            spinner.Visibility = cleaning ? Visibility.Visible : Visibility.Collapsed;
            button.Visibility = cleaning ? Visibility.Collapsed : Visibility.Visible;
            button.IsEnabled = canClean();
        });
        var holder = new Grid { VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(button);
        holder.Children.Add(spinner);
        return holder;
    }

    /// <summary>Asks before a clean-up, in a dialog over the Settings window.</summary>
    private static async void Confirm(FrameworkElement anchor, string title, string message, string action, Func<Task> clean)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = anchor.XamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
                PrimaryButtonText = action,
                CloseButtonText = L.T("Cancel"),
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ElementTheme.Dark,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await clean();
        }
        catch (Exception error)
        {
            Log.Storage.Error($"Clean-up failed: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>The section title, with a spinner while measuring and a Rescan button once measured.</summary>
    private static UIElement Header(Live live, StorageAnalyzer storage)
    {
        var title = UI.Text(L.T("What's using space"), 14, Palette.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        var progress = new ProgressRing { Width = 16, Height = 16 };
        var rescan = SettingsKit.Button(L.T("Rescan"), storage.Rescan);
        live.Add(() =>
        {
            progress.IsActive = storage.IsScanning;
            progress.Visibility = storage.IsScanning ? Visibility.Visible : Visibility.Collapsed;
            rescan.Visibility = storage.HasScanned ? Visibility.Visible : Visibility.Collapsed;
            rescan.IsEnabled = !storage.IsScanning;
        });
        return UI.Row(title, UI.Stack(Orientation.Horizontal, 10, progress, rescan));
    }

    /// <summary>A file or folder with its size, a bar against the largest sibling, and actions.</summary>
    private static UIElement Row(Live live, StorageAnalyzer storage, Func<StorageItem> item, Func<long> largest, bool canOpen)
    {
        var icon = new FontIcon { FontSize = 16, Foreground = Palette.White(0.8), VerticalAlignment = VerticalAlignment.Center };
        var name = UI.Text(live, () => item().Name, 14);
        var note = UI.Visible(live, UI.Text(live, () => item().Note ?? "", 12, color: () => SettingsKit.Secondary), () => item().Note is not null);
        var bar = new BarGauge(4);
        var size = UI.Text(live, () => item().Size is { } bytes ? Format.Bytes(bytes) : "", 13, color: () => SettingsKit.Secondary);
        size.VerticalAlignment = VerticalAlignment.Center;
        var measuring = new ProgressRing { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var reveal = SettingsKit.IconButton(Glyphs.OpenFolder, L.T("Show in Explorer"), () => Shell.Reveal(item().Path));
        var open = SettingsKit.IconButton(Glyphs.ChevronRight, L.T("Look inside"), () => storage.Open(item()));
        var clean = CleanButton(live, () => storage.Cleaning.Contains(item().Path), () => item().Size is not null, button =>
            Confirm(button, L.F("Clean {0}?", item().Name),
                L.T("It moves to the Recycle Bin, and its tool rebuilds what it needs, so the next build or install may take longer."),
                L.T("Move to Recycle Bin"), () => storage.CleanAsync(item())));
        live.Add(() =>
        {
            var current = item();
            icon.Glyph = current.IsDirectory ? Glyphs.Folder : Glyphs.File;
            bar.Update((double)(current.Size ?? 0) / Math.Max(largest(), 1), Level.Accent);
            measuring.IsActive = current.Size is null;
            measuring.Visibility = current.Size is null ? Visibility.Visible : Visibility.Collapsed;
            // Kept in place when unavailable, so the columns line up.
            var openable = canOpen && current.IsDirectory;
            open.IsEnabled = openable;
            open.Opacity = openable ? 1 : 0;
            open.Visibility = current.Cleanup is null ? Visibility.Visible : Visibility.Collapsed;
            clean.Visibility = current.Cleanup is null ? Visibility.Collapsed : Visibility.Visible;
        });

        var grid = new Grid { Padding = new Thickness(16, 8, 10, 8), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UI.Stack(4, UI.Row(name, UI.Stack(Orientation.Horizontal, 6, measuring, size)), note, bar);
        text.VerticalAlignment = VerticalAlignment.Center;
        var actions = UI.Stack(Orientation.Horizontal, 0, reveal, open, clean);
        actions.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>Used space split into the biggest items, the rest of what was measured, and what could not be
    /// attributed (Windows itself, other users, the page and hibernation files).</summary>
    private static UIElement Composition(Live live, StorageAnalyzer storage, StatsStore store)
    {
        var bar = new Grid { Height = 10, CornerRadius = new CornerRadius(5), ColumnSpacing = 1.5 };
        var legend = new StackPanel { Spacing = 4 };
        live.Add(() =>
        {
            var segments = Segments(storage.Items, store.Disk?.Used ?? 0);
            var total = Math.Max(1, segments.Sum(segment => segment.Size));
            bar.Children.Clear();
            bar.ColumnDefinitions.Clear();
            legend.Children.Clear();
            for (var index = 0; index < segments.Count; index++)
            {
                var (name, size, color) = segments[index];
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(size / (double)total, 0.004), GridUnitType.Star) });
                var block = new Border { Background = Palette.Brush(color) };
                Grid.SetColumn(block, index);
                bar.Children.Add(block);
                var dot = new Ellipse { Width = 8, Height = 8, Fill = Palette.Brush(color), VerticalAlignment = VerticalAlignment.Center };
                legend.Children.Add(UI.Stack(Orientation.Horizontal, 6, dot,
                    UI.Text(name, 12), UI.Text(Format.Bytes(size), 12, color: SettingsKit.Secondary)));
            }
        });
        return UI.Stack(8, bar, legend);
    }

    private static List<(string Name, long Size, Rgb Color)> Segments(IReadOnlyList<StorageItem> items, long used)
    {
        var measured = items.Where(item => item.Size > 0).ToList();
        var result = measured.Take(SegmentColors.Length)
            .Select((item, index) => (item.Name, item.Size ?? 0, SegmentColors[index]))
            .ToList();
        var rest = measured.Skip(SegmentColors.Length).Sum(item => item.Size ?? 0);
        if (rest > 0) result.Add((L.T("Everything else"), rest, Rgb.FromUnit(0.56, 0.56, 0.58)));
        var unattributed = used - measured.Sum(item => item.Size ?? 0);
        if (unattributed > 0) result.Add((L.T("Windows and other"), unattributed, Rgb.FromUnit(0.3, 0.3, 0.3)));
        return result;
    }
}
