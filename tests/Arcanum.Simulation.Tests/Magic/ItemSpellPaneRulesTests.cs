using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-UI-016: the bar's weapon button, the item spell pane's buttons and gauge, and the item and hotkey activation test (ledger IP rows).
[TestClass]
public sealed class ItemSpellPaneRulesTests
{
    private const int None = ItemSpellPaneRules.NoSpell;
    private const uint Identified = ItemSpellPaneRules.IdentifiedFlag, Magical = ItemSpellPaneRules.MagicalFlag;

    // Records 140.. technological, 87 has item triggers, 300 is outside the table.
    private static bool? Technological(int spell) => spell is >= 0 and < 223 ? spell >= 140 : null;
    private static bool? Triggers(int spell) => spell is >= 0 and < 223 ? spell == 87 : null;

    [TestMethod]
    public void The_weapon_button_needs_charges_or_the_magical_flag_and_a_positive_complexity()
    {
        Assert.IsTrue(ItemSpellPaneRules.WeaponButtonShown(20, 0, 40));
        Assert.IsTrue(ItemSpellPaneRules.WeaponButtonShown(-1, 0, 40), "unlimited charges count as not 0");
        Assert.IsTrue(ItemSpellPaneRules.WeaponButtonShown(0, Magical, 40), "the magical flag without charges");
        Assert.IsFalse(ItemSpellPaneRules.WeaponButtonShown(0, Identified, 40), "neither charges nor the magical flag");
        Assert.IsFalse(ItemSpellPaneRules.WeaponButtonShown(-1, Magical, 0), "complexity 0");
        Assert.IsFalse(ItemSpellPaneRules.WeaponButtonShown(-1, Magical, -90), "a technological weapon");
    }

    [TestMethod]
    public void An_unidentified_item_or_one_without_positive_complexity_shows_no_button()
    {
        Assert.AreEqual(0, ItemSpellPaneRules.PaneButtons([62, 50, None, None, None], 0, 60, Technological, Triggers).Count);
        Assert.AreEqual(0, ItemSpellPaneRules.PaneButtons([62, 50, None, None, None], Identified, 0, Technological, Triggers).Count);
        Assert.AreEqual(0, ItemSpellPaneRules.PaneButtons([62, 50, None, None, None], Identified | Magical, -10, Technological, Triggers).Count);
    }

    [TestMethod]
    public void The_pane_shows_every_filled_slot_but_technology_and_a_triggered_spell_does_not_activate()
    {
        var buttons = ItemSpellPaneRules.PaneButtons([62, None, 87, 147, 54], Identified, 60, Technological, Triggers);
        CollectionAssert.AreEqual(new ItemSpellButton[] { new(0, 62, true), new(2, 87, false), new(4, 54, true) }, buttons.ToArray());
    }

    [TestMethod]
    public void A_value_outside_the_record_table_shows_no_button_and_activates_nothing()
    {
        Assert.AreEqual(0, ItemSpellPaneRules.PaneButtons([300], Identified, 60, Technological, Triggers).Count);
        Assert.IsFalse(ItemSpellPaneRules.CanActivate(300, Triggers));
    }

    [TestMethod]
    public void The_gauge_shows_the_charges_unlimited_or_nothing_known()
    {
        Assert.AreEqual(new ItemSpellGauge(ItemSpellGaugeKind.Charges, 20), ItemSpellPaneRules.Gauge(20, Identified));
        Assert.AreEqual(new ItemSpellGauge(ItemSpellGaugeKind.Charges, 0), ItemSpellPaneRules.Gauge(0, Identified));
        Assert.AreEqual(new ItemSpellGauge(ItemSpellGaugeKind.Unlimited), ItemSpellPaneRules.Gauge(-1, Identified));
        Assert.AreEqual(new ItemSpellGauge(ItemSpellGaugeKind.Unknown), ItemSpellPaneRules.Gauge(20, Magical));
    }

    [TestMethod]
    public void Only_a_spell_without_item_triggers_activates()
    {
        Assert.IsTrue(ItemSpellPaneRules.CanActivate(62, Triggers));
        Assert.IsFalse(ItemSpellPaneRules.CanActivate(87, Triggers), "item triggers");
        Assert.IsFalse(ItemSpellPaneRules.CanActivate(-1, Triggers), "an empty slot read as -1");
        Assert.IsFalse(ItemSpellPaneRules.CanActivate(None, Triggers), "an empty field");
        Assert.IsTrue(ItemSpellPaneRules.CanActivate(147, Triggers), "the activation itself does not test technology");
    }
}
