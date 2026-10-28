namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-055: the session's store of NPC inventory changes, keyed by the NPC's entity identity (the entity of its home map), so
/// one NPC's changes follow it wherever it stands, as the original carries a follower with its inventory (ledger NI1–NI5). A
/// change is a made item added to the NPC (<c>made:n</c>, numbered with the hero's made items) or an item of its list deleted
/// (the durability deletion, ARC-WORLD-053). An item the PC takes from the NPC (loot, negative <c>IN</c>) leaves it as before;
/// a made item then joins the hero's made items. <see cref="QueryMapInventory"/> lists each NPC's seed items and then its
/// added items. Contract: docs/world/inventory-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>Detached copy of the made items the session added to an NPC, in the order they entered its list (empty when none).
    /// Throws for an invalid entity id.</summary>
    public IReadOnlyList<SessionMadeItem> QueryNpcInventory(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        // ARC-WORLD-071: the kit and taken items the hero gave the NPC are its list's too, but not made items (QueryGivenItems).
        return Array.AsReadOnly(_inventory.AddedTo(npc).Where(item => !_inventory.Given.ContainsKey(item.Entity)).ToArray());
    }

    /// <summary>
    /// Adds a new made item of <paramref name="kind"/> to the end of an NPC's list (the creation 0x4ED8B0 and the transfer 0x4617F0
    /// into the NPC, as the NPC crafter's product enters its inventory). The NPC must be known to the session: an NPC holder of an
    /// attached seed, a party record, or an NPC that already holds added items (<see cref="GameSessionFailure.UnknownHolder"/>);
    /// its life is the caller's concern. The item is <c>made:n</c>, one past every made number the session knows. Refused before
    /// any effect: an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an item type the lists do not
    /// admit (<see cref="GameSessionFailure.ItemTypeUnsupported"/>: gold, keys, key rings), ammunition without its type and units
    /// or with a type out of 0..3 or negative units (<see cref="GameSessionFailure.InvalidAmmo"/>), a full list
    /// (<see cref="SessionMapInventorySeed.MaxListLength"/> with the NPC's current seed items) or store
    /// (<see cref="GameSessionLimits.MaxNpcItems"/>) or no free made number (<see cref="GameSessionFailure.BudgetExceeded"/>), the
    /// revision, then the host's insert check for the NPC (<see cref="GameSessionFailure.InsertRefused"/>; a call back into the
    /// owner from it, a seed attach included, is <see cref="GameSessionFailure.StaleVersion"/>). One revision. ARC-WORLD-066: ammunition
    /// (<paramref name="ammo"/>: its type and units) merges into the NPC's stack of the same prototype when it holds one (the stack's
    /// units grow, no item is added: <see cref="InventoryMutationResult.Item"/> names the stack), as the original's insertion does;
    /// a sum that does not fit 32 bits is refused (<see cref="GameSessionFailure.BudgetExceeded"/>).
    /// </summary>
    public InventoryMutationResult AddItemToNpc(GameSessionVersion expected, string npc, SessionItemKind kind, SessionInsertCheck insertCheck,
        (int AmmoType, int Units)? ammo = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !Enum.IsDefined(kind.Type))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (!InventoryDomain.Admitted(kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if ((kind.Type == SessionItemType.Ammo) != ammo.HasValue || ammo is { } given && (given.AmmoType is < 0 or >= SessionAmmoRecord.AmmoTypes || given.Units < 0))
            return RejectItem(GameSessionFailure.InvalidAmmo);
        if (ammo is { } units && NpcStacks(npc).Where(pair => pair.Prototype == kind.Prototype).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault() is { } stack)
            return MergeIntoNpc(npc, stack, units.Units, insertCheck, kind);
        var added = _inventory.AddedTo(npc).Count;
        if (added + SeedItemCount(npc) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems ||
            ammo is not null && _inventory.Ammo.Count >= _limits.MaxAmmoRecords)
            return RejectItem(GameSessionFailure.BudgetExceeded);
        var number = NextMadeNumber();
        if (number > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var item = new SessionMadeItem(SessionMadeItem.EntityOf((int)number), kind);
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        var verdict = insertCheck(new SessionInventoryItem(item.Entity, InventoryDomain.MadeFacts(kind)));
        ThrowIfDisposed();
        // A seed attached from the callback moves no revision but may change what the NPC is or how long its list is.
        if (_revision != revision || _inventory.SeedGeneration != seeds) return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        _inventory.AddToNpc(npc, item);
        _inventory.Taken.Add(item.Entity);
        if (ammo is { } kept) _inventory.Ammo[item.Entity] = new(item.Entity, kept.AmmoType, kept.Units);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, verdict, Version);
    }

    // ARC-WORLD-066: ammunition added to an NPC that holds a stack of its prototype only grows that stack (the inserted object is
    // destroyed in the original); the host's insert check still answers for the NPC.
    private InventoryMutationResult MergeIntoNpc(string npc, SessionAmmoRecord stack, int units, SessionInsertCheck insertCheck, SessionItemKind kind)
    {
        if ((long)stack.Units + units > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (!_inventory.Ammo.ContainsKey(stack.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        var verdict = insertCheck(new SessionInventoryItem(stack.Entity, InventoryDomain.MadeFacts(kind) with { AmmoType = stack.AmmoType, Quantity = units }));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.SeedGeneration != seeds) return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        _inventory.Ammo[stack.Entity] = stack with { Units = stack.Units + units };
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, stack.Entity, verdict, Version);
    }

    /// <summary>
    /// Deletes an item from an NPC's current list (the object destruction 0x43CCA0, as the NPC crafter's components are consumed):
    /// a made item the session added to the NPC, or an item of its list in an attached seed that is still there. The deletion is
    /// the durability one (ARC-WORLD-053): the item is hidden from every seed, its condition record dropped and it is recorded as
    /// deleted (<see cref="IsItemDestroyed"/>). No item script runs. Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know
    /// (<see cref="GameSessionFailure.UnknownHolder"/>), an item not in its current list (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// a worn item (<see cref="GameSessionFailure.WornItemUnsupported"/>: unequipping an NPC is not modelled), a full durability
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. One revision.
    /// </summary>
    public InventoryMutationResult RemoveItemFromNpc(GameSessionVersion expected, string npc, string item)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (_inventory.NpcOf(item) != npc)
        {
            var listed = SeedListing(npc, item);
            if (listed is null) return RejectItem(GameSessionFailure.ItemNotAvailable);
            if (listed.Facts.Worn) return RejectItem(GameSessionFailure.WornItemUnsupported);
        }
        if (!_durability.Records.ContainsKey(item) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        Delete(item);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item, null, Version);
    }

    /// <summary>An NPC holder of an attached seed, a party record, or an NPC holding added items.</summary>
    private bool KnownNpc(string npc) => _inventory.Npcs.ContainsKey(npc) || _party.Records.ContainsKey(npc) ||
        _inventory.Seeds.Values.Any(seed => seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc);

    /// <summary>The NPC's seed items still in its list, in every attached seed that lists it as an NPC.</summary>
    private int SeedItemCount(string npc) => _inventory.Seeds.Values
        .Sum(seed => seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc
            ? holder.Items.Count(entity => !_inventory.Taken.Contains(entity) && !Dropped(entity)) : 0);

    /// <summary>The seed item of an attached seed that lists <paramref name="item"/> in the NPC's list and still holds it, or null.</summary>
    private SessionSeedItem? SeedListing(string npc, string item)
    {
        if (_inventory.Taken.Contains(item) || Dropped(item)) return null;
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Holders.TryGetValue(npc, out var holder) && holder.Kind == SessionHolderKind.Npc &&
                seed.HolderOf.TryGetValue(item, out var of) && of == npc) return seed.Items[item];
        return null;
    }
}
