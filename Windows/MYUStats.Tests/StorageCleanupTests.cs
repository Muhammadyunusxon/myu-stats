namespace MYUStats.Tests;

public sealed class StorageCleanupTests : IDisposable
{
    private const string FlutterPubspec = "name: app\r\ndependencies:\r\n  flutter:\r\n    sdk: flutter\r\n";

    private readonly string home = Path.Combine(Path.GetTempPath(), "MYUStatsHome-" + Guid.NewGuid());
    private readonly string bin = Path.Combine(Path.GetTempPath(), "MYUStatsBin-" + Guid.NewGuid());

    public StorageCleanupTests()
    {
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(bin);
    }

    public void Dispose()
    {
        foreach (var folder in new[] { home, bin })
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void Write(string path, int bytes, DateTime? modified = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (modified is { } date) File.SetLastWriteTimeUtc(path, date);
    }

    /// <summary>A Flutter project with build output, last touched <paramref name="days"/> ago.</summary>
    private void Project(string relative, double days)
    {
        var folder = Path.Combine(home, relative);
        var date = DateTime.UtcNow.AddDays(-days);
        Write(Path.Combine(folder, "build", "app", "outputs", "app.apk"), 50_000, date);
        Write(Path.Combine(folder, ".dart_tool", "package_config.json"), 2_000, date);
        Write(Path.Combine(folder, "lib", "main.dart"), 100, date);
        File.WriteAllText(Path.Combine(folder, "pubspec.yaml"), FlutterPubspec);
        File.SetLastWriteTimeUtc(Path.Combine(folder, "pubspec.yaml"), date);
        Directory.SetLastWriteTimeUtc(Path.Combine(folder, "build"), date);
    }

    [Fact]
    public void OnlyFlutterProjectsCount()
    {
        Assert.True(StorageAnalyzer.IsFlutter(FlutterPubspec));
        Assert.True(StorageAnalyzer.IsFlutter("dev_dependencies:\n  flutter_test:\n    sdk: 'flutter'\n"));
        Assert.False(StorageAnalyzer.IsFlutter("name: tool\ndependencies:\n  args: ^2.0.0\n"));
    }

    [Fact]
    public void ProjectsAreFoundInTheUsualFoldersNestedOnesTooAndOldOnesAreUnused()
    {
        Project(Path.Combine("source", "repos", "shop"), 90);
        Project(Path.Combine("Projects", "work", "chat"), 0.05);
        Project(Path.Combine("Projects", "work", "chat", "packages", "ui", "example"), 60);
        // Not searched: outside the usual folders, or inside a dependency folder.
        Project(Path.Combine("Music", "band"), 90);
        Project(Path.Combine("Projects", "site", "node_modules", "pkg"), 90);
        // A Flutter project without build output has nothing to clean.
        Directory.CreateDirectory(Path.Combine(home, "Projects", "fresh"));
        File.WriteAllText(Path.Combine(home, "Projects", "fresh", "pubspec.yaml"), FlutterPubspec);

        var found = StorageAnalyzer.FlutterProjects(home, TestContext.Current.CancellationToken).OrderBy(project => project.Name).ToList();
        Assert.Equal(["chat", "example", "shop"], found.Select(project => project.Name));
        var now = DateTimeOffset.Now;
        Assert.Equal([false, true, true], found.Select(project => project.IsStale(now)));
        Assert.All(found, project => Assert.Equal([".dart_tool", "build"], project.Artifacts.Select(Path.GetFileName).Order()));
    }

    [Fact]
    public async Task CleaningAProjectMovesItsBuildOutputAndLeavesTheCode()
    {
        Project(Path.Combine("dev", "app"), 45);
        var recycler = new FakeRecycler(bin);
        var storage = new StorageAnalyzer(home, [], recycler);
        storage.ScanOverview();
        await storage.WaitForScanAsync();
        Assert.Equal(["app"], storage.Projects.Select(project => project.Name));
        Assert.True(storage.Projects[0].Size >= 52_000);

        await storage.CleanStaleProjectsAsync();
        var app = Path.Combine(home, "dev", "app");
        Assert.False(Directory.Exists(Path.Combine(app, "build")));
        Assert.False(Directory.Exists(Path.Combine(app, ".dart_tool")));
        Assert.True(File.Exists(Path.Combine(app, "lib", "main.dart")));
        Assert.Equal(2, recycler.Recycled.Count);
        Assert.Empty(storage.Projects);
        Assert.Contains("app", storage.CleanupReport);
        Assert.Empty(storage.Cleaning);
    }

    [Fact]
    public async Task ACacheIsCleanedTheWayItsEntrySaysOnlyTheNamedFoldersOfThePubCacheGo()
    {
        var pub = Path.Combine(home, "AppData", "Local", "Pub", "Cache");
        Write(Path.Combine(pub, "hosted", "pub.dev", "http-1.2.0", "lib", "http.dart"), 30_000);
        Write(Path.Combine(pub, "git", "cache", "repo", "HEAD"), 1_000);
        Write(Path.Combine(pub, "bin", "flutterfire.bat"), 500);
        var storage = new StorageAnalyzer(home, [], new FakeRecycler(bin));
        storage.ScanOverview();
        await storage.WaitForScanAsync();
        var cache = Assert.Single(storage.DeveloperCaches, item => item.Name == "Cache");
        Assert.Equal(["hosted", "git"], cache.Cleanup?.Only);

        await storage.CleanAsync(cache);
        Assert.False(Directory.Exists(Path.Combine(pub, "hosted")));
        Assert.False(Directory.Exists(Path.Combine(pub, "git")));
        // Globally activated tools stay.
        Assert.True(File.Exists(Path.Combine(pub, "bin", "flutterfire.bat")));
        Assert.Equal(500, Assert.Single(storage.DeveloperCaches, item => item.Name == "Cache").Size);
        Assert.StartsWith("Cache: moved", storage.CleanupReport);
    }

    [Fact]
    public void WhatStaysBehindIsCounted()
    {
        var kept = Path.Combine(home, "kept.bin");
        var gone = Path.Combine(home, "gone.bin");
        Write(kept, 4_000);
        Write(gone, 3_000);
        var result = StorageAnalyzer.MoveToRecycleBin([kept, gone], new PickyRecycler(bin, keep: kept));
        Assert.Equal((3_000L, 1), result);
        // Without a Recycle Bin nothing moves, and nothing is deleted.
        Assert.Equal((0L, 1), StorageAnalyzer.MoveToRecycleBin([kept], null));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void WhatACleanupTakes()
    {
        Write(Path.Combine(home, "a", "1.bin"), 10);
        Write(Path.Combine(home, "b.bin"), 10);
        Assert.Equal(["a", "b.bin"], Cleanup.Contents.Targets(home).Select(Path.GetFileName).Order());
        Assert.Equal(["a"], Cleanup.Items("a", "zzz").Targets(home).Select(Path.GetFileName));
    }

    /// <summary>Moves everything but one path, as when a file is in use.</summary>
    private sealed class PickyRecycler(string bin, string keep) : IRecycler
    {
        public void Recycle(IReadOnlyList<string> paths)
        {
            foreach (var path in paths.Where(path => path != keep)) File.Move(path, Path.Combine(bin, Guid.NewGuid().ToString()));
        }
    }
}
