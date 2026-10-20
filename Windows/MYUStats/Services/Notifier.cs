using System.Security;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.UI.Notifications;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace MYUStats;

/// <summary>
/// Windows notifications for the alerts. Windows only shows them for an app it knows, and an unpackaged
/// app has no identity of its own, so the first notification registers one under the user's registry
/// key: the app's name and icon, which is all Windows needs. A click opens the alert's page while
/// MYU STATS runs.
/// </summary>
internal sealed class Notifier(DispatcherQueue queue) : IAlertPoster
{
    private const string AppId = "Muhammadyunusxon.MYUStats";
    private const string Group = "alerts";

    /// <summary>Windows reports a click only to a notification object that is still alive.</summary>
    private readonly Dictionary<string, ToastNotification> shown = [];
    private ToastNotifier? notifier;

    /// <summary>Called on the UI thread with the page a clicked notification points at: a metric's, or the
    /// Alerts page for null.</summary>
    public event Action<StatMetric?>? Opened;

    /// <summary>Why notifications cannot show, when Windows says so; null when they can, or before the first one.</summary>
    public string? Problem
    {
        get
        {
            if (notifier is null) return null;
            try
            {
                return notifier.Setting switch
                {
                    NotificationSetting.Enabled => null,
                    NotificationSetting.DisabledForApplication => L.T("Notifications are off for MYU STATS in Windows Settings."),
                    NotificationSetting.DisabledForUser => L.T("Notifications are off in Windows Settings."),
                    _ => L.T("Windows does not allow notifications on this PC."),
                };
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Registers the app with Windows' notifications, so <see cref="Problem"/> can be read.</summary>
    public void Prepare()
    {
        try
        {
            notifier ??= Register();
        }
        catch (Exception error)
        {
            Log.App.Error($"Notifications could not be set up: {error.Message}");
        }
    }

    public void Post(Alert alert)
    {
        try
        {
            notifier ??= Register();
            var document = new XmlDocument();
            document.LoadXml(Xml(alert));
            var toast = new ToastNotification(document) { Tag = Tag(alert.Id), Group = Group };
            var metric = alert.Metric;
            toast.Activated += (_, _) => queue.TryEnqueue(() => Opened?.Invoke(metric));
            shown[toast.Tag] = toast;
            notifier.Show(toast);
        }
        catch (Exception error)
        {
            Log.App.Error($"Notification not shown: {error.GetType().Name}: {error.Message}");
        }
    }

    private static ToastNotifier Register()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            key.SetValue("DisplayName", "MYU STATS");
            key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"));
        }
        return ToastNotificationManager.CreateToastNotifier(AppId);
    }

    private static string Xml(Alert alert) => $"""
        <toast launch="{Escape(alert.Id)}">
          <visual>
            <binding template="ToastGeneric">
              <text>{Escape(alert.Title)}</text>
              <text>{Escape(alert.Body)}</text>
            </binding>
          </visual>
        </toast>
        """;

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    /// <summary>Windows takes tags of up to 64 characters.</summary>
    private static string Tag(string id) => id.Length <= 64 ? id : id[..64];
}
