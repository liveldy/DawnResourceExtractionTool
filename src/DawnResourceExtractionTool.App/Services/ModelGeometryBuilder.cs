using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DawnResourceExtractionTool.Core;

namespace DawnResourceExtractionTool.App.Services;

/// <summary>Turns decoded meshes into WPF 3D objects for the Helix viewport.</summary>
public static class ModelGeometryBuilder
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xB8, 0xC4, 0xD9),
        Color.FromRgb(0xD9, 0xC0, 0xB8),
        Color.FromRgb(0xB8, 0xD9, 0xC6),
        Color.FromRgb(0xD6, 0xB8, 0xD9),
        Color.FromRgb(0xD9, 0xD3, 0xB8),
    };

    /// <summary>Builds one model; <paramref name="textures"/> is indexed like <see cref="ModelPreview.Parts"/>.</summary>
    public static Model3DGroup Build(ModelPreview model, IReadOnlyList<ImageSource?>? textures = null)
    {
        var group = new Model3DGroup();

        // Two directional lights plus ambient keep the faceted look readable without
        // washing the model out; the viewport adds no lights of its own.
        group.Children.Add(new AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0xF0, 0xF0, 0xF0), new Vector3D(-0.5, -0.7, -0.9)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x60, 0x60, 0x60), new Vector3D(0.8, 0.4, 0.5)));

        for (var i = 0; i < model.Parts.Count; i++)
        {
            var geometry = BuildGeometry(model.Parts[i]);
            if (geometry is null) continue;

            var material = MaterialFor(i, TextureAt(textures, i));
            group.Children.Add(new GeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                BackMaterial = material,
            });
        }

        group.Freeze();
        return group;
    }

    /// <summary>The texture bound to a part, if that part got one.</summary>
    public static ImageSource? TextureAt(IReadOnlyList<ImageSource?>? textures, int index)
        => textures is not null && index >= 0 && index < textures.Count ? textures[index] : null;

    /// <summary>Textured material when a texture is known, otherwise a stable palette colour per part.</summary>
    public static Material MaterialFor(int index, ImageSource? texture)
    {
        if (texture is not null)
        {
            var brush = new ImageBrush(texture)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewport = new Rect(0, 0, 1, 1),
            };
            brush.Freeze();
            return new DiffuseMaterial(brush);
        }

        return new DiffuseMaterial(new SolidColorBrush(Palette[index % Palette.Length]));
    }

    private static MeshGeometry3D? BuildGeometry(MeshPart part)
    {
        if (part.Indices.Length < 3) return null;

        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection(part.Positions.Length / 3);
        var normals = new Vector3DCollection(part.Positions.Length / 3);
        var uvs = new PointCollection(part.Positions.Length / 3);

        var hasNormals = part.Normals.Length == part.Positions.Length &&
                         Array.Exists(part.Normals, value => value != 0f);
        if (!hasNormals)
        {
            // Static objects (scb) carry no normals: expand per triangle for flat shading.
            BuildFlatShaded(part, mesh);
            mesh.Freeze();
            return mesh;
        }

        for (var i = 0; i + 2 < part.Positions.Length; i += 3)
        {
            positions.Add(new Point3D(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]));
            normals.Add(new Vector3D(part.Normals[i], part.Normals[i + 1], part.Normals[i + 2]));
        }

        for (var i = 0; i + 1 < part.TexCoords.Length; i += 2)
            uvs.Add(new Point(part.TexCoords[i], part.TexCoords[i + 1]));

        var indices = new Int32Collection(part.Indices.Length);
        foreach (var index in part.Indices)
        {
            if (index < 0 || index >= positions.Count) return null;
            indices.Add(index);
        }

        mesh.Positions = positions;
        mesh.Normals = normals;
        if (uvs.Count == positions.Count) mesh.TextureCoordinates = uvs;
        mesh.TriangleIndices = indices;
        mesh.Freeze();
        return mesh;
    }

    private static void BuildFlatShaded(MeshPart part, MeshGeometry3D mesh)
    {
        var positions = new Point3DCollection();
        var normals = new Vector3DCollection();
        var indices = new Int32Collection();

        // Winding order is not reliable in shipped assets: orient every face away from
        // the mesh centre so the surface is lit instead of staying black.
        var centroid = Centroid(part);

        for (var triangle = 0; triangle + 2 < part.Indices.Length; triangle += 3)
        {
            var a = Vertex(part, part.Indices[triangle]);
            var b = Vertex(part, part.Indices[triangle + 1]);
            var c = Vertex(part, part.Indices[triangle + 2]);
            if (a is null || b is null || c is null) continue;

            var normal = Vector3D.CrossProduct(b.Value - a.Value, c.Value - a.Value);
            if (normal.LengthSquared > 1.0e-12) normal.Normalize();
            else normal = new Vector3D(0, 1, 0);

            var middle = new Vector3D(
                (a.Value.X + b.Value.X + c.Value.X) / 3 - centroid.X,
                (a.Value.Y + b.Value.Y + c.Value.Y) / 3 - centroid.Y,
                (a.Value.Z + b.Value.Z + c.Value.Z) / 3 - centroid.Z);
            if (Vector3D.DotProduct(normal, middle) < 0) normal = -normal;

            indices.Add(positions.Count);
            indices.Add(positions.Count + 1);
            indices.Add(positions.Count + 2);
            positions.Add(a.Value);
            positions.Add(b.Value);
            positions.Add(c.Value);
            for (var i = 0; i < 3; i++) normals.Add(normal);
        }

        mesh.Positions = positions;
        mesh.Normals = normals;
        mesh.TriangleIndices = indices;
    }

    private static Point3D Centroid(MeshPart part)
    {
        if (part.Positions.Length < 3) return new Point3D(0, 0, 0);

        var x = 0.0;
        var y = 0.0;
        var z = 0.0;
        var count = 0;
        for (var i = 0; i + 2 < part.Positions.Length; i += 3)
        {
            x += part.Positions[i];
            y += part.Positions[i + 1];
            z += part.Positions[i + 2];
            count++;
        }

        return count == 0 ? new Point3D(0, 0, 0) : new Point3D(x / count, y / count, z / count);
    }

    private static Point3D? Vertex(MeshPart part, int index)
    {
        var offset = index * 3;
        if (offset < 0 || offset + 2 >= part.Positions.Length) return null;
        return new Point3D(part.Positions[offset], part.Positions[offset + 1], part.Positions[offset + 2]);
    }
}
