using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-COMBAT-017: the thrown range of an item (0x4622A0, ledger RH4): how far a thrower throws it. The shared check reads it as the
/// throwing skill's range (impossible beyond it, SC8) and a thrown item's flight ends beyond it (PF9). The weight is the caller's:
/// the item's effective weight (0x461370, its stored weight plus its magic adjustment), which the session does not keep.
/// </summary>
public static class ThrowRules
{
    /// <summary>The largest item weight the rule takes.</summary>
    public const int MaxWeight = 1_000_000;
    /// <summary>ARC-COMBAT-018: the directions a missed throw scatters in (rotation 0..7 of the neighbour step 0x4B8FF0, drawn by
    /// 0x4CEF00(0, 7)).</summary>
    public const int ScatterDirections = 8;

    /// <summary>
    /// <c>max(1, (A / max(1, W / 10)) / 5)</c> with <c>A = 50 × strength</c>, raised by <c>A / 2</c> from expert throwing, and
    /// <c>W</c> the weight; signed 32-bit, every division toward zero, as the original computes it.
    /// </summary>
    [OriginalFunction(0x4622A0, OriginalFunctionScope.Partial)]
    public static int Range(int strength, SkillTraining throwing, int weight)
    {
        if (!Enum.IsDefined(throwing)) throw new ArgumentOutOfRangeException(nameof(throwing));
        if (weight is < 0 or > MaxWeight) throw new ArgumentOutOfRangeException(nameof(weight));
        var arm = unchecked(strength * 50);
        if (throwing >= SkillTraining.Expert) arm = unchecked(arm + arm / 2);
        var per = Math.Max(1, weight / 10);
        return Math.Max(1, arm / per / 5);
    }

    /// <summary>
    /// ARC-COMBAT-023 (0x462330, ledger WB8): the damage of a thrown item that is not a throwable weapon (weapon flag 0x10): normal
    /// <c>(weight + 99) / 100 .. (weight + 49) / 50</c> from its effective <paramref name="weight"/> (0x461370 with its holder), signed
    /// and toward zero, and no other type.
    /// </summary>
    [OriginalFunction(0x462330, OriginalFunctionScope.Partial)]
    public static DamageRanges ItemDamage(int weight)
    {
        if (weight is < 0 or > MaxWeight) throw new ArgumentOutOfRangeException(nameof(weight));
        return DamageRanges.None with { Normal = new((weight + 99) / 100, (weight + 49) / 50) };
    }

    /// <summary>
    /// ARC-COMBAT-023 (the weapon damage range 0x465F70 for skill 3 → 0x462330, ledger WB8, TT rows): what a thrown item strikes with at
    /// the thrown <paramref name="range"/>. A weapon (an item with its weapon block) keeps its own values for this thrower
    /// (<see cref="EquipmentRules.Weapon"/>: to-hit, charts, magic adjustments under <paramref name="aptitude"/>); only a throwable one
    /// (<paramref name="throwable"/>) keeps its own damage, any other weapon strikes with <see cref="ItemDamage"/> of its weight and
    /// keeps its magic damage adjustment (0x461590, a weapon's only). Anything else (null, or an item without a weapon block) is a
    /// <see cref="WeaponKind.ThrownItem"/> with <see cref="ItemDamage"/> and its uber flag.
    /// </summary>
    public static CombatWeapon ThrownWeapon(EquipmentItem? item, bool throwable, int weight, int aptitude, int range)
    {
        if (range is < 0 or > AttackResolver.MaxMagnitude) throw new ArgumentOutOfRangeException(nameof(range));
        var damage = ItemDamage(weight);
        if (item is not { Kind: EquipmentItemKind.Weapon, Weapon: not null })
            return new CombatWeapon { Kind = WeaponKind.ThrownItem, Damage = damage, Range = range, Uber = item?.Uber ?? false };
        var weapon = EquipmentRules.Weapon(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = item }, aptitude);
        return weapon with { Damage = throwable ? weapon.Damage : damage, Range = range };
    }

    /// <summary>
    /// ARC-COMBAT-018: how far a missed throw scatters (the ranged path 0x4B39B0, ledger RH2 and PS rows): <c>(20 − level) / 5 + 1</c>
    /// tiles with the thrower's basic throwing level (0x4C5EB0), signed 32-bit and toward zero, at most the tile
    /// <paramref name="distance"/> from the thrower to the target location; 0 or less moves nothing.
    /// </summary>
    public static int ScatterTiles(int throwingLevel, int distance) => Math.Min(unchecked(20 - throwingLevel) / 5 + 1, distance);

    /// <summary>
    /// ARC-COMBAT-018: 0x4B90D0, the location <paramref name="tiles"/> steps from <paramref name="from"/> in
    /// <paramref name="rotation"/> 0..7 (<paramref name="step"/>: the neighbour step 0x4B8FF0, null off the map): a step off the map
    /// stops the walk on the last tile it reached.
    /// </summary>
    [OriginalFunction(0x4B90D0, OriginalFunctionScope.Partial)]
    public static CellCoord Scatter(CellCoord from, int rotation, int tiles, Func<CellCoord, int, CellCoord?> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (rotation is < 0 or >= ScatterDirections) throw new ArgumentOutOfRangeException(nameof(rotation));
        var cell = from;
        for (var i = 0; i < tiles; i++)
        {
            if (step(cell, rotation) is not { } next) break;
            cell = next;
        }
        return cell;
    }
}
