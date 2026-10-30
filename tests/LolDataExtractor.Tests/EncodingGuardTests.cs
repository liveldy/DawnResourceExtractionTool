using System.Text;

namespace LolDataExtractor.Tests;

/// <summary>
/// 仓库文本文件必须是 UTF-8 无 BOM + LF，避免中文在 Windows 上出现乱码或多余差异。
/// </summary>
public class EncodingGuardTests
{
    private static readonly string[] Extensions =
    {
        ".cs", ".xaml", ".csproj", ".props", ".sln", ".md", ".json", ".editorconfig", ".gitignore", ".ps1", ".bat", ".yml",
    };

    private static IEnumerable<string> RepositoryTextFiles()
    {
        var root = TestPaths.RepositoryRoot;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "Directory.Build.props" or ".editorconfig" or ".gitignore") { yield return file; continue; }

            var extension = Path.GetExtension(file);
            if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            // Generated output: build, publish and scratch folders are not source files.
            if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("out/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("dist/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("tests/testdata/", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return file;
        }
    }

    [Fact]
    public void No_source_file_has_a_utf8_bom()
    {
        var offenders = new List<string>();
        foreach (var file in RepositoryTextFiles())
        {
            var head = new byte[3];
            using var stream = File.OpenRead(file);
            var read = stream.Read(head, 0, 3);
            if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                offenders.Add(Path.GetRelativePath(TestPaths.RepositoryRoot, file));
        }
        Assert.True(offenders.Count == 0, "存在带 BOM 的文件：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_source_file_uses_crlf()
    {
        var offenders = new List<string>();
        foreach (var file in RepositoryTextFiles())
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.AsSpan().IndexOf("\r\n"u8) >= 0)
                offenders.Add(Path.GetRelativePath(TestPaths.RepositoryRoot, file));
        }
        Assert.True(offenders.Count == 0, "存在 CRLF 换行的文件：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Files_are_valid_utf8()
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        foreach (var file in RepositoryTextFiles())
        {
            var bytes = File.ReadAllBytes(file);
            var span = bytes.AsSpan();
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) span = span[3..];
            strict.GetString(span);
        }
        Assert.True(true);
    }
}
