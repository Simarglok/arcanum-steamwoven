namespace Arcanum.Session;

/// <summary>
/// ARC-AI-1309: an NPC's ground pickup (the pickup goal's end 0x42A9B0 → 0x42AA70 → the transfer 0x4617F0 with the NPC as the new parent). Contract:
/// docs/world/ai-support.md, docs/world/inventory-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-AI-1309 (0x4617F0 with an NPC as the new parent, ledger NP1–NP4): moves an item lying on the shown map to the end of the NPC's list: a map seed's loose item (its own cell, a spilled container's,
    /// a critical's drop, a push's cell), one of the hero's placed items or a script-made one, or an item of a junk pile of the map (the pile's own item leaves it and its decay ends). The item keeps its
    /// entity: a seed item stays taken from its seed and joins the NPC's added items, it and the hero's kit or taken item are recorded as given (<see cref="QueryGivenItems"/>; a seed item with the shown map as its origin, the
    /// record a saved NPC list needs for an item that is no made one) so the hero's loot of it returns it to the list it came from, as <see cref="GiveItemToNpc"/> does. Ammunition merges into the NPC's first stack of its prototype (its units added, the item deleted, AM1). The insert sets the NPC's re-wield bit
    /// (NPC flag 0x40, <see cref="Arcanum.Simulation.Ai.DangerFlags.CheckWield"/>) unless it merged. One revision. Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know (<see cref="GameSessionFailure.UnknownHolder"/>), an item that is not on the ground or in a pile of the shown map,
    /// in flight or destroyed (<see cref="GameSessionFailure.ItemNotAvailable"/>), gold or keys (<see cref="GameSessionFailure.ItemTypeUnsupported"/>: the lists do not admit them), an item with a get or insert-item
    /// script, which the session does not run for an NPC (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a full list, store or ammunition budget or a sum past 32 bits
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, the host's verdict (<see cref="GameSessionFailure.InsertRefused"/>; a call back into the owner from it is
    /// <see cref="GameSessionFailure.StaleVersion"/>). NOT READY: the get and insert-item scripts of the item, and the item sound of a local picker.
    /// </summary>
    public InventoryMutationResult PickUpItemByNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (_durability.Destroyed.Contains(item) || ItemInFlight(item)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var pile = _inventory.Objects.PileOf(item);
        if (pile is not null && pile.Map != _actor.Map) return RejectItem(GameSessionFailure.ItemNotAvailable);
        // What lies there: the hero's or a script's placed item, or a seed item that is loose and not taken.
        SessionItemFacts facts;
        var placed = _inventory.PlacedItem(item);
        if (placed is not null)
        {
            if (placed.Map != _actor.Map) return RejectItem(GameSessionFailure.ItemNotAvailable);
            facts = _inventory.WithAmmo(item, InventoryDomain.MadeFacts(placed.Kind, _inventory.PlacedComplexity(placed)));
            // The scripts of a placed item are its origin seed row's (as TakePlaced reads them); a kit or made item has none the session knows.
            if (placed.Origin is { } origin && _inventory.Seeds.TryGetValue(origin, out var originSeed) && originSeed.Items.TryGetValue(item, out var listed) &&
                (listed.Facts.HasScript(SessionScriptPoint.Get) || listed.Facts.HasScript(SessionScriptPoint.InsertItem)))
                return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        }
        else
        {
            if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
            if (!seed.Items.TryGetValue(item, out var row) || _inventory.Taken.Contains(item) || LooseCell(_actor.Map, seed, item) is null)
                return RejectItem(GameSessionFailure.ItemNotAvailable);
            facts = _inventory.WithAmmo(item, row.Facts);
            if (pile is not null && LooseCell(_actor.Map, seed, item) != pile.Cell) return RejectItem(GameSessionFailure.ItemNotAvailable);
        }
        if (pile is not null && !pile.Items.Any(entry => entry.Entity == item)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (!InventoryDomain.Admitted(facts.Kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (facts.HasScript(SessionScriptPoint.Get) || facts.HasScript(SessionScriptPoint.InsertItem)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // Ammunition merges into the NPC's stack of the prototype (AM1), else it enters the list as a stack of its own.
        SessionAmmoRecord? ammo = null, into = null;
        if (facts.Kind.Type == SessionItemType.Ammo)
        {
            if (AmmoOf(item, facts) is not { } brought) return RejectItem(GameSessionFailure.InvalidItem);
            ammo = brought;
            into = NpcStacks(npc).Where(pair => pair.Prototype == facts.Kind.Prototype).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault();
            if (into is { } target)
            {
                if ((long)target.Units + brought.Units > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
                if (!_inventory.Ammo.ContainsKey(target.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
                if (!_durability.Records.ContainsKey(item) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            }
            else if (!_inventory.Ammo.ContainsKey(item) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        }
        if (into is null && (_inventory.AddedTo(npc).Count + SeedItemCount(npc) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems))
            return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, seeds, map) = (_revision, _inventory.SeedGeneration, _actor.Map);
        var verdict = insertCheck(new SessionInventoryItem(item, facts));
        ThrowIfDisposed();
        // The callback changed nothing the move rests on: the revision, the attached seeds, the shown map, the item's place.
        if (_revision != revision || _inventory.SeedGeneration != seeds || _actor.Map != map || _inventory.PlacedItem(item) != placed ||
            !ReferenceEquals(_inventory.Objects.PileOf(item), pile) || !KnownNpc(npc) || _durability.Destroyed.Contains(item))
            return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        if (into is { } stack)
        {
            // The inserted stack's units join the held one and the inserted item is deleted (from a pile too, DestroyObject takes it out).
            DestroyObject(item);
            _inventory.Ammo[stack.Entity] = stack with { Units = stack.Units + ammo!.Value.Units };
        }
        else
        {
            if (placed is not null)
            {
                _inventory.Hero.Remove(item);
                // A kit or taken item remembers where it came from, so the hero's loot of it from the NPC returns it there.
                if (placed.Source is SessionPlacedSource.Kit or SessionPlacedSource.Pc) _inventory.Hero.SetGiven(new(item, placed.Origin));
            }
            else
            {
                // A seed item stays taken (no seed shows it) and keeps its seed's weight facts wherever it goes. It is recorded as given from the shown map, the record a saved NPC list needs for an item that is
                // no made one, so the hero's loot of it from the NPC returns it to the taken items with that origin.
                _inventory.Taken.Add(item);
                _inventory.SetWeight(item, facts.WeightFacts);
                _inventory.Hero.SetGiven(new(item, _actor.Map));
            }
            // A given entry keeps no complexity of its own (a saved NPC list refuses one; its seed answers), a made or created item keeps its.
            var given = placed is null || placed.Source is SessionPlacedSource.Kit or SessionPlacedSource.Pc;
            _inventory.AddToNpc(npc, new SessionMadeItem(item, facts.Kind, given ? 0 : facts.Complexity));
            if (ammo is { } added) _inventory.Ammo[item] = added;
            MarkCheckWield(npc);
        }
        if (pile is not null) _inventory.Objects.LeaveItem(item);
        CancelDecay(item);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, into?.Entity ?? item, verdict, Version);
    }
}
