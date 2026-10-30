using System.Reflection;

namespace LolDataExtractor.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Core_assembly_exposes_the_expected_product_version()
    {
        var assembly = typeof(LolDataExtractor.Core.PathHashing).Assembly;
        var version = assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal(1, version!.Major);
    }

    [Fact]
    public void Core_assembly_has_no_ui_dependency()
    {
        var referenced = typeof(LolDataExtractor.Core.PathHashing).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();
        Assert.DoesNotContain(referenced, name => name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Product_name_is_declared_for_the_core_library()
    {
        var product = typeof(LolDataExtractor.Core.PathHashing).Assembly
            .GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        Assert.Equal("英雄联盟数据提取工具", product);
    }

    [Fact]
    public void App_info_reads_the_version_and_release_date_from_the_build()
    {
        var version = typeof(LolDataExtractor.Core.AppInfo).Assembly.GetName().Version!;

        Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", LolDataExtractor.Core.AppInfo.Version);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", LolDataExtractor.Core.AppInfo.ReleaseDate);
        Assert.Contains(LolDataExtractor.Core.AppInfo.Version, LolDataExtractor.Core.AppInfo.Summary);
        Assert.Contains(LolDataExtractor.Core.AppInfo.ReleaseDate, LolDataExtractor.Core.AppInfo.Summary);
    }
}
