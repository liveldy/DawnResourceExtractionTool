using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LolDataExtractor.Core;

namespace LolDataExtractor.App.Services;

/// <summary>
/// A model bound to a skeleton so it can be posed every frame. Geometry stays unfrozen;
/// <see cref="Apply"/> rewrites positions and normals for the pose at a given time.
/// </summary>
public sealed class ModelScene
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xB8, 0xC4, 0xD9),
        Color.FromRgb(0xD9, 0xC0, 0xB8),
        Color.FromRgb(0xB8, 0xD9, 0xC6),
        Color.FromRgb(0xD6, 0xB8, 0xD9),
        Color.FromRgb(0xD9, 0xD3, 0xB8),
    };

    private readonly SkeletonData _skeleton;
    private readonly PoseSampler _sampler;
    private readonly Matrix4x4[] _matrices;
    private readonly List<PartGeometry> _parts = new();

    private ModelScene(SkeletonData skeleton, AnimationClip? clip)
    {
        _skeleton = skeleton;
        _sampler = new PoseSampler(skeleton, clip);
        _matrices = new Matrix4x4[skeleton.JointCount];
        Clip = clip;
    }

    public Model3DGroup Group { get; } = new();

    public AnimationClip? Clip { get; }

    public float Duration => Clip?.Duration ?? 0f;

    public int VertexCount => _parts.Sum(part => part.Part.VertexCount);

    /// <summary>Builds a posable scene, or <c>null</c> when no part of the model is skinned by this skeleton.</summary>
    /// <param name="textures">One texture per part (indexed like <see cref="ModelPreview.Parts"/>); may be shorter.</param>
    public static ModelScene? Create(ModelPreview model, SkeletonData? skeleton, AnimationClip? clip,
        IReadOnlyList<ImageSource?>? textures)
    {
        if (skeleton is null) return null;
        if (!model.Parts.Any(part => Skinning.CanSkin(part, skeleton))) return null;

        var scene = new ModelScene(skeleton, clip);
        scene.Group.Children.Add(new AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)));
        scene.Group.Children.Add(new DirectionalLight(Color.FromRgb(0xF0, 0xF0, 0xF0), new Vector3D(-0.5, -0.7, -0.9)));
        scene.Group.Children.Add(new DirectionalLight(Color.FromRgb(0x60, 0x60, 0x60), new Vector3D(0.8, 0.4, 0.5)));

        for (var i = 0; i < model.Parts.Count; i++)
        {
            var part = model.Parts[i];
            if (part.Indices.Length < 3 || part.VertexCount == 0) continue;

            var geometry = new MeshGeometry3D
            {
                TextureCoordinates = Uv(part),
                TriangleIndices = new Int32Collection(part.Indices),
            };

            var node = new PartGeometry(part, geometry);
            scene._parts.Add(node);

            // Every submesh may carry its own texture (body vs. tails on the same skin).
            var material = ModelGeometryBuilder.MaterialFor(i, ModelGeometryBuilder.TextureAt(textures, i));
            scene.Group.Children.Add(new GeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                BackMaterial = material,
            });
        }

        scene.Apply(0f);
        return scene._parts.Count == 0 ? null : scene;
    }

    /// <summary>Poses the model at <paramref name="seconds"/>; time past the clip length wraps.</summary>
    public void Apply(float seconds)
    {
        if (_parts.Count == 0) return;

        var time = Duration > 0f ? seconds % Duration : 0f;
        _sampler.Compute(time, _matrices);

        foreach (var node in _parts)
        {
            Skinning.DeformPositions(node.Part, _skeleton, _matrices, node.Positions);
            Skinning.DeformNormals(node.Part, _skeleton, _matrices, node.Normals);

            var positions = new Point3DCollection(node.Part.VertexCount);
            var normals = new Vector3DCollection(node.Part.VertexCount);
            for (var i = 0; i + 2 < node.Positions.Length; i += 3)
            {
                positions.Add(new Point3D(node.Positions[i], node.Positions[i + 1], node.Positions[i + 2]));
                normals.Add(new Vector3D(node.Normals[i], node.Normals[i + 1], node.Normals[i + 2]));
            }

            node.Geometry.Positions = positions;
            node.Geometry.Normals = normals;
        }
    }

    /// <summary>Texture coordinates of a part as a WPF point collection.</summary>
    private static PointCollection Uv(MeshPart part)
    {
        var uvs = new PointCollection(part.VertexCount);
        for (var i = 0; i + 1 < part.TexCoords.Length; i += 2) uvs.Add(new Point(part.TexCoords[i], part.TexCoords[i + 1]));
        return uvs.Count == part.VertexCount ? uvs : new PointCollection();
    }

    private sealed class PartGeometry
    {
        public PartGeometry(MeshPart part, MeshGeometry3D geometry)
        {
            Part = part;
            Geometry = geometry;
            Positions = new float[part.Positions.Length];
            Normals = new float[part.Normals.Length];
        }

        public MeshPart Part { get; }

        public MeshGeometry3D Geometry { get; }

        public float[] Positions { get; }

        public float[] Normals { get; }
    }
}
