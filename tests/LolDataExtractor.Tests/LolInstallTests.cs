using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class LolInstallTests : IDisposable
{
    private readonly string _root;

    public LolInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        var final = Path.Combine(_root, "Game", "DATA", "FINAL");
        Directory.CreateDirectory(Path.Combine(final, "Champions"));
        File.WriteAllBytes(Path.Combine(final, "DATA.wad.client"), new byte[8]);
        File.WriteAllBytes(Path.Combine(final, "Champions", "Ahri.wad.client"), new byte[8]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Normalizes_the_data_folder()
        => Assert.Equal(Path.Combine(_root, "Game", "DATA"), LolInstall.Normalize(Path.Combine(_root, "Game", "DATA")));

    [Fact]
    public void Normalizes_the_game_folder()
        => Assert.Equal(Path.Combine(_root, "Game", "DATA"), LolInstall.Normalize(Path.Combine(_root, "Game")));

    [Fact]
    public void Normalizes_the_install_folder()
        => Assert.Equal(Path.Combine(_root, "Game", "DATA"), LolInstall.Normalize(_root));

    [Fact]
    public void Normalizes_the_final_folder()
        => Assert.Equal(Path.Combine(_root, "Game", "DATA"), LolInstall.Normalize(Path.Combine(_root, "Game", "DATA", "FINAL")));

    [Fact]
    public void Returns_null_for_an_unrelated_folder()
    {
        var unrelated = Path.Combine(Path.GetTempPath(), "loldata-unrelated", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unrelated);
        try
        {
            Assert.Null(LolInstall.Normalize(unrelated));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }
    }

    [Fact]
    public void Enumerates_every_wad_below_the_data_folder()
    {
        var wads = LolInstall.EnumerateWadFiles(Path.Combine(_root, "Game", "DATA"));
        Assert.Equal(2, wads.Count);
        Assert.Contains(wads, w => w.EndsWith("Ahri.wad.client", StringComparison.Ordinal));
    }

    [Fact]
    public void Relative_names_use_forward_slashes()
    {
        var wad = Path.Combine(_root, "Game", "DATA", "FINAL", "Champions", "Ahri.wad.client");
        Assert.Equal("FINAL/Champions/Ahri.wad.client", LolInstall.GetRelativeName(Path.Combine(_root, "Game", "DATA"), wad));
    }

    [Fact]
    public void TryLocate_keeps_an_unusable_hint_instead_of_falling_back()
    {
        // The user typed something that is not a DATA folder: report it, never jump elsewhere.
        var unrelated = Path.Combine(Path.GetTempPath(), "loldata-unrelated", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unrelated);
        try
        {
            Assert.Null(LolInstall.TryLocate(unrelated));
            Assert.Null(LolInstall.TryLocate(Path.Combine(unrelated, "does-not-exist")));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }
    }

    [Fact]
    public void TryLocate_forwards_a_hint_to_the_normalizer()
    {
        var found = LolInstall.TryLocate(Path.Combine(_root, "Game"), extraCandidates: Array.Empty<string>());
        Assert.Equal(Path.Combine(_root, "Game", "DATA"), found);
    }

    [Fact]
    public void TryLocate_probes_explicit_candidates_without_a_hint()
    {
        var found = LolInstall.TryLocate(hint: null, extraCandidates: new[] { Path.Combine(_root, "nope"), _root });
        Assert.Equal(Path.Combine(_root, "Game", "DATA"), found);
    }
}
