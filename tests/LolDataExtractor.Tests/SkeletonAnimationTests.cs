using System.Numerics;
using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

/// <summary>
/// Decoding and skinning of the sample champion wad. The wads are not committed, so these
/// tests return early when <c>tests/testdata</c> has no Ahri wad.
/// </summary>
public class SkeletonAnimationTests
{
    private const string SkeletonPath = "assets/characters/ahri/skins/base/ahri_base.skl";
    private const string MeshPath = "assets/characters/ahri/skins/base/ahri_base.skn";
    private const string IdlePath = "assets/characters/ahri/skins/base/animations/idle_var3.anm";

    private sealed record Assets(SkeletonData Skeleton, AnimationClip Clip, ModelPreview Model);

    private static Assets? Load()
    {
        var wad = TestData.Wad("Ahri.wad.client");
        if (wad is null) return null;

        var skeleton = SkeletonDecoder.Decode(TestData.ReadEntry(wad, TestData.Hash(SkeletonPath)), "ahri_base");
        var clip = AnimationDecoder.Decode(TestData.ReadEntry(wad, TestData.Hash(IdlePath)), "idle_var3");
        var model = ModelDecoder.FromSkn(TestData.ReadEntry(wad, TestData.Hash(MeshPath)));
        return new Assets(skeleton, clip, model);
    }

    [Fact]
    public void Decodes_a_shipped_skeleton()
    {
        if (Load() is not { } assets) return;

        Assert.Equal(211, assets.Skeleton.JointCount);
        Assert.Contains(assets.Skeleton.Joints, joint => joint.Name == "Root");
        Assert.Contains(assets.Skeleton.Joints, joint => joint.Name == "Head");
        Assert.Equal(177, assets.Skeleton.Influences.Count);

        // The root comes first in the computed order and has no parent.
        Assert.Equal(-1, assets.Skeleton.Joints[assets.Skeleton.ParentOrder[0]].ParentIndex);
        Assert.Equal(assets.Skeleton.Influences[0], assets.Skeleton.Joints[assets.Skeleton.ResolveJoint(0)].Id);
    }

    [Fact]
    public void Decodes_a_shipped_animation()
    {
        if (Load() is not { } assets) return;

        Assert.Equal(211, assets.Clip.Tracks.Count);
        Assert.InRange(assets.Clip.Fps, 29f, 31f);
        Assert.InRange(assets.Clip.Duration, 4f, 6f);
        Assert.True(assets.Clip.KeyCount > 1000);

        // Every track addresses a joint of the skeleton: the hashing scheme matches.
        Assert.Equal(assets.Clip.Tracks.Count, assets.Clip.MatchedJointCount(assets.Skeleton));
    }

    [Fact]
    public void The_mesh_carries_bone_weights()
    {
        if (Load() is not { } assets) return;

        foreach (var part in assets.Model.Parts)
        {
            Assert.NotNull(part.BoneIndices);
            Assert.NotNull(part.Weights);
            Assert.Equal(part.VertexCount * Skinning.MaxWeights, part.BoneIndices!.Length);
            Assert.True(Skinning.CanSkin(part, assets.Skeleton));
        }
    }

    [Fact]
    public void The_bind_pose_of_the_mesh_stays_where_it_was_modelled()
    {
        if (Load() is not { } assets) return;

        var matrices = new Matrix4x4[assets.Skeleton.JointCount];
        new PoseSampler(assets.Skeleton, null).Compute(0, matrices);

        var worst = 0f;
        foreach (var part in assets.Model.Parts)
        {
            var deformed = new float[part.Positions.Length];
            Skinning.DeformPositions(part, assets.Skeleton, matrices, deformed);
            for (var i = 0; i < deformed.Length; i++)
                worst = MathF.Max(worst, MathF.Abs(deformed[i] - part.Positions[i]));
        }

        Assert.True(worst < 2f, $"绑定姿势偏移 {worst:F2}");
    }

    [Fact]
    public void Playing_a_clip_moves_the_mesh_but_keeps_it_intact()
    {
        if (Load() is not { } assets) return;

        var matrices = new Matrix4x4[assets.Skeleton.JointCount];
        var sampler = new PoseSampler(assets.Skeleton, assets.Clip);

        foreach (var time in new[] { 0.5f, 2.5f })
        {
            sampler.Compute(time, matrices);

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var moved = 0;
            var total = 0;

            foreach (var part in assets.Model.Parts)
            {
                var deformed = new float[part.Positions.Length];
                Skinning.DeformPositions(part, assets.Skeleton, matrices, deformed);

                for (var i = 0; i + 2 < deformed.Length; i += 3)
                {
                    var point = new Vector3(deformed[i], deformed[i + 1], deformed[i + 2]);
                    Assert.True(float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z),
                        "蒙皮产生了非有限坐标");
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);

                    total++;
                    if (Vector3.Distance(point, new Vector3(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2])) > 1f)
                        moved++;
                }
            }

            // Ahri is about two metres tall with nine tails spread behind her; anything far
            // outside that box means the joints were composed wrongly.
            Assert.InRange(max.Y - min.Y, 120f, 280f);
            Assert.InRange(MathF.Abs(min.X), 0f, 400f);
            Assert.InRange(MathF.Abs(max.X), 0f, 400f);
            Assert.InRange(MathF.Abs(min.Z), 0f, 400f);
            Assert.InRange(MathF.Abs(max.Z), 0f, 400f);
            Assert.True(moved > total / 4, $"只有 {moved}/{total} 个顶点随动画移动");
        }
    }
}
