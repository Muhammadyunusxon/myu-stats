using System.Text;
using System.Text.Json;

namespace MYUStats;

/// <summary>
/// Every user setting, with the macOS app's names and defaults. Setters raise <see cref="Changed"/>
/// only for real changes; <see cref="SettingsFile"/> reads and writes them as JSON.
/// </summary>
public sealed class AppSettings
{
    public const double DefaultHideDelay = 0.45;
    public const double DefaultDarkness = 0.28;
    public const double MaxDarkness = 0.6;
    public const double MinHideDelay = 0.2;
    public const double MaxHideDelay = 2;
    /// <summary>The sampling intervals offered in Settings, in seconds.</summary>
    public static IReadOnlyList<double> SampleIntervals { get; } = [1, 2, 5];

    public const double DefaultClaudeThreshold = 0.8;
    public const double DefaultDiskFreeGB = 10;
    public const double DefaultTemperatureLimit = 90;
    public const double DefaultProcessMinutes = 5;
    public static IReadOnlyList<double> ClaudeThresholds { get; } = [0.7, 0.8, 0.9, 0.95];
    public static IReadOnlyList<double> DiskFreeChoices { get; } = [5, 10, 20, 50];
    public static IReadOnlyList<double> TemperatureChoices { get; } = [80, 85, 90, 95];
    public static IReadOnlyList<double> ProcessMinuteChoices { get; } = [2, 5, 10, 15];

    /// <summary>Raised after a change, with the property's name, or "*" after <see cref="Reset"/>.</summary>
    public event Action<string>? Changed;

    private ScreenEdge edge = ScreenEdge.Right;
    private HashSet<StatMetric> hidden = [];
    private IReadOnlyList<StatMetric> order = StatMetrics.All;
    private bool autoHide = true;
    private double hideDelay = DefaultHideDelay;
    private SurfaceStyle surface = SurfaceStyle.Acrylic;
    private double darkness = DefaultDarkness;
    private double sampleInterval = 1;
    private bool showProcesses = true;
    private double offset;
    private string display = "";
    private string language = L.System;
    private bool alertClaude;
    private double alertClaudeThreshold = DefaultClaudeThreshold;
    private bool alertDisk;
    private double alertDiskFreeGB = DefaultDiskFreeGB;
    private bool alertTemperature;
    private double alertTemperatureLimit = DefaultTemperatureLimit;
    private bool alertProcess;
    private double alertProcessMinutes = DefaultProcessMinutes;
    private bool alertMemory;

    public ScreenEdge Edge
    {
        get => edge;
        set => Set(ref edge, value);
    }

    public IReadOnlySet<StatMetric> Hidden
    {
        get => hidden;
        set
        {
            if (hidden.SetEquals(value)) return;
            hidden = [.. value];
            Raise();
        }
    }

    /// <summary>The user's ring order; metrics added in later versions are appended in their default place.</summary>
    public IReadOnlyList<StatMetric> Order
    {
        get => order;
        set
        {
            var normalized = NormalizeOrder(value);
            if (order.SequenceEqual(normalized)) return;
            order = normalized;
            Raise();
        }
    }

    /// <summary>With auto-hide, only a thin tab stays on the edge until the pointer touches it.</summary>
    public bool AutoHide
    {
        get => autoHide;
        set => Set(ref autoHide, value);
    }

    /// <summary>Seconds the pill stays open after the pointer leaves it.</summary>
    public double HideDelay
    {
        get => hideDelay;
        set => Set(ref hideDelay, Math.Clamp(value, MinHideDelay, MaxHideDelay));
    }

    public SurfaceStyle Surface
    {
        get => surface;
        set => Set(ref surface, value);
    }

    /// <summary>Tint over the acrylic, from 0 to <see cref="MaxDarkness"/>.</summary>
    public double Darkness
    {
        get => darkness;
        set => Set(ref darkness, Math.Clamp(value, 0, MaxDarkness));
    }

    /// <summary>Seconds between samples.</summary>
    public double SampleInterval
    {
        get => sampleInterval;
        set => Set(ref sampleInterval, value > 0 ? value : 1);
    }

    public bool ShowProcesses
    {
        get => showProcesses;
        set => Set(ref showProcesses, value);
    }

    /// <summary>Nudge of the pill along its edge, in DIPs: down on the side edges, right on the top and bottom.</summary>
    public double Offset
    {
        get => offset;
        set => Set(ref offset, value);
    }

    /// <summary>Device path of the display the pill sits on; empty means the main display.</summary>
    public string Display
    {
        get => display;
        set => Set(ref display, value);
    }

    /// <summary>"" follows Windows' display language; "en" or "uz" pick one.</summary>
    public string Language
    {
        get => language;
        set => Set(ref language, value is L.English or L.Uzbek ? value : L.System);
    }

    // Alerts, each off until turned on in Settings › Alerts; see AlertSettings.

    public bool AlertClaude
    {
        get => alertClaude;
        set => Set(ref alertClaude, value);
    }

    /// <summary>Share of a Claude Code limit, 0 to 1.</summary>
    public double AlertClaudeThreshold
    {
        get => alertClaudeThreshold;
        set => Set(ref alertClaudeThreshold, Math.Clamp(value, 0.05, 1));
    }

    public bool AlertDisk
    {
        get => alertDisk;
        set => Set(ref alertDisk, value);
    }

    public double AlertDiskFreeGB
    {
        get => alertDiskFreeGB;
        set => Set(ref alertDiskFreeGB, value > 0 ? value : DefaultDiskFreeGB);
    }

    public bool AlertTemperature
    {
        get => alertTemperature;
        set => Set(ref alertTemperature, value);
    }

    public double AlertTemperatureLimit
    {
        get => alertTemperatureLimit;
        set => Set(ref alertTemperatureLimit, value > 0 ? value : DefaultTemperatureLimit);
    }

    public bool AlertProcess
    {
        get => alertProcess;
        set => Set(ref alertProcess, value);
    }

    public double AlertProcessMinutes
    {
        get => alertProcessMinutes;
        set => Set(ref alertProcessMinutes, value > 0 ? value : DefaultProcessMinutes);
    }

    public bool AlertMemory
    {
        get => alertMemory;
        set => Set(ref alertMemory, value);
    }

    /// <summary>
    /// Known metrics in the saved order, each once. A metric the saved order does not name (one added in
    /// a later version) goes after the last of the metrics that come before it by default, so a new GPU
    /// ring lands next to the CPU ring rather than at the end.
    /// </summary>
    public static IReadOnlyList<StatMetric> NormalizeOrder(IEnumerable<StatMetric> saved)
    {
        var result = saved.Distinct().ToList();
        var all = StatMetrics.All;
        for (var index = 0; index < all.Count; index++)
        {
            if (result.Contains(all[index])) continue;
            var position = all.Take(index).Select(metric => result.IndexOf(metric)).DefaultIfEmpty(-1).Max() + 1;
            result.Insert(position, all[index]);
        }
        return result;
    }

    /// <summary>Puts every setting back to its default.</summary>
    public void Reset()
    {
        edge = ScreenEdge.Right;
        hidden = [];
        order = StatMetrics.All;
        autoHide = true;
        hideDelay = DefaultHideDelay;
        surface = SurfaceStyle.Acrylic;
        darkness = DefaultDarkness;
        sampleInterval = 1;
        showProcesses = true;
        offset = 0;
        display = "";
        language = L.System;
        alertClaude = false;
        alertClaudeThreshold = DefaultClaudeThreshold;
        alertDisk = false;
        alertDiskFreeGB = DefaultDiskFreeGB;
        alertTemperature = false;
        alertTemperatureLimit = DefaultTemperatureLimit;
        alertProcess = false;
        alertProcessMinutes = DefaultProcessMinutes;
        alertMemory = false;
        Changed?.Invoke("*");
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(name);
    }

    private void Raise([System.Runtime.CompilerServices.CallerMemberName] string name = "") => Changed?.Invoke(name);

    // MARK: - JSON

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("edge", edge.Id());
            writer.WriteStartArray("hiddenMetrics");
            foreach (var metric in hidden.OrderBy(metric => metric.Id(), StringComparer.Ordinal)) writer.WriteStringValue(metric.Id());
            writer.WriteEndArray();
            writer.WriteStartArray("metricOrder");
            foreach (var metric in order) writer.WriteStringValue(metric.Id());
            writer.WriteEndArray();
            writer.WriteBoolean("autoHide", autoHide);
            writer.WriteNumber("hideDelay", hideDelay);
            writer.WriteString("surfaceStyle", surface.Id());
            writer.WriteNumber("glassDarkness", darkness);
            writer.WriteNumber("sampleInterval", sampleInterval);
            writer.WriteBoolean("showProcesses", showProcesses);
            writer.WriteNumber("verticalOffset", offset);
            writer.WriteString("display", display);
            writer.WriteString("language", language);
            writer.WriteBoolean("alertClaude", alertClaude);
            writer.WriteNumber("alertClaudeThreshold", alertClaudeThreshold);
            writer.WriteBoolean("alertDisk", alertDisk);
            writer.WriteNumber("alertDiskFreeGB", alertDiskFreeGB);
            writer.WriteBoolean("alertTemperature", alertTemperature);
            writer.WriteNumber("alertTemperatureLimit", alertTemperatureLimit);
            writer.WriteBoolean("alertProcess", alertProcess);
            writer.WriteNumber("alertProcessMinutes", alertProcessMinutes);
            writer.WriteBoolean("alertMemory", alertMemory);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads what it recognises and keeps the default for anything missing, unknown or mistyped.</summary>
    public static AppSettings FromJson(string json)
    {
        var settings = new AppSettings();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return settings;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return settings;

            if (ReadString(root, "edge") is { } edgeId && ScreenEdges.FromId(edgeId) is { } savedEdge) settings.edge = savedEdge;
            if (ReadStrings(root, "hiddenMetrics") is { } hiddenIds)
            {
                settings.hidden = [.. hiddenIds.Select(StatMetrics.FromId).OfType<StatMetric>()];
            }
            if (ReadStrings(root, "metricOrder") is { } orderIds)
            {
                settings.order = NormalizeOrder(orderIds.Select(StatMetrics.FromId).OfType<StatMetric>());
            }
            if (ReadBool(root, "autoHide") is { } savedAutoHide) settings.autoHide = savedAutoHide;
            if (ReadNumber(root, "hideDelay") is { } savedDelay) settings.hideDelay = Math.Clamp(savedDelay, MinHideDelay, MaxHideDelay);
            if (ReadString(root, "surfaceStyle") is { } styleId && SurfaceStyles.FromId(styleId) is { } style) settings.surface = style;
            if (ReadNumber(root, "glassDarkness") is { } savedDarkness) settings.darkness = Math.Clamp(savedDarkness, 0, MaxDarkness);
            if (ReadNumber(root, "sampleInterval") is { } interval && interval > 0) settings.sampleInterval = interval;
            if (ReadBool(root, "showProcesses") is { } processes) settings.showProcesses = processes;
            if (ReadNumber(root, "verticalOffset") is { } savedOffset) settings.offset = savedOffset;
            if (ReadString(root, "display") is { } savedDisplay) settings.display = savedDisplay;
            if (ReadString(root, "language") is { } savedLanguage && savedLanguage is L.English or L.Uzbek) settings.language = savedLanguage;
            if (ReadBool(root, "alertClaude") is { } claude) settings.alertClaude = claude;
            if (ReadNumber(root, "alertClaudeThreshold") is { } threshold && threshold is > 0 and <= 1) settings.alertClaudeThreshold = threshold;
            if (ReadBool(root, "alertDisk") is { } disk) settings.alertDisk = disk;
            if (ReadNumber(root, "alertDiskFreeGB") is { } free && free > 0) settings.alertDiskFreeGB = free;
            if (ReadBool(root, "alertTemperature") is { } temperature) settings.alertTemperature = temperature;
            if (ReadNumber(root, "alertTemperatureLimit") is { } limit && limit > 0) settings.alertTemperatureLimit = limit;
            if (ReadBool(root, "alertProcess") is { } process) settings.alertProcess = process;
            if (ReadNumber(root, "alertProcessMinutes") is { } minutes && minutes > 0) settings.alertProcessMinutes = minutes;
            if (ReadBool(root, "alertMemory") is { } memory) settings.alertMemory = memory;
        }
        return settings;
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? ReadNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            ? number
            : null;

    private static IEnumerable<string?>? ReadStrings(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).ToList()
            : null;
}
