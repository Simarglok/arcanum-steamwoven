using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// A participant of a turn-based round (ARC-COMBAT-003): the session's <see cref="Hero"/> or an NPC by its stable entity
/// id. A null id throws, so an unresolved id can never stand for the hero.
/// </summary>
public sealed record SessionCombatant
{
    private SessionCombatant(string? entity) => Entity = entity;

    /// <summary>The session's player character, the local PC of the round.</summary>
    public static SessionCombatant Hero { get; } = new((string?)null);

    public static SessionCombatant Critter(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new(entity);
    }

    /// <summary>The entity id, or null for the hero.</summary>
    public string? Entity { get; }
    public bool IsHero => Entity is null;

    public override string ToString() => Entity ?? "hero";
}

/// <summary>
/// One critter the host's scan found on the actor's map: who it is and its cell. The hero's cell is not read (the session
/// uses the actor's), but the hero must be listed once, at its own place in the scan order.
/// </summary>
public readonly record struct SessionCombatCandidate(SessionCombatant Combatant, CellCoord Cell);

/// <summary>
/// What the round asks about an NPC: its effective speed (the action points of its subturn), its life facts and the state
/// its map data loads with (the session's life record wins once there is one), whether it is hidden from drawing and
/// whether it sleeps. A hidden NPC that does not sleep loses its subturn.
/// </summary>
public sealed record SessionCombatantFacts
{
    public required int Speed { get; init; }
    public required LifeFacts Life { get; init; }
    public LifeState Loaded { get; init; }
    public bool Hidden { get; init; }
    public bool Sleeping { get; init; }
}

/// <summary>
/// What the host knows during a round call (ARC-COMBAT-003). Both answers must not call back into the owner: a call that
/// moves the session revision makes the round call <see cref="GameSessionFailure.StaleVersion"/> with no effect.
/// </summary>
public interface ISessionCombatWorld
{
    /// <summary>
    /// Every PC and NPC on <paramref name="map"/> that is neither destroyed nor switched off nor carried in an inventory, in
    /// the original's scan order: sectors by rows from the top, each row from the left, and within a sector the critter
    /// that entered it last first. Dead, unconscious and hidden critters are listed too (they lose their subturns). The
    /// session keeps those in the square around the actor (<see cref="TurnBasedRules.ScanRadius"/>), so the host may list
    /// more. Every round scans again; participants of earlier rounds stay in the round even when the scan no longer lists them.
    /// </summary>
    IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map);

    /// <summary>An NPC's facts, or null when it no longer exists on the map (destroyed, switched off or gone), which makes it inactive.</summary>
    SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity);
}

/// <summary>
/// The active turn-based round: the round number (1 for the first), the participants in round order, the index of the
/// participant whose subturn it is and the action points it has left.
/// </summary>
public sealed class SessionCombatRound
{
    internal SessionCombatRound(int turn, SessionCombatant[] participants, int current, int actionPoints)
    {
        Turn = turn;
        Participants = Array.AsReadOnly(participants);
        Current = current;
        ActionPoints = actionPoints;
    }

    public int Turn { get; }
    public IReadOnlyList<SessionCombatant> Participants { get; }
    public int Current { get; }
    public int ActionPoints { get; }
    public SessionCombatant Actor => Participants[Current];
}

/// <summary>Bounds of the round domain.</summary>
public static class SessionCombatLimits
{
    /// <summary>Participants of one round.</summary>
    public const int MaxParticipants = 1024;
    /// <summary>Candidates one scan may list (the session keeps those in the square).</summary>
    public const int MaxScanCandidates = 16_384;
}

public enum SessionCombatEventKind
{
    /// <summary>A round began (<see cref="SessionCombatEvent.Value"/> is its number); after the first, the session clock moved
    /// <see cref="TurnBasedRules.RoundGameTimeMs"/> first.</summary>
    RoundBegan,
    /// <summary>A subturn started with <see cref="SessionCombatEvent.Value"/> action points: for an NPC the host runs its
    /// heartbeat script and, when that allows, its AI.</summary>
    SubturnStarted,
    /// <summary>A subturn ended: for an NPC the host runs its end-of-combat script.</summary>
    SubturnEnded,
    /// <summary>The round ended because nobody could act and the hero was not active.</summary>
    RoundEnded,
}

/// <summary>One step of a round call, in order.</summary>
public readonly record struct SessionCombatEvent(SessionCombatEventKind Kind, SessionCombatant? Combatant, int Value);

/// <summary>How a cost was charged (<see cref="GameSessionOwner.ConsumeActionPoints"/>).</summary>
public enum SessionActionPointUse
{
    /// <summary>No round, or not the actor's subturn: nothing is charged and the action goes ahead.</summary>
    Free,
    Paid,
    /// <summary>The hero acted beyond its action points and took the overexertion's fatigue damage.</summary>
    Overexerted,
    /// <summary>The action fails and the actor's subturn ended.</summary>
    Refused,
}

/// <summary>Whether a critter may start an action in a round (the source's goal-start gate).</summary>
public enum SessionCombatGate
{
    Allowed,
    /// <summary>Dead, unconscious, stunned, paralysed, stoned or gone.</summary>
    Inactive,
    /// <summary>A round is active and it is another participant's subturn.</summary>
    NotTheirTurn,
    /// <summary>An NPC that cannot pay the cost and whose fatigue is at most 35 % of its maximum.</summary>
    LowFatigue,
}

/// <summary>
/// A round call: what it did (<see cref="Events"/> in order), how a cost was charged and, for an overexertion, the hero's
/// life step. A refusal changes nothing and reports no events.
/// </summary>
public sealed record SessionCombatResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    IReadOnlyList<SessionCombatEvent> Events, GameSessionVersion Version)
{
    public SessionActionPointUse Charge { get; init; }
    public SessionLifeResult? Life { get; init; }
}

/// <summary>The round domain of one owner: the saved round, the unsaved stalled-actor check and the saved combat-mode flags
/// (ARC-COMBAT-004).</summary>
internal sealed class CombatDomain
{
    internal CombatRound? Round { get; set; }
    internal TurnBasedStall<SessionCombatant> Stall { get; set; }
    internal bool HeroCombatMode { get; set; }
    internal SortedSet<string> Critters { get; } = new(StringComparer.Ordinal);
    // ARC-SKILLS-008: the concealment flags (the source's critter flags 0x1 and 0x2, set and cleared together) of the hero and of
    // its followers.
    internal bool HeroConcealed { get; set; }
    internal SortedSet<string> Concealed { get; } = new(StringComparer.Ordinal);
}

internal sealed class CombatRound(int turn, List<SessionCombatant> participants, int current, int actionPoints)
{
    internal int Turn { get; set; } = turn;
    internal List<SessionCombatant> Participants { get; set; } = participants;
    internal int Current { get; set; } = current;
    internal int ActionPoints { get; set; } = actionPoints;
    internal SessionCombatant Actor => Participants[Current];

    internal CombatRound Clone() => new(Turn, [.. Participants], Current, ActionPoints);

    internal bool SameAs(CombatRound? other) => other is not null && other.Turn == Turn && other.Current == Current &&
        other.ActionPoints == ActionPoints && other.Participants.SequenceEqual(Participants);

    internal SessionCombatRound Capture() => new(Turn, [.. Participants], Current, ActionPoints);
}

public sealed partial class GameSessionOwner
{
    /// <summary>The active round, or null when none is active.</summary>
    public SessionCombatRound? QueryCombatRound() { ThrowIfDisposed(); return _combat.Round?.Capture(); }

    /// <summary>
    /// The preview check of a cost (the source's action-point check): true without a round or outside the participant's
    /// subturn, when its action points cover the cost, or for the hero with action points left and fatigue above 1 (it
    /// would overexert). An invalid participant or cost throws.
    /// </summary>
    public bool QueryActionPointCheck(SessionCombatant combatant, int cost)
    {
        ThrowIfDisposed();
        CheckCombatant(combatant);
        if (cost is < 0 or > TurnBasedRules.MaxActionCost) throw new ArgumentOutOfRangeException(nameof(cost));
        if (_combat.Round is not { } round || round.Actor != combatant) return true;
        return TurnBasedRules.CanAfford(round.ActionPoints, cost, combatant.IsHero, combatant.IsHero ? HeroStatus().Fatigue : 0);
    }

    /// <summary>
    /// Whether the participant may start an action costing <paramref name="cost"/> (the source's goal-start gate): an
    /// inactive critter never; without a round always; in a round only on its own subturn, and an NPC that cannot pay only
    /// while its fatigue is above 35 % of its maximum. An NPC's <paramref name="facts"/> (null: gone) are the host's; the
    /// hero's are the session's. An invalid participant, cost or facts throw.
    /// </summary>
    public SessionCombatGate QueryCombatActionGate(SessionCombatant combatant, int cost, SessionCombatantFacts? facts = null)
    {
        ThrowIfDisposed();
        CheckCombatant(combatant);
        if (cost is < 0 or > TurnBasedRules.MaxActionCost) throw new ArgumentOutOfRangeException(nameof(cost));
        LifeStatus status;
        if (combatant.IsHero) status = HeroStatus();
        else if (facts is null) return SessionCombatGate.Inactive;
        else
        {
            if (!ValidFacts(facts)) throw new ArgumentException("Invalid combatant facts.", nameof(facts));
            status = LifeRules.Evaluate(NpcState(combatant.Entity!, facts), facts.Life);
        }
        if (!status.IsActive) return SessionCombatGate.Inactive;
        if (_combat.Round is not { } round) return SessionCombatGate.Allowed;
        if (round.Actor != combatant) return SessionCombatGate.NotTheirTurn;
        if (combatant.IsHero) return SessionCombatGate.Allowed;
        return TurnBasedRules.NpcMayStartAction(round.ActionPoints, cost, status.Fatigue, status.MaxFatigue)
            ? SessionCombatGate.Allowed : SessionCombatGate.LowFatigue;
    }

    /// <summary>
    /// Starts a turn-based round (the local PC entering combat mode with turn-based combat on): round 1 over the critters the
    /// host's scan lists in the square <c>actor ± max(10, PE / 2 + 5)</c>, in scan order with the PC's party last, and the
    /// first subturn of the first participant that is active and not hidden (else the hero's, when it is active). Needs a
    /// hero (<see cref="GameSessionFailure.CombatUnavailable"/>); a round already active is NoChange; nobody able to act is
    /// <see cref="GameSessionFailure.CombatUnavailable"/>. Animations, combat mode and the host's own setting are the host's.
    /// </summary>
    public SessionCombatResult StartCombatRound(GameSessionVersion expected, ISessionCombatWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (_hero is null) return RejectCombat(GameSessionFailure.CombatUnavailable);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is not null) return NoCombatChange();
        return RunCombat(expected, world, plan =>
        {
            if (!BeginRound(plan, 1)) throw new CombatPlanException(GameSessionFailure.CombatUnavailable);
        });
    }

    /// <summary>Ends the round (the PC leaving combat mode, or turn-based combat switched off). NoChange without one.</summary>
    public SessionCombatResult EndCombatRound(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is null) return NoCombatChange();
        if (_revision == long.MaxValue) return RejectCombat(GameSessionFailure.RevisionExhausted);
        _combat.Round = null;
        _combat.Stall = default;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, [], Version);
    }

    /// <summary>
    /// Charges <paramref name="cost"/> to the participant (the source's action-point charge, made by walking, running,
    /// attacking, using, casting). Without a round or outside its subturn nothing is charged (<see cref="SessionActionPointUse.Free"/>).
    /// A cost the action points cover is paid. Otherwise the hero with action points left and fatigue above 1 overexerts:
    /// its action points drop to 0 and it takes 2 fatigue damage less ¾ of its normal resistance through the life domain;
    /// if that knocks it out its subturn ends at once (with A12 of the Original mode the next participant then loses its
    /// action points too). Anyone else is refused: action points 0 and the next subturn.
    /// </summary>
    public SessionCombatResult ConsumeActionPoints(GameSessionVersion expected, SessionCombatant combatant, int cost,
        ISessionCombatWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (!ValidCombatant(combatant)) return RejectCombat(GameSessionFailure.InvalidCombatant);
        if (cost is < 0 or > TurnBasedRules.MaxActionCost) return RejectCombat(GameSessionFailure.InvalidCombatCost);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is not { } round || round.Actor != combatant) return NoCombatChange() with { Charge = SessionActionPointUse.Free };
        var hero = combatant.IsHero;
        var status = hero ? HeroStatus() : null;
        var charge = TurnBasedRules.Charge(round.ActionPoints, cost, hero, status?.Fatigue ?? 0);
        var use = charge.Kind switch
        {
            ActionPointChargeKind.Paid => SessionActionPointUse.Paid,
            ActionPointChargeKind.Overexerted => SessionActionPointUse.Overexerted,
            _ => SessionActionPointUse.Refused,
        };
        if (charge.Kind != ActionPointChargeKind.Overexerted)
        {
            var paid = RunCombat(expected, world, plan =>
            {
                plan.Round!.ActionPoints = charge.ActionPointsLeft;
                if (charge.Kind == ActionPointChargeKind.Refused) NextSubturn(plan);
            });
            return paid.Disposition == GameSessionDisposition.Rejected ? paid : paid with { Charge = use };
        }

        // Overexertion: the damage step first (it can refuse), then the round; the knockout is known before either commits,
        // as a fatigue-only packet draws no roll.
        var heroState = _life.Hero!.Value.State;
        var heroFacts = HeroLifeFacts(heroState);
        var damage = new LifeDamage { Fatigue = TurnBasedRules.OverexertionFatigueDamage(heroFacts.NormalResistance) };
        var predicted = LifeRules.ApplyDamage(heroState, heroFacts, damage, NoCombatRolls.Instance);
        var knockedOut = predicted.Events.HasFlag(LifeEvents.LostConsciousness);
        var planned = PlanCombat(world, plan =>
        {
            // The rest of the step sees the hero after the damage (an unconscious hero is skipped and cannot hold the round).
            plan.Hero = predicted.After;
            plan.Round!.ActionPoints = 0;
            if (!knockedOut) return;
            NextSubturn(plan);
            if (plan.Round is not null && plan.Profile.OverexertionKnockoutClearsNextActionPoints) plan.Round.ActionPoints = 0;
        }, out var failure);
        if (planned is null) return RejectCombat(failure);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (planned.AdvancesClock && !CanAdvanceRoundClock()) return RejectCombat(GameSessionFailure.ClockRejected);
        var life = Life(expected, SessionLifeTarget.Hero, true, (state, facts, rolls) => LifeRules.ApplyDamage(state, facts, damage, rolls));
        if (life.Disposition == GameSessionDisposition.Rejected)
            return RejectCombat(life.Failure) with { Life = life };
        if (life.Outcome!.After != predicted.After)
            throw new InvalidOperationException("Overexertion outcome differs from its prediction.");
        var result = CommitCombat(planned, life.Disposition == GameSessionDisposition.Changed);
        return result with { Charge = use, Life = life };
    }

    /// <summary>
    /// Ends the participant's subturn (the source's end-of-critter-turn: an NPC that is done, a critter knocked out):
    /// action points 0 and the next subturn. NoChange outside its subturn.
    /// </summary>
    public SessionCombatResult EndCombatTurn(GameSessionVersion expected, SessionCombatant combatant, ISessionCombatWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (!ValidCombatant(combatant)) return RejectCombat(GameSessionFailure.InvalidCombatant);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is not { } round || round.Actor != combatant) return NoCombatChange();
        return RunCombat(expected, world, plan =>
        {
            plan.Round!.ActionPoints = 0;
            NextSubturn(plan);
        });
    }

    /// <summary>
    /// The current participant's actions all completed (the source's animations-complete check and its 2 ms turn-based
    /// event, as one step): an active hero keeps its subturn while it has action points. Otherwise an actor checked twice in a row
    /// with unchanged action points is forced to spend the rest (with A11 of the Original mode the reference of the second
    /// check is the previous actor's action points), and an actor without action points passes to the next subturn. The
    /// stalled-actor check is not saved; it starts empty every round and after a restore.
    /// </summary>
    public SessionCombatResult CompleteCombatActions(GameSessionVersion expected, ISessionCombatWorld world)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is not { } round) return NoCombatChange();
        // An inactive hero (stunned, knocked out) never holds the round with its action points.
        if (round.Actor.IsHero && round.ActionPoints > 0 && HeroStatus().IsActive) return NoCombatChange();
        return RunCombat(expected, world, plan =>
        {
            var current = plan.Round!;
            if (current.Actor.IsHero && !HeroStatus(plan).IsActive) current.ActionPoints = 0;
            plan.Stall = plan.Stall.Observe(current.Actor, current.ActionPoints, plan.Profile.StallCheckKeepsPreviousActionPoints);
            if (plan.Stall.Forced)
            {
                plan.Stall = plan.Stall with { Forced = false };
                current.ActionPoints = 0;
            }
            if (current.ActionPoints <= 0) NextSubturn(plan);
        });
    }

    /// <summary>
    /// Adds an NPC to the active round (the source's insert, used by the AI when a critter joins the fight): it is
    /// appended and the round re-ordered with the PC's party last; whose subturn it is does not change. NoChange without
    /// a round or when it already takes part; the hero is always a participant (<see cref="GameSessionFailure.InvalidCombatant"/>).
    /// </summary>
    public SessionCombatResult AddCombatant(GameSessionVersion expected, SessionCombatant combatant)
    {
        ThrowIfDisposed();
        if (!ValidCombatant(combatant) || combatant.IsHero) return RejectCombat(GameSessionFailure.InvalidCombatant);
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (_combat.Round is not { } round || round.Participants.Contains(combatant)) return NoCombatChange();
        if (round.Participants.Count >= SessionCombatLimits.MaxParticipants) return RejectCombat(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectCombat(GameSessionFailure.RevisionExhausted);
        var actor = round.Actor;
        round.Participants = [.. TurnBasedRules.Order([.. round.Participants, combatant], InPcParty)];
        round.Current = round.Participants.IndexOf(actor);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, [], Version);
    }

    // ---- planning: every world answer is read and every step decided on a copy, then one commit ----

    private sealed class CombatPlan(ISessionCombatWorld world, CombatRound? round, TurnBasedStall<SessionCombatant> stall,
        CombatAnomalyProfile profile)
    {
        internal ISessionCombatWorld World { get; } = world;
        // The rule mode as read when the step began, so a world answer that switches it cannot mix two modes in one step.
        internal CombatAnomalyProfile Profile { get; } = profile;
        // The hero's life state the step decides with, when the step itself changes it (an overexertion); else the record's.
        internal LifeState? Hero { get; set; }
        internal CombatRound? Round { get; set; } = round;
        internal TurnBasedStall<SessionCombatant> Stall { get; set; } = stall;
        internal List<SessionCombatEvent> Events { get; } = [];
        internal bool AdvancesClock { get; set; }
        internal Dictionary<string, SessionCombatantFacts?> Facts { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CombatPlanException(GameSessionFailure failure) : Exception
    {
        internal GameSessionFailure Failure { get; } = failure;
    }

    private SessionCombatResult RunCombat(GameSessionVersion expected, ISessionCombatWorld world, Action<CombatPlan> step)
    {
        var plan = PlanCombat(world, step, out var failure);
        if (plan is null) return RejectCombat(failure);
        // A world answer that called back into the owner and moved its revision invalidates the plan.
        if (!IsCurrent(expected)) return RejectCombat(GameSessionFailure.StaleVersion);
        if (plan.AdvancesClock && !CanAdvanceRoundClock()) return RejectCombat(GameSessionFailure.ClockRejected);
        return CommitCombat(plan, false);
    }

    private CombatPlan? PlanCombat(ISessionCombatWorld world, Action<CombatPlan> step, out GameSessionFailure failure)
    {
        var plan = new CombatPlan(world, _combat.Round?.Clone(), _combat.Stall, _rules.Combat);
        failure = GameSessionFailure.None;
        try { step(plan); }
        catch (CombatPlanException e) { failure = e.Failure; return null; }
        return plan;
    }

    // All refusals precede this; the clock was checked, so the advance cannot fail.
    private SessionCombatResult CommitCombat(CombatPlan plan, bool revisionTaken)
    {
        var changed = !(plan.Round is null ? _combat.Round is null : plan.Round.SameAs(_combat.Round));
        if (changed && !revisionTaken && _revision == long.MaxValue) return RejectCombat(GameSessionFailure.RevisionExhausted);
        if (plan.AdvancesClock)
        {
            var advanced = AdvanceClock(_clock.QueryClockSnapshot().Token, TurnBasedRules.RoundGameTimeMs, ClockAdvanceReason.ExplicitWait);
            if (advanced.Disposition != ClockMutationDisposition.Changed) throw new InvalidOperationException("Round clock advance failed after admission.");
        }
        _combat.Round = plan.Round;
        _combat.Stall = plan.Stall;
        if (changed && !revisionTaken) _revision++;
        var disposition = changed || revisionTaken || plan.AdvancesClock ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange;
        return new(disposition, GameSessionFailure.None, plan.Events.ToArray(), Version);
    }

    // The source's begin-turn: rescan and merge (the participants of the previous round stay where they were, critters new
    // to the round go in front, the last found first), round order, the first participant that may act (else the active
    // hero), its subturn.
    private bool BeginRound(CombatPlan plan, int turn)
    {
        var scanned = ScanParticipants(plan.World);
        // The first round is the scan itself (the source builds its list from the scan, then merges the same scan again).
        var merged = plan.Round is { } previous ? [.. previous.Participants] : scanned;
        if (plan.Round is not null)
            foreach (var participant in scanned)
                if (!merged.Contains(participant)) merged.Insert(0, participant);
        if (merged.Count > SessionCombatLimits.MaxParticipants) throw new CombatPlanException(GameSessionFailure.BudgetExceeded);
        var participants = TurnBasedRules.Order(merged, InPcParty).ToList();
        plan.Stall = default;
        var index = participants.FindIndex(participant => !Skipped(plan, participant));
        if (index < 0)
        {
            if (!HeroStatus(plan).IsActive)
            {
                plan.Round = null;
                plan.Events.Add(new(SessionCombatEventKind.RoundEnded, null, 0));
                return false;
            }
            index = participants.IndexOf(SessionCombatant.Hero);
        }
        plan.Round = new(turn, participants, index, 0);
        plan.Events.Add(new(SessionCombatEventKind.RoundBegan, null, turn));
        StartSubturn(plan);
        return true;
    }

    private void StartSubturn(CombatPlan plan)
    {
        var round = plan.Round!;
        var actor = round.Actor;
        var speed = actor.IsHero ? HeroStat(CharacterStat.Speed) : Facts(plan, actor.Entity!)?.Speed ?? 0;
        round.ActionPoints = TurnBasedRules.ActionPointGrant(speed);
        plan.Events.Add(new(SessionCombatEventKind.SubturnStarted, actor, round.ActionPoints));
    }

    // The source's next subturn: end this one, skip participants that may not act; past the last one the round ends, the
    // clock moves one second and the next round begins (A9 of the Original mode then starts its first subturn again).
    private void NextSubturn(CombatPlan plan)
    {
        var round = plan.Round!;
        plan.Events.Add(new(SessionCombatEventKind.SubturnEnded, round.Actor, 0));
        var next = round.Current + 1;
        var count = round.Participants.Count;
        if (next < count && Skipped(plan, round.Participants[next]))
        {
            do next++;
            while (next < count && Skipped(plan, round.Participants[next]));
            if (next >= count)
            {
                if (NextRound(plan) && plan.Profile.FirstSubturnStartedTwice) StartSubturn(plan);
                return;
            }
        }
        if (next >= count)
        {
            NextRound(plan);
            return;
        }
        round.Current = next;
        StartSubturn(plan);
    }

    private bool NextRound(CombatPlan plan)
    {
        plan.AdvancesClock = true;
        var turn = plan.Round!.Turn;
        return BeginRound(plan, turn == int.MaxValue ? turn : turn + 1);
    }

    // Candidates in scan order, validated and kept within the square around the actor.
    private List<SessionCombatant> ScanParticipants(ISessionCombatWorld world)
    {
        var scan = world.ScanCombatants(_actor.Map) ?? throw new CombatPlanException(GameSessionFailure.InvalidCombatFacts);
        if (scan.Count > SessionCombatLimits.MaxScanCandidates) throw new CombatPlanException(GameSessionFailure.BudgetExceeded);
        var radius = TurnBasedRules.ScanRadius(HeroStat(CharacterStat.Perception));
        var seen = new HashSet<SessionCombatant>();
        var kept = new List<SessionCombatant>();
        foreach (var candidate in scan)
        {
            if (!ValidCombatant(candidate.Combatant) || !seen.Add(candidate.Combatant))
                throw new CombatPlanException(GameSessionFailure.InvalidCombatFacts);
            if (candidate.Combatant.IsHero || TurnBasedRules.InScanSquare(_actor.Cell, candidate.Cell, radius)) kept.Add(candidate.Combatant);
        }
        if (!seen.Contains(SessionCombatant.Hero)) throw new CombatPlanException(GameSessionFailure.InvalidCombatFacts);
        return kept;
    }

    private bool Skipped(CombatPlan plan, SessionCombatant participant)
    {
        // The hero is never hidden from drawing, so only its activity counts.
        if (participant.IsHero) return !HeroStatus(plan).IsActive;
        if (Facts(plan, participant.Entity!) is not { } facts) return true;
        var active = LifeRules.Evaluate(NpcState(participant.Entity!, facts), facts.Life).IsActive;
        return TurnBasedRules.IsSkipped(active, facts.Hidden, facts.Sleeping);
    }

    // One answer per NPC and call, so a step sees one consistent world.
    private SessionCombatantFacts? Facts(CombatPlan plan, string entity)
    {
        if (plan.Facts.TryGetValue(entity, out var cached)) return cached;
        var facts = plan.World.CombatantFacts(_actor.Map, entity);
        if (facts is not null && !ValidFacts(facts)) throw new CombatPlanException(GameSessionFailure.InvalidCombatFacts);
        plan.Facts[entity] = facts;
        return facts;
    }

    private static bool ValidFacts(SessionCombatantFacts facts) =>
        facts.Life is not null && Math.Abs(facts.Speed) <= TurnBasedRules.MaxStat && LifeRules.IsValid(facts.Life) && LifeRules.IsValid(facts.Loaded);

    private LifeState NpcState(string entity, SessionCombatantFacts facts) =>
        _life.Critters.TryGetValue(entity, out var record) ? record.State : facts.Loaded;

    private bool InPcParty(SessionCombatant participant) => participant.IsHero || LedByPc(participant.Entity!);

    private LifeStatus HeroStatus(CombatPlan? plan = null)
    {
        var state = plan?.Hero ?? _life.Hero!.Value.State;
        return LifeRules.Evaluate(state, HeroLifeFacts(state));
    }

    private int HeroStat(CharacterStat stat)
    {
        var state = _life.Hero!.Value.State;
        return _hero!.Stats.Query(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!.GetEffective(stat);
    }

    private bool CanAdvanceRoundClock()
    {
        var clock = _clock.QueryClockSnapshot();
        return clock.GameTimestampMs <= long.MaxValue - TurnBasedRules.RoundGameTimeMs && clock.Revision < long.MaxValue &&
            ClockCalendar.TryDerive(clock.GameTimestampMs + TurnBasedRules.RoundGameTimeMs, clock.ProfileBinding.StartYear, out _) == ClockFailure.None;
    }

    // A life step that knocks out or stuns the participant whose subturn it is ends that subturn (the source's fatigue
    // write and its stunned animation goal): its action points drop to 0 here and the host's next completion check passes
    // the turn on.
    private void EndSubturnOnIncapacitation(SessionLifeTarget target, LifeOutcome outcome)
    {
        if ((outcome.Events & (LifeEvents.LostConsciousness | LifeEvents.Stunned)) == 0 || _combat.Round is not { } round) return;
        var combatant = target.IsHero ? SessionCombatant.Hero : SessionCombatant.Critter(target.Entity!);
        if (round.Actor == combatant) round.ActionPoints = 0;
    }

    private bool ValidCombatant(SessionCombatant? combatant) =>
        combatant is not null && (combatant.IsHero ? _hero is not null : SessionValidation.Entity(combatant.Entity));

    private void CheckCombatant(SessionCombatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        if (!ValidCombatant(combatant)) throw new ArgumentException("Invalid combatant.", nameof(combatant));
    }

    private SessionCombatResult NoCombatChange() => new(GameSessionDisposition.NoChange, GameSessionFailure.None, [], Version);

    private SessionCombatResult RejectCombat(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, [], new(_identity, _revision));

    // A saved round needs the hero, which takes part exactly once; participants are unique and valid, the index and action
    // points in range. The stalled-actor check starts empty, as after the source's load.
    private static GameSessionFailure ImportCombat(SessionCombatRound? saved, bool hero, out CombatDomain combat)
    {
        combat = new();
        if (saved is null) return GameSessionFailure.None;
        if (!hero) return GameSessionFailure.InvalidCombatRound;
        var participants = saved.Participants;
        if (participants.Count is < 1 or > SessionCombatLimits.MaxParticipants) return GameSessionFailure.InvalidCombatRound;
        if (participants.Any(p => p is null || !p.IsHero && !SessionValidation.Entity(p.Entity)) ||
            participants.Distinct().Count() != participants.Count || participants.Count(p => p.IsHero) != 1)
            return GameSessionFailure.InvalidCombatRound;
        if (saved.Turn < 1 || saved.Current < 0 || saved.Current >= participants.Count ||
            saved.ActionPoints is < 0 or > TurnBasedRules.MaxActionPoints) return GameSessionFailure.InvalidCombatRound;
        combat.Round = new(saved.Turn, [.. participants], saved.Current, saved.ActionPoints);
        return GameSessionFailure.None;
    }
}

/// <summary>A roll source for a step that must draw nothing (a fatigue-only damage packet).</summary>
internal sealed class NoCombatRolls : ICombatRollSource
{
    internal static NoCombatRolls Instance { get; } = new();
    public int Roll(CombatRollPurpose purpose, int min, int max) => throw new InvalidOperationException("No roll expected.");
}
