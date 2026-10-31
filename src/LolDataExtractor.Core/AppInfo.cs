using System.Globalization;
using System.Reflection;

namespace LolDataExtractor.Core;

/// <summary>
/// Version and release date of the running build, taken from the assembly attributes that
/// <c>Directory.Build.props</c> feeds, so a version bump never leaves the UI showing an old number.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    /// <summary>Release date as <c>yyyy-MM-dd</c>.</summary>
    public static string ReleaseDate { get; } = ReadReleaseDate();

    /// <summary>One line for the header: "版本 1.0.2 · 2026-10-30 发布".</summary>
    public static string Summary => $"版本 {Version} · {ReleaseDate} 发布";

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // "1.0.2+<commit>" when source revision information is present.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "1.0.2" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string ReadReleaseDate()
    {
        var value = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ReleaseDate")?.Value;

        if (string.IsNullOrWhiteSpace(value)) return "未标注日期";

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;
    }
}
