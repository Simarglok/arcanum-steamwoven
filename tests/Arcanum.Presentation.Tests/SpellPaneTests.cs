using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-053: the spell pane rules against the behaviour recorded in docs/research/ledger/spell-runs.md SPN1–SPN6 (the background,
/// the college and spell buttons, which of them show); values from the ledger, expected results worked by hand.</summary>
[TestClass]
public sealed class SpellPaneTests
{
    [TestMethod]
    public void The_buttons_lie_where_the_records_put_them()
    {
        Assert.AreEqual((8, 196, 492), (SpellPaneRules.BackgroundArt, SpellPaneRules.BackgroundX, SpellPaneRules.BackgroundY));
        Assert.AreEqual(16, SpellPaneRules.CollegeButtonX.Count);
        Assert.AreEqual(201, SpellPaneRules.CollegeButtonX[0]);
        Assert.AreEqual(376, SpellPaneRules.CollegeButtonX[7]);
        Assert.AreEqual(400, SpellPaneRules.CollegeButtonX[8], "the second eight start 24 pixels on, not 25");
        Assert.AreEqual(575, SpellPaneRules.CollegeButtonX[15]);
        CollectionAssert.AreEqual(new[] { 284, 334, 384, 435, 485 }, SpellPaneRules.SpellButtonX.ToArray());
        Assert.AreEqual((497, 528), (SpellPaneRules.CollegeButtonY, SpellPaneRules.SpellButtonY));
    }

    [TestMethod]
    public void College_buttons_take_their_art_from_the_table()
    {
        Assert.AreEqual(11, SpellPaneRules.CollegeButtonArt(0), "conveyance");
        Assert.AreEqual(56, SpellPaneRules.CollegeButtonArt(6), "force");
        Assert.AreEqual(104, SpellPaneRules.CollegeButtonArt(11), "black necromancy");
        Assert.AreEqual(122, SpellPaneRules.CollegeButtonArt(15), "temporal");
        Assert.IsNull(SpellPaneRules.CollegeButtonArt(16));
        Assert.IsNull(SpellPaneRules.CollegeButtonArt(-1));
        Assert.AreEqual(1 + 16 + 80, SpellPaneRules.ArtNumbers.Count, "the background, sixteen college arts and eighty spell icons, none twice");
        Assert.AreEqual(SpellPaneRules.ArtNumbers.Count, SpellPaneRules.ArtNumbers.Distinct().Count());
    }

    [TestMethod]
    public void Colleges_with_a_rank_show_and_their_known_spells_in_rank_order()
    {
        var ranks = new int[16];
        ranks[6] = 2; ranks[11] = 5; ranks[3] = 0;
        CollectionAssert.AreEqual(new[] { 6, 11 }, SpellPaneRules.ShownColleges(ranks).ToArray());
        CollectionAssert.AreEqual(new[] { 30, 31 }, SpellPaneRules.ShownSpells(ranks, 6).ToArray());
        CollectionAssert.AreEqual(new[] { 55, 56, 57, 58, 59 }, SpellPaneRules.ShownSpells(ranks, 11).ToArray());
        Assert.AreEqual(0, SpellPaneRules.ShownSpells(ranks, 0).Count, "the starting selection, conveyance, shows nothing without a rank");
        Assert.IsFalse(SpellPaneRules.CollegeShown(0));
        Assert.IsTrue(SpellPaneRules.CollegeShown(1));
        Assert.ThrowsExactly<ArgumentException>(() => SpellPaneRules.ShownColleges(new int[15]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellPaneRules.ShownSpells(ranks, 16));
    }
}
