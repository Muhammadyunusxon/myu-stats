using System.Runtime.Versioning;

namespace MYUStats.Sampling;

public static class DiskNames
{
    /// <summary>"Local Disk (C:)" when the volume has no label, as Explorer names it.</summary>
    public static string DriveTitle(string label, string letter) =>
        string.IsNullOrWhiteSpace(label) ? L.F("Local Disk ({0})", letter) : $"{label} ({letter})";
}

/// <summary>Space on the Windows drive, and on the other local drives for the Disk page.</summary>
[SupportedOSPlatform("windows")]
public static class DiskSampler
{
    public static string SystemRoot =>
        Path.GetPathRoot(Environment.SystemDirectory) is { Length: > 0 } root ? root : @"C:\";

    public static DiskUsage? Sample()
    {
        try
        {
            var drive = new DriveInfo(SystemRoot);
            return drive.IsReady ? Usage(drive) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Sampling.Error($"System drive capacity unavailable: {error.Message}");
            return null;
        }
    }

    /// <summary>Other fixed and removable drives. Network drives are left out: a disconnected one can take seconds to answer.</summary>
    public static IReadOnlyList<DiskUsage> OtherVolumes()
    {
        var result = new List<DiskUsage>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) continue;
                if (string.Equals(drive.Name, SystemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (drive.TotalSize > 0) result.Add(Usage(drive));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A drive that went away while being listed.
            }
        }
        return result;
    }

    private static DiskUsage Usage(DriveInfo drive)
    {
        string label;
        try
        {
            label = drive.VolumeLabel;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            label = "";
        }
        return new DiskUsage(DiskNames.DriveTitle(label, drive.Name.TrimEnd('\\')), drive.AvailableFreeSpace, drive.TotalSize);
    }
}
