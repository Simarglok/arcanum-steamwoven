using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-090: what a barter window's open changed: the coins and piles that moved from the substitute container onto the
/// merchant, the merchant's pile they joined (null when none moved) and how many items it newly marked identified.</summary>
public readonly record struct SessionBarterOpenResult(GameSessionDisposition Disposition, GameSessionFailure Failure, int Coins, int Piles,
    string? Pile, int Identified, GameSessionVersion Version);

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
    /// an NPC or a container (not spilled) its seed does not list (<see cref="GameSessionFailure.UnknownHolder"/>), a pile with an
    /// insert-item script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a sum past 32 bits or a full record, list or identified
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>), the revision, and the new pile's refusals of <see cref="GiveGoldToNpc"/>.
    /// One revision; nothing to move or mark is no change.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x572640, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionBarterOpenResult OpenBarter(GameSessionVersion expected, string npc, string? container, bool identify, SessionItemKind pileKind,
        SessionInsertCheck npcInsert)
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
        if (!seed.Holders.TryGetValue(npc, out var merchant) || merchant.Kind != SessionHolderKind.Npc) return Refuse(GameSessionFailure.UnknownHolder);
        SessionSeedHolder? box = null;
        if (container is not null && (!seed.Holders.TryGetValue(container, out box) || box.Kind != SessionHolderKind.Container || SpillCell(map, seed, container) is not null))
            return Refuse(GameSessionFailure.UnknownHolder);
        var stock = box is null ? [] : _inventory.Current(seed, box, entry => DroppedCell(map, seed, entry)).ToArray();
        var piles = stock.Where(entry => entry.Facts.Kind.Type == SessionItemType.Gold && (entry.Facts.Quantity ?? 0) != 0).ToArray();
        if (piles.Any(entry => entry.Facts.HasScript(SessionScriptPoint.InsertItem))) return Refuse(GameSessionFailure.ItemScriptUnsupported);
        var coins = piles.Sum(entry => (long)entry.Facts.Quantity!.Value);
        var target = piles.Length == 0 ? null : NpcPile(npc);
        if (target is { } grown && grown.Quantity + coins > int.MaxValue || target is null && coins > int.MaxValue) return Refuse(GameSessionFailure.BudgetExceeded);
        // The pile records after the move: the moved piles' go, the merchant's pile gets one unless it has one.
        var records = _inventory.Piles.Count - piles.Count(entry => _inventory.Piles.ContainsKey(entry.Entity)) +
            (piles.Length > 0 && (target is null || !_inventory.Piles.ContainsKey(target.Value.Entity)) ? 1 : 0);
        if (records > _limits.MaxGoldRecords) return Refuse(GameSessionFailure.BudgetExceeded);
        if (_durability.Count + piles.Count(entry => !_durability.Records.ContainsKey(entry.Entity)) > _limits.MaxDurabilityRecords) return Refuse(GameSessionFailure.BudgetExceeded);
        var moved = piles.Select(entry => entry.Entity).ToHashSet(StringComparer.Ordinal);
        var marked = !identify ? [] : _inventory.Current(seed, merchant, entry => DroppedCell(map, seed, entry)).Select(entry => entry.Entity)
            .Concat(stock.Select(entry => entry.Entity).Where(entity => !moved.Contains(entity)))
            .Where(entity => !_inventory.Identified.Contains(entity)).Distinct(StringComparer.Ordinal).ToList();
        // A new pile of the merchant's is identified too.
        if (_inventory.Identified.Count + marked.Count + (identify && piles.Length > 0 && target is null ? 1 : 0) > SessionInventoryState.MaxIdentified)
            return Refuse(GameSessionFailure.BudgetExceeded);
        if (piles.Length == 0 && marked.Count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, 0, null, 0, Version);
        if (_revision == long.MaxValue) return Refuse(GameSessionFailure.RevisionExhausted);
        string? pile = target?.Entity;
        if (piles.Length > 0 && target is null)
        {
            // 0x464830 creates the first pile with its coins and moves it in (0x4617F0); the later piles join it.
            var created = NewNpcEntry(npc, pileKind, InventoryDomain.MadeFacts(pileKind) with { Quantity = piles[0].Facts.Quantity }, npcInsert, out var refusal);
            if (created is null) return Refuse(refusal!.Value.Failure);
            pile = created.Value.Entity;
            if (identify) marked.Add(pile);
        }
        if (pile is not null) _inventory.Piles[pile] = (int)((target?.Quantity ?? 0) + coins);
        foreach (var entry in piles) DestroyObject(entry.Entity);
        _inventory.Identified.UnionWith(marked);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, (int)coins, piles.Length, pile, marked.Count, Version);
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
    /// </summary>
    public InventoryMutationResult BarterBuy(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionItemKind pileKind, SessionInsertCheck heroInsert, SessionInsertCheck npcInsert, ISessionItemScriptHost? scripts = null, string? container = null)
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
                if (NpcHasScript(npc, SessionScriptPoint.Transfer)) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
                if (_inventory.AddedTo(npc).Count + SeedItemCount(npc) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems)
                    return RejectItem(GameSessionFailure.BudgetExceeded);
            }
        }
        var moved = container is not null ? TakeFromContainer(container, item, stack, count, newStackKind, heroInsert, scripts)
            : stack ? TakeAmmoFromNpc(Version, npc, item, count, newStackKind, heroInsert) : TakeItemFromNpc(Version, npc, item, heroInsert, scripts);
        if (moved.Disposition != GameSessionDisposition.Changed || total == 0) return moved;
        var paid = GiveGoldToNpc(Version, npc, (int)total, pileKind, npcInsert);
        return paid.Disposition == GameSessionDisposition.Changed ? moved with { Version = Version } : paid with { Item = moved.Item, Version = Version };
    }

    /// <summary>
    /// The hero sells <paramref name="count"/> of its item to an NPC for <paramref name="unitPrice"/> each (the caller's quote, already
    /// limited to the NPC's coins, 0x575360): a whole item (count 1) through <see cref="GiveItemToNpc"/>, or units of an ammunition stack
    /// through <see cref="GiveAmmoToNpc"/> (a new NPC stack of <paramref name="newStackKind"/>); then the NPC pays <c>count × unitPrice</c>
    /// through <see cref="TakeGoldFromNpc"/>. Refused before any effect, beyond those calls' refusals: <see cref="BarterBuy"/>'s input
    /// checks, an NPC with fewer coins than the total (<see cref="GameSessionFailure.GoldShort"/>, the original's rolled-back stack sale),
    /// a pile with an insert-item script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), a hero sum past 32 bits or a full record
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>). ARC-WORLD-090: with a <paramref name="container"/> the item goes into the
    /// container's list (0x578EA0 moves a sold item into the shown object: <see cref="GiveItemToNpc"/>'s rules with the container as
    /// holder; units of ammunition join its first stack of the type, which the original's lookup never finds, AMA5); the coins still come
    /// from the NPC. With <paramref name="identify"/> (a merchant that is not a follower, BT5) a whole item that moved is marked identified
    /// in the move's revision; a full identified budget refuses before any effect.
    /// </summary>
    public InventoryMutationResult BarterSell(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionInsertCheck npcInsert, string? container = null, bool identify = false)
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
            if (pile.Facts?.HasScript(SessionScriptPoint.InsertItem) == true) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
            if (_inventory.Gold + total > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
            if (pile.Quantity == total ? !_durability.Records.ContainsKey(pile.Entity) && _durability.Count >= _limits.MaxDurabilityRecords
                : !_inventory.Piles.ContainsKey(pile.Entity) && _inventory.Piles.Count >= _limits.MaxGoldRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        }
        var into = container ?? npc;
        var kind = container is null ? SessionHolderKind.Npc : SessionHolderKind.Container;
        var moved = stack ? GiveAmmoTo(into, kind, item, count, newStackKind, npcInsert) : GiveItemTo(into, kind, item, npcInsert, identify);
        if (moved.Disposition != GameSessionDisposition.Changed || total == 0) return moved;
        var paid = TakeGoldFromNpc(Version, npc, (int)total);
        return paid.Disposition == GameSessionDisposition.Changed ? moved with { Version = Version } : paid with { Item = moved.Item, Version = Version };
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
            var hero = HeroStackOfType(source.AmmoType);
            return MoveUnits(source, count, hero, hero is null ? () => NewHeroStack(newStackKind, source.AmmoType, count, insertCheck) : null);
        }
        if (_inventory.Lists.IsContainer(container) && _inventory.AddedTo(container).FirstOrDefault(added => added.Entity == item) is { Entity: not null } added)
            return Take(seed, new(item, _inventory.AddedFacts(added)), container, insertCheck, scripts);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != container || _inventory.Taken.Contains(item) || DroppedCell(_actor.Map, seed, item) is not null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(seed, new(item, _inventory.WithAmmo(item, seed.Items[item].Facts)), container, insertCheck, scripts);
    }

    // ARC-WORLD-090: GiveAmmoToNpc's move into an NPC or a container (whose stack lookup follows AMA5).
    private InventoryMutationResult GiveAmmoTo(string holder, SessionHolderKind kind, string stack, int count, SessionItemKind newStackKind, SessionInsertCheck insertCheck)
    {
        if (kind == SessionHolderKind.Npc) return GiveAmmoToNpc(Version, holder, stack, count, newStackKind, insertCheck);
        if (newStackKind.Type != SessionItemType.Ammo) return RejectItem(GameSessionFailure.InvalidAmmo);
        if (!HeroHeld().Any(held => held.Entity == stack && held.Kind.Type == SessionItemType.Ammo)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var source = _inventory.Ammo[stack];
        if (source.Units < count) return RejectItem(GameSessionFailure.AmmoShort);
        var into = ContainerStackOfType(holder, source.AmmoType);
        return MoveUnits(source, count, into, into is null ? () => NewNpcStack(holder, newStackKind, source.AmmoType, count, insertCheck, kind) : null);
    }
}
