using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Items;

/// <summary>Which way an item goes in a barter: the PC sells it to the merchant, or buys it from the merchant.</summary>
public enum TradeDirection { PcSells, PcBuys }

/// <summary>Why a barter quote is 0 (the original's price 0, which cancels the drop outside a follower's window).</summary>
public enum TradeRefusal
{
    None,
    /// <summary>Coins never get a barter price.</summary>
    Gold,
    /// <summary>Sale check: the item's stored worth is 0 (the raw field, not the unidentified 300).</summary>
    NoWorth,
    /// <summary>Sale check: the item is broken (the destroyed bit of its current art).</summary>
    Broken,
    /// <summary>Sale check below Expert haggle: the merchant's buy-object script (point 17) answered "no".</summary>
    BuyScript,
    /// <summary>Sale check below Expert haggle: the merchant's buy list does not hold the item.</summary>
    NotDealt,
    /// <summary>Sale check: a stolen item (item flag 0x8000) and a merchant that is not a fence (NPC flag 0x4000).</summary>
    Stolen,
    /// <summary>Buy check below Master haggle: the item has the "won't sell" flag (0x2).</summary>
    WontSell,
    /// <summary>Buy check below Master haggle: the merchant wears the item.</summary>
    Worn,
}

/// <summary>
/// The item side of a quote. <see cref="Worth"/> is the item worth (0x461F20: 300 unidentified, else the stored worth, at least 2,
/// <see cref="Equipment.EquipmentChoice.Worth"/>), <see cref="StoredWorth"/> the raw field the sale check reads, <see cref="Flags"/> the
/// item flags (field 0x57), <see cref="Condition"/> its hit points and broken bit, <see cref="Worn"/> whether its holder wears it.
/// </summary>
public sealed record TradeItem(int Worth, int StoredWorth, uint Flags, ItemCondition Condition, bool Worn = false, bool Gold = false);

/// <summary>
/// The parties of a quote. <see cref="Multiplier"/> is the merchant's retail price multiplier (NPC field 0x125);
/// <see cref="Haggle"/> the PC's haggle effectiveness (<see cref="SkillCheckRules.Effectiveness"/> of skill 9) and
/// <see cref="HaggleTraining"/> its training; <see cref="Reaction"/> the merchant's standard effective reaction to the PC (0x4C0CC0),
/// read only when the PC buys. For a sale below Expert haggle, <see cref="BuyScriptAccepts"/> is the merchant's buy-object script
/// answer (true without a script) and <see cref="Deals"/> its buy list's verdict on the item; <see cref="Fence"/> is NPC flag 0x4000.
/// </summary>
public sealed record TradeParties(int Multiplier, int Haggle, SkillTraining HaggleTraining, int Reaction = 50, bool BuyScriptAccepts = true,
    bool Deals = true, bool Fence = false);

/// <summary>A quote: the unit price, 0 with the refusal that made it so (a positive price has <see cref="TradeRefusal.None"/>).</summary>
public readonly record struct TradeQuote(int Price, TradeRefusal Refusal);

/// <summary>
/// ARC-UI-050: the barter rules of the original's item price (0x461F80), sale check (0x462170), buy check (0x462230), the reaction
/// price scale (0x4C1150) and the reaction to a generous trade (0x4C11D0), read in the executable by two readers (ledger BT1–BT10). All
/// arithmetic is the original's signed 32 bits: products wrap and divisions truncate toward zero. Contract: docs/world/barter-runtime.md.
/// </summary>
public static class TradeRules
{
    /// <summary>Item flag 0x2: the merchant will not sell the item (below Master haggle).</summary>
    public const uint WontSellFlag = 0x2;
    /// <summary>Item flag 0x8000: the item is stolen; only a fence buys it.</summary>
    public const uint StolenFlag = 0x8000;
    /// <summary>NPC flag 0x4000: the merchant is a fence.</summary>
    public const uint FenceFlag = 0x4000;

    /// <summary>
    /// The sale check (0x462170), in the original's order: a stored worth of 0, a broken item; below Expert haggle the merchant's
    /// buy-object script, then its buy list (both skipped from Expert); a stolen item to a merchant that is not a fence (at every
    /// training). Worn, no-drop and quest items are not tested here (the move refuses a no-drop item later).
    /// </summary>
    [OriginalFunction(0x462170, OriginalFunctionScope.Full)]
    public static TradeRefusal SaleCheck(TradeItem item, TradeParties parties)
    {
        Check(item, parties);
        if (item.StoredWorth == 0) return TradeRefusal.NoWorth;
        if (item.Condition.Broken) return TradeRefusal.Broken;
        if (parties.HaggleTraining < SkillTraining.Expert)
        {
            if (!parties.BuyScriptAccepts) return TradeRefusal.BuyScript;
            if (!parties.Deals) return TradeRefusal.NotDealt;
        }
        if ((item.Flags & StolenFlag) != 0 && !parties.Fence) return TradeRefusal.Stolen;
        return TradeRefusal.None;
    }

    /// <summary>The buy check (0x462230): a Master of haggle buys anything; else a "won't sell" item, then a worn one, is refused.</summary>
    [OriginalFunction(0x462230, OriginalFunctionScope.Full)]
    public static TradeRefusal BuyCheck(TradeItem item, TradeParties parties)
    {
        Check(item, parties);
        if (parties.HaggleTraining == SkillTraining.Master) return TradeRefusal.None;
        if ((item.Flags & WontSellFlag) != 0) return TradeRefusal.WontSell;
        return item.Worn ? TradeRefusal.Worn : TradeRefusal.None;
    }

    /// <summary>
    /// The barter price of one unit (0x461F80 with checks on). Coins are 0. The PC sells: the sale check, then from the worth W, when
    /// the merchant's multiplier m is above 100, <c>W × (100 + trunc(3 × (100 − m) / 8)) / 100</c>; the PC's haggle share
    /// <c>× (trunc(H / 2) + 50) / 100</c>; a damaged item <c>× current / maximum</c>; a negative price is 0 and 1 is 2. The PC buys: the
    /// buy check, then <c>W + (100 − H) × m × W / 10000</c> (the triple product in 32 bits), the reaction scale (<see cref="ReactionScale"/>),
    /// at least <c>W + 1</c>, and 1 or 2 become 3 (dead for every worth the original produces, which is at least 2). No damage scaling when buying.
    /// </summary>
    [OriginalFunction(0x461F80, OriginalFunctionScope.Partial)]
    public static TradeQuote Price(TradeDirection direction, TradeItem item, TradeParties parties)
    {
        Check(item, parties);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (item.Gold) return new(0, TradeRefusal.Gold);
        var worth = item.Worth;
        unchecked
        {
            if (direction == TradeDirection.PcSells)
            {
                if (SaleCheck(item, parties) is var sale and not TradeRefusal.None) return new(0, sale);
                var price = worth;
                if (parties.Multiplier > 100) price = price * (100 + 3 * (100 - parties.Multiplier) / 8) / 100;
                price = price * (parties.Haggle / 2 + 50) / 100;
                var maximum = ItemDurabilityRules.MaximumHitPoints(item.Condition);
                var current = ItemDurabilityRules.HitPoints(item.Condition);
                // The original divides by the maximum unguarded; a current below a maximum of 0 is negative, which data never holds.
                if (current < maximum && maximum != 0) price = current * price / maximum;
                return Bounded(price < 0 ? 0 : price == 1 ? 2 : price);
            }
            if (BuyCheck(item, parties) is var buy and not TradeRefusal.None) return new(0, buy);
            var cost = worth + (100 - parties.Haggle) * parties.Multiplier * worth / 10000;
            cost = ReactionScale(parties.Reaction, cost);
            if (cost <= worth) cost = worth + 1;
            if (cost is 1 or 2) cost = 3;
            return Bounded(cost);
        }
    }

    /// <summary>
    /// The reaction scale of a buy price (0x4C1150): a reaction above 100 counts as 100; the factor is 200 below 0, <c>200 − 2r</c>
    /// below 50, else <c>120 − trunc(2r / 5)</c> (100 at 50, 80 at 100); the result is <c>price × factor / 100</c> in 32 bits.
    /// </summary>
    [OriginalFunction(0x4C1150, OriginalFunctionScope.Partial)]
    public static int ReactionScale(int reaction, int price)
    {
        if (reaction > 100) reaction = 100;
        var factor = reaction < 0 ? 200 : reaction < 50 ? 200 - 2 * reaction : 120 - 2 * reaction / 5;
        return unchecked(price * factor / 100);
    }

    /// <summary>
    /// The reaction change after a trade (0x4C11D0) for the merchant's surplus <paramref name="amount"/>: what the PC paid above the
    /// worth on a buy, or the worth above what it got on a sale; a negative amount (the lost value of a won gamble) gives
    /// <c>trunc(amount / 100)</c>, −2 when that is 0, at least −30. From 0 to 50 nothing. Above 50 only a merchant who loves the PC
    /// (<see cref="ReactionCategory.Love"/>) gains <c>amount / 40</c> below 200, <c>5 + (amount − 200) / 100</c> below 1000, else 15.
    /// The change goes through <see cref="ReactionRules.Adjust"/> (0x4C0DE0).
    /// </summary>
    [OriginalFunction(0x4C11D0, OriginalFunctionScope.Partial)]
    public static int GiftReaction(int amount, ReactionCategory category)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (amount < 0)
        {
            var loss = amount / 100;
            return loss == 0 ? -2 : Math.Max(-30, loss);
        }
        if (amount <= 50 || category != ReactionCategory.Love) return 0;
        return amount < 200 ? amount / 40 : amount < 1000 ? 5 + (amount - 200) / 100 : 15;
    }

    /// <summary>
    /// What the merchant pays for a sale (0x575360): the quoted unit price, at most the merchant's own coins (a merchant without coins
    /// takes the item for nothing).
    /// </summary>
    [OriginalFunction(0x575360, OriginalFunctionScope.Partial)]
    public static int SalePayment(int price, int merchantGold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegative(merchantGold);
        return Math.Min(price, merchantGold);
    }

    /// <summary>
    /// The merchant's surplus a trade reports to <see cref="GiftReaction"/> (0x578EA0), or null when there is no call: a price of 0
    /// pays nothing and changes no reaction. A buy reports <c>total − worth × count</c> when the total reaches the worth, a sale
    /// <c>worth × count − total</c> when the worth reaches the total (both in 32 bits).
    /// </summary>
    [OriginalFunction(0x578EA0, OriginalFunctionScope.Partial)]
    public static int? GiftAmount(TradeDirection direction, int worth, int unitPrice, int count)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (unitPrice <= 0) return null;
        unchecked
        {
            var total = unitPrice * count;
            var value = worth * count;
            return direction == TradeDirection.PcBuys ? total >= value ? total - value : null : value >= total ? value - total : null;
        }
    }

    private static TradeQuote Bounded(int price) => new(price, TradeRefusal.None);

    private static void Check(TradeItem item, TradeParties parties)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(parties);
        if (!Enum.IsDefined(parties.HaggleTraining)) throw new ArgumentOutOfRangeException(nameof(parties));
    }
}
