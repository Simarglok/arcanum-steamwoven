using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>Which critter-state goal a life step starts.</summary>
public enum CritterStateKind { None, Dying, KnockDown, Stun, GetUp }

/// <summary>What a life step starts for an NPC: the goal's starter plan and frame, or <see cref="CritterStateKind.None"/>; <see cref="ClearStun"/> when the stun was proposed but
/// its start failed, so the damage path drops the stunned bit (GT31).</summary>
public sealed record CritterStateStart(CritterStateKind Kind, GoalStartPlan? Plan, GoalFrame? Frame)
{
    public static CritterStateStart Nothing { get; } = new(CritterStateKind.None, null, null);
}

/// <summary>
/// ARC-DEV-137 (MIG13 S6, GT28–GT34): which critter-state goal an NPC's life step starts, as the original's damage path (0x4B4390), the fatigue setter (0x45D5A0), the recovery event (0x45E610) and
/// the kill (0x45DA20) start them. A death starts dying (priority 5, whatever else the step did); otherwise a knock-down or knock-out result or a critter losing consciousness starts the
/// knock-down unless the critter already lies (the starter refuses a critter whose art shows the fall, GT29); otherwise a stun result starts the stun with the countdown from the critter's
/// constitution; a critter that comes round starts the get-up. A knock-down result wins over a stun result (GT31).
/// </summary>
public static class CritterStateRules
{
    /// <summary>The start for <paramref name="outcome"/> of an NPC with <paramref name="facts"/>. <paramref name="lying"/>: its art shows the fall (it was unconscious, or a knock-down run plays).</summary>
    public static CritterStateStart Decide(LifeOutcome outcome, LifeFacts facts, bool lying)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(facts);
        var events = outcome.Events;
        if (events.HasFlag(LifeEvents.AlreadyDead)) return CritterStateStart.Nothing;
        if (events.HasFlag(LifeEvents.Died))
            return new(CritterStateKind.Dying, GoalStarters.Generic, AiExecutorRules.DyingFrame(AiExecutorRules.DyingAnimationOf(outcome.Death), lying));
        var after = LifeRules.Evaluate(outcome.After, facts);
        if (events.HasFlag(LifeEvents.Resurrected)) return CritterStateStart.Nothing;
        if ((events & (LifeEvents.KnockedDown | LifeEvents.KnockedOut | LifeEvents.LostConsciousness)) != 0)
            return lying || after.IsDead ? CritterStateStart.Nothing : new(CritterStateKind.KnockDown, GoalStarters.KnockDown, AiExecutorRules.KnockDownFrame());
        if (events.HasFlag(LifeEvents.Stunned))
            return new(CritterStateKind.Stun, GoalStarters.Stun, AiExecutorRules.StunFrame(facts.Constitution));
        if (events.HasFlag(LifeEvents.RegainedConsciousness) && !after.IsDead && !after.IsUnconscious)
            return new(CritterStateKind.GetUp, GoalStarters.Generic, AiExecutorRules.GetUpFrame());
        return CritterStateStart.Nothing;
    }
}
