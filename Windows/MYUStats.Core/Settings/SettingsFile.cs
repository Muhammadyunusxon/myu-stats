namespace MYUStats;

/// <summary>Where MYU STATS keeps its files: %LOCALAPPDATA%\MYU STATS.</summary>
public static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MYU STATS");

    public static string Settings => Path.Combine(DataFolder, "settings.json");
    public static string Logs => Path.Combine(DataFolder, "logs");

    /// <summary>The fixed working folder for `claude /usage`; see <see cref="ClaudeUsageReader"/>.</summary>
    public static string ClaudeScratch => Path.Combine(DataFolder, "claude-usage");
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON.</summary>
public static class SettingsFile
{
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? AppSettings.FromJson(File.ReadAllText(path)) : new AppSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Settings.Error($"Settings could not be read, using defaults: {error.Message}");
            return new AppSettings();
        }
    }

    /// <summary>Writes through a temporary file, so a crash mid-write never leaves half a file behind.</summary>
    public static void Save(AppSettings settings, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, settings.ToJson());
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Settings.Error($"Settings could not be saved: {error.Message}");
        }
    }
}
