using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Items.TradeRules;

namespace Arcanum.Simulation.Tests.Items;

// ARC-UI-050: the barter rules of ledger BT1–BT9. Values are invented.
[TestClass]
public sealed class TradeRulesTests
{
    private static readonly TradeItem Sword = new(Worth: 400, StoredWorth: 400, Flags: 0, Condition: new(40, 0, 0, false));
    private static readonly TradeParties Merchant = new(Multiplier: 100, Haggle: 30, HaggleTraining: SkillTraining.None, Reaction: 50);

    [TestMethod]
    public void A_sale_pays_the_haggle_share_of_the_worth_and_a_multiplier_above_100_lowers_it_first()
    {
        // trunc(30 / 2) + 50 = 65 %.
        Assert.AreEqual(new TradeQuote(260, TradeRefusal.None), Price(TradeDirection.PcSells, Sword, Merchant));
        // m = 200: 100 + trunc(3 × −100 / 8) = 63 %, then 65 %: 400 → 252 → 163.
        Assert.AreEqual(163, Price(TradeDirection.PcSells, Sword, Merchant with { Multiplier = 200 }).Price);
        Assert.AreEqual(260, Price(TradeDirection.PcSells, Sword, Merchant with { Multiplier = 50 }).Price, "m ≤ 100 changes nothing");
        // m ≥ 370 turns the factor negative: the price floors at 0 with no refusal.
        Assert.AreEqual(new TradeQuote(0, TradeRefusal.None), Price(TradeDirection.PcSells, Sword, Merchant with { Multiplier = 400 }));
        // A 95 haggle: trunc(95 / 2) + 50 = 97 %.
        Assert.AreEqual(388, Price(TradeDirection.PcSells, Sword, Merchant with { Haggle = 95 }).Price);
    }

    [TestMethod]
    public void Damage_scales_only_a_sale_and_its_bounds_make_1_into_2()
    {
        var worn = Sword with { Condition = new(40, 0, 10, false) };
        Assert.AreEqual(195, Price(TradeDirection.PcSells, worn, Merchant).Price, "30 / 40 of 260");
        Assert.AreEqual(Price(TradeDirection.PcBuys, Sword, Merchant), Price(TradeDirection.PcBuys, worn, Merchant), "buying ignores damage");
        var cheap = new TradeItem(2, 2, 0, new(10, 0, 0, false));
        Assert.AreEqual(2, Price(TradeDirection.PcSells, cheap, Merchant with { Haggle = 0 }).Price, "2 × 50 % = 1 becomes 2");
    }

    [TestMethod]
    public void A_buy_adds_the_merchants_markup_less_haggle_then_scales_by_reaction_and_costs_at_least_worth_plus_one()
    {
        // 400 + (100 − 30) × 100 × 400 / 10000 = 680, at reaction 50 × 100 %.
        Assert.AreEqual(new TradeQuote(680, TradeRefusal.None), Price(TradeDirection.PcBuys, Sword, Merchant));
        Assert.AreEqual(1360, Price(TradeDirection.PcBuys, Sword, Merchant with { Reaction = -5 }).Price, "below 0: × 200 %");
        Assert.AreEqual(1088, Price(TradeDirection.PcBuys, Sword, Merchant with { Reaction = 20 }).Price, "200 − 40 = 160 %");
        Assert.AreEqual(544, Price(TradeDirection.PcBuys, Sword, Merchant with { Reaction = 100 }).Price, "120 − 40 = 80 %");
        Assert.AreEqual(544, Price(TradeDirection.PcBuys, Sword, Merchant with { Reaction = 250 }).Price, "above 100 counts as 100");
        Assert.AreEqual(401, Price(TradeDirection.PcBuys, Sword, Merchant with { Multiplier = 0, Reaction = 100 }).Price, "at least worth + 1");
    }

    [TestMethod]
    public void The_buy_markup_multiplies_in_32_bits()
    {
        var dear = Sword with { Worth = 200_000, StoredWorth = 200_000 };
        // 100 × 200 × 200000 = 4e9 wraps to −294967296; / 10000 = −29496; then the floor worth + 1.
        Assert.AreEqual(200_001, Price(TradeDirection.PcBuys, dear, Merchant with { Haggle = 0, Multiplier = 200 }).Price);
    }

    [TestMethod]
    public void Coins_have_no_barter_price()
    {
        Assert.AreEqual(new TradeQuote(0, TradeRefusal.Gold), Price(TradeDirection.PcSells, Sword with { Gold = true }, Merchant));
        Assert.AreEqual(new TradeQuote(0, TradeRefusal.Gold), Price(TradeDirection.PcBuys, Sword with { Gold = true }, Merchant));
    }

    [TestMethod]
    public void The_sale_check_runs_worth_broken_script_list_then_stolen_and_expert_haggle_skips_script_and_list()
    {
        Assert.AreEqual(TradeRefusal.NoWorth, SaleCheck(Sword with { StoredWorth = 0, Worth = 300 }, Merchant), "the raw field");
        Assert.AreEqual(TradeRefusal.Broken, SaleCheck(Sword with { Condition = new(40, 0, 0, true) }, Merchant with { Deals = false }));
        Assert.AreEqual(TradeRefusal.BuyScript, SaleCheck(Sword, Merchant with { BuyScriptAccepts = false, Deals = false }));
        Assert.AreEqual(TradeRefusal.NotDealt, SaleCheck(Sword, Merchant with { Deals = false }));
        var expert = Merchant with { HaggleTraining = SkillTraining.Expert, BuyScriptAccepts = false, Deals = false };
        Assert.AreEqual(TradeRefusal.None, SaleCheck(Sword, expert));
        var stolen = Sword with { Flags = StolenFlag };
        Assert.AreEqual(TradeRefusal.Stolen, SaleCheck(stolen, expert with { HaggleTraining = SkillTraining.Master }), "at every training");
        Assert.AreEqual(TradeRefusal.None, SaleCheck(stolen, Merchant with { Fence = true }));
        Assert.AreEqual(new TradeQuote(0, TradeRefusal.NotDealt), Price(TradeDirection.PcSells, Sword, Merchant with { Deals = false }));
    }

    [TestMethod]
    public void The_buy_check_refuses_wont_sell_then_worn_and_a_master_buys_anything()
    {
        Assert.AreEqual(TradeRefusal.WontSell, BuyCheck(Sword with { Flags = WontSellFlag, Worn = true }, Merchant));
        Assert.AreEqual(TradeRefusal.Worn, BuyCheck(Sword with { Worn = true }, Merchant with { HaggleTraining = SkillTraining.Expert }));
        Assert.AreEqual(TradeRefusal.None, BuyCheck(Sword with { Flags = WontSellFlag, Worn = true }, Merchant with { HaggleTraining = SkillTraining.Master }));
        Assert.AreEqual(new TradeQuote(0, TradeRefusal.WontSell), Price(TradeDirection.PcBuys, Sword with { Flags = WontSellFlag }, Merchant));
    }

    [TestMethod]
    public void The_reaction_scale_matches_its_three_bands()
    {
        Assert.AreEqual((200, 200, 100, 80, 102), (ReactionScale(-1, 100), ReactionScale(0, 100), ReactionScale(50, 100), ReactionScale(100, 100), ReactionScale(49, 100)));
        Assert.AreEqual(98, ReactionScale(55, 100), "120 − trunc(110 / 5) = 98");
    }

    [TestMethod]
    public void A_generous_trade_moves_only_a_loving_merchant_and_a_loss_always_lowers()
    {
        Assert.AreEqual(0, GiftReaction(50, ReactionCategory.Love));
        Assert.AreEqual(0, GiftReaction(500, ReactionCategory.Amiable));
        Assert.AreEqual((1, 4, 5, 12, 15), (GiftReaction(51, ReactionCategory.Love), GiftReaction(199, ReactionCategory.Love),
            GiftReaction(200, ReactionCategory.Love), GiftReaction(999, ReactionCategory.Love), GiftReaction(1000, ReactionCategory.Love)));
        Assert.AreEqual((-2, -2, -5, -30), (GiftReaction(-1, ReactionCategory.Hatred), GiftReaction(-99, ReactionCategory.Love),
            GiftReaction(-550, ReactionCategory.Neutral), GiftReaction(-9000, ReactionCategory.Neutral)));
    }

    [TestMethod]
    public void A_sale_pays_at_most_the_merchants_coins_and_the_gift_amount_is_the_merchants_surplus()
    {
        Assert.AreEqual((260, 100, 0), (SalePayment(260, 1000), SalePayment(260, 100), SalePayment(260, 0)));
        Assert.AreEqual(280, GiftAmount(TradeDirection.PcBuys, 400, 680, 1));
        Assert.IsNull(GiftAmount(TradeDirection.PcBuys, 400, 300, 1), "a buy below the worth reports nothing");
        Assert.AreEqual(140, GiftAmount(TradeDirection.PcSells, 400, 260, 1));
        Assert.AreEqual(30, GiftAmount(TradeDirection.PcSells, 10, 7, 10), "a stack counts every unit");
        Assert.IsNull(GiftAmount(TradeDirection.PcSells, 400, 0, 1), "a price of 0 pays nothing and changes no reaction");
    }
}
