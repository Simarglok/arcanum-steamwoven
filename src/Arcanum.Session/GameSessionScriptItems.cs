namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1279 (ledger WO2, WO7): the first item of a holder's own list whose name class (<c>OBJ_F_NAME</c>, field 0x16) is the number a script action names, as the original finds it (0x462480): the list of the
/// critter or container, in order, worn items included. <see cref="Worn"/> and <see cref="ScriptPoints"/> are what the session knows of the item for the checks a move or a destroy makes.
/// </summary>
public sealed record SessionNamedItem(string Entity, SessionItemKind Kind, bool Worn, IReadOnlyList<int> ScriptPoints)
{
    /// <summary>The item is worn through a trigger host and carries item spells, so it leaves a slot only through one (the session runs none here).</summary>
    public bool ItemSpells { get; init; }

    /// <summary>The key number a key item of an NPC or container carries (null for any other item).</summary>
    public int? KeyId { get; init; }
}

/// <summary>
/// ARC-SCRIPT-1279: what a script move or destroy of the first item named N would do, asked before anything applies so a run is refused whole rather than left half applied: <see cref="Item"/> is the item found (null:
/// the holder has none, the action does nothing, as in the original), <see cref="Refusal"/> why the session cannot apply it (null: it can, though a later check, such as the insert check, can still decline it) and
/// <see cref="Failure"/> the failure the applying call answers for that refusal.
/// </summary>
public sealed record SessionNamedItemPlan(SessionNamedItem? Item, string? Refusal, GameSessionFailure Failure = GameSessionFailure.None);

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
                if (kind.Name == name) return HeroNamedItem(entity, kind);
            return null;
        }
        if (QueryHolderItems(holder) is not { } list) return null;
        foreach (var item in list)
            if (item.Facts.Kind.Name == name) return new(item.Entity, item.Facts.Kind, item.Facts.Worn, KnownScriptPoints(item.Entity, item.Facts)) { KeyId = item.Facts.KeyId };
        return null;
    }

    // One of the hero's items with what the session knows of it: the slots it kept or inherits, the points of the seed row an item taken from a map came from, and, worn, the points and the item spells mark its slot holds.
    private SessionNamedItem HeroNamedItem(string entity, SessionItemKind kind)
    {
        var points = new SortedSet<int>(KnownScriptPoints(entity, null));
        if (HeroListed(entity) is { Origin: { } origin } && _inventory.Seeds.TryGetValue(origin, out var seed) && seed.Items.TryGetValue(entity, out var row)) points.UnionWith(row.Facts.ScriptPoints);
        var spells = false;
        if (_equipment.SlotOf(entity) is { } slot)
        {
            points.UnionWith(_equipment.Worn[slot].ScriptPoints);
            spells = _equipment.Worn[slot].ItemSpells;
        }
        return new(entity, kind, _equipment.SlotOf(entity) is not null, [.. points]) { ItemSpells = spells };
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
            return new(item, $"destroy item named {name} would run a destroy, remove-item or wield-off script of {item.Entity}, which is not modelled", GameSessionFailure.ItemScriptUnsupported);
        if (HasItemRuns(item.Entity) || item.Worn && item.ItemSpells)
            return new(item, $"destroy item named {name}: {item.Entity} is the source of a running spell or carries worn item spells", GameSessionFailure.ItemEffectUnsupported);
        if (_durability.Count >= _limits.MaxDurabilityRecords && !_durability.Records.ContainsKey(item.Entity))
            return new(item, "destroy item named: the budget of deleted-item records is spent", GameSessionFailure.BudgetExceeded);
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
        if (plan.Refusal is not null) return RejectItem(plan.Failure);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        DestroyObject(item.Entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, null, Version);
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO2): what transfer item would do with the first item of <paramref name="from"/>'s list named <paramref name="name"/> (the hero when null) into <paramref name="to"/>'s (0x4617F0 with no slot, the
    /// first free place). The session moves an item between the hero and an NPC or container (<see cref="GiveItemToNpc"/>, <see cref="TakeItemFromNpc"/>, <see cref="LootItem"/> and the container list), so a pair of
    /// two holders neither of which is the hero is refused, as is an item the move would run a script for (without <paramref name="itemScripts"/> the session runs no item script here; with it the move runs them, ARC-MAGIC-1823, ARC-MAGIC-2252; a hero's item counts the points of the seed row it came from and of its slot, an item of another map's NPC the points of its row), a worn
    /// item leaving an NPC or container or carrying item spells, an item that is the source of a running spell, a new holder with a transfer script (point 32), a container off the actor's map or spilled, a holder no attached
    /// map lists, and for a move to the hero a gold pile or a key the key ring cannot take. The item's insert check and the room and weight tests are answered when the move applies.
    /// ARC-MAGIC-2252: with <paramref name="itemScripts"/> the plan also makes a dry run of the move (the item scripts and the hero's transfer script it would run, read from the host and applied nowhere, the insert check taken as accepting):
    /// a run the session could not apply is the plan's refusal, so the calling script is not left half applied by a nested run found only when the move applies. The dry run reads the session as it is before the calling script's earlier
    /// effects apply, so a nested run that only fails after them is still found when the move applies.
    /// </summary>
    public SessionNamedItemPlan QueryNamedItemTransfer(string? from, string? to, int name, ISessionItemScriptHost? itemScripts = null)
    {
        ThrowIfDisposed();
        if (from is not null && !SessionValidation.Entity(from) || to is not null && !SessionValidation.Entity(to)) throw new ArgumentException("Invalid entity id.");
        if (from == to) return new(null, null);
        if (QueryNamedItem(from, name) is not { } item) return new(null, null);
        if (from is not null && to is not null) return new(item, $"transfer item between {from} and {to}, neither the hero, is not modelled", GameSessionFailure.HolderPairUnsupported);
        var other = from ?? to!;
        if (ListedHolderKind(other) is not { } kind) return new(item, $"transfer item: no attached map lists the holder {other}", GameSessionFailure.UnknownHolder);
        // The container moves are the window's (0x572370): a container of the actor's map that did not spill; a body or an NPC of any attached map.
        if (kind == SessionHolderKind.Container && !ShownContainer(other))
            return new(item, $"transfer item: the container {other} is not on the actor's map or has spilled its items", GameSessionFailure.HolderNotLootable);
        if (item.Worn && from is not null) return new(item, $"transfer item of {item.Entity}, worn by {from}, is not modelled", GameSessionFailure.WornItemUnsupported);
        // Without a script host the move runs none of the item's scripts: a hero's worn item with any, or any item with an insert-item, remove-item or wield-off point, is refused.
        // ARC-MAGIC-1823 (IE3, IE4, IE6): with itemScripts the hero's give (to an NPC or container) runs them through the caller's script host (GiveItemTo); ARC-MAGIC-2252: so does the take from an NPC or container (Take: the
        // hero's transfer script, remove item 28 with the holder as the triggerer, insert item 21). The dry run below finds the runs the session cannot apply.
        if (itemScripts is null && (item.ScriptPoints.Any(point => point is SessionScriptPoint.InsertItem or SessionScriptPoint.RemoveItem or SessionScriptPoint.WieldOff) || item.Worn && item.ScriptPoints.Count > 0))
            return new(item, $"transfer item of {item.Entity}, which has an insert-item, remove-item or wield-off script, is not modelled", GameSessionFailure.ItemScriptUnsupported);
        // ARC-MAGIC-1823: the script host answers for the shown map's rows, so an item whose row is another map's seed row is refused before anything applies, as GiveItemTo refuses it (a nested run the session cannot apply
        // is only found when the move applies: the plan cannot run it, so that refusal can leave the earlier effects of the calling run applied).
        if (itemScripts is not null && from is null && HeroListed(item.Entity) is { Origin: { } originMap } && originMap != _actor.Map &&
            item.ScriptPoints.Any(point => point is SessionScriptPoint.InsertItem or SessionScriptPoint.RemoveItem || point == SessionScriptPoint.WieldOff && item.Worn))
            return new(item, $"transfer item of {item.Entity}, whose item scripts belong to another map's row, is not modelled", GameSessionFailure.ItemScriptUnsupported);
        // ARC-MAGIC-2252: a take from an NPC of another attached map (a companion carried here) of an item that map's seed lists: Take refuses its remove-item and insert-item scripts, as the script host answers for the shown map's rows alone.
        if (itemScripts is not null && from is not null && ListedHolderKind(from) == SessionHolderKind.Npc && NpcHolderSeed(from, out var holderMap, out var holderSeed, out _) && holderMap != _actor.Map &&
            holderSeed.Items.ContainsKey(item.Entity) && item.ScriptPoints.Any(point => point is SessionScriptPoint.InsertItem or SessionScriptPoint.RemoveItem))
            return new(item, $"transfer item of {item.Entity}, whose item scripts belong to another map's row, is not modelled", GameSessionFailure.ItemScriptUnsupported);
        if (item.Worn && item.ItemSpells) return new(item, $"transfer item of {item.Entity}, worn with item spells, is not modelled", GameSessionFailure.ItemEffectUnsupported);
        if (to is not null)
        {
            // 0x4617F0 starts with the new holder's transfer script (point 32), which the session does not run, and the removal ends the item's spell runs.
            if (HolderHasScript(to, kind, SessionScriptPoint.Transfer)) return new(item, $"transfer item into {to}, which has a transfer script, is not modelled", GameSessionFailure.HolderScriptUnsupported);
            if (HasItemRuns(item.Entity)) return new(item, $"transfer item: {item.Entity} is the source of a running spell", GameSessionFailure.ItemEffectUnsupported);
        }
        else
        {
            // The hero's list takes the types its lists admit; a key goes onto the key ring when the hero holds one and it has room.
            var type = item.Kind.Type;
            if (type == SessionItemType.Key)
            {
                if (!HeroHoldsKeyRing) return new(item, "transfer item: a key goes only onto the hero's key ring", GameSessionFailure.ItemTypeUnsupported);
                if (item.KeyId is not { } key || !SessionStoryLimits.IsKey(key)) return new(item, $"transfer item: the key {item.Entity} has no valid key number", GameSessionFailure.InvalidItem);
                if (_story.HeroKeys.Count >= SessionStoryLimits.MaxKeys) return new(item, "transfer item: the hero's key ring is full", GameSessionFailure.KeyRingFull);
            }
            else if (!InventoryDomain.Admitted(type)) return new(item, $"transfer item: a {type} is not modelled", GameSessionFailure.ItemTypeUnsupported);
            else if (type == SessionItemType.Ammo && QueryHolderItems(from!)?.FirstOrDefault(listed => listed.Entity == item.Entity) is { } listedAmmo && AmmoOf(item.Entity, listedAmmo.Facts) is null)
                return new(item, $"transfer item: the ammunition {item.Entity} has no type or units", GameSessionFailure.InvalidItem);
        }
        // ARC-MAGIC-2252: the dry run of the move through the host: the failures a script run can end the move with (the insert check's and the lists' budgets are no run's: the script command goes on after them).
        if (itemScripts is not null)
        {
            var dry = DryRunTransfer(from, to, item.Entity, itemScripts);
            if (dry.Disposition == GameSessionDisposition.Rejected && dry.Failure is not (GameSessionFailure.BudgetExceeded or GameSessionFailure.InsertRefused))
                return new(item, $"transfer item of {item.Entity}: its item scripts or the hero's transfer script cannot be applied ({dry.Failure})", dry.Failure);
        }
        return new(item, null);
    }

    // ARC-MAGIC-2252: set while a move is only tried (QueryNamedItemTransfer): the scripts are run and prepared as the move runs them and the move returns before anything applies.
    private bool _transferDryRun;

    private InventoryMutationResult DryRunTransfer(string? from, string? to, string item, ISessionItemScriptHost scripts)
    {
        _transferDryRun = true;
        try { return MoveListedItem(Version, from, to, item, static _ => SessionInsertVerdict.Accepted, scripts); }
        finally { _transferDryRun = false; }
    }

    // The move TransferNamedItem makes of the item the plan found, by the move of the holder pair it is made of.
    private InventoryMutationResult MoveListedItem(GameSessionVersion expected, string? from, string? to, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        if (from is null)
            return ListedHolderKind(to!) == SessionHolderKind.Container ? GiveToContainer(to!, item, insertCheck, scripts) : GiveItemToNpc(expected, to!, item, insertCheck, scripts: scripts);
        return ListedHolderKind(from) == SessionHolderKind.Container ? LootItem(expected, from, item, insertCheck, scripts) : TakeItemFromNpc(expected, from, item, insertCheck, scripts);
    }

    // The kind of the NPC or container the first attached seed that lists the holder gives it; null for a holder none lists (the hero is no holder of a seed).
    private SessionHolderKind? ListedHolderKind(string holder)
    {
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Holders.TryGetValue(holder, out var row)) return row.Kind;
        return null;
    }

    /// <summary>
    /// ARC-SCRIPT-1279 (WO2, the script action transfer item, case 0x29 → 0x4617F0): the first item of <paramref name="from"/>'s list whose name class is <paramref name="name"/> goes to the end of <paramref name="to"/>'s
    /// list (the hero when null) under the item rules of the moves it is made of: an NPC to the hero is <see cref="TakeItemFromNpc"/>, a container to the hero <see cref="LootItem"/>, the hero to an NPC
    /// <see cref="GiveItemToNpc"/> and the hero to a container the container list's insert; with <paramref name="scripts"/> each runs the item scripts and the hero's transfer script as the original does (ARC-MAGIC-1823, ARC-MAGIC-2252). No such item changes nothing. Refused before any effect: the version, a pair, item or holder
    /// <see cref="QueryNamedItemTransfer"/> refuses (<see cref="GameSessionFailure.HolderPairUnsupported"/>, <see cref="GameSessionFailure.ItemScriptUnsupported"/>, <see cref="GameSessionFailure.WornItemUnsupported"/>,
    /// <see cref="GameSessionFailure.UnknownHolder"/>), then each move's own refusals (with <paramref name="scripts"/> the move runs the item's wield-off, remove-item and insert-item scripts, ARC-MAGIC-1823, ARC-MAGIC-2252; the host's insert check as <see cref="GameSessionFailure.InsertRefused"/>, the lists' budgets, the revision). One revision.
    /// </summary>
    public InventoryMutationResult TransferNamedItem(GameSessionVersion expected, string? from, string? to, int name, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (from is not null && !SessionValidation.Entity(from) || to is not null && !SessionValidation.Entity(to)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        var plan = QueryNamedItemTransfer(from, to, name, scripts);
        if (plan.Item is not { } item) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (plan.Refusal is not null) return RejectItem(plan.Failure);
        return MoveListedItem(expected, from, to, item.Entity, insertCheck, scripts);
    }

    // The hero's item into a container's list (the move GiveItemToNpc makes into an NPC's), after the container checks the barter window's sale makes.
    private InventoryMutationResult GiveToContainer(string container, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts) =>
        GiveItemTo(container, SessionHolderKind.Container, item, insertCheck, identify: false, scripts: scripts);

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
