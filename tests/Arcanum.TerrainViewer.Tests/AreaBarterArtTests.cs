using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        Assert.AreEqual(345, AreaBarterArtLayout.GambleArt, "the resting art, which also sizes the box");
        // ARC-UI-1740: an item held over the box shows 344 when the gate passes and 346 when it refuses; nothing held, or moving out, 345.
        Assert.AreEqual(345, AreaBarterArtLayout.GambleBoxArt(false, true));
        Assert.AreEqual(345, AreaBarterArtLayout.GambleBoxArt(false, false));
        Assert.AreEqual(344, AreaBarterArtLayout.GambleBoxArt(true, true));
        Assert.AreEqual(346, AreaBarterArtLayout.GambleBoxArt(true, false));
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
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(own!, new AreaBarterHit(null, null, null, true)).Kind, "the hero's own item on the bet box only goes back: the gate refuses it silently");
        // ARC-UI-1740: the merchant's item released on the bet box is a bet (0x579B60).
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Gamble, ware!), AreaBarterArtLayout.Release(ware!, new AreaBarterHit(null, null, null, true)));
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

    [TestMethod]
    public void TheCycleButtonsExistOnTheFollowerBackgroundOnly()
    {
        Assert.IsTrue(AreaBarterArtLayout.CycleShown(AreaBarterArtLayout.Background(follower: true, pcFollowers: 2)));
        Assert.IsFalse(AreaBarterArtLayout.CycleShown(AreaBarterArtLayout.Background(follower: true, pcFollowers: 1)), "a single follower has no one to cycle to");
        Assert.IsFalse(AreaBarterArtLayout.CycleShown(AreaBarterArtLayout.Background(follower: false, pcFollowers: 3)), "a merchant has none");
        Assert.AreEqual((757, 758), (AreaBarterArtLayout.CycleLeftArt, AreaBarterArtLayout.CycleRightArt));
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), 757);
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), 758);
        CollectionAssert.DoesNotContain(AreaBarterArtLayout.ArtNumbers.ToArray(), 757, "the buttons are drawn when they read and are no reason for the diagnostic window");
    }

    [TestMethod]
    public void TheCycleHitTestUsesTheButtonsArt()
    {
        (int, int) art = (23, 23);
        Assert.AreEqual(0, AreaBarterArtLayout.CycleHit(155, 55, art, art), "the left button's corner");
        Assert.AreEqual(0, AreaBarterArtLayout.CycleHit(177.9f, 77.9f, art, art));
        Assert.IsNull(AreaBarterArtLayout.CycleHit(178, 55, art, art), "right of the left button");
        Assert.AreEqual(1, AreaBarterArtLayout.CycleHit(253, 55, art, art), "the right button's corner");
        Assert.AreEqual(1, AreaBarterArtLayout.CycleHit(275.5f, 77.5f, art, art));
        Assert.IsNull(AreaBarterArtLayout.CycleHit(252.9f, 55, art, art));
        Assert.IsNull(AreaBarterArtLayout.CycleHit(155, 54.9f, art, art));
        Assert.IsNull(AreaBarterArtLayout.CycleHit(155, 55, (0, 0), (0, 0)), "an art that did not read has no hit area");
    }

    [TestMethod]
    public void TheCycleStepsTheFollowerListAndWraps()
    {
        string[] list = ["a", "b", "c"];
        Assert.AreEqual("b", AreaBarterCycle.Step(list, "a", next: true, _ => true));
        Assert.AreEqual("a", AreaBarterCycle.Step(list, "c", next: true, _ => true), "past the last wraps to the first");
        Assert.AreEqual("c", AreaBarterCycle.Step(list, "a", next: false, _ => true), "before the first wraps to the last");
        Assert.AreEqual("a", AreaBarterCycle.Step(list, "b", next: false, _ => true));
        Assert.AreEqual("c", AreaBarterCycle.Step(list, "a", next: true, candidate => candidate != "b"), "a refused follower is skipped");
        Assert.AreEqual("b", AreaBarterCycle.Step(list, "a", next: false, candidate => candidate != "c"), "backwards skips too, wrapping");
        Assert.IsNull(AreaBarterCycle.Step(list, "a", next: true, _ => false), "none acceptable: the window stays");
        Assert.IsNull(AreaBarterCycle.Step(["a"], "a", next: true, _ => true), "a single follower stays");
        Assert.IsNull(AreaBarterCycle.Step(list, "x", next: true, _ => true), "a follower not in the list does not move the window");
        Assert.IsNull(AreaBarterCycle.Step([], "a", next: false, _ => true));
    }

    // ARC-UI-1917 (ledger IW29, IW30): the merchant's doll view and its two toggle buttons.
    [TestMethod]
    public void TheMerchantsDollHasItsOwnArtButtonsAndTheOriginalsTableRectangles()
    {
        Assert.AreEqual((0, 157), AreaBarterArtLayout.DollView);
        Assert.AreEqual((8, 123), AreaBarterArtLayout.DollButton);
        Assert.AreEqual((46, 123), AreaBarterArtLayout.BagButton);
        CollectionAssert.AreEqual(new[] { 337, 338, 339 }, AreaBarterArtLayout.DollArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaBarterArtLayout.DollArtNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.DoesNotContain(AreaBarterArtLayout.ArtNumbers.ToArray(), 337, "a window whose doll arts do not read keeps the grid view, not the diagnostic window");
        // Table 0x5CAD78 (mode 1): the helmet 157, 182, 42 × 42 first, the boots 156, 330, 42 × 42 last; the armour 136, 224, 84 × 106 (all in frame coordinates, as the loot table).
        Assert.AreEqual(9, AreaBarterArtLayout.BodySlotRects.Count);
        Assert.AreEqual((157, 182, 42, 42), AreaBarterArtLayout.BodySlotRects[Arcanum.Simulation.Equipment.EquipmentSlot.Helmet]);
        Assert.AreEqual((136, 224, 84, 106), AreaBarterArtLayout.BodySlotRects[Arcanum.Simulation.Equipment.EquipmentSlot.Armor]);
        Assert.AreEqual((156, 330, 42, 42), AreaBarterArtLayout.BodySlotRects[Arcanum.Simulation.Equipment.EquipmentSlot.Boots]);
        foreach (var (_, rect) in AreaBarterArtLayout.BodySlotRects)
            Assert.IsTrue(rect.X >= 0 && rect.X + rect.Width <= 358 && rect.Y >= AreaBarterArtLayout.DollView.Y && rect.Y + rect.Height <= 400, $"{rect} lies on the 337 doll's 358 × 243 area");
    }

    [TestMethod]
    public void TheMerchantsTogglesShowWheneverAMerchantIsShownAndHitBySizeOfTheirArt()
    {
        Assert.IsTrue(AreaBarterArtLayout.TogglesShown(View()));
        Assert.IsTrue(AreaBarterArtLayout.TogglesShown(View() with { HolderContainer = true }), "the test is the merchant's (before its substitute container is shown), not the shown object's");
        Assert.IsFalse(AreaBarterArtLayout.TogglesShown(new([], null, null, [])), "no shown object: no buttons");
        Assert.AreEqual(true, AreaBarterArtLayout.ToggleHit(8, 123, (30, 20), (30, 20)));
        Assert.AreEqual(true, AreaBarterArtLayout.ToggleHit(8 + 29.9f, 123 + 19.9f, (30, 20), (30, 20)));
        Assert.IsNull(AreaBarterArtLayout.ToggleHit(8 + 30, 123, (30, 20), (30, 20)));
        Assert.AreEqual(false, AreaBarterArtLayout.ToggleHit(46, 123, (30, 20), (30, 20)));
        Assert.AreEqual(false, AreaBarterArtLayout.ToggleHit(46 + 29, 123 + 19, (30, 20), (30, 20)));
        Assert.IsNull(AreaBarterArtLayout.ToggleHit(46, 122.9f, (30, 20), (30, 20)));
        Assert.IsNull(AreaBarterArtLayout.ToggleHit(23, 108, (30, 20), (30, 20)), "the loot window's place is not the barter window's");
        Assert.IsNull(AreaBarterArtLayout.ToggleHit(8, 123, (0, 0), (0, 0)), "an art that did not read has no hit area");
    }

    [TestMethod]
    public void TheDollViewReplacesTheShownGridAndItsSlotsHitTheMerchantsWornItems()
    {
        Assert.AreEqual(new AreaBarterHit(null, null, 0, false), AreaBarterArtLayout.HitTest(8, 168, 0, (80, 43)));
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(8, 168, 0, (80, 43), doll: true), "the grid is hidden in the doll view");
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(8, 400 - 1, 0, (80, 43), doll: true), "no slot there");
        // ARC-UI-1926 (0x575CB0): a point of a slot rectangle is the location 1000 + slot (the helmet at (157, 182)).
        var helmet = (int)Arcanum.Simulation.Equipment.EquipmentSlot.Helmet;
        Assert.AreEqual(new AreaBarterHit(null, null, helmet, false), AreaBarterArtLayout.HitTest(160, 190, 0, (80, 43), doll: true));
        Assert.IsTrue(helmet >= 1000 && helmet <= 1008);
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(157 + 42, 182, 0, (80, 43), doll: true), "right of the helmet's rectangle");
        Assert.AreEqual(new AreaBarterHit(0, null, null, false), AreaBarterArtLayout.HitTest(368, 8, 0, (80, 43), doll: true), "the hero's grid is still there");
        Assert.AreEqual(new AreaBarterHit(null, null, null, true), AreaBarterArtLayout.HitTest(703, 236, 0, (80, 43), doll: true), "and the gamble box");
        var view = View(new AreaLootGridItem("w1", 0, 1, 1, "Ware", Kind)) with
        {
            BodyDoll = [new AreaDollSlot(Arcanum.Simulation.Equipment.EquipmentSlot.Helmet, "helm:1", "Helm")],
        };
        var hit = AreaBarterArtLayout.HitTest(160, 190, 0, (80, 43), doll: true);
        Assert.AreEqual(new AreaInventoryHeld("helm:1", null, 0) { Partner = true }, AreaBarterArtLayout.Press(view, hit), "the worn item is lifted as a grid item of the merchant");
        Assert.IsNull(AreaBarterArtLayout.Press(view, AreaBarterArtLayout.HitTest(136 + 5, 230, 0, (80, 43), doll: true)), "an empty slot lifts nothing");
        // Released on the hero's grid it is a buy; elsewhere it goes back; a right release buys it.
        var held = AreaBarterArtLayout.Press(view, hit)!;
        Assert.AreEqual(AreaInventoryDropKind.Buy, AreaBarterArtLayout.Release(held, AreaBarterArtLayout.HitTest(368, 8, 0, (80, 43), doll: true)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(held, hit).Kind, "on its own doll it goes back");
        Assert.AreEqual(AreaInventoryDropKind.Buy, AreaBarterArtLayout.RightRelease(view, hit)?.Kind);
        // The hero's item released on a slot of the merchant's doll goes back (NOT READY: the wear test 0x464D20 and the sale onto the slot).
        var own = new AreaInventoryHeld("own:1", null, 0);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(own, hit).Kind);
    }

    [TestMethod]
    public void TheQuoteLineQuotesTheMerchantsWornItem()
    {
        var merchant = new AreaBarterMerchant("npc:1", "Smith", 100, false, false, InventorySourceBuyList.Nothing, false);
        var vest = new AreaBarterLine("vest:1", "Vest (worn)", 1, new TradeQuote(0, TradeRefusal.Worn), 100, Kind);
        var view = new AreaBarterView(merchant, 100, 50, 10, SkillTraining.None, 50, [], []) { WornWares = [vest] };
        Assert.AreEqual("Buy Vest (worn): no price (Worn)", AreaBarterArtLayout.Quote(view, "vest:1"));
    }
}
