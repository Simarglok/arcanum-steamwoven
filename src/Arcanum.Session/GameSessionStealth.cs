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
        // 0x45EE30 forces the state on the PC and its recursive follower list (0x441260): the followers of followers too (ARC-WORLD-2118).
        foreach (var follower in FollowerTreeOf(SessionEventKinds.PlayerEntity))
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
    /// ARC-AI-1560 (VBG7, the original's prowling flag of any critter, a follower as the prowler): the prowling margin of a concealed follower over one
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
        var tree = FollowerTreeOf(SessionEventKinds.PlayerEntity);
        var changed = _combat.HeroConcealed || tree.Any(_combat.Concealed.Contains);
        _combat.HeroConcealed = false;
        foreach (var follower in tree) _combat.Concealed.Remove(follower);
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
        var concealed = _combat.HeroConcealed || FollowerTreeOf(SessionEventKinds.PlayerEntity).Any(_combat.Concealed.Contains);
        if (!concealed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (!EndConcealmentForAttackOn(victim)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    /// <summary>
    /// ARC-AI-1653 (the unconceal goal's end node 0x42AFB0 → 0x45EE30, ledger VBG19): the NPC's own goal run stood it up and, unless its leader is
    /// concealed, ends its concealment. The leader is the hero while the NPC is led by it (following or waiting, HK2 keeps the leader); a
    /// follower of a sneaking hero therefore stays concealed (it only stands up), while a concealed NPC whose hero is not concealed, or that has
    /// no leader, stops prowling. The NPC's own followers (the call reaches them too) are not modelled: a follower leads none. One revision of its
    /// own; no change (not concealed, or its leader is) is <see cref="GameSessionDisposition.NoChange"/>. The hero's own concealment is never touched here.
    /// ARC-WORLD-2118: the leader is the hero or an NPC (<see cref="LeaderConcealed"/>), and the NPC's own followers stand up with it.
    /// </summary>
    [OriginalFunction(0x42AFB0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult EndNpcConcealmentForUnconceal(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!_combat.Concealed.Contains(entity) || LeaderConcealed(entity))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // 0x45EE30 with 0 reaches the NPC's own followers too (ARC-WORLD-2118).
        SetConcealment(entity, false);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    // ARC-WORLD-2118: 0x45EE30 (critter, state): the state on the critter (0x45EE90) and on every critter of its recursive follower list (0x441260). The session holds a concealment only for the critters
    // whose chain of leaders reaches the hero, so a critter outside the chain is never added.
    private void SetConcealment(string critter, bool on)
    {
        foreach (var entity in FollowerTreeOf(critter).Prepend(critter))
            if (on && PcLeads(entity)) _combat.Concealed.Add(entity); else _combat.Concealed.Remove(entity);
    }

    // The join's concealment (0x45DE90 → 0x45EE30 with the leader's 0x45EDE0 answer): the joiner and its own followers take the state of the leader they join; the hero's is its flag, an NPC's the set.
    private void TakeLeaderConcealment(string joiner, string leader) =>
        SetConcealment(joiner, leader == SessionEventKinds.PlayerEntity ? _combat.HeroConcealed : _combat.Concealed.Contains(leader));

    // Whether the critter's leader (0x45DE00) is concealed (0x45EDE0): the hero's flag or an NPC leader's entry.
    private bool LeaderConcealed(string critter) => LeaderOf(critter) switch
    {
        null => false,
        SessionEventKinds.PlayerEntity => _combat.HeroConcealed,
        var leader => _combat.Concealed.Contains(leader),
    };

    // A critter leaves its leader or loses it (0x45E040: a concealed leaver is revealed with 0x45EE30, its followers with it). The session holds a concealment only for the hero's chain, so the leaver's
    // whole follower tree drops it, concealed or not (the original leaves a follower of a leaver concealed that has stood up on its own; it is no longer in the hero's chain). Call before the links change.
    private void DropConcealmentOfSubtree(string critter)
    {
        _combat.Concealed.Remove(critter);
        foreach (var entity in FollowerTreeOf(critter)) _combat.Concealed.Remove(entity);
    }

    private SessionConcealmentState CaptureConcealment() =>
        new(_hero is not null && _combat.HeroConcealed, _combat.Concealed.ToArray());

    // Whether the critter's chain of leaders reaches the hero (the import's PcLeads: the session is not built yet).
    private static bool ChainReachesHero(PartyDomain party, string critter)
    {
        var at = critter;
        for (var step = 0; step <= party.NpcLed.Count + 1; step++)
        {
            if (party.Records.TryGetValue(at, out var record) && record.LedByPc) return true;
            if (!party.NpcLed.TryGetValue(at, out var led)) return false;
            at = led.Leader;
        }
        return false;
    }

    // The hero's flag needs a hero; NPC ids are valid, unique, in ordinal order, and followers of the hero's chain (following or waiting, or led by such a follower: ARC-WORLD-2118).
    private static GameSessionFailure ImportConcealment(SessionConcealmentState? saved, bool hero, PartyDomain party, CombatDomain combat)
    {
        if (saved?.Npcs is not { } npcs) return GameSessionFailure.InvalidSnapshot;
        if ((saved.Hero || npcs.Count > 0) && !hero) return GameSessionFailure.InvalidConcealment;
        if (npcs.Count > SessionConcealmentState.MaxNpcs) return GameSessionFailure.BudgetExceeded;
        for (var i = 0; i < npcs.Count; i++)
            if (!SessionValidation.Entity(npcs[i]) || i > 0 && string.CompareOrdinal(npcs[i - 1], npcs[i]) >= 0 ||
                !(party.Records.TryGetValue(npcs[i], out var record) && record.Standing is CompanionStanding.Following or CompanionStanding.Waiting || ChainReachesHero(party, npcs[i])))
                return GameSessionFailure.InvalidConcealment;
        combat.HeroConcealed = saved.Hero;
        foreach (var entity in npcs) combat.Concealed.Add(entity);
        return GameSessionFailure.None;
    }
}
