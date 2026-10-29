using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-064: the barter window's original layout (ledger BW1–BW5): the background by the follower rule, the shown grid from
/// y 168 with 7 rows and its scroll, the gamble box, the hit test, a buy and a sale by drag, the round view's art and the quote line
/// (no game data).</summary>
[TestClass]
public sealed class AreaBarterArtTests
{
    private static readonly SessionItemKind Kind = new(SessionItemType.Generic, 9000, 0, 0);

    private static AreaInventoryView View(params AreaLootGridItem[] wares) =>
        new([], "npc:1", "Smith", []) { LootGrid = wares, Grid = [new AreaGridItem("own:1", 0, 1, 1, "Own")] };

    [TestMethod]
    public void TheBarterArtsAndTheRoundViewsArtAreRead()
    {
        CollectionAssert.AreEqual(new[] { 235, 825, 345, 344, 346, 348, 347 }, AreaBarterArtLayout.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaBarterArtLayout.ArtNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), AreaInventoryHole.BarterArt);
        CollectionAssert.AllItemsAreUnique(AreaInventoryArtContent.Numbers.ToArray());
        Assert.AreEqual(231, AreaInventoryHole.Art(AreaInventoryMode.Barter));
        Assert.AreEqual((16, 17), AreaInventoryHole.Corner(AreaInventoryMode.Barter));
    }

    [TestMethod]
    public void TheFollowerBackgroundNeedsTheCycleButtons()
    {
        Assert.AreEqual(235, AreaBarterArtLayout.Background(follower: false, pcFollowers: 3), "a merchant");
        Assert.AreEqual(235, AreaBarterArtLayout.Background(follower: true, pcFollowers: 1), "the only follower: no cycle buttons");
        Assert.AreEqual(825, AreaBarterArtLayout.Background(follower: true, pcFollowers: 2));
    }

    [TestMethod]
    public void TheShownGridIsTenBySevenShownRowsFrom8And168WithItsScroll()
    {
        Assert.AreEqual(new AreaBarterHit(null, null, 0, false), AreaBarterArtLayout.HitTest(8, 168, 0, (80, 43)));
        Assert.AreEqual(new AreaBarterHit(null, null, 69, false), AreaBarterArtLayout.HitTest(8 + 319.9f, 168 + 223.9f, 0, (80, 43)));
        Assert.AreEqual(new AreaBarterHit(null, null, 21, false), AreaBarterArtLayout.HitTest(8 + 32, 168, 2, (80, 43)), "(row + scroll) × 10 + column");
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(8, 167.9f, 0, (80, 43)), "above the grid: the name and quote");
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(8, 168 + 224, 0, (80, 43)), "below the seventh row");
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(8 + 320, 168, 0, (80, 43)), "the scroll bar's column");
        Assert.AreEqual((5, 89), (AreaBarterArtLayout.ScrollMax(container: false), AreaBarterArtLayout.ScrollMax(container: true)));
    }

    [TestMethod]
    public void TheHerosSideIsItsGridBoxesAndTheGambleBox()
    {
        Assert.AreEqual(new AreaBarterHit(0, null, null, false), AreaBarterArtLayout.HitTest(368, 8, 3, (80, 43)), "the hero's grid does not scroll");
        Assert.AreEqual(new AreaBarterHit(null, AreaInventoryIcon.Drop, null, false), AreaBarterArtLayout.HitTest(703, 344, 0, (80, 43)));
        Assert.AreEqual(new AreaBarterHit(null, AreaInventoryIcon.Use, null, false), AreaBarterArtLayout.HitTest(703, 290, 0, (80, 43)));
        Assert.AreEqual(new AreaBarterHit(null, null, null, true), AreaBarterArtLayout.HitTest(703, 236, 0, (80, 43)));
        Assert.AreEqual(new AreaBarterHit(null, null, null, true), AreaBarterArtLayout.HitTest(782.9f, 278.9f, 0, (80, 43)));
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(703, 236, 0, (0, 0)), "no gamble art: no box");
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(160, 130, 0, (80, 43)), "the hero's doll is not shown: no slot");
        Assert.AreEqual(345, AreaBarterArtLayout.GambleArt, "NOT READY: the gamble check, the box keeps its off art");
    }

    [TestMethod]
    public void AnItemIsDrawnAtItsLocationAndCutToTheSevenShownRows()
    {
        var item = new AreaLootGridItem("ware:1", 23, 1, 2, "Sword", Kind);
        Assert.AreEqual((8f + 96, 168f + 64, 32f, 64f), AreaBarterArtLayout.ItemRect(item, 0));
        Assert.AreEqual((8f + 96, 168f, 32f, 32f), AreaBarterArtLayout.ItemRect(item, 3), "its upper cell is scrolled away");
        Assert.IsNull(AreaBarterArtLayout.ItemRect(item, 4));
        Assert.AreEqual((8f + 96, 168f + 32 * 6, 32f, 32f), AreaBarterArtLayout.ItemRect(new AreaLootGridItem("ware:2", 63, 1, 2, "Staff", Kind), 0));
        Assert.IsNull(AreaBarterArtLayout.ItemRect(new AreaLootGridItem("ware:3", 70, 1, 1, "Ring", Kind), 0), "the eighth row is not shown");
        Assert.AreEqual((8f, 168f - 32, 32f, 32f), AreaBarterArtLayout.FullRect(new AreaLootGridItem("ware:4", 0, 1, 1, "Ring", Kind), 1));
    }

    [TestMethod]
    public void AWareOnTheHerosGridIsBoughtAndAHeroItemOnTheShownGridSold()
    {
        var view = View(new AreaLootGridItem("ware:1", 11, 1, 2, "Sword", Kind));
        var ware = AreaBarterArtLayout.Press(view, new AreaBarterHit(null, null, 21, false));
        Assert.AreEqual(new AreaInventoryHeld("ware:1", null, 10) { Partner = true }, ware, "its lower cell: the grab's offset");
        Assert.IsNull(AreaBarterArtLayout.Press(view, new AreaBarterHit(null, null, 12, false)));
        Assert.IsNull(AreaBarterArtLayout.Press(view, new AreaBarterHit(null, null, null, true)), "the gamble box holds nothing");
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Buy, ware!, GridSlot: 5), AreaBarterArtLayout.Release(ware!, new AreaBarterHit(5, null, null, false)));
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(ware!, new AreaBarterHit(null, null, 30, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(ware!, new AreaBarterHit(null, AreaInventoryIcon.Drop, null, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(ware!, AreaBarterHit.None).Kind);

        var own = AreaBarterArtLayout.Press(view, new AreaBarterHit(0, null, null, false));
        Assert.AreEqual(new AreaInventoryHeld("own:1", null, 0), own);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Sell, own!, GridSlot: 3), AreaBarterArtLayout.Release(own!, new AreaBarterHit(null, null, 3, false)));
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(own!, new AreaBarterHit(7, null, null, false)).Kind, "NOT READY: grid moves");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(own!, new AreaBarterHit(null, null, null, true)).Kind, "NOT READY: the gamble");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(own!, new AreaBarterHit(null, AreaInventoryIcon.Use, null, false)).Kind);
    }

    [TestMethod]
    public void ARightReleaseMovesTheItemToTheOtherSide()
    {
        var view = View(new AreaLootGridItem("ware:1", 11, 1, 2, "Sword", Kind));
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Buy, new AreaInventoryHeld("ware:1", null, 10) { Partner = true }),
            AreaBarterArtLayout.RightRelease(view, new AreaBarterHit(null, null, 21, false)));
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Sell, new AreaInventoryHeld("own:1", null, 0)),
            AreaBarterArtLayout.RightRelease(view, new AreaBarterHit(0, null, null, false)));
        Assert.IsNull(AreaBarterArtLayout.RightRelease(view, new AreaBarterHit(null, null, 12, false)), "no item");
        Assert.IsNull(AreaBarterArtLayout.RightRelease(view, new AreaBarterHit(null, null, null, true)), "the gamble box");
    }

    [TestMethod]
    public void TheBoxesRefuseTheMerchantsItem()
    {
        var ware = new AreaInventoryHeld("ware:1", null, 0) { Partner = true };
        Assert.AreEqual(348, AreaBarterArtLayout.BoxArt(AreaInventoryIcon.Drop, ware, over: true));
        Assert.AreEqual(347, AreaBarterArtLayout.BoxArt(AreaInventoryIcon.Use, ware, over: true));
        Assert.AreEqual(225, AreaBarterArtLayout.BoxArt(AreaInventoryIcon.Drop, new AreaInventoryHeld("own:1", null, 0), over: true));
    }

    [TestMethod]
    public void TheQuoteLineNamesThePriceOrWhyThereIsNone()
    {
        var merchant = new AreaBarterMerchant("npc:1", "Smith", 100, false, false, InventorySourceBuyList.Nothing, false);
        AreaBarterLine Line(string entity, int price, TradeRefusal refusal, int count = 1) =>
            new(entity, entity == "own:1" ? "Sword" : "Axe", count, new TradeQuote(price, refusal), 100, Kind);
        var view = new AreaBarterView(merchant, 100, 50, 10, SkillTraining.None, 50, [Line("own:1", 55, TradeRefusal.None)],
            [Line("ware:1", 0, TradeRefusal.WontSell), Line("ware:2", 12, TradeRefusal.None, count: 20)]);
        Assert.AreEqual("Sell Sword: 55", AreaBarterArtLayout.Quote(view, "own:1"));
        Assert.AreEqual("Buy Axe: no price (WontSell)", AreaBarterArtLayout.Quote(view, "ware:1"));
        Assert.AreEqual("Buy Axe: 12 each", AreaBarterArtLayout.Quote(view, "ware:2"));
        Assert.AreEqual(string.Empty, AreaBarterArtLayout.Quote(view, "coins:1"), "not a line of either side");
        Assert.AreEqual(string.Empty, AreaBarterArtLayout.Quote(view, null));
        Assert.AreEqual(string.Empty, AreaBarterArtLayout.Quote(view with { Merchant = merchant with { Follower = true } }, "own:1"), "a follower has no quote");
    }
}
