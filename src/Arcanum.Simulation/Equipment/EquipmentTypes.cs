using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Equipment;

/// <summary>The nine wear slots of a critter, numbered as the source's inventory locations 1000..1008 (ledger EQ6).</summary>
public enum EquipmentSlot
{
    Helmet = 1000, Ring1 = 1001, Ring2 = 1002, Medallion = 1003, Weapon = 1004, Shield = 1005, Armor = 1006, Gauntlets = 1007,
    Boots = 1008,
}

/// <summary>The item types that can take a wear slot: weapons, armour (which includes shields, helmets, gauntlets, boots,
/// rings and medallions) and generic items that use the torch/shield location.</summary>
public enum EquipmentItemKind { Weapon, Armor, Generic }

/// <summary>An armour item's coverage, in the order its inventory art stores it.</summary>
public enum ArmorCoverage { Torso, Shield, Helmet, Gauntlets, Boots, Ring, Medallion }

/// <summary>The light sizes an item's flags give (the source's <c>OIF_LIGHT_*</c> bits in order); several may be set.</summary>
[Flags]
public enum EquipmentLight { None = 0, Small = 1, Medium = 2, Large = 4, ExtraLarge = 8 }

/// <summary>The armour sizes an item fits (<c>OARF_SIZE_*</c>); a wearer's race has exactly one size.</summary>
[Flags]
public enum ArmorSizes { None = 0, Small = 1, Medium = 2, Large = 4 }

/// <summary>Stored values per resistance, in the source's resistance order (normal, fire, electrical, poison, magic).</summary>
public readonly record struct EquipmentResistances(int Normal, int Fire, int Electrical, int Poison, int Magic)
{
    public static readonly EquipmentResistances None = new(0, 0, 0, 0, 0);

    public EquipmentResistances Add(EquipmentResistances other) => new(Normal + other.Normal, Fire + other.Fire,
        Electrical + other.Electrical, Poison + other.Poison, Magic + other.Magic);
}

/// <summary>
/// The stored weapon fields the wear and combat rules read (ARC-DATA-021 <c>CombatWeaponFields</c>), before any wielder scaling:
/// every <c>Magic…</c> value is the item's magic adjustment, which <see cref="EquipmentRules.AdjustMagic"/> scales by the
/// wielder's aptitude. <see cref="AmmoType"/> is null for a weapon that uses no ammunition.
/// </summary>
public sealed record EquipmentWeapon
{
    public bool TwoHanded { get; init; }
    /// <summary>The hand count is fixed: a two-handed weapon with this flag cannot share the hands with a shield, and its minimum
    /// strength ignores the shield rule.</summary>
    public bool HandCountFixed { get; init; }
    public bool IgnoresResistance { get; init; }
    public bool DamagesArmor { get; init; }
    public CombatSkill Skill { get; init; }
    public DamageRanges Damage { get; init; } = DamageRanges.None;
    public DamageByType MagicDamage { get; init; } = DamageByType.Zero;
    public int ToHit { get; init; }
    public int MagicToHit { get; init; }
    public int SpeedFactor { get; init; }
    public int MagicSpeed { get; init; }
    public int Range { get; init; } = 1;
    public int MagicRange { get; init; }
    public int MinimumStrength { get; init; }
    public int MagicMinimumStrength { get; init; }
    public int? AmmoType { get; init; }
    public int AmmoConsumption { get; init; }
    public CriticalHitChart CriticalHitChart { get; init; } = CriticalHitChart.Crushing;
    public CriticalMissChart CriticalMissChart { get; init; } = CriticalMissChart.Bludgeon;
    public int MagicCriticalHitChance { get; init; }
    public int MagicCriticalHitEffect { get; init; }
    public int MagicCriticalMissChance { get; init; }
    public int MagicCriticalMissEffect { get; init; }
}

/// <summary>
/// The stored armour fields the wear and combat rules read (ARC-DATA-021 <c>CombatArmorFields</c> and the coverage of the
/// item's inventory art). <see cref="MaleOnly"/> wins over <see cref="FemaleOnly"/> when both are set, as in the source.
/// </summary>
public sealed record EquipmentArmor
{
    public ArmorCoverage Coverage { get; init; }
    public ArmorSizes Sizes { get; init; }
    public bool MaleOnly { get; init; }
    public bool FemaleOnly { get; init; }
    public int AcAdjustment { get; init; }
    public int MagicAcAdjustment { get; init; }
    /// <summary>The armour's silent-move adjustment (the noise penalty prowling's hearing check reads, ARC-AI-1518) and its magic adjustment.</summary>
    public int SilentMoveAdjustment { get; init; }
    public int MagicSilentMoveAdjustment { get; init; }
    public EquipmentResistances Resistances { get; init; } = EquipmentResistances.None;
    public EquipmentResistances MagicResistances { get; init; } = EquipmentResistances.None;
    /// <summary>The unarmed bonus damage a gauntlet adds to bare-hand melee attacks.</summary>
    public int UnarmedBonusDamage { get; init; }
}

/// <summary>
/// One item as the wear and combat rules see it: its kind with the weapon block (a weapon) or the armour block (armour), the
/// torch/shield flag of a generic item, the magick/tech complexity (positive magickal, negative technological), the no-drop,
/// broken and uber flags, its light flags and its condition (<see cref="HitPoints"/> of <see cref="MaxHitPoints"/>, 0/0 for none).
/// </summary>
public sealed record EquipmentItem
{
    public required EquipmentItemKind Kind { get; init; }
    public EquipmentWeapon? Weapon { get; init; }
    public EquipmentArmor? Armor { get; init; }
    /// <summary>A generic item that uses the shield location (a torch).</summary>
    public bool UsesShieldLocation { get; init; }
    public int MagickTechComplexity { get; init; }
    public bool NoDrop { get; init; }
    public bool Broken { get; init; }
    public bool Uber { get; init; }
    public EquipmentLight Light { get; init; }
    public int HitPoints { get; init; }
    public int MaxHitPoints { get; init; }
}

/// <summary>
/// What the wear check asks about the wearer: race (armour size), gender, the effective magick/tech aptitude (−100..100), the
/// crippled arms, a polymorph or "body of" spell (no item can be worn), the Magick Allergy background (no magickal item) and art
/// of a unique NPC (no body armour).
/// </summary>
public sealed record EquipmentWearer
{
    public CharacterRace Race { get; init; }
    public CharacterGender Gender { get; init; } = CharacterGender.Male;
    public int MagickTechAptitude { get; init; }
    public bool CrippledArmOne { get; init; }
    public bool CrippledArmsBoth { get; init; }
    public bool Transformed { get; init; }
    public bool MagickAllergy { get; init; }
    public bool UniqueNpcArt { get; init; }
}

/// <summary>Why an item cannot take a slot: the source's wear-check results, then the no-drop result of taking an item out.</summary>
public enum EquipmentRefusal
{
    None,
    /// <summary>The wearer is polymorphed or in a "body of" form, wears unique NPC art (body armour), or has no art for the item.</summary>
    NotWieldable,
    /// <summary>A magickal item and the Magick Allergy background.</summary>
    WieldMagicItems,
    Broken,
    /// <summary>The item does not belong in that slot.</summary>
    WrongType,
    /// <summary>A two-handed weapon with a fixed hand count and a shield.</summary>
    NoFreeHand,
    /// <summary>A crippled arm prevents it.</summary>
    Crippled,
    WrongSize,
    WrongGender,
    /// <summary>The item cannot leave where it is (its no-drop flag).</summary>
    NotDroppable,
}

/// <summary>
/// The worn items' part of a wearer's combat values (<see cref="EquipmentRules.Evaluate"/>), effective for this wearer: the
/// armour-class and resistance adjustments of its worn armour (the caller adds the base values, effects and stats), its light,
/// the attack it strikes with and that attack's skill, the weapon speed and the attack's action-point cost.
/// </summary>
public sealed record EquipmentCombat(int ArmorClassAdjustment, EquipmentResistances Resistances, LightEmission Light,
    CombatWeapon Weapon, CombatSkill Skill, int WeaponSpeed, int AttackCost);
