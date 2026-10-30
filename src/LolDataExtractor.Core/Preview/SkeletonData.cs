using System.Numerics;
using LeagueToolkit.IO.SkeletonFile;

namespace LolDataExtractor.Core;

/// <summary>One joint of a skeleton with its bind pose taken apart into scale, rotation and position.</summary>
public sealed record SkeletonJointInfo(
    string Name,
    short Id,
    int ParentIndex,
    Vector3 LocalTranslation,
    Quaternion LocalRotation,
    Vector3 LocalScale,
    Matrix4x4 BindLocal,
    Matrix4x4 BindGlobal,
    Matrix4x4 InverseBind);

/// <summary>
/// A parsed <c>.skl</c>: the joint hierarchy plus the influence table that maps the bone
/// indices stored in a <c>.skn</c> vertex to joints of this skeleton.
/// </summary>
public sealed class SkeletonData
{
    private SkeletonData(string name, IReadOnlyList<SkeletonJointInfo> joints, IReadOnlyList<short> influences, int[] parentOrder)
    {
        Name = name;
        Joints = joints;
        Influences = influences;
        ParentOrder = parentOrder;
    }

    public string Name { get; }

    public IReadOnlyList<SkeletonJointInfo> Joints { get; }

    /// <summary>Bone index of a skinned vertex → joint id; empty when the skeleton stores no table.</summary>
    public IReadOnlyList<short> Influences { get; }

    /// <summary>Joint indices ordered so that every parent comes before its children.</summary>
    public IReadOnlyList<int> ParentOrder { get; }

    public int JointCount => Joints.Count;

    /// <summary>Hash used by <c>.anm</c> tracks to address their joint.</summary>
    public static uint HashName(string name) => LeagueToolkit.Helpers.Cryptography.Cryptography.ElfHash(name);

    /// <summary>Maps a bone index of a skinned vertex to a joint index, clamped into range.</summary>
    public int ResolveJoint(int boneIndex)
    {
        var jointId = boneIndex >= 0 && boneIndex < Influences.Count ? Influences[boneIndex] : boneIndex;
        if (jointId < 0) return 0;

        // Joints are stored in id order for every shipped skeleton, but never assume it.
        for (var i = 0; i < Joints.Count; i++)
            if (Joints[i].Id == jointId) return i;
        return Math.Min(jointId, Joints.Count - 1);
    }

    public override string ToString() => $"{Name} · {Joints.Count} 关节 · {Influences.Count} 影响";

    public static SkeletonData Create(string name, IReadOnlyList<SkeletonJointInfo> joints, IReadOnlyList<short> influences)
    {
        ArgumentNullException.ThrowIfNull(joints);

        var order = new List<int>(joints.Count);
        var placed = new bool[joints.Count];
        for (var pass = 0; pass < joints.Count && order.Count < joints.Count; pass++)
        {
            for (var i = 0; i < joints.Count; i++)
            {
                if (placed[i]) continue;
                if (joints[i].ParentIndex >= 0 && !placed[joints[i].ParentIndex]) continue;
                placed[i] = true;
                order.Add(i);
            }
        }

        // Cycles or a broken parent chain: append whatever is left so nothing is skipped.
        for (var i = 0; i < joints.Count; i++)
            if (!placed[i]) order.Add(i);

        return new SkeletonData(name, joints, influences, order.ToArray());
    }
}

/// <summary>Reads the skeleton files (<c>.skl</c>) that ship inside champion wads.</summary>
public static class SkeletonDecoder
{
    public static SkeletonData Decode(byte[] bytes, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        Skeleton skeleton;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            skeleton = new Skeleton(stream, true);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("骨骼文件解析失败：" + ex.Message, ex);
        }

        if (skeleton.Joints.Count == 0) throw new InvalidDataException("骨骼文件里没有任何关节");

        // The file stores joint ids; parents are referenced by id, not by list position.
        var indexById = new Dictionary<short, int>(skeleton.Joints.Count);
        for (var i = 0; i < skeleton.Joints.Count; i++) indexById[skeleton.Joints[i].ID] = i;

        var joints = new List<SkeletonJointInfo>(skeleton.Joints.Count);
        foreach (var joint in skeleton.Joints)
        {
            var local = joint.LocalTransform;
            joints.Add(new SkeletonJointInfo(
                joint.Name ?? string.Empty,
                joint.ID,
                indexById.TryGetValue(joint.ParentID, out var parent) ? parent : -1,
                local.Translation,
                RotationOf(local),
                ScaleOf(local),
                local,
                joint.GlobalTransform,
                joint.InverseBindTransform));
        }

        var resolved = string.IsNullOrWhiteSpace(name) ? skeleton.Name : name;
        return SkeletonData.Create(resolved ?? string.Empty, joints, skeleton.Influences.ToList());
    }

    /// <summary>Scale of a transform, taken as the length of its three basis rows.</summary>
    public static Vector3 ScaleOf(Matrix4x4 matrix) => new(
        new Vector3(matrix.M11, matrix.M12, matrix.M13).Length(),
        new Vector3(matrix.M21, matrix.M22, matrix.M23).Length(),
        new Vector3(matrix.M31, matrix.M32, matrix.M33).Length());

    /// <summary>Rotation of a transform with its scale divided out.</summary>
    public static Quaternion RotationOf(Matrix4x4 matrix)
    {
        var scale = ScaleOf(matrix);
        var normalized = new Matrix4x4(
            matrix.M11 / Safe(scale.X), matrix.M12 / Safe(scale.X), matrix.M13 / Safe(scale.X), 0,
            matrix.M21 / Safe(scale.Y), matrix.M22 / Safe(scale.Y), matrix.M23 / Safe(scale.Y), 0,
            matrix.M31 / Safe(scale.Z), matrix.M32 / Safe(scale.Z), matrix.M33 / Safe(scale.Z), 0,
            0, 0, 0, 1);

        var rotation = Quaternion.CreateFromRotationMatrix(normalized);
        return rotation.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.Normalize(rotation);
    }

    private static float Safe(float value) => MathF.Abs(value) < 1e-6f ? 1e-6f : value;
}
