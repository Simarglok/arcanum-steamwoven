using Arcanum.Simulation.Clock;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-124 (MIG12 S5, docs/architecture/clocks-frame.md §3.3, TM6): the AI host's place in the frame step. The frame step calls
/// <see cref="Step"/> once per frame after the event passes (the AI ticks, which are game-time events, have fired by then), and the
/// goal runs step here on the frame's <see cref="FrameClocks.WorldMs"/>, so they run on one clock that stops with the pause (TL23,
/// TL40: the goal steps are animation-clock events) and does not stop in a round (TL38) or move by a jump (TL26); the viewer no longer
/// reads Godot time for them. The participant outlives the host (the host is rebuilt when the party or the map changes), so it asks
/// <c>host</c> for the shown map's host at every frame and holds nothing of it. Nothing here reads a clock but the one given.
/// <list type="bullet">
/// <item>A paused frame (the Esc menu and the lists it opens; no other window pauses since MIG12 S7) runs no goal step: the original's animation clock does not advance behind the pause. The view's sector touch still runs.</item>
/// <item>A frozen frame (a loop of the original that does not ping, TL25) does nothing: no stamp was observed, so no time passed.</item>
/// <item><c>before</c> runs first for a host (the viewer's per-frame sector touch, so a run of an evicted sector waits as the session's cache says, RL16).</item>
/// </list>
/// </summary>
public sealed class AreaAiFrameParticipant(Func<AreaAiHost?> host, Action<AreaAiHost>? before = null) : IFrameParticipant
{
    private int _steps;

    /// <summary>The frames this participant was called in (a test and the diagnostic line read it).</summary>
    public int Frames { get; private set; }

    /// <summary>The frames in which the host's runs were pumped.</summary>
    public int Pumps { get; private set; }

    /// <summary>The frames held back by a pause.</summary>
    public int Paused { get; private set; }

    /// <summary>The goal steps taken since the last <see cref="TakeSteps"/> (the viewer redraws and refreshes its status line for them).</summary>
    public int PendingSteps => _steps;

    /// <summary>The steps taken since the last call, and clears them: the viewer's per-frame handling of what the runs did.</summary>
    public int TakeSteps() { var steps = _steps; _steps = 0; return steps; }

    /// <inheritdoc />
    public void Step(FrameClocks clocks, FrameFlags flags)
    {
        Frames++;
        if (flags.Frozen || host() is not { } ai) return;
        before?.Invoke(ai);
        if (flags.Paused) { Paused++; return; }
        Pumps++;
        _steps += ai.Pump(clocks.WorldMs);
    }
}
