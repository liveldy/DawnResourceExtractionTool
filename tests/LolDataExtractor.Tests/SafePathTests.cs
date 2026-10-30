using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class SafePathTests
{
    private const string Root = @"D:\LoLAssets";

    [Fact]
    public void Keeps_short_paths_untouched()
    {
        var fitted = SafePath.Fit(Root, "Ahri", @"assets\characters\ahri\skins\base\ahri_base.skn");
        Assert.Equal(Path.Combine(Root, "Ahri", @"assets\characters\ahri\skins\base\ahri_base.skn"), fitted);
    }

    [Fact]
    public void Truncates_over_long_file_names_but_keeps_them_unique()
    {
        var stem = new string('a', 400);
        var first = SafePath.Fit(Root, "Ahri", $@"data\characters\ahri\{stem}1.bin");
        var second = SafePath.Fit(Root, "Ahri", $@"data\characters\ahri\{stem}2.bin");

        Assert.True(first.Length <= SafePath.MaxPathLength, $"长度 {first.Length} 超过上限");
        Assert.True(second.Length <= SafePath.MaxPathLength);
        Assert.NotEqual(first, second);
        Assert.EndsWith(".bin", first, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Root, "Ahri", @"data\characters\ahri"), Path.GetDirectoryName(first));
    }

    [Fact]
    public void Falls_back_to_the_long_folder_when_the_directory_alone_is_too_long()
    {
        var deep = string.Join('\\', Enumerable.Repeat(new string('b', 60), 5));
        var fitted = SafePath.Fit(Root, "Ahri", $@"{deep}\file.bin");

        Assert.True(fitted.Length <= SafePath.MaxPathLength);
        Assert.Contains("_long", fitted);
        Assert.EndsWith(".bin", fitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Works_without_a_wad_folder()
    {
        var fitted = SafePath.Fit(Root, string.Empty, @"assets\a.txt");
        Assert.Equal(Path.Combine(Root, @"assets\a.txt"), fitted);
    }
}
