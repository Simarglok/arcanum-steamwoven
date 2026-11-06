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
        var entity = target.Entity!;
        var table = _goals;
        var before = LifeRules.Evaluate(outcome.Before, facts);
        var lying = before.IsDead || before.IsUnconscious || table.RunsOf(entity).Any(run => run.Frames.Any(f => f.Goal == GoalType.KnockDown));
        var start = CritterStateRules.Decide(outcome, facts, lying);
        if (start.Kind == CritterStateKind.None) return;
        var started = StartGoal(new GoalStartRequest(entity, start.Plan!, start.Frame!), new CritterStatePorts(this, entity));
        // The runs the start's interrupts ended (a stun the knock-down ends, the walk a stun interrupts) are the session's own ends: the host runs their end nodes (the art reset, the stun's bit).
        Unclaimed(started.Ended);
        if (!started.Started && start.Kind == CritterStateKind.Stun)
            ClearStun(Version, target);
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
