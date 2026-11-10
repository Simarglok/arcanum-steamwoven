using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>ARC-UI-019: an item in the hero's inventory grid: its entity, its slot (row × 10 + column, the original's inventory location,
/// field 0x5E) and its size in cells, kept from the moment it took the slot.</summary>
public sealed record SessionHeroGridItem(string Entity, int Slot, int Width, int Height)
{
    internal InventoryGridItem Cells => new(Slot, Width, Height);
}

/// <summary>
/// ARC-UI-019: the hero's inventory grid (the original's critter grid, ledger JP3: 10 columns, 12 rows): the items with a slot in slot order,
/// and the held items that are neither worn nor in the grid, in list order (<see cref="Unplaced"/>). ARC-UI-090: the hero's gold pile
/// (<see cref="GoldEntity"/>) is one of them while the hero holds a coin, placed or waiting like an item. The original's insert check refuses an
/// item without room before it enters the list; here the room is the host's answer (not ready), so such an item waits outside the grid
/// and takes the first slot that frees.
/// </summary>
public sealed record SessionHeroGrid(IReadOnlyList<SessionHeroGridItem> Items, IReadOnlyList<string> Unplaced)
{
    public const int Columns = InventoryGridRules.Columns;
    public const int Rows = InventoryGridRules.CritterRows;
    public const int Slots = Columns * Rows;

    /// <summary>ARC-UI-090: the entity of the hero's gold pile in the grid. In the original the hero's gold is an item of type 8 in its list, named by
    /// critter field 0xE8, with a grid location like any item (ledger NT1, AM7, UI-090 row HG1); here the coins stay the session's counter
    /// (<see cref="GameSessionOwner.QueryGold"/>) and the pile is the grid record of this entity, present while the hero holds any coin.</summary>
    public const string GoldEntity = "gold:pc";

    /// <summary>ARC-UI-090: the gold pile's prototype file (ledger AM7, NT1: base prototype 9056, file number 9076 as <c>AreaBarterData.GoldPrototype</c>
    /// counts it), for the art source that sizes its cells.</summary>
    public const int GoldPrototype = 9076;

    /// <summary>The kind the session names the hero's gold pile by when it asks the art source for its cells.</summary>
    public static SessionItemKind GoldKind { get; } = new(SessionItemType.Gold, GoldPrototype, 0, 0);
}

/// <summary>ARC-UI-019: what the host knows of a held item for a move in the grid: its saved kind, its no-drop flag (item flag 0x20) and its
/// script attachment points (the remove-item and insert-item events of the move, ARC-SCRIPT-042).</summary>
public sealed record SessionGridMoveItem(SessionItemKind Kind, bool NoDrop, IReadOnlyList<int> ScriptPoints);

/// <summary>ARC-DEV-339: the answer of <see cref="GameSessionOwner.ArrangeHeroGrid"/>: the disposition (Changed when some item took another
/// slot, NoChange when the arrangement left every item where it was or when <paramref name="NoRoom"/>, Rejected for input, version or no hero),
/// the failure of a rejection, whether the arrangement found no place for an item (the original then writes nothing), and how many items
/// took another slot.</summary>
public sealed record SessionGridArrangement(GameSessionDisposition Disposition, GameSessionFailure Failure, bool NoRoom, int Moved, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    private Func<string, SessionItemKind, (uint Width, uint Height)?>? _itemArtSizes;

    /// <summary>
    /// ARC-UI-055: attaches the host's inventory art frames: for an item (its entity and kind) the width and height in pixels of the frame
    /// of its inventory art (field 0x5D), or null when the host cannot read it. The source must not change the session: the session asks it in the middle of its own changes
    /// (the grid's settling after every change, a drop's placement) and only checks that it was not disposed meanwhile. The session turns a frame into the item's size in grid
    /// cells as the original does (0x461780, ledger JP3: (pixels − 1) / 32 + 1 per side, a 0-pixel side never fits), for the hero's
    /// grid and for the junk piles; without an answer it keeps the seed's size or 1 × 1. Not state: it is never saved and is attached again
    /// after a restore; null detaches it. Attaching it lays out again every grid item whose kept size differs from its art's (a grid made
    /// before the art was known, such as a new hero's kit or a save from before ARC-UI-055): those items leave their slots and take the
    /// first free place for their size in list order (PROJECT; the original's sizes never change), one revision when anything moved.
    /// </summary>
    public void AttachItemArtSizes(Func<string, SessionItemKind, (uint Width, uint Height)?>? sizes)
    {
        ThrowIfDisposed();
        _itemArtSizes = sizes;
        if (sizes is null || !_inventory.Grid.Values.Any(SizeChanged) || _revision == long.MaxValue) return;
        _revision++;
        SettleHeroGrid();
        MarkHotkeyPlaces();
    }

    /// <summary>ARC-UI-055: an item's size in inventory cells as the session sizes it (its art's through <see cref="AttachItemArtSizes"/>,
    /// else its attached seed's facts, else 1 × 1), for a window that draws another holder's items (the loot window, IW28).</summary>
    public (int Width, int Height) QueryItemCells(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity.", nameof(entity));
        return GridSize(entity);
    }

    // The size of an item's inventory art in cells, or null when no art source is attached or it does not answer for the item's kind
    // (the hero's, a placed one's, one the session added to an NPC or a seed item's).
    private (int Width, int Height)? ArtCells(string entity, SessionSeedItem? row)
    {
        if (_itemArtSizes is null) return null;
        if ((entity == SessionHeroGrid.GoldEntity ? SessionHeroGrid.GoldKind : HeldKind(entity) ?? _inventory.PlacedItem(entity)?.Kind ?? row?.Facts.Kind ?? AddedKind(entity)) is not { } kind) return null;
        var pixels = _itemArtSizes(entity, kind);
        ThrowIfDisposed();
        return pixels is { } frame ? InventoryGridRules.CellsOf(frame) : null;
    }

    // The kind of a made item the session added to an NPC's list.
    private SessionItemKind? AddedKind(string entity) => _inventory.NpcOf(entity) is { } npc
        ? _inventory.AddedTo(npc).Where(item => item.Entity == entity).Select(item => (SessionItemKind?)item.Kind).FirstOrDefault() : null;

    // Whether a grid item's kept size differs from its art's (only while an art source answers for it).
    private bool SizeChanged(SessionHeroGridItem item) => ArtCells(item.Entity, SeedRow(item.Entity)) is { } cells && cells != (item.Width, item.Height);

    /// <summary>ARC-UI-019: the hero's inventory grid (the PC's held items: kit, taken and made; worn items have no slot; ARC-UI-090: and the gold
    /// pile <see cref="SessionHeroGrid.GoldEntity"/> while the hero holds a coin).</summary>
    public SessionHeroGrid QueryHeroGrid()
    {
        ThrowIfDisposed();
        return new(Array.AsReadOnly(_inventory.Grid.Values.OrderBy(item => item.Slot).ToArray()),
            Array.AsReadOnly(GridMembers().Where(entity => !_inventory.Grid.ContainsKey(entity)).ToArray()));
    }

    /// <summary>
    /// ARC-UI-019: moves an item of the hero's grid (or one waiting outside it) to <paramref name="slot"/>, the same-holder move of the
    /// original's window (0x5788C0, ledger IW4, IW10–IW12). Refused before any effect, in order: input (<see cref="GameSessionFailure.InvalidItem"/>;
    /// a slot outside the grid), version, an item the hero does not hold or wears (<see cref="GameSessionFailure.ItemNotAvailable"/>; a worn
    /// item comes off with <see cref="Unequip"/> and its slot) or whose saved kind differs (<see cref="GameSessionFailure.InventoryMismatch"/>);
    /// remove-item or insert-item scripts without <paramref name="scripts"/> (<see cref="GameSessionFailure.ItemScriptUnsupported"/>); the
    /// remove check (0x466DA0): the no-drop flag refuses every move, onto its own slot too (<see cref="GameSessionFailure.WearRefused"/>,
    /// <see cref="EquipmentRefusal.NotDroppable"/>); the target cells (<see cref="GameSessionFailure.GridCellsTaken"/>: the rectangle leaves
    /// the grid or meets another item's cells; its own cells count as free; the original refuses silently). Then the insert check with the
    /// item out (0x466510): the hero's load above its carry weight (<see cref="SessionInsertVerdict.TooHeavy"/>), the Technophobia refusal,
    /// then the host's answer; a refusal leaves the item where it was (<see cref="GameSessionFailure.InsertRefused"/>; the original shows no
    /// message). With a script host the item's remove-item (28) and insert-item (21) events run as the removal and the insertion fire them,
    /// on a refused check and onto its own slot too (IW10); without such scripts a move onto its own slot is NoChange. The move changes no
    /// weight and no list, so no encumbrance recalculation follows (the original lowers its message counter around it, WE5). One revision.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x5788C0, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public EquipmentMutationResult MoveHeroItem(GameSessionVersion expected, string entity, int slot, SessionGridMoveItem item, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (entity == SessionHeroGrid.GoldEntity) return MoveHeroGold(expected, slot);
        if (!SessionValidation.Entity(entity) || slot is < 0 or >= SessionHeroGrid.Slots || item.ScriptPoints is null || !Enum.IsDefined(item.Kind.Type) ||
            item.ScriptPoints.Any(point => point is < 0 or > SessionScriptPoint.Last)) return RejectWear(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectWear(GameSessionFailure.StaleVersion);
        if (HeldKind(entity) is not { } kind || _equipment.SlotOf(entity) is not null) return RejectWear(GameSessionFailure.ItemNotAvailable);
        if (kind != item.Kind) return RejectWear(GameSessionFailure.InventoryMismatch);
        var points = item.ScriptPoints.Where(point => point is SessionScriptPoint.RemoveItem or SessionScriptPoint.InsertItem).Distinct().Order().ToArray();
        if (scripts is null && points.Length > 0) return RejectWear(GameSessionFailure.ItemScriptUnsupported);
        if (item.NoDrop) return RejectWear(GameSessionFailure.WearRefused, EquipmentRefusal.NotDroppable);
        _inventory.Grid.TryGetValue(entity, out var current);
        var (width, height) = current is null ? GridSize(entity) : (current.Width, current.Height);
        if (!GridCellsFree(new(slot, width, height), entity)) return RejectWear(GameSessionFailure.GridCellsTaken);
        if (current?.Slot == slot && points.Length == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, null, Version);
        if (_revision == long.MaxValue) return RejectWear(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        var complexity = _inventory.Weights.TryGetValue(entity, out var weight) ? weight.Complexity : 0;
        var (map, flagVersion) = (_actor.Map, _flags.Version);
        var runs = scripts is null || points.Length == 0 ? null
            : new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = complexity });
        bool Stale() => _revision != revision || _actor.Map != map || _flags.Version != flagVersion;
        EquipmentMutationResult Refused(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
            RejectWear(failure, verdict: verdict) with { Scripts = runs?.Runs ?? [] };
        if (runs is not null)
        {
            RemovalRuns(runs, entity, points, worn: false);
            ThrowIfDisposed();
            if (Stale()) return Refused(GameSessionFailure.StaleVersion);
            if (runs.Failure != GameSessionFailure.None) return Refused(runs.Failure);
        }
        var verdict = HeroInsertVerdict(HeroOverloaded() ? SessionInsertVerdict.TooHeavy
            : insertCheck(new SessionInventoryItem(entity, CarriedFacts(entity, complexity))), complexity);
        ThrowIfDisposed();
        if (Stale()) return Refused(GameSessionFailure.StaleVersion, verdict);
        if (runs is null)
        {
            if (verdict != SessionInsertVerdict.Accepted) return Refused(GameSessionFailure.InsertRefused, verdict);
            _inventory.Grid[entity] = new(entity, slot, width, height);
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, null, verdict, Version);
        }
        // The removal ran; the insertion at the target, or back at the old place when the check refused, runs insert item.
        InsertionRuns(runs, entity, points, wield: false);
        ThrowIfDisposed();
        if (Stale()) return Refused(GameSessionFailure.StaleVersion, verdict);
        runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return Refused(runs.Failure, verdict);
        var accepted = verdict == SessionInsertVerdict.Accepted;
        if (!accepted && !runs.Changes)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, verdict, Version)
                { Scripts = runs.Runs, Messages = runs.Messages, RolledBack = true };
        if (accepted && current?.Slot == slot && !runs.Changes)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, verdict, Version)
                { Scripts = runs.Runs, Messages = runs.Messages };
        runs.Commit();
        if (accepted) _inventory.Grid[entity] = new(entity, slot, width, height);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, null, verdict, Version)
            { Scripts = runs.Runs, Messages = runs.Messages, RolledBack = !accepted };
    }

    /// <summary>
    /// ARC-DEV-339: the arrange button of the original's window (0x4670A0, ledger IW37–IW40): lays out the hero's whole list again,
    /// <see cref="InventoryArrangeRules.Arrange"/>: the items on worn and quick slots stay out, the others (an item waiting outside the grid
    /// and the gold pile included) are sorted by height, then width, largest first, and take the first free place of the layout. When one
    /// finds no place nothing changes (<see cref="SessionGridArrangement.NoRoom"/>). The original writes the new slots straight into the items'
    /// locations: no remove check, no insert check, no weight, no remove-item or insert-item script event, no action points (IW12, IW15 do not
    /// apply). Refused before any effect: an unknown layout (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, no hero
    /// (<see cref="GameSessionFailure.EquipmentUnavailable"/>). One revision when some slot changed. PROJECT: the list order is the session's
    /// (kit, taken, made, then the gold pile, whose place in the original's list was not read); the multiplayer branch is not built.
    /// </summary>
    public SessionGridArrangement ArrangeHeroGrid(GameSessionVersion expected, InventoryArrangeLayout layout)
    {
        ThrowIfDisposed();
        SessionGridArrangement Rejected(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, false, 0, Version);
        if (!Enum.IsDefined(layout)) return Rejected(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Rejected(GameSessionFailure.StaleVersion);
        if (_hero is null) return Rejected(GameSessionFailure.EquipmentUnavailable);
        var onHotkeys = HotkeyItems().ToHashSet(StringComparer.Ordinal);
        var list = new List<InventoryArrangeItem>();
        foreach (var entity in HeroHeld().Select(item => item.Entity).Concat(_inventory.Gold > 0 ? [SessionHeroGrid.GoldEntity] : []))
        {
            var (width, height) = GridSize(entity);
            var location = _equipment.SlotOf(entity) is not null ? 1000 : onHotkeys.Contains(entity) ? 2000
                : _inventory.Grid.TryGetValue(entity, out var placed) ? placed.Slot : -1;
            list.Add(new(entity, location, width, height));
        }
        var placements = InventoryArrangeRules.Arrange(list, SessionHeroGrid.Rows, layout);
        if (placements is null) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, true, 0, Version);
        var moved = placements.Count(placement => !_inventory.Grid.TryGetValue(placement.Key, out var now) || now.Slot != placement.Slot);
        if (moved == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, false, 0, Version);
        if (_revision == long.MaxValue) return Rejected(GameSessionFailure.RevisionExhausted);
        var sizes = list.ToDictionary(item => item.Key, item => (item.Width, item.Height), StringComparer.Ordinal);
        foreach (var placement in placements) _inventory.Grid[placement.Key] = new(placement.Key, placement.Slot, sizes[placement.Key].Width, sizes[placement.Key].Height);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, false, moved, Version);
    }

    /// <summary>
    /// ARC-UI-090: the same-holder move of the hero's gold pile (<see cref="SessionHeroGrid.GoldEntity"/>) to <paramref name="slot"/>, the move of
    /// 0x5788C0 for an item of type 8 (ledger HG1): the remove check (0x466DA0) and the insert check (0x466510) pass a pile (no no-drop flag, no weight,
    /// no scripts), so only the target cells decide (<see cref="GameSessionFailure.GridCellsTaken"/>). Refused before any effect: input, version,
    /// no coins (<see cref="GameSessionFailure.ItemNotAvailable"/>). A move onto its own slot is NoChange. One revision.
    /// </summary>
    // A pile with coins but no cell (the grid was full when its first coin came, or a save's upgrade found no room) is refused as ItemNotAvailable: the
    // window draws only placed items, and the next change places the pile when a cell frees.
    private EquipmentMutationResult MoveHeroGold(GameSessionVersion expected, int slot)
    {
        if (slot is < 0 or >= SessionHeroGrid.Slots) return RejectWear(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectWear(GameSessionFailure.StaleVersion);
        if (_hero is null || _inventory.Gold <= 0 || !_inventory.Grid.TryGetValue(SessionHeroGrid.GoldEntity, out var current)) return RejectWear(GameSessionFailure.ItemNotAvailable);
        if (!GridCellsFree(new(slot, current.Width, current.Height), SessionHeroGrid.GoldEntity)) return RejectWear(GameSessionFailure.GridCellsTaken);
        if (current.Slot == slot) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, null, Version);
        if (_revision == long.MaxValue) return RejectWear(GameSessionFailure.RevisionExhausted);
        _inventory.Grid[SessionHeroGrid.GoldEntity] = new(SessionHeroGrid.GoldEntity, slot, current.Width, current.Height);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, null, SessionInsertVerdict.Accepted, Version);
    }

    // Whether a rectangle lies inside the hero's grid and meets no cell of another grid item (the original's target test, IW4).
    private bool GridCellsFree(InventoryGridItem cells, string? except)
    {
        if (!InventoryGridRules.Fits(cells, SessionHeroGrid.Rows)) return false;
        var occupied = InventoryGridRules.Occupied(_inventory.Grid.Values.Where(item => item.Entity != except).Select(item => item.Cells), SessionHeroGrid.Rows);
        for (var row = 0; row < cells.Height; row++)
            for (var column = 0; column < cells.Width; column++)
                if (occupied[cells.Slot + row * SessionHeroGrid.Columns + column]) return false;
        return true;
    }

    // Whether the grid has a free slot for the item (0x4664C0 → 0x4675A0) with its own cells and those of leaving, an item that left
    // too, counted free (the insert check of a move runs with the moved items out, IW3, IW11).
    private bool HeroGridRoom(string entity, string? leaving) => FreeGridSlot(entity, leaving).Slot >= 0;

    private (int Slot, int Width, int Height) FreeGridSlot(string entity, string? leaving)
    {
        var occupied = InventoryGridRules.Occupied(_inventory.Grid.Values.Where(item => item.Entity != entity && item.Entity != leaving).Select(item => item.Cells),
            SessionHeroGrid.Rows);
        var (width, height) = _inventory.Grid.TryGetValue(entity, out var placed) ? (placed.Width, placed.Height) : GridSize(entity);
        return (InventoryGridRules.FindSlot(occupied, width, height), width, height);
    }

    // The item a wear move puts back takes the place its insert check found (IW11), before items waiting outside the grid are placed;
    // leaving is the item that leaves the grid for the slot.
    private void PlaceInGrid(string entity, string? leaving)
    {
        var (slot, width, height) = FreeGridSlot(entity, leaving);
        if (leaving is not null) _inventory.Grid.Remove(leaving);
        if (slot >= 0) _inventory.Grid[entity] = new(entity, slot, width, height);
    }

    // The insert check's weight test for an item the hero holds (ledger WE4, IW12): a move removes it first, so its own weight counts once,
    // and the hero's whole load (less leaving, an item that left too) is refused above the carry weight.
    private bool HeroOverloaded(string? leaving = null) =>
        HeroCarryFacts(leaving is null ? null : new Dictionary<string, int?>(StringComparer.Ordinal) { [leaving] = null }) is { } carry && carry.Weight > carry.CarryWeight;

    // The held items that belong in the grid: not worn and on no item hotkey entry (ARC-UI-022: location 2000 + entry), in list order (kit,
    // taken, made).
    private IEnumerable<string> GridMembers()
    {
        var onHotkeys = HotkeyItems().ToHashSet(StringComparer.Ordinal);
        var items = HeroHeld().Select(item => item.Entity).Where(entity => _equipment.SlotOf(entity) is null && !onHotkeys.Contains(entity));
        // ARC-UI-090: the gold pile is the last member (PROJECT: the original's order of the pile and the kit was not read), present while the
        // hero holds any coin; the pile is destroyed with its last coin (NT1), so its cell frees then.
        return _hero is not null && _inventory.Gold > 0 ? items.Append(SessionHeroGrid.GoldEntity) : items;
    }

    /// <summary>
    /// ARC-UI-019: after every change the grid follows the hero's list as the original's removal and insertion keep it (0x466E00, 0x466640):
    /// an item that left the list or was put on loses its slot first, then each held item without one takes the first free slot for its size
    /// (0x4675A0, ledger JP3), in list order, so an occupant a wear move puts back lands where the room the worn item left counts. An item
    /// without room keeps none (see <see cref="SessionHeroGrid.Unplaced"/>). ARC-UI-055: an item whose kept size differs from its art's
    /// (<see cref="AttachItemArtSizes"/>) loses its slot too and is placed again with its art's size.
    /// </summary>
    private void SettleHeroGrid()
    {
        var members = GridMembers().ToList();
        var held = members.ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _inventory.Grid.Values.Where(item => !held.Contains(item.Entity) || SizeChanged(item)).Select(item => item.Entity).ToArray())
            _inventory.Grid.Remove(gone);
        bool[]? occupied = null;
        foreach (var entity in members)
        {
            if (_inventory.Grid.ContainsKey(entity)) continue;
            occupied ??= InventoryGridRules.Occupied(_inventory.Grid.Values.Select(item => item.Cells), SessionHeroGrid.Rows);
            var (width, height) = GridSize(entity);
            var slot = InventoryGridRules.FindSlot(occupied, width, height);
            if (slot < 0) continue;
            _inventory.Grid[entity] = new(entity, slot, width, height);
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                    occupied[slot + row * SessionHeroGrid.Columns + column] = true;
        }
    }

    // A saved grid: each item one the PC holds and does not wear, on no item hotkey entry (ARC-UI-022), once, inside the grid, with no shared cell.
    private static GameSessionFailure ImportHeroGrid(IReadOnlyList<SessionHeroGridItem> saved, InventoryDomain inventory, EquipmentDomain equipment,
        IReadOnlyList<HotkeyEntry> hotkeys)
    {
        var held = inventory.Hero.PackKinds.Select(item => item.Entity)
            .Where(entity => equipment.SlotOf(entity) is null && !hotkeys.Any(entry => entry.Kind == HotkeyKind.Item && entry.Item == entity))
            .ToHashSet(StringComparer.Ordinal);
        if (inventory.Gold > 0) held.Add(SessionHeroGrid.GoldEntity);
        var occupied = new bool[SessionHeroGrid.Slots];
        foreach (var item in saved)
        {
            if (item is null || !held.Contains(item.Entity) || inventory.Grid.ContainsKey(item.Entity) || !InventoryGridRules.Fits(item.Cells, SessionHeroGrid.Rows))
                return GameSessionFailure.InvalidGrid;
            for (var row = 0; row < item.Height; row++)
                for (var column = 0; column < item.Width; column++)
                {
                    var cell = item.Slot + row * SessionHeroGrid.Columns + column;
                    if (occupied[cell]) return GameSessionFailure.InvalidGrid;
                    occupied[cell] = true;
                }
            inventory.Grid[item.Entity] = item;
        }
        return GameSessionFailure.None;
    }
}
