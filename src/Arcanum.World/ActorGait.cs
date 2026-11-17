using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>How an actor covers one step (ARC-WORLD-020, research ARC-RES-047 L1–L8). ARC-RENDER-2539: <see cref="Sneak"/> is the stealth walk
/// (animation 3) of a concealed critter below prowling expert (the gait function 0x42EDC0); appended so the stored numbers of the others hold.</summary>
public enum MovementGait { Walk, Run, Sneak }

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
    /// <summary>ARC-RENDER-2539: provenance code of a stealth-walk step (drawn with the stealth ART, animation 3, else the walk ART).</summary>
    public const string SneakProvenance = "GAIT_SNEAK";
    /// <summary>ARC-WORLD-021: provenance code of a window-jump step, drawn with the vault ART.</summary>
    public const string VaultProvenance = "GAIT_VAULT";
    private readonly StepDuration _walkAxis, _walkDiagonal, _runAxis, _runDiagonal, _sneakAxis, _sneakDiagonal;

    /// <summary>The Speed the viewer's hero walks and runs at (the middle rate, research L6).</summary>
    public const int BaseSpeed = 8;

    /// <param name="vaultFrames">The vault ART's frames; with <paramref name="vaultFramesPerSecond"/> it gives the
    /// window-jump step one frame per 1000/fps ms, without Speed scaling (research ARC-RES-047 W4). Zero means no
    /// vault: a window jump then takes the walk or run step time.</param>
    /// <param name="rateBonus">ARC-UI-039: what the body adds to every walk and run rate: 4 for a dwarf or halfling body, else 0
    /// (research L6, <see cref="HeroBody.GaitRateBonus"/>).</param>
    /// <param name="sneak">ARC-RENDER-2539: the stealth ART's stride (animation 3). Null for a body without that ART: the repair then plays the walk ART
    /// (ledger MA1, pair 3 to 1), so the walk's stride goes at the stealth rate.</param>
    public GaitTiming(GaitStride walk, GaitStride run, int speed, int vaultFrames = 0, int vaultFramesPerSecond = 0, int rateBonus = 0, GaitStride? sneak = null)
    {
        ArgumentNullException.ThrowIfNull(walk); ArgumentNullException.ThrowIfNull(run);
        if (!walk.IsValid || !run.IsValid || sneak is { IsValid: false } || speed < 0 || vaultFrames < 0 || vaultFramesPerSecond < 0
            || (vaultFrames == 0) != (vaultFramesPerSecond == 0) || vaultFramesPerSecond > 1000 || rateBonus is not (0 or 4)) throw new ArgumentException("GAIT_STRIDE_INVALID");
        Walk = walk; Run = run; Sneak = sneak ?? walk; Speed = speed; RateBonus = rateBonus;
        // The vault frame time is the ART rate alone (1000 / fps ms, the source's set-animation pause, research W4); the
        // 30..800 ms clamp belongs to the Speed-scaled move rate (L6) and does not apply.
        Vault = vaultFrames == 0 ? null : StepDuration.Of(vaultFrames * (long)(1000 / vaultFramesPerSecond), 1000);
        (_walkAxis, _walkDiagonal) = Durations(walk, FramesPerSecond(MovementGait.Walk, speed, rateBonus));
        (_runAxis, _runDiagonal) = Durations(run, FramesPerSecond(MovementGait.Run, speed, rateBonus));
        (_sneakAxis, _sneakDiagonal) = Durations(Sneak, FramesPerSecond(MovementGait.Sneak, speed, rateBonus));
    }

    public GaitStride Walk { get; }
    public GaitStride Run { get; }
    /// <summary>ARC-RENDER-2539: the stealth walk's stride: the stealth ART's, else the walk's (a body without animation 3).</summary>
    public GaitStride Sneak { get; }
    public int Speed { get; }
    /// <summary>ARC-UI-039: the body's rate bonus (0, or 4 for a dwarf or halfling body).</summary>
    public int RateBonus { get; }
    /// <summary>The window-jump (vault) step duration, or null without a vault.</summary>
    public StepDuration? Vault { get; }

    /// <summary>The fixed diagnostic human (ARC-RES-047 L7, HumanFixed walk 2668 / run 2671, vault 2677) at base Speed 8.</summary>
    public static GaitTiming HumanFixed { get; } = new(ActorDiagnosticProfile.HumanFixedArea.Walk.Stride!, ActorDiagnosticProfile.HumanFixedArea.Run!.Stride!, BaseSpeed,
        ActorDiagnosticProfile.HumanFixedArea.Vault!.FramesPerGroup, ActorDiagnosticProfile.HumanFixedArea.Vault.RawFramesPerSecond);

    public StepDuration For(MovementGait gait, NeighborDirection direction)
    {
        var diagonal = direction is NeighborDirection.XMinusYMinus or NeighborDirection.XMinusYPlus
            or NeighborDirection.XPlusYPlus or NeighborDirection.XPlusYMinus;
        return (gait, diagonal) switch
        {
            (MovementGait.Walk, false) => _walkAxis, (MovementGait.Walk, true) => _walkDiagonal,
            (MovementGait.Run, false) => _runAxis, (MovementGait.Run, true) => _runDiagonal,
            (MovementGait.Sneak, false) => _sneakAxis, (MovementGait.Sneak, true) => _sneakDiagonal,
            _ => throw new ArgumentOutOfRangeException(nameof(gait)),
        };
    }

    /// <summary>ARC-UI-039: the rate a walk or run ART plays at while stepping: this timing's Speed and body bonus (research L6), which
    /// the step durations use too. A dwarf or halfling body plays faster than its ART header says.</summary>
    public int ArtRate(MovementGait gait) => FramesPerSecond(gait, Speed, RateBonus);

    public static string Provenance(MovementGait gait) => gait switch { MovementGait.Run => RunProvenance, MovementGait.Sneak => SneakProvenance, _ => WalkProvenance };

    /// <summary>Frames per second of a human (not dwarf or halfling) walking or running at a Speed: below 8 from the
    /// low value, 8 the middle value (the original, ledger LA1), above 8 Speed/30 of the upper
    /// span, 30 and more the high value (research L6; ARC-RENDER-2539: the stealth walk's values are 10, 4 and 16). ARC-UI-039: <paramref name="rateBonus"/> (4 for a dwarf or halfling
    /// body) is added to the low, middle and high values first.</summary>
    public static int FramesPerSecond(MovementGait gait, int speed, int rateBonus = 0)
    {
        var (middle, low, high) = gait switch
        {
            MovementGait.Run => (20 + rateBonus, 8 + rateBonus, 30 + rateBonus),
            MovementGait.Sneak => (10 + rateBonus, 4 + rateBonus, 16 + rateBonus),
            _ => (17 + rateBonus, 6 + rateBonus, 30 + rateBonus),
        };
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
