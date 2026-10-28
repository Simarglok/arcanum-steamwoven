using System.Globalization;

namespace Arcanum.Presentation.Hud;

/// <summary>What a critical took or did, for its message (CH12).</summary>
public enum FloaterCritical { None, Hit, Miss }

/// <summary>A critical hit's damage multiplier message (13–15).</summary>
public enum FloaterMultiplier { None, OneAndHalf, Double, Triple }

/// <summary>What an item's wear step did, for its message: an armour's or a weapon's damage or break.</summary>
public enum FloaterItem { None, Damaged, Broken }

/// <summary>Over whom a floater rises: the critter or object the step is about (the target, or the attacker of a critical
/// self-hit), or the attacker (its own weapon's or hands' wear).</summary>
public enum FloaterOver { Struck, Attacker }

/// <summary>
/// One attack's outcome as its floaters read it (CH12): whether it was dodged; for the damage step on a critter, the struck one's side,
/// the critical and its multiplier, the life effects that took hold, the item steps and the damage; for an object, its damage; and the
/// attacker's own wear. The host fills it from the session's results.
/// </summary>
public sealed record CombatFloaterHit
{
    /// <summary>A dodge: the target floats the dodge message in white and nothing else happens.</summary>
    public bool Dodged { get; init; }
    /// <summary>The damage step ran on a critter (not dead before it).</summary>
    public bool CritterStep { get; init; }
    /// <summary>The damage step ran on an object that is no critter: <see cref="HitPoints"/> is its damage.</summary>
    public bool ObjectStep { get; init; }
    public FloaterSide Struck { get; init; }
    public FloaterCritical Critical { get; init; }
    public FloaterMultiplier Multiplier { get; init; }
    public bool Stunned { get; init; }
    public bool KnockedOut { get; init; }
    public bool KnockedDown { get; init; }
    public bool Scarred { get; init; }
    public bool Blinded { get; init; }
    public bool CrippledArm { get; init; }
    public bool ArmDropsWeapon { get; init; }
    public bool ArmDropsShield { get; init; }
    public bool CrippledLegs { get; init; }
    public FloaterItem Armor { get; init; }
    public bool HelmetDropped { get; init; }
    /// <summary>The struck critter's weapon after the critical "damage weapon" (CW3).</summary>
    public FloaterItem Weapon { get; init; }
    /// <summary>The critical "destroy weapon" or "explode weapon" deleted the struck critter's weapon (CW3): no ammunition or drop message.</summary>
    public bool WeaponDestroyed { get; init; }
    public bool AmmunitionLost { get; init; }
    public bool WeaponDropped { get; init; }
    public int HitPoints { get; init; }
    public int Fatigue { get; init; }
    public int Poison { get; init; }
    /// <summary>The attacker's weapon worn by the struck one's material (CW7): damaged or broken.</summary>
    public FloaterItem AttackerWeapon { get; init; }
    /// <summary>The hit points a bare-handed attacker lost to the struck one's material (CW7); null when none.</summary>
    public int? AttackerHands { get; init; }
}

/// <summary>One floater an attack makes: over whom, in which colour, its text.</summary>
public readonly record struct CombatFloater(FloaterOver Over, FloaterColor Color, string Text);

/// <summary>
/// ARC-UI-021: the floaters of one attack (ledger CH12, the damage step 0x4B4390, the attack 0x4B3170 and the weapon wear 0x4B5580,
/// CW2/CW3/CW7), in the original's order, with their texts from <c>mes\combat.mes</c> by message number. At every level: the dodge
/// (white, over the target); the critical miss (over the attacker, which it struck) or the critical hit, the multiplier, the stun, the
/// unconsciousness or knockdown, the scar, the blinding, the crippled arm and the items it drops, the crippled legs, the armour's
/// damage or break, the helmet's drop, the weapon's damage, break or destruction, the lost ammunition and the dropped weapon, all in
/// the struck critter's colour; the attacker's weapon worn on a hard target in red over the attacker. At level 2 only: the damage line
/// ("N line-1", then " N line-10" for fatigue; with fatigue and no hit-point damage only the fatigue part) in that colour, the poison
/// increase ("line-0 +N") in green, an object's damage ("line-1 N") in white and a bare-handed attacker's own loss ("N line-1") in
/// red over it. Healing makes none.
/// </summary>
public static class CombatFloaters
{
    /// <summary><c>mes\combat.mes</c> message numbers (CH12).</summary>
    public const int PoisonLine = 0, HitPointsLine = 1, CriticalMissLine = 2, ScarLine = 3, BlindLine = 4, ArmLine = 5, LegsLine = 6,
        ArmorDamagedLine = 7, WeaponDamagedLine = 8, AmmunitionLine = 9, FatigueLine = 10, DodgeLine = 11, CriticalHitLine = 12,
        HalfMoreLine = 13, DoubleLine = 14, TripleLine = 15, StunLine = 16, UnconsciousLine = 17, KnockdownLine = 18,
        WeaponDroppedLine = 19, ItemDroppedLine = 20, WeaponDestroyedLine = 22, WeaponBrokenLine = 23, ArmorBrokenLine = 24;
    /// <summary>The highest message number the floaters read.</summary>
    public const int LastLine = 24;

    /// <summary>The floaters of <paramref name="hit"/> at the "text floaters" <paramref name="level"/>, their texts from
    /// <paramref name="message"/> (a message number's text).</summary>
    public static IReadOnlyList<CombatFloater> Of(CombatFloaterHit hit, int level, Func<int, string> message)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, FloaterRules.MaxLevel);
        var numbers = level == FloaterRules.Numbers;
        var culture = CultureInfo.InvariantCulture;
        var floaters = new List<CombatFloater>();
        if (hit.Dodged) { floaters.Add(new(FloaterOver.Struck, FloaterColor.White, message(DodgeLine))); return floaters; }
        if (hit.ObjectStep && numbers)
            floaters.Add(new(FloaterOver.Struck, FloaterColor.White, message(HitPointsLine) + " " + hit.HitPoints.ToString(culture)));
        if (hit.CritterStep)
        {
            var color = FloaterRules.SideColor(hit.Struck);
            void Line(int number) => floaters.Add(new(FloaterOver.Struck, color, message(number)));
            if (hit.Critical == FloaterCritical.Miss) Line(CriticalMissLine);
            else if (hit.Critical == FloaterCritical.Hit) Line(CriticalHitLine);
            if (hit.Multiplier != FloaterMultiplier.None)
                Line(hit.Multiplier switch { FloaterMultiplier.Triple => TripleLine, FloaterMultiplier.Double => DoubleLine, _ => HalfMoreLine });
            if (hit.Stunned) Line(StunLine);
            if (hit.KnockedOut) Line(UnconsciousLine);
            else if (hit.KnockedDown) Line(KnockdownLine);
            if (hit.Scarred) Line(ScarLine);
            if (hit.Blinded) Line(BlindLine);
            if (hit.CrippledArm) Line(ArmLine);
            if (hit.ArmDropsWeapon) Line(WeaponDroppedLine);
            if (hit.ArmDropsShield) Line(ItemDroppedLine);
            if (hit.CrippledLegs) Line(LegsLine);
            if (hit.Armor is FloaterItem.Damaged) Line(ArmorDamagedLine);
            else if (hit.Armor is FloaterItem.Broken) Line(ArmorBrokenLine);
            if (hit.HelmetDropped) Line(ItemDroppedLine);
            if (hit.Weapon is FloaterItem.Damaged) Line(WeaponDamagedLine);
            else if (hit.Weapon is FloaterItem.Broken) Line(WeaponBrokenLine);
            if (hit.WeaponDestroyed) Line(WeaponDestroyedLine);
            else
            {
                if (hit.AmmunitionLost) Line(AmmunitionLine);
                if (hit.WeaponDropped) Line(WeaponDroppedLine);
            }
            if (numbers)
            {
                var text = hit.HitPoints.ToString(culture) + " " + message(HitPointsLine);
                if (hit.Fatigue > 0)
                {
                    var fatigue = " " + hit.Fatigue.ToString(culture) + " " + message(FatigueLine);
                    text = hit.HitPoints > 0 ? text + fatigue : fatigue;
                }
                floaters.Add(new(FloaterOver.Struck, color, text));
                if (hit.Poison > 0)
                    floaters.Add(new(FloaterOver.Struck, FloaterColor.Green, message(PoisonLine) + " +" + hit.Poison.ToString(culture)));
            }
        }
        if (hit.AttackerWeapon is FloaterItem.Damaged) floaters.Add(new(FloaterOver.Attacker, FloaterColor.Red, message(WeaponDamagedLine)));
        else if (hit.AttackerWeapon is FloaterItem.Broken) floaters.Add(new(FloaterOver.Attacker, FloaterColor.Red, message(WeaponBrokenLine)));
        if (hit.AttackerHands is { } hands && hands > 0 && numbers)
            floaters.Add(new(FloaterOver.Attacker, FloaterColor.Red, hands.ToString(culture) + " " + message(HitPointsLine)));
        return floaters;
    }
}
