using System.Text.RegularExpressions;
using ZarobitokApp.Localization;

namespace ZarobitokApp.Tests;

// Мова — глобальний статичний стан: класи, що її перемикають, не мають іти паралельно.
[Collection("Language")]
public class LocalizationTests : IDisposable
{
    public LocalizationTests() => L.Use("uk");
    public void Dispose() => L.Use("uk");   // мова — статичний стан, не лишаємо англійську іншим тестам

    private static string RepoAppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ZarobitokApp");
            if (File.Exists(Path.Combine(candidate, "ZarobitokApp.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("ZarobitokApp project folder not found above " + AppContext.BaseDirectory);
    }

    private static string UnescapeCs(string s)
        => s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

    /// <summary>Усі тексти, які код і XAML віддають на переклад.</summary>
    private static HashSet<string> UsedKeys()
    {
        var root = RepoAppDir();
        var keys = new HashSet<string>();

        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || f.EndsWith("En.cs") || f.EndsWith("L.cs")) continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"L\.(?:T|F)\(\s*""((?:[^""\\]|\\.)*)"""))
                keys.Add(UnescapeCs(m.Groups[1].Value));
        }

        foreach (var f in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{loc:T &quot;(.*?)&quot;\}"))
                keys.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));

        return keys;
    }

    [Fact]
    public void Every_text_used_in_code_and_xaml_has_an_english_translation()
    {
        var keys = UsedKeys();
        Assert.True(keys.Count > 100, $"scan looks broken: only {keys.Count} keys found");

        var missing = keys.Where(k => !En.Map.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "No English translation for:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Translations_keep_the_same_placeholders_as_the_source()
    {
        static List<string> Holes(string s) =>
            Regex.Matches(s, @"\{(\d+)(?::[^}]*)?\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();

        var broken = En.Map.Where(kv => !Holes(kv.Key).SequenceEqual(Holes(kv.Value)))
            .Select(kv => kv.Key).ToList();
        Assert.True(broken.Count == 0, "Placeholder mismatch in:\n  " + string.Join("\n  ", broken));
    }

    [Fact]
    public void No_translation_is_empty()
        => Assert.Empty(En.Map.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));

    [Fact]
    public void T_returns_the_original_when_ukrainian_is_selected_or_a_key_is_unknown()
    {
        Assert.Equal("Налаштування", L.T("Налаштування"));

        L.Use("en");
        Assert.Equal("Settings", L.T("Налаштування"));
        Assert.Equal("Текст без перекладу", L.T("Текст без перекладу"));   // не зникає
    }

    [Fact]
    public void F_translates_the_template_then_formats()
    {
        L.Use("en");
        Assert.Equal("Earned today: 12.5", L.F("Сьогодні зароблено {0}", "12.5"));
        L.Use("uk");
        Assert.Equal("Сьогодні зароблено 12.5", L.F("Сьогодні зароблено {0}", "12.5"));
    }

    [Fact]
    public void Month_and_weekday_names_follow_the_language()
    {
        Assert.Equal("Січень", L.Months[0]);
        Assert.Equal(new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд" }, L.Weekdays(sundayFirst: false));

        L.Use("en");
        Assert.Equal("January", L.Months[0]);
        Assert.Equal(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }, L.Weekdays(sundayFirst: true));
        Assert.Equal("Wed", L.DayName(DayOfWeek.Wednesday));
    }

    [Fact]
    public void Language_codes_are_normalized()
    {
        Assert.Equal("uk", L.Normalize(null));
        Assert.Equal("uk", L.Normalize("fr"));
        Assert.Equal("en", L.Normalize("en"));
        Assert.Equal("system", L.Normalize("system"));
    }
}
