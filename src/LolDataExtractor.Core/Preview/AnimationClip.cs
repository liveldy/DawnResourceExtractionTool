using System.Numerics;

namespace LolDataExtractor.Core;

/// <summary>Keyframes of one joint of an animation, one sorted timeline per transform channel.</summary>
public sealed class AnimationTrackData
{
    public AnimationTrackData(uint jointHash, float[] translationTimes, Vector3[] translations,
        float[] rotationTimes, Quaternion[] rotations, float[] scaleTimes, Vector3[] scales)
    {
        JointHash = jointHash;
        TranslationTimes = translationTimes;
        Translations = translations;
        RotationTimes = rotationTimes;
        Rotations = rotations;
        ScaleTimes = scaleTimes;
        Scales = scales;
    }

    public uint JointHash { get; }

    public float[] TranslationTimes { get; }

    public Vector3[] Translations { get; }

    public float[] RotationTimes { get; }

    public Quaternion[] Rotations { get; }

    public float[] ScaleTimes { get; }

    public Vector3[] Scales { get; }

    public int KeyCount => TranslationTimes.Length + RotationTimes.Length + ScaleTimes.Length;

    public Vector3 SampleTranslation(float time, Vector3 fallback)
    {
        if (TranslationTimes.Length == 0) return fallback;
        var index = LowerBound(TranslationTimes, time);
        if (index <= 0) return Translations[0];
        if (index >= TranslationTimes.Length) return Translations[^1];

        var span = TranslationTimes[index] - TranslationTimes[index - 1];
        var weight = span <= 1e-6f ? 0f : (time - TranslationTimes[index - 1]) / span;
        return Vector3.Lerp(Translations[index - 1], Translations[index], Math.Clamp(weight, 0f, 1f));
    }

    public Quaternion SampleRotation(float time, Quaternion fallback)
    {
        if (RotationTimes.Length == 0) return fallback;
        var index = LowerBound(RotationTimes, time);
        if (index <= 0) return Rotations[0];
        if (index >= RotationTimes.Length) return Rotations[^1];

        var span = RotationTimes[index] - RotationTimes[index - 1];
        var weight = span <= 1e-6f ? 0f : (time - RotationTimes[index - 1]) / span;
        return Quaternion.Normalize(Quaternion.Slerp(Rotations[index - 1], Rotations[index], Math.Clamp(weight, 0f, 1f)));
    }

    public Vector3 SampleScale(float time, Vector3 fallback)
    {
        if (ScaleTimes.Length == 0) return fallback;
        var index = LowerBound(ScaleTimes, time);
        if (index <= 0) return Scales[0];
        if (index >= ScaleTimes.Length) return Scales[^1];

        var span = ScaleTimes[index] - ScaleTimes[index - 1];
        var weight = span <= 1e-6f ? 0f : (time - ScaleTimes[index - 1]) / span;
        return Vector3.Lerp(Scales[index - 1], Scales[index], Math.Clamp(weight, 0f, 1f));
    }

    private static int LowerBound(float[] times, float time)
    {
        var low = 0;
        var high = times.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (times[middle] < time) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}

/// <summary>A parsed <c>.anm</c>: joint tracks with their sampled timelines, plus frame rate and length.</summary>
public sealed class AnimationClip
{
    public AnimationClip(string name, float fps, float duration, IReadOnlyList<AnimationTrackData> tracks)
    {
        Name = name;
        Fps = fps;
        Duration = duration;
        Tracks = tracks;
        KeyCount = tracks.Sum(track => track.KeyCount);
    }

    public string Name { get; }

    public float Fps { get; }

    /// <summary>Length in seconds, taken from the last keyframe of any channel.</summary>
    public float Duration { get; }

    public IReadOnlyList<AnimationTrackData> Tracks { get; }

    public int KeyCount { get; }

    public string Describe => $"{Duration:F1}s · {Fps:F0}fps · {Tracks.Count} 条关节轨道";

    /// <summary>How many tracks of this clip address a joint of the given skeleton.</summary>
    public int MatchedJointCount(SkeletonData skeleton)
    {
        var hashes = skeleton.Joints.Select(joint => SkeletonData.HashName(joint.Name)).ToHashSet();
        return Tracks.Count(track => hashes.Contains(track.JointHash));
    }

    public override string ToString() => $"{Name} · {Describe}";
}

/// <summary>Reads the animation files (<c>.anm</c>) stored next to a champion's models.</summary>
public static class AnimationDecoder
{
    public static AnimationClip Decode(byte[] bytes, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        LeagueToolkit.IO.AnimationFile.Animation animation;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            animation = new LeagueToolkit.IO.AnimationFile.Animation(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("动画文件解析失败：" + ex.Message, ex);
        }

        var tracks = new List<AnimationTrackData>(animation.Tracks.Count);
        var duration = 0f;
        foreach (var track in animation.Tracks)
        {
            var translations = Sorted(track.Translations);
            var rotations = Sorted(track.Rotations);
            var scales = Sorted(track.Scales);

            tracks.Add(new AnimationTrackData(
                track.JointHash,
                translations.Times, translations.Values,
                rotations.Times, rotations.Values,
                scales.Times, scales.Values));

            duration = MathF.Max(duration, MathF.Max(translations.Last, MathF.Max(rotations.Last, scales.Last)));
        }

        // A clip with a single keyframe still needs a length so playback has something to loop over.
        if (duration <= 0f) duration = animation.FrameDuration > 0f ? animation.FrameDuration : 1f;
        var fps = animation.FPS > 0f && !float.IsInfinity(animation.FPS) ? animation.FPS : 30f;
        return new AnimationClip(name ?? string.Empty, fps, duration, tracks);
    }

    private static (float[] Times, T[] Values, float Last) Sorted<T>(Dictionary<float, T> keys)
    {
        if (keys.Count == 0) return (Array.Empty<float>(), Array.Empty<T>(), 0f);

        var ordered = keys.OrderBy(pair => pair.Key).ToArray();
        var times = new float[ordered.Length];
        var values = new T[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            times[i] = ordered[i].Key;
            values[i] = ordered[i].Value;
        }
        return (times, values, times[^1]);
    }
}
