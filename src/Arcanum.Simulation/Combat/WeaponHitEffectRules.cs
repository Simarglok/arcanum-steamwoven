namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-RENDER-1990 (ledger ER5, TT14, GT79): where a weapon's own effects sit in the weapon eye-candy list (0x5DE610, <c>rules\WeaponEyeCandy.mes</c>) and how the weapon's hit effect (0x4377C0, asked as kind 3 at the start of the
/// attack end 0x4B6930) is chosen. Godot-free. The entry is <c>(prototype − 0x178D) · 5 + kind</c>, the prototype being the weapon object's prototype number less 20 (0x49B290: the basic prototype, the number the object's
/// own prototype field of the viewer's records carries); a prototype below 0x178D gives a negative index, which the original's signed range test lets through (F0, no installed weapon is below it); here it names no entry, so no effect plays.
/// </summary>
public static class WeaponHitEffectRules
{
    /// <summary>The first weapon prototype the list names (0x178D, 6029): the list holds one group of five entries for each prototype from here on.</summary>
    public const int FirstPrototype = 0x178D;
    /// <summary>What 0x49B290 takes off a prototype file's number to get the basic prototype the list is indexed by.</summary>
    public const int PrototypeFileOffset = 20;
    public const int PerWeapon = 5;
    /// <summary>The kinds the function takes (the <c>whichIdx</c> asserts allow 0 to 4): 0 the begin of the attack animation (0x432700), 1 the swing (0x4B3170), 2 the projectile's speed (the flight, 0x435870), 3 the hit (0x4B6930). 4 is never asked.</summary>
    public const int Begin = 0, Swing = 1, Projectile = 2, Hit = 3, Unused = 4;
    /// <summary>The add's limit word (+0x20 of the request 0x4CCF70 takes, the same-art count 0x4377C0 sets to 2): the add is refused once more than this many eye candies of the same art class already stand in the victim's foreground list.</summary>
    public const int AddLimit = 2;

    /// <summary>The entry of a weapon's effect of <paramref name="kind"/> (0 to 4) in the weapon list, for the weapon's <paramref name="prototypeNumber"/> (its prototype file's number); null for a basic prototype below the first the list names, or a kind outside 0..4.</summary>
    public static int? Index(int prototypeNumber, int kind)
    {
        var basic = prototypeNumber - PrototypeFileOffset;
        // A number far above any prototype file (a corrupt record) names no entry rather than overflowing.
        return kind is < Begin or > Unused || basic < FirstPrototype || basic - FirstPrototype > (int.MaxValue - Unused) / PerWeapon ? null : (basic - FirstPrototype) * PerWeapon + kind;
    }

    /// <summary>
    /// ARC-RENDER-2035 (ledger RH10, PF4): the steps a flight takes in one tick, from the weapon list's projectile-speed entry (kind 2, <c>Proj Speed:</c>, <paramref name="projectileSpeed"/>, 0 for no entry or none written): the flight start
    /// reads the entry only when the flight's weapon is a weapon object (type 5), stores its speed in the run, and the flight tick takes that many steps, 4 for 0. A negative speed takes no step in the original (none is installed);
    /// the saved flight needs at least one step, so here it flies at the default pace.
    /// </summary>
    public static int StepsPerTick(int projectileSpeed) => projectileSpeed > 0 ? projectileSpeed : ProjectileRules.DefaultStepsPerTick;
}
