using System.Numerics;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>Skinning maths on a hand-built skeleton; no game data needed.</summary>
public class SkinningTests
{
    /// <summary>Root at the origin with a child joint that rotates in place.</summary>
    private static SkeletonData TwoJointSkeleton()
    {
        var root = new SkeletonJointInfo("Root", 0, -1, Vector3.Zero, Quaternion.Identity, Vector3.One,
            Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity);

        var arm = new SkeletonJointInfo("Arm", 1, 0, Vector3.Zero, Quaternion.Identity, Vector3.One,
            Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity);

        return SkeletonData.Create("test", new[] { root, arm }, new short[] { 0, 1 });
    }

    private static AnimationClip RotateArmClip(float angle, Vector3? axis = null)
    {
        var track = new AnimationTrackData(
            SkeletonData.HashName("Arm"),
            Array.Empty<float>(), Array.Empty<Vector3>(),
            new[] { 0f, 1f }, new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle(axis ?? Vector3.UnitZ, angle) },
            Array.Empty<float>(), Array.Empty<Vector3>());
        return new AnimationClip("spin", 30, 1f, new[] { track });
    }

    /// <summary>A triangle whose three corners are fully bound to joint 1.</summary>
    private static MeshPart Triangle(byte bone = 1)
    {
        var positions = new[] { 1f, 0f, 0f, 0f, 1f, 0f, -1f, 0f, 0f };
        var normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f };
        var uvs = new[] { 0f, 0f, 1f, 0f, 0f, 1f };
        var bones = new byte[12];
        var weights = new float[12];
        for (var vertex = 0; vertex < 3; vertex++)
        {
            bones[vertex * 4] = bone;
            weights[vertex * 4] = 1f;
        }

        return new MeshPart("tri", positions, normals, uvs, new[] { 0, 1, 2 }, null, bones, weights);
    }

    private static float[] Deform(SkeletonData skeleton, AnimationClip? clip, float time, MeshPart part)
    {
        var matrices = new Matrix4x4[skeleton.JointCount];
        new PoseSampler(skeleton, clip).Compute(time, matrices);
        var deformed = new float[part.Positions.Length];
        Skinning.DeformPositions(part, skeleton, matrices, deformed);
        return deformed;
    }

    [Fact]
    public void Bind_pose_keeps_vertices_in_place()
    {
        var skeleton = TwoJointSkeleton();
        var part = Triangle();
        var deformed = Deform(skeleton, null, 0, part);

        for (var i = 0; i < part.Positions.Length; i++)
            Assert.Equal(part.Positions[i], deformed[i], 3);
    }

    [Fact]
    public void The_first_keyframe_also_matches_the_bind_pose()
    {
        var skeleton = TwoJointSkeleton();
        var part = Triangle();
        var deformed = Deform(skeleton, RotateArmClip(MathF.PI / 2), 0, part);

        Assert.Equal(1f, deformed[0], 3);
        Assert.Equal(0f, deformed[1], 3);
    }

    [Fact]
    public void A_rotated_joint_moves_its_vertices()
    {
        var skeleton = TwoJointSkeleton();
        var part = Triangle();
        var deformed = Deform(skeleton, RotateArmClip(MathF.PI / 2), 1f, part);

        // A quarter turn about Z sends (1,0,0) to (0,1,0).
        Assert.Equal(0f, deformed[0], 3);
        Assert.Equal(1f, deformed[1], 3);
    }

    [Fact]
    public void Samples_between_keyframes()
    {
        var skeleton = TwoJointSkeleton();
        var part = Triangle();
        var deformed = Deform(skeleton, RotateArmClip(MathF.PI / 2), 0.5f, part);

        // Halfway is a 45 degree turn.
        Assert.Equal(0.7071f, deformed[0], 2);
        Assert.Equal(0.7071f, deformed[1], 2);
    }

    [Fact]
    public void Normals_rotate_with_the_mesh()
    {
        var skeleton = TwoJointSkeleton();
        var part = Triangle();
        var matrices = new Matrix4x4[skeleton.JointCount];
        new PoseSampler(skeleton, RotateArmClip(MathF.PI / 2, Vector3.UnitX)).Compute(1f, matrices);

        var normals = new float[part.Normals.Length];
        Skinning.DeformNormals(part, skeleton, matrices, normals);

        Assert.Equal(0f, normals[0], 3);
        Assert.Equal(-1f, normals[1], 3);
        Assert.Equal(0f, normals[2], 3);
    }

    [Fact]
    public void A_mesh_without_bones_is_returned_unchanged()
    {
        var skeleton = TwoJointSkeleton();
        var part = new MeshPart("plain", new[] { 1f, 2f, 3f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0f }, new[] { 0, 0, 0 }, null);

        var matrices = new Matrix4x4[skeleton.JointCount];
        new PoseSampler(skeleton, RotateArmClip(1f)).Compute(1f, matrices);

        var deformed = new float[3];
        Skinning.DeformPositions(part, skeleton, matrices, deformed);

        Assert.Equal(new[] { 1f, 2f, 3f }, deformed);
        Assert.False(Skinning.CanSkin(part, skeleton));
    }

    [Fact]
    public void Bone_indices_are_resolved_through_the_influence_table()
    {
        var skeleton = SkeletonData.Create("table",
            new[]
            {
                new SkeletonJointInfo("A", 0, -1, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity),
                new SkeletonJointInfo("B", 1, 0, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity),
            },
            new short[] { 1, 0 });

        Assert.Equal(1, skeleton.ResolveJoint(0));
        Assert.Equal(0, skeleton.ResolveJoint(1));

        // Out of range bone indices fall back to the raw id and stay inside the skeleton.
        Assert.InRange(skeleton.ResolveJoint(9), 0, 1);
    }

    [Fact]
    public void Parents_are_ordered_before_their_children()
    {
        var skeleton = SkeletonData.Create("order",
            new[]
            {
                new SkeletonJointInfo("child", 0, 1, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity),
                new SkeletonJointInfo("parent", 1, -1, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity),
            },
            Array.Empty<short>());

        Assert.Equal(new[] { 1, 0 }, skeleton.ParentOrder);
    }

    [Fact]
    public void Pose_sampler_requires_a_large_enough_buffer()
    {
        var skeleton = TwoJointSkeleton();
        Assert.Throws<ArgumentException>(() => new PoseSampler(skeleton, null).Compute(0, new Matrix4x4[1]));
    }

    [Fact]
    public void Clips_keep_their_frame_rate_and_length()
    {
        var clip = RotateArmClip(MathF.PI / 2);
        Assert.Equal(30f, clip.Fps);
        Assert.Equal(1f, clip.Duration);
        Assert.Single(clip.Tracks);
        Assert.Equal(2, clip.KeyCount);
    }
}
