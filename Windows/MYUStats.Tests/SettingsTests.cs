namespace MYUStats.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults()
    {
        var settings = new AppSettings();
        Assert.Equal(ScreenEdge.Right, settings.Edge);
        Assert.Empty(settings.Hidden);
        Assert.Equal(StatMetrics.All, settings.Order);
        Assert.True(settings.AutoHide);
        Assert.Equal(0, settings.Offset);
        Assert.Equal(1, settings.SampleInterval);
        Assert.Equal(L.System, settings.Language);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var settings = new AppSettings
        {
            Edge = ScreenEdge.Left,
            Hidden = new HashSet<StatMetric> { StatMetric.Disk, StatMetric.Battery },
            Order = [StatMetric.Claude, StatMetric.Cpu],
            AutoHide = false,
            HideDelay = 1.25,
            Surface = SurfaceStyle.Solid,
            Darkness = 0.4,
            SampleInterval = 2,
            ShowProcesses = false,
            Offset = -120,
            Display = @"\\?\DISPLAY#DEL4165#5&1a2b3c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            Language = L.Uzbek,
        };
        var copy = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(ScreenEdge.Left, copy.Edge);
        Assert.True(copy.Hidden.SetEquals([StatMetric.Disk, StatMetric.Battery]));
        Assert.Equal(StatMetric.Claude, copy.Order[0]);
        Assert.Equal(StatMetric.Cpu, copy.Order[1]);
        Assert.Equal(StatMetrics.All.Count, copy.Order.Count);
        Assert.False(copy.AutoHide);
        Assert.Equal(1.25, copy.HideDelay);
        Assert.Equal(SurfaceStyle.Solid, copy.Surface);
        Assert.Equal(0.4, copy.Darkness);
        Assert.Equal(2, copy.SampleInterval);
        Assert.False(copy.ShowProcesses);
        Assert.Equal(-120, copy.Offset);
        Assert.Equal(settings.Display, copy.Display);
        Assert.Equal(L.Uzbek, copy.Language);
    }

    [Fact]
    public void AMetricTheSavedOrderLacksGoesAfterTheMetricsBeforeItByDefault()
    {
        // As if saved by a version that did not know about the GPU, thermals and Claude Code yet.
        var settings = AppSettings.FromJson("""{ "metricOrder": ["battery", "cpu", "memory", "network", "disk"] }""");
        Assert.Equal(
            [StatMetric.Battery, StatMetric.Cpu, StatMetric.Gpu, StatMetric.Memory, StatMetric.Network, StatMetric.Disk, StatMetric.Thermals, StatMetric.Claude],
            settings.Order);
        // Someone who moved the CPU ring down still gets the GPU ring next to it.
        var moved = AppSettings.FromJson("""{ "metricOrder": ["memory", "network", "cpu", "disk", "thermals", "battery", "claude"] }""");
        Assert.Equal(
            [StatMetric.Memory, StatMetric.Network, StatMetric.Cpu, StatMetric.Gpu, StatMetric.Disk, StatMetric.Thermals, StatMetric.Battery, StatMetric.Claude],
            moved.Order);
    }

    [Fact]
    public void AlertsAreOffUntilTurnedOnAndRoundTrip()
    {
        Assert.False(AlertSettings.From(new AppSettings()).IsAnyOn);
        var settings = new AppSettings { AlertDisk = true, AlertDiskFreeGB = 20, AlertClaude = true, AlertProcess = true, AlertProcessMinutes = 10 };
        var copy = AppSettings.FromJson(settings.ToJson());
        var alerts = AlertSettings.From(copy);
        Assert.Equal(20L << 30, alerts.DiskFreeBytes);
        Assert.Equal(AppSettings.DefaultClaudeThreshold, alerts.ClaudeThreshold);
        Assert.Equal(10, alerts.ProcessMinutes);
        Assert.Null(alerts.TemperatureLimit);
        Assert.False(alerts.Memory);
        copy.Reset();
        Assert.False(AlertSettings.From(copy).IsAnyOn);
    }

    [Fact]
    public void UnknownOrMistypedValuesKeepTheirDefaults()
    {
        var settings = AppSettings.FromJson("""
            { "edge": "diagonal", "metricOrder": ["fan", "cpu", "cpu"], "hiddenMetrics": ["fan", 3],
              "autoHide": "yes", "hideDelay": 99, "surfaceStyle": "glass", "sampleInterval": -1, "language": "fr" }
            """);
        Assert.Equal(ScreenEdge.Right, settings.Edge);
        Assert.Equal(StatMetric.Cpu, settings.Order[0]);
        Assert.Equal(StatMetrics.All.Count, settings.Order.Count);
        Assert.Empty(settings.Hidden);
        Assert.True(settings.AutoHide);
        Assert.Equal(AppSettings.MaxHideDelay, settings.HideDelay);
        Assert.Equal(SurfaceStyle.Acrylic, settings.Surface);
        Assert.Equal(1, settings.SampleInterval);
        Assert.Equal(L.System, settings.Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void BrokenFilesGiveDefaults(string json) => Assert.Equal(ScreenEdge.Right, AppSettings.FromJson(json).Edge);

    [Fact]
    public void ChangesAreRaisedOnlyForRealChanges()
    {
        var settings = new AppSettings();
        var changes = new List<string>();
        settings.Changed += changes.Add;
        settings.Edge = ScreenEdge.Right;
        settings.Edge = ScreenEdge.Top;
        settings.Hidden = new HashSet<StatMetric>();
        settings.Hidden = new HashSet<StatMetric> { StatMetric.Disk };
        settings.Order = StatMetrics.All;
        Assert.Equal(["Edge", "Hidden"], changes);
    }

    [Fact]
    public void ResetClearsEverySettingBackToItsDefault()
    {
        var settings = new AppSettings { Edge = ScreenEdge.Left, Hidden = new HashSet<StatMetric> { StatMetric.Disk }, SampleInterval = 2, Display = "x" };
        var changes = new List<string>();
        settings.Changed += changes.Add;
        settings.Reset();
        Assert.Equal(ScreenEdge.Right, settings.Edge);
        Assert.Empty(settings.Hidden);
        Assert.Equal(1, settings.SampleInterval);
        Assert.Equal("", settings.Display);
        Assert.Equal(["*"], changes);
    }

    [Fact]
    public void SavesAndLoadsAFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MYUStatsTests-" + Guid.NewGuid());
        var path = Path.Combine(folder, "settings.json");
        try
        {
            Assert.Equal(ScreenEdge.Right, SettingsFile.Load(path).Edge);
            SettingsFile.Save(new AppSettings { Edge = ScreenEdge.Bottom }, path);
            Assert.Equal(ScreenEdge.Bottom, SettingsFile.Load(path).Edge);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void DisplayChoiceFallsBackToTheMainDisplay()
    {
        Assert.Equal(0, DisplayChoice.Index("", ["A", "B"]));
        Assert.Equal(1, DisplayChoice.Index("B", ["A", "B"]));
        Assert.Equal(0, DisplayChoice.Index("C", ["A", null]));
        Assert.Null(DisplayChoice.Index("A", []));
    }
}
