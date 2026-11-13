using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-071: NPC gold as counted piles and the moves between the hero and an NPC that the pick-pocket skill (task S15) and the
/// barter window (ARC-UI-050) share, read in the executable (ledger NT1–NT4): gold coins in part (0x464830), an item of the hero's into an
/// NPC's list and back (0x4617F0 → 0x461CA0), and ammunition units in part (0x4658E0, AM4). No prices and no skill: the caller decides how
/// many coins or units move. Each call is one revision or refused before any effect. Contract: docs/world/inventory-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The coins of the NPC's gold pile (its first pile in list order, the pile the original's field 0xE8 names after an
    /// insertion, AM2): its seed pile's coins or the tracked ones, or a pile the session created; 0 without a pile. Null for an NPC the
    /// session does not know. A second pile (a made pile added while a seed with its own pile was not attached) is not counted until the
    /// first is gone. Throws for an invalid entity id.</summary>
    public int? QueryNpcGold(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        return KnownNpc(npc) ? NpcPile(npc)?.Quantity ?? 0 : null;
    }

    /// <summary>Whether the NPC has a gold pile at all (the original's gold stack field 0xE8 is not null, AM5), even one of 0 coins; false for an
    /// NPC the session does not know. The dialog's <c>$$ −n</c> tells a payer without a stack (the transfer then credits the destination from
    /// nothing, GM2) from one whose stack holds too little. Throws for an invalid entity id.</summary>
    public bool QueryNpcHasGoldPile(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        return KnownNpc(npc) && NpcPile(npc) is not null;
    }

    /// <summary>
    /// Moves <paramref name="count"/> coins from the NPC's gold pile to the hero (0x464830 with the hero as destination, NT1): a pile
    /// with exactly that many is destroyed (the durability deletion), a larger one keeps the rest. The hero's gold grows with no insert
    /// check. Refused before any effect: an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session
    /// does not know (<see cref="GameSessionFailure.UnknownHolder"/>), no hero to hold gold (<see cref="GameSessionFailure.GoldUnavailable"/>),
    /// a pile with fewer coins or none (<see cref="GameSessionFailure.GoldShort"/>, the original's 0 with nothing changed), a pile with an
    /// insert-item script (the event the original fires on it first, not run here: <see cref="GameSessionFailure.ItemScriptUnsupported"/>),
    /// a sum past 32 bits or a full record budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. A count of 0 changes
    /// nothing. The caller limits the count: a steal moves at most 100 (PP3), a merchant pays at most what it holds (NT4).
    /// ARC-WORLD-101: <paramref name="npc"/> may also be a standing container of an attached seed (its loot window), and
    /// <paramref name="pile"/> names the pile of the holder's current list to take from (the original's 0x464830 with the pile given;
    /// one the list does not hold is <see cref="GameSessionFailure.ItemNotAvailable"/>); without it the holder's first pile.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x464830, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public InventoryMutationResult TakeGoldFromNpc(GameSessionVersion expected, string npc, int count, string? pile = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || count < 0 ||
            pile is not null && !SessionValidation.Entity(pile))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        var kind = KnownNpc(npc) ? SessionHolderKind.Npc : KnownContainer(npc) ? SessionHolderKind.Container : (SessionHolderKind?)null;
        if (kind is not { } holderKind) return RejectItem(GameSessionFailure.UnknownHolder);
        if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
        var piles = HolderPiles(npc, holderKind).ToArray();
        if (pile is not null && !piles.Any(found => found.Entity == pile)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        var chosen = pile is null ? piles.Select(found => ((string Entity, int Quantity, SessionItemFacts? Facts)?)found).FirstOrDefault() : piles.First(found => found.Entity == pile);
        return TakeGold(chosen, count);
    }

    private InventoryMutationResult TakeGold((string Entity, int Quantity, SessionItemFacts? Facts)? from, int count)
    {
        if (from is not { } pile || pile.Quantity < count) return RejectItem(GameSessionFailure.GoldShort);
        if (pile.Facts?.HasScript(SessionScriptPoint.InsertItem) == true) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if ((long)_inventory.Gold + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        var emptied = pile.Quantity == count;
        if (emptied ? !_durability.Records.ContainsKey(pile.Entity) && _durability.Count >= _limits.MaxDurabilityRecords
            : !_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        // ARC-WORLD-1132: a hero with no gold pile and no free grid place for one gets no coins, the new pile stays on the ground.
        GoldGround? ground = null;
        if (HeroGoldNeedsGround())
        {
            var failure = PlanGoldGround(pile.Facts?.Kind ?? SessionHeroGrid.GoldKind, out var planned, !emptied && !_inventory.Piles.ContainsKey(pile.Entity) ? 1 : 0);
            if (failure != GameSessionFailure.None) return RejectItem(failure);
            ground = planned;
        }
        if (ground is { } onGround) LeaveGoldOnGround(onGround, count);
        else _inventory.Gold += count;
        if (emptied) DestroyObject(pile.Entity);
        else _inventory.Piles[pile.Entity] = pile.Quantity - count;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, pile.Entity, null, Version);
    }

    /// <summary>
    /// The hero gets <paramref name="count"/> coins from nothing (0x464830 with no source object, the dialog's <c>$$ n</c> with n &gt; 0,
    /// ARC-DEV-179, ledger GM1): the source's call credits the destination whether or not a source exists, and the hero's gold grows with no
    /// insert check when the hero already holds a gold stack; a hero without one gets a new gold object moved in through the insert path (0x4617F0), whose
    /// room test (ARC-WORLD-1132) leaves the pile on the hero's tile (<see cref="InventoryMutationResult.Item"/> names it) when the grid has no place for it, the
    /// call still succeeding (gold weighs nothing, so only the room is checked). Refused before any effect: a negative count (<see cref="GameSessionFailure.InvalidItem"/>), the version, no hero to hold
    /// gold (<see cref="GameSessionFailure.GoldUnavailable"/>), a sum past 32 bits (<see cref="GameSessionFailure.BudgetExceeded"/>), the
    /// revision. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult GiveGoldToHero(GameSessionVersion expected, int count)
    {
        ThrowIfDisposed();
        if (count < 0) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if ((long)_inventory.Gold + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        // ARC-WORLD-1132: no pile and no free grid place for one: the new pile lies on the hero's tile (a plain created object) and the call succeeds.
        if (HeroGoldNeedsGround())
        {
            var failure = PlanGoldGround(SessionHeroGrid.GoldKind, out var ground);
            if (failure != GameSessionFailure.None) return RejectItem(failure);
            var left = LeaveGoldOnGround(ground, count);
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, left, null, Version);
        }
        _inventory.Gold += count;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, Version);
    }

    /// <summary>
    /// An NPC gets <paramref name="count"/> coins from nothing (0x464830 with a source that holds no gold stack, the dialog's <c>$$ −n</c> asking a
    /// payer without a stack, ARC-DEV-179, ledger GM2: the call skips the debit and credits the destination in full). The NPC's first pile grows
    /// with no insert check; an NPC without one gets a new <c>made:n</c> pile of <paramref name="pileKind"/> after <paramref name="insertCheck"/>
    /// (<see cref="GiveGoldToNpc"/>'s rules, but no coins leave the hero). Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know (<see cref="GameSessionFailure.UnknownHolder"/>),
    /// a sum past 32 bits or a full pile, list or store budget (<see cref="GameSessionFailure.BudgetExceeded"/>), a destination with a transfer
    /// script when it needs a new pile (<see cref="GameSessionFailure.HolderScriptUnsupported"/>), the host's verdict
    /// (<see cref="GameSessionFailure.InsertRefused"/>), the revision. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult GiveGoldToNpcFromNothing(GameSessionVersion expected, string npc, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || count < 0 || pileKind.Type != SessionItemType.Gold)
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (NpcPile(npc) is { } pile)
        {
            if ((long)pile.Quantity + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (!_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
            _inventory.Piles[pile.Entity] = pile.Quantity + count;
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, pile.Entity, null, Version);
        }
        if (_inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal);
        if (created is null) return refusal!.Value;
        _inventory.Piles[created.Value.Entity] = count;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, created.Value.Entity, SessionInsertVerdict.Accepted, Version);
    }

    /// <summary>
    /// Moves <paramref name="count"/> coins from one NPC's gold pile to another's (0x464830 with an NPC as source and as destination, a
    /// follower's payment in the dialog's <c>$$ −n</c>, ARC-DEV-179, ledger GM2): the source's first pile must hold at least the count (else
    /// <see cref="GameSessionFailure.GoldShort"/>, the original's 0 with nothing changed: the call never moves part of what it was asked to),
    /// a pile with exactly that many is destroyed, a larger one keeps the rest; the destination's first pile grows with no insert check, and
    /// a destination without one gets a new <c>made:n</c> pile of <paramref name="pileKind"/> after <paramref name="insertCheck"/>
    /// (<see cref="GiveGoldToNpc"/>'s rules). Refused before any effect: an invalid input or the same NPC twice
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know (<see cref="GameSessionFailure.UnknownHolder"/>),
    /// a source pile with an insert-item script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a sum past 32 bits or a full pile,
    /// durability, list or store budget (<see cref="GameSessionFailure.BudgetExceeded"/>), a destination with a transfer script when it needs
    /// a new pile (<see cref="GameSessionFailure.HolderScriptUnsupported"/>), the host's verdict (<see cref="GameSessionFailure.InsertRefused"/>),
    /// the revision. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult MoveGoldBetweenNpcs(GameSessionVersion expected, string from, string to, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(from) || !SessionValidation.Entity(to) || from == to || from.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) ||
            to.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || count < 0 || pileKind.Type != SessionItemType.Gold)
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(from) || !KnownNpc(to)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (NpcPile(from) is not { } source || source.Quantity < count) return RejectItem(GameSessionFailure.GoldShort);
        if (source.Facts?.HasScript(SessionScriptPoint.InsertItem) == true) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        var emptied = source.Quantity == count;
        var target = NpcPile(to);
        if (target is { } grown && (long)grown.Quantity + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (emptied && !_durability.Records.ContainsKey(source.Entity) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        // The pile records after the move: the source's goes when it is destroyed or stays when it keeps coins, the target's is added unless held.
        var records = _inventory.Piles.Count - (emptied && _inventory.Piles.ContainsKey(source.Entity) ? 1 : 0)
            + (!emptied && !_inventory.Piles.ContainsKey(source.Entity) ? 1 : 0)
            + (target is null || !_inventory.Piles.ContainsKey(target.Value.Entity) ? 1 : 0);
        if (records > _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        string targetPile;
        if (target is { } existing) targetPile = existing.Entity;
        else
        {
            var created = NewNpcEntry(to, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal);
            if (created is null) return refusal!.Value;
            targetPile = created.Value.Entity;
        }
        if (emptied) DestroyObject(source.Entity);
        else _inventory.Piles[source.Entity] = source.Quantity - count;
        _inventory.Piles[targetPile] = (target?.Quantity ?? 0) + count;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, targetPile, null, Version);
    }

    /// <summary>
    /// Moves <paramref name="count"/> of the hero's coins to the NPC (0x464830 with the NPC as destination, NT1): its gold pile grows
    /// with no insert check; an NPC without a pile gets a new one, a <c>made:n</c> item of <paramref name="pileKind"/> (the gold prototype,
    /// the host's) at the end of its list, after the host's insert check for the NPC. The original then leaves a refused new pile on the
    /// ground and still reports success; here a refusal moves nothing (<see cref="GameSessionFailure.InsertRefused"/>). Refused before any
    /// effect, beyond <see cref="TakeGoldFromNpc"/>'s input, version, holder and hero checks: a kind that is not gold
    /// (<see cref="GameSessionFailure.InvalidItem"/>), fewer coins than the count (<see cref="GameSessionFailure.GoldShort"/>), a sum past 32
    /// bits, a full list, store or record budget, or no free made number (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. A
    /// count of 0 changes nothing.
    /// </summary>
    public InventoryMutationResult GiveGoldToNpc(GameSessionVersion expected, string npc, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || count < 0 || pileKind.Type != SessionItemType.Gold)
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (_inventory.Gold < count) return RejectItem(GameSessionFailure.GoldShort);
        if (NpcPile(npc) is { } pile)
        {
            if ((long)pile.Quantity + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (!_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
            _inventory.Gold -= count;
            _inventory.Piles[pile.Entity] = pile.Quantity + count;
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, pile.Entity, null, Version);
        }
        if (_inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal);
        if (created is null) return refusal!.Value;
        _inventory.Gold -= count;
        _inventory.Piles[created.Value.Entity] = count;
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, created.Value.Entity, SessionInsertVerdict.Accepted, Version);
    }

    /// <summary>
    /// Moves one of the hero's items (its kit, a taken or a made item; not one it placed on the ground) to the end of the NPC's list
    /// (0x4617F0 → 0x461CA0, NT2, NT3), after the host's insert check for the NPC: a worn item leaves its slot; ammunition merges into the
    /// NPC's first stack of its prototype (its units added, the moved stack deleted, AM1). The item keeps its entity: a kit or taken item is
    /// recorded as given (<see cref="QueryGivenItems"/>), so taking it back returns it to the list it came from. Refused before any effect:
    /// an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know
    /// (<see cref="GameSessionFailure.UnknownHolder"/>), an item the hero does not hold (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// an NPC with a transfer script (point 32, which can veto in the original; not run here:
    /// <see cref="GameSessionFailure.HolderScriptUnsupported"/>), a taken item whose attached origin seed shows a remove-item, insert-item
    /// or (worn) wield-off script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a full list, store or durability budget or a sum
    /// past 32 bits (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, the host's verdict
    /// (<see cref="GameSessionFailure.InsertRefused"/>; a call back into the owner from it is <see cref="GameSessionFailure.StaleVersion"/>).
    /// ARC-WORLD-1283: an item with the no-drop flag (item flag 0x20; the worn record's, the origin seed row's, else its prototype's through <see cref="AttachItemNoDrop"/>) is refused by the original's remove check (0x466DA0) after the transfer script and before the insert check: <see cref="GameSessionFailure.ItemNotDroppable"/> with <see cref="InventoryMutationResult.PcMessage"/> 111, nothing changed. The NPC flag 0x40 (re-wield) is not modelled. One revision.
    /// ARC-MAGIC-1743 (ledger IE3, IE4, IE6): with <paramref name="scripts"/> a worn item's wield-off (25) and the item's remove-item (28) scripts run with the hero as the triggerer and its insert-item (21) with the NPC as the triggerer
    /// (<see cref="InventoryMutationResult.Scripts"/>, <see cref="InventoryMutationResult.InsertScript"/>), before the move is applied; a run the session cannot apply refuses the move with nothing changed; without a host an item with
    /// such a script is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>). The NPC's own wield on the receiving side is its re-wield (flag 0x40), not this call.
    /// ARC-WORLD-1284: a moved item that is not merged marks the NPC with its re-wield bit (NPC flag 0x40, <see cref="Arcanum.Simulation.Ai.DangerFlags.CheckWield"/>). One revision.
    /// ARC-MAGIC-022: an item that is the source of a spell run needs <paramref name="triggers"/> (else
    /// <see cref="GameSessionFailure.ItemEffectUnsupported"/>); with one, the removal's spell steps run after the move
    /// (<see cref="ISessionItemTriggers.Remove"/>, <see cref="InventoryMutationResult.ItemSpells"/>).
    /// </summary>
    public InventoryMutationResult GiveItemToNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck,
        ISessionItemTriggers? triggers = null, ISessionItemScriptHost? scripts = null)
    {
        var result = GiveToNpc(expected, npc, item, insertCheck, triggers, scripts);
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed) return result;
        return result with { ItemSpells = [triggers.Remove(item, SessionSpellCaster.Hero)], Version = Version };
    }

    private InventoryMutationResult GiveToNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck, ISessionItemTriggers? triggers,
        ISessionItemScriptHost? scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        return GiveItemTo(npc, SessionHolderKind.Npc, item, insertCheck, identify: false, triggers, scripts);
    }

    // The move of GiveItemToNpc after its input, version and holder checks, into an NPC's or (ARC-WORLD-090) a container's list;
    // identify marks the moved item identified in the same revision (a barter window's sale, BT5); without triggers (ARC-MAGIC-022) an
    // item that is the source of a spell run is refused.
    private InventoryMutationResult GiveItemTo(string npc, SessionHolderKind kind, string item, SessionInsertCheck insertCheck, bool identify,
        ISessionItemTriggers? triggers = null, ISessionItemScriptHost? scripts = null)
    {
        if (HeroListed(item) is not { } held) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (HolderHasScript(npc, kind, SessionScriptPoint.Transfer)) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
        // ARC-WORLD-1283 (NT2, VP5, VPG2): the recipient's transfer script is the first gate of 0x461CA0, the item's remove check (0x466DA0) the second: the no-drop flag refuses
        // the hero's item (worn or not) with message 111 before any other check, so a barter sale, a plant or a hand-over of such an item changes nothing.
        if (HeroItemNoDrop(item, held.Kind, held.Origin)) return NoDropRefusal();
        var worn = _equipment.SlotOf(item) is not null;
        // ARC-MAGIC-1743 (IE3, IE4, IE6): the item event scripts of the move, from the worn record's points (a kit or made item too) and the origin seed row's. The removal runs wield off (25, only a worn
        // item) and remove item (28) with the hero as the triggerer, the insertion insert item (21) with the recipient as the triggerer; without a script host an item with any of them is refused.
        var wornPoints = _equipment.SlotOf(item) is { } wornIn ? _equipment.Worn[wornIn].ScriptPoints : [];
        SessionSeedItem? listed = held.Origin is { } originMap && _inventory.Seeds.TryGetValue(originMap, out var originSeed) && originSeed.Items.TryGetValue(item, out var givenRow) ? givenRow : null;
        bool Has(int point) => wornPoints.Contains(point) || listed?.Facts.HasScript(point) == true;
        var (wieldOff, removeOwn, insertOwn) = (worn && Has(SessionScriptPoint.WieldOff), Has(SessionScriptPoint.RemoveItem), Has(SessionScriptPoint.InsertItem));
        if (scripts is null && (wornPoints.Count > 0 || wieldOff || removeOwn || insertOwn)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // The script host answers for the shown map's rows, so the scripts of an item whose row is another map's seed row are not run: refused, never moved with a script skipped.
        if (held.Origin is { } rowMap && rowMap != _actor.Map && (wieldOff || removeOwn || insertOwn)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // ARC-MAGIC-022: a worn item with the item spells mark would leave without its unwear (0x4CBFC0).
        if (_equipment.SlotOf(item) is { } markedIn && _equipment.Worn[markedIn].ItemSpells) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        // ARC-MAGIC-022: the removal ends the item's runs and runs its drop (IU9) only through an item trigger host.
        if (triggers is null && HasItemRuns(item)) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        // ARC-WORLD-083: a made item keeps its complexity in the NPC's list, so taking it back can refuse it (a taken item's seed answers).
        var complexity = held.Origin is { } takenFrom && _inventory.Seeds.TryGetValue(takenFrom, out var takenSeed) && takenSeed.Items.TryGetValue(item, out var seedRow)
            ? seedRow.Facts.Complexity : held.Complexity;
        // ARC-WORLD-085: the host's insert check for the NPC sees the item's weight facts.
        var weights = _inventory.Weights.TryGetValue(item, out var row) ? row : default;
        var facts = _inventory.WithAmmo(item, InventoryDomain.MadeFacts(held.Kind, complexity) with { Weight = weights.Weight, MagicWeight = weights.MagicWeight });
        // ARC-WORLD-090: a container's stack is found only where its lookup finds one (AMA5, ContainerStackOfType).
        var into = held.Kind.Type != SessionItemType.Ammo ? null : kind == SessionHolderKind.Container
            ? _rules.Items.ContainerStackLookupFails ? null
                : ContainerStacks(npc).Where(pair => pair.Prototype == held.Kind.Prototype).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault()
            : NpcStacks(npc).Where(pair => pair.Prototype == held.Kind.Prototype).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault();
        if (into is { } stack)
        {
            var units = _inventory.Ammo[item].Units;
            if ((long)stack.Units + units > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (!_inventory.Ammo.ContainsKey(stack.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (!_durability.Records.ContainsKey(item) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        }
        else if (_inventory.AddedTo(npc).Count + SeedItemCount(npc, kind) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems)
            return RejectItem(GameSessionFailure.BudgetExceeded);
        if (identify && into is null && !_inventory.Identified.Contains(item) && _inventory.Identified.Count >= SessionInventoryState.MaxIdentified)
            return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        var verdict = insertCheck(new SessionInventoryItem(item, facts));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.SeedGeneration != seeds) return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        ItemScriptRuns? runs = null;
        ScrRunResult? insertRun = null;
        if (scripts is not null && (wieldOff || removeOwn || insertOwn))
        {
            // The original moves the item first and then runs these scripts, whose results it ignores (IE3, IE4, IF10); the admitted commands read nothing the move changes, so the runs are made
            // before the commit and applied with it, or the whole move is refused.
            var (shown, flagVersion) = (_actor.Map, _flags.Version);
            runs = new ItemScriptRuns(this, scripts, shown.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = complexity });
            if (wieldOff) runs.Run(SessionScriptPoint.WieldOff, item, SessionEventKinds.PlayerEntity, null);
            if (runs.Failure == GameSessionFailure.None && removeOwn) runs.Run(SessionScriptPoint.RemoveItem, item, SessionEventKinds.PlayerEntity, null);
            // The insertion's triggerer is the recipient: an NPC reads its own printed line variant; a container is no critter, so its run keeps the hero's reading.
            runs.NpcTriggerer = kind == SessionHolderKind.Npc;
            if (runs.Failure == GameSessionFailure.None && insertOwn) insertRun = runs.Run(SessionScriptPoint.InsertItem, item, npc, null);
            ThrowIfDisposed();
            if (_revision != revision || _actor.Map != shown || _flags.Version != flagVersion) return RejectItem(GameSessionFailure.StaleVersion, verdict) with { InsertScript = insertRun, Scripts = runs.Runs };
            runs.Prepare();
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure, verdict) with { InsertScript = insertRun, Scripts = runs.Runs };
        }
        if (into is { } target)
        {
            var units = _inventory.Ammo[item].Units;
            DestroyObject(item);
            _inventory.Ammo[target.Entity] = target with { Units = target.Units + units };
        }
        else
        {
            if (_equipment.SlotOf(item) is { } slot) _equipment.Worn.Remove(slot);
            _inventory.Hero.Remove(item);
            // The item stays taken (no seed shows it); a kit or taken item remembers where it came from.
            if (kind == SessionHolderKind.Container) _inventory.AddToContainer(npc, new SessionMadeItem(item, held.Kind, held.Complexity));
            else
            {
                _inventory.AddToNpc(npc, new SessionMadeItem(item, held.Kind, held.Complexity));
                StoreInsertPlace(npc, item);
                // ARC-WORLD-1284 (VPG1): the insert sets NPC flag 0x40 on the receiver; a merge into a held stack (above) returns before the flag.
                MarkCheckWield(npc);
            }
            if (held.Source != SessionPlacedSource.Made) _inventory.Hero.SetGiven(new(item, held.Origin));
            if (identify) _inventory.Identified.Add(item);
        }
        runs?.Commit();
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, into?.Entity ?? item, verdict, Version)
            { InsertScript = insertRun, Scripts = runs?.Runs ?? [], Messages = runs?.Messages ?? [] };
    }

    /// <summary>
    /// Moves an item of an NPC's current list in the seed of the actor's map, or (ARC-DEV-087, MIG6e) of the other attached map that lists the NPC (alive or not: life is the caller's concern) to the hero (0x4617F0 with the hero as the new parent, NT2, NT3), as
    /// a steal or a purchase does: a seed item, a made item the session added to it, or an item the hero gave it (which returns to the list
    /// it came from). The item rules and the commit are <see cref="LootItem"/>'s (the insert check, the Technophobia refusal, the
    /// insert-item script, the ammunition merge), without its dead-holder and use-script conditions. Refused before any effect: an invalid
    /// input, the version, no seed for the actor's map (<see cref="GameSessionFailure.InventoryUnavailable"/>), a holder that is not an NPC of
    /// it (<see cref="GameSessionFailure.UnknownHolder"/>), an item not in its current list (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// gold (<see cref="TakeGoldFromNpc"/>) or, for a session without a hero, a key (<see cref="GameSessionFailure.ItemTypeUnsupported"/>; a key otherwise folds into the hero's key list, ARC-WORLD-123), and the rules of
    /// <see cref="LootItem"/>. One revision. ARC-MAGIC-022: with <paramref name="triggers"/> the pickup executor runs after the move
    /// (<see cref="InventoryMutationResult.ItemSpells"/>).
    /// </summary>
    public InventoryMutationResult TakeItemFromNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null, ISessionItemTriggers? triggers = null) => PickedUp(TakeFromNpc(expected, npc, item, insertCheck, scripts), triggers);

    private InventoryMutationResult TakeFromNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        // ARC-DEV-087 (MIG6e): an NPC of another attached map (a companion carried here) moves its item from that map's seed.
        if (!NpcHolderSeed(npc, out var map, out var seed, out _)) return RejectItem(UnseededHolder());
        if (_inventory.AddedTo(npc).FirstOrDefault(added => added.Entity == item) is { Entity: not null } added)
            return Take(seed, new(item, _inventory.Worn(npc, item, _inventory.AddedFacts(added))), npc, insertCheck, scripts, map);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != npc || _inventory.Taken.Contains(item) || DroppedCell(map, seed, item) is not null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        // ARC-WORLD-103: worn as the session's lists show it (a dressed NPC's record over its map data).
        return Take(seed, new(item, _inventory.Worn(npc, item, _inventory.WithAmmo(item, seed.Items[item].Facts))), npc, insertCheck, scripts, map);
    }

    /// <summary>
    /// Moves <paramref name="count"/> units of an NPC's ammunition stack <paramref name="stack"/> to the hero (0x4658E0 with the hero as
    /// destination and the stack given, AM4): a stack with exactly that many is deleted, a larger one keeps the rest; the hero's first stack
    /// of the same ammunition type grows, or a new <c>made:n</c> stack of <paramref name="newStackKind"/> (the type's base prototype, the
    /// host's) enters the hero's list after the host's insert check. Refused before any effect: an invalid input or a kind that is not
    /// ammunition (<see cref="GameSessionFailure.InvalidAmmo"/>), the version, an NPC the session does not know, a stack not in its current
    /// list (<see cref="GameSessionFailure.ItemNotAvailable"/>), fewer units (<see cref="GameSessionFailure.AmmoShort"/>), a full budget or a
    /// sum past 32 bits (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, the host's verdict. A count of 0 changes nothing.
    /// The caller limits the count (a steal moves at most 20, PP3). ARC-WORLD-101: <paramref name="npc"/> may also be a standing container
    /// of an attached seed (its loot window), whose stack is found by entity.
    /// </summary>
    public InventoryMutationResult TakeAmmoFromNpc(GameSessionVersion expected, string npc, string stack, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(stack))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (count < 0 || newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        var npcHolder = KnownNpc(npc);
        if (!npcHolder && !KnownContainer(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if ((npcHolder ? NpcStacks(npc) : ContainerStacks(npc)).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault(pair => pair!.Value.Entity == stack) is not { } source)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        // A stack that goes at its last unit is deleted (0x43CCA0, AM4), not removed: no remove-item event, as for an emptied gold pile (NT1).
        var into = HeroStackOfType(source.AmmoType);
        return MoveUnits(source, count, into, into is null ? () => NewHeroStack(newStackKind, source.AmmoType, count, insertCheck) : null);
    }

    /// <summary>
    /// Moves <paramref name="count"/> units of the hero's ammunition stack <paramref name="stack"/> to the NPC (0x4658E0 with the NPC as
    /// destination, AM4): the hero's stack goes at its last unit or keeps the rest; the NPC's first stack of the same type grows, or a new
    /// <c>made:n</c> stack of <paramref name="newStackKind"/> enters its list after the host's insert check for the NPC. The refusals are
    /// <see cref="TakeAmmoFromNpc"/>'s with the hero's stack. A count of 0 changes nothing.
    /// </summary>
    public InventoryMutationResult GiveAmmoToNpc(GameSessionVersion expected, string npc, string stack, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(stack))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (count < 0 || newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (!HeroHeld().Any(held => held.Entity == stack && held.Kind.Type == SessionItemType.Ammo)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var source = _inventory.Ammo[stack];
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        var into = NpcStackOfType(npc, source.AmmoType);
        return MoveUnits(source, count, into, into is null ? () => NewNpcStack(npc, newStackKind, source.AmmoType, count, insertCheck) : null);
    }

    /// <summary>ARC-WORLD-071: the kit and taken items the hero gave NPCs and that are still in an NPC's list, by entity in ordinal order.</summary>
    public IReadOnlyList<SessionGivenItem> QueryGivenItems() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Hero.GivenAll.ToArray()); }

    /// <summary>
    /// ARC-UI-036: every starting-kit item the session still knows, wherever it is: held by the hero (<see cref="QueryHeroKit"/>), placed
    /// on the ground (<see cref="SessionPlacedSource.Kit"/>) or given to an NPC or a container (a given item without an origin), each with
    /// its kind, in entity order. A host that rebuilds the kit's item facts by kit number reads it, so an item that comes back (picked up,
    /// taken back) keeps its facts; only a destroyed item is gone.
    /// </summary>
    public IReadOnlyList<SessionKitItem> QueryKitItemsAnywhere()
    {
        ThrowIfDisposed();
        var known = new SortedDictionary<string, SessionItemKind>(StringComparer.Ordinal);
        foreach (var item in _inventory.Kit) known.TryAdd(item.Entity, item.Kind);
        foreach (var item in _inventory.Placed) if (item.Source == SessionPlacedSource.Kit) known.TryAdd(item.Entity, item.Kind);
        foreach (var item in _inventory.Lists.All)
            if (_inventory.Hero.GivenOf(item.Entity) is { Origin: null }) known.TryAdd(item.Entity, item.Kind);
        return Array.AsReadOnly(known.Select(pair => new SessionKitItem(pair.Key, pair.Value)).ToArray());
    }

    // The units' move after the source checks: into an existing stack (its units grow), or into a new stack the creator makes after its
    // insert check (null: refused, its result returned). The source goes at exactly the count.
    private InventoryMutationResult MoveUnits(SessionAmmoRecord source, int count, SessionAmmoRecord? into, Func<(SessionMadeItem? Created, InventoryMutationResult? Refusal)>? create)
    {
        var emptied = source.Units == count;
        if (into is { } target && (long)target.Units + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (emptied && !_durability.Records.ContainsKey(source.Entity) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        // The records this move adds: the source's rest when untracked, the destination's (a new stack, or an untracked one that grows).
        var added = (emptied || _inventory.Ammo.ContainsKey(source.Entity) ? 0 : 1) + (into is { } grown ? _inventory.Ammo.ContainsKey(grown.Entity) ? 0 : 1 : 1);
        if (_inventory.Ammo.Count + added > _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        string destination;
        SessionInsertVerdict? verdict = null;
        if (into is { } stack)
        {
            _inventory.Ammo[stack.Entity] = stack with { Units = stack.Units + count };
            destination = stack.Entity;
        }
        else
        {
            var (created, refusal) = create!();
            if (created is null) return refusal!.Value;
            destination = created.Value.Entity;
            verdict = SessionInsertVerdict.Accepted;
        }
        if (emptied) DestroyObject(source.Entity);
        else _inventory.Ammo[source.Entity] = source with { Units = source.Units - count };
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, destination, verdict, Version);
    }

    // A new ammunition stack for the hero: a made item at the end of its made items (the Technophobia refusal does not apply: no
    // complexity), its units recorded.
    private (SessionMadeItem? Created, InventoryMutationResult? Refusal) NewHeroStack(SessionItemKind kind, int ammoType, int units, SessionInsertCheck insertCheck)
    {
        if (_inventory.Held >= _limits.MaxPcItems) return (null, RejectItem(GameSessionFailure.BudgetExceeded));
        if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) return (null, RejectItem(GameSessionFailure.BudgetExceeded));
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        // ARC-WORLD-085: the new object has its base prototype's weight; the insert check weighs its units.
        var weight = CreatedWeight(kind.Prototype);
        var verdict = HeroInsertVerdict(insertCheck(new SessionInventoryItem(entity, InventoryDomain.MadeFacts(kind) with { AmmoType = ammoType, Quantity = units })), 0,
            new((int)kind.Type, weight.Weight, weight.MagicWeight, weight.Complexity, units), room: NewItemRoom(entity, kind));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.SeedGeneration != seeds) return (null, RejectItem(GameSessionFailure.StaleVersion, verdict));
        if (verdict != SessionInsertVerdict.Accepted) return (null, RejectItem(GameSessionFailure.InsertRefused, verdict));
        var item = CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, weight));
        _inventory.Ammo[item.Entity] = new(item.Entity, ammoType, units);
        return (item, null);
    }

    private (SessionMadeItem? Created, InventoryMutationResult? Refusal) NewNpcStack(string npc, SessionItemKind kind, int ammoType, int units, SessionInsertCheck insertCheck,
        SessionHolderKind holder = SessionHolderKind.Npc)
    {
        var created = NewNpcEntry(npc, kind, InventoryDomain.MadeFacts(kind) with { AmmoType = ammoType, Quantity = units }, insertCheck, out var refusal, holder);
        if (created is null) return (null, refusal);
        _inventory.Ammo[created.Value.Entity] = new(created.Value.Entity, ammoType, units);
        return (created, null);
    }

    // A new made item at the end of the NPC's (ARC-WORLD-090: or a container's) list after the host's insert check for it (the
    // original's new object moved in by 0x4617F0); its own bookkeeping (gold or ammunition record, revision) is the caller's.
    private SessionMadeItem? NewNpcEntry(string npc, SessionItemKind kind, SessionItemFacts facts, SessionInsertCheck insertCheck, out InventoryMutationResult? refusal,
        SessionHolderKind holder = SessionHolderKind.Npc)
    {
        refusal = null;
        // The new object moves in through 0x4617F0, whose first step is the NPC's transfer script (NT2): not run here.
        if (HolderHasScript(npc, holder, SessionScriptPoint.Transfer)) { refusal = RejectItem(GameSessionFailure.HolderScriptUnsupported); return null; }
        if (_inventory.AddedTo(npc).Count + SeedItemCount(npc, holder) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems)
        {
            refusal = RejectItem(GameSessionFailure.BudgetExceeded);
            return null;
        }
        if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) { refusal = RejectItem(GameSessionFailure.BudgetExceeded); return null; }
        if (_revision == long.MaxValue) { refusal = RejectItem(GameSessionFailure.RevisionExhausted); return null; }
        var (revision, seeds) = (_revision, _inventory.SeedGeneration);
        // ARC-WORLD-085: the new object has its prototype's weight (the NPC's own weight is the host's insert check).
        var weight = CreatedWeight(kind.Prototype);
        var verdict = insertCheck(new SessionInventoryItem(entity, facts with { Weight = weight.Weight, MagicWeight = weight.MagicWeight }));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.SeedGeneration != seeds) { refusal = RejectItem(GameSessionFailure.StaleVersion, verdict); return null; }
        if (verdict != SessionInsertVerdict.Accepted) { refusal = RejectItem(GameSessionFailure.InsertRefused, verdict); return null; }
        var made = CreateMadeItem(new(entity, kind, holder == SessionHolderKind.Container ? SessionCreatePlace.ContainerList : SessionCreatePlace.NpcList, weight, Holder: npc));
        if (holder != SessionHolderKind.Container) StoreInsertPlace(npc, made.Entity);
        return made;
    }

    // The NPC's first gold pile in its list order: its seed piles still in its list (in every attached seed that lists it), then the
    // piles the session added to it; with the tracked or seed coins and a seed pile's facts.
    private (string Entity, int Quantity, SessionItemFacts? Facts)? NpcPile(string npc) =>
        HolderPiles(npc, SessionHolderKind.Npc).Select(found => ((string Entity, int Quantity, SessionItemFacts? Facts)?)found).FirstOrDefault();

    // The holder's gold piles in its list order (ARC-WORLD-101: a container's too, when it did not spill, with the piles a barter or a
    // regeneration added to it).
    private IEnumerable<(string Entity, int Quantity, SessionItemFacts? Facts)> HolderPiles(string holder, SessionHolderKind kind)
    {
        foreach (var (map, seed) in _inventory.Seeds)
            if (seed.Holders.TryGetValue(holder, out var row) && row.Kind == kind && (kind == SessionHolderKind.Npc || SpillCell(map, seed, holder) is null))
                foreach (var entity in row.Items)
                    if (!_inventory.Taken.Contains(entity) && !Dropped(entity) && seed.Items[entity].Facts is { Kind.Type: SessionItemType.Gold } facts)
                        yield return (entity, _inventory.Piles.TryGetValue(entity, out var tracked) ? tracked : facts.Quantity ?? 0, facts);
        if (kind == SessionHolderKind.Npc || _inventory.Lists.IsContainer(holder))
            foreach (var item in _inventory.AddedTo(holder))
                if (item.Kind.Type == SessionItemType.Gold) yield return (item.Entity, _inventory.Piles[item.Entity], null);
    }

    // Whether any attached seed's holder row of the NPC has a script at the point.
    private bool NpcHasScript(string npc, int point) => HolderHasScript(npc, SessionHolderKind.Npc, point);

    // ARC-WORLD-090: the same for a holder of either kind.
    private bool HolderHasScript(string holder, SessionHolderKind kind, int point) => _inventory.Seeds.Values.Any(seed =>
        seed.Holders.TryGetValue(holder, out var row) && row.Kind == kind && row.ScriptPoints.Contains(point));

    // One of the hero's held items (not placed) with its list, for a taken item its origin map, and for a made item its complexity.
    private (SessionItemKind Kind, SessionPlacedSource Source, SessionMapId? Origin, int Complexity)? HeroListed(string entity)
    {
        if (_inventory.Hero.KitItem(entity) is { } kit) return (kit.Kind, SessionPlacedSource.Kit, null, 0);
        if (_inventory.Hero.PcItem(entity) is { } pc) return (pc.Kind, SessionPlacedSource.Pc, pc.Origin, 0);
        if (_inventory.Hero.MadeItem(entity) is { } made) return (made.Kind, SessionPlacedSource.Made, null, made.Complexity);
        return null;
    }
}
