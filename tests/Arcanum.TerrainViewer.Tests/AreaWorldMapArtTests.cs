using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-073: the world-map window's original look (interface mode 5, ledger WM1-WM39). Synthetic data only.
[TestClass]
public sealed class AreaWorldMapArtTests
{
    private static byte[] Bmp(int width, int height, int bits = 8, int compression = 0)
    {
        var bytes = new byte[54 + 1024];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)bits).CopyTo(bytes, 28);
        BitConverter.GetBytes(compression).CopyTo(bytes, 30);
        return bytes;
    }

    [TestMethod]
    public void The_buttons_and_the_view_stand_where_the_window_table_puts_them()
    {
        // WM9: window coordinates = the executable's screen coordinates minus the window's 41 rows.
        Assert.AreEqual((811, 20, 165), AreaWorldMapArtLayout.Button(AreaWorldMapArtButton.Zoom));
        Assert.AreEqual((812, 19, 267), AreaWorldMapArtLayout.Button(AreaWorldMapArtButton.Whole));
        Assert.AreEqual((813, 698, 312), AreaWorldMapArtLayout.Button(AreaWorldMapArtButton.Travel));
        Assert.AreEqual((139, 375, 347), AreaWorldMapArtLayout.Button(AreaWorldMapArtButton.Compass));
        Assert.AreEqual((150, 11, 501, 365), AreaWorldMapArtLayout.View);
        CollectionAssert.Contains(AreaWorldMapArtLayout.ArtNumbers.ToList(), 138);
        Assert.AreEqual(AreaWorldMapArtLayout.ArtNumbers.Count, AreaWorldMapArtLayout.ArtNumbers.Distinct().Count());
    }

    [TestMethod]
    public void The_scroll_maximum_is_the_picture_minus_the_view()
    {
        Assert.AreEqual((1499, 1635), AreaWorldMapArtLayout.MaxOffset());
        Assert.AreEqual((0, 0), AreaWorldMapArtLayout.MaxOffset(300, 300), "a picture smaller than the view does not scroll");
    }

    [TestMethod]
    public void Centring_clamps_to_the_scroll_maximum_where_the_original_shows_blank_fill()
    {
        var max = AreaWorldMapArtLayout.MaxOffset();
        // WM13: the original clamps the upper end at 2000, so a party at the south-east corner would centre at 1750 / 1818 (blank fill).
        Assert.AreEqual(max, AreaWorldMapArtLayout.CentreOn(1999, 1999, max));
        Assert.AreEqual((0, 0), AreaWorldMapArtLayout.CentreOn(10, 10, max), "below 0 it is 0");
        Assert.AreEqual((1000 - 250, 1000 - 182), AreaWorldMapArtLayout.CentreOn(1000, 1000, max), "offset = point - half the view");
    }

    [TestMethod]
    public void The_arrow_keys_give_the_executable_s_eight_directions_and_the_first_key_of_a_pair_wins()
    {
        Assert.AreEqual(-1, AreaWorldMapArtLayout.Direction(false, false, false, false));
        Assert.AreEqual(0, AreaWorldMapArtLayout.Direction(false, false, true, false));
        Assert.AreEqual(2, AreaWorldMapArtLayout.Direction(false, true, false, false));
        Assert.AreEqual(4, AreaWorldMapArtLayout.Direction(false, false, false, true));
        Assert.AreEqual(6, AreaWorldMapArtLayout.Direction(true, false, false, false));
        Assert.AreEqual(7, AreaWorldMapArtLayout.Direction(true, false, true, false));
        Assert.AreEqual(3, AreaWorldMapArtLayout.Direction(false, true, false, true));
        Assert.AreEqual(6, AreaWorldMapArtLayout.Direction(true, true, false, false), "left beats right (WM18)");
        Assert.AreEqual(0, AreaWorldMapArtLayout.Direction(false, false, true, true), "up beats down (WM18)");
    }

    [TestMethod]
    public void A_scroll_step_moves_each_moving_axis_and_stops_at_the_edges()
    {
        var max = AreaWorldMapArtLayout.MaxOffset();
        Assert.AreEqual((80, 80), AreaWorldMapArtLayout.Scroll((100, 100), 7, AreaWorldMapArtLayout.KeyStep, max), "a diagonal moves 20 px on both axes");
        Assert.AreEqual((120, 100), AreaWorldMapArtLayout.Scroll((100, 100), 2, 20, max));
        Assert.AreEqual((0, 0), AreaWorldMapArtLayout.Scroll((5, 5), 7, 40, max), "clamped at the north-west edge");
        Assert.AreEqual(max, AreaWorldMapArtLayout.Scroll((1490, 1630), 3, 40, max), "clamped at the south-east edge");
        Assert.AreEqual((100, 100), AreaWorldMapArtLayout.Scroll((100, 100), -1, 20, max), "no direction, no step");
    }

    [TestMethod]
    public void The_view_touches_only_the_tiles_it_shows()
    {
        Assert.AreEqual((0, 2, 0, 1), AreaWorldMapArtLayout.VisibleTiles((0, 0)), "501 x 365 from the corner: three columns, two rows");
        Assert.AreEqual((1, 3, 0, 1), AreaWorldMapArtLayout.VisibleTiles((250, 0)));
        Assert.AreEqual((5, 7, 6, 7), AreaWorldMapArtLayout.VisibleTiles((1499, 1635)), "the end of the picture: the last tiles");
    }

    [TestMethod]
    public void Tile_files_are_numbered_by_row_and_column_and_the_picture_pixel_follows_the_offset()
    {
        Assert.AreEqual("worldmap/smallmapchunks001.bmp", AreaWorldMapArtLayout.TilePath("SmallMapChunks", 0, 0));
        Assert.AreEqual("worldmap/smallmapchunks012.bmp", AreaWorldMapArtLayout.TilePath("SmallMapChunks", 3, 1));
        Assert.AreEqual("worldmap/smallmapchunks064.bmp", AreaWorldMapArtLayout.TilePath("SmallMapChunks", 7, 7));
        Assert.AreEqual((150 + 250 - 30, 11 + 500 - 10), AreaWorldMapArtLayout.ToWindow(250, 500, (30, 10)), "tile (c, r) is drawn at (150 + 250 c - offset x, 11 + 250 r - offset y) in the window");
        Assert.AreEqual((550L, 110L), AreaWorldMapArtLayout.ToPicture(150, 11, (550, 110)), "the view's top-left is the offset");
        Assert.AreEqual((500L + 100, 364L + 100), AreaWorldMapArtLayout.ToPicture(150 + 500, 11 + 364, (100, 100)));
        Assert.IsNull(AreaWorldMapArtLayout.ToPicture(149, 60, (0, 0)));
        Assert.IsNull(AreaWorldMapArtLayout.ToPicture(151, 376, (0, 0)), "the view's bottom edge is outside");
    }

    [TestMethod]
    public void The_tile_set_reads_the_keyed_map_s_entry_of_worldmap_mes()
    {
        var set = AreaWorldMapTileSet.Parse("8, 8, SmallMapChunks, ZoomedName: Map_Zoomed, MapKeyedTo: 1");
        Assert.AreEqual(new AreaWorldMapTileSet(8, 8, "SmallMapChunks", 1), set);
        Assert.IsNull(AreaWorldMapTileSet.Parse("8, x, Name"));
        Assert.IsNull(AreaWorldMapTileSet.Parse("0, 8, Name"));
        Assert.IsNull(AreaWorldMapTileSet.Parse("8, 8"));
    }

    [TestMethod]
    public void A_tile_is_a_plain_8_bit_bitmap_of_250_by_250()
    {
        Assert.IsTrue(AreaWorldMapArtContent.IsTile(Bmp(250, 250)));
        Assert.IsTrue(AreaWorldMapArtContent.IsTile(Bmp(250, -250)), "top-down");
        Assert.IsFalse(AreaWorldMapArtContent.IsTile(Bmp(251, 250)));
        Assert.IsFalse(AreaWorldMapArtContent.IsTile(Bmp(250, 250, bits: 24)));
        Assert.IsFalse(AreaWorldMapArtContent.IsTile(Bmp(250, 250, compression: 1)));
        Assert.IsFalse(AreaWorldMapArtContent.IsTile(new byte[10]));
    }

    [TestMethod]
    public void The_look_is_ready_only_with_all_its_art_and_all_64_tiles()
    {
        var art = AreaWorldMapArtLayout.ArtNumbers.ToDictionary(number => number,
            number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(2, 2, new byte[16])], "synthetic"));
        var tiles = Enumerable.Range(0, 64).Select(_ => (byte[]?)Bmp(250, 250)).ToArray();
        Assert.IsTrue(new AreaWorldMapArtContent(art, tiles, "synthetic").Ready);
        tiles[17] = null;
        Assert.IsFalse(new AreaWorldMapArtContent(art, tiles, "synthetic").Ready, "a missing tile");
        Assert.IsFalse(new AreaWorldMapArtContent(art, tiles[..10], "synthetic").TilesReady);
        Assert.IsFalse(AreaWorldMapArtContent.Empty("none").Ready);
    }

    [TestMethod]
    public void The_compass_scrolls_outside_its_dead_zone_only()
    {
        Assert.AreEqual(-1, AreaWorldMapArtLayout.CompassDirection(395, 365), "inside the dead zone");
        Assert.AreEqual(6, AreaWorldMapArtLayout.CompassDirection(380, 365), "left of it");
        Assert.AreEqual(2, AreaWorldMapArtLayout.CompassDirection(410, 365), "right of it");
        Assert.AreEqual(0, AreaWorldMapArtLayout.CompassDirection(395, 350), "above it");
        Assert.AreEqual(4, AreaWorldMapArtLayout.CompassDirection(395, 385), "below it");
        Assert.AreEqual(7, AreaWorldMapArtLayout.CompassDirection(380, 350), "up-left diagonal");
    }
}
