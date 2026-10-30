using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>Locates the sample wads in <c>tests/testdata</c>; the integration tests skip when they are absent.</summary>
internal static class TestData
{
    private static readonly Lazy<string> RootDirectory = new(FindRepositoryRoot);

    public static string? Wad(string fileName)
    {
        var path = Path.Combine(RootDirectory.Value, "tests", "testdata", fileName);
        return File.Exists(path) ? path : null;
    }

    public static byte[] ReadEntry(string wadPath, ulong hash)
    {
        using var archive = WadArchive.Open(wadPath);
        using var stream = archive.OpenDecompressed(hash);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static ulong Hash(string path) => PathHashing.Compute(path);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LolDataExtractor.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
