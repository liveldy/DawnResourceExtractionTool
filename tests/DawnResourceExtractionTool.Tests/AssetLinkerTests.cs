using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.Tests;

public class AssetLinkerTests
{
    private static WadEntryInfo Entry(string path)
        => new(PathHashing.Compute(path), path, 1024, 512, "Uncompressed", false);

    [Fact]
    public void Links_the_skeleton_texture_and_clips_of_a_skin()
    {
        var model = Entry("assets/characters/ahri/skins/skin14/ahri_skin14.skn");
        var entries = new[]
        {
            model,
            Entry("assets/characters/ahri/skins/skin14/ahri_skin14.skl"),
            Entry("assets/characters/ahri/skins/skin14/ahri_skin14_tx_cm.tex"),
            Entry("assets/characters/ahri/skins/skin14/ahri_skin14_tx_multi.tex"),
            Entry("assets/characters/ahri/skins/skin14/animations/idle.anm"),
            Entry("assets/characters/ahri/skins/skin14/animations/run.anm"),
            Entry("assets/characters/ahri/skins/skin14/particles/ahri_skin14_orb.tex"),
            Entry("assets/characters/ahri/skins/base/ahri_base.skl"),
            Entry("assets/characters/ahri/skins/base/animations/attack1.anm"),
            Entry("assets/characters/akali/skins/base/akali_base.skl"),
            Entry("assets/characters/akali/skins/base/animations/idle.anm"),
        };

        var linked = AssetLinker.Find(model, entries);

        Assert.Equal("ahri_skin14.skl", linked.Skeletons[0].FileName);
        Assert.Equal(2, linked.Animations.Count);
        Assert.All(linked.Animations, entry => Assert.Contains("skins/skin14/animations", entry.Path));
        Assert.Equal("ahri_skin14_tx_cm.tex", linked.Textures[0].FileName);
        Assert.DoesNotContain(linked.Textures, entry => entry.Path!.Contains("/particles/"));

        // Other champions never leak in.
        Assert.DoesNotContain(linked.Skeletons, entry => entry.Path!.Contains("/akali/"));
        Assert.DoesNotContain(linked.Animations, entry => entry.Path!.Contains("/akali/"));
    }

    [Fact]
    public void Falls_back_to_the_base_skin_animations()
    {
        var model = Entry("assets/characters/ahri/skins/skin27/ahri_skin27.skn");
        var entries = new[]
        {
            model,
            Entry("assets/characters/ahri/skins/skin27/ahri_skin27.skl"),
            Entry("assets/characters/ahri/skins/base/animations/idle.anm"),
            Entry("assets/characters/ahri/skins/base/animations/run.anm"),
        };

        var linked = AssetLinker.Find(model, entries);

        Assert.Single(linked.Skeletons);
        Assert.Equal(2, linked.Animations.Count);
        Assert.Equal("ahri_skin27.skl", linked.Skeletons[0].FileName);
    }

    [Fact]
    public void Idle_is_the_default_clip()
    {
        var model = Entry("assets/characters/ahri/skins/base/ahri_base.skn");
        var entries = new[]
        {
            model,
            Entry("assets/characters/ahri/skins/base/animations/attack1.anm"),
            Entry("assets/characters/ahri/skins/base/animations/idle.anm"),
            Entry("assets/characters/ahri/skins/base/animations/run.anm"),
        };

        var linked = AssetLinker.Find(model, entries);

        Assert.Equal("idle.anm", linked.DefaultAnimation?.FileName);
        Assert.Null(linked.DefaultSkeleton);
        Assert.Null(linked.DefaultTexture);
    }

    [Fact]
    public void Particle_models_keep_their_sibling_textures()
    {
        var model = Entry("assets/characters/ahri/skins/base/particles/ahri_base_orb.scb");
        var entries = new[]
        {
            model,
            Entry("assets/characters/ahri/skins/base/particles/ahri_base_orb.tex"),
            Entry("assets/characters/ahri/skins/base/particles/other.tex"),
        };

        var linked = AssetLinker.Find(model, entries);

        Assert.Equal(2, linked.Textures.Count);
        Assert.Equal("ahri_base_orb.tex", linked.Textures[0].FileName);
        Assert.Empty(linked.Animations);
    }

    [Fact]
    public void Unknown_assets_are_ignored()
    {
        var model = Entry("assets/characters/ahri/skins/base/ahri_base.skn");
        var entries = new[]
        {
            model,
            Entry("assets/characters/ahri/skins/base/ahri_base.bin"),
            Entry("assets/characters/ahri/skins/base/ahri_base.bnk"),
        };

        var linked = AssetLinker.Find(model, entries);

        Assert.Empty(linked.Skeletons);
        Assert.Empty(linked.Animations);
        Assert.Empty(linked.Textures);
        Assert.Contains("骨骼 0", linked.Describe);
    }
}
