using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

// ARC-UI-1740: the gate 0x579840, the price limit 0x4C6520 and the NPC line ranges 0x414020 (ledger BT6, BW3, SK5). Values are invented except the table's.
[TestClass]
public sealed class GamblingRulesTests
{
    private static readonly GambleFacts Open = new(MerchantsItem: true, MerchantPoints: 3, Price: 100, HeroLevel: 4, HeroTraining: SkillTraining.None, HeroGold: 500,
        Worn: false, WontSell: false);

    [TestMethod]
    public void The_limit_is_the_tables_value_of_the_level_doubled_from_apprentice()
    {
        Assert.AreEqual(0, GamblingRules.PriceLimit(0, SkillTraining.None));
        Assert.AreEqual(100, GamblingRules.PriceLimit(4, SkillTraining.None));
        Assert.AreEqual(200, GamblingRules.PriceLimit(4, SkillTraining.Apprentice));
        Assert.AreEqual(5000, GamblingRules.PriceLimit(20, SkillTraining.None));
        Assert.AreEqual(10000, GamblingRules.PriceLimit(20, SkillTraining.Master));
        Assert.AreEqual(2000, GamblingRules.PriceLimit(17, SkillTraining.None), "level 17 is the 18th entry");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GamblingRules.PriceLimit(21, SkillTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GamblingRules.PriceLimit(-1, SkillTraining.None));
    }

    [TestMethod]
    public void The_gate_refuses_in_the_originals_order()
    {
        Assert.IsTrue(GamblingRules.Gate(Open).Allowed);
        Assert.AreEqual(GambleRefusal.HeroSide, GamblingRules.Gate(Open with { MerchantsItem = false, MerchantPoints = 0, Price = 99999 }).Refusal, "the side comes first");
        Assert.AreEqual(GambleRefusal.NoGambler, GamblingRules.Gate(Open with { MerchantPoints = 0, Price = 99999 }).Refusal);
        Assert.AreEqual(GambleRefusal.TooValuable, GamblingRules.Gate(Open with { Price = 101, HeroGold = 0 }).Refusal, "the limit is tested before the coins");
        Assert.IsTrue(GamblingRules.Gate(Open with { Price = 100 }).Allowed, "the limit itself is allowed");
        Assert.AreEqual(GambleRefusal.TooPoor, GamblingRules.Gate(Open with { HeroGold = 99 }).Refusal);
        Assert.IsTrue(GamblingRules.Gate(Open with { HeroGold = 100 }).Allowed, "exactly the price in coins is enough");
    }

    [TestMethod]
    public void A_price_of_zero_passes_the_limit_and_the_coins()
    {
        Assert.IsTrue(GamblingRules.Gate(Open with { Price = 0, HeroGold = 0, HeroLevel = 0 }).Allowed);
    }

    [TestMethod]
    public void A_worn_item_needs_expert_and_a_wont_sell_item_master_of_the_heros_own_training()
    {
        var worn = Open with { Worn = true };
        Assert.AreEqual(GambleRefusal.NeedsExpert, GamblingRules.Gate(worn).Refusal);
        Assert.AreEqual(GambleRefusal.NeedsExpert, GamblingRules.Gate(worn with { HeroTraining = SkillTraining.Apprentice }).Refusal);
        var expert = GamblingRules.Gate(worn with { HeroTraining = SkillTraining.Expert });
        Assert.IsTrue(expert.Allowed && expert.Warned, "an allowed worn item warns");
        var wont = Open with { WontSell = true };
        Assert.AreEqual(GambleRefusal.NeedsMaster, GamblingRules.Gate(wont with { HeroTraining = SkillTraining.Expert }).Refusal);
        Assert.IsTrue(GamblingRules.Gate(wont with { HeroTraining = SkillTraining.Master }).Warned);
        Assert.AreEqual(GambleRefusal.NeedsMaster, GamblingRules.Gate(worn with { WontSell = true, HeroTraining = SkillTraining.Expert }).Refusal, "worn passes, then won't sell asks Master");
        Assert.IsFalse(GamblingRules.Gate(Open).Warned);
    }

    [TestMethod]
    public void The_line_categories_and_ranges_are_the_gates()
    {
        Assert.AreEqual(2, GamblingRules.LineCategory(GambleRefusal.HeroSide));
        Assert.AreEqual(9, GamblingRules.LineCategory(GambleRefusal.NoGambler));
        Assert.AreEqual(3, GamblingRules.LineCategory(GambleRefusal.TooValuable));
        Assert.AreEqual(4, GamblingRules.LineCategory(GambleRefusal.TooPoor));
        Assert.AreEqual(5, GamblingRules.LineCategory(GambleRefusal.NeedsExpert));
        Assert.AreEqual(6, GamblingRules.LineCategory(GambleRefusal.NeedsMaster));
        Assert.AreEqual(-1, GamblingRules.LineCategory(GambleRefusal.None));
        Assert.AreEqual((3100, 3199), GamblingRules.LineRange(0));
        Assert.AreEqual((3300, 3399), GamblingRules.LineRange(2));
        Assert.AreEqual((3800, 3899), GamblingRules.LineRange(7));
        Assert.AreEqual((3900, 3999), GamblingRules.LineRange(8));
        Assert.AreEqual((5600, 5699), GamblingRules.LineRange(9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GamblingRules.LineRange(10));
    }

    [TestMethod]
    public void A_line_takes_the_price_in_its_first_percent_d()
    {
        Assert.AreEqual("Put up 250 then.", GamblingRules.WithPrice("Put up %d then.", 250));
        Assert.AreEqual("No price here.", GamblingRules.WithPrice("No price here.", 250));
        Assert.AreEqual("1 and %d", GamblingRules.WithPrice("%d and %d", 1));
    }

    [TestMethod]
    public void A_won_bet_costs_the_merchant_at_least_two_reaction_steps_and_at_most_thirty()
    {
        Assert.AreEqual(-2, GamblingRules.WonReaction(50, 1, ReactionCategory.Neutral), "50 / 100 truncates to 0: −2");
        Assert.AreEqual(-5, GamblingRules.WonReaction(500, 1, ReactionCategory.Neutral));
        Assert.AreEqual(-30, GamblingRules.WonReaction(100000, 1, ReactionCategory.Love));
        Assert.AreEqual(-10, GamblingRules.WonReaction(500, 2, ReactionCategory.Neutral), "the worth of all units");
    }
}
