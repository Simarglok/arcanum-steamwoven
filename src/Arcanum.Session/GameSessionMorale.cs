using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>Perform fleeing (0x4AC620) of the fleeing NPC <see cref="Entity"/>: the setter's facts for its SURRENDER request and
/// the NPC's combat-mode facts the surrender's combat-mode exit needs when it is in combat mode (the toggle's refusals).</summary>
public sealed record SessionPerformFleeingRequest
{
    public required string Entity { get; init; }
    public DangerSetterFacts Facts { get; init; } = new();
    public SessionCombatModeContext? CombatMode { get; init; }
}

/// <summary>What the flee-spell step did: the outcome (none when the call was rejected).</summary>
public sealed record SessionFleeSpellResult(GameSessionDisposition Disposition, GameSessionFailure Failure, GameSessionVersion Version)
{
    public FleeSpellOutcome? Outcome { get; init; }
}

/// <summary>The kill notification's writes (0x4AA1B0) for <see cref="Victim"/>, as <see cref="Plan"/>
/// (<see cref="MoraleRules.KillNotification"/>) decided them, with the setter's facts for the victim's stop fleeing.</summary>
public sealed record SessionKillNotificationRequest
{
    public required SessionCombatant Victim { get; init; }
    public required KillNotificationPlan Plan { get; init; }
    public DangerSetterFacts VictimFacts { get; init; } = new();
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The flee-spell step (<see cref="MoraleRules.FleeSpell"/>) of a fleeing NPC in one revision: SPELL_FLEE set → cleared and
    /// nothing chosen; otherwise <paramref name="choosesFleeAction"/> (the host's list chooser over the NPC's flee list, which
    /// must not call back into the owner) is asked once and a choice sets SPELL_FLEE. An NPC not in FLEE is
    /// <see cref="GameSessionFailure.InvalidDangerFacts"/> (the danger update runs the step only in FLEE).
    /// </summary>
    public SessionFleeSpellResult RunFleeSpellStep(GameSessionVersion expected, string entity, Func<bool> choosesFleeAction)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(choosesFleeAction);
        if (!SessionValidation.Entity(entity)) return RejectFleeSpell(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectFleeSpell(GameSessionFailure.StaleVersion);
        var record = _danger.Get(entity);
        if (record.State != DangerState.Flee) return RejectFleeSpell(GameSessionFailure.InvalidDangerFacts);
        var step = MoraleRules.FleeSpell(record.Flags, choosesFleeAction);
        var plan = new DangerPlan(this);
        plan.Update(entity, r => r with { Flags = step.Flags });
        return plan.Commit(new SessionFleeSpellResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version) { Outcome = step.Outcome },
            (r, disposition, version) => r with { Disposition = disposition, Version = version }, RejectFleeSpell);
    }

    /// <summary>
    /// Perform fleeing (0x4AC620) in one revision: the setter asked for SURRENDER from what the NPC flees (its fleeing-from
    /// handle, which stays the record's fleeing-from), as <see cref="SetDangerState"/> applies it, and
    /// <see cref="SessionDangerResult.StartsFleeGoal"/> when the flee goal away from that source starts
    /// (<see cref="MoraleRules.StartsFleeGoal"/>, MR9); the goal is the executor's. An NPC not in FLEE is
    /// <see cref="GameSessionFailure.InvalidDangerFacts"/>, as is a surrender of an NPC in combat mode without
    /// <see cref="SessionPerformFleeingRequest.CombatMode"/> facts.
    /// </summary>
    [OriginalFunction(0x4AC620, OriginalFunctionScope.Partial)]
    public SessionDangerResult PerformFleeing(GameSessionVersion expected, SessionPerformFleeingRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!SessionValidation.Entity(request.Entity)) return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (request.CombatMode is { Facts: { } facts } && !ValidFacts(facts)) return RejectDanger(GameSessionFailure.InvalidDangerFacts);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var record = _danger.Get(request.Entity);
        if (record.State != DangerState.Flee) return RejectDanger(GameSessionFailure.InvalidDangerFacts);
        var plan = new DangerPlan(this);
        var result = plan.Set(new SessionDangerRequest
        {
            Entity = request.Entity, State = DangerState.Surrender, Target = record.FleeingFrom, Facts = request.Facts, CombatMode = request.CombatMode,
        }, false);
        if (result.Failure != GameSessionFailure.None) return RejectDanger(result.Failure);
        return plan.Commit(result with { StartsFleeGoal = MoraleRules.StartsFleeGoal(result.Transition!.Value, _rules.Combat) });
    }

    /// <summary>
    /// Stop fleeing (<see cref="MoraleRules.StopFleeing"/>) of an NPC in one revision: in FLEE the setter is asked for NONE with
    /// no target (<see cref="SessionDangerResult.Transition"/> is its step); in any other state nothing is written (a surrendered
    /// NPC stays surrendered). Interrupting the NPC's flee goals, in every state, is the host's.
    /// </summary>
    public SessionDangerResult StopFleeing(GameSessionVersion expected, string entity, DangerSetterFacts facts)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var result = PlanStopFleeing(plan, entity, facts);
        return result.Failure != GameSessionFailure.None ? RejectDanger(result.Failure) : plan.Commit(result);
    }

    /// <summary>The back-off start of combat perform (<see cref="MoraleRules.StartsBackOff"/>, after its roll came up): BACKING_OFF
    /// and CHECK_GRENADE set, in one revision. The run that follows at once is the host's.</summary>
    public SessionDangerResult StartBackOff(GameSessionVersion expected, string entity) =>
        ChangeDanger(expected, entity, null, false, plan =>
            plan.Update(entity, record => record with { Flags = record.Flags | DangerFlags.BackingOff | DangerFlags.CheckGrenade }));

    /// <summary>The back-off's end (<see cref="MoraleRules.BackOff"/>: the range reached, or no "away" path): BACKING_OFF cleared.</summary>
    public SessionDangerResult EndBackOff(GameSessionVersion expected, string entity) =>
        ChangeDanger(expected, entity, null, false, plan => plan.Update(entity, record => record with { Flags = record.Flags & ~DangerFlags.BackingOff }));

    /// <summary>
    /// The kill notification's writes (0x4AA1B0) in one revision: the victim's stop fleeing (<see cref="StopFleeing"/>, decided on
    /// the session's record; the hero has none) and BACKING_OFF on each NPC of <see cref="KillNotificationPlan.BacksOff"/>. The
    /// victim's flee-goal interrupt and stop waiting (0x4AA8C0) are the host's. A plan naming the hero or an invalid id among the
    /// back-offs is <see cref="GameSessionFailure.InvalidDangerTarget"/>.
    /// </summary>
    public SessionDangerResult ApplyKillNotification(GameSessionVersion expected, SessionKillNotificationRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Victim is null || !ValidDangerHandle(request.Victim) || request.Plan?.BacksOff is not { } backsOff ||
            backsOff.Any(npc => !SessionValidation.Entity(npc)))
            return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var result = new SessionDangerResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (!request.Victim.IsHero)
        {
            result = PlanStopFleeing(plan, request.Victim.Entity!, request.VictimFacts);
            if (result.Failure != GameSessionFailure.None) return RejectDanger(result.Failure);
        }
        foreach (var npc in backsOff) plan.Update(npc, record => record with { Flags = record.Flags | DangerFlags.BackingOff });
        return plan.Commit(result);
    }

    // Stop fleeing into the plan (0x4AF170): FLEE → the setter's NONE with no target; otherwise nothing.
    private SessionDangerResult PlanStopFleeing(DangerPlan plan, string entity, DangerSetterFacts facts)
    {
        var stop = MoraleRules.StopFleeing(new HostilityCritter { Id = entity, Kind = HostilityKind.Npc }, plan.Get(entity).State);
        return stop.RequestsNone
            ? plan.Set(new SessionDangerRequest { Entity = entity, State = DangerState.None, Facts = facts }, false)
            : new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
    }

    private SessionFleeSpellResult RejectFleeSpell(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, new(_identity, _revision));
}
