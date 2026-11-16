using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-2311: the bottom strip's counters 3 and 4 against docs/research/ledger/spell-runs.md BC1–BC8; values from the ledger,
/// expected results worked by hand.</summary>
[TestClass]
public sealed class BarCountersTests
{
    [TestMethod]
    public void The_slots_stand_where_the_table_puts_them()
    {
        Assert.AreEqual((104, 512, 50, 20), BarCounterRules.Counter3Rect);
        Assert.AreEqual((264, 562, 50, 20), BarCounterRules.Counter4Rect);
        Assert.AreEqual((6, 3), (BarCounterRules.Text(0, BarCounterRules.Counter3Digits).Length, BarCounterRules.Text(0, BarCounterRules.Counter4Digits).Length));
        Assert.AreEqual((61, 509), (BarCounterRules.IconX, BarCounterRules.IconY));
        CollectionAssert.AreEqual(new[] { 250, 251, 252, 253, 469, 474 }, BarCounterRules.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void A_weapon_with_an_ammunition_type_shows_the_heros_count_and_its_types_icon()
    {
        Assert.AreEqual(new BarCounter3(37, 250), BarCounterRules.Counter3(true, 0, 37, 99, 500));
        Assert.AreEqual(new BarCounter3(0, 253), BarCounterRules.Counter3(true, 3, 0, 99, 500), "no ammunition item: 0");
        Assert.AreEqual(new BarCounter3(5, null), BarCounterRules.Counter3(true, 4, 5, 0, 0), "past the table: no icon");
    }

    [TestMethod]
    public void Without_ammunition_the_mana_store_then_the_gold_shows()
    {
        Assert.AreEqual(new BarCounter3(12, 469), BarCounterRules.Counter3(true, null, 0, 12, 500));
        Assert.AreEqual(new BarCounter3(500, 474), BarCounterRules.Counter3(true, null, 0, 0, 500));
        Assert.AreEqual(new BarCounter3(500, 474), BarCounterRules.Counter3(true, null, 0, -4, 500), "a store of 0 or less does not show");
        Assert.AreEqual(new BarCounter3(0, 474), BarCounterRules.Counter3(false, 1, 7, 9, 500), "no hero");
    }

    [TestMethod]
    public void Counter_4_shows_the_charges_minus_1_for_unlimited_and_minus_2_for_unidentified()
    {
        Assert.AreEqual(7, BarCounterRules.Counter4(true, 7));
        Assert.AreEqual(0, BarCounterRules.Counter4(true, 0));
        Assert.AreEqual(-1, BarCounterRules.Counter4(true, -5));
        Assert.AreEqual(-2, BarCounterRules.Counter4(false, 7));
        Assert.AreEqual(-2, BarCounterRules.Counter4(false, -3));
    }

    [TestMethod]
    public void The_text_pads_to_the_places_and_draws_a_run_for_a_negative_value()
    {
        Assert.AreEqual("000037", BarCounterRules.Text(37, 6));
        Assert.AreEqual("000", BarCounterRules.Text(0, 3));
        Assert.AreEqual("1234567", BarCounterRules.Text(1234567, 6), "a longer number is not cut");
        Assert.AreEqual("---", BarCounterRules.Text(-1, 3));
        Assert.AreEqual("???", BarCounterRules.Text(-2, 3));
        Assert.AreEqual("??????", BarCounterRules.Text(-9, 6));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BarCounterRules.Text(1, 0));
    }
}
