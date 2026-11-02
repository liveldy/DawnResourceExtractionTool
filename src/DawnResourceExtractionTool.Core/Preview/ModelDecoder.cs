using System.Numerics;
using DawnResourceExtractionTool.Core.Localization;
using LeagueToolkit.Helpers.Structures;
using LeagueToolkit.IO.SimpleSkinFile;
using LeagueToolkit.IO.StaticObjectFile;

namespace DawnResourceExtractionTool.Core;

/// <summary>One drawable submesh, stored as plain arrays so any renderer can consume it.</summary>
public sealed record MeshPart(
    string Name,
    float[] Positions,
    float[] Normals,
    float[] TexCoords,
    int[] Indices,
    byte[]? Colors,
    byte[]? BoneIndices = null,
    float[]? Weights = null)
{
    public int VertexCount => Positions.Length / 3;

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Geometry ready for a 3D preview, with bounds computed from the vertices.</summary>
public sealed record ModelPreview(
    string Name,
    string Kind,
    IReadOnlyList<MeshPart> Parts,
    Vector3 Min,
    Vector3 Max)
{
    public int VertexCount => Parts.Sum(p => p.VertexCount);

    public int TriangleCount => Parts.Sum(p => p.TriangleCount);

    public Vector3 Center => (Min + Max) / 2f;

    public Vector3 Size => Max - Min;

    public string Describe => Strings.T("Core.Model.Describe", Parts.Count, VertexCount, TriangleCount);
}

/// <summary>Reads the two model formats the game ships (<c>.scb</c> static, <c>.skn</c> skinned).</summary>
public static class ModelDecoder
{
    /// <summary>Decodes by content signature, falling back to the extension.</summary>
    public static ModelPreview Decode(byte[] bytes, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var ext = extension?.TrimStart('.').ToLowerInvariant() ?? string.Empty;

        if (ext == "scb" || ext == "sco" || StartsWith(bytes, "r3d2Mesh"u8))
            return FromScb(bytes, ext is "sco" ? "sco" : "scb");
        if (ext == "skn" || (bytes.Length > 4 && bytes[0] == 0x33 && bytes[1] == 0x22 && bytes[2] == 0x11 && bytes[3] == 0x00))
            return FromSkn(bytes);

        throw new NotSupportedException(Strings.T("Core.Model.Unsupported",
            ext.Length == 0 ? Strings.T("Core.Model.NoExtension") : ext));
    }

    public static ModelPreview FromScb(byte[] bytes, string kind)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        StaticObject model;
        try
        {
            model = StaticObject.ReadSCB(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(Strings.T("Core.Model.StaticFailed", ex.Message), ex);
        }

        var parts = new List<MeshPart>(model.Submeshes.Count);
        foreach (var submesh in model.Submeshes)
        {
            var vertices = submesh.Vertices;
            if (vertices.Count == 0) continue;

            var positions = new float[vertices.Count * 3];
            var uvs = new float[vertices.Count * 2];
            var hasColor = vertices[0].Color.HasValue;
            var colors = hasColor ? new byte[vertices.Count * 4] : null;

            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                positions[i * 3] = vertex.Position.X;
                positions[i * 3 + 1] = vertex.Position.Y;
                positions[i * 3 + 2] = vertex.Position.Z;
                uvs[i * 2] = vertex.UV.X;
                uvs[i * 2 + 1] = vertex.UV.Y;
                if (colors is not null && vertex.Color is { } color)
                {
                    colors[i * 4] = ToByte(color.R);
                    colors[i * 4 + 1] = ToByte(color.G);
                    colors[i * 4 + 2] = ToByte(color.B);
                    colors[i * 4 + 3] = ToByte(color.A);
                }
            }

            // Static objects carry no normals; the renderer derives flat ones.
            var indices = submesh.Indices.Select(i => (int)i).ToArray();
            parts.Add(new MeshPart(submesh.Name, positions, Array.Empty<float>(), uvs, indices, colors));
        }

        return Build(kind == "sco" ? Strings.T("Core.Model.StaticSco") : Strings.T("Core.Model.StaticScb"),
            string.IsNullOrEmpty(model.Name) ? kind : model.Name, parts);
    }

    public static ModelPreview FromSkn(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        SimpleSkin skin;
        try
        {
            skin = new SimpleSkin(stream, false);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(Strings.T("Core.Model.SkinnedFailed", ex.Message), ex);
        }

        var parts = new List<MeshPart>(skin.Submeshes.Count);
        foreach (var submesh in skin.Submeshes)
        {
            var vertices = submesh.Vertices;
            if (vertices.Count == 0) continue;

            var positions = new float[vertices.Count * 3];
            var normals = new float[vertices.Count * 3];
            var uvs = new float[vertices.Count * 2];
            var hasColor = vertices[0].BaseColor.HasValue;
            var colors = hasColor ? new byte[vertices.Count * 4] : null;

            // Skinning needs the bones of every vertex: four indices plus their weights.
            var bones = new byte[vertices.Count * 4];
            var weights = new float[vertices.Count * 4];

            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                positions[i * 3] = vertex.Position.X;
                positions[i * 3 + 1] = vertex.Position.Y;
                positions[i * 3 + 2] = vertex.Position.Z;
                normals[i * 3] = vertex.Normal.X;
                normals[i * 3 + 1] = vertex.Normal.Y;
                normals[i * 3 + 2] = vertex.Normal.Z;
                uvs[i * 2] = vertex.UV.X;
                uvs[i * 2 + 1] = vertex.UV.Y;
                for (var k = 0; k < 4; k++)
                {
                    bones[i * 4 + k] = k < vertex.BoneIndices.Length ? vertex.BoneIndices[k] : (byte)0;
                    weights[i * 4 + k] = k < vertex.Weights.Length ? vertex.Weights[k] : 0f;
                }
                if (colors is not null && vertex.BaseColor is { } color)
                {
                    colors[i * 4] = ToByte(color.R);
                    colors[i * 4 + 1] = ToByte(color.G);
                    colors[i * 4 + 2] = ToByte(color.B);
                    colors[i * 4 + 3] = ToByte(color.A);
                }
            }

            var indices = submesh.Indices.Select(i => (int)i).ToArray();
            parts.Add(new MeshPart(submesh.Name, positions, normals, uvs, indices, colors, bones, weights));
        }

        return Build(Strings.T("Core.Model.Skinned"), "skn", parts);
    }

    private static ModelPreview Build(string kind, string name, List<MeshPart> parts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
        {
            for (var i = 0; i + 2 < part.Positions.Length; i += 3)
            {
                var point = new Vector3(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }

        if (parts.Count == 0) throw new InvalidDataException(Strings.T("Core.Model.NoVertices"));
        return new ModelPreview(name, kind, parts, min, max);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> magic)
        => bytes.Length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic);
}
