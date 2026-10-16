namespace Arcanum.Simulation.Combat;

/// <summary>
/// Critical-hit effects other than the damage multiplier (FR-COMBAT-009). The resolver only reports them; applying
/// them (stun, knockout fatigue, crippling, drops, wear) belongs to the vital-state and item owners.
/// </summary>
[Flags]
public enum CriticalHitEffects
{
    None = 0,
    Stun = 1 << 0,
    Knockout = 1 << 1,
    Knockdown = 1 << 2,
    CrippleArm = 1 << 3,
    CrippleLeg = 1 << 4,
    Blind = 1 << 5,
    Scar = 1 << 6,
    DropHelmet = 1 << 7,
    DamageWeapon = 1 << 8,
    DropWeapon = 1 << 9,
    DamageArmor = 1 << 10,
}

/// <summary>Critical-miss effects on the attacker's own weapon and ammunition (FR-COMBAT-009).</summary>
[Flags]
public enum CriticalMissEffects
{
    None = 0,
    DestroyWeapon = 1 << 0,
    ExplodeWeapon = 1 << 1,
    DestroyAmmo = 1 << 2,
    LoseAmmo = 1 << 3,
}

/// <summary>
/// Context of one critical-hit effect roll. <see cref="Chance"/> is the source's critical effect chance: the weapon's
/// magic critical effect value (0 unarmed, for a thrown item or when the magic was cancelled) plus the attacker's
/// effect adjustment, +10 when aimed. <see cref="NpcHitsPc"/> suppresses the effects the source denies an NPC
/// against a PC. For a critical self-hit <see cref="Recipient"/> is the attacker.
/// </summary>
public sealed record CriticalHitContext(
    Combatant Attacker,
    Combatant Recipient,
    CombatWeapon Weapon,
    CriticalHitChart Chart,
    HitLocation HitLocation,
    bool Aimed,
    int Chance,
    bool NpcHitsPc,
    bool SelfHit);

/// <summary>
/// Context of one critical-miss effect roll on the attacker's weapon, made when the critical miss is a critical self-hit.
/// <see cref="Chance"/> is the weapon's magic critical-miss effect value (0 without a weapon item or when the magic was
/// cancelled) plus the attacker's critical-miss effect adjustment.
/// </summary>
public sealed record CriticalMissContext(Combatant Attacker, CombatWeapon Weapon, CriticalMissChart Chart, int Chance);

/// <summary>
/// The critical-effect tables (FR-COMBAT-009), injected so the resolver stays a pure function of its inputs. An
/// implementation draws its rolls from the given source with <see cref="CombatRollPurpose.CriticalHitEffect"/> or
/// <see cref="CombatRollPurpose.CriticalMissEffect"/>.
/// </summary>
public interface ICriticalEffects
{
    CriticalHitEffects RollHit(CriticalHitContext context, ICombatRollSource rolls);

    CriticalMissEffects RollMiss(CriticalMissContext context, ICombatRollSource rolls);
}

/// <summary>
/// No effect tables: every critical hit only gets its damage multiplier, which then falls back to ×1.5 whenever the
/// three multiplier rolls fail (the source's "no effect at all" rule).
/// </summary>
public sealed class NoCriticalEffects : ICriticalEffects
{
    public static readonly NoCriticalEffects Instance = new();

    private NoCriticalEffects() { }

    public CriticalHitEffects RollHit(CriticalHitContext context, ICombatRollSource rolls) => CriticalHitEffects.None;

    public CriticalMissEffects RollMiss(CriticalMissContext context, ICombatRollSource rolls) => CriticalMissEffects.None;
}
