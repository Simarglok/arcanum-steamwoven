using Arcanum.Simulation;
using Arcanum.Simulation.Ai;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// Set no-flee (0x4AF1D0) of an NPC: first stop fleeing (0x4AF170, <see cref="MoraleRules.StopFleeing"/>: an NPC in FLEE goes to NONE through the danger setter, with
    /// <paramref name="facts"/>), then <see cref="DangerFlags.NoFlee"/> on its danger record, for good, in one revision (none when the NPC is not fleeing and the bit is set
    /// already). The setter then turns every FLEE request into COMBAT_FOCUS (rule 2). Ending the NPC's flee goals, which the original's stop fleeing does in the FLEE state
    /// only, is the host's. The move-away goal asks it (<see cref="AiExecutorRules.Step"/>: a flight with no escape line, or one bent after a partial straight line), and the
    /// start of mind control.
    /// </summary>
    [OriginalFunction(0x4AF1D0, OriginalFunctionScope.Partial)]
    public SessionDangerResult SetNoFlee(GameSessionVersion expected, string entity, DangerSetterFacts facts = default)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var stop = PlanStopFleeing(plan, entity, facts);
        if (stop.Failure != GameSessionFailure.None) return RejectDanger(stop.Failure);
        plan.Update(entity, record => record with { Flags = record.Flags | DangerFlags.NoFlee });
        return plan.Commit(stop);
    }
}
