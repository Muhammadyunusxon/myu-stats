using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace MYUStats;

/// <summary>A repeating timer on the UI thread.</summary>
internal sealed class UiTimer(DispatcherQueue queue) : IUiTimer
{
    private DispatcherQueueTimer? timer;

    public void Start(TimeSpan interval, Action tick)
    {
        Stop();
        var next = queue.CreateTimer();
        next.Interval = interval;
        next.IsRepeating = true;
        next.Tick += (_, _) => tick();
        next.Start();
        timer = next;
    }

    public void Stop()
    {
        timer?.Stop();
        timer = null;
    }
}

/// <summary>A one-shot delay on the UI thread that can be restarted or cancelled.</summary>
internal sealed class UiDelay
{
    private readonly DispatcherQueueTimer timer;
    private Action? action;

    public UiDelay(DispatcherQueue queue)
    {
        timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var pending = action;
            action = null;
            pending?.Invoke();
        };
    }

    public bool IsPending => timer.IsRunning;

    public void Run(TimeSpan delay, Action then)
    {
        timer.Stop();
        action = then;
        timer.Interval = delay;
        timer.Start();
    }

    public void Cancel()
    {
        timer.Stop();
        action = null;
    }
}

/// <summary>Loads the settings once and saves them a moment after the last change.</summary>
internal sealed class SettingsService
{
    private readonly UiDelay save;

    public SettingsService(DispatcherQueue queue)
    {
        Settings = SettingsFile.Load(AppPaths.Settings);
        save = new UiDelay(queue);
        Settings.Changed += _ => save.Run(TimeSpan.FromMilliseconds(300), Save);
    }

    public AppSettings Settings { get; }

    /// <summary>Writes a pending change straight away, before quitting.</summary>
    public void Flush()
    {
        if (!save.IsPending) return;
        save.Cancel();
        Save();
    }

    private void Save() => SettingsFile.Save(Settings, AppPaths.Settings);
}

/// <summary>Starting with Windows, through the user's Run key, as Task Manager's Startup apps page lists it.</summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MYU STATS";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal static class Shell
{
    /// <summary>Opens Explorer with the item selected.</summary>
    public static void Reveal(string path) => Start("explorer.exe", $"/select,\"{path}\"");

    public static void OpenFolder(string path) => Start("explorer.exe", $"\"{path}\"");

    /// <summary>Opens a page of Windows Settings, such as "ms-settings:notifications".</summary>
    public static void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.App.Error($"Windows Settings could not be opened: {error.Message}");
        }
    }

    private static void Start(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.App.Error($"Explorer could not be opened: {error.Message}");
        }
    }
}
