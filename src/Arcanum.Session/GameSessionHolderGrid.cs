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
}
