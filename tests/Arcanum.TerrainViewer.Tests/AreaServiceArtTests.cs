using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1765: the item identify and repair windows' original layout (ledger IS2, IW20, IW26–IW27: the own window's frame with the NPC's image at half size and its quote line, no boxes;
/// no game data).</summary>
[TestClass]
public sealed class AreaServiceArtTests
{
    private static AreaInventoryView View() => new([], null, null, [])
    {
        Grid = [new AreaGridItem("axe", 0, 1, 3, "Axe"), new AreaGridItem("ring", 11, 1, 1, "Ring")],
        Doll = [new AreaDollSlot(EquipmentSlot.Helmet, "helm", "Helm")],
    };

    [TestMethod]
    public void TheBackgroundIsTheOwnWindowsAndTheArtsAreRead()
    {
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), AreaServiceArtLayout.BackgroundArt);
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), AreaInventoryHole.InventoryArt);
        Assert.AreEqual(233, AreaInventoryHole.Art(AreaInventoryMode.Own), "the round view of modes 0, 5 and 6");
        Assert.AreEqual((11, 9), AreaInventoryHole.Corner(AreaInventoryMode.Own));
    }

    [TestMethod]
    public void TheImageIsAtItsCornerAtHalfItsSize()
    {
        Assert.AreEqual((141, 36), AreaServiceArtLayout.Image);
        Assert.AreEqual((141f, 36f, 40f, 21f), AreaServiceArtLayout.ImageRect(80, 43), "an integer halving of the art's size");
        Assert.AreEqual((141f, 36f, 0f, 0f), AreaServiceArtLayout.ImageRect(1, 1));
    }

    [TestMethod]
    public void TheQuoteLineAndTheImageLieInsideThePdollBackgroundAndClearTheRoundViewAndTheHelmet()
    {
        // The frame is 358 wide on the left (the background); the quote line ends inside it and below the round view's square (11 + 89, 9 + 89).
        var (qx, qy, qw, qh) = AreaServiceArtLayout.QuoteLine;
        Assert.IsTrue(qx + qw <= 358 && qy + qh <= 400, "the quote line is inside the background");
        var (hx, hy) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
        Assert.IsTrue(qx >= hx + AreaInventoryHole.Side, "the quote line starts right of the round view");
        // An image art of up to 128 × 128 pixels (half size 64) ends inside the quote line's columns and above the doll's helmet slot (y 107).
        var helmet = AreaInventoryDrag.SlotRects[EquipmentSlot.Helmet];
        var image = AreaServiceArtLayout.ImageRect(128, 128);
        Assert.IsTrue(image.X + image.Width <= qx + qw && image.Y + image.Height <= helmet.Y, "a 128 × 128 art at half size does not reach the doll's helmet slot");
    }

    [TestMethod]
    public void AnItemUnderThePointIsNamedAndTheBoxesNameNone()
    {
        var view = View();
        Assert.AreEqual("axe", AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 5, AreaInventoryDrag.GridY + 70), "the axe's third cell");
        Assert.AreEqual("ring", AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 32 + 5, AreaInventoryDrag.GridY + 32 + 5));
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 5 * 32, AreaInventoryDrag.GridY + 5 * 32), "an empty cell");
        var helmet = AreaInventoryDrag.SlotRects[EquipmentSlot.Helmet];
        Assert.AreEqual("helm", AreaServiceArtLayout.ItemAt(view, helmet.X + 3, helmet.Y + 3), "a worn item is named too");
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.IconX + 3, AreaInventoryDrag.DropIconY + 3), "the drop box is not made in modes 5 and 6");
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, 5, 380), "outside the grid and the slots");
    }

    [TestMethod]
    public void TheRememberedItemsCellsAreFilledWithTheTooHeavyWeaponsColour()
    {
        var view = View();
        Assert.AreEqual((AreaInventoryDrag.GridX, AreaInventoryDrag.GridY, 32f, 96f), AreaServiceArtLayout.SelectedRect(view, "axe"));
        Assert.AreEqual((AreaInventoryDrag.GridX + 32, AreaInventoryDrag.GridY + 32, 32f, 32f), AreaServiceArtLayout.SelectedRect(view, "ring"));
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, "helm"), "a worn item has no grid cells to fill");
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, null));
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, "gone"));
        Assert.AreEqual(AreaLootArtLayout.TooHeavyFill, AreaServiceArtLayout.SelectedFill);
        Assert.AreEqual((0x40, 0, 0), AreaServiceArtLayout.SelectedFill);
    }
}
