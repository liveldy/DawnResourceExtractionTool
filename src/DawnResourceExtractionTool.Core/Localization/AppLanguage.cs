namespace DawnResourceExtractionTool.Core.Localization;

/// <summary>
/// The languages the tool speaks. Everything the user can read comes from a localization key, so a
/// new language only needs one more column in the json files below <c>Localization</c>.
/// </summary>
public enum AppLanguage
{
    /// <summary>简体中文, the default.</summary>
    Chinese,

    /// <summary>English.</summary>
    English,
}

public static class AppLanguages
{
    public static IReadOnlyList<AppLanguage> All { get; } = new[] { AppLanguage.Chinese, AppLanguage.English };

    /// <summary>Reads "zh"/"zh-CN"/"chinese"/"中文" as Chinese; anything else that names English is English.</summary>
    public static AppLanguage Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return AppLanguage.Chinese;

        var text = name.Trim();
        if (text.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return AppLanguage.English;
        if (text.Equals("english", StringComparison.OrdinalIgnoreCase)) return AppLanguage.English;
        if (text.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Chinese;
        if (text.Equals("chinese", StringComparison.OrdinalIgnoreCase) || text == "中文") return AppLanguage.Chinese;

        return AppLanguage.Chinese;
    }

    /// <summary>The other language, for the one button that flips between them.</summary>
    public static AppLanguage Other(AppLanguage language)
        => language == AppLanguage.English ? AppLanguage.Chinese : AppLanguage.English;

    /// <summary>Chinese on a Chinese Windows, English everywhere else.</summary>
    public static AppLanguage SystemDefault()
        => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Chinese
            : AppLanguage.English;

    /// <summary>BCP-47 tag, also used as the column name in the json files.</summary>
    public static string Tag(AppLanguage language)
        => language == AppLanguage.English ? "en" : "zh";

    /// <summary>Name of the language written in that language: 中文 / English.</summary>
    public static string NativeName(AppLanguage language)
        => language == AppLanguage.English ? "English" : "中文";
}
