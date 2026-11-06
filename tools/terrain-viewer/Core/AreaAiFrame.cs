using Arcanum.Session;
using Arcanum.Simulation.Ai;
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

/// <summary>
/// ARC-DEV-138 (MIG13 S7, goal-table.md §3.5): the frame step's one goal-step host for the viewer: a run whose goal is a door goal (70, 71, 72: a portal owns it) goes to the shown map's door host,
/// every other run to the AI host's steps. The door host exists in map mode whether or not the AI does (the portals are bound with the session), so a door animates in the automated runs that
/// turn the AI off. A door run with no door host (another map's portals, a transfer in flight) ends; its door stays where it stands. ARC-DEV-139 (MIG13 S8): a spell missile's run (goal 28 on a created
/// object) goes to the missile host the same way; ARC-DEV-141 (S8c): a knockback's run (goal 43 on the pushed NPC) goes to the knockback host; ARC-DEV-142 (S9a): a scenery's loop run (goal 1 or 77) goes to the scenery host and ends without one; ARC-DEV-143 (S9b): an overlay run (an eye candy 45–48, 58, 59, 75, 76 or the hover 44, on any object) goes to the eye-candy host and ends without one; a missile run with no missile host ends (its object is the host's to clean, the session's map close clears the objects).
/// </summary>
public sealed class AreaGoalStepRouter(Func<AreaDoorHost?> doors, IGoalStepHost ai, Func<AreaMissileHost?>? missiles = null, Func<AreaProjectileHost?>? projectiles = null, Func<AreaKnockbackHost?>? knockbacks = null, Func<AreaSceneryHost?>? sceneries = null, Func<AreaEyeCandyHost?>? overlays = null) : IGoalStepHost
{
    /// <summary>The door runs that found no door host and ended.</summary>
    public int DoorsLost { get; private set; }

    /// <summary>The missile runs that found no missile host and ended.</summary>
    public int MissilesLost { get; private set; }

    /// <summary>ARC-DEV-140: the projectile runs that found no projectile host and ended.</summary>
    public int ProjectilesLost { get; private set; }

    /// <summary>ARC-DEV-141: the knockback runs that found no knockback host and ended (the pushed object stays where it stands).</summary>
    public int KnockbacksLost { get; private set; }

    /// <summary>ARC-DEV-142: the scenery loop runs that found no scenery host and ended (the scenery keeps the frame it shows).</summary>
    public int SceneriesLost { get; private set; }

    /// <summary>ARC-DEV-143: the overlay runs (eye candy, hover) that found no overlay host and ended (the parts leave with the run).</summary>
    public int OverlaysLost { get; private set; }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        if (AiExecutorRules.IsOverlayRunGoal(run.Bottom.Goal))
        {
            if (overlays?.Invoke() is { } showing) return showing.StepGoal(run, clocks);
            // An overlay of a session the view has left: the run ends and its parts leave with it.
            OverlaysLost++;
            return SessionGoalStepAnswer.End;
        }
        if (AiExecutorRules.IsSceneryRunGoal(run.Bottom.Goal))
        {
            if (sceneries?.Invoke() is { } looping) return looping.StepGoal(run, clocks);
            // A loop of a map or window the view has left: the run ends and its scenery stays on the frame it shows.
            SceneriesLost++;
            return SessionGoalStepAnswer.End;
        }
        if (AiExecutorRules.IsMissileRunGoal(run.Bottom.Goal))
        {
            if (missiles?.Invoke() is { } flying) return flying.StepGoal(run, clocks);
            // Unreachable in play (the host exists from the first launch); the missile object of such a run would be cleared with its map.
            MissilesLost++;
            return SessionGoalStepAnswer.End;
        }
        if (AiExecutorRules.IsProjectileRunGoal(run.Bottom.Goal))
        {
            if (projectiles?.Invoke() is { } flying) return flying.StepGoal(run, clocks);
            // Unreachable in play (the host exists from the first launch); the projectile object of such a run would be cleared with its map.
            ProjectilesLost++;
            return SessionGoalStepAnswer.End;
        }
        if (AiExecutorRules.IsKnockbackRunGoal(run.Bottom.Goal))
        {
            if (knockbacks?.Invoke() is { } pushing) return pushing.StepGoal(run, clocks);
            // Unreachable in play (the host exists from the first push); the pushed object just stays where it stands.
            KnockbacksLost++;
            return SessionGoalStepAnswer.End;
        }
        if (!AiExecutorRules.IsDoorRunGoal(run.Bottom.Goal)) return ai.StepGoal(run, clocks);
        if (doors() is { } host) return host.StepGoal(run, clocks);
        DoorsLost++;
        return SessionGoalStepAnswer.End;
    }
}
