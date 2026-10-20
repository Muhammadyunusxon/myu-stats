namespace MYUStats.Tests;

public sealed class StorageAnalyzerTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "MYUStatsHome-" + Guid.NewGuid());

    /// <summary>home/big (300 KB, part in a subfolder), home/small (20 KB), home/note.txt (5 KB).</summary>
    public StorageAnalyzerTests()
    {
        foreach (var (path, size) in new[] { ("big/inner/a.bin", 200_000), ("big/b.bin", 100_000), ("small/c.bin", 20_000), ("note.txt", 5_000) })
        {
            var file = Path.Combine(home, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, new byte[size]);
        }
    }

    private static long Size(string path) => StorageAnalyzer.AllocatedSize(path, TestContext.Current.CancellationToken);

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    [Fact]
    public void AFoldersSizeCoversEveryFileBeneathIt()
    {
        Assert.Equal(300_000, Size(Path.Combine(home, "big")));
        Assert.Equal(325_000, Size(home));
        Assert.Equal(5_000, Size(Path.Combine(home, "note.txt")));
    }

    [Fact]
    public void MissingPathsMeasureAsZeroRatherThanFailing() =>
        Assert.Equal(0, Size(Path.Combine(home, "nonexistent")));

    [Fact]
    public void LinksAreNotFollowed()
    {
        Directory.CreateSymbolicLink(Path.Combine(home, "small", "loop"), home);
        Assert.Equal(20_000, Size(Path.Combine(home, "small")));
    }

    [Fact]
    public async Task TheOverviewIsMeasuredAndSortedLargestFirst()
    {
        var storage = new StorageAnalyzer(home, []);
        storage.ScanOverview();
        await storage.WaitForScanAsync();

        Assert.False(storage.IsScanning);
        Assert.True(storage.HasScanned);
        Assert.Equal(["big", "small", "note.txt"], storage.Items.Select(item => item.Name));
        Assert.All(storage.Items, item => Assert.NotNull(item.Size));
        Assert.Equal(325_000, storage.MeasuredTotal);
        Assert.Empty(storage.DeveloperCaches);
    }

    [Fact]
    public async Task OpeningAFolderAndGoingBackKeepsTheSizesAlreadyMeasured()
    {
        var storage = new StorageAnalyzer(home, []);
        storage.ScanOverview();
        await storage.WaitForScanAsync();
        var big = storage.Items[0];

        storage.Open(big);
        await storage.WaitForScanAsync();
        Assert.Equal(["big"], storage.Trail.Select(item => item.Name));
        Assert.Equal(["inner", "b.bin"], storage.Items.Select(item => item.Name));

        storage.Back();
        // Sizes are cached, so nothing is left to measure.
        Assert.False(storage.IsScanning);
        Assert.Empty(storage.Trail);
        Assert.Equal("big", storage.Items[0].Name);
        Assert.Equal(big.Size, storage.Items[0].Size);
    }

    [Fact]
    public async Task RescanningAFolderMeasuresItAgainAndStaysInIt()
    {
        var storage = new StorageAnalyzer(home, []);
        storage.ScanOverview();
        await storage.WaitForScanAsync();
        storage.Open(storage.Items[0]);
        await storage.WaitForScanAsync();

        File.WriteAllBytes(Path.Combine(home, "big", "new.bin"), [1]);
        storage.Rescan();
        await storage.WaitForScanAsync();
        Assert.Equal(["big"], storage.Trail.Select(item => item.Name));
        Assert.Contains(storage.Items, item => item.Name == "new.bin");
    }

    [Fact]
    public void FilesCannotBeOpened()
    {
        var storage = new StorageAnalyzer(home, []);
        storage.Open(new StorageItem(Path.Combine(home, "note.txt"), "note.txt", IsDirectory: false));
        Assert.Empty(storage.Trail);
    }
}
