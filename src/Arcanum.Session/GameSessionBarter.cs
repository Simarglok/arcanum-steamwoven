using Arcanum.Simulation.Skills;
using Arcanum.Simulation;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-090: what a barter window's open changed: the coins and piles that moved from the substitute container onto the
/// merchant, the merchant's pile they joined (null when none moved) and how many items it newly marked identified.</summary>
public readonly record struct SessionBarterOpenResult(GameSessionDisposition Disposition, GameSessionFailure Failure, int Coins, int Piles,
    string? Pile, int Identified, GameSessionVersion Version)
{
    /// <summary>ARC-SCRIPT-2855: the lines the moved piles' insert-item scripts printed, in order, for the host to show; empty when none ran.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get => _messages ?? Array.Empty<SessionScriptMessage>(); init => _messages = value; }
    private readonly IReadOnlyList<SessionScriptMessage>? _messages;
}

/// <summary>
/// ARC-UI-050: the barter window's trades between the hero and an NPC merchant, in the original's order (0x578EA0, ledger BT5): the item
/// (or ammunition units) moves first, then the coins, only when the move succeeded. The price is the caller's (the barter rules of
/// <see cref="Arcanum.Simulation.Items.TradeRules"/>); a price of 0 moves the item for nothing, as a follower's window does. The reaction
/// change of a generous trade is the caller's next command. ARC-WORLD-090: a merchant's substitute container (NPC field 0x126, which the
/// host finds) is the shown side: <see cref="OpenBarter"/> moves its coins onto the merchant and identifies both lists, and the trades
/// move items out of and into it while the coins stay the merchant's. Contract: docs/world/barter-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero's haggle as the barter price reads it (0x461F80 through 0x4C62E0 and 0x4C60C0): its effectiveness (4L + 10, +10 for
    /// extraordinary intelligence, the game difficulty, the 95 cap, <see cref="SkillCheckRules.Effectiveness"/>) and its training; null
    /// without a hero or its life record.
    /// </summary>
    public (int Effectiveness, Arcanum.Simulation.Combat.SkillTraining Training)? QueryHeroHaggle()
    {
        ThrowIfDisposed();
        if (_hero is null || _life.Hero is null) return null;
        var request = HeroSkillRequest(Skill.Haggle, null);
        return (SkillCheckRules.Effectiveness(Skill.Haggle, request.Level, request.Effectiveness), request.Training);
    }

    /// <summary>
    /// ARC-WORLD-090: the open of a barter window with the NPC on the actor's map (0x572640 in barter mode, ledger BT4, BT11): with a
    /// substitute <paramref name="container"/> (the host's: NPC field 0x126 of an NPC without a leader, within 20 tiles, 0x45F650), every
    /// gold pile in the container's list moves onto the NPC in list order (0x464830 with the pile given: the container's pile is
    /// destroyed, the NPC's first pile grows, an NPC without one gets a new <c>made:n</c> pile of <paramref name="pileKind"/> after
    /// <paramref name="npcInsert"/>, which sees the first pile's coins); then, when <paramref name="identify"/> (the host's: no PC at the top
    /// of the NPC's leader chain, 0x45DDA0), every item of the NPC's list and of the container's is marked identified (0x4643D0: item flag
    /// 0x1 on each list entry, worn and gold included). A pile of 0 coins stays in the container: the original's loop finds it again for
    /// ever (BT11, a hang: a technical bug, fixed in both modes without a switch). Refused before any effect: an invalid input
    /// (<see cref="GameSessionFailure.InvalidItem"/>), the version, no seed for the actor's map (<see cref="GameSessionFailure.InventoryUnavailable"/>),
    /// an NPC no attached seed lists (a companion carried here is listed by its home map's seed) or a container (not spilled) the actor's map seed does not list (<see cref="GameSessionFailure.UnknownHolder"/>), a pile with an
    /// insert-item script when there is no <paramref name="scripts"/> host (ARC-SCRIPT-2855, ledger NT1: with one the event runs on each moved pile first, 0x464830 → 0x441980 point 21 with the merchant as the triggerer, applied with the move (all runs before the first pile moves; the original's 0x4643D0 loop moves pile by pile, so a later pile's script reads the state before the earlier piles joined the merchant's: a limit), a run
    /// the session cannot apply refuses: <see cref="GameSessionFailure.ItemScriptUnsupported"/>), a sum past 32 bits or a full record, list or identified
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, and the new pile's refusals of <see cref="GiveGoldToNpc"/>.
    /// One revision; nothing to move or mark is no change.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x572640, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4643D0, OriginalFunctionScope.Partial)]
    public SessionBarterOpenResult OpenBarter(GameSessionVersion expected, string npc, string? container, bool identify, SessionItemKind pileKind,
        SessionInsertCheck npcInsert, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(npcInsert);
        SessionBarterOpenResult Refuse(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, 0, 0, null, 0, Version);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || pileKind.Type != SessionItemType.Gold ||
            container is not null && (!SessionValidation.Entity(container) || container.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || container == npc))
            return Refuse(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Refuse(GameSessionFailure.StaleVersion);
        var map = _actor.Map;
        if (!_inventory.Seeds.TryGetValue(map, out var seed)) return Refuse(GameSessionFailure.InventoryUnavailable);
        // ARC-WORLD-3118: a companion carried to this map is listed by its home map's seed (the actor's map first, else another attached one, NI4: an object stands on one map).
        if (!NpcHolderSeed(npc, out var merchantMap, out var merchantSeed, out var merchant)) return Refuse(GameSessionFailure.UnknownHolder);
        SessionSeedHolder? box = null;
        if (container is not null && (!seed.Holders.TryGetValue(container, out box) || box.Kind != SessionHolderKind.Container || SpillCell(map, seed, container) is not null))
            return Refuse(GameSessionFailure.UnknownHolder);
        var stock = box is null ? [] : _inventory.Current(seed, box, entry => DroppedCell(map, seed, entry)).ToArray();
        var piles = stock.Where(entry => entry.Facts.Kind.Type == SessionItemType.Gold && (entry.Facts.Quantity ?? 0) != 0).ToArray();
        // ARC-SCRIPT-2855 (BT11, NT1): each moved pile's insert-item script (21) runs first with the merchant as the triggerer, in the container's order.
        var insert = piles.Length == 0 ? PileInsert.None : RunPileInserts(scripts, piles.Select(entry => entry.Entity).ToArray(), npc, npcTriggerer: true, container);
        if (insert.Failure != GameSessionFailure.None) return Refuse(insert.Failure);
        var coins = piles.Sum(entry => (long)entry.Facts.Quantity!.Value);
        var target = piles.Length == 0 ? null : NpcPile(npc);
        if (target is { } grown && grown.Quantity + coins > int.MaxValue || target is null && coins > int.MaxValue) return Refuse(GameSessionFailure.BudgetExceeded);
        // The pile records after the move: the moved piles' go, the merchant's pile gets one unless it has one.
        var records = _inventory.Piles.Count - piles.Count(entry => _inventory.Piles.ContainsKey(entry.Entity)) +
            (piles.Length > 0 && (target is null || !_inventory.Piles.ContainsKey(target.Value.Entity)) ? 1 : 0);
        if (records > _limits.MaxGoldRecords) return Refuse(GameSessionFailure.BudgetExceeded);
        if (_durability.Count + piles.Count(entry => !_durability.Records.ContainsKey(entry.Entity)) > _limits.MaxDurabilityRecords) return Refuse(GameSessionFailure.BudgetExceeded);
        var moved = piles.Select(entry => entry.Entity).ToHashSet(StringComparer.Ordinal);
        var marked = !identify ? [] : _inventory.Current(merchantSeed, merchant, entry => DroppedCell(merchantMap, merchantSeed, entry)).Select(entry => entry.Entity)
            .Concat(stock.Select(entry => entry.Entity).Where(entity => !moved.Contains(entity)))
            .Where(entity => !_inventory.Identified.Contains(entity)).Distinct(StringComparer.Ordinal).ToList();
        // A new pile of the merchant's is identified too.
        if (_inventory.Identified.Count + marked.Count + (identify && piles.Length > 0 && target is null ? 1 : 0) > SessionInventoryState.MaxIdentified)
            return Refuse(GameSessionFailure.BudgetExceeded);
        if (piles.Length == 0 && marked.Count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, 0, null, 0, Version);
        if (_revision == long.MaxValue) return Refuse(GameSessionFailure.RevisionExhausted);
        string? pile = target?.Entity;
        var report = new CreatedScripts { Defer = true };
        if (piles.Length > 0 && target is null)
        {
            // 0x464830 creates the first pile with its coins and moves it in (0x4617F0); the later piles join it.
            var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = piles[0].Facts.Quantity }, npcInsert, out var refusal, scripts: scripts, report: report);
            if (created is null) return Refuse(refusal!.Value.Failure) with { Messages = refusal.Value.Messages };
            pile = created.Value.Entity;
            if (identify) marked.Add(pile);
        }
        if (pile is not null) _inventory.Piles[pile] = (int)((target?.Quantity ?? 0) + coins);
        insert.Runs?.Commit();
        report.CommitPending();
        foreach (var entry in piles) DestroyObject(entry.Entity);
        _inventory.Identified.UnionWith(marked);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, (int)coins, piles.Length, pile, marked.Count, Version) { Messages = [.. insert.Runs?.Messages ?? [], .. report.Messages] };
    }

    /// <summary>ARC-WORLD-090: whether the session marked the item identified (item flag 0x1 a barter window set); the item's own
    /// stored flag is the host's. Throws for an invalid entity id.</summary>
    public bool QueryItemIdentified(string item)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(item)) throw new ArgumentException("Invalid entity id.", nameof(item));
        return _inventory.Identified.Contains(item);
    }

    /// <summary>
    /// The hero buys <paramref name="count"/> of an NPC's item for <paramref name="unitPrice"/> each: a whole item (count 1) through
    /// <see cref="TakeItemFromNpc"/>, or units of an ammunition stack through <see cref="TakeAmmoFromNpc"/> (a new hero stack of
    /// <paramref name="newStackKind"/>); then the hero pays <c>count × unitPrice</c> to the NPC through <see cref="GiveGoldToNpc"/> (a new
    /// pile of <paramref name="pileKind"/> after <paramref name="npcInsert"/>). Refused before any effect, beyond those calls' refusals: a
    /// negative price or a count below 1 or above 1 for an item (<see cref="GameSessionFailure.InvalidItem"/>), a total past 32 bits
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>), a hero with fewer coins than the total (<see cref="GameSessionFailure.GoldShort"/>,
    /// the original's "not enough money": its unit test before the move, its rolled-back stack payment after), and the payment's own
    /// refusals that do not depend on the move (an NPC pile past 32 bits, a full pile budget, and for an NPC without a pile a transfer
    /// script or a full NPC item budget or list, checked before the move). A payment refused after the move (the host's insert check for a
    /// new pile, an exhausted made number, coins an insert-item script spent) leaves the item with the hero and reports the payment's
    /// failure with <see cref="InventoryMutationResult.Item"/> set, as the original leaves a moved item that its payment failed.
    /// ARC-WORLD-090: with a <paramref name="container"/> (the merchant's substitute container on the actor's map, not spilled) the item
    /// comes out of the container's current list (a seed item or one a sale put there: <see cref="TakeItemFromNpc"/>'s rules with the
    /// container as holder, or units of one of its stacks); the coins still go to the NPC (0x578EA0 pays the NPC object, never the
    /// container); a container its seed does not list is <see cref="GameSessionFailure.UnknownHolder"/>.
    /// ARC-UI-1926: with <paramref name="wornItem"/> the item is one the NPC wears (the barter window's doll view): the move takes it off its slot as a loot does
    /// (<see cref="TakeItemFromNpc"/> with <c>unwield</c>, over <paramref name="npcWearer"/>), no container.
    /// </summary>
    public InventoryMutationResult BarterBuy(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionItemKind pileKind, SessionInsertCheck heroInsert, SessionInsertCheck npcInsert, ISessionItemScriptHost? scripts = null, string? container = null,
        bool wornItem = false, SessionNpcWieldScripts? npcWearer = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(heroInsert);
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item) ||
            unitPrice < 0 || count < 1 || pileKind.Type != SessionItemType.Gold || !ValidContainer(container, npc)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (container is not null && !ShownContainer(container)) return RejectItem(GameSessionFailure.UnknownHolder);
        var stack = (container is null ? NpcStacks(npc) : ContainerStacks(container)).Any(pair => pair.Stack.Entity == item);
        if (!stack && count != 1) return RejectItem(GameSessionFailure.InvalidItem);
        var total = (long)unitPrice * count;
        if (total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (total > 0)
        {
            if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
            if (_inventory.Gold < total) return RejectItem(GameSessionFailure.GoldShort);
            if (NpcPile(npc) is { } pile)
            {
                if (pile.Quantity + total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
                if (!_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            }
            else
            {
                if (_inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
                if (_inventory.AddedTo(npc).Count + SeedItemCount(npc) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems)
                    return RejectItem(GameSessionFailure.BudgetExceeded);
            }
        }
        var moved = container is not null ? TakeFromContainer(container, item, stack, count, newStackKind, heroInsert, scripts)
            : stack ? TakeAmmoFromNpc(Version, npc, item, count, newStackKind, heroInsert, scripts)
                : TakeItemFromNpc(Version, npc, item, heroInsert, scripts, unwield: wornItem, npcWearer: npcWearer);
        // A recipient's transfer script that declined the item (DeclinedBy, ARC-SCRIPT-2827) leaves it with the hero: no payment.
        if (moved.Disposition != GameSessionDisposition.Changed || moved.DeclinedBy is not null || total == 0) return moved;
        var paid = GiveGoldToNpc(Version, npc, (int)total, pileKind, npcInsert, scripts);
        return paid.Disposition == GameSessionDisposition.Changed ? moved with { Version = Version, Scripts = [.. moved.Scripts, .. paid.Scripts], Messages = [.. moved.Messages, .. paid.Messages] }
            : paid with { Item = moved.Item, Version = Version };
    }

    /// <summary>
    /// ARC-UI-2215 (a lost bet, flags 0x11 of 0x579B60 → 0x578EA0, ledger GB3, GB6): the hero pays <c>count × unitPrice</c> to the NPC for a staked item
    /// and the item stays where it is. The move block of 0x578EA0 is skipped under bit 0x10; the payment through 0x464830 (<see cref="GiveGoldToNpc"/>)
    /// still runs. <see cref="BarterBuy"/>'s input checks apply, plus a stake the holder (the NPC, or the shown container) really lists
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>) and, for an ammunition stack, units enough (<see cref="GameSessionFailure.AmmoShort"/>); no room test for the hero, no item scripts. A total of 0 pays nothing and still counts
    /// as one change (the caller's roll, draws and fate stand in this revision); a payment's refusals leave everything as it was.
    /// </summary>
    private InventoryMutationResult PayForStakedItem(string npc, string item, int count, int unitPrice, SessionItemKind pileKind, SessionInsertCheck npcInsert, string? container)
    {
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item) ||
            unitPrice < 0 || count < 1 || pileKind.Type != SessionItemType.Gold || !ValidContainer(container, npc)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (container is not null && !ShownContainer(container)) return RejectItem(GameSessionFailure.UnknownHolder);
        var source = (container is null ? NpcStacks(npc) : ContainerStacks(container)).Select(pair => pair.Stack).Cast<SessionAmmoRecord?>().FirstOrDefault(pair => pair!.Value.Entity == item);
        var stack = source is not null;
        if (!stack && count != 1) return RejectItem(GameSessionFailure.InvalidItem);
        if (source is { } units && units.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        if (!stack && QueryHolderItems(container ?? npc)?.Any(listed => listed.Entity == item) != true) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var total = (long)unitPrice * count;
        if (total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (total > 0) return GiveGoldToNpc(Version, npc, (int)total, pileKind, npcInsert);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, Version);
    }

    /// <summary>
    /// The hero sells <paramref name="count"/> of its item to an NPC for <paramref name="unitPrice"/> each (the caller's quote, already
    /// limited to the NPC's coins, 0x575360): a whole item (count 1) through <see cref="GiveItemToNpc"/>, or units of an ammunition stack
    /// through <see cref="GiveAmmoToNpc"/> (a new NPC stack of <paramref name="newStackKind"/>); then the NPC pays <c>count × unitPrice</c>
    /// through <see cref="TakeGoldFromNpc"/>. Refused before any effect, beyond those calls' refusals: <see cref="BarterBuy"/>'s input
    /// checks, an NPC with fewer coins than the total (<see cref="GameSessionFailure.GoldShort"/>, the original's rolled-back stack sale),
    /// a pile with an insert-item script when there is no <paramref name="scripts"/> host or its run cannot be applied (the payment's run is tried before the item moves; ARC-SCRIPT-2855, <see cref="GameSessionFailure.ItemScriptUnsupported"/>), a hero sum past 32 bits or a full record
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>). ARC-WORLD-090: with a <paramref name="container"/> the item goes into the
    /// container's list (0x578EA0 moves a sold item into the shown object: <see cref="GiveItemToNpc"/>'s rules with the container as
    /// holder; units of ammunition join its first stack of the type, which the original's lookup never finds, AMA5); the coins still come
    /// from the NPC. With <paramref name="identify"/> (a merchant that is not a follower, BT5) a whole item that moved is marked identified
    /// in the move's revision; a full identified budget refuses before any effect. ARC-MAGIC-1743: with <paramref name="scripts"/> a sold item with item event scripts runs them as the move does
    /// (<see cref="GiveItemToNpc"/>); without a host it is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>).
    /// </summary>
    public InventoryMutationResult BarterSell(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionInsertCheck npcInsert, string? container = null, bool identify = false, ISessionItemScriptHost? scripts = null) =>
        SellCore(expected, npc, item, count, unitPrice, newStackKind, npcInsert, container, identify, scripts, null, null);

    // BarterSell's body; ARC-UI-2159: slotWearer (with the item's listed points) is the merchant as the wearer of a sale onto a slot of its doll.
    private InventoryMutationResult SellCore(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionInsertCheck npcInsert, string? container, bool identify, ISessionItemScriptHost? scripts, NpcWearer? slotWearer, IReadOnlyList<int>? slotPoints)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item) ||
            unitPrice < 0 || count < 1 || !ValidContainer(container, npc)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (container is not null && !ShownContainer(container)) return RejectItem(GameSessionFailure.UnknownHolder);
        var stack = HeroHeld().Any(held => held.Entity == item && held.Kind.Type == SessionItemType.Ammo);
        if (!stack && count != 1) return RejectItem(GameSessionFailure.InvalidItem);
        var total = (long)unitPrice * count;
        if (total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (total > 0)
        {
            if (_hero is null) return RejectItem(GameSessionFailure.GoldUnavailable);
            if (NpcPile(npc) is not { } pile || pile.Quantity < total) return RejectItem(GameSessionFailure.GoldShort);
            // ARC-SCRIPT-2855: the payment's insert-item script (21 on the merchant's pile) is tried before the item moves, so a run the session cannot apply refuses the sale with nothing moved; the payment runs it again for real.
            if (RunPileInsert(scripts, pile.Entity, SessionEventKinds.PlayerEntity, npcTriggerer: false, pile.Quantity == total ? npc : null) is { Failure: not GameSessionFailure.None } probe) return RejectItem(probe.Failure);
            if (_inventory.Gold + total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (pile.Quantity == total ? !_durability.Records.ContainsKey(pile.Entity) && _durability.Count >= _limits.MaxDurabilityRecords
                : !_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        }
        var into = container ?? npc;
        var kind = container is null ? SessionHolderKind.Npc : SessionHolderKind.Container;
        var moved = stack ? GiveAmmoTo(into, kind, item, count, newStackKind, npcInsert, scripts) : GiveItemTo(into, kind, item, npcInsert, identify, scripts: scripts, slotWearer: slotWearer, slotPoints: slotPoints);
        // ARC-SCRIPT-2827: the merchant's transfer script declined the item (DeclinedBy): it stays with the hero and nothing is paid.
        if (moved.Disposition != GameSessionDisposition.Changed || moved.DeclinedBy is not null || total == 0) return moved;
        var paid = TakeGoldFromNpc(Version, npc, (int)total, scripts: scripts);
        return paid.Disposition == GameSessionDisposition.Changed
            ? moved with { Version = Version, InsertScript = paid.InsertScript ?? moved.InsertScript, Scripts = [.. moved.Scripts, .. paid.Scripts], Messages = [.. moved.Messages, .. paid.Messages] }
            : paid with { Item = moved.Item, Version = Version, Scripts = moved.Scripts, InsertScript = moved.InsertScript, Messages = moved.Messages };
    }

    /// <summary>
    /// ARC-UI-2138 (0x575360 → 0x5788C0 → 0x578EA0 with the location 1000 + slot, executor 0x461810 → 0x461CA0 → 0x466640, ledger IW35, BW5): the hero's item, worn or in its pack, is sold onto a slot of the merchant's doll and the merchant
    /// wears it. <see cref="BarterSell"/>'s sale of one whole item into the NPC itself (never the substitute container: the executor puts a slot location on the merchant, 0x57934B), then the NPC's worn record names the item in <paramref name="slot"/>
    /// (the record a dressed NPC keeps, ARC-WORLD-103; for an NPC the session never dressed it starts from what its map data has worn), in the sale's one revision. The wear test (0x464D20) is the caller's, as <see cref="QueryWearRefusal"/>'s is for the hero. Refused
    /// before any effect: a slot that is not a wear slot or that the NPC's list shows filled (the original puts the item back, no swap for an NPC), a first worn record past the saved budget, and an item the session cannot wear for the NPC: one with item spells (<see cref="GameSessionFailure.ItemEffectUnsupported"/>; NOT READY: the NPC's item spells are not modelled, as in its best-items wield) or one with item event
    /// scripts on the move when the host gives no script host or no wield context of the merchant (<see cref="GameSessionFailure.ItemScriptUnsupported"/>). ARC-UI-2159: with them the hero's removal runs (wield off, remove item; the hero the wearer) and the merchant's wield on (24)
    /// and insert item (21) run (<paramref name="npcWearer"/>: the merchant is their wearer and triggerer, its effect list the runs change), planned in two run sets, one commit and the sale's one revision (NOT READY: the two sets do not share pending state, so a global variable, flag, quest, story or item slot one set writes is not read by the other's runs, and when both write the same one the later commit wins). NOT READY: the art and
    /// light of the NPC's wear. The room test the executor runs for the NPC (0x466510, its carry weight: code 1, no grid room for a slot) is <paramref name="npcInsert"/>. A sale refused in the sale rules leaves the NPC's record as it was; a payment refused after the move leaves the item on the NPC's slot, as the original leaves a moved item.
    /// </summary>
    public InventoryMutationResult BarterSellWorn(GameSessionVersion expected, string npc, string item, EquipmentSlot slot, SessionWearableItem wearable, int unitPrice,
        SessionItemKind newStackKind, SessionInsertCheck npcInsert, bool identify = false, ISessionItemScriptHost? scripts = null, SessionNpcWieldScripts? npcWearer = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(wearable);
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (!SessionValidation.Entity(npc) || !SessionValidation.Entity(item) || !Enum.IsDefined(slot)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (EquipmentRules.WearSlot(wearable.Item) is not { } own || own != slot && !(own is EquipmentSlot.Ring1 && slot is EquipmentSlot.Ring2)) return RejectItem(GameSessionFailure.InvalidEquipment);
        // ARC-UI-2159 (IE4): an item with item event scripts is worn by the merchant through the item script host, as the NPC best-items wield does (the merchant is the wearer and the triggerer of the wield on and insert item runs); without the host or the
        // merchant's wield context the sale is refused.
        var points = EquipmentDomain.MovePoints(wearable.ScriptPoints);
        if (points.Count > 0 && (scripts is null || npcWearer is null)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (wearable.HasItemSpells) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        if (QueryHolderItems(npc) is not { } listed) return RejectItem(GameSessionFailure.UnknownHolder);
        var record = new SortedDictionary<EquipmentSlot, string>();
        foreach (var held in listed)
            if (held.Facts.Worn && held.Facts.InventoryLocation is >= (int)EquipmentSlot.Helmet and <= (int)EquipmentSlot.Boots) record[(EquipmentSlot)held.Facts.InventoryLocation] = held.Entity;
        if (record.ContainsKey(slot)) return RejectItem(GameSessionFailure.InvalidEquipment);
        var had = _inventory.NpcWorn.TryGetValue(npc, out var before);
        if (!had && _inventory.NpcWorn.Count >= SessionInventoryState.MaxNpcWorn) return RejectItem(GameSessionFailure.BudgetExceeded);
        record[slot] = item;
        _inventory.NpcWorn[npc] = record;
        InventoryMutationResult result;
        try
        {
            result = SellCore(expected, npc, item, 1, unitPrice, newStackKind, npcInsert, null, identify, scripts,
                points.Count > 0 ? new NpcWearer(npc, npcWearer!.Target, NpcStatsOverSheet(npc, npcWearer.Stored)) : null, points);
        }
        catch
        {
            // A host callback that throws leaves the item with the hero: the record is as it was.
            if (had) _inventory.NpcWorn[npc] = before!; else _inventory.NpcWorn.Remove(npc);
            throw;
        }
        // The item reached the NPC's list (a sale, or a payment that failed after the move): it stays on the slot; else the record is as it was.
        if (_inventory.NpcOf(item) != npc)
        {
            if (had) _inventory.NpcWorn[npc] = before!; else _inventory.NpcWorn.Remove(npc);
        }
        return result;
    }

    private static bool ValidContainer(string? container, string npc) => container is null ||
        SessionValidation.Entity(container) && !container.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) && container != npc;

    // ARC-WORLD-090: a container of the actor's map seed that is not spilled, the shown side of a barter window.
    private bool ShownContainer(string container) => _inventory.Seeds.TryGetValue(_actor.Map, out var seed) &&
        seed.Holders.TryGetValue(container, out var row) && row.Kind == SessionHolderKind.Container && SpillCell(_actor.Map, seed, container) is null;

    // ARC-WORLD-090: an item or units of a stack out of the container's current list to the hero (TakeItemFromNpc's and TakeAmmoFromNpc's
    // rules with the container as holder).
    private InventoryMutationResult TakeFromContainer(string container, string item, bool stack, int count, SessionItemKind newStackKind,
        SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        var seed = _inventory.Seeds[_actor.Map];
        if (stack)
        {
            if (newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
            var source = ContainerStacks(container).First(pair => pair.Stack.Entity == item).Stack;
            if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
            // ARC-SCRIPT-2855 (AM4): the container's stack's insert-item script runs first with the hero as the triggerer.
            var insert = RunPileInsert(scripts, source.Entity, SessionEventKinds.PlayerEntity, npcTriggerer: false, source.Units == count ? container : null);
            if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
            var hero = HeroStackOfType(source.AmmoType);
            var report = new CreatedScripts { Defer = true };
            return MoveUnits(source, count, hero, hero is null ? () => NewHeroStack(newStackKind, source.AmmoType, count, insertCheck, scripts, report) : null, insert, report);
        }
        if (_inventory.Lists.IsContainer(container) && _inventory.AddedTo(container).FirstOrDefault(added => added.Entity == item) is { Entity: not null } added)
            return Take(seed, new(item, _inventory.AddedFacts(added)), container, insertCheck, scripts);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != container || _inventory.Taken.Contains(item) || DroppedCell(_actor.Map, seed, item) is not null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(seed, new(item, _inventory.WithAmmo(item, seed.Items[item].Facts)), container, insertCheck, scripts);
    }

    // ARC-WORLD-090: GiveAmmoToNpc's move into an NPC or a container (whose stack lookup follows AMA5).
    private InventoryMutationResult GiveAmmoTo(string holder, SessionHolderKind kind, string stack, int count, SessionItemKind newStackKind, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null)
    {
        if (kind == SessionHolderKind.Npc) return GiveAmmoToNpc(Version, holder, stack, count, newStackKind, insertCheck, scripts);
        if (newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!HeroHeld().Any(held => held.Entity == stack && held.Kind.Type == SessionItemType.Ammo)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var source = _inventory.Ammo[stack];
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        // ARC-SCRIPT-2855 (AM4): the hero's stack's insert-item script runs first with the container as the triggerer (no critter: the hero's reading of a printed line).
        var insert = RunPileInsert(scripts, source.Entity, holder, npcTriggerer: false, source.Units == count ? SessionEventKinds.PlayerEntity : null);
        if (insert.Failure != GameSessionFailure.None) return insert.Refused(RejectItem(insert.Failure));
        var into = ContainerStackOfType(holder, source.AmmoType);
        var report = new CreatedScripts { Defer = true };
        return MoveUnits(source, count, into, into is null ? () => NewNpcStack(holder, newStackKind, source.AmmoType, count, insertCheck, kind, scripts, report) : null, insert, report);
    }
}
