namespace Arcanum.Experiments.RelightLab;

/// <summary>One frame of an animation: 8-bit RGBA, row-major.</summary>
public sealed record CycleFrame(int Width, int Height, byte[] Rgba);

/// <summary>What <see cref="WalkCycleShimmer.Measure"/> found over the consecutive pairs of one animation.</summary>
/// <param name="Frames">Frames given.</param>
/// <param name="Pairs">Consecutive pairs measured (with a loop the last frame is followed by the first).</param>
/// <param name="Skipped">Pairs left out because the two frames differ in size.</param>
/// <param name="RatioMean">Mean of <see cref="NormalMapGenerator.ShimmerRatio"/> over the pairs (0 when none).</param>
/// <param name="RatioMax">The largest ratio of any pair.</param>
/// <param name="MeanNormalChangeDegrees">Mean over the pairs of <see cref="NormalMapGenerator.MeanAngleDegrees"/> (the normals of the texels solid in both frames).</param>
public sealed record CycleShimmer(int Frames, int Pairs, int Skipped, double RatioMean, double RatioMax, double MeanNormalChangeDegrees);

/// <summary>
/// ARC-RES-2000: frame-to-frame shimmer of the generated normals over a real animation (a walk cycle), the check the first experiment's smoke could not make (its
/// hero did not animate, so its ratio was 0 by construction). The ratio is the Lambert term's change under one fixed light divided by the sprites' own luminance change,
/// the angle is how far a texel's normal moves between two frames; both are over the texels solid in both frames, so a pose change that moves the silhouette
/// is part of the number (a walk cycle's legs really move), and the ratio says whether the normals move more than the picture does.
/// </summary>
public static class WalkCycleShimmer
{
    public static CycleShimmer Measure(IReadOnlyList<CycleFrame> frames, NormalMapOptions options, bool loop = false, (double X, double Y, double Z)? light = null)
    {
        ArgumentNullException.ThrowIfNull(frames); ArgumentNullException.ThrowIfNull(options);
        var l = light ?? (-1, -1, 1);
        var maps = frames.Select(f => NormalMapGenerator.Generate(f.Rgba, f.Width, f.Height, options)).ToArray();
        var ratios = new List<double>(); var angles = new List<double>(); var skipped = 0;
        var count = loop && frames.Count > 1 ? frames.Count : frames.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = frames[i]; var j = (i + 1) % frames.Count; var b = frames[j];
            if (a.Width != b.Width || a.Height != b.Height) { skipped++; continue; }
            ratios.Add(NormalMapGenerator.ShimmerRatio(a.Rgba, maps[i], b.Rgba, maps[j], l));
            angles.Add(NormalMapGenerator.MeanAngleDegrees(maps[i], maps[j]));
        }
        return new CycleShimmer(frames.Count, ratios.Count, skipped, ratios.Count == 0 ? 0 : ratios.Average(), ratios.Count == 0 ? 0 : ratios.Max(), angles.Count == 0 ? 0 : angles.Average());
    }
}
