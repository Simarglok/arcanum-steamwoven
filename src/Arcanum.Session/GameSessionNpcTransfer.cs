using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;

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
    /// insert-item script when there is no <paramref name="scripts"/> host (ARC-SCRIPT-2855: with one the original's event runs first, 0x464830 → 0x441980 point 21 with the hero as the triggerer and the pile as the attachee,
    /// its answer ignored, its effects applied with the move; a run the session cannot apply refuses: <see cref="GameSessionFailure.ItemScriptUnsupported"/>),
    /// a sum past 32 bits or a full record budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. A count of 0 changes
    /// nothing. The caller limits the count: a steal moves at most 100 (PP3), a merchant pays at most what it holds (NT4).
    /// ARC-WORLD-101: <paramref name="npc"/> may also be a standing container of an attached seed (its loot window), and
    /// <paramref name="pile"/> names the pile of the holder's current list to take from (the original's 0x464830 with the pile given;
    /// one the list does not hold is <see cref="GameSessionFailure.ItemNotAvailable"/>); without it the holder's first pile.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x464830, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public InventoryMutationResult TakeGoldFromNpc(GameSessionVersion expected, string npc, int count, string? pile = null, ISessionItemScriptHost? scripts = null)
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
        return TakeGold(chosen, count, npc, scripts);
    }

    private InventoryMutationResult TakeGold((string Entity, int Quantity, SessionItemFacts? Facts)? from, int count, string holder, ISessionItemScriptHost? scripts = null)
    {
        if (from is not { } pile || pile.Quantity < count) return RejectItem(GameSessionFailure.GoldShort);
        // ARC-SCRIPT-2855 (NT1): the pile's insert-item script (21) runs first with the hero as the triggerer; the runs are applied with the move or the whole move is refused.
        var insert = RunPileInsert(scripts, pile.Entity, SessionEventKinds.PlayerEntity, npcTriggerer: false, pile.Quantity == count ? holder : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        if ((long)_inventory.Gold + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        var emptied = pile.Quantity == count;
        if (emptied ? !_durability.Records.ContainsKey(pile.Entity) && _durability.Count >= _limits.MaxDurabilityRecords
            : !_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        // ARC-SCRIPT-2910: the hero's first pile is a new gold object moved in through the fresh path, so its prototype's get script runs before the insert check (after the source's insert-item above, as the original's order).
        var (freshRefusal, fresh) = BeginFreshHeroGold(scripts);
        if (freshRefusal is { } refusedFresh) return insert.Refused(refusedFresh);
        // ARC-WORLD-1132: a hero with no gold pile and no free grid place for one gets no coins, the new pile stays on the ground (so it does when the get script skipped the default).
        GoldGround? ground = null;
        if (HeroGoldNeedsGround() || fresh is { Declined: true })
        {
            var failure = PlanGoldGround(pile.Facts?.Kind ?? SessionHeroGrid.GoldKind, out var planned, !emptied && !_inventory.Piles.ContainsKey(pile.Entity) ? 1 : 0);
            if (failure != GameSessionFailure.None) return insert.Refused(RejectItem(failure));
            ground = planned;
        }
        ScrRunResult? insertRun = null;
        if (fresh is not null && FinishFreshHeroGold(fresh, ground is null, out insertRun) is { } unfinished) return insert.Refused(unfinished);
        if (ground is { } onGround) LeaveGoldOnGround(onGround, count);
        else _inventory.Gold += count;
        insert.Runs?.Commit();
        if (fresh is not null) CommitFreshHeroGold(fresh);
        if (emptied) DestroyObject(pile.Entity);
        else _inventory.Piles[pile.Entity] = pile.Quantity - count;
        Revise();
        return WithFreshHeroGold(insert.Result(new(GameSessionDisposition.Changed, GameSessionFailure.None, pile.Entity, null, Version)), fresh, insertRun);
    }

    /// <summary>
    /// The hero gets <paramref name="count"/> coins from nothing (0x464830 with no source object, the dialog's <c>$$ n</c> with n &gt; 0,
    /// ARC-DEV-179, ledger GM1): the source's call credits the destination whether or not a source exists, and the hero's gold grows with no
    /// insert check when the hero already holds a gold stack; a hero without one gets a new gold object moved in through the insert path (0x4617F0), whose
    /// room test (ARC-WORLD-1132) leaves the pile on the hero's tile (<see cref="InventoryMutationResult.Item"/> names it) when the grid has no place for it, the
    /// call still succeeding (gold weighs nothing, so only the room is checked). Refused before any effect: a negative count (<see cref="GameSessionFailure.InvalidItem"/>), the version, no hero to hold
    /// gold (<see cref="GameSessionFailure.GoldUnavailable"/>), a sum past 32 bits (<see cref="GameSessionFailure.BudgetExceeded"/>), the
    /// revision. A count of 0 changes nothing. One revision.
    /// ARC-SCRIPT-2910: the new pile's get (4) and insert-item (21) scripts run through <paramref name="scripts"/> with the hero as the triggerer (<see cref="BeginFreshHeroGold"/>): a get script that skips the default leaves the pile on the hero's tile, and a prototype with such a script and no host refuses the call (<see cref="GameSessionFailure.ItemScriptUnsupported"/>).
    /// </summary>
    public InventoryMutationResult GiveGoldToHero(GameSessionVersion expected, int count, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        if (count < 0) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
        if (count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
        if ((long)_inventory.Gold + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        // ARC-SCRIPT-2910: a hero without a pile gets a new gold object moved in through the fresh path: its prototype's get script runs first.
        var (freshRefusal, fresh) = BeginFreshHeroGold(scripts);
        if (freshRefusal is { } refusedFresh) return refusedFresh;
        // ARC-WORLD-1132: no pile and no free grid place for one: the new pile lies on the hero's tile (a plain created object) and the call succeeds (so it does when the get script skipped the default).
        GoldGround? ground = null;
        if (HeroGoldNeedsGround() || fresh is { Declined: true })
        {
            var failure = PlanGoldGround(SessionHeroGrid.GoldKind, out var planned);
            if (failure != GameSessionFailure.None) return RejectItem(failure);
            ground = planned;
        }
        ScrRunResult? insertRun = null;
        if (fresh is not null && FinishFreshHeroGold(fresh, ground is null, out insertRun) is { } unfinished) return unfinished;
        string? left = null;
        if (ground is { } onGround) left = LeaveGoldOnGround(onGround, count);
        else _inventory.Gold += count;
        if (fresh is not null) CommitFreshHeroGold(fresh);
        Revise();
        return WithFreshHeroGold(new(GameSessionDisposition.Changed, GameSessionFailure.None, left, null, Version), fresh, insertRun);
    }

    /// <summary>
    /// An NPC gets <paramref name="count"/> coins from nothing (0x464830 with a source that holds no gold stack, the dialog's <c>$$ −n</c> asking a
    /// payer without a stack, ARC-DEV-179, ledger GM2: the call skips the debit and credits the destination in full). The NPC's first pile grows
    /// with no insert check; an NPC without one gets a new <c>made:n</c> pile of <paramref name="pileKind"/> after <paramref name="insertCheck"/>
    /// (<see cref="GiveGoldToNpc"/>'s rules, but no coins leave the hero). Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know (<see cref="GameSessionFailure.UnknownHolder"/>),
    /// a sum past 32 bits or a full pile, list or store budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the host's verdict
    /// (<see cref="GameSessionFailure.InsertRefused"/>), the revision. A count of 0 changes nothing. One revision. A destination's transfer script (point 32) is not run for a new pile (ARC-SCRIPT-2857: it has no parent, 0x461810).
    /// </summary>
    public InventoryMutationResult GiveGoldToNpcFromNothing(GameSessionVersion expected, string npc, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
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
        var report = new CreatedScripts();
        var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal, scripts: scripts, report: report, ground: true);
        if (created is null) return refusal!.Value;
        _inventory.Piles[created.Value.Entity] = count;
        Revise();
        return report.Apply(new(GameSessionDisposition.Changed, GameSessionFailure.None, created.Value.Entity, SessionInsertVerdict.Accepted, Version));
    }

    /// <summary>
    /// Moves <paramref name="count"/> coins from one NPC's gold pile to another's (0x464830 with an NPC as source and as destination, a
    /// follower's payment in the dialog's <c>$$ −n</c>, ARC-DEV-179, ledger GM2): the source's first pile must hold at least the count (else
    /// <see cref="GameSessionFailure.GoldShort"/>, the original's 0 with nothing changed: the call never moves part of what it was asked to),
    /// a pile with exactly that many is destroyed, a larger one keeps the rest; the destination's first pile grows with no insert check, and
    /// a destination without one gets a new <c>made:n</c> pile of <paramref name="pileKind"/> after <paramref name="insertCheck"/>
    /// (<see cref="GiveGoldToNpc"/>'s rules). Refused before any effect: an invalid input or the same NPC twice
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know (<see cref="GameSessionFailure.UnknownHolder"/>),
    /// a source pile with an insert-item script when there is no <paramref name="scripts"/> host (with one it runs first with the destination NPC as the triggerer, as in <see cref="TakeGoldFromNpc"/>; ARC-SCRIPT-2855) or a run the session cannot apply
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a sum past 32 bits or a full pile,
    /// durability, list or store budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the host's verdict (<see cref="GameSessionFailure.InsertRefused"/>),
    /// the revision. A count of 0 changes nothing. One revision.
    /// </summary>
    public InventoryMutationResult MoveGoldBetweenNpcs(GameSessionVersion expected, string from, string to, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
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
        // ARC-SCRIPT-2855 (NT1): the source pile's insert-item script runs first with the destination NPC as the triggerer.
        var insert = RunPileInsert(scripts, source.Entity, to, npcTriggerer: true, source.Quantity == count ? from : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
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
        var report = new CreatedScripts { Defer = true };
        if (target is { } existing) targetPile = existing.Entity;
        else
        {
            var created = NewNpcEntry(to, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal, scripts: scripts, report: report, ground: true);
            if (created is null) return refusal!.Value;
            targetPile = created.Value.Entity;
        }
        insert.Runs?.Commit();
        report.CommitPending();
        if (emptied) DestroyObject(source.Entity);
        else _inventory.Piles[source.Entity] = source.Quantity - count;
        _inventory.Piles[targetPile] = (target?.Quantity ?? 0) + count;
        Revise();
        return report.Apply(insert.Result(new(GameSessionDisposition.Changed, GameSessionFailure.None, targetPile, null, Version)));
    }

    /// <summary>
    /// Moves <paramref name="count"/> of the hero's coins to the NPC (0x464830 with the NPC as destination, NT1): its gold pile grows
    /// with no insert check; an NPC without a pile gets a new one, a <c>made:n</c> item of <paramref name="pileKind"/> (the gold prototype,
    /// the host's) at the end of its list, after the host's insert check for the NPC. The original then leaves a refused new pile on the
    /// ground and still reports success (ARC-SCRIPT-2911: so does the session when the host attaches a tile source, <see cref="AttachHolderCells"/>: the pile lies on the NPC's tile, the coins are out of the hero and
    /// the result is Changed with the refusing <see cref="InventoryMutationResult.Verdict"/>; without one a refusal moves nothing, <see cref="GameSessionFailure.InsertRefused"/>). Refused before any
    /// effect, beyond <see cref="TakeGoldFromNpc"/>'s input, version, holder and hero checks: a kind that is not gold
    /// (<see cref="GameSessionFailure.InvalidItem"/>), fewer coins than the count (<see cref="GameSessionFailure.GoldShort"/>), a sum past 32
    /// bits, a full list, store or record budget, or no free made number (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision. A
    /// count of 0 changes nothing.
    /// ARC-SCRIPT-2900: the hero's gold pile (the counter's one pile) runs its insert-item script (21, the NPC as the triggerer) first and, when every coin goes, its remove-item script (28, the hero) through <paramref name="scripts"/> or the standing host; a pile with such a script and no host refuses the move whole.
    /// </summary>
    public InventoryMutationResult GiveGoldToNpc(GameSessionVersion expected, string npc, int count, SessionItemKind pileKind, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
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
        // ARC-SCRIPT-2900 (NT1, IE3; 0x464830 → 0x43CCA0 → 0x466E00 → 0x467860): the hero's gold pile (the counter's one pile, SessionHeroGrid.GoldEntity) runs its insert-item script (21) first with the NPC as the triggerer; the whole pile given destroys it, which
        // then runs its remove-item script (28) with the hero as the triggerer (the destroy script, point 2, comes between and is not run: #2759). One run set, committed with the move; without a host (given or standing) a pile with such a script refuses the move whole.
        var insert = WithHeroGoldObject(() => RunPileInsert(scripts, SessionHeroGrid.GoldEntity, npc, npcTriggerer: true, _inventory.Gold == count ? SessionEventKinds.PlayerEntity : null));
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        if (NpcPile(npc) is { } pile)
        {
            if ((long)pile.Quantity + count > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (!_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
            CommitHeroGoldRuns(insert.Runs);
            _inventory.Gold -= count;
            _inventory.Piles[pile.Entity] = pile.Quantity + count;
            Revise();
            return insert.Result(new(GameSessionDisposition.Changed, GameSessionFailure.None, pile.Entity, null, Version));
        }
        if (_inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        var report = new CreatedScripts { Defer = true };
        var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = count }, insertCheck, out var refusal, scripts: scripts, report: report, ground: true);
        if (created is null) return refusal!.Value;
        CommitHeroGoldRuns(insert.Runs);
        report.CommitPending();
        _inventory.Gold -= count;
        _inventory.Piles[created.Value.Entity] = count;
        Revise();
        return report.Apply(insert.Result(new(GameSessionDisposition.Changed, GameSessionFailure.None, created.Value.Entity, SessionInsertVerdict.Accepted, Version)));
    }

    /// <summary>
    /// Moves one of the hero's items (its kit, a taken or a made item; not one it placed on the ground) to the end of the NPC's list
    /// (0x4617F0 → 0x461CA0, NT2, NT3), after the host's insert check for the NPC: a worn item leaves its slot; ammunition merges into the
    /// NPC's first stack of its prototype (its units added, the moved stack deleted, AM1). The item keeps its entity: a kit or taken item is
    /// recorded as given (<see cref="QueryGivenItems"/>), so taking it back returns it to the list it came from. Refused before any effect:
    /// an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an NPC the session does not know
    /// (<see cref="GameSessionFailure.UnknownHolder"/>), an item the hero does not hold (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// an NPC with a transfer script (point 32) when there is no <paramref name="scripts"/> host or the shown map's seed does not list it (with a host the script runs first, ARC-SCRIPT-2827: skip default is
    /// <see cref="InventoryMutationResult.DeclinedBy"/> 32 and the item stays; otherwise <see cref="GameSessionFailure.HolderScriptUnsupported"/>), a taken item whose attached origin seed shows a remove-item, insert-item
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
        // A recipient's transfer script that declined the give (DeclinedBy) left the item with the hero: its removal spell steps do not run.
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.DeclinedBy is not null) return result;
        return result with { ItemSpells = [triggers.Remove(item, SessionSpellCaster.Hero)], Version = Version };
    }

    /// <summary>
    /// ARC-UI-1925 (the loot window's move into the holder, mode 2; ledger IW35): the hero's item, worn or in its pack, goes into the shown container's list under
    /// <see cref="GiveItemToNpc"/>'s rules with the container as holder (the barter window's sale into a substitute container makes the same
    /// move). A container that is not shown (a spilled one, another map's) is <see cref="GameSessionFailure.UnknownHolder"/>. One revision.
    /// </summary>
    public InventoryMutationResult GiveItemToContainer(GameSessionVersion expected, string container, string item, SessionInsertCheck insertCheck,
        ISessionItemTriggers? triggers = null, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(container) || container.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item))
            return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!ShownContainer(container)) return RejectItem(GameSessionFailure.UnknownHolder);
        var result = GiveItemTo(container, SessionHolderKind.Container, item, insertCheck, identify: false, triggers, scripts);
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.DeclinedBy is not null) return result;
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
        ISessionItemTriggers? triggers = null, ISessionItemScriptHost? scripts = null, NpcWearer? slotWearer = null, IReadOnlyList<int>? slotPoints = null)
    {
        if (HeroListed(item) is not { } held) return RejectItem(GameSessionFailure.ItemNotAvailable);
        // ARC-SCRIPT-2827 (IE2, TQ19; 0x461CA0 → 0x441980 point 32): the recipient's transfer script is the first gate of the move, run with the giver (the hero) as the triggerer, the recipient as the attachee and the item as the
        // extra, before the no-drop check, the capacity checks and the insert check; skip default refuses the move. The script host answers for the shown map's rows, so a recipient that map's seed does not list with the point,
        // or a move without a host, stays refused (HolderScriptUnsupported) as before.
        var holderTransfer = HolderHasScript(npc, kind, SessionScriptPoint.Transfer);
        if (holderTransfer && (scripts is null || !ShownHolderHasScript(npc, kind, SessionScriptPoint.Transfer))) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
        // ARC-WORLD-083: a made item keeps its complexity in the NPC's list, so taking it back can refuse it (a taken item's seed answers).
        var complexity = held.Origin is { } takenFrom && _inventory.Seeds.TryGetValue(takenFrom, out var takenSeed) && takenSeed.Items.TryGetValue(item, out var seedRow)
            ? seedRow.Facts.Complexity : held.Complexity;
        ItemScriptRuns? runs = null, wearRuns = null;
        var (gateRevision, gateMap, gateFlags) = (_revision, _actor.Map, _flags.Version);
        if (holderTransfer)
        {
            runs = new ItemScriptRuns(this, scripts!, gateMap.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = complexity });
            var gate = runs.Run(SessionScriptPoint.Transfer, npc, SessionEventKinds.PlayerEntity, item);
            ThrowIfDisposed();
            if (_revision != gateRevision || _actor.Map != gateMap || _flags.Version != gateFlags) return RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs };
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
            if (gate?.Outcome == ScrOutcome.SkipDefault)
            {
                // A dry run (QueryNamedItemTransfer) applies nothing, not even a declined gate's flags (ARC-MAGIC-2252).
                if (!_transferDryRun) return Declined(SessionScriptPoint.Transfer, runs);
                runs.Prepare();
                return runs.Failure != GameSessionFailure.None ? RejectItem(runs.Failure) with { Scripts = runs.Runs } : new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version);
            }
        }
        // ARC-WORLD-1283 (NT2, VP5, VPG2): the recipient's transfer script is the first gate of 0x461CA0, the item's remove check (0x466DA0) the second: the no-drop flag refuses
        // the hero's item (worn or not) with message 111 before any other check, so a barter sale, a plant or a hand-over of such an item changes nothing.
        if (HeroItemNoDrop(item, held.Kind, held.Origin)) return NoDropRefusal(runs?.Runs);
        var worn = _equipment.SlotOf(item) is not null;
        // ARC-MAGIC-1743 (IE3, IE4, IE6): the item event scripts of the move, from the worn record's points (a kit or made item too) and the origin seed row's. The removal runs wield off (25, only a worn
        // item) and remove item (28) with the hero as the triggerer, the insertion insert item (21) with the recipient as the triggerer; without a script host an item with any of them is refused.
        var wornPoints = _equipment.SlotOf(item) is { } wornIn ? _equipment.Worn[wornIn].ScriptPoints : [];
        SessionSeedItem? listed = held.Origin is { } originMap && _inventory.Seeds.TryGetValue(originMap, out var originSeed) && originSeed.Items.TryGetValue(item, out var givenRow) ? givenRow : null;
        // ARC-UI-2159 (IE3, IE4, IE6, BW5): a sale onto a slot of a merchant's doll (slotWearer is the merchant as the wearer) also meets the item's own points the host listed (slotPoints: a kit or made item has no seed row) and its wield on (24).
        bool Has(int point) => wornPoints.Contains(point) || listed?.Facts.HasScript(point) == true || slotPoints?.Contains(point) == true;
        var (wieldOff, removeOwn, insertOwn) = (worn && Has(SessionScriptPoint.WieldOff), Has(SessionScriptPoint.RemoveItem), Has(SessionScriptPoint.InsertItem));
        var wieldOn = slotWearer is not null && Has(SessionScriptPoint.WieldOn);
        if (scripts is null && (wornPoints.Count > 0 || wieldOff || removeOwn || insertOwn || wieldOn)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // The script host answers for the shown map's rows, so the scripts of an item whose row is another map's seed row are not run: refused, never moved with a script skipped.
        if (held.Origin is { } rowMap && rowMap != _actor.Map && (wieldOff || removeOwn || insertOwn || wieldOn)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // ARC-MAGIC-022: a worn item with the item spells mark would leave without its unwear (0x4CBFC0).
        if (_equipment.SlotOf(item) is { } markedIn && _equipment.Worn[markedIn].ItemSpells) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        // ARC-MAGIC-022: the removal ends the item's runs and runs its drop (IU9) only through an item trigger host.
        if (triggers is null && HasItemRuns(item)) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
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
        ScrRunResult? insertRun = null;
        if (scripts is not null && (runs is not null || wieldOff || removeOwn || insertOwn || wieldOn))
        {
            // The original moves the item first and then runs these scripts, whose results it ignores (IE3, IE4, IF10); the admitted commands read nothing the move changes, so the runs are made
            // before the commit and applied with it, or the whole move is refused.
            // The gate's reads (its flags, the shown map) stay current until the move applies, the insert check included.
            var (shown, flagVersion) = holderTransfer ? (gateMap, gateFlags) : (_actor.Map, _flags.Version);
            runs ??= new ItemScriptRuns(this, scripts, shown.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = complexity });
            if (wieldOff) runs.Run(SessionScriptPoint.WieldOff, item, SessionEventKinds.PlayerEntity, null);
            if (runs.Failure == GameSessionFailure.None && removeOwn) runs.Run(SessionScriptPoint.RemoveItem, item, SessionEventKinds.PlayerEntity, null);
            // The insertion's triggerer is the recipient: an NPC reads its own printed line variant; a container is no critter, so its run keeps the hero's reading.
            runs.NpcTriggerer = kind == SessionHolderKind.Npc;
            // ARC-UI-2159 (IE4): an item put on the merchant's slot is wielded by the merchant (wield on 24, then insert item 21): those runs are the merchant's (its effect list the runs change, its stats they read), planned after the hero's removal.
            if (slotWearer is not null && runs.Failure == GameSessionFailure.None && (wieldOn || insertOwn))
            {
                wearRuns = new ItemScriptRuns(this, scripts, shown.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = complexity }, slotWearer) { NpcTriggerer = true };
                if (wieldOn) wearRuns.Run(SessionScriptPoint.WieldOn, item, npc, null);
                if (wearRuns.Failure == GameSessionFailure.None && insertOwn) insertRun = wearRuns.Run(SessionScriptPoint.InsertItem, item, npc, null);
            }
            else if (runs.Failure == GameSessionFailure.None && insertOwn) insertRun = runs.Run(SessionScriptPoint.InsertItem, item, npc, null);
            ThrowIfDisposed();
            var made = wearRuns is null ? runs.Runs : [.. runs.Runs, .. wearRuns.Runs];
            if (_revision != revision || _actor.Map != shown || _flags.Version != flagVersion) return RejectItem(GameSessionFailure.StaleVersion, verdict) with { InsertScript = insertRun, Scripts = made };
            runs.Prepare();
            wearRuns?.Prepare();
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure, verdict) with { InsertScript = insertRun, Scripts = made };
            if (wearRuns is not null && wearRuns.Failure != GameSessionFailure.None) return RejectItem(wearRuns.Failure, verdict) with { InsertScript = insertRun, Scripts = made };
        }
        // ARC-MAGIC-2252: a dry run (QueryNamedItemTransfer) ends where the move would apply: every run is made and prepared, nothing is applied.
        if (_transferDryRun) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, item, verdict, Version);
        if (into is { } target)
        {
            // The merged item is destroyed after the runs commit (ARC-SCRIPT-2346: a toggle state its insert-item script ran leaves no off record behind it).
            _inventory.Ammo[target.Entity] = target with { Units = target.Units + _inventory.Ammo[item].Units };
        }
        else
        {
            if (_equipment.SlotOf(item) is { } slot) _equipment.Worn.Remove(slot);
            _inventory.Hero.Remove(item);
            // The item stays taken (no seed shows it); a kit or taken item remembers where it came from.
            if (kind == SessionHolderKind.Container)
            {
                _inventory.AddToContainer(npc, new SessionMadeItem(item, held.Kind, held.Complexity));
                StoreInsertPlace(npc, item, container: true);
            }
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
        wearRuns?.Commit();
        if (into is not null) DestroyObject(item);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, into?.Entity ?? item, verdict, Version)
            { InsertScript = insertRun, Scripts = wearRuns is null ? runs?.Runs ?? [] : [.. runs!.Runs, .. wearRuns.Runs], Messages = wearRuns is null ? runs?.Messages ?? [] : [.. runs!.Messages, .. wearRuns.Messages] };
    }

    /// <summary>
    /// Moves an item of an NPC's current list in the seed of the actor's map, or (ARC-DEV-087, MIG6e) of the other attached map that lists the NPC (alive or not: life is the caller's concern) to the hero (0x4617F0 with the hero as the new parent, NT2, NT3), as
    /// a steal or a purchase does: a seed item, a made item the session added to it, or an item the hero gave it (which returns to the list
    /// it came from). The item rules and the commit are <see cref="LootItem"/>'s (the insert check, the Technophobia refusal, the
    /// insert-item script, the ammunition merge), without its dead-holder and use-script conditions. Refused before any effect: an invalid
    /// input, the version, no seed for the actor's map (<see cref="GameSessionFailure.InventoryUnavailable"/>), a holder that is not an NPC of
    /// it (<see cref="GameSessionFailure.UnknownHolder"/>), an item not in its current list (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// gold (<see cref="TakeGoldFromNpc"/>) or, for a session without a hero, a key (<see cref="GameSessionFailure.ItemTypeUnsupported"/>; a key otherwise folds into the hero's key list, ARC-WORLD-123), and the rules of
    /// <see cref="LootItem"/>. One revision. ARC-UI-1926: a worn item is refused unless <paramref name="unwield"/> takes it off its slot as the loot of a body does (the barter window's doll
    /// view: 0x575200 → 0x5788C0 → 0x461CA0, whose removal 0x466E00 → 0x467860 unwields; ARC-DEV-1338: the pick-pocket steal), over the NPC's own wield context when <paramref name="npcWearer"/> gives it. ARC-MAGIC-022: with <paramref name="triggers"/> the pickup executor runs after the move
    /// (<see cref="InventoryMutationResult.ItemSpells"/>).
    /// </summary>
    public InventoryMutationResult TakeItemFromNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null, ISessionItemTriggers? triggers = null, bool unwield = false, SessionNpcWieldScripts? npcWearer = null) =>
        PickedUp(TakeFromNpc(expected, npc, item, insertCheck, scripts, unwield, npcWearer), triggers);

    private InventoryMutationResult TakeFromNpc(GameSessionVersion expected, string npc, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts,
        bool unwield = false, SessionNpcWieldScripts? npcWearer = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        // ARC-DEV-087 (MIG6e): an NPC of another attached map (a companion carried here) moves its item from that map's seed.
        if (!NpcHolderSeed(npc, out var map, out var seed, out _)) return RejectItem(UnseededHolder());
        if (_inventory.AddedTo(npc).FirstOrDefault(added => added.Entity == item) is { Entity: not null } added)
            return Take(seed, new(item, _inventory.Worn(npc, item, _inventory.AddedFacts(added))), npc, insertCheck, scripts, map, unwield: unwield, npcWearer: npcWearer);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != npc || _inventory.Taken.Contains(item) || DroppedCell(map, seed, item) is not null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        // ARC-WORLD-103: worn as the session's lists show it (a dressed NPC's record over its map data).
        return Take(seed, new(item, _inventory.Worn(npc, item, _inventory.WithAmmo(item, seed.Items[item].Facts))), npc, insertCheck, scripts, map, unwield: unwield, npcWearer: npcWearer);
    }

    /// <summary>
    /// Moves <paramref name="count"/> units of an NPC's ammunition stack <paramref name="stack"/> to the hero (0x4658E0 with the hero as
    /// destination and the stack given, AM4): a stack with exactly that many is deleted, a larger one keeps the rest; the hero's first stack
    /// of the same ammunition type grows, or a new <c>made:n</c> stack of <paramref name="newStackKind"/> (the type's base prototype, the
    /// host's) enters the hero's list after the host's insert check. Refused before any effect: an invalid input or a kind that is not
    /// ammunition (<see cref="GameSessionFailure.InvalidAmmo"/>), the version, an NPC the session does not know, a stack not in its current
    /// list (<see cref="GameSessionFailure.ItemNotAvailable"/>), fewer units (<see cref="GameSessionFailure.AmmoShort"/>), a full budget or a
    /// sum past 32 bits (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, the host's verdict, a stack with an insert-item script when there is no <paramref name="scripts"/> host or a run the session cannot apply
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>; ARC-SCRIPT-2855: with a host that event runs first, 0x4658E0 → 0x441980 point 21, the hero as the triggerer and the stack as the attachee, its answer ignored). A count of 0 changes nothing.
    /// The caller limits the count (a steal moves at most 20, PP3). ARC-WORLD-101: <paramref name="npc"/> may also be a standing container
    /// of an attached seed (its loot window), whose stack is found by entity.
    /// </summary>
    public InventoryMutationResult TakeAmmoFromNpc(GameSessionVersion expected, string npc, string stack, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
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
        // ARC-SCRIPT-2855 (AM4): the source stack's insert-item script (21) runs first with the hero as the triggerer.
        // ARC-SCRIPT-2879: a stack that goes at its last unit is deleted (0x43CCA0, AM4) out of its holder, which runs the stack's remove-item script (28) with the holder as the triggerer, as for an emptied gold pile (NT1).
        var insert = RunPileInsert(scripts, source.Entity, SessionEventKinds.PlayerEntity, npcTriggerer: false, source.Units == count ? npc : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        var into = HeroStackOfType(source.AmmoType);
        // ARC-SCRIPT-2910: a new stack runs its own get and insert-item scripts after the source's insert-item (the original runs the source's 21 first), with the hero as the triggerer.
        var report = new CreatedScripts { Defer = true };
        return MoveUnits(source, count, into, into is null ? () => NewHeroStack(newStackKind, source.AmmoType, count, insertCheck, scripts, report) : null, insert, report);
    }

    /// <summary>
    /// Moves <paramref name="count"/> units of the hero's ammunition stack <paramref name="stack"/> to the NPC (0x4658E0 with the NPC as
    /// destination, AM4): the hero's stack goes at its last unit or keeps the rest; the NPC's first stack of the same type grows, or a new
    /// <c>made:n</c> stack of <paramref name="newStackKind"/> enters its list after the host's insert check for the NPC. The refusals are
    /// <see cref="TakeAmmoFromNpc"/>'s with the hero's stack; with <paramref name="scripts"/> the hero's stack's insert-item script runs first with the NPC as the triggerer (ARC-SCRIPT-2855). A count of 0 changes nothing.
    /// </summary>
    public InventoryMutationResult GiveAmmoToNpc(GameSessionVersion expected, string npc, string stack, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
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
        // ARC-SCRIPT-2855 (AM4): the hero's stack's insert-item script (21) runs first with the NPC as the triggerer.
        var insert = RunPileInsert(scripts, source.Entity, npc, npcTriggerer: true, source.Units == count ? SessionEventKinds.PlayerEntity : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        var into = NpcStackOfType(npc, source.AmmoType);
        var report = new CreatedScripts { Defer = true };
        return MoveUnits(source, count, into, into is null ? () => NewNpcStack(npc, newStackKind, source.AmmoType, count, insertCheck, scripts: scripts, report: report, ground: true) : null, insert, report);
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
    private InventoryMutationResult MoveUnits(SessionAmmoRecord source, int count, SessionAmmoRecord? into, Func<(SessionMadeItem? Created, InventoryMutationResult? Refusal)>? create, PileInsert insert = default,
        CreatedScripts? report = null)
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
        insert.Runs?.Commit();
        report?.CommitPending();
        if (emptied) DestroyObject(source.Entity);
        else _inventory.Ammo[source.Entity] = source with { Units = source.Units - count };
        Revise();
        var moved = insert.Result(new(GameSessionDisposition.Changed, GameSessionFailure.None, destination, verdict, Version));
        return report?.Apply(moved) ?? moved;
    }

    // A new ammunition stack for the hero: a made item at the end of its made items (the Technophobia refusal does not apply: no
    // complexity), its units recorded. ARC-SCRIPT-2910 (ledger TQ19): the new stack moves in through the same fresh path as an NPC's (0x4617F0 → 0x461810 → 0x466640), so its prototype's get (4) script runs before the insert check and its
    // insert-item (21) script at the end, both with the hero as the triggerer (NewNpcEntry, hero mode); the lines and runs go to <paramref name="report"/>.
    private (SessionMadeItem? Created, InventoryMutationResult? Refusal) NewHeroStack(SessionItemKind kind, int ammoType, int units, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null, CreatedScripts? report = null)
    {
        var created = NewNpcEntry(SessionEventKinds.PlayerEntity, kind, InventoryDomain.MadeFacts(kind) with { AmmoType = ammoType, Quantity = units }, insertCheck, out var refusal, scripts: scripts, report: report, hero: true);
        if (created is null) return (null, refusal);
        _inventory.Ammo[created.Value.Entity] = new(created.Value.Entity, ammoType, units);
        return (created, null);
    }

    private (SessionMadeItem? Created, InventoryMutationResult? Refusal) NewNpcStack(string npc, SessionItemKind kind, int ammoType, int units, SessionInsertCheck insertCheck,
        SessionHolderKind holder = SessionHolderKind.Npc, ISessionItemScriptHost? scripts = null, CreatedScripts? report = null, bool ground = false)
    {
        var created = NewNpcEntry(npc, kind, InventoryDomain.MadeFacts(kind) with { AmmoType = ammoType, Quantity = units }, insertCheck, out var refusal, holder, scripts, report, ground: ground);
        if (created is null) return (null, refusal);
        _inventory.Ammo[created.Value.Entity] = new(created.Value.Entity, ammoType, units);
        return (created, null);
    }

    /// <summary>
    /// ARC-SCRIPT-2887 (ledger TQ19, NT2): what the item scripts of a created object came to for the caller to report with its result: the runs (get and insert-item) in order, the lines they printed, and the insert-item script's answer. Empty when the prototype has
    /// neither point or no host ran them.
    /// </summary>
    private sealed class CreatedScripts
    {
        internal List<SessionItemScriptRun> Runs { get; } = [];
        internal List<SessionScriptMessage> Messages { get; } = [];
        internal ScrRunResult? Insert { get; set; }

        /// <summary>ARC-SCRIPT-2911: the new pile or stack was refused (the get script skipped the default, <see cref="Declined"/>, or the insert check answered <see cref="RefusedVerdict"/>) and lies on the holder's tile; the call still succeeds.</summary>
        internal bool OnGround { get; set; }
        internal bool Declined { get; set; }
        internal SessionInsertVerdict? RefusedVerdict { get; set; }

        /// <summary>The move also has a source pile whose own insert-item runs commit first (the original runs the source's 21 before the new item's scripts): the new item's runs wait in <see cref="Pending"/> for <see cref="CommitPending"/>.</summary>
        internal bool Defer { get; init; }
        internal ItemScriptRuns? Pending { get; set; }

        internal void CommitPending()
        {
            Pending?.Commit();
            Pending = null;
        }

        /// <summary>The result with the created object's script runs, lines and answer added after its own.</summary>
        internal InventoryMutationResult Apply(InventoryMutationResult result)
        {
            if (OnGround) result = result with { Verdict = RefusedVerdict, DeclinedBy = Declined ? SessionScriptPoint.Get : result.DeclinedBy };
            return Runs.Count == 0 ? result
                : result with { Scripts = [.. result.Scripts, .. Runs], Messages = [.. result.Messages, .. Messages], InsertScript = result.InsertScript ?? Insert };
        }
    }

    // A new made item at the end of the NPC's (ARC-WORLD-090: or a container's) list after the host's insert check for it (the
    // original's new object moved in by 0x4617F0); its own bookkeeping (gold or ammunition record, revision) is the caller's.
    private SessionMadeItem? NewNpcEntry(string npc, SessionItemKind kind, SessionItemFacts facts, SessionInsertCheck insertCheck, out InventoryMutationResult? refusal,
        SessionHolderKind holder = SessionHolderKind.Npc, ISessionItemScriptHost? scripts = null, CreatedScripts? report = null, bool hero = false, SessionAmmoRecord? mergeInto = null, bool ground = false)
    {
        refusal = null;
        // ARC-SCRIPT-2857 (NT2, IE2): the new object moves in through 0x4617F0 → 0x461810. It has no parent, so 0x461810 does not forward to the parented transfer 0x461CA0: the holder's transfer script (point 32) is never
        // run for it, whatever the holder's scripts. ARC-SCRIPT-2887: the fresh path runs the new item's own get script (point 4) with the holder as the triggerer before the insert check, and at the end of the insert 0x466640 its insert-item script (21), also
        // with the holder as the triggerer; both from the prototype's array (the new item owns no slot yet), the get script's skip default refuses the move.
        // ARC-SCRIPT-2910: <paramref name="hero"/> makes the hero the recipient (npc then names the hero as the triggerer): its list budget, its insert check (the hero's verdict, the room for the stack) and its list place.
        // ARC-SCRIPT-2909: <paramref name="mergeInto"/> is a created ammunition object that joins the holder's stack of its type (0x466640's merge branch, 0x462930): it runs the same two scripts, then is destroyed after its units are raised; no list place is
        // taken, so the list budgets do not apply, and the host's insert check answers for the stack. The caller adds the units; the returned item is the incoming one, never created. (A gold or ammunition move onto an existing pile or stack, 0x464830 and 0x4658E0, and
        // a restock's coins onto a persistent pile create no object at all: they run none of this.)
        if (mergeInto is null && (hero ? _inventory.Held >= _limits.MaxPcItems : _inventory.AddedTo(npc).Count + SeedItemCount(npc, holder) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems))
        {
            refusal = RejectItem(GameSessionFailure.BudgetExceeded);
            return null;
        }
        if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) { refusal = RejectItem(GameSessionFailure.BudgetExceeded); return null; }
        if (_revision == long.MaxValue) { refusal = RejectItem(GameSessionFailure.RevisionExhausted); return null; }
        var (revision, seeds, map, flagVersion) = (_revision, _inventory.SeedGeneration, _actor.Map, _flags.Version);
        // The new item answers as the made item it will be while its scripts run (it is created after them, like a split throw's stack).
        _pendingMade = (entity, kind);
        ItemScriptRuns? runs = null;
        try
        {
            var hasGet = InheritedSlot(entity, SessionScriptPoint.Get) is not null;
            var hasInsert = InheritedSlot(entity, SessionScriptPoint.InsertItem) is not null;
            if ((hasGet || hasInsert) && scripts is null) { refusal = RejectItem(GameSessionFailure.ItemScriptUnsupported); return null; }
            if (hasGet || hasInsert) runs = new ItemScriptRuns(this, scripts!, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = 0 }) { NpcTriggerer = !hero && holder == SessionHolderKind.Npc };
            bool Stale() => _revision != revision || _inventory.SeedGeneration != seeds || _actor.Map != map || _flags.Version != flagVersion;
            InventoryMutationResult Refused(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
                RejectItem(failure, verdict) with { Scripts = runs?.Runs ?? [], Messages = failure == GameSessionFailure.InsertRefused ? runs?.Messages ?? [] : [] };
            var declined = false;
            if (runs is not null && hasGet)
            {
                var gate = runs.Run(SessionScriptPoint.Get, entity, npc, null);
                ThrowIfDisposed();
                if (Stale()) { refusal = Refused(GameSessionFailure.StaleVersion); return null; }
                if (runs.Failure != GameSessionFailure.None) { refusal = Refused(runs.Failure); return null; }
                // Skip default: the move does not happen. A refused restock item is destroyed by 0x463E20, so it is refused here with the refusing run's own effects not applied (the documented limit of a refused move). A new pile or stack
                // (ARC-SCRIPT-2911, <paramref name="ground"/>) is not destroyed: 0x461810 returns 0 and the object stays where 0x464970 / 0x4658E0 made it, on the holder's tile, and the call reports success (see GroundTarget).
                if (gate?.Outcome == ScrOutcome.SkipDefault)
                {
                    if (ground && !hero && mergeInto is null) declined = true;
                    else { refusal = Refused(GameSessionFailure.InsertRefused) with { DeclinedBy = SessionScriptPoint.Get }; return null; }
                }
            }
            // ARC-WORLD-085: the new object has its prototype's weight (the NPC's own weight is the host's insert check).
            var weight = CreatedWeight(kind.Prototype);
            SessionInsertVerdict? refusedVerdict = null, verdict = null;
            if (!declined)
            {
                verdict = mergeInto is { } stack ? insertCheck(new SessionInventoryItem(stack.Entity, facts))
                    : hero
                    ? HeroInsertVerdict(insertCheck(new SessionInventoryItem(entity, facts)), 0, new((int)kind.Type, weight.Weight, weight.MagicWeight, weight.Complexity, facts.Quantity ?? 0), room: NewItemRoom(entity, kind))
                    : insertCheck(new SessionInventoryItem(entity, facts with { Weight = weight.Weight, MagicWeight = weight.MagicWeight }));
                ThrowIfDisposed();
                if (Stale()) { refusal = Refused(GameSessionFailure.StaleVersion, verdict); return null; }
                if (verdict != SessionInsertVerdict.Accepted)
                {
                    if (!(ground && !hero && mergeInto is null)) { refusal = Refused(GameSessionFailure.InsertRefused, verdict); return null; }
                    refusedVerdict = verdict;
                }
            }
            // ARC-SCRIPT-2911: the refused new pile or stack lies on the holder's tile when the host knows it and the ground can hold it; else the move is refused as before (InsertRefused, nothing created).
            CellCoord? lying = null;
            if (declined || refusedVerdict is not null)
            {
                lying = GroundTarget(npc);
                ThrowIfDisposed();
                if (Stale()) { refusal = Refused(GameSessionFailure.StaleVersion, refusedVerdict); return null; }
                if (lying is null)
                {
                    refusal = declined ? Refused(GameSessionFailure.InsertRefused) with { DeclinedBy = SessionScriptPoint.Get } : Refused(GameSessionFailure.InsertRefused, refusedVerdict);
                    return null;
                }
            }
            ScrRunResult? insertRun = null;
            if (runs is not null)
            {
                // The insert-item script runs at the end of the insertion and cannot cancel it: it is run here, before the item is made, and applied with it, or the whole move is refused. An object left on the ground was never inserted: it has none.
                if (hasInsert && lying is null) insertRun = runs.Run(SessionScriptPoint.InsertItem, entity, npc, null);
                ThrowIfDisposed();
                if (Stale()) { refusal = Refused(GameSessionFailure.StaleVersion, verdict); return null; }
                runs.Prepare();
                if (runs.Failure != GameSessionFailure.None) { refusal = Refused(runs.Failure, verdict); return null; }
            }
            var merging = mergeInto is not null;
            var made = merging ? new SessionMadeItem(entity, kind)
                : lying is { } cell ? CreateMadeItem(new(entity, kind, SessionCreatePlace.Ground, weight, 0, null, _actor.Map, cell, SessionPlacedSource.Made))
                : hero ? CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, weight))
                : CreateMadeItem(new(entity, kind, holder == SessionHolderKind.Container ? SessionCreatePlace.ContainerList : SessionCreatePlace.NpcList, weight, Holder: npc));
            if (!hero && !merging && lying is null) StoreInsertPlace(npc, made.Entity, container: holder == SessionHolderKind.Container);
            if (lying is not null && report is not null) { report.OnGround = true; report.Declined = declined; report.RefusedVerdict = refusedVerdict; }
            if (runs is not null)
            {
                if (report is { Defer: true }) report.Pending = runs;
                else runs.Commit();
                if (report is not null)
                {
                    report.Runs.AddRange(runs.Runs);
                    report.Messages.AddRange(runs.Messages);
                    report.Insert = insertRun;
                }
                // The incoming object is destroyed after the merge, with the slots and flags its runs wrote (its number stays used).
                if (merging) Discard(entity);
            }
            return made;
        }
        finally { _pendingMade = null; }
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

    // ARC-SCRIPT-2827: whether the seed of the shown map lists the holder with the point: the script host answers for that map's rows alone.
    private bool ShownHolderHasScript(string holder, SessionHolderKind kind, int point) =>
        _inventory.Seeds.TryGetValue(_actor.Map, out var seed) && seed.Holders.TryGetValue(holder, out var row) && row.Kind == kind && row.ScriptPoints.Contains(point);

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
