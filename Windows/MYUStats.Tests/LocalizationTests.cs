using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MYUStats.Tests;

/// <summary>The Windows twin of the macOS string catalog checks: every string the code shows has an
/// Uzbek translation with the same placeholders, and the table holds nothing the code no longer uses.</summary>
public partial class LocalizationTests
{
    [GeneratedRegex(@"L\.(?:T|F)\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Lookup();

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string WindowsFolder([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    /// <summary>Every string passed to L.T or L.F in the app and the core library.</summary>
    private static HashSet<string> UsedStrings()
    {
        var folder = WindowsFolder();
        var files = new[] { "MYUStats", "MYUStats.Core" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(folder, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var used = new HashSet<string>();
        foreach (var file in files)
        {
            foreach (Match match in Lookup().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
            }
        }
        return used;
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(match => match.Groups[1].Value).Distinct().Order()];

    [Fact]
    public void TheSourceIsFound() => Assert.True(UsedStrings().Count > 150);

    [Fact]
    public void EveryStringHasAnUzbekTranslation()
    {
        var missing = UsedStrings().Where(text => !L.UzbekTable.ContainsKey(text)).Order().ToList();
        Assert.True(missing.Count == 0, "Add to MYUStats.Core/Strings/uz.json: " + string.Join(" | ", missing));
    }

    [Fact]
    public void TranslationsKeepEveryPlaceholder()
    {
        var mismatched = L.UzbekTable
            .Where(entry => !Placeholders(entry.Key).SequenceEqual(Placeholders(entry.Value)))
            .Select(entry => entry.Key)
            .ToList();
        Assert.True(mismatched.Count == 0, "Placeholders differ: " + string.Join(" | ", mismatched));
    }

    [Fact]
    public void TheTableHoldsNothingUnused()
    {
        var used = UsedStrings();
        var stale = L.UzbekTable.Keys.Where(key => !used.Contains(key)).Order().ToList();
        Assert.True(stale.Count == 0, "No longer used, remove from uz.json: " + string.Join(" | ", stale));
    }

    [Fact]
    public void EveryTranslationFormats()
    {
        foreach (var (english, uzbek) in L.UzbekTable)
        {
            var arguments = Placeholders(english).Select(_ => (object?)"x").ToArray();
            _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, uzbek, arguments);
        }
    }
}

/// <summary>Switching the language changes global state, so these run on their own.</summary>
[Collection(nameof(LanguageSwitching))]
public class LanguageSwitchingTests
{
    [Fact]
    public void TheSettingPicksTheLanguageAndSystemFollowsWindows()
    {
        try
        {
            L.Use(L.Uzbek);
            Assert.Equal("Xotira", StatMetric.Memory.Title());
            Assert.Equal("Claude Code", StatMetric.Claude.Title());
            Assert.Equal("5 daq", Format.Duration(5));

            L.Use(L.System, System.Globalization.CultureInfo.GetCultureInfo("uz-Latn-UZ"));
            Assert.Equal(L.Uzbek, L.Current);
            L.Use(L.System, System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            Assert.Equal(L.English, L.Current);
            Assert.Equal("Memory", StatMetric.Memory.Title());
        }
        finally
        {
            L.Use(L.English);
        }
    }
}

[CollectionDefinition(nameof(LanguageSwitching), DisableParallelization = true)]
public class LanguageSwitching;
