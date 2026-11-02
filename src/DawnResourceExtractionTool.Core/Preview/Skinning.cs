using System.Numerics;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.Core;

/// <summary>
/// Turns a skeleton plus an optional clip into the per-joint skinning matrices of one pose.
/// The composition follows the shipped data: a joint's local transform is
/// <c>scale × rotation × translation</c> and a joint's global transform is
/// <c>local × parentGlobal</c> (row-vector convention), while the skinning matrix is
/// <c>inverseBind × global</c>.
/// </summary>
public sealed class PoseSampler
{
    private readonly SkeletonData _skeleton;
    private readonly AnimationTrackData?[] _tracks;
    private readonly Matrix4x4[] _globals;

    public PoseSampler(SkeletonData skeleton, AnimationClip? clip)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        _skeleton = skeleton;
        _globals = new Matrix4x4[skeleton.Joints.Count];
        _tracks = new AnimationTrackData?[skeleton.Joints.Count];

        if (clip is null) return;
        var byHash = clip.Tracks.ToDictionary(track => track.JointHash);
        for (var i = 0; i < skeleton.Joints.Count; i++)
        {
            var hash = SkeletonData.HashName(skeleton.Joints[i].Name);
            _tracks[i] = byHash.GetValueOrDefault(hash);
        }
    }

    public SkeletonData Skeleton => _skeleton;

    /// <summary>Fills <paramref name="destination"/> with one skinning matrix per joint.</summary>
    public void Compute(float time, Matrix4x4[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length < _skeleton.Joints.Count)
            throw new ArgumentException(Strings.T("Core.Skinning.BufferTooSmall"), nameof(destination));

        foreach (var index in _skeleton.ParentOrder)
        {
            var joint = _skeleton.Joints[index];
            var track = _tracks[index];

            var translation = track?.SampleTranslation(time, joint.LocalTranslation) ?? joint.LocalTranslation;
            var rotation = track?.SampleRotation(time, joint.LocalRotation) ?? joint.LocalRotation;
            var scale = track?.SampleScale(time, joint.LocalScale) ?? joint.LocalScale;

            var local = Matrix4x4.CreateScale(scale)
                        * Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(translation);

            var global = joint.ParentIndex >= 0 ? local * _globals[joint.ParentIndex] : local;
            _globals[index] = global;
            destination[index] = joint.InverseBind * global;
        }
    }
}

/// <summary>Linear blend skinning of the decoded mesh parts.</summary>
public static class Skinning
{
    public const int MaxWeights = 4;

    /// <summary>A mesh part can be deformed only when it carries bones and the skeleton matches it.</summary>
    public static bool CanSkin(MeshPart part, SkeletonData? skeleton)
        => skeleton is { JointCount: > 0 } &&
           part.BoneIndices is { Length: > 0 } bones &&
           part.Weights is { Length: > 0 } weights &&
           bones.Length == part.VertexCount * MaxWeights &&
           weights.Length == part.VertexCount * MaxWeights;

    /// <summary>Writes the deformed positions of <paramref name="part"/> into <paramref name="destination"/>.</summary>
    public static void DeformPositions(MeshPart part, SkeletonData skeleton, Matrix4x4[] jointMatrices, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(destination);

        if (!CanSkin(part, skeleton) || destination.Length < part.Positions.Length)
        {
            Array.Copy(part.Positions, destination, Math.Min(part.Positions.Length, destination.Length));
            return;
        }

        var bones = part.BoneIndices!;
        var weights = part.Weights!;
        for (var vertex = 0; vertex < part.VertexCount; vertex++)
        {
            var source = vertex * 3;
            var x = part.Positions[source];
            var y = part.Positions[source + 1];
            var z = part.Positions[source + 2];

            var rx = 0f;
            var ry = 0f;
            var rz = 0f;
            var weightSum = 0f;

            for (var k = 0; k < MaxWeights; k++)
            {
                var weight = weights[vertex * MaxWeights + k];
                if (weight <= 0f) continue;

                var matrix = jointMatrices[skeleton.ResolveJoint(bones[vertex * MaxWeights + k])];
                rx += (x * matrix.M11 + y * matrix.M21 + z * matrix.M31 + matrix.M41) * weight;
                ry += (x * matrix.M12 + y * matrix.M22 + z * matrix.M32 + matrix.M42) * weight;
                rz += (x * matrix.M13 + y * matrix.M23 + z * matrix.M33 + matrix.M43) * weight;
                weightSum += weight;
            }

            if (weightSum <= 0f)
            {
                // Weightless vertices (props, attachments) stay where the artist put them.
                destination[source] = x;
                destination[source + 1] = y;
                destination[source + 2] = z;
            }
            else
            {
                destination[source] = rx / weightSum;
                destination[source + 1] = ry / weightSum;
                destination[source + 2] = rz / weightSum;
            }
        }
    }

    /// <summary>Rotates the normals of <paramref name="part"/> with the same blend as the positions.</summary>
    public static void DeformNormals(MeshPart part, SkeletonData skeleton, Matrix4x4[] jointMatrices, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(destination);

        if (part.Normals.Length < part.Positions.Length || destination.Length < part.Normals.Length)
        {
            Array.Copy(part.Normals, destination, Math.Min(part.Normals.Length, destination.Length));
            return;
        }

        if (!CanSkin(part, skeleton))
        {
            Array.Copy(part.Normals, destination, part.Normals.Length);
            return;
        }

        var bones = part.BoneIndices!;
        var weights = part.Weights!;
        for (var vertex = 0; vertex < part.VertexCount; vertex++)
        {
            var source = vertex * 3;
            var x = part.Normals[source];
            var y = part.Normals[source + 1];
            var z = part.Normals[source + 2];

            var rx = 0f;
            var ry = 0f;
            var rz = 0f;
            var weightSum = 0f;

            for (var k = 0; k < MaxWeights; k++)
            {
                var weight = weights[vertex * MaxWeights + k];
                if (weight <= 0f) continue;

                var matrix = jointMatrices[skeleton.ResolveJoint(bones[vertex * MaxWeights + k])];
                rx += (x * matrix.M11 + y * matrix.M21 + z * matrix.M31) * weight;
                ry += (x * matrix.M12 + y * matrix.M22 + z * matrix.M32) * weight;
                rz += (x * matrix.M13 + y * matrix.M23 + z * matrix.M33) * weight;
                weightSum += weight;
            }

            if (weightSum <= 0f)
            {
                rx = x;
                ry = y;
                rz = z;
            }

            var normal = new Vector3(rx, ry, rz);
            var length = normal.Length();
            if (length > 1e-6f) normal /= length;
            else normal = new Vector3(0, 1, 0);

            destination[source] = normal.X;
            destination[source + 1] = normal.Y;
            destination[source + 2] = normal.Z;
        }
    }
}
