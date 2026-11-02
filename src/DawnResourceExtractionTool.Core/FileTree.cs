using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// One node of the browsable asset tree: a category root, a folder or a file leaf.
/// Nodes are plain data; the UI adds its own selection and expansion state.
/// </summary>
public sealed class FileTreeNode
{
    private FileTreeNode(string name, bool isFolder, WadEntryInfo? entry, string? category)
    {
        Name = name;
        IsFolder = isFolder;
        Entry = entry;
        Category = category;
    }

    public string Name { get; }

    /// <summary>Asset path below the category root, e.g. <c>assets/characters/ahri/skins/base</c>.</summary>
    public string Path { get; internal set; } = string.Empty;

    /// <summary>Category key ("texture", "model", …) for the roots produced by <see cref="FileTree.Build"/>.</summary>
    public string? Category { get; }

    public bool IsFolder { get; }

    public WadEntryInfo? Entry { get; }

    public List<FileTreeNode> Children { get; } = new();

    /// <summary>Number of files at or below this node.</summary>
    public int FileCount { get; internal set; }

    /// <summary>Uncompressed size of the files at or below this node.</summary>
    public long TotalSize { get; internal set; }

    public static FileTreeNode Folder(string name) => new(name, true, null, null);

    public static FileTreeNode File(WadEntryInfo entry, string name) => new(name, false, entry, null);

    public static FileTreeNode CategoryRoot(string category, string display) => new(display, true, null, category);

    public override string ToString() => IsFolder ? $"{Name}/ ({FileCount})" : $"{Name} ({Entry?.UncompressedSize ?? 0} B)";
}

/// <summary>Groups wad entries into category → folder → file nodes for the tree view.</summary>
public static class FileTree
{
    /// <summary>Display name of a category key, shared with the entry list.</summary>
    public static string CategoryDisplay(string? category) => category switch
    {
        "texture" => Strings.T("Core.FileTree.Category.Texture"),
        "model" => Strings.T("Core.FileTree.Category.Model"),
        "audio" => Strings.T("Core.FileTree.Category.Audio"),
        "bin" => Strings.T("Core.FileTree.Category.Bin"),
        "text" => Strings.T("Core.FileTree.Category.Text"),
        "shader" => Strings.T("Core.FileTree.Category.Shader"),
        "video" => Strings.T("Core.FileTree.Category.Video"),
        _ => Strings.T("Core.FileTree.Category.Other"),
    };

    /// <summary>Fixed order of the category roots, textures first because they are everywhere.</summary>
    private static readonly string[] CategoryOrder = { "texture", "model", "audio", "video", "bin", "text", "shader", "other" };

    public static IReadOnlyList<FileTreeNode> Build(IEnumerable<WadEntryInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var roots = new Dictionary<string, FileTreeNode>(StringComparer.Ordinal);
        var folders = new Dictionary<FileTreeNode, Dictionary<string, FileTreeNode>>(ReferenceEqualityComparer.Instance);
        var unnamedRoot = FileTreeNode.CategoryRoot("", Strings.T("Core.FileTree.Unnamed"));

        foreach (var entry in entries)
        {
            FileTreeNode root;
            string[] segments;

            if (entry.IsNamed)
            {
                var category = entry.Category;
                if (!roots.TryGetValue(category, out var existing))
                {
                    existing = FileTreeNode.CategoryRoot(category, CategoryDisplay(category));
                    roots[category] = existing;
                }
                root = existing;

                segments = entry.Path!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0) segments = new[] { PathHashing.ToText(entry.Hash) };
            }
            else
            {
                root = unnamedRoot;
                var hash = PathHashing.ToText(entry.Hash);
                segments = new[] { hash[..2], hash };
            }

            Add(root, folders, segments, entry);
        }

        var ordered = new List<FileTreeNode>();
        foreach (var category in CategoryOrder)
        {
            if (roots.TryGetValue(category, out var root)) ordered.Add(root);
        }
        foreach (var extra in roots.Values.Where(r => !CategoryOrder.Contains(r.Category!))) ordered.Add(extra);
        if (unnamedRoot.Children.Count > 0) ordered.Add(unnamedRoot);

        foreach (var root in ordered) Sort(root);
        return ordered;
    }

    private static void Add(FileTreeNode root, Dictionary<FileTreeNode, Dictionary<string, FileTreeNode>> folders,
        string[] segments, WadEntryInfo entry)
    {
        var node = root;
        node.FileCount++;
        node.TotalSize += entry.UncompressedSize;

        for (var i = 0; i < segments.Length; i++)
        {
            var path = node.Path.Length == 0 ? segments[i] : node.Path + "/" + segments[i];
            if (i == segments.Length - 1)
            {
                var file = FileTreeNode.File(entry, segments[i]);
                file.Path = path;
                node.Children.Add(file);
                return;
            }

            if (!folders.TryGetValue(node, out var children))
            {
                children = new Dictionary<string, FileTreeNode>(StringComparer.Ordinal);
                folders[node] = children;
            }

            if (!children.TryGetValue(segments[i], out var child))
            {
                child = FileTreeNode.Folder(segments[i]);
                child.Path = path;
                children[segments[i]] = child;
                node.Children.Add(child);
            }

            node = child;
            node.FileCount++;
            node.TotalSize += entry.UncompressedSize;
        }
    }

    private static void Sort(FileTreeNode node)
    {
        if (node.Children.Count == 0) return;
        node.Children.Sort(static (a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Children)
            if (child.IsFolder) Sort(child);
    }
}
