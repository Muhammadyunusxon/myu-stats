using System.Globalization;
using System.Text.Json;

namespace MYUStats;

/// <summary>
/// UI strings. The English text is the key, as in the macOS string catalog, and the Uzbek text comes
/// from the embedded Strings/uz.json. Anything without a translation shows in English.
/// </summary>
public static class L
{
    /// <summary>Follow Windows' display language.</summary>
    public const string System = "";
    public const string English = "en";
    public const string Uzbek = "uz";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> uzbek = new(LoadUzbek);
    private static IReadOnlyDictionary<string, string>? active;

    /// <summary>"en" or "uz": the language the UI shows now.</summary>
    public static string Current { get; private set; } = English;

    /// <summary>The Uzbek table, for tests.</summary>
    public static IReadOnlyDictionary<string, string> UzbekTable => uzbek.Value;

    /// <summary>Picks the UI language from the setting: "" follows <paramref name="system"/>, else "en" or "uz".</summary>
    public static void Use(string? setting, CultureInfo? system = null)
    {
        var code = setting is English or Uzbek
            ? setting
            : (system ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == Uzbek ? Uzbek : English;
        Current = code;
        active = code == Uzbek ? uzbek.Value : null;
    }

    /// <summary>The text in the current language.</summary>
    public static string T(string english) =>
        active is not null && active.TryGetValue(english, out var text) ? text : english;

    /// <summary>A format string in the current language, filled in with <paramref name="args"/>.</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    private static IReadOnlyDictionary<string, string> LoadUzbek()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("MYUStats.Strings.uz.json");
        if (stream is null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}
