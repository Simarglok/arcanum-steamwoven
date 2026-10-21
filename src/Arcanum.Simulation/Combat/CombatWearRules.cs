using Arcanum.Simulation.Equipment;

namespace Arcanum.Simulation.Combat;

/// <summary>What the attacker's side of an attack wears (<see cref="AttackWear.AttackerWear"/>).</summary>
public enum AttackerWearKind
{
    None,
    /// <summary>The attacker's wielded weapon item loses <see cref="AttackWear.AttackerWearAmount"/> hit points.</summary>
    Weapon,
    /// <summary>A bare-handed attacker loses hit points itself, never its last one (<see cref="CombatWearRules.HandsWear"/>).</summary>
    Hands,
}

/// <summary>
/// The wear one resolved attack causes (ARC-COMBAT-007; the item steps of the original's damage step 0x4B4390, ledger "Combat
/// wear"). <see cref="Wearer"/> is who took the damage: the target, or the attacker of a critical self-hit. A critter wearer's
/// armour in <see cref="ArmorSlot"/> (the hit location's slot) takes <see cref="ArmorDamage"/> and its wielded weapon the critical
/// weapon effects. The attacker's own wear is <see cref="AttackerWear"/>; on an object that is no critter it applies only while
/// the hit left the object whole (<see cref="AttackerWearNeedsWholeTarget"/>). Applying it to items and to the attacker's hit
/// points is the session's (<c>GameSessionOwner.ApplyAttackWear</c>).
/// </summary>
/// <param name="WeaponDamage">The critical "damage weapon" amount on the wearer's wielded weapon (0 or
/// <see cref="CombatWearRules.CriticalWeaponDamage"/>); an invulnerable weapon is passed by.</param>
/// <param name="DestroyWeapon">The critical "destroy weapon" or "explode weapon": the wearer's wielded weapon is deleted without a
/// break, after <paramref name="WeaponDamage"/>; an invulnerable weapon is passed by.</param>
/// <param name="ExplodeWeapon">The "explode weapon" critical: the deletion above plus a visual effect on the wearer (the host's).</param>
public sealed record AttackWear(
    DamageRecipient Wearer,
    bool WearerIsCritter,
    EquipmentSlot ArmorSlot,
    int ArmorDamage,
    int WeaponDamage,
    bool DestroyWeapon,
    bool ExplodeWeapon,
    AttackerWearKind AttackerWear,
    int AttackerWearAmount,
    bool AttackerWearNeedsWholeTarget)
{
    /// <summary>Whether any step has something to do.</summary>
    public bool Any => ArmorDamage > 0 || WeaponDamage > 0 || DestroyWeapon || AttackerWear != AttackerWearKind.None;
}

/// <summary>
/// ARC-COMBAT-007: armour and weapon wear in combat as the retail 1.0.7.4 executable applies it (contract
/// docs/world/attack-resolver-runtime.md, "Wear"; ledger "Combat wear"). Pure functions; the item and life changes are the
/// session's.
/// </summary>
public static class CombatWearRules
{
    /// <summary>What the armour at the hit location loses on fire damage, a damage-armour weapon or the "damage armour"
    /// critical (once, however many of them come together).</summary>
    public const int ArmorWear = 10;

    /// <summary>What the wearer's wielded weapon loses on the "damage weapon" critical.</summary>
    public const int CriticalWeaponDamage = 10;

    /// <summary>The highest art subtype (bits 6..9 of an item art id).</summary>
    public const int MaxArtSubtype = 15;

    /// <summary>
    /// The armour slot a hit location strikes (0x4B54B0): the head the helmet (1000), an arm the gauntlets (1007), a leg the
    /// boots (1008), the torso the body armour (1006).
    /// </summary>
    [OriginalFunction(0x4B54B0, OriginalFunctionScope.Full)]
    public static EquipmentSlot ArmorSlot(HitLocation location) => location switch
    {
        HitLocation.Head => EquipmentSlot.Helmet,
        HitLocation.Arm => EquipmentSlot.Gauntlets,
        HitLocation.Leg => EquipmentSlot.Boots,
        HitLocation.Torso => EquipmentSlot.Armor,
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };

    /// <summary>
    /// The wear of the attacker's side of a melee hit by the struck critter's or object's material (0x4B5580): stone 4, brick 3,
    /// wood 2, plant 1, metal 5, fire 5, glass 5 for bare hands only, every other material 0. A weapon's wear then falls with its
    /// magick/tech complexity, <c>wear − trunc(wear × min(|complexity|, 100) / 100)</c> (none at all at 100 either way), by 2 for
    /// art subtype 4 and by 1 for subtypes 5 and 13; 0 or less is none. Bare hands keep the material's value, which
    /// <see cref="HandsWear"/> caps by the attacker's hit points. No roll. Which hits wear at all is <see cref="Of"/>'s.
    /// </summary>
    [OriginalFunction(0x4B5580, OriginalFunctionScope.Partial)]
    public static int WeaponWear(ObjectMaterial material, CombatWeapon weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (!Enum.IsDefined(material)) throw new ArgumentOutOfRangeException(nameof(material));
        if (weapon.Kind is not (WeaponKind.Unarmed or WeaponKind.Weapon)) throw new ArgumentException("Only a weapon or bare hands wear.", nameof(weapon));
        var unarmed = weapon.Kind == WeaponKind.Unarmed;
        var wear = material switch
        {
            ObjectMaterial.Stone => 4,
            ObjectMaterial.Brick => 3,
            ObjectMaterial.Wood => 2,
            ObjectMaterial.Plant => 1,
            ObjectMaterial.Metal or ObjectMaterial.Fire => 5,
            ObjectMaterial.Glass => unarmed ? 5 : 0,
            _ => 0,
        };
        if (unarmed) return wear;
        if (weapon.ArtSubtype is < 0 or > MaxArtSubtype) throw new ArgumentOutOfRangeException(nameof(weapon), "ArtSubtype");
        if (Math.Abs((long)weapon.MagickTechComplexity) > AttackResolver.MaxMagnitude)
            throw new ArgumentOutOfRangeException(nameof(weapon), "MagickTechComplexity");
        wear -= wear * Math.Min(Math.Abs(weapon.MagickTechComplexity), 100) / 100;
        wear -= weapon.ArtSubtype switch { 4 => 2, 5 or 13 => 1, _ => 0 };
        return Math.Max(0, wear);
    }

    /// <summary>
    /// The hit points a bare-handed attacker loses (the unarmed branch of 0x4B5580): the material's wear, but at most
    /// <c>current − 1</c>, so it never takes the last hit point; none when that is 0 or less.
    /// </summary>
    public static int HandsWear(int wear, int currentHitPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wear);
        return Math.Max(0, wear >= currentHitPoints ? currentHitPoints - 1 : wear);
    }

    /// <summary>
    /// The hand wear on a life state, through the hit-point damage setter as the original writes it: <see cref="HandsWear"/> of
    /// the current hit points is added to the hit-point damage. No stoned, invulnerable or death test: the amount never reaches the
    /// last hit point.
    /// </summary>
    public static LifeOutcome WearHands(LifeState state, LifeFacts facts, int wear)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var amount = HandsWear(wear, LifeRules.Evaluate(state, facts).HitPoints);
        if (amount == 0) return new(state, state, LifeEvents.None, 0, DeathAnimation.None);
        var after = state with { HitPointDamage = (int)Math.Clamp((long)state.HitPointDamage + amount, 0, LifeLimits.MaxDamage) };
        return new(state, after, LifeEvents.None, amount, DeathAnimation.None);
    }

    /// <summary>
    /// The wear of a resolved attack's damage packet (see <see cref="AttackWear"/>), or null when the damage step returns before any
    /// wear (invulnerability, nigh-invulnerability or a wall prevented the damage).
    /// <list type="bullet">
    /// <item>Armour: fire damage after resistance, a weapon with the damage-armour flag or the "damage armour" critical wear the
    /// wearer's armour at the hit location by <see cref="ArmorWear"/>, once; a critter wearer only.</item>
    /// <item>Critical weapon effects on the wearer's wielded weapon: "damage weapon" <see cref="CriticalWeaponDamage"/>, then
    /// "destroy weapon" or "explode weapon" deletes it; a critter wearer only.</item>
    /// <item>The attacker's side: a melee attack (with a weapon or bare-handed; never a throw, bow or firearm) that dealt damage
    /// (hit points above 0, or for a critter fatigue above 0) wears by the struck one's material (<see cref="WeaponWear"/>); on an
    /// object only while it stays whole.</item>
    /// </list>
    /// </summary>
    public static AttackWear? Of(AttackRequest request, AttackDamage damage, HitLocation location, ObjectMaterial recipientMaterial,
        bool recipientIsCritter)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(damage);
        if (damage.Prevented != DamagePrevention.None) return null;
        var armor = recipientIsCritter && (damage.DamagesArmor || damage.HitEffects.HasFlag(CriticalHitEffects.DamageArmor)) ? ArmorWear : 0;
        var weaponDamage = recipientIsCritter && damage.HitEffects.HasFlag(CriticalHitEffects.DamageWeapon) ? CriticalWeaponDamage : 0;
        var explode = recipientIsCritter && damage.MissEffects.HasFlag(CriticalMissEffects.ExplodeWeapon);
        var destroy = explode || (recipientIsCritter && damage.MissEffects.HasFlag(CriticalMissEffects.DestroyWeapon));

        var kind = AttackerWearKind.None;
        var amount = 0;
        var dealt = damage.HitPointDamage > 0 || (recipientIsCritter && damage.FatigueDamage > 0);
        if (request.Skill == CombatSkill.Melee && request.Weapon.Kind != WeaponKind.ThrownItem && dealt)
        {
            amount = WeaponWear(recipientMaterial, request.Weapon);
            if (amount > 0) kind = request.Weapon.Kind == WeaponKind.Unarmed ? AttackerWearKind.Hands : AttackerWearKind.Weapon;
        }
        return new AttackWear(damage.Recipient, recipientIsCritter, ArmorSlot(location), armor, weaponDamage, destroy, explode, kind, amount,
            AttackerWearNeedsWholeTarget: kind != AttackerWearKind.None && !recipientIsCritter);
    }
}
