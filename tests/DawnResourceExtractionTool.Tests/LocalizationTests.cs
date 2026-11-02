using System.Text.RegularExpressions;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Tests;

/// <summary>
/// Guards the two language columns: every key exists in both languages with the same placeholders, no
/// Chinese text is left hard coded in the interface, and every key the source asks for really exists.
/// </summary>
public class LocalizationTests
{
    /// <summary>Files whose Chinese string literals are model input, not interface text.</summary>
    private static readonly string[] NotInterfaceText =
    {
        "SpeechRecognizer.cs",
    };

    /// <summary>
    /// The localization code itself writes 中文 / English as the name of a language and keeps both columns,
    /// so it is the one place where Chinese text belongs in the source.
    /// </summary>
    private static readonly string LocalizationFolder =
        Path.Combine("DawnResourceExtractionTool.Core", "Localization");

    [Fact]
    public void No_key_is_defined_twice()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (var file in LocalizationFiles())
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (seen.TryGetValue(property.Name, out var owner))
                    duplicates.Add($"{property.Name}: {Path.GetFileName(owner)} / {Path.GetFileName(file)}");
                else
                    seen[property.Name] = file;
            }
        }

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_key_speaks_both_languages()
    {
        var missing = Strings.All
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value.Chinese) || string.IsNullOrWhiteSpace(pair.Value.English))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(missing);
        Assert.True(Strings.All.Count > 100, $"只找到 {Strings.All.Count} 个本地化键");
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var offenders = new List<string>();
        foreach (var (key, value) in Strings.All)
        {
            var chinese = Placeholders(value.Chinese);
            var english = Placeholders(value.English);
            if (!chinese.SequenceEqual(english)) offenders.Add($"{key}: {string.Join(',', chinese)} / {string.Join(',', english)}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void English_text_holds_no_chinese()
    {
        var offenders = Strings.All
            .Where(pair => HasChinese(pair.Value.English))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_key_the_source_uses_exists()
    {
        var missing = new List<string>();

        foreach (var file in SourceFiles("*.cs"))
        {
            var text = StripComments(File.ReadAllText(file), lineComment: "//", blockStart: "/*", blockEnd: "*/");
            foreach (Match match in Regex.Matches(text, "Strings\\s*\\.\\s*T\\(\\s*\"([^\"]+)\""))
                if (!Strings.Has(match.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
        }

        foreach (var file in SourceFiles("*.xaml"))
        {
            var text = StripComments(File.ReadAllText(file), blockStart: "<!--", blockEnd: "-->");
            foreach (Match match in Regex.Matches(text, "\\{loc:Loc\\s+([A-Za-z0-9_.]+)"))
                if (!Strings.Has(match.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
        }

        Assert.Empty(missing.Distinct());
    }

    [Fact]
    public void No_chinese_text_is_left_in_the_interface_sources()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles("*.cs").Concat(SourceFiles("*.xaml")))
        {
            if (NotInterfaceText.Contains(Path.GetFileName(file))) continue;
            if (file.Contains(LocalizationFolder, StringComparison.OrdinalIgnoreCase)) continue;

            var text = File.ReadAllText(file);
            text = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? StripComments(text, blockStart: "<!--", blockEnd: "-->")
                : StripComments(text, lineComment: "//", blockStart: "/*", blockEnd: "*/");

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (HasChinese(lines[i])) offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Switching_the_language_changes_the_text()
    {
        var original = Strings.Language;
        try
        {
            Strings.Language = AppLanguage.Chinese;
            var chinese = Strings.T("App.Title");
            Strings.Language = AppLanguage.English;
            var english = Strings.T("App.Title");

            Assert.NotEqual(chinese, english);
            Assert.Equal(AppLanguage.English, Strings.Language);
        }
        finally
        {
            Strings.Language = original;
        }
    }

    [Fact]
    public void Language_names_parse_from_short_tags()
    {
        Assert.Equal(AppLanguage.English, AppLanguages.Parse("en"));
        Assert.Equal(AppLanguage.English, AppLanguages.Parse("en-US"));
        Assert.Equal(AppLanguage.English, AppLanguages.Parse("English"));
        Assert.Equal(AppLanguage.Chinese, AppLanguages.Parse("zh-CN"));
        Assert.Equal(AppLanguage.Chinese, AppLanguages.Parse(null));
        Assert.Equal(AppLanguage.English, AppLanguages.Other(AppLanguage.Chinese));
    }

    private static List<string> Placeholders(string text)
        => Regex.Matches(text, "\\{(\\d+)").Select(match => match.Groups[1].Value).ToList();

    private static bool HasChinese(string text)
    {
        foreach (var c in text)
            if (c is >= '\u4e00' and <= '\u9fff') return true;
        return false;
    }

    private static string StripComments(string text, string? lineComment = null, string blockStart = "/*",
        string blockEnd = "*/")
    {
        if (!string.IsNullOrEmpty(lineComment))
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var index = lines[i].IndexOf(lineComment, StringComparison.Ordinal);
                if (index >= 0) lines[i] = lines[i][..index];
            }

            text = string.Join('\n', lines);
        }

        while (true)
        {
            var start = text.IndexOf(blockStart, StringComparison.Ordinal);
            if (start < 0) return text;
            var end = text.IndexOf(blockEnd, start, StringComparison.Ordinal);
            if (end < 0) return text[..start];
            text = text[..start] + text[(end + blockEnd.Length)..];
        }
    }

    private static IEnumerable<string> LocalizationFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", LocalizationFolder), "strings.*.json");

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var src = Path.Combine(RepoRoot(), "src");
        return Directory.EnumerateFiles(src, pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
