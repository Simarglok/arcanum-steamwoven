using System.Collections.Immutable;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// The ten global AI action lists (0x4CC240 → 0x4CC350 → 0x4CC380, built once at init): for each list, every spell 0..222 with a
/// value other than −1, sorted highest value first by the runtime's quicksort (<see cref="AiActionLists.Quicksort{T}"/>) with the
/// comparator 0x4CC2D0. The sort is not stable, so spells of equal value come out in the order that algorithm makes of the
/// number order.
/// </summary>
public sealed class AiGlobalLists
{
    /// <summary>Spell records.</summary>
    public const int Spells = 223;

    /// <summary>Action lists.</summary>
    public const int Lists = 10;

    private readonly ImmutableArray<AiSpellValues> _values;
    private readonly ImmutableArray<ImmutableArray<int>> _lists;

    private AiGlobalLists(ImmutableArray<AiSpellValues> values, ImmutableArray<ImmutableArray<int>> lists) => (_values, _lists) = (values, lists);

    /// <summary>The lists of <paramref name="spells"/> (exactly <see cref="Spells"/> records, in spell number order).</summary>
    [OriginalFunction(0x4CC240, OriginalFunctionScope.Partial)]
    public static AiGlobalLists Build(IReadOnlyList<AiSpellValues> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        if (spells.Count != Spells || spells.Any(s => s is null)) throw new ArgumentException($"The global lists need {Spells} spell records.", nameof(spells));
        var values = spells.ToImmutableArray();
        var lists = Enumerable.Range(0, Lists).Select(list => Sorted(values, (AiActionList)list)).ToImmutableArray();
        return new(values, lists);
    }

    /// <summary>The list's spells, highest value first.</summary>
    public ImmutableArray<int> this[AiActionList list] => _lists[Index(list)];

    /// <summary>A spell's value for a list (0x4CC310): −1 for a spell outside 0..222.</summary>
    [OriginalFunction(0x4CC310, OriginalFunctionScope.Partial)]
    public int Value(int spell, AiActionList list) => spell is >= 0 and < Spells ? _values[spell][(AiActionList)Index(list)] : -1;

    /// <summary>A spell's second defensive value (0x4CC2A0); a spell outside 0..222 has 0 (the original reads out of bounds there,
    /// SP18, which the chooser never asks).</summary>
    [OriginalFunction(0x4CC2A0, OriginalFunctionScope.Partial)]
    public int DefensiveSecond(int spell) => spell is >= 0 and < Spells ? _values[spell].DefensiveSecond : 0;

    // 0x4CC380: the spells in number order with a value, then the runtime's quicksort with 0x4CC2D0.
    [OriginalFunction(0x4CC380, OriginalFunctionScope.Partial)]
    private static ImmutableArray<int> Sorted(ImmutableArray<AiSpellValues> values, AiActionList list)
    {
        var spells = Enumerable.Range(0, Spells).Where(s => values[s][list] != -1).ToArray();
        AiActionLists.Quicksort(spells, (a, b) => AiActionLists.CompareValues(values[a][list], values[b][list]));
        return [.. spells];
    }

    private static int Index(AiActionList list) =>
        (int)list is >= 0 and < Lists ? (int)list : throw new ArgumentOutOfRangeException(nameof(list), list, "An undefined AI action list.");
}

/// <summary>
/// The NPC AI's action lists (stage A5, research ARC-RES-059; slice I8, ARC-WORLD-088, re-read in the retail 1.0.7.4 executable):
/// the runtime's sort of the global lists, a critter's list build (0x4CC580's build half) and the item-use test of the chooser.
/// The 40-slot cache of the Original setting is <see cref="AiListCache"/>.
/// </summary>
public static class AiActionLists
{
    /// <summary>A critter's list holds at most this many entries (0x4CCAD0, 0x4CCB10, 0x4CCB50); more are dropped.</summary>
    public const int MaxEntries = 200;

    /// <summary>Spell slots of an item (fields 0x64..0x68).</summary>
    public const int ItemSpellSlots = 5;

    /// <summary>The runtime's part size at or below which a selection sort runs instead of partitioning.</summary>
    public const int ShortSortSize = 8;

    /// <summary>Items come first in combat mode at or below this fatigue percent.</summary>
    public const int ItemsFirstFatigue = 50;

    /// <summary>
    /// The comparator 0x4CC2D0 on two values: 1 when the first is smaller, 0 when equal, −1 when larger, so the sort puts the highest
    /// value first.
    /// </summary>
    [OriginalFunction(0x4CC2D0, OriginalFunctionScope.Partial)]
    public static int CompareValues(int first, int second) => first < second ? 1 : first == second ? 0 : -1;

    /// <summary>
    /// The runtime's quicksort (0x58A548): the middle element swapped to the front as the pivot, the partition's two scans
    /// (<c>≤ 0</c> from the left, <c>≥ 0</c> from the right), the pivot swapped into place, the larger part pushed and the smaller
    /// one sorted next; parts of <see cref="ShortSortSize"/> or fewer elements by the selection sort 0x58A69C, which moves the first
    /// maximum of the part to its end. Not stable.
    /// </summary>
    [OriginalFunction(0x58A548, OriginalFunctionScope.Full)]
    public static void Quicksort<T>(T[] items, Comparison<T> compare)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(compare);
        if (items.Length < 2) return;
        var stack = new Stack<(int Lo, int Hi)>();
        int lo = 0, hi = items.Length - 1;
        while (true)
        {
            var size = hi - lo + 1;
            var next = false;
            if (size <= ShortSortSize) ShortSort(items, lo, hi, compare);
            else
            {
                Swap(items, lo + size / 2, lo);
                int left = lo, right = hi + 1;
                while (true)
                {
                    do left++; while (left <= hi && compare(items[left], items[lo]) <= 0);
                    do right--; while (right > lo && compare(items[right], items[lo]) >= 0);
                    if (right < left) break;
                    Swap(items, left, right);
                }
                Swap(items, lo, right);
                if (right - 1 - lo >= hi - left)
                {
                    if (lo + 1 < right) stack.Push((lo, right - 1));
                    if (left < hi) { lo = left; next = true; }
                }
                else
                {
                    if (left < hi) stack.Push((left, hi));
                    if (lo + 1 < right) { hi = right - 1; next = true; }
                }
            }
            if (next) continue;
            if (stack.Count == 0) return;
            (lo, hi) = stack.Pop();
        }
    }

    // 0x58A69C: from the whole part down to two elements, the first maximum moved to the end.
    [OriginalFunction(0x58A69C, OriginalFunctionScope.Full)]
    private static void ShortSort<T>(T[] items, int lo, int hi, Comparison<T> compare)
    {
        for (; hi > lo; hi--)
        {
            var max = lo;
            for (var p = lo + 1; p <= hi; p++)
                if (compare(items[p], items[max]) > 0) max = p;
            Swap(items, max, hi);
        }
    }

    private static void Swap<T>(T[] items, int a, int b) => (items[a], items[b]) = (items[b], items[a]);

    /// <summary>
    /// A critter's list, built fresh (0x4CC580 from its build step): only for a PC or NPC. In combat mode at or below
    /// <see cref="ItemsFirstFatigue"/> % fatigue the items, and the spells only when no item entry results; otherwise the spells, and
    /// the items only when no spell entry results. Spells (0x4CC8A0): with magick points above 0, the global list's spells the critter
    /// knows, in the global order. Items (0x4CC810, 0x4CC930): the hexed wielded weapon alone, else the inventory in order; per item,
    /// with a mana store and the identification rule (0x4CCA30: identified, no PC leader, or exempt), each spell slot with a spell that
    /// has a value for the list and that the critter may use, in slot order; then, independently, an item with the valid-AI-action flag
    /// and this AI action gives an <see cref="AiListEntry.ItemAction"/> entry. At most <see cref="MaxEntries"/> entries.
    /// </summary>
    [OriginalFunction(0x4CC580, OriginalFunctionScope.Partial)]
    public static ImmutableArray<AiListEntry> Build(IAiSupportWorld world, string critter, AiActionList list)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(critter);
        if (world.Find(critter) is not { Kind: HostilityKind.Pc or HostilityKind.Npc } c) return [];
        var support = world.Support(critter);
        var entries = ImmutableArray.CreateBuilder<AiListEntry>();
        var itemsFirst = c.InCombatMode && support.FatiguePercent <= ItemsFirstFatigue;
        if (itemsFirst) Items(world, c, list, entries); else Spells(world, c, support, list, entries);
        if (entries.Count == 0)
        {
            if (itemsFirst) Spells(world, c, support, list, entries); else Items(world, c, list, entries);
        }
        return entries.ToImmutable();
    }

    // 0x4CC8A0.
    [OriginalFunction(0x4CC8A0, OriginalFunctionScope.Full)]
    private static void Spells(IAiSupportWorld world, HostilityCritter c, AiSupportCritter support, AiActionList list, ImmutableArray<AiListEntry>.Builder entries)
    {
        if (support.MagickPoints <= 0) return;
        foreach (var spell in world.GlobalLists[list])
            if (world.Knows(c.Id, spell)) Add(entries, new(spell, null));
    }

    // 0x4CC810: the hexed weapon alone, else every inventory item.
    [OriginalFunction(0x4CC810, OriginalFunctionScope.Full)]
    private static void Items(IAiSupportWorld world, HostilityCritter c, AiActionList list, ImmutableArray<AiListEntry>.Builder entries)
    {
        if (world.HexedWeapon(c.Id) is { } hexed) { Item(world, c, hexed, list, entries); return; }
        foreach (var item in world.Inventory(c.Id)) Item(world, c, item, list, entries);
    }

    // 0x4CC930.
    [OriginalFunction(0x4CC930, OriginalFunctionScope.Full)]
    private static void Item(IAiSupportWorld world, HostilityCritter c, string item, AiActionList list, ImmutableArray<AiListEntry>.Builder entries)
    {
        var facts = world.Item(item);
        if (facts.ManaStore != 0 && Identifies(facts, c))
        {
            for (var slot = 0; slot < ItemSpellSlots && !facts.Spells.IsDefault && slot < facts.Spells.Length; slot++)
            {
                var spell = facts.Spells[slot];
                if (spell != -1 && world.GlobalLists.Value(spell, list) != -1 && world.CanUseItem(c.Id, item)) Add(entries, new(spell, item));
            }
        }
        if (facts.ValidAiAction && facts.AiAction == (int)list) Add(entries, new(AiListEntry.ItemAction, item));
    }

    /// <summary>
    /// The identification rule (0x4CCA30): an identified item, any item of a critter without a PC leader, and an exempt item (food or
    /// a scroll) may be listed. Both settings use the critter's current PC leader (the Original cache's hit path builds with a
    /// request field no cache function sets, SP19).
    /// </summary>
    [OriginalFunction(0x4CCA30, OriginalFunctionScope.Partial)]
    public static bool Identifies(AiListItem item, HostilityCritter critter)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(critter);
        return item.Identified || critter.PcLeader is null || item.IdentificationExempt;
    }

    /// <summary>
    /// The chooser's item-use test (0x4CC160): an item with a mana store whose first spell slot is empty, whose first spell has item
    /// triggers, or that has the valid-AI-action flag is used (pass action 2); any other item's spell is cast with it (action 1).
    /// </summary>
    [OriginalFunction(0x4CC160, OriginalFunctionScope.Full)]
    public static bool UsesItem(AiListItem item, Func<int, AiSpellFacts> spell)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(spell);
        if (item.ManaStore == 0) return false;
        var first = item.Spells.IsDefaultOrEmpty ? -1 : item.Spells[0];
        return first == -1 || spell(first).ItemTriggers || item.ValidAiAction;
    }

    private static void Add(ImmutableArray<AiListEntry>.Builder entries, AiListEntry entry)
    {
        if (entries.Count < MaxEntries) entries.Add(entry);
    }
}

/// <summary>One slot of the Original setting's list cache: the critter and its ten lists (empty where none was stored).</summary>
public sealed record AiListCacheSlot(string Critter, ImmutableArray<ImmutableArray<AiListEntry>> Lists);

/// <summary>A list request's answer: the entries and the cache after it.</summary>
public sealed record AiListRequest(ImmutableArray<AiListEntry> Entries, AiListCache Cache);

/// <summary>
/// The list cache of the Original setting (SP11, <see cref="CombatAnomalyProfile.AiListCacheKeepsFirstList"/>; 0x4CC580, 0x4CC6F0,
/// 0x4CC770, 0x4CCB90, 0x4CCC00, 0x4CCC40, 0x4CC4C0): forty slots keyed by critter. A request without a slot takes a free slot,
/// else the next one in round-robin order (its lists dropped), and builds the requested list into it when the build is not empty;
/// a request with a slot uses the slot's list when it is not empty, however old, and otherwise builds the list for itself only. So
/// a slot ever holds only the list requested when it was taken. Transient host state, never saved: a load starts with
/// <see cref="Empty"/>. The Steamwoven setting builds every list fresh and leaves the cache as it is.
/// </summary>
public sealed class AiListCache
{
    /// <summary>Slots.</summary>
    public const int Slots = 40;

    private AiListCache(ImmutableArray<AiListCacheSlot?> slots, int nextReuse) => (Table, NextReuse) = (slots, nextReuse);

    /// <summary>No slot taken (the reset 0x4CC4C0, also the start of a game).</summary>
    public static AiListCache Empty { get; } = new([.. new AiListCacheSlot?[Slots]], 0);

    public ImmutableArray<AiListCacheSlot?> Table { get; }

    /// <summary>The slot the next request without a free slot takes (0x6016FC).</summary>
    public int NextReuse { get; }

    /// <summary>The list request (0x4CCA90): fresh in the Steamwoven setting, through the cache in the Original one.</summary>
    [OriginalFunction(0x4CCA90, OriginalFunctionScope.Partial)]
    public AiListRequest Request(IAiSupportWorld world, string critter, AiActionList list, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(profile);
        var index = (int)list is >= 0 and < AiGlobalLists.Lists ? (int)list : throw new ArgumentOutOfRangeException(nameof(list));
        if (!profile.AiListCacheKeepsFirstList || world.Find(critter) is not { Kind: HostilityKind.Pc or HostilityKind.Npc })
            return new(AiActionLists.Build(world, critter, list), this);
        var at = Find(critter);
        if (at >= 0)
        {
            var kept = Table[at]!.Lists[index];
            return new(kept.IsEmpty ? AiActionLists.Build(world, critter, list) : kept, this);
        }
        var built = AiActionLists.Build(world, critter, list);
        var free = Table.IndexOf(null);
        var reuse = free >= 0 ? NextReuse : (NextReuse + 1) % Slots;
        var slot = free >= 0 ? free : NextReuse;
        var lists = Enumerable.Repeat(ImmutableArray<AiListEntry>.Empty, AiGlobalLists.Lists).ToImmutableArray().SetItem(index, built);
        return new(built, new(Table.SetItem(slot, new AiListCacheSlot(critter, lists)), reuse));
    }

    /// <summary>The critter's slot dropped (0x4CCC00: an item inserted into or force-removed from it).</summary>
    [OriginalFunction(0x4CCC00, OriginalFunctionScope.Full)]
    public AiListCache Clear(string critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        var at = Find(critter);
        return at < 0 ? this : new(Table.SetItem(at, null), NextReuse);
    }

    /// <summary>The critter's slot dropped when one of its lists holds the item (0x4CCC40: a listed item's mana store ran out).</summary>
    [OriginalFunction(0x4CCC40, OriginalFunctionScope.Full)]
    public AiListCache ItemExhausted(string critter, string item)
    {
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(item);
        var at = Find(critter);
        return at >= 0 && Table[at]!.Lists.Any(l => l.Any(e => e.Item == item)) ? new(Table.SetItem(at, null), NextReuse) : this;
    }

    private int Find(string critter)
    {
        for (var i = 0; i < Slots; i++)
            if (Table[i]?.Critter == critter) return i;
        return -1;
    }
}
