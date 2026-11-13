using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation;

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

/// <summary>ARC-AI-1560: a follower as a prowler: the combat build the host reads its light and worn armour from, the advancement sheet
/// its map data loads (the session's levelled one wins) and the modifiers of its effects, as <see cref="GameSessionOwner.QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)"/> takes them.</summary>
public sealed record SessionNpcProwler(SessionCritterCombat Critter, SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers);

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
    /// and every follower in the follower list takes the hero's new state; a waiting NPC has left that list (HK2) and keeps its own. No check, no action points, no fate, no experience.
    /// Refused without a hero or for a dead or unconscious one (<see cref="GameSessionFailure.ConcealmentUnavailable"/>). The
    /// crouch animation and its art are the host's: the source sets no flags on a critter whose crouch art is missing, which the
    /// session does not see. One revision.
    /// </summary>
    [OriginalFunction(0x45EE90, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45EE30, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x579FA0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x57A0A0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult ToggleHeroConcealment(GameSessionVersion expected)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead() || _life.Hero is { } life && LifeRules.IsUnconscious(life.State, HeroLifeFacts(life.State)))
            return Reject(GameSessionFailure.ConcealmentUnavailable);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var on = !_combat.HeroConcealed;
        _combat.HeroConcealed = on;
        foreach (var follower in _party.Order)
            if (on) _combat.Concealed.Add(follower); else _combat.Concealed.Remove(follower);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// The hero's prowling margin over one observer in a sight or hearing check (AW4, AW5): 0 while the hero is not concealed;
    /// otherwise <see cref="StealthRules.Margin"/> of the shared check's prowling effectiveness (opposed by the observer's
    /// perception, <paramref name="observerPerception"/>) and its difficulty for <paramref name="observer"/> (the host's facts of
    /// the observer, the light at the hero, cover and the worn armour's silent-move adjustments; the sight or hearing flag is
    /// set here). The source computes it at every check and stores nothing; the awareness checks (Ai.AwarenessRules, slice I2) take it as an input, called by the AI from slice I4 on.
    /// </summary>
    public SessionProwlingMargin HeroProwlingMargin(int observerPerception, SkillDifficultyFacts observer, bool seeing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(observer);
        if (_hero is null || !_combat.HeroConcealed) return new(0, null, null);
        var request = HeroSkillRequest(Skill.Prowling, null);
        // ARC-AI-1518: the prowler's own light and worn armour (AW4), the hero's whatever the observer facts say.
        return ProwlingMargin(request, observerPerception, observer, seeing, QueryHeroLight(),
            EquipmentRules.SilentMoves(_equipment.Items().Values, HeroAptitude()), request.Difficulty.SourceInjuries);
    }

    /// <summary>
    /// ARC-AI-1560 (VBG7, the original's 0x45EDE0 with a follower as the prowler): the prowling margin of a concealed follower over one
    /// observer in a sight or hearing check, as <see cref="HeroProwlingMargin"/> gives the hero's: 0 while the NPC is not in the session's
    /// concealed set (<see cref="IsConcealed"/>), a refused life record, or invalid input; otherwise <see cref="StealthRules.Margin"/> of
    /// the NPC's own prowling level and training (<see cref="QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)"/>: the sheet the session levelled it to, else its map data's),
    /// opposed by <paramref name="observerPerception"/>, under the check's difficulty for <paramref name="observer"/> with the NPC's own
    /// light and worn armour's silent-move terms (the build the host reads its attack from) and its blindness and crippled limbs. The
    /// effectiveness is not the local PC's (as the other NPC skill checks read it). Stores nothing, so no revision.
    /// </summary>
    public SessionProwlingMargin NpcProwlingMargin(SessionNpcProwler prowler, int observerPerception, SkillDifficultyFacts observer, bool seeing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(prowler);
        ArgumentNullException.ThrowIfNull(prowler.Critter);
        ArgumentNullException.ThrowIfNull(prowler.Loaded);
        ArgumentNullException.ThrowIfNull(prowler.Modifiers);
        ArgumentNullException.ThrowIfNull(observer);
        var entity = prowler.Critter.Entity;
        if (!_combat.Concealed.Contains(entity) || prowler.Loaded.Entity != entity || !AutoLevelRules.IsValid(prowler.Loaded.Sheet) || !AutoLevelRules.IsValid(prowler.Modifiers) ||
            Resolve(prowler.Critter.Target, out var record, out _, out _) != GameSessionFailure.None)
            return new(0, null, null);
        var sheet = _advancement.Npcs.TryGetValue(entity, out var levelled) ? levelled.Sheet : prowler.Loaded.Sheet;
        var skill = QueryNpcSkills(prowler.Loaded, prowler.Modifiers)[Skill.Prowling];
        var aptitude = Math.Clamp(AutoLevelRules.DerivedStat(sheet, prowler.Modifiers, CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var request = new SkillCheckRequest
        {
            Skill = Skill.Prowling,
            Level = skill.Level,
            Training = skill.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = AutoLevelRules.EffectiveStat(sheet, prowler.Modifiers, CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                Difficulty = _difficulty,
            },
        };
        return ProwlingMargin(request, observerPerception, observer, seeing, prowler.Critter.OwnLight,
            EquipmentRules.SilentMoves(prowler.Critter.WornItems.Values, aptitude), SourceInjuries(record.State.Conditions));
    }

    // The margin of a prowler's effectiveness (opposed by the observer's perception) and the check's difficulty for the observer (AW4).
    private static SessionProwlingMargin ProwlingMargin(SkillCheckRequest request, int observerPerception, SkillDifficultyFacts observer, bool seeing,
        LightEmission light, IReadOnlyList<int> silentMoves, SkillSourceInjuries injuries)
    {
        var effectiveness = SkillCheckRules.Effectiveness(Skill.Prowling, request.Level, request.Effectiveness with { OpponentValue = observerPerception });
        var difficulty = SkillCheckRules.Difficulty(Skill.Prowling, request.Training, effectiveness, observer with
        {
            // The observer is the check's target critter, so its paralysed, unconscious, stunned and sleeping terms count (AW4).
            Target = true,
            TargetCritter = true,
            SeeingCheck = seeing,
            HearingCheck = !seeing,
            SourceLight = light,
            ArmorSilentMoves = silentMoves,
            SourceInjuries = injuries,
        });
        return new(StealthRules.Margin(effectiveness, difficulty.Total), effectiveness, difficulty);
    }

    // A dialog's start (0x567460, ST4): the hero and the NPC it talks to are no longer concealed; true when that changed anything.
    [OriginalFunction(0x567460, OriginalFunctionScope.Partial)]
    private bool EndConcealmentForDialog(string entity)
    {
        var changed = _combat.HeroConcealed || _combat.Concealed.Contains(entity);
        _combat.HeroConcealed = false;
        _combat.Concealed.Remove(entity);
        return changed;
    }

    // The attack event (0x4A9650, ST4): when the victim is the hero or an NPC the hero leads (following or waiting), the hero and
    // the followers in its follower list are no longer concealed; a waiting victim is not in that list and keeps its state. The
    // attacker is not touched as the attacker: the hero attacking its own follower is in the victim's party and loses it too. True
    // when that changed anything.
    private bool EndConcealmentForAttackOn(SessionCombatant victim)
    {
        if (!victim.IsHero && (victim.Entity is not { } entity || !_party.Records.TryGetValue(entity, out var record) ||
            record.Standing is not (CompanionStanding.Following or CompanionStanding.Waiting)))
            return false;
        var changed = _combat.HeroConcealed || _party.Order.Any(_combat.Concealed.Contains);
        _combat.HeroConcealed = false;
        foreach (var follower in _party.Order) _combat.Concealed.Remove(follower);
        return changed;
    }

    /// <summary>
    /// ARC-AI-1517 (attack event 0x4A9650 → 0x45EE30, VBG6): the attack event's concealment step for any attacker. When the victim is the
    /// hero or an NPC the hero leads, the hero and the followers in its follower list stop prowling (<see cref="EndConcealmentForAttackOn"/>);
    /// a victim the hero does not lead is no group of the session's. One revision of its own; no change (nothing concealed, or not the
    /// hero's group) is <see cref="GameSessionDisposition.NoChange"/>. The host calls it for the plan's
    /// <see cref="Arcanum.Simulation.Ai.AttackEventPlan.LosesConcealment"/>, which is set only while the group's head is concealed.
    /// </summary>
    public GameSessionMutationResult EndConcealmentForAttackEvent(GameSessionVersion expected, SessionCombatant victim)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(victim);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var concealed = _combat.HeroConcealed || _party.Order.Any(_combat.Concealed.Contains);
        if (!concealed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (!EndConcealmentForAttackOn(victim)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    private SessionConcealmentState CaptureConcealment() =>
        new(_hero is not null && _combat.HeroConcealed, _combat.Concealed.ToArray());

    // The hero's flag needs a hero; NPC ids are valid, unique, in ordinal order, and followers (following or waiting).
    private static GameSessionFailure ImportConcealment(SessionConcealmentState? saved, bool hero, PartyDomain party, CombatDomain combat)
    {
        if (saved?.Npcs is not { } npcs) return GameSessionFailure.InvalidSnapshot;
        if ((saved.Hero || npcs.Count > 0) && !hero) return GameSessionFailure.InvalidConcealment;
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
