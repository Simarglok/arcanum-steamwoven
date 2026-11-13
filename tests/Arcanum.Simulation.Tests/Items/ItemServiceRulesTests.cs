using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

// ARC-UI-1731 (ledger IS1–IS6): the identify and repair service rules of the item windows opened by the dialog replies ii and ri.
[TestClass]
public sealed class ItemServiceRulesTests
{
    [TestMethod]
    public void Identify_costs_a_hundred_and_an_identified_item_is_refused()
    {
        Assert.AreEqual(new ItemServiceQuote(ItemServiceStatus.Quoted, 100, 5800, 5899), ItemServiceRules.Identify(identified: false));
        Assert.AreEqual(new ItemServiceQuote(ItemServiceStatus.AlreadyIdentified, 0, 5700, 5799), ItemServiceRules.Identify(identified: true));
    }

    [TestMethod]
    public void An_item_is_identified_without_complexity_or_with_the_flag_and_only_types_5_to_14_count()
    {
        Assert.IsTrue(ItemServiceRules.IsIdentified(5, 0, false));
        Assert.IsTrue(ItemServiceRules.IsIdentified(5, -3, false), "a technological item has a negative complexity");
        Assert.IsTrue(ItemServiceRules.IsIdentified(14, 30, true));
        Assert.IsFalse(ItemServiceRules.IsIdentified(5, 30, false));
        Assert.IsFalse(ItemServiceRules.IsIdentified(4, 0, true), "not an item type");
    }

    [TestMethod]
    public void The_repair_quote_carries_the_line_range_of_its_status()
    {
        var damaged = new ItemCondition(40, 10, 25, false);
        var quoted = ItemServiceRules.Repair(ItemDurabilityRules.RepairQuote(damaged, 200, RepairTraining.Apprentice, ItemRuleProfile.Steamwoven));
        // Steamwoven: the undamaged price 200 halved, × damage 25 / maximum 50 = 50.
        Assert.AreEqual(new ItemServiceQuote(ItemServiceStatus.Quoted, 50, 6100, 6199), quoted);
        Assert.AreEqual(new ItemServiceQuote(ItemServiceStatus.NothingToRepair, 0, 6000, 6099),
            ItemServiceRules.Repair(ItemDurabilityRules.RepairQuote(damaged with { Damage = 0 }, 200, RepairTraining.Master, ItemRuleProfile.Steamwoven)));
        var broken = new ItemCondition(40, 10, 0, true, true);
        Assert.AreEqual(new ItemServiceQuote(ItemServiceStatus.NeedsMaster, 0, 5900, 5999),
            ItemServiceRules.Repair(ItemDurabilityRules.RepairQuote(broken, 200, RepairTraining.Expert, ItemRuleProfile.Steamwoven)));
    }

    [TestMethod]
    public void The_kept_price_follows_the_stale_price_setting()
    {
        var quoted = ItemServiceRules.Identify(identified: false);
        var refused = ItemServiceRules.Identify(identified: true);
        Assert.AreEqual(100, ItemServiceRules.KeptPrice(ItemServiceRules.NoPrice, quoted, ItemRuleProfile.Steamwoven));
        Assert.AreEqual(ItemServiceRules.NoPrice, ItemServiceRules.KeptPrice(100, refused, ItemRuleProfile.Steamwoven), "a refused item clears the price");
        Assert.AreEqual(100, ItemServiceRules.KeptPrice(100, refused, ItemRuleProfile.Original), "the retail window keeps the last quoted price (IS5)");
        Assert.AreEqual(ItemServiceRules.NoPrice, ItemServiceRules.KeptPrice(ItemServiceRules.NoPrice, refused, ItemRuleProfile.Original), "after an open nothing is kept");
        Assert.IsTrue(ItemRuleProfile.Original.ServiceKeepsStalePrice);
        Assert.IsFalse(ItemRuleProfile.Steamwoven.ServiceKeepsStalePrice);
    }

    [TestMethod]
    public void The_unchecked_sale_price_skips_the_sale_check_and_scales_by_multiplier_and_haggle()
    {
        var item = new TradeItem(100, 0, 0, new ItemCondition(40, 10, 0, true), false);
        // No stored worth and a broken item would refuse a sale; the repair quote's price ignores it: 100 × (10 / 2 + 50) / 100.
        Assert.AreEqual(55, TradeRules.UncheckedSalePrice(item, new TradeParties(100, 10, SkillTraining.None)));
        Assert.AreEqual(TradeRefusal.NoWorth, TradeRules.Price(TradeDirection.PcSells, item, new TradeParties(100, 10, SkillTraining.None)).Refusal);
        // A multiplier of 60 (below 100) changes nothing; one above 100 lowers it: 100 × (100 + 3 × (100 − 140) / 8) / 100 = 85.
        Assert.AreEqual(55, TradeRules.UncheckedSalePrice(item, new TradeParties(60, 10, SkillTraining.None)));
        Assert.AreEqual(46, TradeRules.UncheckedSalePrice(item, new TradeParties(140, 10, SkillTraining.None)));
        Assert.AreEqual(0, TradeRules.UncheckedSalePrice(item with { Gold = true }, new TradeParties(100, 10, SkillTraining.None)));
    }
}
