using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class HashTableFetcherTests
{
    [Fact]
    public void Finds_downloaded_parts_in_numeric_order()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in new[] { "hashes.game.txt.10", "hashes.game.txt.2", "hashes.game.txt.0", "settings.json", "hashes.game.txt.bak" })
                File.WriteAllText(Path.Combine(directory, name), "");

            var parts = HashTableFetcher.FindTextTables(directory);

            Assert.Equal(
                new[] { "hashes.game.txt.0", "hashes.game.txt.2", "hashes.game.txt.10" },
                parts.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Includes_plain_text_tables_and_skips_missing_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "hashes.en_us.txt"), "0a1b2c3d4e5f6071 assets/foo.dds");
            File.WriteAllText(Path.Combine(directory, "hashes.game.txt.0"), "1111222233334444 assets/baz.bin");

            var files = HashTableFetcher.FindTextTables(directory);

            Assert.Equal(2, files.Count);
            Assert.Contains(Path.Combine(directory, "hashes.en_us.txt"), files);
            Assert.Empty(HashTableFetcher.FindTextTables(Path.Combine(directory, "missing")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Downloaded_parts_resolve_names_into_a_non_empty_table()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loldata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "hashes.game.txt.0"), "0a1b2c3d4e5f6071 assets/foo/bar.dds");
            File.WriteAllText(Path.Combine(directory, "hashes.game.txt.1"), "1111222233334444 assets/foo/baz.bin");

            var table = NameTable.LoadTextFiles(HashTableFetcher.FindTextTables(directory));

            Assert.Equal(2, table.Count);
            Assert.Equal("assets/foo/bar.dds", table.GetPath(0x0a1b2c3d4e5f6071UL));
            Assert.Equal("assets/foo/baz.bin", table.GetPath(0x1111222233334444UL));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
