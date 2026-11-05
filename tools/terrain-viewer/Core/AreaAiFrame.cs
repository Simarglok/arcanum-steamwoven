using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-124 (MIG12 S5, docs/architecture/clocks-frame.md §3.3, TM6): the AI host's place in the frame step. The frame step calls
/// <see cref="Step"/> once per frame after the event passes (the AI ticks, which are game-time events, have fired by then). ARC-DEV-134 (MIG13 S4): the goal runs
/// no longer step here: each is a session run whose <c>goal.step</c> fires on the animation clock inside the frame's passes (<see cref="AreaAiGoalSteps"/>), so they stop
/// with the pause (TL23, TL40) and take a round's jump once (TL38); here the host's frame (<see cref="AreaAiHost.Frame"/>) settles the movers on the frame's
/// <see cref="FrameClocks.WorldMs"/> and observes the round after the last step, and the steps the host took are counted. The participant outlives the host (the host is rebuilt when the party or the map changes), so it asks
/// <c>host</c> for the shown map's host at every frame and holds nothing of it. Nothing here reads a clock but the one given.
/// <list type="bullet">
/// <item>A paused frame (the Esc menu and a save or load list; no other window pauses since MIG12 S7) runs no goal step: the original's animation clock does not advance behind the pause. The view's sector touch still runs.</item>
/// <item>A frozen frame (a loop of the original that does not ping, TL25) does nothing: no stamp was observed, so no time passed.</item>
/// <item><c>before</c> runs first for a host (the viewer's per-frame sector touch, so a run of an evicted sector waits as the session's cache says, RL16). ARC-DEV-134: the
/// frame's goal steps came before it, so a run whose sector this touch loads goes on in the next frame.</item>
/// </list>
/// </summary>
public sealed class AreaAiFrameParticipant(Func<AreaAiHost?> host, Action<AreaAiHost>? before = null) : IFrameParticipant
{
    private int _steps;

    /// <summary>The frames this participant was called in (a test and the diagnostic line read it).</summary>
    public int Frames { get; private set; }

    /// <summary>The frames in which the host's frame ran (not paused, not frozen).</summary>
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
        // ARC-DEV-134 (MIG13 S4): the runs stepped in the frame's passes (AreaAiGoalSteps); the host's frame settles the movers and observes the round after them.
        ai.Frame(clocks.WorldMs);
        _steps += ai.TakeSteps();
    }
}

/// <summary>
/// ARC-DEV-134 (MIG13 S4, docs/architecture/goal-table.md §3.2): the frame step's goal-step host (<see cref="FrameStep.GoalSteps"/>) for the viewer. Like the participant it
/// outlives the AI host (rebuilt when the party or the map changes) and asks <c>host</c> for the shown map's host at every step. With no host (none built yet, a transfer
/// in flight, another session's) a run waits about a frame (<see cref="NoHostWaitMs"/>), as the pump that did not run held it; a run is ended only by its owner's host or the session.
/// </summary>
public sealed class AreaAiGoalSteps(Func<AreaAiHost?> host) : IGoalStepHost
{
    /// <summary>The wait of a run with no host, in real milliseconds: about one frame at 60 frames a second.</summary>
    public const long NoHostWaitMs = 16;

    /// <summary>The steps that found no host and waited.</summary>
    public int Waited { get; private set; }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        if (host() is { } ai) return ai.StepGoal(run, clocks);
        Waited++;
        return SessionGoalStepAnswer.After(NoHostWaitMs);
    }
}
