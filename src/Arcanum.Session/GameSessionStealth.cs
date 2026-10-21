using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// Saved concealment (ARC-SKILLS-008; the source's critter flags 0x1 "concealed" and 0x2 "moving silently", always set together):
/// the hero's (false without a hero) and its concealed followers, in ordinal entity order.
/// </summary>
public sealed class SessionConcealmentState
{
    /// <summary>At most this many concealed NPCs: the party's bound.</summary>
    public const int MaxNpcs = GameSessionLimits.PolicyMaxCompanions;

    internal SessionConcealmentState(bool hero, string[] npcs)
    {
        Hero = hero;
        Npcs = Array.AsReadOnly(npcs);
    }

    public static SessionConcealmentState Empty { get; } = new(false, []);

    public bool Hero { get; }
    public IReadOnlyList<string> Npcs { get; }
}

/// <summary>A prowling margin over one observer (ARC-SKILLS-008, AW4): the margin (0 while the hero is not concealed), the
/// prowler's effectiveness and the check's difficulty it came from (null while not concealed).</summary>
public sealed record SessionProwlingMargin(int Margin, int? Effectiveness, SkillDifficulty? Difficulty);

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's and its followers' concealment.</summary>
    public SessionConcealmentState QueryConcealment() { ThrowIfDisposed(); return CaptureConcealment(); }

    /// <summary>Whether the critter is concealed: <see cref="SessionCombatant.Hero"/> or a follower's entity.</summary>
    public bool IsConcealed(SessionCombatant critter)
    {
        ThrowIfDisposed();
        return critter.IsHero ? _hero is not null && _combat.HeroConcealed : critter.Entity is { } entity && _combat.Concealed.Contains(entity);
    }

    /// <summary>
    /// The skill bar's prowling button (0x579FA0 → 0x57A0A0 → 0x45EE30, ledger ST1): the hero's concealment turns to the opposite
    /// and every follower (following or waiting) takes the hero's new state. No check, no action points, no fate, no experience.
    /// Refused without a hero or for a dead or unconscious one (<see cref="GameSessionFailure.ConcealmentUnavailable"/>). The
    /// crouch animation and its art are the host's: the source sets no flags on a critter whose crouch art is missing, which the
    /// session does not see. One revision.
    /// </summary>
    public GameSessionMutationResult ToggleHeroConcealment(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead() || _life.Hero is { } life && LifeRules.IsUnconscious(life.State, HeroLifeFacts(life.State)))
            return Reject(GameSessionFailure.ConcealmentUnavailable);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var on = !_combat.HeroConcealed;
        _combat.HeroConcealed = on;
        foreach (var follower in _party.Records.Values.Where(record => record.Standing is CompanionStanding.Following or CompanionStanding.Waiting))
            if (on) _combat.Concealed.Add(follower.Entity); else _combat.Concealed.Remove(follower.Entity);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// The hero's prowling margin over one observer in a sight or hearing check (AW4, AW5): 0 while the hero is not concealed;
    /// otherwise <see cref="StealthRules.Margin"/> of the shared check's prowling effectiveness (opposed by the observer's
    /// perception, <paramref name="observerPerception"/>) and its difficulty for <paramref name="observer"/> (the host's facts of
    /// the observer, the light at the hero, cover and the worn armour's silent-move adjustments; the sight or hearing flag is
    /// set here). The source computes it at every check and stores nothing; the observer's AI (slice I2) is its consumer.
    /// </summary>
    public SessionProwlingMargin HeroProwlingMargin(int observerPerception, SkillDifficultyFacts observer, bool seeing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observer);
        if (_hero is null || !_combat.HeroConcealed) return new(0, null, null);
        var request = HeroSkillRequest(Skill.Prowling, null);
        var effectiveness = SkillCheckRules.Effectiveness(Skill.Prowling, request.Level, request.Effectiveness with { OpponentValue = observerPerception });
        var difficulty = SkillCheckRules.Difficulty(Skill.Prowling, request.Training, effectiveness, observer with
        {
            SeeingCheck = seeing,
            HearingCheck = !seeing,
            SourceInjuries = request.Difficulty.SourceInjuries,
        });
        return new(StealthRules.Margin(effectiveness, difficulty.Total), effectiveness, difficulty);
    }

    // A dialog's start (0x567460, ST4): the hero and the NPC it talks to are no longer concealed; true when that changed anything.
    private bool EndConcealmentForDialog(string entity)
    {
        var changed = _combat.HeroConcealed || _combat.Concealed.Contains(entity);
        _combat.HeroConcealed = false;
        _combat.Concealed.Remove(entity);
        return changed;
    }

    // The attack event (0x4A9650, ST4): when the victim is the hero or one of its followers, the hero and all its followers are no
    // longer concealed; the attacker keeps its state. True when that changed anything.
    private bool EndConcealmentForAttackOn(SessionCombatant victim)
    {
        if (!victim.IsHero && (victim.Entity is not { } entity || !_party.Records.TryGetValue(entity, out var record) ||
            record.Standing is not (CompanionStanding.Following or CompanionStanding.Waiting)))
            return false;
        var changed = _combat.HeroConcealed || _combat.Concealed.Count > 0;
        _combat.HeroConcealed = false;
        _combat.Concealed.Clear();
        return changed;
    }

    private SessionConcealmentState CaptureConcealment() =>
        new(_hero is not null && _combat.HeroConcealed, _combat.Concealed.ToArray());

    // The hero's flag needs a hero; NPC ids are valid, unique, in ordinal order, and followers (following or waiting).
    private static GameSessionFailure ImportConcealment(SessionConcealmentState? saved, bool hero, PartyDomain party, CombatDomain combat)
    {
        if (saved?.Npcs is not { } npcs) return GameSessionFailure.InvalidSnapshot;
        if (saved.Hero && !hero) return GameSessionFailure.InvalidConcealment;
        if (npcs.Count > SessionConcealmentState.MaxNpcs) return GameSessionFailure.BudgetExceeded;
        for (var i = 0; i < npcs.Count; i++)
            if (!SessionValidation.Entity(npcs[i]) || i > 0 && string.CompareOrdinal(npcs[i - 1], npcs[i]) >= 0 ||
                !party.Records.TryGetValue(npcs[i], out var record) || record.Standing is not (CompanionStanding.Following or CompanionStanding.Waiting))
                return GameSessionFailure.InvalidConcealment;
        combat.HeroConcealed = saved.Hero;
        foreach (var entity in npcs) combat.Concealed.Add(entity);
        return GameSessionFailure.None;
    }
}
