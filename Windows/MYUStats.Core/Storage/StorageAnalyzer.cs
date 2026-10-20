using System.Text.RegularExpressions;
using MYUStats.Native;

namespace MYUStats;

public sealed record StorageItem(string Path, string Name, bool IsDirectory)
{
    /// <summary>Bytes on disk; null while still being measured.</summary>
    public long? Size { get; init; }

    /// <summary>Shown under the name for well-known caches.</summary>
    public string? Note { get; init; }

    /// <summary>How the Disk page can clear it; null for anything that holds the user's own data.</summary>
    public Cleanup? Cleanup { get; init; }
}

/// <summary>How a cache is cleared: everything inside the folder, or only the named sub-folders, so the
/// tool's settings and the downloads it cannot fetch again stay.</summary>
public sealed record Cleanup(IReadOnlyList<string>? Only)
{
    public static Cleanup Contents { get; } = new((IReadOnlyList<string>?)null);

    public static Cleanup Items(params string[] names) => new(names);

    /// <summary>What goes to the Recycle Bin for the folder at <paramref name="path"/>.</summary>
    public IReadOnlyList<string> Targets(string path)
    {
        if (Only is not null)
        {
            return [.. Only.Select(name => System.IO.Path.Combine(path, name)).Where(item => Directory.Exists(item) || File.Exists(item))];
        }
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(path)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>A Flutter project with build output that <c>flutter clean</c> would remove.</summary>
public sealed record FlutterProject(string Path, DateTimeOffset LastUsed, IReadOnlyList<string> Artifacts)
{
    /// <summary>Bytes in <see cref="Artifacts"/>; null while still being measured.</summary>
    public long? Size { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>Untouched for a month: its build output is only taking space.</summary>
    public bool IsStale(DateTimeOffset now) => now - LastUsed > StorageAnalyzer.StaleAfter;
}

/// <summary>Moves files and folders to the Recycle Bin; tests move them somewhere of their own.</summary>
public interface IRecycler
{
    /// <summary>Moves as many of these as it can; what it could not move stays where it was.</summary>
    void Recycle(IReadOnlyList<string> paths);
}

/// <summary>
/// Measures what takes space on the system drive, on demand.
/// </summary>
/// <remarks>
/// The overview lists every top-level item in the user folder plus Program Files, largest first. Any
/// folder can be opened to measure its own children. Sizes are cached for the session, so going back
/// up is instant. Measuring walks every file, which takes a while on large folders, so it runs on pool
/// threads, a few folders at a time, and is cancelled when the page is left. Results are applied on
/// the thread that started the scan (the UI thread in the app).
/// </remarks>
public sealed partial class StorageAnalyzer
{
    private const int Parallelism = 4;
    /// <summary>The file attribute cloud files carry until they are downloaded.</summary>
    private const int RecallOnDataAccess = 0x400000;
    public const int ProjectSearchDepth = 6;

    /// <summary>Results arriving closer together than this are sorted once, so rows do not jump under the pointer.</summary>
    public static TimeSpan SortDelay { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A project untouched for this long counts as unused.</summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromDays(30);

    /// <summary>Folders in the user folder where projects usually live, matched without case.</summary>
    public static IReadOnlySet<string> ProjectFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "projects", "project", "developer", "dev", "code", "src", "source", "sources", "repos", "repositories",
        "workspace", "work", "github", "gitlab", "flutter", "flutter_projects", "flutterprojects",
        "StudioProjects", "AndroidStudioProjects", "Documents", "Desktop",
    };

    /// <summary>Never hold a project of their own, or are too big to walk for one.</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "node_modules", "Pods", "ios", "android", "macos", "windows", "linux", "web", "lib", "test",
        "assets", "AppData", "bin", "obj",
    };

    private static readonly EnumerationOptions ListOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly string home;
    private readonly IReadOnlyList<string> extraRoots;
    private readonly IRecycler? recycler;
    private readonly Dictionary<string, long> sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> cleaning = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? scan;
    private Task? scanTask;
    private CancellationTokenSource? pendingSort;
    private CancellationTokenSource? projectSearch;
    private Task? projectTask;

    /// <param name="home">Folder whose top-level items make up the overview.</param>
    /// <param name="extraRoots">Measured alongside them.</param>
    /// <param name="recycler">Moves what is cleaned up to the Recycle Bin; the shell's own on Windows.</param>
    public StorageAnalyzer(string home, IReadOnlyList<string> extraRoots, IRecycler? recycler = null)
    {
        this.home = home;
        this.extraRoots = extraRoots;
        this.recycler = recycler ?? (OperatingSystem.IsWindows() ? new RecycleBin() : null);
    }

    /// <summary>The user folder, plus Program Files for installed apps.</summary>
    public static StorageAnalyzer ForCurrentUser()
    {
        var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }
            .Where(path => path.Length > 0 && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new StorageAnalyzer(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), roots);
    }

    /// <summary>Folders opened from the overview, outermost first. Empty means the overview.</summary>
    public IReadOnlyList<StorageItem> Trail { get; private set; } = [];
    public IReadOnlyList<StorageItem> Items { get; private set; } = [];
    public IReadOnlyList<StorageItem> DeveloperCaches { get; private set; } = [];
    public bool IsScanning { get; private set; }
    public bool HasScanned { get; private set; }

    /// <summary>Flutter projects under the usual project folders, largest build output first.</summary>
    public IReadOnlyList<FlutterProject> Projects { get; private set; } = [];
    public bool IsFindingProjects { get; private set; }

    /// <summary>Paths of the caches and projects being cleaned right now.</summary>
    public IReadOnlySet<string> Cleaning => cleaning;

    /// <summary>What the last clean-up did, shown until the next one.</summary>
    public string? CleanupReport { get; private set; }

    public long MeasuredTotal => Items.Sum(item => item.Size ?? 0);

    /// <summary>Raised when the list, a size or the scanning state changes.</summary>
    public event Action? Changed;

    // MARK: - Navigation

    public void ScanOverview()
    {
        Trail = [];
        HasScanned = true;
        Items = [.. OverviewRoots().Select(ItemFor)];
        DeveloperCaches = [.. DeveloperCacheRoots(home).Select(cache => ItemFor(cache.Path) with { Note = cache.Note, Cleanup = cache.Cleanup })];
        Measure([.. Items.Select(item => item.Path), .. DeveloperCaches.Select(item => item.Path)]);
        FindProjects();
    }

    public void Open(StorageItem folder)
    {
        if (!folder.IsDirectory) return;
        Trail = [.. Trail, folder];
        Items = [.. Children(folder.Path).Select(ItemFor)];
        Measure([.. Items.Select(item => item.Path)]);
    }

    public void Back()
    {
        if (Trail.Count == 0) return;
        Trail = [.. Trail.Take(Trail.Count - 1)];
        Items = Trail.Count > 0
            ? [.. Children(Trail[^1].Path).Select(ItemFor)]
            : [.. OverviewRoots().Select(ItemFor)];
        Measure([.. Items.Select(item => item.Path)]);
    }

    public void Rescan()
    {
        sizes.Clear();
        if (Trail.Count == 0)
        {
            ScanOverview();
            return;
        }
        var current = Trail[^1];
        Trail = [.. Trail.Take(Trail.Count - 1)];
        Open(current);
    }

    public void Cancel()
    {
        scan?.Cancel();
        scan = null;
        projectSearch?.Cancel();
        projectSearch = null;
        if (!IsScanning && !IsFindingProjects) return;
        IsScanning = false;
        IsFindingProjects = false;
        Changed?.Invoke();
    }

    /// <summary>Waits for the current measurement, and the project search, to finish; for tests.</summary>
    public async Task WaitForScanAsync()
    {
        await (scanTask ?? Task.CompletedTask);
        await (projectTask ?? Task.CompletedTask);
    }

    // MARK: - Measuring

    private StorageItem ItemFor(string path) => new(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), Directory.Exists(path))
    {
        Size = sizes.TryGetValue(path, out var size) ? size : null,
    };

    private void Measure(IReadOnlyList<string> paths)
    {
        scan?.Cancel();
        var pending = paths.Where(path => !sizes.ContainsKey(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SortNow();
        if (pending.Count == 0)
        {
            IsScanning = false;
            scanTask = null;
            Changed?.Invoke();
            return;
        }
        IsScanning = true;
        Changed?.Invoke();
        var cancellation = new CancellationTokenSource();
        scan = cancellation;
        scanTask = MeasureAsync(pending, cancellation.Token);
    }

    /// <summary>Runs the walks on pool threads and records each result back on the calling thread.</summary>
    private async Task MeasureAsync(List<string> pending, CancellationToken cancellation)
    {
        var queue = new Queue<string>(pending);
        var running = new List<Task<(string Path, long Size)>>();
        void Enqueue()
        {
            if (!queue.TryDequeue(out var path)) return;
            running.Add(Task.Run(() => (path, SafeSize(path, cancellation)), CancellationToken.None));
        }
        for (var index = 0; index < Parallelism; index++) Enqueue();

        while (running.Count > 0)
        {
            var done = await Task.WhenAny(running);
            running.Remove(done);
            if (cancellation.IsCancellationRequested) return;
            var (path, size) = await done;
            Record(size, path);
            Enqueue();
        }
        SortNow();
        IsScanning = false;
        Changed?.Invoke();
    }

    private void Record(long size, string path)
    {
        sizes[path] = size;
        Items = [.. Items.Select(item => PathEquals(item.Path, path) ? item with { Size = size } : item)];
        DeveloperCaches = [.. DeveloperCaches.Select(item => PathEquals(item.Path, path) ? item with { Size = size } : item)];
        Changed?.Invoke();
        ScheduleSort();
    }

    /// <summary>Sorts once results pause for <see cref="SortDelay"/>, instead of on every result.</summary>
    private async void ScheduleSort()
    {
        pendingSort?.Cancel();
        var cancellation = new CancellationTokenSource();
        pendingSort = cancellation;
        try
        {
            await Task.Delay(SortDelay, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        SortNow();
        Changed?.Invoke();
    }

    /// <summary>Largest first; anything still being measured sinks to the bottom.</summary>
    private void SortNow()
    {
        pendingSort?.Cancel();
        pendingSort = null;
        Items = [.. Items.OrderByDescending(item => item.Size ?? -1)];
        DeveloperCaches = [.. DeveloperCaches.OrderByDescending(item => item.Size ?? -1)];
    }

    private static bool PathEquals(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bytes taken by a file or everything under a folder. Unreadable entries are skipped. Junctions
    /// and symbolic links are not followed (the user folder has several that loop back), and cloud
    /// files that are not downloaded count as nothing.
    /// </summary>
    public static long AllocatedSize(string path, CancellationToken cancellation = default)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists) return IsCloudOnly(file.Attributes) ? 0 : file.Length;
            if (!Directory.Exists(path)) return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        long total = 0;
        var checkedEntries = 0;
        var folders = new Stack<string>();
        folders.Push(path);
        while (folders.Count > 0)
        {
            var folder = folders.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListOptions))
                {
                    if (++checkedEntries % 2_000 == 0 && cancellation.IsCancellationRequested) return total;
                    var attributes = entry.Attributes;
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        if (!attributes.HasFlag(FileAttributes.ReparsePoint)) folders.Push(entry.FullName);
                    }
                    else if (entry is FileInfo item && !IsCloudOnly(attributes))
                    {
                        total += item.Length;
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Protected or vanished folders are expected; skip them and keep walking.
            }
        }
        return total;
    }

    /// <summary>A walk that fails in an unexpected way measures as nothing rather than stopping the scan.</summary>
    private static long SafeSize(string path, CancellationToken cancellation)
    {
        try
        {
            return AllocatedSize(path, cancellation);
        }
        catch (Exception error)
        {
            Log.Storage.Error($"Measuring failed: {error.GetType().Name}: {error.Message}");
            return 0;
        }
    }

    private static bool IsCloudOnly(FileAttributes attributes) =>
        ((int)attributes & RecallOnDataAccess) != 0 || attributes.HasFlag(FileAttributes.Offline);

    // MARK: - Cleaning up

    /// <summary>Clears a cache the way its <see cref="StorageItem.Cleanup"/> says, then measures it again.</summary>
    public async Task CleanAsync(StorageItem item)
    {
        if (item.Cleanup is not { } cleanup || !cleaning.Add(item.Path)) return;
        Changed?.Invoke();
        try
        {
            var result = await Task.Run(() => MoveToRecycleBin(cleanup.Targets(item.Path), recycler));
            CleanupReport = Report(item.Name, result);
            Log.Storage.Notice($"Cleaned a cache: {result.Moved} bytes moved, {result.Failed} not moved");
            sizes.Remove(item.Path);
            var size = await Task.Run(() => SafeSize(item.Path, CancellationToken.None));
            Record(size, item.Path);
        }
        finally
        {
            cleaning.Remove(item.Path);
            Changed?.Invoke();
        }
    }

    /// <summary>Moves a project's build output to the Recycle Bin.</summary>
    public Task CleanAsync(FlutterProject project) => CleanProjectsAsync([project]);

    /// <summary>Moves the build output of every project untouched for a month to the Recycle Bin.</summary>
    public Task CleanStaleProjectsAsync()
    {
        var now = DateTimeOffset.Now;
        return CleanProjectsAsync([.. Projects.Where(project => project.IsStale(now))]);
    }

    private async Task CleanProjectsAsync(IReadOnlyList<FlutterProject> chosen)
    {
        var pending = chosen.Where(project => !cleaning.Contains(project.Path)).ToList();
        if (pending.Count == 0) return;
        foreach (var project in pending) cleaning.Add(project.Path);
        Changed?.Invoke();
        try
        {
            var result = await Task.Run(() => MoveToRecycleBin([.. pending.SelectMany(project => project.Artifacts)], recycler));
            CleanupReport = Report(pending.Count == 1 ? pending[0].Name : L.F("{0} projects", pending.Count), result);
            Log.Storage.Notice($"Cleaned {pending.Count} Flutter projects: {result.Moved} bytes moved, {result.Failed} not moved");
            // What could not be moved is still there.
            var cleaned = pending
                .Where(project => !project.Artifacts.Any(Directory.Exists))
                .Select(project => project.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Projects = [.. Projects.Where(project => !cleaned.Contains(project.Path))];
        }
        finally
        {
            foreach (var project in pending) cleaning.Remove(project.Path);
            Changed?.Invoke();
        }
    }

    /// <summary>Measures each item, then moves them all; counts by what is gone afterwards.</summary>
    public static (long Moved, int Failed) MoveToRecycleBin(IReadOnlyList<string> paths, IRecycler? recycler)
    {
        if (paths.Count == 0) return (0, 0);
        var sizes = paths.Select(path => SafeSize(path, CancellationToken.None)).ToList();
        try
        {
            if (recycler is null) throw new PlatformNotSupportedException("There is no Recycle Bin here.");
            recycler.Recycle(paths);
        }
        catch (Exception error)
        {
            Log.Storage.Notice($"Not everything went to the Recycle Bin: {error.GetType().Name}: {error.Message}");
        }
        long moved = 0;
        var failed = 0;
        for (var index = 0; index < paths.Count; index++)
        {
            if (File.Exists(paths[index]) || Directory.Exists(paths[index])) failed++;
            else moved += sizes[index];
        }
        return (moved, failed);
    }

    private static string Report(string name, (long Moved, int Failed) result)
    {
        if (result.Failed > 0)
        {
            return L.F("{0}: moved {1} to the Recycle Bin. Some items could not be moved; they may be in use.", name, Format.Bytes(result.Moved));
        }
        return result.Moved == 0
            ? L.F("{0}: nothing to clean.", name)
            : L.F("{0}: moved {1} to the Recycle Bin. Empty it to get the space back.", name, Format.Bytes(result.Moved));
    }

    // MARK: - Flutter projects

    private void FindProjects()
    {
        projectSearch?.Cancel();
        var cancellation = new CancellationTokenSource();
        projectSearch = cancellation;
        Projects = [];
        IsFindingProjects = true;
        Changed?.Invoke();
        projectTask = FindProjectsAsync(cancellation.Token);
    }

    /// <summary>Searches on a pool thread, then measures a few projects at a time; results land on the calling thread.</summary>
    private async Task FindProjectsAsync(CancellationToken cancellation)
    {
        var found = await Task.Run(() => FlutterProjects(home, cancellation), CancellationToken.None);
        if (cancellation.IsCancellationRequested) return;
        Projects = found;
        Changed?.Invoke();

        var queue = new Queue<FlutterProject>(found);
        var running = new List<Task<(string Path, long Size)>>();
        void Enqueue()
        {
            if (!queue.TryDequeue(out var project)) return;
            running.Add(Task.Run(() => (project.Path, project.Artifacts.Sum(artifact => SafeSize(artifact, cancellation))), CancellationToken.None));
        }
        for (var index = 0; index < Parallelism; index++) Enqueue();
        while (running.Count > 0)
        {
            var done = await Task.WhenAny(running);
            running.Remove(done);
            if (cancellation.IsCancellationRequested) return;
            var (path, size) = await done;
            Projects = [.. Projects.Select(project => PathEquals(project.Path, path) ? project with { Size = size } : project)];
            Changed?.Invoke();
            Enqueue();
        }
        Projects = [.. Projects.Where(project => project.Size > 0).OrderByDescending(project => project.Size)];
        IsFindingProjects = false;
        Changed?.Invoke();
    }

    /// <summary>Flutter projects with build output, searched for a few levels down the usual project folders.
    /// Projects nest (a package's example app, a monorepo's packages), so the search goes on inside them.</summary>
    public static List<FlutterProject> FlutterProjects(string home, CancellationToken cancellation = default)
    {
        var pending = new Stack<(string Path, int Depth)>();
        foreach (var folder in Folders(home).Where(path => ProjectFolders.Contains(System.IO.Path.GetFileName(path))))
        {
            pending.Push((folder, 1));
        }
        var found = new List<FlutterProject>();
        while (pending.Count > 0 && !cancellation.IsCancellationRequested)
        {
            var (folder, depth) = pending.Pop();
            if (FlutterProjectAt(folder) is { } project) found.Add(project);
            if (depth >= ProjectSearchDepth) continue;
            foreach (var child in Folders(folder).Where(path => !SkippedFolders.Contains(System.IO.Path.GetFileName(path))))
            {
                pending.Push((child, depth + 1));
            }
        }
        return found;
    }

    public static FlutterProject? FlutterProjectAt(string folder)
    {
        var pubspec = new FileInfo(System.IO.Path.Combine(folder, "pubspec.yaml"));
        try
        {
            // A pubspec kept only in OneDrive would be downloaded by reading it.
            if (!pubspec.Exists || IsCloudOnly(pubspec.Attributes) || pubspec.Length > 1 << 20) return null;
            if (!IsFlutter(File.ReadAllText(pubspec.FullName))) return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        List<string> artifacts = [.. new[] { "build", ".dart_tool" }.Select(name => System.IO.Path.Combine(folder, name)).Where(Directory.Exists)];
        return artifacts.Count == 0 ? null : new FlutterProject(folder, LastUsed(folder), artifacts);
    }

    /// <summary>A Flutter app or plugin depends on the Flutter SDK; a plain Dart package does not.</summary>
    public static bool IsFlutter(string pubspec) => FlutterSdk().IsMatch(pubspec);

    [GeneratedRegex(@"^\s+sdk:\s*[""']?flutter[""']?\s*$", RegexOptions.Multiline)]
    private static partial Regex FlutterSdk();

    /// <summary>The newest of the project's builds, package fetches and source edits.</summary>
    public static DateTimeOffset LastUsed(string folder)
    {
        var newest = DateTime.MinValue;
        void Consider(FileSystemInfo info)
        {
            if (info.Exists && info.LastWriteTimeUtc > newest) newest = info.LastWriteTimeUtc;
        }
        Consider(new FileInfo(System.IO.Path.Combine(folder, "pubspec.yaml")));
        Consider(new FileInfo(System.IO.Path.Combine(folder, "pubspec.lock")));
        Consider(new FileInfo(System.IO.Path.Combine(folder, ".dart_tool", "package_config.json")));
        Consider(new DirectoryInfo(System.IO.Path.Combine(folder, "build")));
        var lib = new DirectoryInfo(System.IO.Path.Combine(folder, "lib"));
        if (lib.Exists)
        {
            try
            {
                foreach (var file in lib.EnumerateFiles("*", SearchOptions).Take(2_000)) Consider(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // What could be read is enough.
            }
        }
        return newest == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(newest, TimeSpan.Zero);
    }

    private static readonly EnumerationOptions SearchOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Sub-folders, less hidden ones, links and junctions.</summary>
    private static List<string> Folders(string folder)
    {
        try
        {
            return
            [
                .. new DirectoryInfo(folder).EnumerateDirectories("*", ListOptions)
                    .Where(entry => !entry.Name.StartsWith('.')
                        && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                        && !entry.Attributes.HasFlag(FileAttributes.Hidden))
                    .Select(entry => entry.FullName),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // MARK: - What to measure

    /// <summary>Everything directly in the user folder (hidden ones too: .gradle and friends are often the biggest), plus Program Files.</summary>
    private IEnumerable<string> OverviewRoots() => Children(home).Concat(extraRoots);

    private static List<string> Children(string folder)
    {
        try
        {
            return
            [
                .. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListOptions)
                    // The user folder holds old compatibility junctions ("My Documents", "Application
                    // Data") that lead back into it; they are not places of their own.
                    .Where(entry => !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    .Select(entry => entry.FullName),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Storage.Notice($"A folder could not be listed: {error.GetType().Name}");
            return [];
        }
    }

    /// <summary>
    /// Well-known tool caches; only the ones present on this PC are listed. Those with a clean-up are
    /// rebuilt by their tool; the others (emulators, Docker's disk, the SDK) hold the user's own data,
    /// and temporary files are often in use.
    /// </summary>
    private static List<(string Path, string Note, Cleanup? Cleanup)> DeveloperCacheRoots(string home)
    {
        (string Relative, string Note, Cleanup? Cleanup)[] candidates =
        [
            (@"AppData\Local\Temp", L.T("Temporary files · safe to clear while other apps are closed"), null),
            (@"AppData\Local\CrashDumps", L.T("Crash dumps · safe to delete"), Cleanup.Contents),
            // The wrapper's Gradle downloads stay: only the dependency and build caches go.
            (".gradle", L.T("Gradle caches · Clean keeps the downloaded Gradle versions"), Cleanup.Items("caches")),
            (@"AppData\Local\Android\Sdk", L.T("Android SDK, system images and emulators"), null),
            (@".android\avd", L.T("Android emulator images"), null),
            // Globally activated tools live in bin and global_packages, and stay.
            (@"AppData\Local\Pub\Cache", L.T("Flutter / Dart packages · downloaded again by pub get"), Cleanup.Items("hosted", "git")),
            (@".nuget\packages", L.T("NuGet packages · `dotnet nuget locals all --clear`"), Cleanup.Contents),
            (@"AppData\Local\npm-cache", L.T("npm cache · `npm cache clean --force`"), Cleanup.Items("_cacache")),
            (@"AppData\Local\Yarn\Cache", L.T("Yarn cache"), Cleanup.Contents),
            (@"AppData\Local\pnpm\store", L.T("pnpm store · `pnpm store prune`"), null),
            (@"AppData\Local\pip\Cache", L.T("pip cache · `pip cache purge`"), Cleanup.Contents),
            (@".cargo\registry", L.T("Rust crates · rebuilt on the next build"), Cleanup.Items("cache", "src")),
            (@"AppData\Local\Docker\wsl", L.T("Docker Desktop images and volumes"), null),
        ];
        return
        [
            .. candidates
                // Split, so the same paths also resolve where the tests run.
                .Select(candidate => (Path: System.IO.Path.Combine([home, .. candidate.Relative.Split('\\')]), candidate.Note, candidate.Cleanup))
                .Where(candidate => Directory.Exists(candidate.Path)),
        ];
    }
}
