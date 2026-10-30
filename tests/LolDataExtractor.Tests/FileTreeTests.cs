using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class FileTreeTests
{
    private static WadEntryInfo Entry(string? path, long size = 100)
        => new(PathHashing.Compute(path ?? Guid.NewGuid().ToString("N")), path, size, (int)size, "Uncompressed", false);

    [Fact]
    public void Groups_entries_by_category_then_folder()
    {
        var roots = FileTree.Build(new[]
        {
            Entry("assets/characters/ahri/ahri.skn", 300),
            Entry("assets/characters/ahri/ahri.tex", 200),
            Entry("assets/sounds/ahri.bnk", 100),
        });

        Assert.Equal(new[] { "纹理", "模型", "音频" }, roots.Select(r => r.Name));
        Assert.Equal(new[] { "texture", "model", "audio" }, roots.Select(r => r.Category));

        var texture = roots[0];
        Assert.Equal(1, texture.FileCount);
        Assert.Equal(200, texture.TotalSize);

        var model = roots[1];
        Assert.Equal(1, model.FileCount);
        var characters = Assert.Single(model.Children);
        Assert.Equal("assets", characters.Name);
        Assert.Equal(300, characters.TotalSize);
    }

    [Fact]
    public void Counts_roll_up_through_every_folder()
    {
        var roots = FileTree.Build(new[]
        {
            Entry("a/b/c/one.tex", 10),
            Entry("a/b/c/two.tex", 20),
            Entry("a/b/three.tex", 30),
        });

        var root = Assert.Single(roots);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(60, root.TotalSize);

        var a = Assert.Single(root.Children);
        Assert.Equal(3, a.FileCount);
        var b = Assert.Single(a.Children);
        Assert.Equal(3, b.FileCount);
        Assert.Equal(60, b.TotalSize);

        // "a/b" holds the nested folder c plus its own file.
        Assert.Equal(new[] { "c", "three.tex" }, b.Children.Select(child => child.Name));
        var c = b.Children[0];
        Assert.Equal(2, c.FileCount);
        Assert.Equal(30, c.TotalSize);
    }

    [Fact]
    public void Folders_sort_before_files_and_names_are_ordered()
    {
        var roots = FileTree.Build(new[]
        {
            Entry("src/zeta.tex"),
            Entry("src/alpha.tex"),
            Entry("src/nested/deep.tex"),
        });

        var src = Assert.Single(roots).Children[0];
        Assert.Equal("nested", src.Children[0].Name);
        Assert.True(src.Children[0].IsFolder);
        Assert.Equal(new[] { "alpha.tex", "zeta.tex" }, src.Children.Skip(1).Select(c => c.Name));
    }

    [Fact]
    public void Unnamed_entries_get_their_own_root()
    {
        var roots = FileTree.Build(new[] { Entry(null), Entry(null) });

        var root = Assert.Single(roots);
        Assert.False(string.IsNullOrEmpty(root.Name));
        Assert.Equal(2, root.FileCount);
        Assert.All(root.Children, child => Assert.Equal(2, child.Name.Length));
    }

    [Fact]
    public void Empty_input_produces_no_roots()
        => Assert.Empty(FileTree.Build(Array.Empty<WadEntryInfo>()));
}
