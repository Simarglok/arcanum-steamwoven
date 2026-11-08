using System.Text;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-084: the town map of the world-map window (kind 2, ledger WM65-WM94). Synthetic data only.
[TestClass]
public sealed class AreaTownMapArtTests
{
    private static byte[] InfoRecord(int version = 2, int number = 99, int width = 5120, int height = 2560, int columns = 20, int rows = 20,
        int originX = 96, int originY = 96, float scale = .25f)
    {
        var bytes = new byte[48];
        BitConverter.GetBytes(version).CopyTo(bytes, 0);
        BitConverter.GetBytes(number).CopyTo(bytes, 8);
        BitConverter.GetBytes(width).CopyTo(bytes, 12);
        BitConverter.GetBytes(height).CopyTo(bytes, 16);
        BitConverter.GetBytes(columns).CopyTo(bytes, 20);
        BitConverter.GetBytes(rows).CopyTo(bytes, 24);
        BitConverter.GetBytes(((long)originY << 32) | (uint)originX).CopyTo(bytes, 32);
        BitConverter.GetBytes(scale).CopyTo(bytes, 40);
        return bytes;
    }

    private static AreaTownMapPlan Plan(bool whole = false, int columns = 20, int rows = 20) =>
        new(new(7, "Test Town", whole), AreaTownMapInfo.Parse(InfoRecord(columns: columns, rows: rows))!, 64, 32);

    [TestMethod]
    public void A_clicked_pixel_gives_the_tile_whose_diamond_it_lies_in_and_a_corner_pixel_the_tile_one_row_up()
    {
        // WM110, WM113: the town's origin (96, 96) is at the picture's middle (2560, 1280) of the full picture = pixel (640, 320) at scale 0.25.
        var info = AreaTownMapInfo.Parse(InfoRecord())!;
        Assert.AreEqual((96L, 95L), AreaTownMapLayout.ToLocation(info, 640, 320), "the forward pixel of (96, 96) is its left corner");
        Assert.AreEqual((96L, 96L), AreaTownMapLayout.ToLocation(info, 650, 325), "its diamond's centre, (10, 5) picture pixels on");
        foreach (var (x, y) in new[] { (97L, 96L), (96L, 97L), (100L, 90L), (80L, 120L), (96L, 96L) })
        {
            var (px, py) = AreaTownMapLayout.ToPixel(info, x, y);
            Assert.AreEqual((x, y), AreaTownMapLayout.ToLocation(info, px + 10, py + 5), $"the centre of ({x}, {y})");
            Assert.AreEqual((x, y), AreaTownMapLayout.ToLocation(info, px + 12, py + 6), $"a pixel inside it ({x}, {y})");
        }
        // No clamp to the picture, and a negative quotient takes one more (the executable's division): u = 1640, v = 20 gives (76, 116).
        Assert.AreEqual((76L, 116L), AreaTownMapLayout.ToLocation(info, 1050, 325));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaTownMapLayout.ToLocation(info with { Scale = 0 }, 1, 1));
        // The division is of the whole coordinate (40 × origin + the step, WM111): pixel (640, 305) makes the x step exactly -40 and the y step -80, which for an origin of 96 is a plain
        // floor of 3800 / 40 and 3760 / 40; only a negative total takes the executable's extra one (and the host refuses such a tile as outside the map).
        Assert.AreEqual((95L, 94L), AreaTownMapLayout.ToLocation(info, 640, 305));
        var small = AreaTownMapInfo.Parse(InfoRecord(originX: 1, originY: 1))!;
        Assert.AreEqual((0L, -2L), AreaTownMapLayout.ToLocation(small, 640, 305), "x: -40 + 40 = 0; y: -80 + 40 = -40 divides to -1 and the negative quirk takes one more");
        Assert.AreEqual((-1L, -2L), AreaTownMapLayout.ToLocation(small, 640, 300));
    }

    [TestMethod]
    public void A_list_line_is_cut_at_the_whole_flag_and_names_a_folder()
    {
        // WM66: the text is cut at "[w:" in either case, trailing blanks removed; the flag is 1 when the first non-blank after it is 1.
        Assert.AreEqual(new AreaTownMapEntry(2, "crash-site", true), AreaTownMapTable.ParseEntry(2, "crash-site [w:1]"));
        Assert.AreEqual(new AreaTownMapEntry(3, "Shrouded Hills", true), AreaTownMapTable.ParseEntry(3, "Shrouded Hills [W: 1]"));
        Assert.AreEqual(new AreaTownMapEntry(4, "Dungeon", false), AreaTownMapTable.ParseEntry(4, "Dungeon  [w:0]"));
        Assert.AreEqual(new AreaTownMapEntry(5, "Plain Name", false), AreaTownMapTable.ParseEntry(5, "Plain Name   "));
        // Number 0 is <none>; a name that could leave its folder is refused.
        Assert.IsNull(AreaTownMapTable.ParseEntry(0, "<none>"));
        Assert.IsNull(AreaTownMapTable.ParseEntry(6, "../escape"));
        Assert.IsNull(AreaTownMapTable.ParseEntry(7, "a/b"));
        Assert.IsNull(AreaTownMapTable.ParseEntry(8, "a\\b [w:1]"));
        Assert.IsNull(AreaTownMapTable.ParseEntry(9, " [w:1]"));
    }

    [TestMethod]
    public void The_list_file_is_read_by_its_numbers()
    {
        var table = AreaTownMapTable.Parse(Encoding.Latin1.GetBytes("{0}{<none>}\n{1}{Town-Map Empty}\n{2}{crash-site [w:1]}\n{21}{Tarant [w:1]}\n"));
        Assert.IsNull(table.Find(0));
        Assert.AreEqual(3, table.Count);
        Assert.AreEqual("Town-Map Empty", table.Find(1)!.Name);
        Assert.IsTrue(table.Find(21)!.Whole);
        Assert.IsNull(table.Find(22));
        Assert.AreEqual(0, AreaTownMapTable.Parse("not a mes file {"u8).Count);
    }

    [TestMethod]
    public void The_info_record_is_read_with_the_original_refusals()
    {
        var info = AreaTownMapInfo.Parse(InfoRecord())!;
        Assert.AreEqual(new AreaTownMapInfo(99, 5120, 2560, 20, 20, 96, 96, .25), info);
        // WM69: a short read and a version other than 2 fail; sizes outside the limits are refused here.
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord()[..47]));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(version: 1)));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(columns: 0)));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(width: 1 << 20)));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(columns: 1024, rows: 1024)));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(scale: 0)));
        Assert.IsNull(AreaTownMapInfo.Parse(InfoRecord(scale: float.NaN)));
        // The origin's two halves are x (low) and y (high), signed.
        var negative = AreaTownMapInfo.Parse(InfoRecord(originX: -5, originY: 7))!;
        Assert.AreEqual((-5L, 7L), (negative.OriginX, negative.OriginY));
    }

    [TestMethod]
    public void A_location_lands_from_the_pictures_middle()
    {
        // WM70: the origin is the centre (2560, 1280 full pixels, 640 x 320 at 0.25); a tile east is (-40, +20) full pixels away.
        var info = AreaTownMapInfo.Parse(InfoRecord())!;
        Assert.AreEqual((640, 320), AreaTownMapLayout.ToPixel(info, 96, 96));
        Assert.AreEqual((630, 325), AreaTownMapLayout.ToPixel(info, 97, 96));
        Assert.AreEqual((650, 325), AreaTownMapLayout.ToPixel(info, 96, 97));
        Assert.AreEqual((640, 330), AreaTownMapLayout.ToPixel(info, 97, 97));
        // Outside the full picture the pixel stays (0, 0) (WM70, WM81).
        Assert.AreEqual((0, 0), AreaTownMapLayout.ToPixel(info, 96 - 200, 96));
        Assert.AreEqual((0, 0), AreaTownMapLayout.ToPixel(info, 5000, 5000));
    }

    [TestMethod]
    public void A_walk_reveals_the_tile_under_the_location()
    {
        // WM78: (y x rows / height) x columns + x x columns / width of the full pixel (2560 x 1280 at the origin of a 5120 x 2560 picture of 20 x 20 tiles).
        var info = AreaTownMapInfo.Parse(InfoRecord())!;
        Assert.AreEqual((2560L, 1280L), AreaTownMapLayout.FullPixel(info, 96, 96));
        Assert.AreEqual(10 * 20 + 10, AreaTownMapLayout.CellOf(info, 96, 96));
        // A tile east: full (2520, 1300) -> column 9 (9.84), row 10 (10.15).
        Assert.AreEqual(10 * 20 + 9, AreaTownMapLayout.CellOf(info, 97, 96));
        // Outside the picture there is no cell.
        Assert.IsNull(AreaTownMapLayout.CellOf(info, 96 - 200, 96));
        Assert.IsNull(AreaTownMapLayout.FullPixel(info, 5000, 5000));
    }

    [TestMethod]
    public void The_scroll_range_and_the_visible_tiles_follow_the_picture()
    {
        // WM72: the picture is columns x tile width by rows x tile height; the scroll maxima are the picture minus the 501 x 365 view.
        var plan = Plan();
        Assert.AreEqual((1280, 640), (plan.PictureWidth, plan.PictureHeight));
        Assert.AreEqual((779, 275), AreaTownMapLayout.MaxOffset(plan));
        Assert.AreEqual((0, 0), AreaTownMapLayout.MaxOffset(Plan(columns: 4, rows: 4)));
        // WM77: from offset / tile to that plus view / tile plus 2 (columns) or 3 (rows), capped at the grid.
        Assert.AreEqual((0, 9, 0, 14), AreaTownMapLayout.VisibleTiles(plan, (0, 0)));
        Assert.AreEqual((12, 20, 8, 20), AreaTownMapLayout.VisibleTiles(plan, (779, 275)));
        Assert.AreEqual((0, 4, 0, 4), AreaTownMapLayout.VisibleTiles(Plan(columns: 4, rows: 4), (0, 0)));
    }

    [TestMethod]
    public void A_tile_is_plain_faded_or_black_by_its_revealed_neighbours()
    {
        // WM77: a whole map blits every tile plainly; otherwise a revealed tile does, an unrevealed one fades beside a revealed neighbour and the rest are black.
        static bool Only(int index, params int[] set) => set.Contains(index);
        Assert.AreEqual(AreaTownMapTileState.Plain, AreaTownMapLayout.State(true, 5, 5, 2, 2, _ => false, out _));
        Assert.AreEqual(AreaTownMapTileState.Plain, AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 12), out var none));
        Assert.AreEqual(0, none);
        Assert.AreEqual(AreaTownMapTileState.Black, AreaTownMapLayout.State(false, 5, 5, 4, 4, i => Only(i, 0), out var far));
        Assert.AreEqual(0, far);
        // The west neighbour (1, 2) lights both left corners (top left 8, bottom left 1).
        Assert.AreEqual(AreaTownMapTileState.Faded, AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 11), out var west));
        Assert.AreEqual(9, west);
        // The north-west neighbour lights the top left corner alone, the south-east one the bottom right.
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 6), out var northWest);
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 18), out var southEast);
        Assert.AreEqual((8, 2), (northWest, southEast));
        // North 0xC, east 6, south 3; several neighbours add up.
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 7), out var north);
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 13), out var east);
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 17), out var south);
        Assert.AreEqual((0xC, 6, 3), (north, east, south));
        AreaTownMapLayout.State(false, 5, 5, 2, 2, i => Only(i, 7, 13), out var both);
        Assert.AreEqual(0xE, both);
        // At the grid's edge the missing neighbours are skipped (and the row wraps nowhere).
        Assert.AreEqual(AreaTownMapTileState.Black, AreaTownMapLayout.State(false, 5, 5, 4, 0, i => Only(i, 5), out var edge));
        Assert.AreEqual(AreaTownMapTileState.Faded, AreaTownMapLayout.State(false, 5, 5, 4, 0, i => Only(i, 9), out var below));
        Assert.AreEqual((0, 3), (edge, below));
    }

    [TestMethod]
    public void Tile_and_info_paths_name_the_towns_folder_in_lower_case()
    {
        Assert.AreEqual("townmap/crash-site/crash-site000012.bmp", AreaTownMapLayout.TilePath("Crash-Site", 12));
        Assert.AreEqual("townmap/crash-site/crash-site000000.bmp", AreaTownMapLayout.TilePath("crash-site", 0));
        Assert.AreEqual("townmap/shrouded hills/shrouded hills.tmi", AreaTownMapLayout.InfoPath("Shrouded Hills"));
    }

    [TestMethod]
    public void A_tile_must_be_a_plain_eight_bit_bitmap_of_a_sane_size()
    {
        static byte[] Header(int width, int height, int bits = 8, int compression = 0)
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
        Assert.IsTrue(AreaTownMapLayout.TryTileSize(Header(64, 32), out var width, out var height));
        Assert.AreEqual((64, 32), (width, height));
        Assert.IsTrue(AreaTownMapLayout.TryTileSize(Header(64, -32), out _, out var topDown));
        Assert.AreEqual(32, topDown);
        Assert.IsFalse(AreaTownMapLayout.TryTileSize(Header(64, 32, bits: 24), out _, out _));
        Assert.IsFalse(AreaTownMapLayout.TryTileSize(Header(64, 32, compression: 1), out _, out _));
        Assert.IsFalse(AreaTownMapLayout.TryTileSize(Header(0, 32), out _, out _));
        Assert.IsFalse(AreaTownMapLayout.TryTileSize(Header(4096, 32), out _, out _));
        Assert.IsFalse(AreaTownMapLayout.TryTileSize(new byte[10], out _, out _));
    }

    [TestMethod]
    public void The_town_view_converts_a_window_point_at_one_to_one()
    {
        // WM84: the kind-2 converter is the kind-0 formula at 1 : 1; outside the view there is no pixel.
        Assert.AreEqual((100L, 50L), AreaTownMapLayout.ToPicture(150 + 100, 11 + 50, (0, 0)));
        Assert.AreEqual((140L, 120L), AreaTownMapLayout.ToPicture(150 + 100, 11 + 50, (40, 70)));
        Assert.IsNull(AreaTownMapLayout.ToPicture(149, 20, (0, 0)));
    }
}
