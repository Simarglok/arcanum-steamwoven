using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-WORLD-2792 (ledger RF10): a roof covers a body by the tile it stands on now. An NPC the AI host walked out of a roofed building (Farkhus, stored off in the map data and switched on by his heartbeat) is drawn outdoors, and one that
/// walked in is hidden, whatever its T0 tile says.</summary>
[TestClass]
public sealed class AreaRoofCoverTests
{
    private static readonly AreaSectorKey Sector = new("s", 1, 0);
    private static readonly Func<long, long, bool> Indoor = (_, _) => true;

    private static AreaObjectKey Key(int index) => new("s", 1, default, null, index, 0, "id" + index, 0);

    // One centre roof piece (8) at block 64,64: it covers the tiles 61..64 on both axes.
    private static AreaRoofGrid Grid(bool fill = false, bool fade = false)
    {
        var raw = (10u << 28) | (8u << 14) | (fill ? 0x2000u : 0) | (fade ? 0x1000u : 0);
        return new AreaRoofGrid([new AreaRoofRecord(Sector, 0, 64, 64, raw, 8, fill, fade, new(AreaSourceKind.Terrain, Sector, 4096, string.Empty, AreaLayerKind.Roof))]);
    }

    [TestMethod]
    public void A_body_that_walked_out_of_the_roofed_house_is_no_longer_covered()
    {
        var walker = Key(1); var stayer = Key(2);
        var covered = new HashSet<AreaObjectKey> { walker, stayer };   // both stand under the roof at T0
        AreaRoofCover.Place(covered, [(walker, 70L, 70L)], Grid(), Indoor);
        CollectionAssert.AreEquivalent(new[] { stayer }, covered.ToArray(), "the one outside is drawn, the one that did not move stays covered");
    }

    [TestMethod]
    public void A_body_that_walked_into_the_roofed_house_is_covered_although_its_T0_tile_is_open()
    {
        var walker = Key(1);
        var covered = new HashSet<AreaObjectKey>();
        AreaRoofCover.Place(covered, [(walker, 62L, 63L)], Grid(), Indoor);
        CollectionAssert.AreEquivalent(new[] { walker }, covered.ToArray());
    }

    [TestMethod]
    public void A_faded_roof_still_covers_the_body_and_a_filled_one_covers_nothing()
    {
        var walker = Key(1);
        var faded = new HashSet<AreaObjectKey>();
        AreaRoofCover.Place(faded, [(walker, 62L, 62L)], Grid(fade: true), Indoor);
        Assert.IsTrue(faded.Contains(walker), "a faded roof still hides objects (RF10)");
        var filled = new HashSet<AreaObjectKey> { walker };
        AreaRoofCover.Place(filled, [(walker, 62L, 62L)], Grid(fill: true), Indoor);
        Assert.IsFalse(filled.Contains(walker), "the roof of the building the hero stands in covers nothing");
    }

    [TestMethod]
    public void An_outdoor_tile_under_the_roof_block_covers_nothing()
    {
        var walker = Key(1);
        var covered = new HashSet<AreaObjectKey> { walker };
        AreaRoofCover.Place(covered, [(walker, 62L, 62L)], Grid(), (_, _) => false);
        Assert.AreEqual(0, covered.Count);
    }
}
