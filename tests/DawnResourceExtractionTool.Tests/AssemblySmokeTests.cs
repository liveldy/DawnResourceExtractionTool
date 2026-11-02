using System.Reflection;

namespace DawnResourceExtractionTool.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Core_assembly_exposes_the_expected_product_version()
    {
        var assembly = typeof(DawnResourceExtractionTool.Core.PathHashing).Assembly;
        var version = assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal(1, version!.Major);
    }

    [Fact]
    public void Core_assembly_has_no_ui_dependency()
    {
        var referenced = typeof(DawnResourceExtractionTool.Core.PathHashing).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();
        Assert.DoesNotContain(referenced, name => name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Product_name_is_declared_for_the_core_library()
    {
        var product = typeof(DawnResourceExtractionTool.Core.PathHashing).Assembly
            .GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        Assert.Equal("破晓资源提取工具", product);
    }

    [Fact]
    public void App_info_reads_the_version_and_release_date_from_the_build()
    {
        var version = typeof(DawnResourceExtractionTool.Core.AppInfo).Assembly.GetName().Version!;

        Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", DawnResourceExtractionTool.Core.AppInfo.Version);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", DawnResourceExtractionTool.Core.AppInfo.ReleaseDate);
        Assert.Contains(DawnResourceExtractionTool.Core.AppInfo.Version, DawnResourceExtractionTool.Core.AppInfo.Summary);
        Assert.Contains(DawnResourceExtractionTool.Core.AppInfo.ReleaseDate, DawnResourceExtractionTool.Core.AppInfo.Summary);
    }
}
