using System.Collections.Immutable;
using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// The stun and injury conditions the life state owns (the source's critter flags). Values are Steamwoven identifiers,
/// not source bits.
/// </summary>
[Flags]
public enum LifeConditions
{
    None = 0,
    /// <summary>Stunned by a critical hit until the stun animation ends (see <see cref="LifeRules.ClearStun"/>).</summary>
    Stunned = 1 << 0,
    Blinded = 1 << 1,
    CrippledArmOne = 1 << 2,
    CrippledArmsBoth = 1 << 3,
    CrippledLegsBoth = 1 << 4,
}

/// <summary>
/// What a critter is, as far as its life rules ask: creature flags (undead, mechanical, fatigue-immune, the two
/// resurrection refusals), object flags (invulnerable, nigh-invulnerable) and the spell or critter states owned elsewhere
/// (stoned, paralysed). Values are Steamwoven identifiers, not source bits.
/// </summary>
[Flags]
public enum LifeTraits
{
    None = 0,
    /// <summary>Never unconscious, whatever its fatigue; poison does not reach it.</summary>
    Undead = 1 << 0,
    /// <summary>Poison does not reach it.</summary>
    Mechanical = 1 << 1,
    /// <summary>Fatigue damage from attacks does not reach it.</summary>
    FatigueImmune = 1 << 2,
    /// <summary>Turned to stone: hit-point, fatigue and poison damage are zeroed, critical effects still apply.</summary>
    Stoned = 1 << 3,
    /// <summary>Paralysed: not active.</summary>
    Paralyzed = 1 << 4,
    /// <summary>The object's invulnerable flag: a damage step changes nothing, and a kill only sets the pinned damage.</summary>
    Invulnerable = 1 << 5,
    /// <summary>Only an "uber" weapon damages it (the attack resolver checks the weapon); a poison tick never does.</summary>
    NighInvulnerable = 1 << 6,
    /// <summary>Refuses reanimation.</summary>
    Unrevivifiable = 1 << 7,
    /// <summary>Refuses resurrection.</summary>
    Unresurrectable = 1 << 8,
}

/// <summary>Explicit bounds of the life rules (ARC-COMBAT-002).</summary>
public static class LifeLimits
{
    /// <summary>
    /// Hit-point damage the source writes when a critter dies (and on a kill). Pools are bounded so that every maximum
    /// stays below it, so the pinned damage always reads as dead.
    /// </summary>
    public const int DeathHitPointDamage = 32_000;
    /// <summary>Stored hit-point and fatigue damage saturate here; every maximum is far below it.</summary>
    public const int MaxDamage = 1_000_000;
    /// <summary>The source's poison level range is 0..1000.</summary>
    public const int MaxPoisonLevel = 1000;
    /// <summary>Scarring effects kept per critter; a scar beyond this is reported, not added.</summary>
    public const int MaxScars = 64;
    /// <summary>Bound of the stored hit-point and fatigue points (each counts four).</summary>
    public const int MaxPoolPoints = 5000;
    /// <summary>Bound of the magnitude of the stored hit-point and fatigue adjustments.</summary>
    public const int MaxPoolAdjustment = 5000;
    /// <summary>The source's level range is 0..51.</summary>
    public const int MaxLevel = 51;
    /// <summary>Bound of an effective stat a formula reads.</summary>
    public const int MaxStat = 100;
    /// <summary>Bound of the magnitude of a normal resistance in percent (monstrous NPCs are not clamped to 0..95).</summary>
    public const int MaxResistance = 1000;
    /// <summary>Bound of the magnitude of one damage or heal amount.</summary>
    public const int MaxAmount = 1_000_000;
    /// <summary>Bound of one resting heal (ten game years).</summary>
    public const int MaxRestHours = 24 * 365 * 10;
    /// <summary>Bound of the effect changes of one maximum (ARC-WORLD-029): 64 effects of up to seven changes each.</summary>
    public const int MaxMaximumEffects = 64 * 7;
    /// <summary>Bound of the magnitude of one effect change of a maximum.</summary>
    public const int MaxMaximumEffectMagnitude = 1_000_000;
}

/// <summary>
/// The mutable life state of one critter: hit-point damage, fatigue damage, the poison level, the conditions and the
/// number of scarring effects. Current hit points and fatigue are the maxima of <see cref="LifeFacts"/> minus the damage,
/// so a state means nothing without its facts. <see cref="Unharmed"/> is the source's fresh critter.
/// </summary>
public readonly record struct LifeState(int HitPointDamage, int FatigueDamage, int PoisonLevel, LifeConditions Conditions, int Scars)
{
    public static LifeState Unharmed => default;
}

/// <summary>
/// The facts the life formulas read about one critter, every stat already effective (effects, background and the
/// critter's crippled legs resolved by the caller). <see cref="Strength"/> is the effective strength without the poison
/// penalty; <see cref="PoisonedStrength"/> holds the effective strength under poison penalty 1, 2 and 3 (poison level
/// 100.., 200.., 300..) and may be left empty when it equals <c>max(1, Strength − penalty)</c> (no effects push strength
/// against its range). The session fills all three for its hero from the character owner. <see cref="NormalResistance"/> is the effective normal
/// resistance a poison tick meets (attacks get theirs from the attack resolver). <see cref="MaxHitPointEffects"/> and
/// <see cref="MaxFatigueEffects"/> are the critter's effect changes of the two maxima (ARC-WORLD-029, the source's <c>maxhps</c> and
/// <c>maxfatigue</c>), applied to the whole sum of points, adjustment and stat part; facts whose maxima reach
/// <see cref="LifeLimits.DeathHitPointDamage"/> in either direction are invalid.
/// </summary>
public sealed record LifeFacts
{
    public int HitPointPoints { get; init; }
    public int HitPointAdjustment { get; init; }
    public int FatiguePoints { get; init; }
    public int FatigueAdjustment { get; init; }
    public int Level { get; init; } = 1;
    public int Strength { get; init; } = 8;
    public ImmutableArray<int> PoisonedStrength { get; init; } = ImmutableArray<int>.Empty;
    public int Constitution { get; init; } = 8;
    public int Willpower { get; init; } = 8;
    /// <summary>Effective HealRate stat (fatigue recovery and resting heal per step).</summary>
    public int HealRate { get; init; } = 3;
    /// <summary>Effective PoisonRecovery stat (poison removed per recovery tick).</summary>
    public int PoisonRecovery { get; init; } = 8;
    public int NormalResistance { get; init; }
    public LifeTraits Traits { get; init; }
    public ImmutableArray<EffectModifier> MaxHitPointEffects { get; init; } = ImmutableArray<EffectModifier>.Empty;
    public ImmutableArray<EffectModifier> MaxFatigueEffects { get; init; } = ImmutableArray<EffectModifier>.Empty;
    /// <summary>The critter's race, whose stat ranges cut a stat an effect changes (ARC-WORLD-1144: the session re-reads an NPC's strength, constitution and willpower under its effect list; the life
    /// formulas themselves do not read it). Human for a critter whose host does not say.</summary>
    public CharacterRace Race { get; init; } = CharacterRace.Human;
}

/// <summary>
/// The current values of one state under its facts. <see cref="IsDead"/> is hit points ≤ 0, <see cref="IsUnconscious"/>
/// fatigue ≤ 0 for a critter that is not undead, and <see cref="IsActive"/> excludes both, stunned, paralysed and stoned
/// (the source's predicates; a destroyed or switched-off object is the host's).
/// </summary>
public sealed record LifeStatus(int MaxHitPoints, int HitPoints, int MaxFatigue, int Fatigue, bool IsDead, bool IsUnconscious,
    bool IsActive);

/// <summary>
/// One damage packet for the life state (the source's damage step after resistances, difficulty and the critical
/// multiplier, which the attack resolver already applied). <see cref="HitPoints"/> is normal + electrical + fire and may be
/// negative (a resistance above 100 % heals); <see cref="Fatigue"/> counts only when positive; <see cref="Poison"/> is added
/// to the poison level. <see cref="Effects"/> are the critical-hit effects; only stun, knockout, knockdown, blinding,
/// crippling and scarring belong to the life state, the item effects are the item owner's. <see cref="Location"/> picks
/// the dying animation.
/// </summary>
public sealed record LifeDamage
{
    public int HitPoints { get; init; }
    public int Fatigue { get; init; }
    public int Poison { get; init; }
    public CriticalHitEffects Effects { get; init; }
    public HitLocation Location { get; init; }
    /// <summary>The death flag of the packet (a spell's <c>Death</c>): a death falls with the plain animation (ledger FA10).</summary>
    public bool PlainDeath { get; init; }

    /// <summary>ARC-MAGIC-012: the damage of a step without an attack (<see cref="AttackResolver.DamageStep"/>) at
    /// <paramref name="location"/>, or null when it was prevented.</summary>
    public static LifeDamage? FromStep(AttackDamage damage, HitLocation location = HitLocation.Torso, bool plainDeath = false)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (damage.Prevented != DamagePrevention.None) return null;
        return new LifeDamage
        {
            HitPoints = damage.HitPointDamage,
            Fatigue = damage.FatigueDamage,
            Poison = damage.PoisonDamage,
            Effects = damage.HitEffects,
            Location = location,
            PlainDeath = plainDeath,
        };
    }

    /// <summary>
    /// The damage of a resolved attack for its recipient (<see cref="AttackDamage.Recipient"/>: the target, or the attacker
    /// of a critical self-hit), or null when the attack dealt none: refused, missed, or prevented by invulnerability or a
    /// wall, where the source returns before any life change.
    /// </summary>
    public static LifeDamage? FromAttack(AttackResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.Damage is not { Prevented: DamagePrevention.None } damage) return null;
        return new LifeDamage
        {
            HitPoints = damage.HitPointDamage,
            Fatigue = damage.FatigueDamage,
            Poison = damage.PoisonDamage,
            Effects = damage.HitEffects,
            Location = resolution.HitLocation,
        };
    }
}

/// <summary>
/// One healing (the source's heal step): amounts of hit points, fatigue and poison removed, and the flags of the heal.
/// <see cref="Full"/> heals all hit-point damage (and all fatigue damage when <see cref="Fatigue"/> is positive), removes
/// five scars and clears blindness and crippling. Without it, <see cref="Scars"/> removes five scars and
/// <see cref="Blindness"/>, <see cref="CrippledArms"/> and <see cref="CrippledLegs"/> clear their condition.
/// <see cref="Resurrect"/> lets the heal reach a dead critter (<see cref="Reanimate"/> marks a reanimation).
/// </summary>
public sealed record LifeHeal
{
    public int HitPoints { get; init; }
    public int Fatigue { get; init; }
    public int Poison { get; init; }
    public bool Full { get; init; }
    public bool Scars { get; init; }
    public bool Blindness { get; init; }
    public bool CrippledArms { get; init; }
    public bool CrippledLegs { get; init; }
    public bool Resurrect { get; init; }
    public bool Reanimate { get; init; }
}

/// <summary>What a life transition did, for the host's animations, messages and other owners.</summary>
[Flags]
public enum LifeEvents
{
    None = 0,
    /// <summary>The critter was already dead: damage, recovery and healing without resurrection change nothing.</summary>
    AlreadyDead = 1 << 0,
    Stunned = 1 << 1,
    /// <summary>A knockdown or knockout: the host plays the knockdown.</summary>
    KnockedDown = 1 << 2,
    /// <summary>A knockout set fatigue damage to maximum fatigue + 10..20.</summary>
    KnockedOut = 1 << 3,
    Scarred = 1 << 4,
    /// <summary>A scar was not added because the critter already has <see cref="LifeLimits.MaxScars"/>.</summary>
    ScarLimitReached = 1 << 5,
    Blinded = 1 << 6,
    CrippledArm = 1 << 7,
    CrippledBothArms = 1 << 8,
    CrippledLegs = 1 << 9,
    /// <summary>A crippling arm hit: the item owner drops the wielded weapon and shield where they can be unwielded.</summary>
    ArmHitDropsItems = 1 << 10,
    /// <summary>Fatigue went from above 0 to 0 or below (not for undead): spells end, the host plays the knockdown.</summary>
    LostConsciousness = 1 << 11,
    /// <summary>Fatigue went from 0 or below back above 0: the host plays the get-up.</summary>
    RegainedConsciousness = 1 << 12,
    /// <summary>The death step ran: hit-point damage pinned, death time stamped, dying animation.</summary>
    Died = 1 << 13,
    /// <summary>A resurrection ran (the critter may still be dead without a hit-point heal).</summary>
    Resurrected = 1 << 14,
    ResurrectionRefused = 1 << 15,
    /// <summary>The stun ended.</summary>
    StunEnded = 1 << 16,
}

/// <summary>Which dying animation the source picks: the plain fall for a kill or at most 20 damage, else by location.</summary>
public enum DeathAnimation { None, Plain, Head, Leg, Body }

/// <summary>
/// The result of one life transition: the state before and after, what happened, the hit-point damage the step dealt (the
/// source's total damage; negative heals) and the dying animation when <see cref="LifeEvents.Died"/>.
/// </summary>
public sealed record LifeOutcome(LifeState Before, LifeState After, LifeEvents Events, int HitPointDamage, DeathAnimation Death)
{
    public bool Changed => Before != After;
}
