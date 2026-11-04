using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>An item in a junk pile (ARC-WORLD-063): its entity, its slot in the pile's grid (row × 10 + column) and its size in cells.</summary>
public sealed record SessionJunkPileItem(string Entity, int Slot, int Width, int Height)
{
    internal InventoryGridItem Grid => new(Slot, Width, Height);
}

/// <summary>
/// A junk pile the session made (ARC-WORLD-063; the original's container of prototype 3043, description 3023): its entity
/// (<c>junk:n</c>), map and cell, and its items in the pile's order. An item in a pile still lies on the pile's cell in its own
/// ground record (a placed item, a dropped or spilled seed item, a seed item of that cell).
/// </summary>
public sealed record SessionJunkPile(string Entity, SessionMapId Map, CellCoord Cell, IReadOnlyList<SessionJunkPileItem> Items)
{
    /// <summary>The junk piles the session holds at once (project bound; the original has none).</summary>
    public const int MaxPiles = 4096;
    /// <summary>The items all junk piles hold together (project bound).</summary>
    public const int MaxItems = 8192;
    /// <summary>The entity prefix of a pile the session made.</summary>
    public const string EntityPrefix = "junk:";

    /// <summary>The pile's number in its entity, or null when the entity is not a pile's.</summary>
    public static int? Number(string? entity) =>
        entity is not null && entity.StartsWith(EntityPrefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(EntityPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == EntityPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;
}

/// <summary>A junk pile of one map as <see cref="SessionMapInventory.JunkPiles"/> lists it: its entity, cell and items.</summary>
public sealed record SessionJunkPileInventory(string Entity, CellCoord Cell, IReadOnlyList<SessionInventoryItem> Items);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-063: the drop's placement (0x466E50 → 0x466EF0, ledger JP1–JP3): an item the session laid on the ground at
    /// <paramref name="cell"/> of <paramref name="map"/> joins the first of the cell's junk piles with room for it, or, when loose items
    /// lie there, a new pile is made from them (each that fits, in ground order; a loose item whose get or insert-item script the
    /// session would have to run stays on the ground) and the item joins it when it fits; else it stays loose. The drop's caller then
    /// schedules its decay. <paramref name="notYetDropped"/> are items the caller lays on the ground after this one (a spill drops
    /// its items one by one). The session runs no script here: an item with an insert-item script stays loose and makes no pile
    /// (Not ready). Beyond <see cref="SessionJunkPile.MaxPiles"/> or <see cref="SessionJunkPile.MaxItems"/> nothing is piled (project bound).
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x466E50, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    internal void SettleDrop(string entity, SessionMapId map, CellCoord cell, IReadOnlySet<string>? notYetDropped = null)
    {
        if (_inventory.Objects.PileOf(entity) is not null || Scripted(entity, SessionScriptPoint.InsertItem)) return;
        var (width, height) = GridSize(entity);
        var piles = _inventory.Objects.Piles.Where(pile => pile.Map == map && pile.Cell == cell).ToArray();
        var loose = LooseAt(map, cell).Where(item => item != entity && notYetDropped?.Contains(item) != true).ToArray();
        var placement = JunkPileRules.Place(
            piles.Select(pile => (IReadOnlyList<bool>)InventoryGridRules.Occupied(pile.Items.Select(item => item.Grid), InventoryGridRules.ContainerRows)).ToArray(),
            loose.Select(item => { var (w, h) = GridSize(item); return new JunkPileLooseItem(w, h, !Scripted(item, SessionScriptPoint.Get) && !Scripted(item, SessionScriptPoint.InsertItem)); }).ToArray(),
            width, height);
        switch (placement.Kind)
        {
            case JunkPilePlacementKind.JoinPile:
                if (_inventory.Objects.PileItems >= SessionJunkPile.MaxItems) return;
                var pile = piles[placement.Pile];
                _inventory.Objects.ReplacePile(pile with { Items = Array.AsReadOnly([.. pile.Items, new(entity, placement.Slot, width, height)]) });
                break;
            case JunkPilePlacementKind.NewPile:
                var items = new List<SessionJunkPileItem>();
                for (var i = 0; i < loose.Length; i++)
                    if (placement.Swept[i] >= 0) { var (w, h) = GridSize(loose[i]); items.Add(new(loose[i], placement.Swept[i], w, h)); }
                if (placement.Slot >= 0) items.Add(new(entity, placement.Slot, width, height));
                // The original leaves a new pile that took nothing standing; so does the session (an empty pile).
                if (CreateJunkPile(map, cell, items) is null) return;
                // The swept items keep their decay events: in a junk pile an item still decays, so the pickup's cancel leaves them (ID4).
                break;
        }
    }

    /// <summary>
    /// ARC-WORLD-063: the check after an item left a junk pile (0x463540, ledger JP4): a pile left with no item is removed; one left
    /// with one item is removed and that item is dropped again on the pile's cell (<see cref="SettleDrop"/>, then its decay starts
    /// over, two days on); a pile with more stays. No revision of its own: the caller's change carries it.
    /// </summary>
    internal void DissolveJunkPile(SessionJunkPile pile)
    {
        var step = JunkPileRules.Dissolve(pile.Items.Count);
        if (step == JunkPileDissolve.Keep) return;
        if (_inventory.Objects.Pile(pile.Entity) is null) throw new InvalidOperationException("JUNK_PILE_GONE");
        DestroyObject(pile.Entity);
        if (step == JunkPileDissolve.Release)
        {
            SettleDrop(pile.Items[0].Entity, pile.Map, pile.Cell);
            ScheduleDecay(pile.Items[0].Entity, pile.Map);
        }
    }

    /// <summary>ARC-WORLD-063: the junk piles the session made, on every map, in the order they were made.</summary>
    public IReadOnlyList<SessionJunkPile> QueryJunkPiles() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Objects.Piles.ToArray()); }

    /// <summary>
    /// ARC-WORLD-063: the host's loot window closes on a junk pile of the actor's map (0x573440 → 0x4ED6C0 → 0x463540, ledger JP4,
    /// JP5): a pile left with no item is removed, one left with one item is removed and the item is dropped again on its cell with a
    /// new decay event. While the window shows the pile, taking items from it (<see cref="LootItem"/>) leaves it standing, as in the
    /// original. One revision when the pile went; an unknown pile or one on another map is <see cref="GameSessionFailure.UnknownHolder"/>.
    /// </summary>
    public GameSessionMutationResult CloseJunkPile(GameSessionVersion expected, string pile)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(pile)) return new(GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder, Version);
        if (!IsCurrent(expected)) return new(GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion, Version);
        if (_inventory.Objects.Pile(pile) is not { } found || found.Map != _actor.Map) return new(GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder, Version);
        if (JunkPileRules.Dissolve(found.Items.Count) == JunkPileDissolve.Keep) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        if (_revision == long.MaxValue) return new(GameSessionDisposition.Rejected, GameSessionFailure.RevisionExhausted, Version);
        DissolveJunkPile(found);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }

    // Loot from a junk pile of the actor's map (0x461CA0 from a container, ledger JP6): the item leaves the pile for the PC's list as
    // a loot from a holder does (the recipient's transfer, the item's remove-item and insert-item scripts through the host), and its
    // decay ends; a placed item goes back to the list it came from. The pile stays while the host's loot window shows it
    // (CloseJunkPile).
    private InventoryMutationResult LootJunkPile(SessionJunkPile pile, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        // The window shows the pile while it is looted, so a destroy inside the loot (an ammunition stack that merged) leaves it standing.
        var outer = _lootedPile;
        _lootedPile = pile.Entity;
        try { return LootJunkPileItem(pile, item, insertCheck, scripts); }
        finally { _lootedPile = outer; }
    }

    private InventoryMutationResult LootJunkPileItem(SessionJunkPile pile, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        if (pile.Items.FirstOrDefault(entry => entry.Entity == item) is not { } entry) return RejectItem(GameSessionFailure.ItemNotAvailable);
        InventoryMutationResult result;
        if (_inventory.PlacedItem(item) is { } placed)
        {
            if (placed.Map != pile.Map || placed.Cell != pile.Cell) return RejectItem(GameSessionFailure.ItemNotAvailable);
            result = TakePlaced(placed, insertCheck);
        }
        else
        {
            if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
            // The item must still lie on the pile's cell (a host's map change may have moved it).
            if (!seed.Items.TryGetValue(item, out var row) || _inventory.Taken.Contains(item) || LooseCell(_actor.Map, seed, item) != pile.Cell)
                return RejectItem(GameSessionFailure.ItemNotAvailable);
            // In the pile the item lies in its slot, not in the worn slot it may have been dropped from.
            result = Take(seed, new(item, _inventory.WithAmmo(item, row.Facts with { InventoryLocation = entry.Slot })), pile.Entity, insertCheck, scripts);
        }
        if (result.Disposition == GameSessionDisposition.Changed && result.Item is not null)
        {
            // An ammunition stack that merged was deleted, which already took it out of its pile.
            _inventory.Objects.LeaveItem(item);
            CancelDecay(item);
        }
        return result;
    }

    // The items lying loose on a cell, in ground order (the session's order; the original's object list order was not read): the
    // map's seed items, spilled and dropped items when its seed is attached, then the hero's placed items; none in a junk pile or in
    // flight.
    private IReadOnlyList<string> LooseAt(SessionMapId map, CellCoord cell)
    {
        IEnumerable<SessionGroundItem> ground = _inventory.Seeds.TryGetValue(map, out var seed)
            ? _inventory.WithPiled(map, seed, KilledNpc, holder => SpillCell(map, seed, holder), item => DroppedCell(map, seed, item)).Ground
            : _inventory.Placed.Where(item => item.Map == map).Select(item => new SessionGroundItem(new(item.Entity, InventoryDomain.MadeFacts(item.Kind)), item.Cell));
        // ARC-COMBAT-017: a thrown item in flight is hidden (object flag 0x2), which the tile's object list skips.
        return ground.Where(item => item.Cell == cell && _inventory.Objects.PileOf(item.Item.Entity) is null && !ItemInFlight(item.Item.Entity))
            .Select(item => item.Item.Entity).ToArray();
    }

    // An item's size in inventory cells (0x461780): the frame of its inventory art in 32-pixel cells when the attached art source answers
    // for it (ARC-UI-055, AttachItemArtSizes); else from the facts of the attached seed that lists it; else 1 × 1 (the original's size
    // when the art does not read).
    private (int Width, int Height) GridSize(string entity)
    {
        var row = SeedRow(entity);
        if (ArtCells(entity, row) is { } cells) return cells;
        return row is not null ? (row.Facts.InventoryWidth, row.Facts.InventoryHeight) : (1, 1);
    }

    // Whether the item's attached seed row shows the script point (unknown, so false, for a kit or made item).
    private bool Scripted(string entity, int point) => SeedRow(entity)?.Facts.HasScript(point) == true;

    private SessionSeedItem? SeedRow(string entity)
    {
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Items.TryGetValue(entity, out var row)) return row;
        return null;
    }
}
