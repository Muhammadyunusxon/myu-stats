using System.Diagnostics;
using System.Globalization;

namespace MYUStats;

/// <summary>
/// A small log file, in the categories the macOS app uses for its unified-log messages. It lives in
/// %LOCALAPPDATA%\MYU STATS\logs and rolls over to myustats.1.log at about a megabyte. Nothing is
/// written to disk until <see cref="Start"/> is called, so tests only reach the debugger output.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1 << 20;
    private static readonly Lock gate = new();
    private static string? file;

    public static LogCategory App { get; } = new("app");
    public static LogCategory Sampling { get; } = new("sampling");
    public static LogCategory Storage { get; } = new("storage");
    public static LogCategory Settings { get; } = new("settings");
    public static LogCategory Claude { get; } = new("claude");

    /// <summary>The folder the log is written to, once started.</summary>
    public static string? Folder { get; private set; }

    public static void Start(string folder)
    {
        Directory.CreateDirectory(folder);
        lock (gate)
        {
            Folder = folder;
            file = Path.Combine(folder, "myustats.log");
        }
    }

    internal static void Write(string category, string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {level}: {message}{Environment.NewLine}");
        Debug.Write(line);
        lock (gate)
        {
            if (file is null) return;
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(file, Path.ChangeExtension(file, ".1.log"), overwrite: true);
                }
                File.AppendAllText(file, line);
            }
            catch (IOException)
            {
                // A log that cannot be written must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed class LogCategory(string name)
{
    public void Info(string message) => Log.Write(name, "info", message);

    /// <summary>Something unusual that the app handles, worth a look when a user reports a problem.</summary>
    public void Notice(string message) => Log.Write(name, "notice", message);

    public void Error(string message) => Log.Write(name, "error", message);
}
