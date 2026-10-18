using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>How an actor covers one step (ARC-WORLD-020, research ARC-RES-047 L1–L8).</summary>
public enum MovementGait { Walk, Run }

/// <summary>One gait's ART stride: frames per cycle, the ART rate, and what the frame offsets of one cycle add up
/// to on an axis step (one coordinate changes) and on a diagonal step, as positive screen pixels.</summary>
public sealed record GaitStride(int FramesPerCycle, int ArtFramesPerSecond, int AxisDx, int AxisDy, int DiagonalPixels)
{
    public bool IsValid => FramesPerCycle > 0 && ArtFramesPerSecond > 0 && AxisDx >= 0 && AxisDy >= 0
        && AxisDx * AxisAlong + AxisDy * AxisAcross > 0 && DiagonalPixels > 0;
    // One axis step moves the anchor (40, 20) screen pixels, a diagonal step 80 pixels across (or 40 down, the same
    // tile fraction for this symmetric stride), research L7.
    internal const int AxisAlong = 40, AxisAcross = 20, DiagonalStep = 80;
}

/// <summary>Exact step durations per gait for one actor: the original's ART-driven stride at a frame rate chosen by
/// Speed. Time per tile = frames × frame time ÷ tiles per cycle, where the tiles per cycle are the cycle's offsets
/// projected on the step (research L6–L8). This is a per-tile average, not per-frame parity (research U1).</summary>
public sealed class GaitTiming
{
    /// <summary>Provenance code a run step's permit carries, so the presentation draws the run ART for it.</summary>
    public const string RunProvenance = "GAIT_RUN";
    public const string WalkProvenance = "GAIT_WALK";
    /// <summary>ARC-WORLD-021: provenance code of a window-jump step, drawn with the vault ART.</summary>
    public const string VaultProvenance = "GAIT_VAULT";
    private readonly StepDuration _walkAxis, _walkDiagonal, _runAxis, _runDiagonal;

    /// <param name="vaultFrames">The vault ART's frames; with <paramref name="vaultFramesPerSecond"/> it gives the
    /// window-jump step one frame per 1000/fps ms, without Speed scaling (research ARC-RES-047 W4). Zero means no
    /// vault: a window jump then takes the walk or run step time.</param>
    public GaitTiming(GaitStride walk, GaitStride run, int speed, int vaultFrames = 0, int vaultFramesPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(walk); ArgumentNullException.ThrowIfNull(run);
        if (!walk.IsValid || !run.IsValid || speed < 0 || vaultFrames < 0 || vaultFramesPerSecond < 0
            || (vaultFrames == 0) != (vaultFramesPerSecond == 0) || vaultFramesPerSecond > 1000) throw new ArgumentException("GAIT_STRIDE_INVALID");
        Walk = walk; Run = run; Speed = speed;
        // The vault frame time is the ART rate alone (1000 / fps ms, the source's set-animation pause, research W4); the
        // 30..800 ms clamp belongs to the Speed-scaled move rate (L6) and does not apply.
        Vault = vaultFrames == 0 ? null : StepDuration.Of(vaultFrames * (long)(1000 / vaultFramesPerSecond), 1000);
        (_walkAxis, _walkDiagonal) = Durations(walk, FramesPerSecond(MovementGait.Walk, speed));
        (_runAxis, _runDiagonal) = Durations(run, FramesPerSecond(MovementGait.Run, speed));
    }

    public GaitStride Walk { get; }
    public GaitStride Run { get; }
    public int Speed { get; }
    /// <summary>The window-jump (vault) step duration, or null without a vault.</summary>
    public StepDuration? Vault { get; }

    /// <summary>The fixed diagnostic human (ARC-RES-047 L7, HumanFixed walk 2668 / run 2671, vault 2677) at base Speed 8.</summary>
    public static GaitTiming HumanFixed { get; } = new(ActorDiagnosticProfile.HumanFixedArea.Walk.Stride!, ActorDiagnosticProfile.HumanFixedArea.Run!.Stride!, 8,
        ActorDiagnosticProfile.HumanFixedArea.Vault!.FramesPerGroup, ActorDiagnosticProfile.HumanFixedArea.Vault.RawFramesPerSecond);

    public StepDuration For(MovementGait gait, NeighborDirection direction)
    {
        var diagonal = direction is NeighborDirection.XMinusYMinus or NeighborDirection.XMinusYPlus
            or NeighborDirection.XPlusYPlus or NeighborDirection.XPlusYMinus;
        return (gait, diagonal) switch
        {
            (MovementGait.Walk, false) => _walkAxis, (MovementGait.Walk, true) => _walkDiagonal,
            (MovementGait.Run, false) => _runAxis, (MovementGait.Run, true) => _runDiagonal,
            _ => throw new ArgumentOutOfRangeException(nameof(gait)),
        };
    }

    public static string Provenance(MovementGait gait) => gait == MovementGait.Run ? RunProvenance : WalkProvenance;

    /// <summary>Frames per second of a human (not dwarf or halfling) walking or running at a Speed: below 8 from the
    /// low value, 8 the middle value (the original, ledger LA1), above 8 Speed/30 of the upper
    /// span, 30 and more the high value (research L6).</summary>
    public static int FramesPerSecond(MovementGait gait, int speed)
    {
        var (middle, low, high) = gait == MovementGait.Run ? (20, 8, 30) : (17, 6, 30);
        if (speed < 8) return low + speed * (middle - low) / 8;
        if (speed == 8) return middle;
        return speed < 30 ? middle + speed * (high - middle) / 30 : high;
    }

    /// <summary>Real milliseconds per frame: 1000 / fps, clamped to 30..800.</summary>
    public static int FrameMilliseconds(int fps) => fps <= 0 ? throw new ArgumentOutOfRangeException(nameof(fps)) : Math.Clamp(1000 / fps, 30, 800);

    private static (StepDuration Axis, StepDuration Diagonal) Durations(GaitStride stride, int fps)
    {
        // seconds = frames × ms × |step|² ÷ (1000 × cycle·step)
        var cycleMs = (long)stride.FramesPerCycle * FrameMilliseconds(fps);
        const long axisSquared = GaitStride.AxisAlong * GaitStride.AxisAlong + GaitStride.AxisAcross * GaitStride.AxisAcross;
        var axisDot = (long)stride.AxisDx * GaitStride.AxisAlong + (long)stride.AxisDy * GaitStride.AxisAcross;
        var axis = StepDuration.Of(cycleMs * axisSquared, 1000L * axisDot);
        var diagonal = StepDuration.Of(cycleMs * GaitStride.DiagonalStep, 1000L * stride.DiagonalPixels);
        return (axis, diagonal);
    }
}
