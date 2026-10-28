using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>
/// Who a life call acts on (ARC-COMBAT-002): the session's <see cref="Hero"/>, whose facts the session builds from the
/// character owner, or an NPC by entity id with the host's <see cref="LifeFacts"/> and the state its map data loads with
/// (<see cref="Loaded"/>: stored hit-point and fatigue damage, poison level, injury flags). The loaded state is read only
/// while the session holds no record for the entity; after the first change the record wins.
/// </summary>
public sealed class SessionLifeTarget
{
    private SessionLifeTarget(bool hero, string? entity, LifeFacts? facts, LifeState loaded)
    {
        IsHero = hero;
        Entity = entity;
        Facts = facts;
        Loaded = loaded;
    }

    /// <summary>The session's player character.</summary>
    public static SessionLifeTarget Hero { get; } = new(true, null, null, LifeState.Unharmed);

    /// <summary>An NPC (or another critter) by its stable entity id; a null id or facts throw, so an unresolved id can never
    /// reach the hero.</summary>
    public static SessionLifeTarget Critter(string entity, LifeFacts facts, LifeState loaded)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(facts);
        return new(false, entity, facts, loaded);
    }

    /// <summary>The entity id, or null for the hero.</summary>
    public string? Entity { get; }
    public LifeFacts? Facts { get; }
    public LifeState Loaded { get; }
    public bool IsHero { get; }
}

/// <summary>
/// One critter's saved life: its state and the session game time of its last death (the source's death time, kept
/// after a resurrection), or null when it never died in this session.
/// </summary>
public readonly record struct SessionLifeRecord(LifeState State, long? DeathTimeMs);

/// <summary>The saved life of one NPC.</summary>
public readonly record struct SessionCritterLife(string Entity, SessionLifeRecord Record);

/// <summary>
/// Saved life domain: the hero's record (null exactly when the session has no hero) and the NPC records the session
/// changed, in ordinal entity order. An NPC without a record is at the state its map data loads with.
/// </summary>
public sealed class SessionLifeState
{
    internal SessionLifeState(SessionLifeRecord? hero, SessionCritterLife[] critters)
    {
        Hero = hero;
        Critters = Array.AsReadOnly(critters);
    }

    public SessionLifeRecord? Hero { get; }
    public IReadOnlyList<SessionCritterLife> Critters { get; }
}

/// <summary>
/// A life query: the record (for an NPC without one, its loaded state and no death time), the current values under the
/// facts, and whether the session holds a record.
/// </summary>
public sealed record SessionLifeView(SessionLifeRecord Record, LifeStatus Status, bool Recorded);

/// <summary>A life query or its typed refusal.</summary>
public sealed record SessionLifeQueryResult(GameSessionFailure Failure, SessionLifeView? View);

/// <summary>
/// A life mutation. <see cref="Outcome"/> is set whenever the step ran, also for NoChange (a knockdown, a dead target or a
/// full scar list change no state but still tell the host what happened); it is null for a refusal.
/// </summary>
public sealed record SessionLifeResult(GameSessionDisposition Disposition, GameSessionFailure Failure, LifeOutcome? Outcome,
    GameSessionVersion Version)
{
    /// <summary>ARC-MAGIC-024: the item events the step calls for, in order (ledger IC2, IC4): for a damage step with its parties
    /// (<see cref="GameSessionOwner.ApplyDamage"/>) its wrapper calls, for any other step the fatigue setter's going unconscious;
    /// empty for a refusal. The host fires them (<see cref="GameSessionOwner.FireItemEvents"/>,
    /// <see cref="SessionSpellCaster.FireItemEvents"/>); the session fires none by itself.</summary>
    public IReadOnlyList<SessionItemEvent> ItemEvents { get; init; } = [];
}

/// <summary>The life domain of one owner.</summary>
internal sealed class LifeDomain
{
    internal SessionLifeRecord? Hero { get; set; }
    internal Dictionary<string, SessionLifeRecord> Critters { get; } = new(StringComparer.Ordinal);

    internal SessionLifeState Capture() => new(Hero,
        Critters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new SessionCritterLife(pair.Key, pair.Value)).ToArray());

    /// <summary>
    /// The hero's life facts at a state: level and effective stats from the character owner, strength without and under
    /// each poison penalty, crippled legs as a query condition. The hit-point and fatigue points are the ones the hero bought
    /// (ARC-WORLD-036); the adjustments are 0, as every pre-made PC prototype stores none. No traits; the normal resistance is the worn armour's
    /// (ARC-WORLD-028, <see cref="GameSessionOwner"/> equipment); the effect changes of the two maxima are the hero's durable
    /// effects' (ARC-WORLD-029). Pools or adjustments added here must raise <see cref="EffectDomain.HeroMaximumCeiling"/> too,
    /// or the effect bounds stop holding.
    /// </summary>
    internal static LifeFacts HeroFacts(CharacterStatsOwner stats, SessionHeroState hero, LifeState state, int normalResistance,
        ImmutableArray<EffectModifier> maxHitPoints, ImmutableArray<EffectModifier> maxFatigue)
    {
        var level = hero.Level;
        var legs = state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth);
        CharacterStatsSnapshot At(int poison) => stats.Query(new(0, poison, legs)).Snapshot!;
        var plain = At(0);
        return new LifeFacts
        {
            HitPointPoints = hero.Advancement.HitPointPoints,
            FatiguePoints = hero.Advancement.FatiguePoints,
            Level = level,
            Strength = plain.GetEffective(CharacterStat.Strength),
            PoisonedStrength = [At(100).GetEffective(CharacterStat.Strength), At(200).GetEffective(CharacterStat.Strength),
                At(300).GetEffective(CharacterStat.Strength)],
            Constitution = plain.GetEffective(CharacterStat.Constitution),
            Willpower = plain.GetEffective(CharacterStat.Willpower),
            HealRate = plain.GetEffective(CharacterStat.HealRate),
            PoisonRecovery = plain.GetEffective(CharacterStat.PoisonRecovery),
            NormalResistance = normalResistance,
            MaxHitPointEffects = maxHitPoints,
            MaxFatigueEffects = maxFatigue,
        };
    }

    internal static bool ValidRecord(SessionLifeRecord record, long clockMs) =>
        LifeRules.IsValid(record.State) && (record.DeathTimeMs is not { } time || time >= 0 && time <= clockMs);
}

/// <summary>
/// The session generator as the roll source of one life step: it draws from a trial copy, so a step that runs out of draws
/// is refused with nothing drawn, and the owner adopts the copy only when the step commits.
/// </summary>
internal sealed class TrialCombatRolls(SeededRandom trial) : ICombatRollSource
{
    internal SeededRandom Trial { get; } = trial;

    public int Roll(CombatRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        return Trial.TryNext(min, max, out var value) ? value : throw new ExhaustedException();
    }

    internal sealed class ExhaustedException : Exception;
}
