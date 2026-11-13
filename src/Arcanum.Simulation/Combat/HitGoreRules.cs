namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-UI-1737 (ledger OH2, ER5, RH3): which blood eye candy the original's damage effect (0x433020) asks for a struck critter, kind 1 (the
/// gore of a hit), and how the "violence filter" setting (a plain global read, 0x5DE6A4) changes it. The gates before the choice (a null
/// object, a kind of 0, a multiplayer client, a target that is no PC or NPC, object flags 0x1 or 0x2, critter flags 2 bit 0x100) are the
/// caller's: <see cref="Refused"/> names the one the flags decide. Godot-free; Steamwoven draws no blood eye candy of a hit yet, so this
/// is the rule the draw will read (the setting itself already reaches the dying animation and the pool of blood).
/// </summary>
public static class HitGoreRules
{
    /// <summary>The clean effect: with the filter on, or for a stoned critter, the choice is always this; so it is for a mechanical critter.</summary>
    public const int CleanIndex = 7;
    /// <summary>The effect of a critter with critter flag 0x4 (the pool's "undead" bit, <see cref="BloodPoolRules.UndeadFlag"/>; OH2 called it the mechanical one, but the pool rule BP2 names 0x4 undead and 0x20000000 mechanical).</summary>
    public const int UndeadIndex = 6;
    public const uint StonedSpellFlag = BloodPoolRules.StonedSpellFlag, NoBloodSplotches = 0x100;

    /// <summary>Critter flags 2 bit 0x100 stops the damage effect for a critter altogether (before the choice).</summary>
    public static bool Refused(uint critterFlags2) => (critterFlags2 & NoBloodSplotches) != 0;

    /// <summary>
    /// The index into the blood eye candy table for <paramref name="kind"/> (1: the hit's gore; another kind starts at kind − 1). Kind 1:
    /// with the filter off and no stoned critter, a critter with flag 0x4 takes 6, one with flag 0x20000000 (mechanical) takes 7, any other keeps 0;
    /// with the filter on or a stoned critter it is 7. Then an index still 0 takes the damage tier from the packet's field 0x5C figure
    /// (<paramref name="packetDamage"/>, not above 5: none; below 10: 8; above 14: 10; else 9), so with the filter on, which always makes
    /// 7, the tiers are never read.
    /// </summary>
    [OriginalFunction(0x433020, OriginalFunctionScope.Partial)]
    public static int Index(int kind, bool violenceFilter, uint critterFlags, uint spellFlags, int packetDamage)
    {
        var index = kind - 1;
        if (kind == 1)
        {
            if (violenceFilter || (spellFlags & StonedSpellFlag) != 0) index = CleanIndex;
            else if ((critterFlags & BloodPoolRules.UndeadFlag) != 0) index = UndeadIndex;
            else if ((critterFlags & BloodPoolRules.MechanicalFlag) != 0) index = CleanIndex;
        }
        if (packetDamage > 5 && index == 0) index = packetDamage < 10 ? 8 : packetDamage > 14 ? 10 : 9;
        return index;
    }
}
