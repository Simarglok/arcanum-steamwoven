namespace Arcanum.Data.Combat;

/// <summary>Index of a per-damage-type value (weapon damage and magic damage arrays, NPC damage pairs), the source's
/// damage type order.</summary>
public enum CombatDamageIndex { Normal, Poison, Electrical, Fire, Fatigue }

/// <summary>Index of a per-resistance value (object resistances, armour adjustments), the source's resistance order,
/// which differs from the damage order.</summary>
public enum CombatResistanceIndex { Normal, Fire, Electrical, Poison, Magic }

/// <summary>Ammunition types of a weapon or an ammunition item. A weapon that uses none stores <see cref="CombatFieldReader.NoAmmo"/>.</summary>
public enum CombatAmmoType { Arrow = 0, Bullet = 1, PowerCell = 2, Fuel = 3 }

/// <summary>The weapon skill the source derives from an item's stored fields (0x465D80).</summary>
public enum CombatWeaponSkill { Melee, Bow, Throwing, Firearms }

/// <summary>Weapon flags (<c>OBJ_F_WEAPON_FLAGS</c>); undefined stored bits are kept as they are.</summary>
[Flags]
public enum CombatWeaponFlags : uint
{
    None = 0, Loud = 0x1, Silent = 0x2, TwoHanded = 0x4, HandCountFixed = 0x8, Throwable = 0x10, TransparentProjectile = 0x20,
    Boomerangs = 0x40, IgnoreResistance = 0x80, DamageArmor = 0x100, DefaultThrows = 0x200,
}

/// <summary>Armour flags (<c>OBJ_F_ARMOR_FLAGS</c>); undefined stored bits are kept as they are.</summary>
[Flags]
public enum CombatArmorFlags : uint { None = 0, SizeSmall = 0x1, SizeMedium = 0x2, SizeLarge = 0x4, MaleOnly = 0x8, FemaleOnly = 0x10 }

/// <summary>Where the values came from: a prototype file alone, or an Area object's effective fields (the instance's
/// own fields over its prototype's).</summary>
public enum CombatFieldOrigin { Prototype, AreaObject }

/// <summary>A stored inclusive damage range; nothing orders or clamps it.</summary>
public readonly record struct CombatDamageRange(int Minimum, int Maximum);

/// <summary>Fields every object carries. <see cref="Resistances"/> has five values by <see cref="CombatResistanceIndex"/>;
/// <see cref="Name"/> is the numeric name class (<c>OBJ_F_NAME</c>), not display text. <see cref="CurrentArt"/> is the stored
/// current art id (<c>OBJ_F_CURRENT_AID</c>); for an item its "destroyed" bit marks a broken item (see <see cref="CombatItemArt"/>).</summary>
public sealed record CombatObjectFields(int Type, int Prototype, uint Flags, uint SpellFlags, int Name, int Description, int ArmorClass,
    int HitPoints, int HitPointAdjustment, int HitPointDamage, int Material, IReadOnlyList<int> Resistances, uint CurrentArt)
{
    /// <summary>The attachment points (0..35) that carry a script, ascending (ARC-WORLD-034: the wear moves refuse items with move scripts).</summary>
    public IReadOnlyList<int> ScriptPoints { get; init; } = [];
}

/// <summary>Fields every item carries. <see cref="MagickTechComplexity"/> is positive for magickal, negative for
/// technological items; <see cref="Spells"/> holds the five item spells (10000 is none); <see cref="InventoryLocation"/>
/// is the inventory slot (1004 wielded weapon, 1006 worn armour). <see cref="InventoryArt"/> (<c>OBJ_F_ITEM_INV_AID</c>) carries an
/// armour item's coverage and <see cref="UseArtFragment"/> (<c>OBJ_F_ITEM_USE_AID_FRAGMENT</c>) the weapon or armour art subtype a
/// wearer's art takes (see <see cref="CombatItemArt"/>).</summary>
public sealed record CombatItemFields(uint Flags, int Weight, int Worth, int ManaStore, int MagickTechComplexity, int Discipline,
    int InventoryLocation, IReadOnlyList<int> Spells, int SpellManaStore, int AiAction, uint InventoryArt, uint UseArtFragment);

/// <summary>
/// The item art id bits the wear rules read (the bits the retail 1.0.7.4 executable decodes,
/// ARC-WORLD-028): an art id's type is bits 28..31, and only an item art id (type 6) has the item fields; anything else
/// reads 0. Coverage is bits 14..16 of the inventory art, the subtype bits 6..9 of the use-art fragment, and bit 10 of the current
/// art marks a destroyed (broken) item.
/// </summary>
public static class CombatItemArt
{
    public const uint ItemArtType = 6;

    public static bool IsItemArt(uint art) => art >> 28 == ItemArtType;

    /// <summary>Armour coverage: 0 torso, 1 shield, 2 helmet, 3 gauntlets, 4 boots, 5 ring, 6 medallion.</summary>
    public static int Coverage(uint art) => IsItemArt(art) ? (int)((art >> 14) & 7) : 0;

    public static int Subtype(uint art) => IsItemArt(art) ? (int)((art >> 6) & 15) : 0;

    public static bool Destroyed(uint art) => IsItemArt(art) && (art & 0x400) != 0;
}

/// <summary>
/// Weapon fields as stored, before any wielder scaling: the <c>Magic…</c> values are the item's magic adjustments, which the
/// source scales by the wielder's aptitude; <see cref="Damage"/> and <see cref="MagicDamageAdjustment"/> have five values
/// by <see cref="CombatDamageIndex"/>. <see cref="AmmoType"/> is a <see cref="CombatAmmoType"/> value or
/// <see cref="CombatFieldReader.NoAmmo"/>; the two charts are the stored critical hit and miss chart numbers.
/// </summary>
public sealed record CombatWeaponFields(CombatWeaponFlags Flags, CombatWeaponSkill Skill, int BonusToHit, int MagicHitAdjustment,
    IReadOnlyList<CombatDamageRange> Damage, IReadOnlyList<int> MagicDamageAdjustment, int SpeedFactor, int MagicSpeedAdjustment,
    int Range, int MagicRangeAdjustment, int MinimumStrength, int MagicMinimumStrengthAdjustment, int AmmoType, int AmmoConsumption,
    int CriticalHitChart, int MagicCriticalHitChance, int MagicCriticalHitEffect, int CriticalMissChart, int MagicCriticalMissChance,
    int MagicCriticalMissEffect);

/// <summary>Ammunition item fields: flags, stack quantity and <see cref="CombatAmmoType"/> value.</summary>
public sealed record CombatAmmoFields(uint Flags, int Quantity, int AmmoType);

/// <summary>Armour fields as stored; the two resistance lists have five values by <see cref="CombatResistanceIndex"/>.</summary>
public sealed record CombatArmorFields(CombatArmorFlags Flags, int ArmorClassAdjustment, int MagicArmorClassAdjustment,
    IReadOnlyList<int> ResistanceAdjustment, IReadOnlyList<int> MagicResistanceAdjustment, int SilentMoveAdjustment,
    int MagicSilentMoveAdjustment, int UnarmedBonusDamage);

/// <summary>Generic item fields: flags (lockpick, trap device, healing item, grenade and others), usage bonus and remaining
/// usage count. The AI finds grenades and healing items by these flags.</summary>
public sealed record CombatGenericFields(uint Flags, int UsageBonus, int UsageCountRemaining);

/// <summary>
/// Critter (PC and NPC) fields as stored: <see cref="BaseStats"/> are the 28 base stats (strength 0 .. race 27), not the
/// effective stats; <see cref="BasicSkills"/> (12) and <see cref="TechSkills"/> (4) hold points in bits 0..5 and training
/// in bits 6..7 (<see cref="CombatFieldReader.SkillPoints"/>, <see cref="CombatFieldReader.SkillTraining"/>);
/// <see cref="SpellTech"/> (25) holds the 16 college levels, the mastery college (−1 none) and the 8 tech degrees.
/// <see cref="CriticalHitChart"/> is the body type the source uses for critical hits against this critter.
/// <see cref="Effects"/> (ARC-WORLD-036) is its effect list in order, number and cause (the two parallel effect arrays); empty
/// when it has none, or for a caller that built the record without them.
/// </summary>
public sealed record CombatCritterFields(uint Flags, uint Flags2, IReadOnlyList<int> BaseStats, IReadOnlyList<int> BasicSkills,
    IReadOnlyList<int> TechSkills, IReadOnlyList<int> SpellTech, int FatiguePoints, int FatigueAdjustment, int FatigueDamage,
    int CriticalHitChart, int InventorySource, int AutoLevelScheme, IReadOnlyList<CombatEffectEntry>? Effects = null);

/// <summary>One entry of a critter's effect list: the effect number and its cause code (0 race .. 9 gender).</summary>
public readonly record struct CombatEffectEntry(int Number, int Cause);

/// <summary>NPC fields the combat and AI domains read: flags, AI parameter row, experience worth and pool, faction,
/// reaction base, social class and the monster damage table (five ranges by <see cref="CombatDamageIndex"/>).</summary>
public sealed record CombatNpcFields(uint Flags, int AiData, int ExperienceWorth, int ExperiencePool, int Faction, int ReactionBase,
    int SocialClass, IReadOnlyList<CombatDamageRange> Damage);

/// <summary>
/// The combat-relevant stored fields of one object (ARC-DATA-021): the common block always; the item block for items
/// (types 5..14) with the weapon, ammunition, armour or generic block for those types; the critter block for PCs and NPCs (15, 16)
/// with the NPC block for NPCs. Raw values only: no stat, effect, aptitude, difficulty or wielder is applied.
/// </summary>
public sealed record CombatFields(CombatFieldOrigin Origin, CombatObjectFields Object, CombatItemFields? Item, CombatWeaponFields? Weapon,
    CombatAmmoFields? Ammo, CombatArmorFields? Armor, CombatGenericFields? Generic, CombatCritterFields? Critter, CombatNpcFields? Npc)
{
    /// <summary>The written block of a written item (type 13, ARC-TECHNOLOGY-003); null for any other object.</summary>
    public CombatWrittenFields? Written { get; init; }
}

/// <summary>
/// Written item fields as stored (ARC-TECHNOLOGY-003): the written flags, the subtype (<see cref="SchematicSubtype"/> marks a
/// schematic document) and the first and last text line; for a schematic document the start line is its schematic number.
/// </summary>
public sealed record CombatWrittenFields(uint Flags, int Subtype, int TextStartLine, int TextEndLine)
{
    public const int SchematicSubtype = 5;

    public bool IsSchematic => Subtype == SchematicSubtype;
}

public enum CombatFieldFailure
{
    None,
    PrototypeMissing,
    PrototypeAmbiguous,
    /// <summary>Too large, unreadable, not a complete v119 prototype record, or its number is not the file's.</summary>
    PrototypeInvalid,
    /// <summary>Not an item (5..14) or a critter (15, 16).</summary>
    TypeUnsupported,
    /// <summary>An Area snapshot that is not strictly published.</summary>
    SnapshotNotStrict,
    /// <summary>An item or critter of an Area snapshot that comes from a sector file, not the mobile aggregate
    /// (<see cref="CombatAreaRead.Detail"/> is its type).</summary>
    StaticObjectUnsupported,
    /// <summary>A mandatory field is absent or not a plain number (<see cref="CombatFieldRead.Detail"/> names its slot).</summary>
    FieldUnresolved,
    /// <summary>An array field that is not an int32 array, has an element of another width or a key outside its range.</summary>
    ArrayMalformed,
}

/// <param name="Detail">The v119 field slot of a field or array failure; the prototype number of a prototype failure; the
/// object type of a type failure.</param>
public sealed record CombatFieldRead(CombatFieldFailure Failure, CombatFields? Fields, int? Detail = null);

/// <summary>One mobile object of an Area snapshot with its combat fields.</summary>
public sealed record CombatAreaObject(int SourceIndex, int Prototype, CombatFields Fields);

/// <param name="SourceIndex">The mobile record a failure is about.</param>
/// <param name="Detail">As <see cref="CombatFieldRead.Detail"/>.</param>
public sealed record CombatAreaRead(CombatFieldFailure Failure, IReadOnlyList<CombatAreaObject>? Objects, int? SourceIndex = null, int? Detail = null);
