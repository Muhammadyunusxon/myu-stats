namespace MYUStats.Tests;

public class FormatTests
{
    [Fact]
    public void Percent()
    {
        Assert.Equal("0%", Format.Percent(0));
        Assert.Equal("12%", Format.Percent(0.124));
        Assert.Equal("13%", Format.Percent(0.125));
        Assert.Equal("100%", Format.Percent(1));
    }

    [Fact]
    public void ShortRate()
    {
        Assert.Equal("0K", Format.ShortRate(0));
        Assert.Equal("<1K", Format.ShortRate(999));
        Assert.Equal("10K", Format.ShortRate(9_960));
        Assert.Equal("9.9K", Format.ShortRate(9_940));
        Assert.Equal("1.5K", Format.ShortRate(1_500));
        Assert.Equal("12K", Format.ShortRate(12_000));
        Assert.Equal("3.4M", Format.ShortRate(3_400_000));
        Assert.Equal("2.0G", Format.ShortRate(2_000_000_000));
    }

    [Fact]
    public void BytesUseBinaryUnitsLikeTaskManager()
    {
        using var culture = new CultureScope("en-US");
        Assert.Equal("512 B", Format.Bytes(512UL));
        Assert.Equal("4 KB", Format.Bytes(4096UL));
        Assert.Equal("8.5 MB", Format.Bytes((ulong)(8.5 * 1024 * 1024)));
        Assert.Equal("512 MB", Format.Bytes(512UL << 20));
        Assert.Equal("15.7 GB", Format.Bytes((ulong)(15.7 * (1 << 30))));
        Assert.Equal("466 GB", Format.Bytes(466UL << 30));
        Assert.Equal("2.0 TB", Format.Bytes(2UL << 40));
        Assert.Equal("0 B", Format.Bytes(-5L));
    }

    [Fact]
    public void BytesFollowTheRegionalDecimalSeparator()
    {
        using var culture = new CultureScope("uz-Latn-UZ");
        Assert.Equal("15,7 GB", Format.Bytes((ulong)(15.7 * (1 << 30))));
    }

    [Fact]
    public void Rate()
    {
        using var culture = new CultureScope("en-US");
        Assert.Equal("0 KB/s", Format.Rate(0));
        Assert.Equal("340 KB/s", Format.Rate(340_000));
        Assert.Equal("1.2 MB/s", Format.Rate(1_240_000));
        Assert.Equal("1.25 GB/s", Format.Rate(1_250_000_000));
    }

    [Fact]
    public void Durations()
    {
        Assert.Equal("45m", Format.Duration(45));
        Assert.Equal("2h 5m", Format.Duration(125));
        Assert.Equal("0h 59m", Format.Uptime(59 * 60));
        Assert.Equal("3d 2h", Format.Uptime(3 * 86_400 + 2 * 3_600 + 30));
    }

    [Fact]
    public void Temperature()
    {
        Assert.Equal("–", Format.Temperature(null));
        Assert.Equal("55 °C", Format.Temperature(54.6));
    }

    [Fact]
    public void TimeUntil()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        Assert.Equal("45m", Format.TimeUntil(now.AddMinutes(45), now));
        Assert.Equal("3h 20m", Format.TimeUntil(now.AddHours(3).AddMinutes(20), now));
        Assert.Equal("2d 5h", Format.TimeUntil(now.AddDays(2).AddHours(5), now));
        Assert.Equal("0m", Format.TimeUntil(now.AddMinutes(-1), now));
    }

    [Fact]
    public void UpdatedAgo()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        Assert.Equal("Updated just now", Format.UpdatedAgo(now, now));
        Assert.Equal("Updated 4 min ago", Format.UpdatedAgo(now.AddMinutes(-4), now));
        Assert.Equal("Updated 2 h ago", Format.UpdatedAgo(now.AddHours(-2), now));
    }
}

public class LevelTests
{
    [Fact]
    public void FractionsGoGreenAmberRed()
    {
        Assert.Equal(Level.Good, Level.ColorFor(0.59));
        Assert.Equal(Level.Caution, Level.ColorFor(0.6));
        Assert.Equal(Level.Caution, Level.ColorFor(0.84));
        Assert.Equal(Level.Bad, Level.ColorFor(0.85));
    }

    [Fact]
    public void BatteryIsGreenWhilePluggedIn()
    {
        Assert.Equal(Level.Good, Level.BatteryColor(new BatteryState(5, IsCharging: false, IsOnAC: true)));
        Assert.Equal(Level.Bad, Level.BatteryColor(new BatteryState(5, IsCharging: false, IsOnAC: false)));
        Assert.Equal(Level.Good, Level.BatteryColor(new BatteryState(80, IsCharging: false, IsOnAC: false)));
    }

    [Fact]
    public void Temperatures()
    {
        Assert.Equal(Level.Good, Level.TemperatureColor(55));
        Assert.Equal(Level.Caution, Level.TemperatureColor(75));
        Assert.Equal(Level.Bad, Level.TemperatureColor(95));
    }
}

public class ModelTests
{
    [Fact]
    public void CpuIdle()
    {
        var usage = new CpuUsage(0.3, 0.2);
        Assert.Equal(0.5, usage.Total, 6);
        Assert.Equal(0.5, usage.Idle, 6);
        Assert.Equal(0, new CpuUsage(0.8, 0.4).Idle);
    }

    [Fact]
    public void MemoryIsInUseWhenNotAvailable()
    {
        var memory = new MemoryUsage(16, 4, 10, 20, 2, 1, 1, MemoryPressure.Normal);
        Assert.Equal(12UL, memory.Used);
        Assert.Equal(0.75, memory.Fraction);
        Assert.Equal(0, default(MemoryUsage).Fraction);
        Assert.Equal(0UL, new MemoryUsage(8, 9, 0, 0, 0, 0, 0, MemoryPressure.Normal).Used);
    }

    [Theory]
    [InlineData(100, 50, 10, 100, false, MemoryPressure.Normal)]
    [InlineData(100, 10, 10, 100, false, MemoryPressure.Warning)]
    [InlineData(100, 50, 95, 100, false, MemoryPressure.Warning)]
    [InlineData(100, 4, 10, 100, false, MemoryPressure.Critical)]
    [InlineData(100, 50, 10, 100, true, MemoryPressure.Critical)]
    public void MemoryPressureFromWhatIsLeft(ulong total, ulong available, ulong committed, ulong limit, bool low, MemoryPressure expected) =>
        Assert.Equal(expected, MemoryPressures.From(total, available, committed, limit, low));

    [Fact]
    public void DiskDerived()
    {
        var disk = new DiskUsage("Local Disk (C:)", 25, 100);
        Assert.Equal(75, disk.Used);
        Assert.Equal(0.75, disk.UsedFraction);
        Assert.Equal(0, new DiskUsage("x", 0, 0).UsedFraction);
    }

    [Fact]
    public void ThermalHeadlineIsTheHottestZone()
    {
        var state = new ThermalState([new ThermalZone("TZ00", 48), new ThermalZone("CPUZ", 71.5)]);
        Assert.Equal(71.5, state.Headline);
        Assert.Equal(59.75, state.Average);
        Assert.Null(new ThermalState([]).Headline);
    }

    [Fact]
    public void ThermalStatesCompareByValue()
    {
        Assert.Equal(new ThermalState([new ThermalZone("A", 40)]), new ThermalState([new ThermalZone("A", 40)]));
        Assert.NotEqual(new ThermalState([new ThermalZone("A", 40)]), new ThermalState([new ThermalZone("A", 41)]));
    }

    [Fact]
    public void CoresDescription()
    {
        Assert.Equal("6P + 8E", new CpuInfo("Intel", 6, 8, 14, 20).CoresDescription);
        Assert.Equal("8 cores", new CpuInfo("AMD", 0, 0, 8, 16).CoresDescription);
        Assert.Equal("12 cores", new CpuInfo("", 0, 0, 0, 12).CoresDescription);
    }
}

public class TokenAndCostFormatTests
{
    [Fact]
    public void Tokens()
    {
        Assert.Equal("845", Format.Tokens(845));
        Assert.Equal("12K", Format.Tokens(12_345));
        Assert.Equal("9.9K", Format.Tokens(9_949));
        Assert.Equal("3.4M", Format.Tokens(3_400_000));
        Assert.Equal("741M", Format.Tokens(740_850_039));
        Assert.Equal("1.0M", Format.Tokens(999_700));
        Assert.Equal("1.2B", Format.Tokens(1_200_000_000));
    }

    [Fact]
    public void Cost()
    {
        using var culture = new CultureScope("en-US");
        Assert.Equal("$278.61", Format.Cost(278.614));
        Assert.Equal("$0.00", Format.Cost(0));
        Assert.Equal("<$0.01", Format.Cost(0.001));
    }
}
