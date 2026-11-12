namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1279 (ledger WO2, WO7): the first item of a holder's own list whose name class (<c>OBJ_F_NAME</c>, field 0x16) is the number a script action names, as the original finds it (0x462480): the list of the
/// critter or container, in order, worn items included. <see cref="Worn"/> and <see cref="ScriptPoints"/> are what the session knows of the item for the checks a move or a destroy makes.
/// </summary>
public sealed record SessionNamedItem(string Entity, SessionItemKind Kind, bool Worn, IReadOnlyList<int> ScriptPoints);

/// <summary>
/// ARC-SCRIPT-1279: what a script move or destroy of the first item named N would do, asked before anything applies so a run is refused whole rather than left half applied: <see cref="Item"/> is the item found (null:
/// the holder has none, the action does nothing, as in the original), <see cref="Refusal"/> why the session cannot apply it (null: it can, though a later check, such as the insert check, can still decline it).
/// </summary>
public sealed record SessionNamedItemPlan(SessionNamedItem? Item, string? Refusal);

public sealed partial class GameSessionOwner
{
    /// <summary>The most item field writes the script actions keep: every item is one row.</summary>
    private const int ItemFieldRowLimit = SessionInventoryState.MaxItemFields;

    // ---- Named items (WO2, WO7) ----

    /// <summary>
    /// ARC-SCRIPT-1279 (WO2, WO7, the finder 0x462480): the first item of the holder's list whose name class is <paramref name="name"/>; <paramref name="holder"/> null is the hero (its kit, then the items it took, then
    /// the items it made, as its list is ordered), any other holder an NPC or a container an attached map lists (the seed items still in it, then the items the session added). Null when the holder has no such item
    /// and for a holder no attached map lists (the original's finder returns nothing for any object that is no critter or container). A read of the current state, in no revision.
    /// </summary>
    public SessionNamedItem? QueryNamedItem(string? holder, int name)
    {
        ThrowIfDisposed();
        if (holder is not null && !SessionValidation.Entity(holder)) throw new ArgumentException("Invalid entity id.", nameof(holder));
        if (holder is null)
        {
            if (_hero is null) return null;
            foreach (var (entity, kind) in _inventory.Hero.PackKinds)
                if (kind.Name == name) return new(entity, kind, _equipment.SlotOf(entity) is not null, KnownScriptPoints(entity, null));
            return null;
        }
        if (QueryHolderItems(holder) is not { } list) return null;
        foreach (var item in list)
            if (item.Facts.Kind.Name == name) return new(item.Entity, item.Facts.Kind, item.Facts.Worn, KnownScriptPoints(item.Entity, item.Facts));
        return null;
    }

    // The attachment points with a script an item has, as far as the session knows them: the points its seed row shows, and the slots the session kept or a made item inherits from its prototype.
    private IReadOnlyList<int> KnownScriptPoints(string entity, SessionItemFacts? facts)
    {
        var points = new SortedSet<int>(facts?.ScriptPoints ?? []);
        for (var point = 0; point <= SessionScriptPoint.Last; point++)
            if (point != Arcanum.Scripting.Scr.ScrPoint.DialogOverride && QueryItemScriptSlot(entity, point) is { Script: > 0 }) points.Add(point);
        return [.. points];
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO7): what destroy item named would do to the first item of <paramref name="holder"/>'s list named <paramref name="name"/> (the destroy 0x43CCA0, the one destroy path of
    /// <see cref="DestroyObject"/>): refused when the item has a destroy (2), remove-item (28) or, worn, wield-off (25) script, which the session does not run here (the destroy script can refuse the destroy and the
    /// removal fires its event), or a spell run that names it as its source (<see cref="GameSessionFailure.ItemEffectUnsupported"/>), or when the budget of deleted-item records is spent.
    /// </summary>
    public SessionNamedItemPlan QueryNamedItemDestroy(string? holder, int name)
    {
        ThrowIfDisposed();
        if (QueryNamedItem(holder, name) is not { } item) return new(null, null);
        if (item.ScriptPoints.Contains(SessionScriptPoint.RemoveItem) || item.ScriptPoints.Contains(Arcanum.Scripting.Scr.ScrPoint.Destroy) || item.Worn && item.ScriptPoints.Contains(SessionScriptPoint.WieldOff))
            return new(item, $"destroy item named {name} would run a destroy, remove-item or wield-off script of {item.Entity}, which is not modelled");
        if (HasItemRuns(item.Entity)) return new(item, $"destroy item named {name}: {item.Entity} is the source of a running spell");
        if (_durability.Count >= _limits.MaxDurabilityRecords && !_durability.Records.ContainsKey(item.Entity))
            return new(item, "destroy item named: the budget of deleted-item records is spent");
        return new(item, null);
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO7, the script action destroy item named, case 0x7E → 0x43CCA0): the first item of the holder's list whose name class is <paramref name="name"/> is destroyed as every deletion is
    /// (<see cref="DestroyObject"/>: out of its slot and list, hidden from its seed, its records dropped, recorded as deleted); no such item changes nothing. One revision. Refused before any effect: the version, the checks of
    /// <see cref="QueryNamedItemDestroy"/> (<see cref="GameSessionFailure.ItemScriptUnsupported"/>, <see cref="GameSessionFailure.ItemEffectUnsupported"/>, <see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// </summary>
    public InventoryMutationResult DestroyNamedItem(GameSessionVersion expected, string? holder, int name)
    {
        ThrowIfDisposed();
        if (holder is not null && !SessionValidation.Entity(holder)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        var plan = QueryNamedItemDestroy(holder, name);
        if (plan.Item is not { } item) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (plan.Refusal is not null)
        {
            var failure = HasItemRuns(item.Entity) ? GameSessionFailure.ItemEffectUnsupported
                : _durability.Count >= _limits.MaxDurabilityRecords && !_durability.Records.ContainsKey(item.Entity) ? GameSessionFailure.BudgetExceeded : GameSessionFailure.ItemScriptUnsupported;
            return RejectItem(failure);
        }
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        DestroyObject(item.Entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, null, Version);
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO2): what transfer item would do with the first item of <paramref name="from"/>'s list named <paramref name="name"/> (the hero when null) into <paramref name="to"/>'s (0x4617F0 with no slot, the
    /// first free place). The session moves an item between the hero and an NPC or container (<see cref="GiveItemToNpc"/>, <see cref="TakeItemFromNpc"/>, <see cref="LootItem"/> and the container list), so a pair of
    /// two holders neither of which is the hero is refused, as is an item the move would run a script for (the session runs no item script here), a worn item leaving an NPC or container, and a holder no attached
    /// map lists. The item's insert check, the room and weight tests and a transfer script of the new holder (point 32) are answered when the move applies.
    /// </summary>
    public SessionNamedItemPlan QueryNamedItemTransfer(string? from, string? to, int name)
    {
        ThrowIfDisposed();
        if (from is not null && !SessionValidation.Entity(from) || to is not null && !SessionValidation.Entity(to)) throw new ArgumentException("Invalid entity id.");
        if (from == to) return new(null, null);
        if (QueryNamedItem(from, name) is not { } item) return new(null, null);
        if (from is not null && to is not null) return new(item, $"transfer item between {from} and {to}, neither the hero, is not modelled");
        var other = from ?? to!;
        if (!HolderKnown(other)) return new(item, $"transfer item: no attached map lists the holder {other}");
        if (item.Worn && from is not null) return new(item, $"transfer item of {item.Entity}, worn by {from}, is not modelled");
        if (item.ScriptPoints.Any(point => point is SessionScriptPoint.Get or SessionScriptPoint.InsertItem or SessionScriptPoint.RemoveItem or SessionScriptPoint.WieldOff))
            return new(item, $"transfer item of {item.Entity}, which has a get, insert-item, remove-item or wield-off script, is not modelled");
        return new(item, null);
    }

    // An NPC or a container one of the attached maps lists as a holder (the hero is no holder of a seed).
    private bool HolderKnown(string holder) => _inventory.Seeds.Values.Any(seed => seed.Holders.ContainsKey(holder));

    /// <summary>
    /// ARC-SCRIPT-1279 (WO2, the script action transfer item, case 0x29 → 0x4617F0): the first item of <paramref name="from"/>'s list whose name class is <paramref name="name"/> goes to the end of <paramref name="to"/>'s
    /// list (the hero when null) under the item rules of the moves it is made of: an NPC to the hero is <see cref="TakeItemFromNpc"/>, a container to the hero <see cref="LootItem"/>, the hero to an NPC
    /// <see cref="GiveItemToNpc"/> and the hero to a container the container list's insert. No such item changes nothing. Refused before any effect: the version, a pair, item or holder
    /// <see cref="QueryNamedItemTransfer"/> refuses (<see cref="GameSessionFailure.HolderPairUnsupported"/>, <see cref="GameSessionFailure.ItemScriptUnsupported"/>, <see cref="GameSessionFailure.WornItemUnsupported"/>,
    /// <see cref="GameSessionFailure.UnknownHolder"/>), then each move's own refusals (the host's insert check as <see cref="GameSessionFailure.InsertRefused"/>, the lists' budgets, the revision). One revision.
    /// </summary>
    public InventoryMutationResult TransferNamedItem(GameSessionVersion expected, string? from, string? to, int name, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (from is not null && !SessionValidation.Entity(from) || to is not null && !SessionValidation.Entity(to)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        var plan = QueryNamedItemTransfer(from, to, name);
        if (plan.Item is not { } item) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (plan.Refusal is not null)
            return RejectItem(from is not null && to is not null ? GameSessionFailure.HolderPairUnsupported : !HolderKnown(from ?? to!) ? GameSessionFailure.UnknownHolder
                : item.Worn && from is not null ? GameSessionFailure.WornItemUnsupported : GameSessionFailure.ItemScriptUnsupported);
        if (from is null)
        {
            var container = _inventory.Seeds.Values.Any(seed => seed.Holders.TryGetValue(to!, out var row) && row.Kind == SessionHolderKind.Container);
            return container ? GiveToContainer(to!, item.Entity, insertCheck) : GiveItemToNpc(expected, to!, item.Entity, insertCheck);
        }
        var fromContainer = _inventory.Seeds.Values.Any(seed => seed.Holders.TryGetValue(from, out var row) && row.Kind == SessionHolderKind.Container);
        return fromContainer ? LootItem(expected, from, item.Entity, insertCheck) : TakeItemFromNpc(expected, from, item.Entity, insertCheck);
    }

    // The hero's item into a container's list (the move GiveItemToNpc makes into an NPC's), after the container checks the barter window's sale makes.
    private InventoryMutationResult GiveToContainer(string container, string item, SessionInsertCheck insertCheck) =>
        GiveItemTo(container, SessionHolderKind.Container, item, insertCheck, identify: false);

    // ---- Item fields (WO6, WO8) ----

    /// <summary>ARC-SCRIPT-1279 (WO6, WO8): the fields scripts wrote on an item, empty when none. A reader applies it over what the item's data stores. A read of the current state, in no revision.</summary>
    public SessionItemFieldWrites QueryItemFieldWrites(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return _inventory.ItemFields.GetValueOrDefault(entity);
    }

    /// <summary>The items with written fields, ascending by entity.</summary>
    public IReadOnlyList<SessionItemFieldRecord> QueryAllItemFieldWrites()
    {
        ThrowIfDisposed();
        return [.. _inventory.ItemFields.Select(pair => new SessionItemFieldRecord(pair.Key, pair.Value))];
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO6, the script action set written start, case 0x5E → the field write 0x4EFDD0 of field 0xCB): the item's written start line (the first line of its text; a schematic document's schematic number) is
    /// <paramref name="start"/> from now on, over the data's. The host checked the item is a written one (type 13: the original writes the field for no other). One revision; the same start again is NoChange. Refused before any
    /// effect: an invalid or deleted item (<see cref="GameSessionFailure.InvalidItem"/>), the version, a new item beyond <see cref="SessionInventoryState.MaxItemFields"/> (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// </summary>
    public GameSessionMutationResult SetItemWrittenStart(GameSessionVersion expected, string entity, int start)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Entity(entity) || _durability.Destroyed.Contains(entity)) return Reject(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _inventory.ItemFields.TryGetValue(entity, out var old);
        if (held && old.WrittenStart == start) return NoChange();
        if (!held && _inventory.ItemFields.Count >= ItemFieldRowLimit) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _inventory.ItemFields[entity] = old with { WrittenStart = start };
        return Changed();
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO8, the script action toggle item inventory display, case 0x7F): bit 0x10 of the item's flags (field 0x57) is flipped, as it is in the original (the inventory window's painter skips an item that has the
    /// bit and clears it when a window opens, 0x576520). The session keeps the parity against the data's bit, so two toggles leave nothing. The host checked the item is an item (type 5..14). One revision. Refused before any
    /// effect: an invalid or deleted item (<see cref="GameSessionFailure.InvalidItem"/>), the version, a new item beyond <see cref="SessionInventoryState.MaxItemFields"/> (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision.
    /// </summary>
    public GameSessionMutationResult ToggleItemDisplay(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Entity(entity) || _durability.Destroyed.Contains(entity)) return Reject(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _inventory.ItemFields.TryGetValue(entity, out var old);
        if (!held && _inventory.ItemFields.Count >= ItemFieldRowLimit) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var now = old with { DisplayFlipped = !old.DisplayFlipped };
        if (now.IsEmpty) _inventory.ItemFields.Remove(entity); else _inventory.ItemFields[entity] = now;
        return Changed();
    }
}
