using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-UI-025: the hero drops one of its items (its kit, a taken or a made item, listed or worn) on its own tile, as the inventory
    /// window's drop icon does (the transfer to no holder with flag 2 → 0x461A70, the drop 0x461AB0 with distance 0, ledger IW6, PI1):
    /// the item's drop script (point 5, the hero as triggerer), whose skip default keeps the item (<see cref="InventoryMutationResult.DeclinedBy"/>;
    /// what the run did still happens); the no-drop flag (item flag 0x20, the remove check 0x466DA0:
    /// <see cref="GameSessionFailure.ItemNotDroppable"/>); the tile (<see cref="GameSessionFailure.DropTileUnfit"/> outside the drop's
    /// bounds); then the removal (0x466E00 → 0x467860: a worn item's wield-off event 25, then remove item 28) and the placement on the
    /// actor's cell (0x466E50: a junk pile on the tile or a new one, then the decay timer). The item joins the hero's placed items and
    /// stays taken. No action points (IW8).
    /// Refused before any effect: an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an item the hero does not
    /// hold (<see cref="GameSessionFailure.ItemNotAvailable"/>, also one it placed already), a kind other than the saved one
    /// (<see cref="GameSessionFailure.InventoryMismatch"/>), an item event script without <paramref name="scripts"/>
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), an item that is the source of a spell run or a worn item with the item
    /// spells mark without <paramref name="triggers"/> (<see cref="GameSessionFailure.ItemEffectUnsupported"/>), the revision. A worn
    /// item's facts are the session's own worn record's (its no-drop flag and script points); <paramref name="item"/> gives a listed
    /// item's. With <paramref name="triggers"/>, after the move a worn item's unwear executor runs (0x4CBFC0), then the removal's spell
    /// steps (<see cref="ISessionItemTriggers.Remove"/>: its runs ended, the drop withdrawal; ledger IU9). One revision for the move.
    /// The caller places the actor on the hero's current tile first.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x461A70, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public InventoryMutationResult DropHeroItem(GameSessionVersion expected, string entity, SessionGridMoveItem item, ISessionItemScriptHost? scripts = null,
        ISessionItemTriggers? triggers = null)
    {
        var wornBefore = SessionValidation.Entity(entity) && _equipment.SlotOf(entity) is not null;
        var result = DropHero(expected, entity, item, scripts, triggers);
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.Item is null) return result;
        var spells = new List<SessionItemTriggerResult>();
        if (wornBefore) spells.Add(triggers.Withdraw(ItemSpellTriggerRules.Unwear, entity, SessionEventKinds.PlayerEntity));
        spells.Add(triggers.Remove(entity, SessionSpellCaster.Hero));
        return result with { ItemSpells = spells.AsReadOnly(), Version = Version };
    }

    private InventoryMutationResult DropHero(GameSessionVersion expected, string entity, SessionGridMoveItem item, ISessionItemScriptHost? scripts,
        ISessionItemTriggers? triggers)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        if (!SessionValidation.Entity(entity) || item.ScriptPoints is null || !Enum.IsDefined(item.Kind.Type) ||
            item.ScriptPoints.Any(point => point is < 0 or > SessionScriptPoint.Last)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (HeldKind(entity) is not { } kind) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (kind != item.Kind) return RejectItem(GameSessionFailure.InventoryMismatch);
        var slot = _equipment.SlotOf(entity);
        var worn = slot is { } wornIn ? _equipment.Worn[wornIn] : null;
        var points = (worn?.ScriptPoints ?? item.ScriptPoints).Where(point => point is SessionScriptPoint.Drop or SessionScriptPoint.RemoveItem ||
            worn is not null && point == SessionScriptPoint.WieldOff).ToHashSet();
        // A taken item's attached origin seed shows its drop script too (the critical drop's test).
        if (HeroItemDropScript(entity)) points.Add(SessionScriptPoint.Drop);
        if (scripts is null && points.Count > 0) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (triggers is null && (worn?.ItemSpells == true || HasItemRuns(entity))) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        bool Stale() => _revision != revision || _actor.Map != map || _flags.Version != flagVersion || HeldKind(entity) != kind;
        var complexity = worn?.Item.MagickTechComplexity ?? (_inventory.Weights.TryGetValue(entity, out var weight) ? weight.Complexity : 0);
        var runs = scripts is null || points.Count == 0 ? null
            : new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = complexity });
        if (runs is not null && points.Contains(SessionScriptPoint.Drop))
        {
            var gate = runs.Run(SessionScriptPoint.Drop, entity, SessionEventKinds.PlayerEntity, null);
            ThrowIfDisposed();
            if (Stale()) return RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs };
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
            if (gate?.Outcome == ScrOutcome.SkipDefault) return Declined(SessionScriptPoint.Drop, runs);
        }
        // Session policy, as the pickup's (IE1): a refusal after the drop script drops its writes (the original keeps them, PI1).
        if (item.NoDrop || worn?.Item.NoDrop == true) return RejectItem(GameSessionFailure.ItemNotDroppable) with { Scripts = runs?.Runs ?? [] };
        var cell = _actor.Cell;
        if (!DropCellFits(cell)) return RejectItem(GameSessionFailure.DropTileUnfit) with { Scripts = runs?.Runs ?? [] };
        if (runs is not null)
        {
            // The removal fires wield off for a worn place, then remove item (0x467860, IU9); the placement fires none.
            if (worn is not null && points.Contains(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, entity, SessionEventKinds.PlayerEntity, null);
            if (points.Contains(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, entity, SessionEventKinds.PlayerEntity, null);
            ThrowIfDisposed();
            if (Stale()) return RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs };
            runs.Prepare();
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
            runs.Commit();
        }
        PlaceHeroItem(entity, cell);
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, null, Version)
            { Scripts = runs?.Runs ?? [], Messages = runs?.Messages ?? [] };
    }

    /// <summary>
    /// ARC-UI-029: the hero drops <paramref name="count"/> units of one of its ammunition stacks on its own tile, as the inventory
    /// window's drop does after the quantity window (ledger QW5, IW16): the unit use with no destination (0x4658E0) takes the units from
    /// the stack (the stack goes at exactly the count, a deletion with no remove-item event, else it keeps the rest), then a new object
    /// of <paramref name="newStackKind"/> carries them (0x465A40: the original creates the type's base prototype; the host names the kind)
    /// and the drop's placement lays it on the hero's tile (0x466E50: a junk pile, then the decay timer). It joins the hero's placed items
    /// as a made item. Unlike an ordinary item's drop (<see cref="DropHeroItem"/>) the stack's drop script and no-drop flag are not
    /// consulted: the original's gold and ammunition path never reaches 0x461A70. No insert check, no events, no action points (IW8).
    /// A count of 0 changes nothing (<see cref="GameSessionDisposition.NoChange"/>), as the original's request does (QW5).
    /// Refused before any effect: an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), a negative count or a kind that is
    /// not ammunition (<see cref="GameSessionFailure.InvalidAmmo"/>), the version, a stack the hero does not hold
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), fewer units than the count (<see cref="GameSessionFailure.AmmoShort"/>),
    /// the tile (<see cref="GameSessionFailure.DropTileUnfit"/>), a full list, record budget or no free made number
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. One revision. The caller places the actor on the hero's current
    /// tile first.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x465A40, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public InventoryMutationResult DropHeroAmmo(GameSessionVersion expected, string stack, int count, SessionItemKind newStackKind)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(stack)) return RejectItem(GameSessionFailure.InvalidItem);
        if (count < 0 || newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!HeroHeld().Any(held => held.Entity == stack && held.Kind.Type == SessionItemType.Ammo)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var source = _inventory.Ammo[stack];
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        var cell = _actor.Cell;
        if (!DropCellFits(cell)) return RejectItem(GameSessionFailure.DropTileUnfit);
        var emptied = source.Units == count;
        if (emptied && !_durability.Records.ContainsKey(source.Entity) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        // The new stack's record and item; an emptied source's go.
        var freed = emptied ? 1 : 0;
        if (_inventory.Ammo.Count - freed + 1 > _limits.MaxAmmoRecords || _inventory.Held - freed + 1 > _limits.MaxPcItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        var number = NextMadeNumber();
        if (number > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        if (emptied) Delete(source.Entity);
        else _inventory.Ammo[source.Entity] = source with { Units = source.Units - count };
        var item = new SessionMadeItem(SessionMadeItem.EntityOf((int)number), newStackKind);
        _inventory.Add(item);
        _inventory.SetWeight(item.Entity, CreatedWeight(newStackKind.Prototype));
        _inventory.Ammo[item.Entity] = new(item.Entity, source.AmmoType, count);
        // The placement is the drop's (junk pile, decay) and makes the one revision.
        PlaceHeroItem(item.Entity, cell);
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, null, Version);
    }
}
