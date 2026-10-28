using Arcanum.Simulation.Equipment;

namespace Arcanum.Simulation.Items;

/// <summary>Bounds of the weight facts the carry rules accept (ARC-WORLD-085).</summary>
public static class CarryLimits
{
    /// <summary>An item's stored weight (field 0x59) and its magic weight adjustment (field 0x5A) each lie within ±this; installed items
    /// stay far below.</summary>
    public const int MaxWeight = 1_000_000;
    /// <summary>The unit count of a stack lies within 0..this (the session's ammunition and gold counts are 32-bit and non-negative).</summary>
    public const int MaxQuantity = int.MaxValue;
}

/// <summary>Who holds an item when it is weighed (the owner the original passes to 0x461370): nobody, a PC or an NPC, or any other
/// object (a container).</summary>
public enum CarryHolder { None, Critter, Container }

/// <summary>
/// One item as the weight reads it: its object type (5..14; 6 ammunition and 8 gold), its stored weight (field 0x59), its magic weight
/// adjustment (field 0x5A), its magick/tech complexity (field 0x60) and, for ammunition, its unit count (field 0x8E).
/// </summary>
public readonly record struct CarriedItem(int Type, int Weight, int MagicWeight = 0, int Complexity = 0, int Quantity = 0);

/// <summary>
/// ARC-WORLD-085: the weight of carried items and the encumbrance level of a critter, read in the 1.0.7.4 executable (ledger WE1–WE9,
/// <c>docs/research/ledger/items.md</c>). Pure functions over stored values and the holder's effective stats; the session keeps the
/// facts and the state. Arithmetic is the original's 32-bit arithmetic: sums and products wrap, divisions truncate toward zero.
/// </summary>
public static class CarryRules
{
    public const int AmmoType = 6, GoldType = 8;

    /// <summary>The encumbrance levels 0..6 (0x45F790 answers 6 for a non-critter and at or above the 90 % threshold).</summary>
    public const int Levels = 7;
    public const int HeaviestLevel = Levels - 1;

    /// <summary>The effect the level adds, once a level (effect 158 of <c>rules\effect.mes</c>, speed −2 in the installed game), with the
    /// item cause (5) (0x45F820, WE5).</summary>
    public const int EffectNumber = 158;

    /// <summary>The first <c>mes\critter.mes</c> line of the level-change messages (line 11 + level, shown to the local PC) and of the
    /// level names (line 18 + level, 0x45F930).</summary>
    public const int MessageLine = 11, NameLine = 18;

    // The thresholds in percent of the carry weight (0x5B306C, read through 0x45F960) and the fatigue drain intervals in game
    // milliseconds (0x5B3050), by level.
    private static readonly int[] Thresholds = [15, 30, 45, 60, 75, 90, 100];
    private static readonly int[] DrainIntervals = [0, 0, 0, 0, 600_000, 60_000, 5_000];

    /// <summary>
    /// An item's weight (the source's 0x461370, WE1): gold weighs nothing; any other item weighs its stored weight plus, with a holder,
    /// its magic weight adjustment as the holder gets it (<see cref="HolderMagicWeight"/>); ammunition weighs that times its units / 4.
    /// </summary>
    /// <param name="aptitude">The critter holder's effective magick/tech aptitude (stat 16), read only for <see cref="CarryHolder.Critter"/>.</param>
    [OriginalFunction(0x461370, OriginalFunctionScope.Full)]
    public static int ItemWeight(CarriedItem item, CarryHolder holder = CarryHolder.None, int aptitude = 0)
    {
        Check(item);
        if (!Enum.IsDefined(holder)) throw new ArgumentOutOfRangeException(nameof(holder));
        if (item.Type == GoldType) return 0;
        var weight = holder == CarryHolder.None ? item.Weight
            : unchecked(item.Weight + HolderMagicWeight(item.MagicWeight, item.Complexity, holder, aptitude));
        return item.Type == AmmoType ? unchecked(item.Quantity * weight) / 4 : weight;
    }

    /// <summary>
    /// The magic weight adjustment as a holder gets it (0x461590 with the power 0x4614A0, WE2): a critter scales it by its power
    /// (<see cref="EquipmentRules.AdjustMagic"/>); any other holder has the item's full power, so a magickal item keeps the whole
    /// adjustment and a technological one none; a neutral item keeps it whatever the holder.
    /// </summary>
    public static int HolderMagicWeight(int magicWeight, int complexity, CarryHolder holder, int aptitude = 0)
    {
        if (magicWeight is < -CarryLimits.MaxWeight or > CarryLimits.MaxWeight) throw new ArgumentOutOfRangeException(nameof(magicWeight));
        if (aptitude is < EquipmentLimits.MinAptitude or > EquipmentLimits.MaxAptitude) throw new ArgumentOutOfRangeException(nameof(aptitude));
        if (holder == CarryHolder.None) return 0;
        if (!Enum.IsDefined(holder)) throw new ArgumentOutOfRangeException(nameof(holder));
        if (complexity == 0) return magicWeight;
        // The power of a non-critter holder is the complexity; a critter's is (complexity + aptitude) / 2 in 32 bits, truncated toward
        // zero and clamped to 0..complexity on the item's side (0x4614A0). The product wraps at 32 bits as the original's does.
        var power = complexity;
        if (holder == CarryHolder.Critter)
        {
            power = unchecked(complexity + aptitude) / 2;
            power = complexity > 0 ? Math.Clamp(power, 0, complexity) : Math.Clamp(power, complexity, 0);
        }
        var share = unchecked(magicWeight * power) / complexity;
        return complexity > 0 ? share : unchecked(magicWeight - share);
    }

    /// <summary>A holder's total weight (0x461410, WE3): the sum of its list's item weights, each weighed with this holder (a critter's
    /// worn items are in its list), wrapping at 32 bits. An empty list weighs 0.</summary>
    [OriginalFunction(0x461410, OriginalFunctionScope.Full)]
    public static int TotalWeight(IEnumerable<CarriedItem> items, CarryHolder holder, int aptitude = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        var total = 0;
        foreach (var item in items) total = unchecked(total + ItemWeight(item, holder, aptitude));
        return total;
    }

    /// <summary>The insert check's weight part for a critter (0x466510, WE4): the item fits when the holder's total weight plus the item's
    /// weight (with this holder) is at most the holder's effective carry weight (stat 8); the sum wraps at 32 bits.</summary>
    public static bool Fits(int totalWeight, int itemWeight, int carryWeight) => unchecked(totalWeight + itemWeight) <= carryWeight;

    /// <summary>
    /// A critter's encumbrance level (0x45F790, WE6): the first level whose threshold, <c>percent × carry weight / 100</c> in 32 bits
    /// truncated toward zero, is above the total weight; the heaviest level (6) at or above the 90 % threshold. The 100 % entry of the
    /// table is never decisive.
    /// </summary>
    [OriginalFunction(0x45F790, OriginalFunctionScope.Partial)]
    public static int Level(int totalWeight, int carryWeight)
    {
        for (var level = 0; level < Levels; level++)
            if (totalWeight < unchecked(Thresholds[level] * carryWeight) / 100) return level;
        return HeaviestLevel;
    }

    /// <summary>The threshold of a level: the load from which the next level starts (<c>percent × carry weight / 100</c>).</summary>
    public static int Threshold(int level, int carryWeight) => unchecked(Thresholds[CheckLevel(level)] * carryWeight) / 100;

    /// <summary>How many entries of <see cref="EffectNumber"/> a level holds (0x45F820, WE5): one a level, eight at the heaviest.</summary>
    public static int EffectEntries(int level) => CheckLevel(level) == HeaviestLevel ? HeaviestLevel + 2 : level;

    /// <summary>The interval of the encumbrance fatigue timer at a level in game milliseconds (0x5B3050, WE5): 600 s at level 4, 60 s
    /// at 5, 5 s at 6; null below level 4, where no timer is scheduled.</summary>
    public static int? DrainIntervalMs(int level) => DrainIntervals[CheckLevel(level)] is > 0 and var ms ? ms : null;

    /// <summary>
    /// The fatigue one encumbrance tick drains (the fatigue timer's mode 1, 0x45E610, WE7): nothing below 5 current fatigue; otherwise
    /// <paramref name="steps"/> (the timer's <c>max(1, elapsed s / 80)</c>, <see cref="Combat.LifeRules.FatigueRecoverySteps"/>), or 1
    /// when that would leave less than 5.
    /// </summary>
    public static int Drain(int currentFatigue, int steps)
    {
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps));
        if (currentFatigue < 5) return 0;
        return currentFatigue - steps < 5 ? 1 : steps;
    }

    private static int CheckLevel(int level) =>
        level is >= 0 and < Levels ? level : throw new ArgumentOutOfRangeException(nameof(level));

    private static void Check(CarriedItem item)
    {
        if (item.Type is < 5 or > 14) throw new ArgumentOutOfRangeException(nameof(item), "Not an item type.");
        if (item.Weight is < -CarryLimits.MaxWeight or > CarryLimits.MaxWeight) throw new ArgumentOutOfRangeException(nameof(item), "Weight out of range.");
        if (item.Quantity < 0 || (item.Quantity != 0 && item.Type is not (AmmoType or GoldType)))
            throw new ArgumentOutOfRangeException(nameof(item), "Only gold and ammunition carry a unit count.");
    }
}
