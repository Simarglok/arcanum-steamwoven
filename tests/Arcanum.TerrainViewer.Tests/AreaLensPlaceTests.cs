using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2714: the lens stands at its own place in the Original look and at the one common place with the generated interface.</summary>
[TestClass]
public sealed class AreaLensPlaceTests
{
    [TestCleanup]
    public void Restore() => AreaLensPlace.Unified = false;

    [TestMethod]
    public void The_Original_look_keeps_every_windows_own_place()
    {
        AreaLensPlace.Unified = false;
        Assert.AreEqual((11, 9), AreaInventoryHole.Corner(AreaInventoryMode.Own));
        Assert.AreEqual((16, 17), AreaInventoryHole.Corner(AreaInventoryMode.Barter));
        Assert.AreEqual((16, 17), AreaInventoryHole.Corner(AreaInventoryMode.Loot));
        Assert.AreEqual((12, 10), (AreaCharacterSheetArtLayout.Lens.X, AreaCharacterSheetArtLayout.Lens.Y));
        Assert.AreEqual((25, 24), (AreaLogbookArtLayout.Lens.X, AreaLogbookArtLayout.Lens.Y));
        Assert.AreEqual((25, 24), (AreaWorldMapArtLayout.Lens.X, AreaWorldMapArtLayout.Lens.Y));
        Assert.AreEqual((25, 24), (AreaReadingArtLayout.Lens.X, AreaReadingArtLayout.Lens.Y));
        Assert.AreEqual((50, 26), (AreaSchematicWindowLayout.RoundView.X, AreaSchematicWindowLayout.RoundView.Y));
    }

    [TestMethod]
    public void The_generated_interface_puts_every_lens_at_the_common_place_and_the_inventory_grid_stays_clear_of_it()
    {
        AreaLensPlace.Unified = true;
        var common = (AreaLensPlace.X, AreaLensPlace.Y);
        foreach (var mode in Enum.GetValues<AreaInventoryMode>()) Assert.AreEqual(common, AreaInventoryHole.Corner(mode), mode.ToString());
        Assert.AreEqual(common, (AreaCharacterSheetArtLayout.Lens.X, AreaCharacterSheetArtLayout.Lens.Y));
        Assert.AreEqual(common, (AreaLogbookArtLayout.Lens.X, AreaLogbookArtLayout.Lens.Y));
        Assert.AreEqual(common, (AreaWorldMapArtLayout.Lens.X, AreaWorldMapArtLayout.Lens.Y));
        Assert.AreEqual(common, (AreaReadingArtLayout.Lens.X, AreaReadingArtLayout.Lens.Y));
        Assert.AreEqual(common, (AreaSchematicWindowLayout.RoundView.X, AreaSchematicWindowLayout.RoundView.Y));
        // The place is the inventory window's own (its item grid starts at y 107 under the lens's bottom at y 98, which no lower lens would leave clear), and every other window's lens moves toward the corner only.
        Assert.AreEqual((11, 9), common);
        foreach (var legacy in new[] { (12, 10), (16, 17), (25, 24), (50, 26) }) Assert.IsTrue(legacy.Item1 >= AreaLensPlace.X && legacy.Item2 >= AreaLensPlace.Y, legacy.ToString());
    }
}
