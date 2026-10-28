using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

/// <summary>
/// Saved combat-mode flags (ARC-COMBAT-004; the source's per-critter <c>OCF_COMBAT_MODE_ACTIVE</c>): the hero's (false
/// without a hero) and the NPCs in combat mode, in ordinal entity order.
/// </summary>
public sealed class SessionCombatModeState
{
    internal SessionCombatModeState(bool hero, string[] critters)
    {
        Hero = hero;
        Critters = Array.AsReadOnly(critters);
    }

    public static SessionCombatModeState Empty { get; } = new(false, []);

    public bool Hero { get; }
    public IReadOnlyList<string> Critters { get; }
}

/// <summary>
/// What a combat-mode change needs from the host: the <c>turn_based</c> setting (the hero entering combat mode with it on
/// starts a round, leaving ends it), the world that round start scans (needed exactly then), an NPC's facts (null: it is
/// gone; the hero's are the session's; stoned and paralysed come from their traits) and whether the critter plays its
/// concealed animation.
/// </summary>
public sealed record SessionCombatModeContext
{
    public bool TurnBased { get; init; }
    public ISessionCombatWorld? World { get; init; }
    public SessionCombatantFacts? Facts { get; init; }
    public bool Concealed { get; init; }

    public static SessionCombatModeContext RealTime { get; } = new();
}

/// <summary>
/// An NPC near the hero (the source's screen-sized vicinity) as the exit gate reads it: whom it has as combat focus,
/// whether it is active and whether its AI is fighting.
/// </summary>
public readonly record struct SessionCombatThreat(SessionCombatant Focus, bool Active, bool Fighting);

/// <summary>
/// The exit gate's host facts: whether the hero is attempting an attack (its current animation goal) and the NPCs in its
/// vicinity with their combat focus.
/// </summary>
public sealed record SessionCombatExit
{
    public bool AttemptingAttack { get; init; }
    public IReadOnlyList<SessionCombatThreat> Threats { get; init; } = [];

    public static SessionCombatExit Clear { get; } = new();
}

/// <summary>Why a combat-mode change did not happen.</summary>
public enum SessionCombatModeRefusal
{
    None,
    Stoned,
    Paralysed,
    /// <summary>A stunned critter cannot leave combat mode.</summary>
    Stunned,
    /// <summary>An inactive critter (unconscious, stunned, paralysed, stoned) cannot enter combat mode.</summary>
    Inactive,
    /// <summary>An NPC in its concealed animation.</summary>
    Concealed,
    /// <summary>The PC's exit gate: it is attacking, or an active, fighting NPC has it or a follower as combat focus.</summary>
    UnderAttack,
    /// <summary>The PC's combat-mode key does nothing for a dead PC.</summary>
    HeroDead,
}

/// <summary>
/// A combat-mode call: its disposition, a rule refusal (with <see cref="GameSessionDisposition.NoChange"/>), the mode after
/// the call and, when the hero's change started or ended a turn-based round, that round call.
/// </summary>
public sealed record SessionCombatModeResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    SessionCombatModeRefusal Refusal, bool InCombatMode, GameSessionVersion Version)
{
    public SessionCombatResult? Round { get; init; }
}

/// <summary>
/// A change of the <c>turn_based</c> setting (the source's setting callback): <see cref="Store"/> says whether the host
/// keeps the new value; <see cref="Round"/> is the round started or ended for a hero in combat mode.
/// </summary>
public sealed record SessionTurnBasedSwitch(GameSessionDisposition Disposition, GameSessionFailure Failure, bool Store, GameSessionVersion Version)
{
    public SessionCombatResult? Round { get; init; }
}

public static class SessionCombatModeLimits
{
    /// <summary>NPCs in combat mode at once.</summary>
    public const int MaxCritters = 4096;
}

public sealed partial class GameSessionOwner
{
    /// <summary>Whether the critter is in combat mode (an NPC without a flag is not; the hero without a hero is not).</summary>
    public bool QueryCombatMode(SessionCombatant combatant)
    {
        ThrowIfDisposed();
        CheckCombatant(combatant);
        return InCombatMode(combatant);
    }

    /// <summary>The saved combat-mode flags.</summary>
    public SessionCombatModeState QueryCombatModes()
    {
        ThrowIfDisposed();
        return CaptureCombatMode();
    }

    /// <summary>
    /// The source's activate or deactivate: NoChange when the critter is already in the requested mode, else its toggle
    /// (<see cref="RealTimeCombatRules.ToggleRefusal"/>; a refusal is NoChange with the reason). When the hero enters
    /// combat mode with <see cref="SessionCombatModeContext.TurnBased"/> a round starts (<see cref="StartCombatRound"/>);
    /// the flag is set first, and a round start the rules refuse leaves it set with no round, as in the original. When it
    /// leaves with the setting on, the round ends. An NPC's facts are required; a gone NPC is refused
    /// (<see cref="GameSessionFailure.InvalidCombatFacts"/>).
    /// </summary>
    public SessionCombatModeResult SetCombatMode(GameSessionVersion expected, SessionCombatant combatant, bool active,
        SessionCombatModeContext context)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        if (!ValidCombatant(combatant)) return RejectCombatMode(GameSessionFailure.InvalidCombatant);
        if (!combatant.IsHero && (context.Facts is not { } facts || !ValidFacts(facts))) return RejectCombatMode(GameSessionFailure.InvalidCombatFacts);
        if (combatant.IsHero && active && context.TurnBased && context.World is null) return RejectCombatMode(GameSessionFailure.InvalidCombatFacts);
        if (!IsCurrent(expected)) return RejectCombatMode(GameSessionFailure.StaleVersion);
        var current = InCombatMode(combatant);
        if (current == active) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, SessionCombatModeRefusal.None, current, Version);
        return ToggleCombatMode(expected, combatant, context);
    }

    /// <summary>
    /// The PC's combat-mode key (the source's interface toggle): nothing for a dead hero; out of combat mode it enters;
    /// in combat mode it leaves only through the exit gate (<see cref="QueryCanExitCombatMode"/>, refused as
    /// <see cref="SessionCombatModeRefusal.UnderAttack"/>, when the original shows "cannot exit combat mode when under
    /// attack"). Needs a hero (<see cref="GameSessionFailure.CombatUnavailable"/>).
    /// </summary>
    public SessionCombatModeResult ToggleHeroCombatMode(GameSessionVersion expected, SessionCombatModeContext context, SessionCombatExit exit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exit);
        if (_hero is null) return RejectCombatMode(GameSessionFailure.CombatUnavailable);
        if (!ValidExit(exit)) return RejectCombatMode(GameSessionFailure.InvalidCombatFacts);
        if (!IsCurrent(expected)) return RejectCombatMode(GameSessionFailure.StaleVersion);
        if (HeroStatus().IsDead) return Refused(SessionCombatModeRefusal.HeroDead, _combat.HeroCombatMode);
        if (_combat.HeroCombatMode && !CanExit(exit)) return Refused(SessionCombatModeRefusal.UnderAttack, true);
        return SetCombatMode(expected, SessionCombatant.Hero, !_combat.HeroCombatMode, context);
    }

    /// <summary>
    /// The hero leaves combat mode before a dialog, sleep or the world map: NoChange out of combat mode; refused through the
    /// exit gate (the caller then does not start the dialog); else the deactivation, which a stunned hero refuses while
    /// the caller goes ahead, as the source does. Needs a hero.
    /// </summary>
    public SessionCombatModeResult LeaveHeroCombatMode(GameSessionVersion expected, SessionCombatModeContext context, SessionCombatExit exit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exit);
        if (_hero is null) return RejectCombatMode(GameSessionFailure.CombatUnavailable);
        if (!ValidExit(exit)) return RejectCombatMode(GameSessionFailure.InvalidCombatFacts);
        if (!IsCurrent(expected)) return RejectCombatMode(GameSessionFailure.StaleVersion);
        if (!_combat.HeroCombatMode) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, SessionCombatModeRefusal.None, false, Version);
        if (!CanExit(exit)) return Refused(SessionCombatModeRefusal.UnderAttack, true);
        return SetCombatMode(expected, SessionCombatant.Hero, false, context);
    }

    /// <summary>
    /// The exit gate (the source's 0x4B3D90) for the hero: true out of combat mode or when not
    /// active; false while it attempts an attack or when an active, fighting NPC of <paramref name="exit"/> has the hero or
    /// one of its followers as combat focus. False without a hero is never returned: without one it is true.
    /// </summary>
    public bool QueryCanExitCombatMode(SessionCombatExit exit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(exit);
        if (!ValidExit(exit)) throw new ArgumentException("Invalid exit facts.", nameof(exit));
        return _hero is null || CanExit(exit);
    }

    /// <summary>
    /// The <c>turn_based</c> setting changes from <paramref name="current"/> to <paramref name="requested"/> (the source's
    /// setting callback): no change stores; animations that cannot be interrupted refuse it; a hero out of combat mode
    /// stores it; switched off, the round ends; switched on, a round starts, and when the rules refuse the start the
    /// setting is not stored (the hero stays in combat mode in real time).
    /// </summary>
    public SessionTurnBasedSwitch SwitchTurnBased(GameSessionVersion expected, bool current, bool requested, ISessionCombatWorld world,
        bool animationsInterruptible = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, false, Version);
        if (current == requested) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, Version);
        if (!animationsInterruptible) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, false, Version);
        if (_hero is null || !_combat.HeroCombatMode) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, Version);
        var round = requested ? StartCombatRound(expected, world) : EndCombatRound(expected);
        if (round.Disposition == GameSessionDisposition.Rejected && round.Failure != GameSessionFailure.CombatUnavailable)
            return new(GameSessionDisposition.Rejected, round.Failure, false, Version) { Round = round };
        var store = round.Disposition != GameSessionDisposition.Rejected;
        var disposition = round.Disposition == GameSessionDisposition.Changed ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange;
        return new(disposition, GameSessionFailure.None, store, Version) { Round = round };
    }

    /// <summary>
    /// A run step's fatigue charge for a critter in combat mode (<see cref="RealTimeCombatRules.RunStep"/> decides when):
    /// fatigue damage + 1 through the life domain, whatever the critter's immunities (the source's plain fatigue write);
    /// losing consciousness ends its subturn like any life step. A critter out of combat mode is refused
    /// (<see cref="GameSessionFailure.CombatModeInactive"/>).
    /// </summary>
    public SessionLifeResult ChargeCombatRun(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        var combatant = target.IsHero ? SessionCombatant.Hero : target.Entity is { } entity && SessionValidation.Entity(entity) ? SessionCombatant.Critter(entity) : null;
        if (combatant is not null && ValidCombatant(combatant) && !InCombatMode(combatant)) return RejectLife(GameSessionFailure.CombatModeInactive);
        return Life(expected, target, true, (state, facts, _) => LifeRules.ChargeFatigue(state, facts, RealTimeCombatRules.RunChargeFatigue));
    }

    // ---- the toggle ----

    private SessionCombatModeResult ToggleCombatMode(GameSessionVersion expected, SessionCombatant combatant, SessionCombatModeContext context)
    {
        var entering = !InCombatMode(combatant);
        var refusal = ToggleRefusal(combatant, context, entering);
        if (refusal != CombatModeRefusal.None) return Refused(Refusal(refusal), !entering);
        if (!combatant.IsHero && entering && _combat.Critters.Count >= SessionCombatModeLimits.MaxCritters)
            return RejectCombatMode(GameSessionFailure.BudgetExceeded);
        // A turn-based round call may take a revision of its own before the flag takes one.
        if (_revision >= long.MaxValue - 1) return RejectCombatMode(GameSessionFailure.RevisionExhausted);

        // The hero's round with turn-based on: the source writes the flag, then starts the round, and a start its rules
        // refuse leaves the flag set; leaving ends the round. The round domain plans and commits its own call (it does not
        // read the flag, so the order is not observable); a refusal that is not a rule (a stale world answer, a budget)
        // has no counterpart in the source and changes nothing.
        SessionCombatResult? round = null;
        if (combatant.IsHero && context.TurnBased)
        {
            round = entering ? StartCombatRound(expected, context.World!) : EndCombatRound(expected);
            if (round.Disposition == GameSessionDisposition.Rejected && !(entering && round.Failure == GameSessionFailure.CombatUnavailable))
                return RejectCombatMode(round.Failure) with { Round = round };
        }
        SetFlag(combatant, entering);
        // ARC-WORLD-040: the toggle starts combat music for an NPC entering combat mode and stops it when the PC leaves.
        if (!combatant.IsHero && entering) _sound.StartCombatMusic();
        else if (combatant.IsHero && !entering) _sound.StopCombatMusic(_soundSchemes);
        // ARC-WORLD-079: the hero's entry allows its followers to attack (0x4B42B2); an NPC's exit unlocks its target (0x4B411C).
        if (combatant.IsHero && entering) AllowFollowersOnCombatModeEntry();
        else if (!combatant.IsHero && !entering) UnlockOnCombatModeExit(combatant.Entity!);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, SessionCombatModeRefusal.None, entering, Version) { Round = round };
    }

    // Whether a surrender's combat-mode exit (0x4B3F20) acts for an NPC in combat mode: the toggle's refusals for leaving.
    private bool NpcCombatModeExitActs(string entity, SessionCombatModeContext context) =>
        ToggleRefusal(SessionCombatant.Critter(entity), context, false) == CombatModeRefusal.None;

    // The toggle's refusal (RealTimeCombatRules.ToggleRefusal) from the hero's life or the NPC's facts.
    private CombatModeRefusal ToggleRefusal(SessionCombatant combatant, SessionCombatModeContext context, bool entering)
    {
        LifeStatus status;
        LifeFacts lifeFacts;
        if (combatant.IsHero)
        {
            var state = _life.Hero!.Value.State;
            lifeFacts = HeroLifeFacts(state);
            status = LifeRules.Evaluate(state, lifeFacts);
        }
        else
        {
            var facts = context.Facts!;
            lifeFacts = facts.Life;
            status = LifeRules.Evaluate(NpcState(combatant.Entity!, facts), facts.Life);
        }
        var traits = lifeFacts.Traits;
        var stunned = combatant.IsHero ? _life.Hero!.Value.State.Conditions.HasFlag(LifeConditions.Stunned)
            : NpcState(combatant.Entity!, context.Facts!).Conditions.HasFlag(LifeConditions.Stunned);
        return RealTimeCombatRules.ToggleRefusal(!entering, new(status.IsDead, status.IsActive,
            traits.HasFlag(LifeTraits.Stoned), traits.HasFlag(LifeTraits.Paralyzed), stunned,
            context.Concealed, combatant.IsHero));
    }

    private static SessionCombatModeRefusal Refusal(CombatModeRefusal refusal) => refusal switch
    {
        CombatModeRefusal.Stoned => SessionCombatModeRefusal.Stoned,
        CombatModeRefusal.Paralysed => SessionCombatModeRefusal.Paralysed,
        CombatModeRefusal.Stunned => SessionCombatModeRefusal.Stunned,
        CombatModeRefusal.Inactive => SessionCombatModeRefusal.Inactive,
        CombatModeRefusal.Concealed => SessionCombatModeRefusal.Concealed,
        _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
    };

    private bool InCombatMode(SessionCombatant combatant) =>
        combatant.IsHero ? _hero is not null && _combat.HeroCombatMode : _combat.Critters.Contains(combatant.Entity!);

    private void SetFlag(SessionCombatant combatant, bool on)
    {
        if (combatant.IsHero) _combat.HeroCombatMode = on;
        else if (on) _combat.Critters.Add(combatant.Entity!);
        else _combat.Critters.Remove(combatant.Entity!);
    }

    // The source's death notice leaves combat mode (its toggle skips every refusal for the dead), and for the PC in combat
    // mode with turn-based on (an active round) it ends the round; the life step that killed the critter commits it with
    // its own revision.
    private void LeaveCombatModeOnDeath(SessionLifeTarget target)
    {
        if (!target.IsHero)
        {
            if (_combat.Critters.Remove(target.Entity!)) UnlockOnCombatModeExit(target.Entity!);
            return;
        }
        if (_combat.HeroCombatMode && _combat.Round is not null) { _combat.Round = null; _combat.Stall = default; }
        if (_combat.HeroCombatMode) _sound.StopCombatMusic(_soundSchemes);
        _combat.HeroCombatMode = false;
    }

    private bool CanExit(SessionCombatExit exit)
    {
        var status = HeroStatus();
        var threatened = exit.Threats.Any(threat => threat.Active && threat.Fighting &&
            (threat.Focus.IsHero || _party.Get(threat.Focus.Entity!).Standing == CompanionStanding.Following));
        return RealTimeCombatRules.CanExitCombatMode(_combat.HeroCombatMode, status.IsActive, exit.AttemptingAttack, threatened);
    }

    private static bool ValidExit(SessionCombatExit exit) =>
        exit.Threats is { } threats && threats.Count <= SessionCombatLimits.MaxScanCandidates &&
        threats.All(threat => threat.Focus is not null && (threat.Focus.IsHero || SessionValidation.Entity(threat.Focus.Entity)));

    private SessionCombatModeResult Refused(SessionCombatModeRefusal refusal, bool inCombatMode) =>
        new(GameSessionDisposition.NoChange, GameSessionFailure.None, refusal, inCombatMode, Version);

    private SessionCombatModeResult RejectCombatMode(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, SessionCombatModeRefusal.None, false, new(_identity, _revision));

    private SessionCombatModeState CaptureCombatMode() =>
        new(_hero is not null && _combat.HeroCombatMode, _combat.Critters.ToArray());

    // The hero's flag needs a hero; NPC ids are valid, unique, in ordinal order and within the bound.
    private static GameSessionFailure ImportCombatMode(SessionCombatModeState? saved, bool hero, CombatDomain combat)
    {
        if (saved?.Critters is not { } critters) return GameSessionFailure.InvalidSnapshot;
        if (saved.Hero && !hero) return GameSessionFailure.InvalidCombatMode;
        if (critters.Count > SessionCombatModeLimits.MaxCritters) return GameSessionFailure.BudgetExceeded;
        for (var i = 0; i < critters.Count; i++)
            if (!SessionValidation.Entity(critters[i]) || i > 0 && string.CompareOrdinal(critters[i - 1], critters[i]) >= 0)
                return GameSessionFailure.InvalidCombatMode;
        combat.HeroCombatMode = saved.Hero;
        foreach (var entity in critters) combat.Critters.Add(entity);
        return GameSessionFailure.None;
    }
}
