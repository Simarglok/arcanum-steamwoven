using Arcanum.Simulation.Items;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-RES-1410 (ledger XR16 (g), IW39): the arrangement the original's inventory window writes at its open on another holder (0x5727B0 near its
    /// end calls 0x4670A0 with layout 0, row by row, on the shown object, then builds the window's grid again). It runs in the loot (mode 2), steal
    /// (mode 3) and See Contents (mode 4) windows when the barter flag is clear, the shown object is no PC and no other window has it open; the host
    /// decides the mode and calls this once per open. The holder is a container, a dead or living NPC of the actor's map's seed (its list as
    /// <see cref="QueryMapInventory"/> gives it) or a junk pile the session made. The whole list is laid out again (<see cref="InventoryArrangeRules.Arrange"/>:
    /// the worn and quick-slot items stay out and keep their locations, the rest are sorted by height, then width, largest first, and take the first free
    /// place of an empty grid, 96 rows for a container, 12 for a critter): a taken item's old cell closes up at the next open, and items the map's data
    /// places in an order of its own take the sorted one. When an item finds no place nothing changes (<see cref="SessionGridArrangement.NoRoom"/>, the
    /// original writes nothing). The slots are written straight (no insert check, no weight, no script event, no action points, IW40): a seed holder's in
    /// the inventory's map of holder slots (saved with the inventory section, ARC-RES-1441; at most <c>MaxHolderSlots</c> holders, past that nothing is written), a junk
    /// pile's in its record. Refused before any effect: an invalid entity or a holder the actor's map does not have (<see cref="GameSessionFailure.UnknownHolder"/>),
    /// a stale version, the revision bound. One revision when some slot changed. PROJECT: the list order is the session's list order (the seed's, then the
    /// made items added); the multiplayer branch (0x4EDE80) is not built; the open table test (no other window on the object) always passes in a single-player
    /// session, so it is not modelled.
    /// </summary>
    public SessionGridArrangement ArrangeHolderGrid(GameSessionVersion expected, string holder)
    {
        ThrowIfDisposed();
        SessionGridArrangement Rejected(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, false, 0, Version);
        if (!SessionValidation.Entity(holder)) return Rejected(GameSessionFailure.UnknownHolder);
        if (!IsCurrent(expected)) return Rejected(GameSessionFailure.StaleVersion);
        var map = _actor.Map;
        List<InventoryArrangeItem> list;
        int rows;
        SessionJunkPile? pile = _inventory.Objects.Pile(holder);
        if (pile is not null)
        {
            if (pile.Map != map) return Rejected(GameSessionFailure.UnknownHolder);
            rows = InventoryGridRules.ContainerRows;
            list = pile.Items.Select(item => new InventoryArrangeItem(item.Entity, item.Slot, item.Width, item.Height)).ToList();
        }
        else
        {
            if (QueryMapInventory(map)?.Holders.FirstOrDefault(listed => listed.Entity == holder) is not { } found) return Rejected(GameSessionFailure.UnknownHolder);
            rows = found.Kind == SessionHolderKind.Container ? InventoryGridRules.ContainerRows : InventoryGridRules.CritterRows;
            list = [];
            foreach (var item in found.Items)
            {
                var (width, height) = GridSize(item.Entity);
                list.Add(new(item.Entity, item.Facts.InventoryLocation, width, height));
            }
        }
        // A list with a side below one cell cannot be laid out (the hero's grid refuses it the same way through the art source's clamp); nothing is written.
        if (list.Any(item => item.Width < 1 || item.Height < 1)) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, 0, Version);
        var placements = InventoryArrangeRules.Arrange(list, rows, InventoryArrangeLayout.Horizontal);
        if (placements is null) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, 0, Version);
        var locations = list.ToDictionary(item => item.Key, item => item.Location, StringComparer.Ordinal);
        var moved = placements.Count(placement => locations[placement.Key] != placement.Slot);
        if (moved == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, false, 0, Version);
        if (_revision == long.MaxValue) return Rejected(GameSessionFailure.RevisionExhausted);
        // ARC-RES-1441: a save keeps the slots of so many holders (<c>SessionInventoryState.MaxHolderSlots</c>); past that bound the layout stays the map data's (nothing is written).
        if (pile is null && !_inventory.HolderSlots.ContainsKey(holder) && _inventory.HolderSlots.Count >= SessionInventoryState.MaxHolderSlots) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, 0, Version);
        if (pile is not null)
        {
            var slots = placements.ToDictionary(placement => placement.Key, placement => placement.Slot, StringComparer.Ordinal);
            _inventory.Objects.ReplacePile(pile with { Items = Array.AsReadOnly(pile.Items.Select(item => item with { Slot = slots[item.Entity] }).ToArray()) });
        }
        else
        {
            // The arranged items replace the holder's earlier record; the items left out (worn, quick slots) keep their locations.
            _inventory.HolderSlots[holder] = new(placements.ToDictionary(placement => placement.Key, placement => placement.Slot, StringComparer.Ordinal), StringComparer.Ordinal);
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, false, moved, Version);
    }

    /// <summary>
    /// ARC-AI-1654 (the room test of the insert check 0x466510, its answer code 2): whether the holder's grid has a place for <paramref name="item"/>, an NPC's or a container's, as the list its inventory
    /// window shows (<see cref="QueryHolderItems"/>) lies now. An ammunition item whose prototype another item of an NPC's list shares needs no place (the stack field 0x462410 and the same-prototype
    /// finder 0x462930 join it to that stack: AM1); every other item needs a free rectangle of its size (<see cref="QueryItemCells"/>: 10 columns by 12 rows for an NPC, 96 for a container)
    /// at the first free place of the grid the list's items fill (<see cref="InventoryGridRules.HolderFreeSlot"/>: each item marks its rectangle from its location, seed data or the slot
    /// an arrangement gave it, <see cref="ArrangeHolderGrid"/>; the worn and quick-slot items take no cell; an item with no place, a made, given or picked-up one, takes the first free one
    /// in list order). A holder no attached seed lists has an empty grid (a junk pile the session made is none: it is not covered). The item's own entry in the list, when it is already there, is left out. PROJECT: gold and keys are not admitted by the lists
    /// (a key going onto a key ring needs no place, KY3); the item's size is the session's (art first, else the seed row's, else 1 × 1).
    /// </summary>
    public bool QueryHolderRoom(string holder, SessionInventoryItem item)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(holder)) throw new ArgumentException("Invalid entity id.", nameof(holder));
        ArgumentNullException.ThrowIfNull(item);
        var kind = item.Facts.Kind;
        var container = _inventory.Seeds.Values.Any(seed => seed.Holders.TryGetValue(holder, out var row) && row.Kind == SessionHolderKind.Container);
        // 0x462930 finds another item of the list with the item's prototype (never the item itself).
        if (kind.Type == SessionItemType.Ammo && (container ? ContainerStacks(holder) : NpcStacks(holder)).Any(pair => pair.Prototype == kind.Prototype && pair.Stack.Entity != item.Entity)) return true;
        return HolderFreeSlotFor(holder, item, container) >= 0;
    }

    // The first free place of the holder's grid for the item (InventoryGridRules.HolderFreeSlot over the list without the item's own entry), -1 when none.
    private int HolderFreeSlotFor(string holder, SessionInventoryItem item, bool container)
    {
        var list = (QueryHolderItems(holder) ?? []).Where(listed => listed.Entity != item.Entity)
            .Select(listed => { var (listedWidth, listedHeight) = GridSize(listed.Entity); return new InventoryGridRules.HolderListItem(listed.Facts.InventoryLocation, listedWidth, listedHeight); }).ToArray();
        var (width, height) = GridSize(item.Entity);
        return InventoryGridRules.HolderFreeSlot(list, container ? InventoryGridRules.ContainerRows : InventoryGridRules.CritterRows, width, height);
    }

    /// <summary>
    /// ARC-AI-1698 (0x466640 after the room test 0x466510 → 0x4664C0, ledger NL8): the original's insert writes the free place the room test found into the inserted item's inventory location
    /// (field 0x5E), so the item keeps it whatever else leaves the list. Called right after an item entered an NPC's list as an item of its own (not merged into an ammunition stack): the first free place of
    /// the grid as the list lies now (<see cref="HolderFreeSlotFor"/>, the same as <see cref="QueryHolderRoom"/> searches) goes to the inventory's map of holder slots (saved with the inventory section,
    /// schema 95, no schema step; the wear record of an NPC drops the slot of an item it puts on), where <see cref="SessionInventoryState.Current"/> reads it for the list's location. No place found, a worn item, a gold pile or a key (the original's insert folds those into the coin count and the key ring, no list place), or a save already keeping the slots of
    /// <see cref="SessionInventoryState.MaxHolderSlots"/> other holders leaves the item without one (the earlier first-free-place derivation answers for it). Writes no revision (the caller's insert holds the one).
    /// </summary>
    private void StoreInsertPlace(string npc, string item)
    {
        if (QueryHolderItems(npc)?.FirstOrDefault(listed => listed.Entity == item) is not { } listed || listed.Facts.Worn || listed.Facts.Kind.Type is SessionItemType.Gold or SessionItemType.Key) return;
        if (!_inventory.HolderSlots.TryGetValue(npc, out var slots) && _inventory.HolderSlots.Count >= SessionInventoryState.MaxHolderSlots) return;
        var slot = HolderFreeSlotFor(npc, listed, container: false);
        if (slot < 0) return;
        if (slots is null) _inventory.HolderSlots[npc] = slots = new(StringComparer.Ordinal);
        slots[item] = slot;
    }
}
