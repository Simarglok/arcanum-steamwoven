using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

// ARC-WORLD-046: the follower order rules (ARC-RES-064 OL1–OL16). Names are invented.
[TestClass]
public sealed class CompanionOrderRulesTests
{
    private static readonly CompanionOrderRuleProfile Original = CompanionOrderRuleProfile.Original, Steamwoven = CompanionOrderRuleProfile.Steamwoven;

    [TestMethod]
    public void The_presets_are_walk_attack_close_spread_back_off_and_an_inert_f6()
    {
        CompanionOrderType[] types = [CompanionOrderType.WalkHere, CompanionOrderType.Attack, CompanionOrderType.StayClose, CompanionOrderType.SpreadOut, CompanionOrderType.BackOff];
        for (var i = 0; i < types.Length; i++)
        {
            Assert.AreEqual(new CompanionOrderPreset(types[i], true), CompanionOrderRules.Preset(i, Original));
            Assert.AreEqual(new CompanionOrderPreset(types[i], true), CompanionOrderRules.Preset(i, Steamwoven));
        }
        Assert.AreEqual(new CompanionOrderPreset(CompanionOrderType.None, false), CompanionOrderRules.Preset(5, Original), "OL14: F6 names nobody");
        Assert.AreEqual(new CompanionOrderPreset(CompanionOrderType.Follow, true), CompanionOrderRules.Preset(5, Steamwoven), "D2: follow to all");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionOrderRules.Preset(6, Original));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionOrderRules.Preset(-1, Original));
    }

    [TestMethod]
    public void The_portrait_menu_sends_its_orders_and_no_wait_to_a_mind_controlled_follower()
    {
        Assert.AreEqual(CompanionOrderType.WalkHere, CompanionOrderRules.PortraitOrder(FollowerMenuItem.Walk, false));
        Assert.AreEqual(CompanionOrderType.Attack, CompanionOrderRules.PortraitOrder(FollowerMenuItem.Attack, false));
        Assert.AreEqual(CompanionOrderType.StayClose, CompanionOrderRules.PortraitOrder(FollowerMenuItem.StayClose, false));
        Assert.AreEqual(CompanionOrderType.SpreadOut, CompanionOrderRules.PortraitOrder(FollowerMenuItem.SpreadOut, false));
        Assert.AreEqual(CompanionOrderType.BackOff, CompanionOrderRules.PortraitOrder(FollowerMenuItem.BackOff, false));
        Assert.AreEqual(CompanionOrderType.Wait, CompanionOrderRules.PortraitOrder(FollowerMenuItem.Wait, false));
        Assert.IsNull(CompanionOrderRules.PortraitOrder(FollowerMenuItem.Wait, true));
        Assert.AreEqual(CompanionOrderType.StayClose, CompanionOrderRules.PortraitOrder(FollowerMenuItem.StayClose, true));
        Assert.IsNull(CompanionOrderRules.PortraitOrder(FollowerMenuItem.Inventory, false));
        Assert.IsNull(CompanionOrderRules.PortraitOrder(FollowerMenuItem.CharacterSheet, false));
    }

    [TestMethod]
    public void An_order_to_all_reaches_the_followers_within_ten_tiles_in_array_order()
    {
        CompanionOrderCandidate[] followers = [new("c", "Cara", 3), new("a", "Abe", 11), new("b", "Bo", 10), new("d", "Dee", 0), new("e", "Eve", -1)];
        CollectionAssert.AreEqual(new[] { "c", "b", "d" }, CompanionOrderRules.AllInReach(followers).ToArray());
        Assert.IsTrue(CompanionOrderRules.IsCombat(CompanionOrderType.Attack) && CompanionOrderRules.IsCombat(CompanionOrderType.BackOff)
            && CompanionOrderRules.IsCombat(CompanionOrderType.Curse) && !CompanionOrderRules.IsCombat(CompanionOrderType.Wait));
    }

    // ARC-UI-1975: the label is the row's message of follower_ui.mes, whatever the language: the text "name label" still reaches its follower with the type.
    [TestMethod]
    public void A_localized_portrait_label_joins_the_name_and_still_reaches_the_follower()
    {
        CompanionOrderCandidate[] followers = [new("bo", "Бо", 4), new("bob", "Боб", 4)];
        Assert.AreEqual(("bob", CompanionOrderType.Wait), CompanionOrderRules.PortraitReceiver(followers.Reverse().ToArray(), "bob", CompanionOrderType.Wait, "Ждать", Original));
        Assert.AreEqual(("bo", CompanionOrderType.None), CompanionOrderRules.PortraitReceiver(followers, "bob", CompanionOrderType.Wait, "Ждать", Original), "the shorter name sharing the prefix reads the rest as its word");
        Assert.AreEqual(("bob", CompanionOrderType.Wait), CompanionOrderRules.PortraitReceiver(followers, "bob", CompanionOrderType.Wait, "Ждать", Steamwoven));
    }

    [TestMethod]
    public void The_original_portrait_addresses_by_name_prefix_and_steamwoven_the_clicked_follower()
    {
        // Two followers the PC knows by the same name: the first in reach takes the order, whichever portrait was clicked.
        CompanionOrderCandidate[] twins = [new("first", "Wolf", 2), new("second", "wolf", 1)];
        Assert.AreEqual(("first", CompanionOrderType.Wait), CompanionOrderRules.PortraitReceiver(twins, "second", CompanionOrderType.Wait, "wait", Original));
        Assert.AreEqual(("second", CompanionOrderType.Wait), CompanionOrderRules.PortraitReceiver(twins, "second", CompanionOrderType.Wait, "wait", Steamwoven));
        // A shorter name sharing the prefix takes it and reads the rest as its order word: no order.
        CompanionOrderCandidate[] prefix = [new("bo", "Bo", 4), new("bob", "Bob", 4)];
        Assert.AreEqual(("bo", CompanionOrderType.None), CompanionOrderRules.PortraitReceiver(prefix, "bob", CompanionOrderType.StayClose, "stay close", Original));
        Assert.AreEqual(("bo", CompanionOrderType.StayClose), CompanionOrderRules.PortraitReceiver(prefix, "bo", CompanionOrderType.StayClose, "stay close", Original));
        // A clicked follower beyond reach: another in reach may still take the original's text; Steamwoven sends nothing.
        CompanionOrderCandidate[] far = [new("near", "Wolf", 1), new("far", "Wolf", 12)];
        Assert.AreEqual(("near", CompanionOrderType.Wait), CompanionOrderRules.PortraitReceiver(far, "far", CompanionOrderType.Wait, "wait", Original));
        Assert.IsNull(CompanionOrderRules.PortraitReceiver(far, "far", CompanionOrderType.Wait, "wait", Steamwoven));
        Assert.IsNull(CompanionOrderRules.PortraitReceiver([new("far", "Wolf", 12)], "far", CompanionOrderType.Wait, "wait", Original));
        Assert.IsNull(CompanionOrderRules.PortraitReceiver(far, "nobody", CompanionOrderType.Wait, "wait", Original), "not a follower");
    }

    [TestMethod]
    public void The_farewell_range_follows_the_reaction_level_and_d3()
    {
        Assert.AreEqual(CompanionOrderLine.FriendlyFarewell, CompanionOrderRules.Farewell(40, Original), "OL13: a disliking follower says the friendly goodbye");
        Assert.AreEqual(CompanionOrderLine.HostileFarewell, CompanionOrderRules.Farewell(41, Original));
        Assert.AreEqual(CompanionOrderLine.FriendlyFarewell, CompanionOrderRules.Farewell(-50, Original));
        Assert.AreEqual(CompanionOrderLine.HostileFarewell, CompanionOrderRules.Farewell(40, Steamwoven), "D3");
        Assert.AreEqual(CompanionOrderLine.FriendlyFarewell, CompanionOrderRules.Farewell(41, Steamwoven));
        Assert.AreEqual(new LoyaltyLines(1900, 1999), CompanionOrderRules.Lines(CompanionOrderLine.FriendlyFarewell));
        Assert.AreEqual(new LoyaltyLines(2000, 2099), CompanionOrderRules.Lines(CompanionOrderLine.HostileFarewell));
        Assert.AreEqual(new LoyaltyLines(2100, 2199), CompanionOrderRules.Lines(CompanionOrderLine.Acknowledge));
        Assert.AreEqual(new LoyaltyLines(2200, 2299), CompanionOrderRules.Lines(CompanionOrderLine.Cannot));
    }

    [TestMethod]
    public void The_settings_are_named_and_differ_in_every_switch()
    {
        Assert.AreEqual(Original, CompanionOrderRuleProfile.FromName("original"));
        Assert.AreEqual(Steamwoven, CompanionOrderRuleProfile.FromName("steamwoven"));
        Assert.IsNull(CompanionOrderRuleProfile.FromName("Original"));
        Assert.IsTrue(Original.FollowOrderNeverUnwaits && Original.PresetSixAddressesNobody && Original.FarewellRangesSwapped && Original.PortraitAddressesByNamePrefix);
        Assert.IsFalse(Steamwoven.FollowOrderNeverUnwaits || Steamwoven.PresetSixAddressesNobody || Steamwoven.FarewellRangesSwapped || Steamwoven.PortraitAddressesByNamePrefix);
    }
}
