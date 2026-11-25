using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2714: the lens stands at its own place in the Original look and at the one common place with the generated interface.</summary>
[TestClass]
public sealed class AreaLensPlaceTests
{
    [TestCleanup]
    public void Restore() { AreaLensPlace.Unified = false; AreaLensPlace.Surface = null; }

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

    /// <summary>ARC-UI-3101: a kit surface that carries its lens ring puts the lens at the circle measured inside the ring (46 units round the ring's centre), whatever the common place is; the window then draws no ring art.</summary>
    [TestMethod]
    public void A_kit_surface_with_a_ring_puts_the_lens_at_its_own_circle_and_the_window_draws_no_ring()
    {
        Assert.IsFalse(AreaLensPlace.SurfaceRing);
        Assert.AreEqual(89, AreaLensPlace.Side);
        // The centres measured on the built surfaces, in window units (the table rounds the 46-unit circle's corner to whole units: the centre is within half a unit).
        var measured = new Dictionary<SteamwovenKitWindow, (double X, double Y)>
        {
            [SteamwovenKitWindow.Inventory] = (55.87, 51.62), [SteamwovenKitWindow.Character] = (56.71, 52.61),
            [SteamwovenKitWindow.Logbook] = (69.83, 66.80), [SteamwovenKitWindow.Schematic] = (94.79, 68.78),
        };
        foreach (var (window, centre) in measured)
        {
            var corner = AreaKitLens.CornerOf(window) ?? throw new AssertFailedException(window.ToString());
            Assert.IsTrue(Math.Abs(corner.X + AreaLensPlace.SurfaceSide / 2.0 - centre.X) <= .5 && Math.Abs(corner.Y + AreaLensPlace.SurfaceSide / 2.0 - centre.Y) <= .5, window.ToString());
        }
        // The loot, barter and map backgrounds and no kit carry no ring of the surfaces' kind.
        foreach (var window in new SteamwovenKitWindow?[] { SteamwovenKitWindow.Loot, SteamwovenKitWindow.WorldMap, SteamwovenKitWindow.Barter, SteamwovenKitWindow.BarterFollower, null }) Assert.IsNull(AreaKitLens.CornerOf(window), window.ToString());

        AreaLensPlace.Unified = true;
        AreaLensPlace.Surface = AreaKitLens.CornerOf(SteamwovenKitWindow.Logbook);
        Assert.IsTrue(AreaLensPlace.SurfaceRing);
        Assert.AreEqual(AreaLensPlace.SurfaceSide, AreaLensPlace.Side);
        Assert.AreEqual((24, 21), (AreaLogbookArtLayout.Lens.X, AreaLogbookArtLayout.Lens.Y));
        Assert.AreEqual(AreaLensPlace.SurfaceSide, AreaLogbookArtLayout.Lens.Side);
        AreaLensPlace.Surface = AreaKitLens.CornerOf(SteamwovenKitWindow.Schematic);
        Assert.AreEqual((49, 23, 92, 92), AreaSchematicWindowLayout.RoundView);
        AreaLensPlace.Surface = AreaKitLens.CornerOf(SteamwovenKitWindow.Inventory);
        Assert.IsTrue(AreaInventoryHole.Corner(AreaInventoryMode.Own).Y + AreaLensPlace.SurfaceSide <= 107);
        // The whole circle's square is inside the lens's click rectangle, one unit beyond it is not.
        var (left, top) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
        Assert.IsTrue(AreaInventoryHole.Contains(AreaInventoryMode.Own, left + 91, top + 91));
        Assert.IsFalse(AreaInventoryHole.Contains(AreaInventoryMode.Own, left + 92, top));
    }
}
