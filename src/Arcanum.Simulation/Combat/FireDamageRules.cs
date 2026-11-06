namespace Arcanum.Simulation.Combat;

/// <summary>The round gate's answer (<see cref="FireDamageRules.Gate"/>): whether the burn hurts anyone now, and whether its damage is doubled.</summary>
public readonly record struct FireRoundGate(bool Burns, bool Doubled);

/// <summary>
/// ARC-DEV-144 (MIG13 S9c, docs/architecture/goal-table.md, the S9c read GT94–GT99): the rules of the burning object's damage (0x42A720), shared by goal 77 (burning scenery) and the fire-damage eye candy 75/76.
/// Every object of the types 5..16 (weapon, ammunition, armour, gold, food, scroll, key, key ring, written, generic, PC, NPC) on the burning object's own tile is hurt, the burning object itself included; a wall, portal, container,
/// scenery, projectile or trap is not, nor an item in an inventory. The damage is 3 fire for goal 77 and 1 normal for the eye candy (every 75/76 run the add builds carries frame flag 0x4000). In a turn-based round the gate belongs to the
/// <b>run</b>: its slot 19 holds the id of the round it last burned in, a call in the same round hurts nobody, and the first call of a round hurts everyone once with both amounts doubled. Outside a round every call burns, undoubled.
/// </summary>
public static class FireDamageRules
{
    /// <summary>Fire damage of goal 77 and the normal damage of an eye candy's burn (0x42A720's base amounts).</summary>
    public const int FireAmount = 3, NormalAmount = 1;

    /// <summary>The object types the burn hurts: 5..16 (mask 0x1FFE0).</summary>
    public static bool HurtType(int objectType) => objectType is >= 5 and <= 16;

    /// <summary>The damage packet of one burn: 3 fire, or 1 normal for <paramref name="normal"/> (frame flag 0x4000); doubled in a round (both entries).</summary>
    public static DamageByType Damage(bool normal, bool doubled)
    {
        var factor = doubled ? 2 : 1;
        return normal ? DamageByType.Zero with { Normal = NormalAmount * factor } : DamageByType.Zero with { Fire = FireAmount * factor };
    }

    /// <summary>
    /// The round gate: outside a round (<paramref name="roundId"/> null) every call burns undoubled; in a round the call burns, doubled, only when the run's stored id (<paramref name="stored"/>, 0 for a fresh run) is not the
    /// current id. The first round's id is 1, so a fresh run never matches. The stored id survives the end of combat, so a run still alive in a later combat skips that combat's round with the same number (the original's quirk, kept).
    /// </summary>
    public static FireRoundGate Gate(int? roundId, int stored) => roundId is not { } id ? new(true, false) : new(stored != id, true);
}
