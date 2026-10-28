using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-UI-050: the barter window's trades between the hero and an NPC merchant, in the original's order (0x578EA0, ledger BT5): the item
/// (or ammunition units) moves first, then the coins, only when the move succeeded. The price is the caller's (the barter rules of
/// <see cref="Arcanum.Simulation.Items.TradeRules"/>); a price of 0 moves the item for nothing, as a follower's window does. The reaction
/// change of a generous trade is the caller's next command. Contract: docs/world/barter-runtime.md.
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
    /// The hero buys <paramref name="count"/> of an NPC's item for <paramref name="unitPrice"/> each: a whole item (count 1) through
    /// <see cref="TakeItemFromNpc"/>, or units of an ammunition stack through <see cref="TakeAmmoFromNpc"/> (a new hero stack of
    /// <paramref name="newStackKind"/>); then the hero pays <c>count × unitPrice</c> to the NPC through <see cref="GiveGoldToNpc"/> (a new
    /// pile of <paramref name="pileKind"/> after <paramref name="npcInsert"/>). Refused before any effect, beyond those calls' refusals: a
    /// negative price or a count below 1 or above 1 for an item (<see cref="GameSessionFailure.InvalidItem"/>), a total past 32 bits
    /// (<see cref="GameSessionFailure.BudgetExceeded"/>), a hero with fewer coins than the total (<see cref="GameSessionFailure.GoldShort"/>,
    /// the original's "not enough money": its unit test before the move, its rolled-back stack payment after), and the payment's own
    /// refusals that do not depend on the move (an NPC pile past 32 bits, a full pile budget, a transfer script on an NPC without a pile).
    /// A payment refused after the move (the host's insert check for a new pile) leaves the item with the hero and reports the payment's
    /// failure with <see cref="InventoryMutationResult.Item"/> set, as the original leaves a moved item that its payment failed.
    /// </summary>
    public InventoryMutationResult BarterBuy(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionItemKind pileKind, SessionInsertCheck heroInsert, SessionInsertCheck npcInsert, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(heroInsert);
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item) ||
            unitPrice < 0 || count < 1 || pileKind.Type != SessionItemType.Gold) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
        var stack = NpcStacks(npc).Any(pair => pair.Stack.Entity == item);
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
            }
        }
        var moved = stack ? TakeAmmoFromNpc(Version, npc, item, count, newStackKind, heroInsert) : TakeItemFromNpc(Version, npc, item, heroInsert, scripts);
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
    /// budget (<see cref="GameSessionFailure.BudgetExceeded"/>).
    /// </summary>
    public InventoryMutationResult BarterSell(GameSessionVersion expected, string npc, string item, int count, int unitPrice, SessionItemKind newStackKind,
        SessionInsertCheck npcInsert)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(npcInsert);
        if (!SessionValidation.Entity(npc) || npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) || !SessionValidation.Entity(item) ||
            unitPrice < 0 || count < 1) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!KnownNpc(npc)) return RejectItem(GameSessionFailure.UnknownHolder);
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
        var moved = stack ? GiveAmmoToNpc(Version, npc, item, count, newStackKind, npcInsert) : GiveItemToNpc(Version, npc, item, npcInsert);
        if (moved.Disposition != GameSessionDisposition.Changed || total == 0) return moved;
        var paid = TakeGoldFromNpc(Version, npc, (int)total);
        return paid.Disposition == GameSessionDisposition.Changed ? moved with { Version = Version } : paid with { Item = moved.Item, Version = Version };
    }
}
