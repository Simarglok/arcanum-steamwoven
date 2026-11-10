using System.Globalization;
using System.Text;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What the item window's lines (0x554F10) are made of for one item: its fields, the PC's identification of it and the facts of
/// the item's own state the session keeps (units, charges, condition), or none of them (the fields' own are then read).</summary>
/// <param name="Fields">The item's stored fields (its object row's when a map holds it, else its prototype's); an item type 5..14.</param>
/// <param name="Identified">The identification test (0x464370): complexity 0 or less, else the stored flag or the session's mark.</param>
/// <param name="Viewer">The local PC (<see cref="GameSessionOwner.QueryItemViewer"/>): its aptitude, weapon training and key ring.</param>
public sealed record AreaItemWindowInput(CombatFields Fields, bool Identified, SessionItemViewer Viewer)
{
    /// <summary>The item's effects description text (field 0x63 through 0x4D4080, <c>item_effect.mes</c>); null or empty none.</summary>
    public string? EffectsText { get; init; }
    /// <summary>The units of an ammunition stack the session keeps (else the fields' quantity).</summary>
    public int? Units { get; init; }
    /// <summary>The quantity of a gold pile (field 0xA5); null reads as 0.</summary>
    public int? GoldQuantity { get; init; }
    /// <summary>The spell charges the session left (field 0x69), else the item's own.</summary>
    public int? Charges { get; init; }
    /// <summary>The item's condition now (the session's record), else the fields' own.</summary>
    public ItemCondition? Condition { get; init; }
}

/// <summary>
/// ARC-UI-108: the lines of the item window after the name and the type icon, as the original's item window 0x554F10 builds them (checked
/// in the 1.0.7.4 executable, ledger OH12–OH18), Godot-free. By type: a weapon's speed (0x465CF0) in red on the title's line; a second line
/// (effects text when it has some and the item is not magickal or is identified, else a weapon's damage line 0x555910 or an armour's 0x555B50,
/// an ammunition's or gold's quantity, a key ring's keys, a generic item's usage bonus); for an identified weapon, armour or scroll its
/// magick or tech line; the charges, uses or the ammunition per shot at the right; the weight; the hit points of a weapon, armour or
/// generic item that has them. Numbers use the formats of the executable (<c>%s: %d</c> and the others); the labels are
/// <c>intgame.mes</c>'s. NOT READY: the examine script (0x441980), the turn-based action-point preview (0x557370, which also moves the
/// window while a preview is up) and an item worn by a holder other than the PC (the observer is always the PC).
/// </summary>
public static class AreaItemWindowLines
{
    /// <summary>The <c>intgame.mes</c> numbers the lines use (0x554F10, 0x555910, 0x555B50, 0x555780).</summary>
    public const int PowerMessage = 2, TechMessage = 3, WeightMessage = 4, StoneMessage = 5, QuantityMessage = 6, KeysMessage = 7, SpeedMessage = 8,
        UsesMessage = 19, DamageMessage = 43, FatigueDamageMessage = 44, ToHitMessage = 45, RangeMessage = 46, PoisonDamageMessage = 47,
        FireDamageMessage = 48, ElectricalDamageMessage = 49, ArmorClassMessage = 50, DamageResistanceMessage = 51, PoisonResistanceMessage = 52,
        FireResistanceMessage = 53, ElectricalResistanceMessage = 54, MagicResistanceMessage = 55, NoisePenaltyMessage = 56,
        LockpickBonusMessage = 71, HealBonusMessage = 72;

    /// <summary>Every <c>intgame.mes</c> number the item window reads.</summary>
    public static IReadOnlyList<int> Messages { get; } =
    [
        PowerMessage, TechMessage, WeightMessage, StoneMessage, QuantityMessage, KeysMessage, SpeedMessage, UsesMessage, DamageMessage,
        FatigueDamageMessage, ToHitMessage, RangeMessage, PoisonDamageMessage, FireDamageMessage, ElectricalDamageMessage, ArmorClassMessage,
        DamageResistanceMessage, PoisonResistanceMessage, FireResistanceMessage, ElectricalResistanceMessage, MagicResistanceMessage,
        NoisePenaltyMessage, LockpickBonusMessage, HealBonusMessage,
    ];

    /// <summary>Generic item flag bits the window reads (field 0xD3): 0x2 a lockpick bonus, 0x8 a healing bonus (and uses), 0x20 hit points shown.</summary>
    public const uint LockpickFlag = 0x2, UsesFlag = 0x8, HealFlag = 0x8, HitPointsFlag = 0x20;
    /// <summary>The armour coverage of gauntlets (0x504570), whose unarmed bonus the armour line adds.</summary>
    public const int GauntletsCoverage = 3;

    /// <summary>The lines of the item window for <paramref name="input"/>; <paramref name="label"/> gives an <c>intgame.mes</c> text,
    /// <paramref name="ammoName"/> an ammunition type's name (<c>item.mes</c> 0..3, 0x4657D0). Null for a type that is not an item.</summary>
    public static AreaItemLines? Build(AreaItemWindowInput input, Func<int, string> label, Func<int, string> ammoName)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(ammoName);
        var fields = input.Fields;
        var type = fields.Object.Type;
        if (fields.Item is not { } item || type < CombatFieldReader.WeaponType || type > CombatFieldReader.GenericType) return null;
        var complexity = item.MagickTechComplexity;
        var identified = input.Identified;
        // The bonuses in brackets (the magic adjustments) are shown for an identified magickal item only (0x555910, 0x555B50: ebp).
        var showMagic = complexity > 0 && identified;
        var aptitude = Math.Clamp(input.Viewer.Aptitude, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);

        var speed = type == CombatFieldReader.WeaponType && fields.Weapon is { } speedWeapon
            ? Format("{0}: {1}", label(SpeedMessage), WeaponSpeed(speedWeapon, complexity, aptitude, input.Viewer.TrainingOf((int)speedWeapon.Skill))) : "";

        var second = SecondLine(input, fields, item, type, complexity, identified, showMagic, label);
        var (third, thirdRight) = ThirdLines(input, fields, item, type, complexity, identified, aptitude, label, ammoName);
        var weight = WeightLine(input, fields, item, type, complexity, aptitude, label);
        var hitPoints = HitPointsLine(input, fields, type);
        return new(speed, second, third, thirdRight, weight, hitPoints);
    }

    /// <summary>The weapon speed 0x465CF0 gives for the PC as the owner: the magic speed adjustment as the PC gets it (0x461590), plus 5 with at
    /// least apprentice training in the weapon's skill, plus the speed factor. Zero when a stored value is outside the rules' bounds.</summary>
    public static int WeaponSpeed(CombatWeaponFields weapon, int complexity, int aptitude, SkillTraining training)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        try
        {
            var adjusted = EquipmentRules.AdjustMagic(weapon.MagicSpeedAdjustment, complexity, aptitude);
            return unchecked(adjusted + (training >= SkillTraining.Apprentice ? 5 : 0) + weapon.SpeedFactor);
        }
        catch (ArgumentException) { return unchecked(weapon.MagicSpeedAdjustment + weapon.SpeedFactor); }
    }

    // The second line (0x5C70D8): the effects text, else the weapon's or armour's line, else the type's own.
    private static string SecondLine(AreaItemWindowInput input, CombatFields fields, CombatItemFields item, int type, int complexity, bool identified,
        bool showMagic, Func<int, string> label)
    {
        // 0x4D4080's text counts when it has any, and the item is not magickal or is identified (0x555106, 0x555126, 0x5551E6).
        var effects = !string.IsNullOrEmpty(input.EffectsText) && (complexity <= 0 || identified) ? input.EffectsText! : "";
        switch (type)
        {
            case CombatFieldReader.WeaponType:
                return effects.Length > 0 ? effects : fields.Weapon is { } weapon ? WeaponLine(weapon, showMagic, label) : "";
            case CombatFieldReader.ArmorType:
                return effects.Length > 0 ? effects : fields.Armor is { } armor ? ArmorLine(armor, item, showMagic, label) : "";
            case CombatFieldReader.AmmoType:
                return Format("{0}: {1}", label(QuantityMessage), input.Units ?? fields.Ammo?.Quantity ?? 0);
            case 8:
                return Format("{0}: {1}", label(QuantityMessage), input.GoldQuantity ?? 0);
            case 11:
                return "";
            case 12:
                return Format("{0}: {1}", label(KeysMessage), input.Viewer.KeyCount);
            default:
                if (effects.Length > 0) return effects;
                // A generic item without a text: its usage bonus (flag 0x2 a lockpick bonus, else flag 0x8 a healing bonus), "%s: %+d%%" (0x555240).
                if (type == CombatFieldReader.GenericType && fields.Generic is { } generic)
                {
                    var message = (generic.Flags & LockpickFlag) != 0 ? LockpickBonusMessage : (generic.Flags & HealFlag) != 0 ? HealBonusMessage : 0;
                    if (message != 0) return Format("{0}: {1}%", label(message), Signed(generic.UsageBonus));
                }
                return "";
        }
    }

    // 0x555910: damage (normal, fatigue), to hit, range, then poison, fire and electrical damage.
    private static string WeaponLine(CombatWeaponFields weapon, bool showMagic, Func<int, string> label)
    {
        var text = new StringBuilder();
        Segment(text, label(DamageMessage), Range(weapon, CombatDamageIndex.Normal), Magic(weapon, CombatDamageIndex.Normal, showMagic), false);
        Segment(text, label(FatigueDamageMessage), Range(weapon, CombatDamageIndex.Fatigue), Magic(weapon, CombatDamageIndex.Fatigue, showMagic), false);
        Segment(text, label(ToHitMessage), (weapon.BonusToHit, 0), showMagic ? weapon.MagicHitAdjustment : 0, true);
        // A range of 1 (a melee weapon) is not shown (0x555A21).
        Segment(text, label(RangeMessage), (weapon.Range == 1 ? 0 : weapon.Range, 0), showMagic ? weapon.MagicRangeAdjustment : 0, false);
        Segment(text, label(PoisonDamageMessage), Range(weapon, CombatDamageIndex.Poison), Magic(weapon, CombatDamageIndex.Poison, showMagic), false);
        Segment(text, label(FireDamageMessage), Range(weapon, CombatDamageIndex.Fire), Magic(weapon, CombatDamageIndex.Fire, showMagic), false);
        Segment(text, label(ElectricalDamageMessage), Range(weapon, CombatDamageIndex.Electrical), Magic(weapon, CombatDamageIndex.Electrical, showMagic), false);
        return text.ToString();
    }

    // 0x555B50: armour class, the resistances (normal, poison, fire, electrical, magic), the noise penalty and gauntlets' unarmed bonus.
    private static string ArmorLine(CombatArmorFields armor, CombatItemFields item, bool showMagic, Func<int, string> label)
    {
        var text = new StringBuilder();
        Segment(text, label(ArmorClassMessage), (armor.ArmorClassAdjustment, 0), showMagic ? armor.MagicArmorClassAdjustment : 0, false);
        void Resistance(int message, CombatResistanceIndex index) =>
            Segment(text, label(message), (At(armor.ResistanceAdjustment, (int)index), 0), showMagic ? At(armor.MagicResistanceAdjustment, (int)index) : 0, true);
        Resistance(DamageResistanceMessage, CombatResistanceIndex.Normal);
        Resistance(PoisonResistanceMessage, CombatResistanceIndex.Poison);
        Resistance(FireResistanceMessage, CombatResistanceIndex.Fire);
        Resistance(ElectricalResistanceMessage, CombatResistanceIndex.Electrical);
        Resistance(MagicResistanceMessage, CombatResistanceIndex.Magic);
        Segment(text, label(NoisePenaltyMessage), (armor.SilentMoveAdjustment, 0), showMagic ? armor.MagicSilentMoveAdjustment : 0, true);
        // Armour that covers the hands (0x465C30) adds its unarmed damage bonus.
        if (CombatItemArt.Coverage(item.InventoryArt) == GauntletsCoverage)
            Segment(text, label(DamageMessage), (armor.UnarmedBonusDamage, 0), 0, true);
        return text.ToString();
    }

    // The third line's two parts: the magick or tech line of an identified weapon, armour or scroll (left), and the charges, a generic
    // item's uses or a weapon's ammunition per shot (right).
    private static (string Left, string Right) ThirdLines(AreaItemWindowInput input, CombatFields fields, CombatItemFields item, int type, int complexity,
        bool identified, int aptitude, Func<int, string> label, Func<int, string> ammoName)
    {
        var left = "";
        if (identified && type is CombatFieldReader.WeaponType or CombatFieldReader.ArmorType or 10)
        {
            try
            {
                if (complexity > 0) left = Format("{0}: {1}%", label(PowerMessage), EquipmentRules.EffectivePower(complexity, aptitude) * 100 / complexity);
                // 0x461700: the PC's aptitude times the (negative) complexity over 100, zero without a positive aptitude.
                else if (complexity < 0) left = Format("{0}: {1}%", label(TechMessage), Signed(aptitude > 0 ? unchecked(aptitude * complexity) / 100 : 0));
            }
            catch (ArgumentException) { left = ""; }
        }
        var charges = input.Charges ?? item.SpellManaStore;
        string right;
        if (complexity > 0 && charges != 0)
            right = !identified ? Format("{0}: ??", label(UsesMessage)) : charges > 0 ? Format("{0}: {1}", label(UsesMessage), charges) : Format("{0}: +", label(UsesMessage));
        else if (type == CombatFieldReader.GenericType && fields.Generic is { } generic && (generic.Flags & UsesFlag) != 0)
            right = Format("{0}: {1}", label(UsesMessage), generic.UsageCountRemaining);
        else if (type == CombatFieldReader.WeaponType && fields.Weapon is { } weapon && weapon.AmmoType != CombatFieldReader.NoAmmo && weapon.AmmoConsumption > 0)
            right = Format("{0}: {1}", ammoName(weapon.AmmoType), weapon.AmmoConsumption);
        else right = "";
        return (left, right);
    }

    // "%s: %d %s" (0x5C7864): the weight as the PC holds it (0x461370), the label and the unit.
    private static string WeightLine(AreaItemWindowInput input, CombatFields fields, CombatItemFields item, int type, int complexity, int aptitude, Func<int, string> label)
    {
        try
        {
            var units = type == CombatFieldReader.AmmoType ? input.Units ?? fields.Ammo?.Quantity ?? 0 : 0;
            var weight = CarryRules.ItemWeight(new(type, item.Weight, item.MagicWeight, complexity, units), CarryHolder.Critter, aptitude);
            return Format("{0}: {1} {2}", label(WeightMessage), weight, label(StoneMessage));
        }
        catch (ArgumentException) { return ""; }
    }

    // "%d/%d" (0x5C7848): a weapon's, an armour's or a generic item's (flag 0x20) hit points, not those of a destroyed item (0x5045A0).
    private static string HitPointsLine(AreaItemWindowInput input, CombatFields fields, int type)
    {
        var shows = type is CombatFieldReader.WeaponType or CombatFieldReader.ArmorType
            || type == CombatFieldReader.GenericType && fields.Generic is { } generic && (generic.Flags & HitPointsFlag) != 0;
        if (!shows) return "";
        var broken = input.Condition?.Broken ?? CombatItemArt.Destroyed(fields.Object.CurrentArt);
        if (broken) return "";
        var condition = input.Condition ?? new ItemCondition(fields.Object.HitPoints, fields.Object.HitPointAdjustment, fields.Object.HitPointDamage, false);
        return AreaHoverWindow.HitPointText(unchecked(condition.Points + condition.Adjustment - condition.Damage), unchecked(condition.Points + condition.Adjustment));
    }

    // 0x555780: "label:" then "min-max", or the single value (signed when asked), then "(+bonus)" and two spaces; nothing when all are 0.
    private static void Segment(StringBuilder text, string name, (int Minimum, int Maximum) value, int bonus, bool signed)
    {
        if (value.Minimum == 0 && value.Maximum == 0 && bonus == 0) return;
        text.Append(name).Append(':');
        if (value.Maximum != 0) text.Append(value.Minimum.ToString(CultureInfo.InvariantCulture)).Append('-').Append(value.Maximum.ToString(CultureInfo.InvariantCulture));
        else if (value.Minimum != 0) text.Append(signed ? Signed(value.Minimum) : value.Minimum.ToString(CultureInfo.InvariantCulture));
        if (bonus != 0) text.Append('(').Append(Signed(bonus)).Append(')');
        text.Append("  ");
    }

    private static (int Minimum, int Maximum) Range(CombatWeaponFields weapon, CombatDamageIndex index) =>
        (int)index < weapon.Damage.Count ? (weapon.Damage[(int)index].Minimum, weapon.Damage[(int)index].Maximum) : (0, 0);

    private static int Magic(CombatWeaponFields weapon, CombatDamageIndex index, bool showMagic) =>
        showMagic && (int)index < weapon.MagicDamageAdjustment.Count ? weapon.MagicDamageAdjustment[(int)index] : 0;

    private static int At(IReadOnlyList<int> list, int index) => index < list.Count ? list[index] : 0;

    private static string Signed(int value) => value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
