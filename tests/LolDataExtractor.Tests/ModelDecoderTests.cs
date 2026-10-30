using System.Numerics;
using LeagueToolkit.Helpers.Structures;
using LeagueToolkit.IO.SimpleSkinFile;
using LeagueToolkit.IO.StaticObjectFile;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class ModelDecoderTests
{
    private static SimpleSkin BuildSkin()
    {
        var vertices = new List<SimpleSkinVertex>
        {
            new(new Vector3(0, 0, 0), new byte[4], new float[4], new Vector3(0, 1, 0), new Vector2(0, 0)),
            new(new Vector3(1, 0, 0), new byte[4], new float[4], new Vector3(0, 1, 0), new Vector2(1, 0)),
            new(new Vector3(0, 2, 0), new byte[4], new float[4], new Vector3(0, 1, 0), new Vector2(0, 1)),
        };
        var submesh = new SimpleSkinSubmesh("body", new List<ushort> { 0, 1, 2 }, vertices);
        var skin = new SimpleSkin(new List<SimpleSkinSubmesh> { submesh });

        using var stream = new MemoryStream();
        skin.Write(stream);
        return skin;
    }

    [Fact]
    public void Reads_a_skinned_mesh_with_its_bounds()
    {
        var bytes = Serialize(BuildSkin());
        var model = ModelDecoder.FromSkn(bytes);

        Assert.Single(model.Parts);
        Assert.Equal("body", model.Parts[0].Name);
        Assert.Equal(3, model.Parts[0].VertexCount);
        Assert.Equal(1, model.Parts[0].TriangleCount);
        Assert.Equal(new Vector3(0, 0, 0), model.Min);
        Assert.Equal(new Vector3(1, 2, 0), model.Max);
    }

    [Fact]
    public void Reads_a_static_object_mesh()
    {
        var vertices = new List<StaticObjectVertex>
        {
            new(new Vector3(0, 0, 0), new Vector2(0, 0)),
            new(new Vector3(2, 0, 0), new Vector2(1, 0)),
            new(new Vector3(0, 0, 2), new Vector2(0, 1)),
        };
        var submesh = new StaticObjectSubmesh("lambert1", vertices, new List<uint> { 0, 1, 2 });
        var model = new StaticObject("tri", new List<StaticObjectSubmesh> { submesh }, new Vector3(0, 0, 0));

        using var stream = new MemoryStream();
        model.WriteSCB(stream, true);

        var preview = ModelDecoder.FromScb(stream.ToArray(), "scb");
        Assert.Equal("tri", preview.Name);
        Assert.Equal(3, preview.VertexCount);
        Assert.Equal(new Vector3(2, 0, 2), preview.Max);
    }

    [Fact]
    public void Detects_the_format_from_the_content()
    {
        var bytes = Serialize(BuildSkin());
        var model = ModelDecoder.Decode(bytes);
        Assert.Contains("skn", model.Kind);
    }

    [Fact]
    public void Rejects_unknown_model_data()
    {
        Assert.Throws<NotSupportedException>(() => ModelDecoder.Decode(new byte[] { 9, 9, 9, 9 }, "fbx"));
    }

    private static byte[] Serialize(SimpleSkin skin)
    {
        using var stream = new MemoryStream();
        skin.Write(stream);
        return stream.ToArray();
    }
}
