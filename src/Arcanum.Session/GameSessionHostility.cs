using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>The reaction facts peace needs when it makes peace with the hero's side (the NPC's standard reaction raised to 50).</summary>
public sealed record SessionPeaceReaction(ReactionNpcFacts Npc, ReactionPcFacts Pc);

/// <summary>
/// One NPC's facts for peace and stop attacking: the setter's facts for its NONE request, the reaction facts a peace with the
/// hero's side needs (<see cref="Reaction"/>) and the combat-mode facts stop attacking needs when the NPC is in combat mode
/// (<see cref="CombatMode"/>, the toggle's refusals).
/// </summary>
public sealed record SessionPeaceFacts
{
    public DangerSetterFacts Setter { get; init; } = new();
    public SessionPeaceReaction? Reaction { get; init; }
    public SessionCombatModeContext? CombatMode { get; init; }
}

/// <summary>Peace (0x4AA300) of the NPC <see cref="Entity"/> with <see cref="Other"/>'s side.</summary>
public sealed record SessionPeaceRequest
{
    public required string Entity { get; init; }
    public required SessionCombatant Other { get; init; }
    public SessionPeaceFacts Facts { get; init; } = new();
}

/// <summary>Stop attacking (0x4AA4A0) for an NPC or, for the hero, for each of its followers; <see cref="Facts"/> by NPC entity
/// id (an NPC without an entry gets the default facts).</summary>
public sealed record SessionStopAttackingRequest
{
    public required SessionCombatant Combatant { get; init; }
    public IReadOnlyDictionary<string, SessionPeaceFacts> Facts { get; init; } = new Dictionary<string, SessionPeaceFacts>();
}

/// <summary>
/// What peace or stop attacking did to one NPC. <see cref="Head"/>: the side it made peace with (the hero for the hero's
/// followers; none when it had no focus to stop); the host interrupts the NPC's attack goals at the head or its party (0x44E050)
/// and each of <see cref="Followers"/>' attack goals at the NPC (0x44E0E0). <see cref="Transition"/> and <see cref="Hook"/> come
/// from the setter's NONE request; <see cref="Reaction"/> is the raise toward the hero. Stop attacking adds the animation
/// interrupt (<see cref="Interrupted"/>, priority 3), the combat-mode exit (<see cref="LeftCombatMode"/>) and the no-attack flag
/// for a follower of the hero (<see cref="NoAttack"/>).
/// </summary>
public sealed record SessionPeaceStep(string Entity)
{
    public SessionCombatant? Head { get; init; }
    public IReadOnlyList<string> Followers { get; init; } = [];
    public DangerTransition? Transition { get; init; }
    public SessionDangerHook? Hook { get; init; }
    public ReactionAdjustment? Reaction { get; init; }
    public bool Interrupted { get; init; }
    public bool LeftCombatMode { get; init; }
    public bool NoAttack { get; init; }
}

/// <summary>What a peace or stop-attacking call did, one step per NPC in the original's order.</summary>
public sealed record SessionPeaceResult(GameSessionDisposition Disposition, GameSessionFailure Failure, GameSessionVersion Version)
{
    public IReadOnlyList<SessionPeaceStep> Steps { get; init; } = [];
}

/// <summary>The attack event's writes (0x4A9650 steps 10 and 11) for <see cref="Attacker"/> (the hero or an NPC) and the NPC
/// <see cref="Victim"/>, as <see cref="Plan"/> (<see cref="HostilityRules.AttackEvent"/>) decided them.</summary>
public sealed record SessionAttackEventRequest
{
    public required SessionCombatant Attacker { get; init; }
    public required string Victim { get; init; }
    public required AttackEventPlan Plan { get; init; }
}

/// <summary>An enemy-list pick: the entry, or none, or the typed reason the pick was refused.</summary>
public sealed record SessionEnemyPickResult(GameSessionFailure Failure, SessionCombatant? Pick);

/// <summary>
/// The session generator behind the AI rules' <see cref="IAiRollSource"/> (ARC-WORLD-080): each roll is labelled <c>ai.</c> and
/// the <see cref="AiRollPurpose"/> name; refusals throw as in <see cref="SessionCombatRolls"/>, and so does a range with
/// <c>min &gt; max</c> (the rules swap the original's reversed bounds themselves, as the enemy-list pick of an empty list does).
/// </summary>
internal sealed class SessionAiRolls(GameSessionOwner owner) : IAiRollSource
{
    public int Roll(AiRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        var result = owner.Roll(Label(purpose), min, max);
        return result.Roll?.Value ?? throw new InvalidOperationException($"Session roll {purpose} refused: {result.Failure}.");
    }

    internal static string Label(AiRollPurpose purpose) => "ai." + purpose;
}

public sealed partial class GameSessionOwner
{
    /// <summary>The standard reaction peace raises the hero's side to (0x4AA300).</summary>
    public const int PeaceReaction = 50;

    /// <summary>The session generator as the AI rules' roll source; rolls are labelled <c>ai.</c> and the purpose name (for example
    /// <c>ai.EnemyListStart</c>).</summary>
    public IAiRollSource AiRolls { get { ThrowIfDisposed(); return _aiRolls ??= new SessionAiRolls(this); } }

    private SessionAiRolls? _aiRolls;

    /// <summary>
    /// Peace (0x4AA300) of an NPC with another critter's side, in one revision: head := the other's PC leader (the hero for its
    /// followers), else the other. The NPC's focus and who-hit-me-last are cleared and the head removed from its enemy list; for
    /// the hero its standard reaction below 50 is raised to 50 through the PC's effects (needing
    /// <see cref="SessionPeaceFacts.Reaction"/>); the setter is asked for NONE with no target; for the hero's side each follower
    /// in the follower array forgets the NPC (focus and who-hit-me-last when they name it, the NPC removed from its list) and is
    /// removed from the NPC's list. Peace is one-sided: the head's own record keeps the NPC. The goal interrupts are the host's.
    /// </summary>
    [OriginalFunction(0x4AA300, OriginalFunctionScope.Partial)]
    public SessionPeaceResult MakePeace(GameSessionVersion expected, SessionPeaceRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!SessionValidation.Entity(request.Entity) || request.Other is null || !ValidDangerHandle(request.Other) || request.Facts is null)
            return RejectPeace(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectPeace(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var failure = PlanPeace(plan, request.Entity, request.Other, request.Facts, out var step);
        return failure != GameSessionFailure.None ? RejectPeace(failure) : CommitPeace(plan, [step]);
    }

    /// <summary>
    /// Stop attacking (0x4AA4A0) in one revision. For an NPC: when it has a focus, peace with that focus
    /// (<see cref="MakePeace"/>), the animation interrupt and its combat-mode exit (when in combat mode and its toggle acts; needs
    /// <see cref="SessionPeaceFacts.CombatMode"/>); a follower of the hero then gets NO_ATTACK; the target lock is cleared. For the
    /// hero: the same for each NPC of its follower array, in order (the original's recursive follower list; the session has no
    /// followers of followers).
    /// </summary>
    [OriginalFunction(0x4AA4A0, OriginalFunctionScope.Partial)]
    public SessionPeaceResult StopAttacking(GameSessionVersion expected, SessionStopAttackingRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Combatant is null || !ValidDangerHandle(request.Combatant) || request.Facts is null ||
            request.Facts.Any(pair => !SessionValidation.Entity(pair.Key) || pair.Value is null))
            return RejectPeace(GameSessionFailure.InvalidDangerTarget);
        if (request.Facts.Values.Any(facts => facts.CombatMode is { Facts: { } combat } && !ValidFacts(combat)))
            return RejectPeace(GameSessionFailure.InvalidDangerFacts);
        if (!IsCurrent(expected)) return RejectPeace(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var steps = new List<SessionPeaceStep>();
        foreach (var npc in request.Combatant.IsHero ? _party.Order.ToArray() : [request.Combatant.Entity!])
        {
            var facts = request.Facts.TryGetValue(npc, out var given) ? given : new SessionPeaceFacts();
            var step = new SessionPeaceStep(npc);
            if (plan.Get(npc).Focus is { } focus)
            {
                var failure = PlanPeace(plan, npc, focus, facts, out step);
                var left = false;
                if (failure == GameSessionFailure.None) failure = plan.LeaveCombatMode(npc, facts.CombatMode, out left);
                if (failure != GameSessionFailure.None) return RejectPeace(failure);
                step = step with { Interrupted = true, LeftCombatMode = left };
            }
            if (LedByPc(npc))
            {
                plan.Update(npc, record => record with { Flags = record.Flags | DangerFlags.NoAttack });
                step = step with { NoAttack = true };
            }
            plan.Update(npc, record => record with { Flags = record.Flags & ~DangerFlags.TargetLocked });
            steps.Add(step);
        }
        return CommitPeace(plan, steps);
    }

    /// <summary>
    /// The attack event's writes (0x4A9650 steps 10 and 11) in one revision, as the plan decided them: who-hit-me-last := the
    /// attacker (<see cref="AttackEventPlan.WritesHitBy"/>), the attacker's team onto the victim's enemy list and, for an NPC
    /// attacker, the victim's team onto the attacker's (<see cref="AddDangerEnemyTeam"/>). A plan that stopped early writes
    /// nothing (NoChange); a plan that adds the victim's team for the hero is <see cref="GameSessionFailure.InvalidDangerFacts"/>.
    /// The rest of the plan (reaction, followers, noise, the reaction to an attack) is the caller's.
    /// </summary>
    public SessionDangerResult ApplyAttackEvent(GameSessionVersion expected, SessionAttackEventRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Attacker is null || !ValidDangerHandle(request.Attacker) || !SessionValidation.Entity(request.Victim) ||
            request.Attacker.Entity == request.Victim)
            return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (request.Plan is not { } attack || attack.AddsVictimTeam && request.Attacker.IsHero) return RejectDanger(GameSessionFailure.InvalidDangerFacts);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        if (attack.Stop == AttackEventStop.None)
        {
            if (attack.WritesHitBy) plan.Update(request.Victim, record => record with { HitBy = request.Attacker });
            if (attack.AddsAttackerTeam) plan.AddTeam(request.Victim, request.Attacker);
            if (attack.AddsVictimTeam) plan.AddTeam(request.Attacker.Entity!, SessionCombatant.Critter(request.Victim));
        }
        return plan.Commit(new SessionDangerResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version));
    }

    /// <summary>
    /// The enemy-list pick (<see cref="HostilityRules.PickEnemy{T}"/>) over the NPC's enemy list with the host's target validity
    /// (<paramref name="valid"/>, which must not call back into the owner): the start drawn from the session generator
    /// (<c>ai.EnemyListStart</c>, also for an empty list). It writes nothing and does not move the revision. An invalid entity id
    /// is <see cref="GameSessionFailure.InvalidDangerTarget"/>; an exhausted generator <see cref="GameSessionFailure.RandomExhausted"/>.
    /// </summary>
    public SessionEnemyPickResult PickDangerEnemy(string entity, Func<SessionCombatant, bool> valid)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(valid);
        if (!SessionValidation.Entity(entity)) return new(GameSessionFailure.InvalidDangerTarget, null);
        var enemies = _danger.Get(entity).Enemies;
        // Only a one-entry list draws nothing (equal bounds).
        if (_random.Draws == long.MaxValue && enemies.Count != 1) return new(GameSessionFailure.RandomExhausted, null);
        return new(GameSessionFailure.None, HostilityRules.PickEnemy(enemies, valid, AiRolls));
    }

    // Peace for one NPC into the plan (0x4AA300).
    private GameSessionFailure PlanPeace(DangerPlan plan, string npc, SessionCombatant other, SessionPeaceFacts facts, out SessionPeaceStep step)
    {
        step = new(npc);
        var self = SessionCombatant.Critter(npc);
        var head = other.IsHero || LedByPc(other.Entity!) ? SessionCombatant.Hero : other;
        plan.Update(npc, record => record with { Focus = null, HitBy = null, Enemies = DangerRules.RemoveEnemy(record.Enemies, head) });
        ReactionAdjustment? reaction = null;
        if (head.IsHero)
        {
            if (facts.Reaction is not { } peace || !ReactionRules.IsValid(peace.Npc) || !ReactionRules.IsValid(peace.Pc))
                return GameSessionFailure.InvalidDangerFacts;
            var failure = plan.PlanReactionTo(npc, peace.Npc, peace.Pc, plan.Get(npc).Enemies.Contains(SessionCombatant.Hero), PeaceReaction,
                ReactionQuery.Standard, lower: false, out reaction);
            if (failure != GameSessionFailure.None) return failure;
        }
        var calm = plan.Set(new SessionDangerRequest { Entity = npc, State = DangerState.None, Facts = facts.Setter }, false);
        if (calm.Failure != GameSessionFailure.None) return calm.Failure;
        var followers = head.IsHero ? _party.Order.ToArray() : [];
        foreach (var follower in followers)
        {
            plan.Update(follower, record => record with
            {
                Focus = record.Focus == self ? null : record.Focus,
                HitBy = record.HitBy == self ? null : record.HitBy,
                Enemies = DangerRules.RemoveEnemy(record.Enemies, self),
            });
            plan.Update(npc, record => record with { Enemies = DangerRules.RemoveEnemy(record.Enemies, SessionCombatant.Critter(follower)) });
        }
        step = step with { Head = head, Followers = followers, Transition = calm.Transition, Hook = calm.Hook, Reaction = reaction };
        return GameSessionFailure.None;
    }

    private SessionPeaceResult CommitPeace(DangerPlan plan, IReadOnlyList<SessionPeaceStep> steps) =>
        plan.Commit(new SessionPeaceResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version) { Steps = steps },
            (r, disposition, version) => r with { Disposition = disposition, Version = version }, RejectPeace);

    private SessionPeaceResult RejectPeace(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, new(_identity, _revision));

    // FO7 (ARC-WORLD-080): an NPC that stops following or waiting for the hero loses NO_ATTACK; the Original setting keeps it.
    private void ClearNoAttackOnLeave(string entity)
    {
        if (_rules.Combat.NoAttackSurvivesLeaving) return;
        var record = _danger.Get(entity);
        if (record.Flags.HasFlag(DangerFlags.NoAttack)) _danger.Put(record with { Flags = record.Flags & ~DangerFlags.NoAttack });
    }

    // The session's enemy list replaces the caller's PcOnEnemyList (reaction R1: a listed PC reads 0).
    private ReactionNpcFacts Listed(string entity, ReactionNpcFacts npc) => npc is null ? npc! :
        npc with { PcOnEnemyList = SessionValidation.Entity(entity) && _danger.Get(entity).Enemies.Contains(SessionCombatant.Hero) };
}
