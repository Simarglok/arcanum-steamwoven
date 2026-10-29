using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Animation;

/// <summary>
/// ARC-RENDER-035: the original's scenery animation loop (ledger AS1–AS7): the start (0x43F710) runs the goal 1
/// <c>ag_animate_loop</c>, or goal 77 <c>ag_animate_loop_fire_dmg</c> for scenery flag 0x10, which steps its frames the same way.
/// </summary>
public static class SceneryAnimationRules
{
    /// <summary>Within this many tiles of the local PC (Chebyshev, 0x441AE0) the loop steps its frames (AS4).</summary>
    public const int NearTiles = 30;
    /// <summary>The loop measures the distance again on every third check (AS4).</summary>
    public const int ChecksPerMeasure = 3;
    /// <summary>A loop away from the PC checks again after this many ms without stepping (AS4).</summary>
    public const long FarRetryMs = 800;
    /// <summary>The pause when the ART cannot be read (AS3).</summary>
    public const long UnreadPauseMs = 100;
    public const int SceneryType = 3;
    /// <summary>Object flags destroyed and off, scenery flag no auto-animation (AS1), spell flag stoned (AS3).</summary>
    public const uint HiddenFlags = 0x3, NoAutoAnimate = 0x1, Stoned = 0x10000;

    /// <summary>Whether the start runs a drawn loop on a scenery: not destroyed or off, no scenery flag 0x1, not stoned (the
    /// begin node refuses it), an ART of more than one frame (AS1, AS3). A one-frame ART with an ambient sound runs the loop for
    /// its sound only, which draws nothing.</summary>
    [OriginalFunction(0x43F710, OriginalFunctionScope.Partial)]
    public static bool Starts(int type, uint objectFlags, uint sceneryFlags, uint spellFlags, int frameCount) =>
        type == SceneryType && (objectFlags & HiddenFlags) == 0 && (sceneryFlags & NoAutoAnimate) == 0 && (spellFlags & Stoned) == 0 && frameCount > 1;

    /// <summary>The pause between two frames: 1000 / the ART's frame rate, unsigned (AS3). A rate that was not read (0) takes the
    /// unread ART's 100 ms; the original would divide by zero there, and no installed animated scenery has it.</summary>
    [OriginalFunction(0x42DA50, OriginalFunctionScope.Partial)]
    public static long PauseMs(int framesPerSecond) => framesPerSecond > 0 ? 1000 / framesPerSecond : UnreadPauseMs;

    /// <summary>The next frame: the following one, frame 0 after the last (AS5).</summary>
    [OriginalFunction(0x43F7F0, OriginalFunctionScope.Partial)]
    public static int NextFrame(int frame, int frameCount) => frame + 1 >= frameCount ? 0 : frame + 1;

    /// <summary>Whether an object tile is near the PC's tile (AS4).</summary>
    [OriginalFunction(0x42DCF0, OriginalFunctionScope.Partial)]
    public static bool Near(long objectX, long objectY, CellCoord pc) => Math.Max(Math.Abs(objectX - pc.X), Math.Abs(objectY - pc.Y)) < NearTiles;
}

/// <summary>
/// ARC-RENDER-035: one scenery's running loop on a caller's millisecond clock: the frame shown, the object offset its frames'
/// deltas moved it by (AS6), the checks and the last distance answer (AS4).
/// </summary>
public sealed class SceneryLoop
{
    private readonly IReadOnlyList<(int Dx, int Dy)> _deltas;
    private int _checks;
    private bool _near;
    private long _dueMs;

    /// <param name="deltas">Each frame's delta, in frame order; their count is the ART's frame count.</param>
    public SceneryLoop(long x, long y, IReadOnlyList<(int Dx, int Dy)> deltas, int frame, long pauseMs, long nowMs, bool near)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count < 2 || frame < 0 || frame >= deltas.Count || pauseMs <= 0) throw new ArgumentOutOfRangeException(nameof(deltas));
        X = x; Y = y; _deltas = deltas; Frame = frame; PauseMs = pauseMs;
        // The begin node measures the distance and the run waits one pause before its first check (AS3).
        _near = near; _dueMs = nowMs + pauseMs;
    }

    public long X { get; }
    public long Y { get; }
    public int FrameCount => _deltas.Count;
    public long PauseMs { get; }
    public int Frame { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public long Steps { get; private set; }

    /// <summary>Runs the checks that fall due by <paramref name="nowMs"/>; true when the frame changed. A gap longer than
    /// <paramref name="maxChecks"/> checks resynchronises the clock instead of replaying every check.</summary>
    public bool Advance(long nowMs, CellCoord pc, int maxChecks = 64)
    {
        var changed = false;
        for (var check = 0; _dueMs <= nowMs; check++)
        {
            if (check == maxChecks) { _dueMs = nowMs + PauseMs; break; }
            // AS4: every third check measures the distance again; the others keep the last answer.
            if (++_checks >= SceneryAnimationRules.ChecksPerMeasure) { _checks = 0; _near = SceneryAnimationRules.Near(X, Y, pc); }
            if (!_near) { _dueMs += SceneryAnimationRules.FarRetryMs; continue; }
            Frame = SceneryAnimationRules.NextFrame(Frame, FrameCount);
            // AS6: a frame with a non-zero delta moves the object's offset by it.
            OffsetX = checked(OffsetX + _deltas[Frame].Dx); OffsetY = checked(OffsetY + _deltas[Frame].Dy);
            Steps++; changed = true;
            _dueMs += PauseMs;
        }
        return changed;
    }
}
