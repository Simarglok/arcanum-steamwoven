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

/// <summary>Critical-miss effects on the attacker's own weapon and ammunition (FR-COMBAT-009). <see cref="DestroyAmmo"/> removes all
/// the holder's ammunition of the weapon's type, <see cref="LoseAmmo"/> five shots' worth (the source's 0x4B65D0, ARC-COMBAT-009).</summary>
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
/// implementation draws its rolls from the given source, each with a purpose of its own (<see cref="OriginalCriticalEffects"/> uses
/// <see cref="CombatRollPurpose.CriticalStun"/> … <see cref="CombatRollPurpose.CriticalLoseAmmo"/>; the generic
/// <see cref="CombatRollPurpose.CriticalHitEffect"/> and <see cref="CombatRollPurpose.CriticalMissEffect"/> serve others).
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

/// <summary>
/// The original's critical-effect procedures (ARC-COMBAT-009; the effect part of the critical-hit procedure 0x4B5F40 and the
/// effect rolls of the critical-miss procedure 0x4B6410, read in the retail 1.0.7.4 executable by two readers, ledger "Critical
/// effects"). The thresholds are in the code, not in data; the data are the weapon's charts and the critter's body type. Every
/// roll is the source's d100 against a threshold (a pass is ≤), except the constitution checks (0x45F060): a roll of
/// 1 .. <see cref="Combatant.ConstitutionMaximum"/> passes at ≤ constitution + modifier, and only a failed check gives the effect.
/// The rolls are made in the source's order, so a seeded run draws as the original does. The multiplier rolls before them and the
/// ×1.5 fallback after them are the resolver's.
/// </summary>
public sealed class OriginalCriticalEffects : ICriticalEffects
{
    /// <summary>The body type a knockdown needs.</summary>
    public const int KnockdownBody = 0;
    /// <summary>The body type that takes no stun, knockout, injury, scar or knockdown (only the item effects).</summary>
    public const int ItemsOnlyBody = 2;
    /// <summary>The body type whose location effects are the head's only.</summary>
    public const int HeadOnlyBody = 4;
    /// <summary>The body type without arm crippling.</summary>
    public const int NoArmsBody = 5;

    public static readonly OriginalCriticalEffects Instance = new();

    private OriginalCriticalEffects() { }

    /// <summary>
    /// The critical-hit effects on <see cref="CriticalHitContext.Recipient"/> (0x4B5F40 after the multiplier rolls), in order:
    /// <list type="number">
    /// <item>unless the body type is <see cref="ItemsOnlyBody"/> or the recipient is undead: stun at d100 ≤ chance + 5 (+10 for the
    /// crushing or electrical chart, +10 on the head without a helmet) and a failed constitution check at −5; then, for a stunned
    /// recipient that is no PC and not fatigue-immune, knockout at d100 ≤ the same threshold and another failed check at −5;</item>
    /// <item>the hit location, one at most: an arm (not for body types <see cref="HeadOnlyBody"/> and <see cref="NoArmsBody"/>)
    /// cripples at d100 ≤ chance + 1 and a failed check at −5, a leg (not for <see cref="HeadOnlyBody"/>) the same; the head with
    /// the crushing chart drops a worn helmet at d100 ≤ chance + 5, with any other chart blinds (not when an NPC hits a PC) at
    /// d100 ≤ chance (+1 without a helmet) and a failed check at 0;</item>
    /// <item>a PC is scarred at d100 ≤ chance + 5, except a scarred PC that hit itself;</item>
    /// <item>(also for the undead) body type <see cref="KnockdownBody"/> is knocked down at d100 ≤ chance + 5 (+5 crushing, +10 leg);</item>
    /// <item>for any body type: a wielded weapon is damaged at d100 ≤ chance + 10 and, unless an NPC hits a PC, dropped at a second
    /// d100 ≤ chance + 10; the armour the location strikes is damaged at d100 ≤ chance + 10.</item>
    /// </list>
    /// </summary>
    [OriginalFunction(0x4B5F40, OriginalFunctionScope.Partial)]
    public CriticalHitEffects RollHit(CriticalHitContext context, ICombatRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rolls);
        var target = context.Recipient;
        var chance = context.Chance;
        var location = context.HitLocation;
        var chart = context.Chart;
        var body = target.CriticalBody;
        var helmet = target.Worn.HasFlag(CombatWornItems.Helmet);
        var effects = CriticalHitEffects.None;
        bool D100(CombatRollPurpose purpose, int threshold) => rolls.Roll(purpose, 1, 100) <= threshold;
        // A failed constitution check (0x45F060): 1 .. the race's maximum against constitution + modifier.
        bool Fails(CombatRollPurpose purpose, int modifier) =>
            rolls.Roll(purpose, 1, Math.Max(1, target.ConstitutionMaximum)) > target.Constitution + modifier;

        if (body != ItemsOnlyBody && !target.Conditions.HasFlag(CombatantConditions.Undead))
        {
            var stun = chance + 5;
            if (chart is CriticalHitChart.Crushing or CriticalHitChart.Electrical) stun += 10;
            if (location == HitLocation.Head && !helmet) stun += 10;
            if (D100(CombatRollPurpose.CriticalStun, stun) && Fails(CombatRollPurpose.CriticalStunCheck, -5))
            {
                effects |= CriticalHitEffects.Stun;
                if (!target.IsPc && !target.Conditions.HasFlag(CombatantConditions.FatigueImmune)
                    && D100(CombatRollPurpose.CriticalKnockout, stun) && Fails(CombatRollPurpose.CriticalKnockoutCheck, -5))
                    effects |= CriticalHitEffects.Knockout;
            }

            if (location == HitLocation.Arm && body is not (HeadOnlyBody or NoArmsBody))
            {
                if (D100(CombatRollPurpose.CriticalCrippleArm, chance + 1) && Fails(CombatRollPurpose.CriticalCrippleArmCheck, -5))
                    effects |= CriticalHitEffects.CrippleArm;
            }
            else if (location == HitLocation.Leg && body != HeadOnlyBody)
            {
                if (D100(CombatRollPurpose.CriticalCrippleLeg, chance + 1) && Fails(CombatRollPurpose.CriticalCrippleLegCheck, -5))
                    effects |= CriticalHitEffects.CrippleLeg;
            }
            else if (location == HitLocation.Head)
            {
                if (chart == CriticalHitChart.Crushing)
                {
                    if (helmet && D100(CombatRollPurpose.CriticalDropHelmet, chance + 5)) effects |= CriticalHitEffects.DropHelmet;
                }
                else if (!context.NpcHitsPc && D100(CombatRollPurpose.CriticalBlind, chance + (helmet ? 0 : 1))
                    && Fails(CombatRollPurpose.CriticalBlindCheck, 0))
                    effects |= CriticalHitEffects.Blind;
            }

            if (target.IsPc && !(context.SelfHit && target.Scarred) && D100(CombatRollPurpose.CriticalScar, chance + 5))
                effects |= CriticalHitEffects.Scar;
        }

        if (body == KnockdownBody)
        {
            var knockdown = chance + 5;
            if (chart == CriticalHitChart.Crushing) knockdown += 5;
            if (location == HitLocation.Leg) knockdown += 10;
            if (D100(CombatRollPurpose.CriticalKnockdown, knockdown)) effects |= CriticalHitEffects.Knockdown;
        }

        if (target.Worn.HasFlag(CombatWornItems.Weapon))
        {
            if (D100(CombatRollPurpose.CriticalDamageWeapon, chance + 10)) effects |= CriticalHitEffects.DamageWeapon;
            if (!context.NpcHitsPc && D100(CombatRollPurpose.CriticalDropWeapon, chance + 10)) effects |= CriticalHitEffects.DropWeapon;
        }
        if (target.Worn.HasFlag(ArmorAt(location)) && D100(CombatRollPurpose.CriticalDamageArmor, chance + 10))
            effects |= CriticalHitEffects.DamageArmor;
        return effects;
    }

    /// <summary>
    /// The critical-miss effects on the attacker's own weapon (0x4B6410 after its 50 % critical self-hit roll), in order: "destroy
    /// weapon" (not for the hands chart) at d100 ≤ chance + 1; "explode weapon" (explosive, fire and electrical charts) at
    /// d100 ≤ chance + 1; for a weapon with an ammunition type, all its ammunition at d100 ≤ chance + 1, else five shots' worth at
    /// a second d100 ≤ chance + 11.
    /// </summary>
    [OriginalFunction(0x4B6410, OriginalFunctionScope.Partial)]
    public CriticalMissEffects RollMiss(CriticalMissContext context, ICombatRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rolls);
        var chance = context.Chance;
        var effects = CriticalMissEffects.None;
        bool D100(CombatRollPurpose purpose, int threshold) => rolls.Roll(purpose, 1, 100) <= threshold;
        if (context.Chart != CriticalMissChart.Hands && D100(CombatRollPurpose.CriticalDestroyWeapon, chance + 1))
            effects |= CriticalMissEffects.DestroyWeapon;
        if (context.Chart is CriticalMissChart.Explosive or CriticalMissChart.Fire or CriticalMissChart.Electrical
            && D100(CombatRollPurpose.CriticalExplodeWeapon, chance + 1))
            effects |= CriticalMissEffects.ExplodeWeapon;
        if (context.Weapon.Kind == WeaponKind.Weapon && context.Weapon.UsesAmmunition)
        {
            if (D100(CombatRollPurpose.CriticalDestroyAmmo, chance + 1)) effects |= CriticalMissEffects.DestroyAmmo;
            else if (D100(CombatRollPurpose.CriticalLoseAmmo, chance + 11)) effects |= CriticalMissEffects.LoseAmmo;
        }
        return effects;
    }

    /// <summary>The worn item a hit location's armour wear strikes (<see cref="CombatWearRules.ArmorSlot"/>).</summary>
    public static CombatWornItems ArmorAt(HitLocation location) => location switch
    {
        HitLocation.Head => CombatWornItems.Helmet,
        HitLocation.Arm => CombatWornItems.Gauntlets,
        HitLocation.Leg => CombatWornItems.Boots,
        HitLocation.Torso => CombatWornItems.BodyArmor,
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };
}
