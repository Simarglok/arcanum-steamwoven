using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Equipment;

/// <summary>Bounds of an <see cref="EquipmentItem"/>'s values (<see cref="EquipmentRules.IsValid"/>).</summary>
public static class EquipmentLimits
{
    /// <summary>Every stored number of an item lies within ±this.</summary>
    public const int MaxMagnitude = 1000;
    /// <summary>Speed factor and magic speed adjustment each lie within ±this, so a weapon speed stays a valid stat.</summary>
    public const int MaxSpeed = 495;
    /// <summary>An item's condition (hit points) lies within 0..this; indestructible items store 10 000.</summary>
    public const int MaxHitPoints = 1_000_000;
    public const int MinAptitude = -100, MaxAptitude = 100;
}

/// <summary>
/// ARC-WORLD-028: which item may take which wear slot, and what the worn items give a wearer in combat. Pure functions over
/// stored item values (<see cref="EquipmentItem"/>) and the wearer's facts. Each rule is the reading of pinned CE
/// <c>a7ff41b300ef712f0e7d088183a3d08957110cdd</c> (<c>item.c</c>, <c>object.c</c>) checked against the retail 1.0.7.4
/// executable at the same functions (verification ledger, "Equipment (ARC-WORLD-028)"); no code or structure was copied.
/// Art existence (whether the wearer's art has the weapon, shield or armour) is not a rule here: the caller answers it after
/// <see cref="CheckWear"/> when <see cref="NeedsArtCheck"/> says so.
/// </summary>
public static class EquipmentRules
{
    /// <summary>The slot an item belongs in (the source's <c>item_location_get</c>): a weapon the weapon slot, armour the slot of its
    /// coverage (a ring <see cref="EquipmentSlot.Ring1"/>), a generic item that uses the shield location the shield slot; else null.</summary>
    public static EquipmentSlot? WearSlot(EquipmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            EquipmentItemKind.Weapon => EquipmentSlot.Weapon,
            EquipmentItemKind.Armor => item.Armor?.Coverage switch
            {
                ArmorCoverage.Helmet => EquipmentSlot.Helmet,
                ArmorCoverage.Ring => EquipmentSlot.Ring1,
                ArmorCoverage.Medallion => EquipmentSlot.Medallion,
                ArmorCoverage.Shield => EquipmentSlot.Shield,
                ArmorCoverage.Torso => EquipmentSlot.Armor,
                ArmorCoverage.Gauntlets => EquipmentSlot.Gauntlets,
                ArmorCoverage.Boots => EquipmentSlot.Boots,
                _ => null,
            },
            EquipmentItemKind.Generic when item.UsesShieldLocation => EquipmentSlot.Shield,
            _ => null,
        };
    }

    /// <summary>The armour size a race wears (the source's race table: dwarves, gnomes and halflings small, half-ogres and ogres
    /// large, everyone else medium).</summary>
    public static ArmorSizes ArmorSize(CharacterRace race) => race switch
    {
        CharacterRace.Dwarf or CharacterRace.Gnome or CharacterRace.Halfling => ArmorSizes.Small,
        CharacterRace.HalfOgre or CharacterRace.Ogre => ArmorSizes.Large,
        CharacterRace.Human or CharacterRace.Elf or CharacterRace.HalfElf or CharacterRace.HalfOrc or CharacterRace.DarkElf
            or CharacterRace.Orc => ArmorSizes.Medium,
        _ => throw new ArgumentOutOfRangeException(nameof(race)),
    };

    /// <summary>
    /// The source's wear check (<c>sub_464D20</c>) without its art lookups, in its order: a transformed wearer, a magickal item
    /// against Magick Allergy, a broken item, the wrong slot (either ring slot takes a ring); then by slot. Weapon: a two-handed
    /// weapon with a fixed hand count is refused next to a shield and with any crippled arm, any other weapon only with both arms
    /// crippled. Shield: refused next to such a two-handed weapon and with any crippled arm (the original tests both arm flags;
    /// CE reads only "both", a reconstruction slip). Body armour: the race's size, male-only before female-only, unique NPC art.
    /// Minimum strength is not checked. <paramref name="worn"/> is what the wearer wears now.
    /// </summary>
    public static EquipmentRefusal CheckWear(EquipmentItem item, EquipmentSlot slot, EquipmentWearer wearer,
        IReadOnlyDictionary<EquipmentSlot, EquipmentItem> worn)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(wearer);
        ArgumentNullException.ThrowIfNull(worn);
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        if (wearer.Transformed) return EquipmentRefusal.NotWieldable;
        var location = slot == EquipmentSlot.Ring2 ? EquipmentSlot.Ring1 : slot;
        if (item.MagickTechComplexity > 0 && wearer.MagickAllergy) return EquipmentRefusal.WieldMagicItems;
        if (item.Broken) return EquipmentRefusal.Broken;
        if (WearSlot(item) != location) return EquipmentRefusal.WrongType;
        var anyArm = wearer.CrippledArmOne || wearer.CrippledArmsBoth;
        switch (location)
        {
            case EquipmentSlot.Weapon:
                if (FixedTwoHanded(item))
                {
                    if (worn.ContainsKey(EquipmentSlot.Shield)) return EquipmentRefusal.NoFreeHand;
                    if (anyArm) return EquipmentRefusal.Crippled;
                }
                else if (wearer.CrippledArmsBoth) return EquipmentRefusal.Crippled;
                break;
            case EquipmentSlot.Shield:
                if (worn.TryGetValue(EquipmentSlot.Weapon, out var weapon) && FixedTwoHanded(weapon)) return EquipmentRefusal.NoFreeHand;
                if (anyArm) return EquipmentRefusal.Crippled;
                break;
            case EquipmentSlot.Armor:
                var armor = item.Armor!;
                if ((armor.Sizes & ArmorSize(wearer.Race)) == 0) return EquipmentRefusal.WrongSize;
                if (armor.MaleOnly ? wearer.Gender != CharacterGender.Male : armor.FemaleOnly && wearer.Gender != CharacterGender.Female)
                    return EquipmentRefusal.WrongGender;
                if (wearer.UniqueNpcArt) return EquipmentRefusal.NotWieldable;
                break;
        }
        return EquipmentRefusal.None;
    }

    /// <summary>Whether the source's wear check also looks up the wearer's art for the item, after every rule of
    /// <see cref="CheckWear"/>: always for the weapon and body-armour slots, for the shield slot only for an armour shield (a torch
    /// is not drawn on the body). A missing art refuses the item as <see cref="EquipmentRefusal.NotWieldable"/>.</summary>
    public static bool NeedsArtCheck(EquipmentItem item, EquipmentSlot slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        return slot switch
        {
            EquipmentSlot.Weapon or EquipmentSlot.Armor => true,
            EquipmentSlot.Shield => item.Kind == EquipmentItemKind.Armor,
            _ => false,
        };
    }

    /// <summary>Whether the item may leave its place (the source's <c>item_check_remove</c>): never with the no-drop flag. Taking a
    /// worn item off is otherwise always allowed: the original's hexed-item check is a stub that always allows it.</summary>
    public static EquipmentRefusal CheckRemove(EquipmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.NoDrop ? EquipmentRefusal.NotDroppable : EquipmentRefusal.None;
    }

    /// <summary>
    /// The power a critter draws from an item (the source's <c>item_effective_power</c>): <c>(complexity + aptitude) / 2</c>,
    /// truncated toward zero, clamped to 0 .. complexity on the item's side (a magickal item gives a technologist nothing, a
    /// technological item a mage nothing).
    /// </summary>
    public static int EffectivePower(int complexity, int aptitude)
    {
        CheckComplexity(complexity);
        CheckAptitude(aptitude);
        var power = (complexity + aptitude) / 2;
        return complexity < 0 ? (power > 0 ? 0 : Math.Max(power, complexity)) : (power < 0 ? 0 : Math.Min(power, complexity));
    }

    /// <summary>The effective power in percent of the complexity, 100 for a neutral item (the light tiers read it).</summary>
    public static int EffectivePowerRatio(int complexity, int aptitude) =>
        complexity == 0 ? 100 : 100 * EffectivePower(complexity, aptitude) / complexity;

    /// <summary>
    /// A magic adjustment as the wearer gets it (the source's <c>item_adjust_magic</c>): a magickal item gives
    /// <c>value × power / complexity</c>, a technological one <c>value − value × power / complexity</c> (nothing to a perfect
    /// technologist), a neutral one the whole value; integer division truncates toward zero.
    /// </summary>
    public static int AdjustMagic(int value, int complexity, int aptitude)
    {
        if (value is < -EquipmentLimits.MaxMagnitude or > EquipmentLimits.MaxMagnitude) throw new ArgumentOutOfRangeException(nameof(value));
        var power = EffectivePower(complexity, aptitude);
        return complexity > 0 ? value * power / complexity : complexity < 0 ? value - value * power / complexity : value;
    }

    /// <summary>The worn items' armour-class adjustment (the worn part of the source's <c>object_get_ac</c>): every worn armour item's
    /// AC adjustment plus its magic AC adjustment for this wearer. Base AC, effects and the AcAdjustment stat are the caller's
    /// (<see cref="AttackRules.ArmorClass"/>).</summary>
    public static int ArmorClassAdjustment(IEnumerable<EquipmentItem> worn, int aptitude)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var total = 0;
        foreach (var item in worn)
            if (item.Armor is { } armor) total += armor.AcAdjustment + AdjustMagic(armor.MagicAcAdjustment, item.MagickTechComplexity, aptitude);
        return total;
    }

    /// <summary>The worn items' resistance adjustments (the worn part of the source's <c>object_get_resistance</c>): every worn
    /// armour item's adjustment plus its magic adjustment for this wearer, per type. The base, the poison bonus of
    /// constitution, effects and the 0..95 clamp are the caller's.</summary>
    public static EquipmentResistances ResistanceAdjustments(IEnumerable<EquipmentItem> worn, int aptitude)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var total = EquipmentResistances.None;
        foreach (var item in worn)
        {
            if (item.Armor is not { } armor) continue;
            int Magic(int value) => AdjustMagic(value, item.MagickTechComplexity, aptitude);
            var m = armor.MagicResistances;
            total = total.Add(armor.Resistances).Add(new(Magic(m.Normal), Magic(m.Fire), Magic(m.Electrical), Magic(m.Poison), Magic(m.Magic)));
        }
        return total;
    }

    /// <summary>
    /// The light a wearer gives off from its worn items (the source's <c>item_recalc_light</c>): each worn item's light flags shrink
    /// by one size at an effective power ratio of 75 % or less, two at 50 % or less, three at 25 % or less (a small light then goes
    /// out); the largest remaining size wins.
    /// </summary>
    public static LightEmission Light(IEnumerable<EquipmentItem> worn, int aptitude)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var flags = 0;
        foreach (var item in worn)
        {
            var light = (int)item.Light;
            if (light == 0) continue;
            var ratio = EffectivePowerRatio(item.MagickTechComplexity, aptitude);
            var shift = ratio <= 25 ? 3 : ratio <= 50 ? 2 : ratio <= 75 ? 1 : 0;
            flags |= (light >> shift) & 0xF;
        }
        return (flags & 8) != 0 ? LightEmission.ExtraLarge : (flags & 4) != 0 ? LightEmission.Large
            : (flags & 2) != 0 ? LightEmission.Medium : (flags & 1) != 0 ? LightEmission.Small : LightEmission.None;
    }

    /// <summary>
    /// A weapon's minimum strength for this wielder (the source's <c>item_weapon_min_strength</c>): the stored value plus its magic
    /// adjustment; unless the hand count is fixed, a two-handed weapon needs 4 more next to a shield and a one-handed weapon 2 less
    /// without one.
    /// </summary>
    public static int MinimumStrength(EquipmentItem weapon, int aptitude, bool shieldWorn)
    {
        var facts = WeaponFacts(weapon);
        var strength = facts.MinimumStrength + AdjustMagic(facts.MagicMinimumStrength, weapon.MagickTechComplexity, aptitude);
        if (facts.HandCountFixed) return strength;
        return facts.TwoHanded ? (shieldWorn ? strength + 4 : strength) : (shieldWorn ? strength : strength - 2);
    }

    /// <summary>The weapon speed of a wielded weapon (the source's <c>item_weapon_magic_speed</c> with an owner): speed factor plus
    /// the magic speed adjustment for this wielder, +5 with at least apprentice training in its skill; 10 bare-handed.</summary>
    public static int WeaponSpeed(EquipmentItem? weapon, int aptitude, SkillTraining training)
    {
        if (weapon is null)
        {
            if (!Enum.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(training));
            return TurnBasedRules.UnarmedWeaponSpeed;
        }
        var facts = WeaponFacts(weapon);
        return TurnBasedRules.WeaponSpeed(facts.SpeedFactor, AdjustMagic(facts.MagicSpeed, weapon.MagickTechComplexity, aptitude), training);
    }

    /// <summary>
    /// What a wearer strikes with: bare hands with the worn gauntlets' unarmed bonus (read for melee only, as the source does), or
    /// the wielded weapon with its stored damage, to-hit and charts and its magic adjustments, range and minimum strength for this
    /// wielder (<see cref="MinimumStrength"/>). A thrown attack's range is the throwing distance, which the caller sets; a
    /// monstrous critter attacks with its own table (<see cref="CombatWeapon.Natural"/>).
    /// </summary>
    public static CombatWeapon Weapon(IReadOnlyDictionary<EquipmentSlot, EquipmentItem> worn, int aptitude)
    {
        ArgumentNullException.ThrowIfNull(worn);
        if (!worn.TryGetValue(EquipmentSlot.Weapon, out var item))
            return CombatWeapon.Unarmed(worn.TryGetValue(EquipmentSlot.Gauntlets, out var gauntlets) ? gauntlets.Armor?.UnarmedBonusDamage ?? 0 : 0);
        var w = WeaponFacts(item);
        int Magic(int value) => AdjustMagic(value, item.MagickTechComplexity, aptitude);
        var magicDamage = w.MagicDamage;
        return new CombatWeapon
        {
            Kind = WeaponKind.Weapon,
            Damage = w.Damage,
            Range = w.Range + Magic(w.MagicRange),
            ToHitBonus = w.ToHit,
            MinimumStrength = MinimumStrength(item, aptitude, worn.ContainsKey(EquipmentSlot.Shield)),
            MagickTechComplexity = item.MagickTechComplexity,
            MagicToHitAdjustment = Magic(w.MagicToHit),
            MagicCriticalHitChance = Magic(w.MagicCriticalHitChance),
            MagicCriticalMissChance = Magic(w.MagicCriticalMissChance),
            MagicCriticalEffectChance = Magic(w.MagicCriticalHitEffect),
            MagicCriticalMissEffectChance = Magic(w.MagicCriticalMissEffect),
            CriticalHitChart = w.CriticalHitChart,
            CriticalMissChart = w.CriticalMissChart,
            MagicDamageAdjustment = new(Magic(magicDamage.Normal), Magic(magicDamage.Poison), Magic(magicDamage.Electrical),
                Magic(magicDamage.Fire), Magic(magicDamage.Fatigue)),
            Hp = item.HitPoints,
            MaxHp = item.MaxHitPoints,
            IgnoresResistance = w.IgnoresResistance,
            DamagesArmor = w.DamagesArmor,
            Uber = item.Uber,
        };
    }

    /// <summary>The worn items' part of a wearer's combat values; <paramref name="weaponTraining"/> is the wearer's training in the
    /// wielded weapon's skill (melee bare-handed), which only the weapon speed reads.</summary>
    public static EquipmentCombat Evaluate(IReadOnlyDictionary<EquipmentSlot, EquipmentItem> worn, int aptitude, SkillTraining weaponTraining)
    {
        ArgumentNullException.ThrowIfNull(worn);
        CheckAptitude(aptitude);
        foreach (var (slot, item) in worn)
            if (!Enum.IsDefined(slot) || !IsValid(item)) throw new ArgumentException("invalid worn item", nameof(worn));
        worn.TryGetValue(EquipmentSlot.Weapon, out var wielded);
        var speed = WeaponSpeed(wielded, aptitude, weaponTraining);
        return new(ArmorClassAdjustment(worn.Values, aptitude), ResistanceAdjustments(worn.Values, aptitude), Light(worn.Values, aptitude),
            Weapon(worn, aptitude), wielded?.Weapon?.Skill ?? CombatSkill.Melee, speed, TurnBasedRules.AttackCost(speed));
    }

    /// <summary>
    /// Whether an item is well formed: a defined kind with exactly its block (weapon block for a weapon, armour block for armour,
    /// neither for a generic item, the shield-location flag only on a generic item), defined enumerations, light flags of the four
    /// sizes, every number within <see cref="EquipmentLimits.MaxMagnitude"/> (speeds within <see cref="EquipmentLimits.MaxSpeed"/>),
    /// a condition 0 ≤ hit points ≤ maximum ≤ <see cref="EquipmentLimits.MaxHitPoints"/> and a non-negative ammunition type and consumption.
    /// </summary>
    public static bool IsValid(EquipmentItem? item)
    {
        if (item is null || !Enum.IsDefined(item.Kind) || !Small(item.MagickTechComplexity) || ((int)item.Light & ~0xF) != 0) return false;
        if (item.HitPoints < 0 || item.HitPoints > item.MaxHitPoints || item.MaxHitPoints > EquipmentLimits.MaxHitPoints) return false;
        if (item.UsesShieldLocation && item.Kind != EquipmentItemKind.Generic) return false;
        if ((item.Weapon is not null) != (item.Kind == EquipmentItemKind.Weapon) || (item.Armor is not null) != (item.Kind == EquipmentItemKind.Armor))
            return false;
        if (item.Weapon is { } w)
        {
            if (!Enum.IsDefined(w.Skill) || !Enum.IsDefined(w.CriticalHitChart) || !Enum.IsDefined(w.CriticalMissChart)) return false;
            if (!Ranges(w.Damage) || !Amounts(w.MagicDamage)) return false;
            if (w.SpeedFactor is < -EquipmentLimits.MaxSpeed or > EquipmentLimits.MaxSpeed ||
                w.MagicSpeed is < -EquipmentLimits.MaxSpeed or > EquipmentLimits.MaxSpeed) return false;
            if (!All(w.ToHit, w.MagicToHit, w.Range, w.MagicRange, w.MinimumStrength, w.MagicMinimumStrength, w.AmmoConsumption,
                    w.MagicCriticalHitChance, w.MagicCriticalHitEffect, w.MagicCriticalMissChance, w.MagicCriticalMissEffect)) return false;
            if (w.AmmoType is < 0 or > EquipmentLimits.MaxMagnitude || w.AmmoConsumption < 0) return false;
        }
        if (item.Armor is { } a)
        {
            if (!Enum.IsDefined(a.Coverage) || ((int)a.Sizes & ~7) != 0) return false;
            if (!All(a.AcAdjustment, a.MagicAcAdjustment, a.UnarmedBonusDamage) || !Resistances(a.Resistances) || !Resistances(a.MagicResistances))
                return false;
        }
        return true;

        static bool Small(int value) => value is >= -EquipmentLimits.MaxMagnitude and <= EquipmentLimits.MaxMagnitude;
        static bool All(params int[] values) => values.All(Small);
        static bool Ranges(DamageRanges d) => All(d.Normal.Min, d.Normal.Max, d.Poison.Min, d.Poison.Max, d.Electrical.Min, d.Electrical.Max,
            d.Fire.Min, d.Fire.Max, d.Fatigue.Min, d.Fatigue.Max);
        static bool Amounts(DamageByType d) => All(d.Normal, d.Poison, d.Electrical, d.Fire, d.Fatigue);
        static bool Resistances(EquipmentResistances r) => All(r.Normal, r.Fire, r.Electrical, r.Poison, r.Magic);
    }

    private static bool FixedTwoHanded(EquipmentItem item) => item.Weapon is { TwoHanded: true, HandCountFixed: true };

    private static EquipmentWeapon WeaponFacts(EquipmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Weapon ?? throw new ArgumentException("not a weapon", nameof(item));
    }

    private static void CheckComplexity(int complexity)
    {
        if (complexity is < -EquipmentLimits.MaxMagnitude or > EquipmentLimits.MaxMagnitude) throw new ArgumentOutOfRangeException(nameof(complexity));
    }

    private static void CheckAptitude(int aptitude)
    {
        if (aptitude is < EquipmentLimits.MinAptitude or > EquipmentLimits.MaxAptitude) throw new ArgumentOutOfRangeException(nameof(aptitude));
    }
}
