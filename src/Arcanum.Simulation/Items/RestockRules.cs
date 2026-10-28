using Arcanum.Simulation.Randomness;

namespace Arcanum.Simulation.Items;

/// <summary>The object a restock event names, by its type field (0x14F): an NPC (type 16), a container (type 2) or anything else (a PC
/// included, which 0x4641A0 gives no inventory source).</summary>
public enum RestockHolderKind { Other, Npc, Container }

/// <summary>
/// What the restock handler reads about the event's object (the host's, from the live object): its kind, its inventory source (NPC field
/// 0xEF, container field 0x3D; 0 is none), whether it is dead (0x45D8D0, hit points at 0 or below) and has a PC at the top of its leader
/// chain (0x45DDA0), a container's flags (field 0x38) and whether the container's screen rectangle meets the game window's (0x43DFD0
/// against the window 0x5E8808..0x5E8814).
/// </summary>
public readonly record struct RestockHolder(RestockHolderKind Kind, int InventorySource, bool Dead, bool PcLeader, uint ContainerFlags, bool OnScreen);

/// <summary>What a restock event does when it fires (0x463C60).</summary>
public enum RestockStep
{
    /// <summary>Nothing: no inventory source, a dead NPC or one with a PC leader, or an off-screen container without a living merchant
    /// near it. The event is not scheduled again.</summary>
    None,
    /// <summary>The object's inventory is cleared and generated again (0x463E20).</summary>
    Regenerate,
    /// <summary>A container on screen: the event is scheduled again (0x4640C0), nothing else changes.</summary>
    Reschedule,
    /// <summary>An off-screen container through its merchant: the merchant regenerates when it has an inventory source of its own (the
    /// handler run again on it), then the container regenerates.</summary>
    RegenerateMerchantAndContainer,
}

/// <summary>The decision of a restock event: the step and, for <see cref="RestockStep.RegenerateMerchantAndContainer"/>, whether the
/// merchant regenerates too.</summary>
public readonly record struct RestockDecision(RestockStep Step, bool MerchantRegenerates);

/// <summary>One item line of an inventory source set: the chance in percent and the basic prototype number (the prototype is number + 20).</summary>
public readonly record struct RestockLine(int Rate, int BasicPrototype);

/// <summary>An inventory source set as the generation reads it (0x4BF5D0): the coin range and the item lines in order.</summary>
public sealed record RestockSet(int MinCoins, int MaxCoins, IReadOnlyList<RestockLine> Lines);

/// <summary>What a generation draws (0x4CEF00): the coins first, then one percent roll per item line in order.</summary>
public enum RestockRoll { Coins, Line }

/// <summary>The generation's draws from the one generator: an inclusive uniform integer in <c>min..max</c>.</summary>
public interface IRestockRolls
{
    int Roll(RestockRoll purpose, int min, int max);
}

/// <summary>
/// The PC that seeds a generated-once container's draws (0x463E20, RK10): its race and gender (stats 27 and 26 as the stat getter
/// 0x4B0490 gives them), its background (field 0x101, 0x4C2690) and the bytes of its player name (field 0x10E as 0x441B60 copies it for
/// the PC itself; the name ends at its first zero byte, and an unset name is empty).
/// </summary>
public sealed record RestockSeedPc(int Race, int Gender, int Background, IReadOnlyList<byte> PlayerName);

/// <summary>The original generator behind <see cref="IRestockRolls"/> (ARC-WORLD-098): every roll is one range draw (0x4CEF00) of
/// <paramref name="random"/>, so equal bounds draw nothing and the sequence is the original's for the same seed.</summary>
public sealed class OriginalRestockRolls(OriginalRandom random) : IRestockRolls
{
    private readonly OriginalRandom _random = random ?? throw new ArgumentNullException(nameof(random));

    public int Roll(RestockRoll purpose, int min, int max) => _random.Between(min, max);
}

/// <summary>What a generation creates: the coins of the new gold pile (0 is none) and the basic prototype numbers of the items, in the order
/// they are created and moved in.</summary>
public sealed record RestockLoot(int Coins, IReadOnlyList<int> BasicPrototypes);

/// <summary>
/// ARC-WORLD-093: the original's inventory restock (time event type 0x16; 0x4640C0, 0x464150, 0x464180, 0x4641A0, 0x463C60, 0x463E20,
/// 0x463B30, read in the retail 1.0.7.4 executable, ledger "Merchant restock" RK1–RK8; ARC-WORLD-098 the generated-once
/// containers' seed, RK10–RK14). Every opening of the inventory window on an
/// object with an inventory source (re)sets its event 12 to 24 game hours on; when it fires, a living NPC without a PC leader, or a
/// container that regenerates on its own or whose merchant is near and alive, has its inventory cleared (persistent items stay) and
/// generated again from its <c>rules\InvenSource.mes</c> set.
/// </summary>
public static class RestockRules
{
    /// <summary>The original's time event type.</summary>
    public const int EventType = 0x16;
    /// <summary>The shortest restock delay: 12 game hours.</summary>
    public const long MinDelayMs = 43_200_000;
    /// <summary>The longest restock delay: 24 game hours.</summary>
    public const long MaxDelayMs = 86_400_000;
    /// <summary>The item flag (field 0x57) that keeps an item through a regeneration.</summary>
    public const uint PersistentItemFlag = 0x2000;
    /// <summary>The container flag (field 0x38) of a container that regenerates without its merchant and whether it is on screen.</summary>
    public const uint RegeneratesAloneFlag = 0x400;
    /// <summary>The container flag (field 0x38) of a container generated once: its generation draws from a seed of the PC and the
    /// container's place and then clears the flag and its inventory source.</summary>
    public const uint GeneratedOnceFlag = 0x200;
    /// <summary>The percent roll's range.</summary>
    public const int MinPercent = 1, MaxPercent = 100;
    /// <summary>The most item lines a set holds (0x4BF7B0).</summary>
    public const int MaxLines = 300;

    /// <summary>The object's inventory source (0x4641A0): an NPC's or a container's; 0 for any other object.</summary>
    [OriginalFunction(0x4641A0, OriginalFunctionScope.Full)]
    public static int InventorySource(RestockHolder holder) => holder.Kind is RestockHolderKind.Npc or RestockHolderKind.Container ? holder.InventorySource : 0;

    /// <summary>
    /// The delay of a new restock event (0x4640C0): the draw in <see cref="MinDelayMs"/>..<see cref="MaxDelayMs"/> game milliseconds, a
    /// quarter of it (shifted right twice) in a multiplayer game. Only an object with an inventory source gets an event; the new one
    /// replaces the object's pending one (0x45BF10 with the predicate 0x464150), and an object without a source keeps its pending event.
    /// </summary>
    [OriginalFunction(0x4640C0, OriginalFunctionScope.Partial)]
    public static long Delay(long draw, bool multiplayer)
    {
        if (draw is < MinDelayMs or > MaxDelayMs) throw new ArgumentOutOfRangeException(nameof(draw));
        return multiplayer ? draw >> 2 : draw;
    }

    /// <summary>
    /// The firing (0x464180 → 0x463C60). An object without an inventory source does nothing. An NPC regenerates unless it is dead or has a
    /// PC leader. A container regenerates with <see cref="RegeneratesAloneFlag"/>; otherwise one on screen is scheduled again, and one off
    /// screen looks for its <paramref name="merchant"/>: the first NPC of the game-window-sized area around it (0x440FC0, NPCs only) whose
    /// substitute container (0x45F650: no leader, within 20 tiles) it is. Without one, or when that NPC is dead or has a PC leader, nothing
    /// happens; otherwise the merchant is run through the handler again (it regenerates when it has a source of its own) and then the
    /// container regenerates.
    /// </summary>
    [OriginalFunction(0x463C60, OriginalFunctionScope.Partial)]
    public static RestockDecision Fire(RestockHolder holder, RestockHolder? merchant)
    {
        if (!Enum.IsDefined(holder.Kind)) throw new ArgumentOutOfRangeException(nameof(holder));
        if (InventorySource(holder) == 0) return new(RestockStep.None, false);
        if (holder.Kind == RestockHolderKind.Npc)
            return new(holder.Dead || holder.PcLeader ? RestockStep.None : RestockStep.Regenerate, false);
        if ((holder.ContainerFlags & RegeneratesAloneFlag) != 0) return new(RestockStep.Regenerate, false);
        if (holder.OnScreen) return new(RestockStep.Reschedule, false);
        if (merchant is { Kind: not RestockHolderKind.Npc }) throw new ArgumentException("The merchant is an NPC.", nameof(merchant));
        if (merchant is not { } user || user.Dead || user.PcLeader) return new(RestockStep.None, false);
        return new(RestockStep.RegenerateMerchantAndContainer, InventorySource(user) != 0);
    }

    /// <summary>Whether an item stays through the clearing of a regeneration (0x463B30 with its keep argument 1): only an item with
    /// <see cref="PersistentItemFlag"/>; every other entry of the list, worn items and gold included, is removed and destroyed.</summary>
    [OriginalFunction(0x463B30, OriginalFunctionScope.Partial)]
    public static bool Keeps(uint itemFlags) => (itemFlags & PersistentItemFlag) != 0;

    /// <summary>
    /// The seed of a generated-once container's draws (0x463E20 with flag 0x200, RK10): the low 32 bits of the container's location
    /// (its tile x) plus, with a PC (0x40DA50), race × background + gender and, for each pair of name bytes in order, the product of the
    /// two bytes as signed chars (an odd name's last byte pairs with the terminating zero). Every sum and product wraps at 32 bits.
    /// Without a PC the seed is the tile x alone.
    /// </summary>
    public static int GeneratedOnceSeed(RestockSeedPc? pc, int locationX)
    {
        if (pc is null) return locationX;
        ArgumentNullException.ThrowIfNull(pc.PlayerName);
        var length = 0;
        while (length < pc.PlayerName.Count && pc.PlayerName[length] != 0) length++;
        var seed = unchecked(pc.Gender + locationX + pc.Race * pc.Background);
        for (var i = 0; i < length; i += 2)
            seed = unchecked(seed + (sbyte)pc.PlayerName[i] * (i + 1 < length ? (sbyte)pc.PlayerName[i + 1] : 0));
        return seed;
    }

    /// <summary>A generated-once container's generation (0x463E20 with flag 0x200, RK10, RK11): <see cref="Generate"/> drawing from the
    /// original generator seeded with <paramref name="seed"/> (<see cref="GeneratedOnceSeed"/>). The clear before it, and the clearing of
    /// the container's source and flag 0x200 after it, are the caller's.</summary>
    public static RestockLoot GenerateOnce(RestockSet set, int seed) => Generate(set, new OriginalRestockRolls(new OriginalRandom(seed)));

    /// <summary>
    /// The generation of a regeneration (0x463E20) after the clearing: the coins are one draw in the set's range (0x4CEF00 swaps reversed
    /// bounds) and become a new gold pile only when above 0 (0x464830); then each line in order draws a percent and creates its item when the
    /// draw is at most the rate (so a rate of 100 or more always, 0 or less never). A created item that the holder refuses is destroyed
    /// (0x4617F0, then 0x43CCA0): the caller's. An NPC then wields its best items (0x4654F0): the caller's.
    /// </summary>
    [OriginalFunction(0x463E20, OriginalFunctionScope.Partial)]
    public static RestockLoot Generate(RestockSet set, IRestockRolls rolls)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(set.Lines);
        ArgumentNullException.ThrowIfNull(rolls);
        if (set.Lines.Count > MaxLines) throw new ArgumentException("A set holds at most 300 lines.", nameof(set));
        var (low, high) = set.MinCoins <= set.MaxCoins ? (set.MinCoins, set.MaxCoins) : (set.MaxCoins, set.MinCoins);
        var coins = rolls.Roll(RestockRoll.Coins, low, high);
        if (coins < low || coins > high) throw new InvalidOperationException("The coin draw left its range.");
        var created = new List<int>();
        foreach (var line in set.Lines)
        {
            var percent = rolls.Roll(RestockRoll.Line, MinPercent, MaxPercent);
            if (percent is < MinPercent or > MaxPercent) throw new InvalidOperationException("The percent draw left its range.");
            if (percent <= line.Rate) created.Add(line.BasicPrototype);
        }
        return new(Math.Max(coins, 0), created.AsReadOnly());
    }
}
