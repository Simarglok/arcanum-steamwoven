using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-1884: the quantity pane's layout, icon art ids and count cells (ledger QW3, bar mode 9 of 0x551210, 0x56A300).</summary>
[TestClass]
public sealed class QuantityPaneTests
{
    [TestMethod]
    public void TheFiveButtonsAreTheOriginalsTable()
    {
        CollectionAssert.AreEqual(
            new[] { (QuantityPaneAction.All, 364, 548, 299), (QuantityPaneAction.Up, 479, 547, 805), (QuantityPaneAction.Down, 479, 559, 804),
                (QuantityPaneAction.Accept, 545, 506, 33), (QuantityPaneAction.Cancel, 545, 557, 32) },
            QuantityPaneRules.Buttons.Select(b => (b.Action, b.X, b.Y, b.Art)).ToArray());
        CollectionAssert.AreEqual(new[] { 298, 299, 805, 804, 33, 32 }, QuantityPaneRules.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void TheIconIsAnItemArtOfPlaceTwo()
    {
        // 0x5043C0 with number 0, place 2, type 3 (gold) and with number n, place 2, type 1 (ammunition n): class 6, bits 12..13 = 2.
        Assert.AreEqual(0x60002003u, QuantityPaneRules.IconArtId(gold: true, 0));
        Assert.AreEqual(0x60002001u, QuantityPaneRules.IconArtId(gold: false, 0));
        Assert.AreEqual(0x60002001u | (3u << 17), QuantityPaneRules.IconArtId(gold: false, 3));
        foreach (var id in new[] { QuantityPaneRules.IconArtId(true, 0), QuantityPaneRules.IconArtId(false, 2) })
        {
            Assert.AreEqual(6u, id!.Value >> 28);
            Assert.AreEqual(2u, (id.Value >> 12) & 3);
            Assert.AreEqual(0u, (id.Value >> 4) & 3, "palette 0");
        }
        Assert.IsNull(QuantityPaneRules.IconArtId(false, 4), "ammunition type 4 leaves the icon unset");
        Assert.IsNull(QuantityPaneRules.IconArtId(false, 5));
        Assert.IsNull(QuantityPaneRules.IconArtId(false, -1));
    }

    [TestMethod]
    public void TheCountDrawsNoLeadingZerosAndKeepsItsLowSixDigits()
    {
        string Text(int count) => new string(QuantityPaneRules.Cells(count).Reverse().Select(c => c ?? '.').ToArray());
        Assert.AreEqual(".....0", Text(0), "a zero count draws its one digit");
        Assert.AreEqual("....12", Text(12));
        Assert.AreEqual("100000", Text(100000));
        Assert.AreEqual("999999", Text(999999));
        Assert.AreEqual("000000", Text(1000000), "the cells hold the low six digits (the original's loop runs six times)");
        Assert.AreEqual("234567", Text(1234567));
        Assert.AreEqual("..1000", Text(1000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => QuantityPaneRules.Cells(-1));
    }
}
