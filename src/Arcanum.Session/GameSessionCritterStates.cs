using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-137 (MIG13 S6, GT28–GT34): the critter-state goals an NPC's life step starts on the table, the starters' work the original does inside the damage path, the fatigue setter, the recovery
    /// event and the kill (<see cref="CritterStateRules.Decide"/>). Called by <see cref="Life"/> after the step committed, for an NPC of the active map only (its runs belong to the table of this
    /// map). A death starts dying (whose node plays the dying animation and then the pool is the viewer's), a knock-down result or lost consciousness the knock-down, a stun result the stun
    /// (a failed start, an interrupt refused by a priority-5 run, drops the stunned bit again as the damage path does), a critter that comes round the get-up (its gate: in a round only the current
    /// participant). The first step of each is 5 animation milliseconds later (GS7), when the AI host's stepper plays it.
    /// </summary>
    private void StartCritterStates(SessionLifeTarget target, LifeOutcome outcome, LifeFacts facts)
    {
        // A life step inside a spell run (spell damage, the busy window of the spell system) cannot start a goal: the table refuses while the spell-run rules hold it. The start waits
        // for the system's end (DrainCritterStarts), as the stun's call of the starter does not fail in the original.
        if (TableBusy())
        {
            if (_pendingCritterStarts.Count < MaxPendingCritterStarts) _pendingCritterStarts.Add((target, outcome, facts, _actor.Map.RuntimeMap));
            return;
        }
        var entity = target.Entity!;
        var table = _goals;
        var before = LifeRules.Evaluate(outcome.Before, facts);
        var lying = before.IsDead || before.IsUnconscious || table.RunsOf(entity).Any(run => run.Frames.Any(f => f.Goal == GoalType.KnockDown));
        var start = CritterStateRules.Decide(outcome, facts, lying);
        // The damage path drops the stunned bit again when the stun did not start (a knock-down result won, the start was refused): a stun result that starts no stun run leaves no bit behind.
        var stunDropped = outcome.Events.HasFlag(LifeEvents.Stunned) && start.Kind != CritterStateKind.Stun;
        if (start.Kind != CritterStateKind.None)
        {
            // The stepped run is only marked done by the start's interrupts (a run done before the start is the host's own and is not claimed here).
            var steppedBefore = _goalStepOpen is { } open && _goals.TryGet(open, out var openRun) && openRun.Owner == entity && !openRun.Done ? open : (GoalHandle?)null;
            var started = StartGoal(new GoalStartRequest(entity, start.Plan!, start.Frame!), new CritterStatePorts(this, entity));
            // The runs the start's interrupts ended (a stun the knock-down ends, the walk a stun interrupts) are the session's own ends: the host runs their end nodes (the art reset, the stun's bit).
            Unclaimed(started.Ended);
            // The run being stepped (the NPC's own step changed its own life) is only marked done by the interrupt; its close reports the end, so the host must claim it.
            if (steppedBefore is { } stepped && _goals.TryGet(stepped, out var steppedNow) && steppedNow.Done) _goalMarkedBySession.Add(stepped);
            if (!started.Started && start.Kind == CritterStateKind.Stun) stunDropped = true;
        }
        if (stunDropped) ClearStun(Version, target);
    }

    private readonly List<(SessionLifeTarget Target, LifeOutcome Outcome, LifeFacts Facts, int Map)> _pendingCritterStarts = [];
    private const int MaxPendingCritterStarts = 256;

    /// <summary>The critter-state starts a life step asked for while the spell system held the table: run now, in order (the end of the spell system, ARC-DEV-137).</summary>
    internal void DrainCritterStarts()
    {
        if (_pendingCritterStarts.Count == 0 || TableBusy()) return;
        var pending = _pendingCritterStarts.ToArray();
        _pendingCritterStarts.Clear();
        // A start that waited is dropped when its NPC is no longer on the active map (its runs belong to the table of that map).
        foreach (var (target, outcome, facts, map) in pending)
            if (map == _actor.Map.RuntimeMap) StartCritterStates(target, outcome, facts);
    }

    /// <summary>
    /// ARC-DEV-137 (GT27, 0x427000 called by the round's participant test 0x4B7580): a round that reaches an NPC which cannot act skips its turn, and a stunned NPC whose first run is the stun
    /// takes 3 off its countdown; at 0 or below the run is interrupted at level 6 (its end node clears the stunned bit and stands the critter), and that turn is still skipped. Out of a round the
    /// animation's own plays count (<see cref="AiExecutorRules"/>' stun node). A run being stepped keeps its count (the call is refused then, as any frame replacement is).
    /// </summary>
    internal void CountStunnedTurn(string entity)
    {
        if (_goals.FirstRunOf(entity) is not { } run || run.Bottom.Goal != GoalType.AnimateStunned) return;
        var count = run.Bottom.Count - AiExecutorRules.StunTurnCost;
        if (count > 0)
        {
            var frames = run.Frames.SetItem(0, run.Bottom with { Count = count });
            ReplaceGoalFrames(run.Handle, frames);
            return;
        }
        var change = InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel);
        Unclaimed(change.Ended);
        MarkedBySession(change.Steps);
    }

    private sealed class CritterStatePorts(GameSessionOwner owner, string entity) : IGoalStartPorts
    {
        public bool Loading => false;
        public bool IsDestroyed(string candidate) => false;

        // The get-up's gate (0x4348E0 with cost 0): outside a round it passes, in a round only the current participant does. The other starters have no gate here: the life rules already
        // applied the stun starter's (alive, conscious, not stunned or paralysed or stoned) and the knock-down's refusals are the caller's.
        public bool GatePasses(GoalStartRequest request) =>
            request.Frame.Goal != GoalType.GetUp || owner._combat.Round is not { } round || round.Actor == SessionCombatant.Critter(entity);
    }
}
