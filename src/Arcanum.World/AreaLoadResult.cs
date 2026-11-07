using Arcanum.Data.Area;
using Arcanum.Data.Lighting;

namespace Arcanum.World;

public sealed record DecodedAreaFrame(int Width, int Height, byte[] Rgba);
public sealed record AreaLoadResult(long Generation, AreaSnapshot Snapshot, AreaScenePlan Plan,
    IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames, AreaBudgetUsage InitialUsage, AreaBudgetUsage FinalUsage,
    long CpuRgbaBytes, long AllocatedBytes, long ManagedBytes, long ProcessBytes, long ElapsedMilliseconds)
{
    /// <summary>ARC-RENDER-010 day/night inputs; absent for structural fixtures (drawn unlit).</summary>
    public AreaLightingPlan? Lighting { get; init; }
    /// <summary>ARC-RENDER-011 wall check inputs, validated at load (empty when the area has no walls).</summary>
    public IReadOnlyList<AreaWallPiece> Walls { get; init; } = Array.Empty<AreaWallPiece>();
    /// <summary>ARC-RENDER-017: why a whole map is drawn unlit (its lighting sources were not admitted); null otherwise.</summary>
    public LightingFailure? UnlitReason { get; init; }
    /// <summary>ARC-RENDER-017: why a lit whole map has no stationary lights; null otherwise.</summary>
    public LightingFailure? UnlitLightsReason { get; init; }
    /// <summary>ARC-COMBAT-005: the archive file of each input key the snapshot's art came from (empty for fixtures), where a
    /// fallen pose is read on demand (<see cref="AreaFallenPoses"/>).</summary>
    public IReadOnlyDictionary<AreaInputKey, string> ArtSources { get; init; } = new Dictionary<AreaInputKey, string>();
    /// <summary>ARC-DEV-118 (MIG9 S5b): a window's map read whole (its mobile rows and its size in sectors), which the session opens the map's residency
    /// and sector cache from; null for a whole map (its snapshot already holds the rows) and for a fixture.</summary>
    public AreaWholeMobile? WholeMobile { get; init; }
}
