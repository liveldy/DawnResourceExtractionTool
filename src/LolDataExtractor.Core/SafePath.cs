namespace LolDataExtractor.Core;

/// <summary>
/// Windows 经典路径上限是 260 字符，而英雄联盟里存在几百字符的合成皮肤文件名。
/// 超长时截断文件名并附上哈希后缀（仍然唯一、可打开），必要时整体退到 _long 目录。
/// </summary>
public static class SafePath
{
    /// <summary>留出余量给系统与临时文件。</summary>
    public const int MaxPathLength = 240;

    public static string Fit(string outputRoot, string wadFolder, string relativePath)
    {
        var full = Combine(outputRoot, wadFolder, relativePath);
        if (full.Length <= MaxPathLength) return full;

        var fileName = Path.GetFileName(relativePath);
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var token = "_" + PathHashing.ToText(PathHashing.Compute(relativePath))[..10];

        var head = Combine(outputRoot, wadFolder, directory);
        var budget = MaxPathLength - head.Length - 1 - token.Length - extension.Length;
        if (budget >= 8)
        {
            var trimmed = stem.Length <= budget ? stem : stem[..budget];
            return Path.Combine(head, trimmed + token + extension);
        }

        // 目录本身就太长：整体退化成 _long\<哈希>.<扩展名>
        var fallback = Path.Combine(outputRoot, wadFolder, "_long");
        return Path.Combine(fallback, token[1..] + (extension.Length > 0 ? extension : ".bin"));
    }

    private static string Combine(string outputRoot, string wadFolder, string relative)
        => string.IsNullOrEmpty(wadFolder)
            ? Path.Combine(outputRoot, relative)
            : Path.Combine(outputRoot, wadFolder, relative);
}
