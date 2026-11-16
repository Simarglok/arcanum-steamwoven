using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    public void A_note_is_drawn_with_the_art_of_its_marker_type()
    {
        // ARC-UI-2330 (BM4d table 0x5C9160 rows 0-2, WM79, WM132): 0 note 142, 1 skull 144, 2 chest 141, 3 question 143 (scenery notes); anything else keeps 143.
        Assert.AreEqual(142, AreaWorldMapArtLayout.NoteArtOf(0));
        Assert.AreEqual(144, AreaWorldMapArtLayout.NoteArtOf(1));
        Assert.AreEqual(141, AreaWorldMapArtLayout.NoteArtOf(2));
        Assert.AreEqual(143, AreaWorldMapArtLayout.NoteArtOf(AreaTownNotes.QuestionType));
        Assert.AreEqual(143, AreaWorldMapArtLayout.NoteArtOf(-1));
        Assert.AreEqual(143, AreaWorldMapArtLayout.NoteArtOf(4));
        CollectionAssert.IsSubsetOf(AreaWorldMapArtLayout.NoteTypeArts.ToArray(), AreaWorldMapArtLayout.ArtNumbers.ToArray(), "the view reads every note art");
    }

    [TestMethod]
    public void The_lens_is_the_square_hole_of_the_background_art_and_its_test_is_half_open()
    {
        // ARC-UI-133 (WM36): the registered rectangle (25, 65, 89, 89) of the screen is (25, 24) in the window; 0x551000 keeps the left and top edges
        // in and the right and bottom out, and does not consult the round art.
        Assert.AreEqual((25, 24, 89), AreaWorldMapArtLayout.Lens);
        Assert.IsTrue(AreaWorldMapArtLayout.LensContains(25, 24));
        Assert.IsTrue(AreaWorldMapArtLayout.LensContains(113.9, 112.9));
        Assert.IsTrue(AreaWorldMapArtLayout.LensContains(26, 25), "a corner of the square, outside the round ring, still counts");
        Assert.IsFalse(AreaWorldMapArtLayout.LensContains(24.9, 60));
        Assert.IsFalse(AreaWorldMapArtLayout.LensContains(60, 23.9));
        Assert.IsFalse(AreaWorldMapArtLayout.LensContains(114, 60));
        Assert.IsFalse(AreaWorldMapArtLayout.LensContains(60, 113));
        Assert.IsFalse(AreaWorldMapArtLayout.LensContains(AreaWorldMapArtLayout.View.X + 10, AreaWorldMapArtLayout.View.Y + 10), "the map view is not the lens");
    }

    [TestMethod]
    public void The_scroll_maximum_is_the_picture_minus_the_view()
    {
        Assert.AreEqual((1499, 1635), AreaWorldMapArtLayout.MaxOffset());
        Assert.AreEqual((0, 0), AreaWorldMapArtLayout.MaxOffset(300, 300), "a picture smaller than the view does not scroll");
    }

    [TestMethod]
    public void The_travel_button_is_hidden_in_the_map_view_of_a_teleportation_cast()
    {
        // ARC-MAGIC-1147 (WM62, TP6): 813 is shown in the town map and in the walkable map view, not in the continent view, and never in the scripted open of Teleportation (walkable flag cleared).
        Assert.IsTrue(AreaWorldMapArtLayout.TravelShown(true, false, AreaWorldMapKind.Map));
        Assert.IsFalse(AreaWorldMapArtLayout.TravelShown(false, false, AreaWorldMapKind.Map), "off the start map the view is not walkable");
        Assert.IsFalse(AreaWorldMapArtLayout.TravelShown(true, true, AreaWorldMapKind.Map), "the teleport mode has no journey");
        Assert.IsFalse(AreaWorldMapArtLayout.TravelShown(true, false, AreaWorldMapKind.Continent));
        Assert.IsTrue(AreaWorldMapArtLayout.TravelShown(false, false, AreaWorldMapKind.Town), "the town map shows it on any map");
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
        Assert.AreEqual(new AreaWorldMapTileSet(8, 8, "SmallMapChunks", 1, "Map_Zoomed"), set);
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
        Assert.IsTrue(new AreaWorldMapArtContent(art, tiles, "synthetic").Ready, "the continent picture is no condition (WM62)");
        Assert.IsTrue(new AreaWorldMapArtContent(art, tiles, "synthetic") { Zoomed = Bmp(365, 365) }.Ready);
        tiles[17] = null;
        Assert.IsFalse(new AreaWorldMapArtContent(art, tiles, "synthetic") { Zoomed = Bmp(365, 365) }.Ready, "a missing tile");
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

    [TestMethod]
    public void The_continent_view_maps_the_picture_onto_365_pixels_at_window_218_11()
    {
        Assert.AreEqual((218, 11, 365, 365), AreaWorldMapArtLayout.Zoomed);
        Assert.AreEqual((218, 11), AreaWorldMapArtLayout.ToZoomed(0, 0, 2000));
        Assert.AreEqual((218 + 182, 11 + 182), AreaWorldMapArtLayout.ToZoomed(1000, 1000, 2000), "x × 365 / W");
        Assert.AreEqual((0L, 0L), AreaWorldMapArtLayout.ZoomedToPicture(218, 11, 2000));
        Assert.AreEqual((997L, 997L), AreaWorldMapArtLayout.ZoomedToPicture(218 + 182.5, 11 + 182.5, 2000)!.Value, "(182 × 2000) / 365, integer");
        Assert.IsNull(AreaWorldMapArtLayout.ZoomedToPicture(217, 100, 2000), "the black margin is not the picture");
        Assert.IsNull(AreaWorldMapArtLayout.ZoomedToPicture(300, 376, 2000));
    }

    [TestMethod]
    public void The_readouts_are_the_sector_column_west_and_the_row_south()
    {
        Assert.AreEqual(("1500 W", "400 S"), AreaWorldMapArtLayout.Readouts(500, 400, 2000, 0, 0), "W - (x + longitude offset), y + latitude offset");
        Assert.AreEqual(("1495 W", "412 S"), AreaWorldMapArtLayout.Readouts(500, 400, 2000, 5, 12));
        Assert.AreEqual((707, 159, 60, 18), AreaWorldMapArtLayout.ReadoutWest);
        Assert.AreEqual((707, 191, 60, 18), AreaWorldMapArtLayout.ReadoutSouth);
    }

    [TestMethod]
    public void The_newest_marker_within_the_hover_box_wins()
    {
        var markers = new (long X, long Y)[] { (100, 100), (105, 100), (300, 300) };
        Assert.AreEqual(1, AreaWorldMapArtLayout.HitMarker(markers, 102, 100, AreaWorldMapArtLayout.HoverHalf), "both in the ±9 box: the last wins");
        Assert.AreEqual(0, AreaWorldMapArtLayout.HitMarker(markers, 91, 100, AreaWorldMapArtLayout.HoverHalf), "only the first within 9");
        Assert.AreEqual(-1, AreaWorldMapArtLayout.HitMarker(markers, 90, 100, AreaWorldMapArtLayout.HoverHalf));
        Assert.AreEqual(2, AreaWorldMapArtLayout.HitMarker(markers, 320, 280, AreaWorldMapArtLayout.ZoomedHoverHalf), "±20 in the continent view, inclusive");
    }

    [TestMethod]
    public void A_waypoint_is_hit_in_half_the_area_arts_size_plus_five_and_the_last_wins()
    {
        // WM102: half-open box, hx = width / 2 + 5, hy = height / 2 + 5 (signed halving toward 0); art 205 is taken as 20 × 20 here (invented).
        var waypoints = new (long X, long Y)[] { (100, 100), (110, 100), (300, 300) };
        Assert.AreEqual(1, AreaWorldMapArtLayout.HitWaypoint(waypoints, 105, 100, 20, 20), "both boxes hold it: the last wins");
        Assert.AreEqual(0, AreaWorldMapArtLayout.HitWaypoint(waypoints, 85, 100, 20, 20), "100 − 15 is inside");
        Assert.AreEqual(-1, AreaWorldMapArtLayout.HitWaypoint(waypoints, 84, 100, 20, 20));
        Assert.AreEqual(0, AreaWorldMapArtLayout.HitWaypoint([(100, 100)], 114, 100, 20, 20), "100 + 15 is outside, 114 inside");
        Assert.AreEqual(-1, AreaWorldMapArtLayout.HitWaypoint([(100, 100)], 115, 100, 20, 20));
        Assert.AreEqual(-1, AreaWorldMapArtLayout.HitWaypoint([(100, 100)], 100, 115, 20, 20));
        Assert.AreEqual(0, AreaWorldMapArtLayout.HitWaypoint([(100, 100)], 100, 114, 20, 20));
        Assert.AreEqual(0, AreaWorldMapArtLayout.HitWaypoint([(100, 100)], 100, 100, 0, 0), "a 5 px box still holds the centre");
        Assert.AreEqual(-1, AreaWorldMapArtLayout.HitWaypoint([], 1, 1, 20, 20));
    }

    [TestMethod]
    public void A_drag_scrolls_the_view_within_23_px_of_its_edge_and_not_outside_it()
    {
        var view = AreaWorldMapArtLayout.View;
        Assert.AreEqual((-1, -1), AreaWorldMapArtLayout.DragScroll(view.X + 100, view.Y + 100));
        Assert.AreEqual((0, -1), AreaWorldMapArtLayout.DragScroll(view.X + 100, view.Y + 22), "north");
        Assert.AreEqual((-1, -1), AreaWorldMapArtLayout.DragScroll(view.X + 100, view.Y + 23));
        Assert.AreEqual((4, -1), AreaWorldMapArtLayout.DragScroll(view.X + 100, view.Y + view.Height - 23), "south");
        Assert.AreEqual((-1, 6), AreaWorldMapArtLayout.DragScroll(view.X + 22, view.Y + 100), "west");
        Assert.AreEqual((-1, 2), AreaWorldMapArtLayout.DragScroll(view.X + view.Width - 23, view.Y + 100), "east");
        Assert.AreEqual((0, 6), AreaWorldMapArtLayout.DragScroll(view.X, view.Y), "a corner scrolls both axes");
        Assert.AreEqual((-1, -1), AreaWorldMapArtLayout.DragScroll(view.X - 1, view.Y + 5), "outside the view: nothing");
        Assert.AreEqual((-1, -1), AreaWorldMapArtLayout.DragScroll(view.X + view.Width, view.Y + 5));
        Assert.AreEqual(((byte)160, (byte)50, (byte)50), AreaWorldMapArtLayout.RouteColour);
        Assert.AreEqual(((byte)50, (byte)50, (byte)110), AreaWorldMapArtLayout.SelectedRouteColour);
    }

    [TestMethod]
    public void A_label_starts_12_right_and_10_above_the_marker_plus_the_name_offsets()
    {
        // WM49: the buffer is placed at the marker minus (10, 10) plus the offsets and its text starts 22 inside it.
        Assert.AreEqual((500 - 100 + 12, 300 - 50 - 10), AreaWorldMapArtLayout.LabelAt(500, 300, (100, 50), 0, 0));
        Assert.AreEqual((400 + 12 - 30, 250 - 10 + 8), AreaWorldMapArtLayout.LabelAt(500, 300, (100, 50), -30, 8), "signed offsets, no scale");
    }

    [TestMethod]
    public void The_texts_and_colours_of_worldmap_mes_are_read_with_defaults()
    {
        Assert.AreEqual(((byte)254, (byte)248, (byte)236), AreaWorldMapArtContent.ParseColour("254, 248, 236"));
        Assert.IsNull(AreaWorldMapArtContent.ParseColour("254, 248"));
        Assert.IsNull(AreaWorldMapArtContent.ParseColour("256, 0, 0"));
        Assert.AreEqual((0, 0), AreaWorldMapArtContent.ParsePair("0, 0"));
        Assert.AreEqual((-3, 7), AreaWorldMapArtContent.ParsePair("-3, 7"));
        Assert.IsNull(AreaWorldMapArtContent.ParsePair("x"));
        var content = AreaWorldMapArtContent.Empty("none");
        Assert.AreEqual(AreaWorldMapArtLayout.DefaultAreaColour, content.AreaColour, "no entry 655: (250, 250, 250)");
        Assert.AreEqual("Action", content.Text(AreaWorldMapArtContent.ActionText, "Action"));
        var read = content with { Texts = new Dictionary<int, string> { [655] = "254, 248, 236" } };
        Assert.AreEqual(((byte)254, (byte)248, (byte)236), read.AreaColour);
        Assert.AreEqual("Continent View", AreaWorldMapTileSet.Parse("8, 8, SmallMapChunks, ZoomedName: Map_Zoomed, MapKeyedTo: 1") is { ZoomedName: "Map_Zoomed" } ? "Continent View" : "");
    }

    [TestMethod]
    public void A_marker_description_is_written_at_once_and_a_pane_refuses_it()
    {
        // WM60: 0x550770 replaces the message area directly: no 3 s quiet time, not stored.
        var log = new AreaMessageLog();
        log.Post(AreaMessageEntry.Text(AreaMessageKind.Message, "stored"), 1000);
        var description = AreaMessageEntry.Text(AreaMessageKind.Hover, "Synthetic Place");
        Assert.IsFalse(log.Post(description, 1100).Shown, "a hover entry waits out the quiet time");
        Assert.IsTrue(log.ShowTransient(description));
        Assert.AreEqual(description, log.Shown);
        log.ClearHover();
        Assert.AreNotEqual(description, log.Shown);
        log.EnterPane();
        Assert.IsFalse(log.ShowTransient(description), "a pane owns the window");
    }
}
