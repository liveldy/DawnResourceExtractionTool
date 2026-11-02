using System.Globalization;
using System.Reflection;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// Version and release date of the running build, taken from the assembly attributes that
/// <c>Directory.Build.props</c> feeds, so a version bump never leaves the UI showing an old number.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    /// <summary>Release date as <c>yyyy-MM-dd</c>.</summary>
    public static string ReleaseDate { get; } = ReadReleaseDate();

    /// <summary>One line for the header: "版本 1.1.3 · 2026-11-01 发布" / "Version 1.1.3 · released 2026-11-01".</summary>
    public static string Summary => Strings.T("App.Summary", Version, ReleaseDate);

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

        if (string.IsNullOrWhiteSpace(value)) return Strings.T("App.ReleaseDateUnknown");

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;
    }
}
