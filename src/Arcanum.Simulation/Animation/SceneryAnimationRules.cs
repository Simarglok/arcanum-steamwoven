using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Animation;

/// <summary>
/// ARC-RENDER-035: the original's scenery animation loop (ledger AS1–AS7): the start (0x43F710) runs the goal 1
/// <c>ag_animate_loop</c>, or goal 77 <c>ag_animate_loop_fire_dmg</c> for scenery flag 0x10, which steps its frames the same way.
/// </summary>
/// <summary>ARC-DEV-144 (GT78): what the scenery restart 0x43F710 does for an object: nothing, kill its loop run, or start the loop.</summary>
public enum SceneryRestart { Skip, Kill, Start }

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

    /// <summary>
    /// ARC-DEV-144 (MIG13 S9c, GT78): the restart 0x43F710 that the object-list insert, an art change, a cleared off flag and an object's creation call. A destroyed or off object and anything but scenery is skipped. A scenery with flag 0x1 (no
    /// auto-animation) has its run <b>killed</b>, unless a run is being stepped now (<paramref name="stepping"/>: then nothing); otherwise an ART of more than one frame starts the loop (the starter refuses while a main run exists, so a running
    /// loop keeps its own art), and a one-frame ART starts it only for an ambient sound, else kills (the same guard). The stoned flag is the begin node's refusal, not the restart's.
    /// </summary>
    public static SceneryRestart Restart(int type, uint objectFlags, uint sceneryFlags, int frameCount, bool ambientSound, bool stepping)
    {
        if (type != SceneryType || (objectFlags & HiddenFlags) != 0) return SceneryRestart.Skip;
        if ((sceneryFlags & NoAutoAnimate) != 0) return stepping ? SceneryRestart.Skip : SceneryRestart.Kill;
        if (frameCount > 1 || (frameCount == 1 && ambientSound)) return SceneryRestart.Start;
        return stepping ? SceneryRestart.Skip : SceneryRestart.Kill;
    }

    /// <summary>The pause between two frames: 1000 / the ART's frame rate, unsigned (AS3). A rate that was not read (0) takes the
    /// unread ART's 100 ms (the original would divide by zero there), and a rate above 1000 waits 1 ms (the original 0); no
    /// installed animated scenery has either.</summary>
    [OriginalFunction(0x42DA50, OriginalFunctionScope.Partial)]
    public static long PauseMs(int framesPerSecond) => framesPerSecond > 0 ? Math.Max(1, 1000 / framesPerSecond) : UnreadPauseMs;

    /// <summary>The next frame: the following one, frame 0 after the last (AS5).</summary>
    [OriginalFunction(0x43F7F0, OriginalFunctionScope.Partial)]
    public static int NextFrame(int frame, int frameCount) => frame + 1 >= frameCount ? 0 : frame + 1;

    /// <summary>Whether an object tile is near the PC's tile (AS4).</summary>
    [OriginalFunction(0x42DCF0, OriginalFunctionScope.Partial)]
    public static bool Near(long objectX, long objectY, CellCoord pc) => Math.Max(Math.Abs(objectX - pc.X), Math.Abs(objectY - pc.Y)) < NearTiles;
}

/// <summary>
/// ARC-RENDER-035, ARC-DEV-142 (MIG13 S9a): one scenery's loop state: the frame shown and the object offset its frames' deltas moved it by (AS6). The loop's timing is the scenery's goal run (goals 1 and 77, the nodes
/// of <c>ExecutorSceneryGoals</c>): every step the run makes calls <see cref="Step"/>.
/// </summary>
public sealed class SceneryLoop
{
    private readonly IReadOnlyList<(int Dx, int Dy)> _deltas;

    /// <param name="deltas">Each frame's delta, in frame order; their count is the ART's frame count.</param>
    public SceneryLoop(long x, long y, IReadOnlyList<(int Dx, int Dy)> deltas, int frame, long pauseMs)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count < 2 || frame < 0 || frame >= deltas.Count || pauseMs <= 0) throw new ArgumentOutOfRangeException(nameof(deltas));
        X = x; Y = y; _deltas = deltas; Frame = frame; PauseMs = pauseMs;
    }

    public long X { get; }
    public long Y { get; }
    public int FrameCount => _deltas.Count;
    public long PauseMs { get; }
    public int Frame { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public long Steps { get; private set; }

    /// <summary>Steps to the next frame (the first after the last wraps to 0, AS5) and moves the offset by that frame's delta (AS6). Returns the new frame.</summary>
    public int Step()
    {
        Frame = SceneryAnimationRules.NextFrame(Frame, FrameCount);
        OffsetX = checked(OffsetX + _deltas[Frame].Dx); OffsetY = checked(OffsetY + _deltas[Frame].Dy);
        Steps++;
        return Frame;
    }
}
