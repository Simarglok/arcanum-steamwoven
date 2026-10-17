using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-RENDER-021 roof rules on hand-made roofs (ledger RF1–RF7).</summary>
[TestClass]
public sealed class AreaRoofGridTests
{
    private static readonly AreaSectorKey Sector = new("s", 1, 0);
    private static readonly AreaRoofShades Shades = new(255, 200, 80);

    // Piece p as a stored roof art: frame p (or p − 9 mirrored for 9–12), fill and fade bits as given.
    private static AreaRoofRecord Roof(long blockX, long blockY, int piece, bool fill = false, bool fade = false)
    {
        var frame = piece >= 9 ? piece - 9 : piece;
        var raw = (10u << 28) | ((uint)frame << 14) | (fill ? 0x2000u : 0) | (fade ? 0x1000u : 0) | (piece >= 9 ? 1u : 0);
        return new(Sector, 0, blockX, blockY, raw, piece, fill, fade, new(AreaSourceKind.Terrain, Sector, 4096, string.Empty, AreaLayerKind.Roof));
    }

    // The original's per-piece fill conditions on the tile's (x % 4, y % 4), written from the ledger's prose (RF3).
    private static bool Condition(int piece, int x, int y) => piece switch
    {
        0 => y > 1 && x < 3, 1 => x < 3, 2 => y > 1, 3 => !(x >= 3 && y <= 1), 4 => x < 3 && y < 3, 5 => x < 3 || y < 3,
        6 => x > 1 || y > 1, 7 => x > 1 && y > 1, 8 => true, 9 => x > 1 && y < 3, 10 => y < 3, 11 => x > 1, _ => x > 1 || y < 3,
    };

    [TestMethod]
    public void Recalculation_fills_a_block_exactly_on_the_tiles_its_piece_band_holds()
    {
        for (var piece = 0; piece < 13; piece++)
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                {
                    var grid = new AreaRoofGrid([Roof(64, 64, piece)]);
                    grid.Recalc(64 + x, 64 + y);
                    Assert.AreEqual(Condition(piece, x, y), grid.State(grid.Records.Single()).Fill, $"piece {piece} at {x},{y}");
                }
    }

    [TestMethod]
    public void A_roof_covers_an_indoor_tile_three_tiles_before_its_band_and_a_faded_roof_only_objects()
    {
        var grid = new AreaRoofGrid([Roof(64, 64, 8)]);
        static bool Indoor(long x, long y) => true;
        // The centre piece covers every tile whose (x + 3, y + 3) lies in its block: 61..64 on each axis.
        Assert.IsTrue(grid.IsCovered(61, 61, false, Indoor));
        Assert.IsTrue(grid.IsCovered(64, 64, false, Indoor));
        Assert.IsFalse(grid.IsCovered(65, 64, false, Indoor));
        Assert.IsFalse(grid.IsCovered(61, 61, false, (_, _) => false), "an outdoor tile is never covered");
        grid.FadeOn(64, 64);
        Assert.IsTrue(grid.IsFaded(66, 66));
        Assert.IsFalse(grid.IsCovered(62, 62, false, Indoor), "tiles show under a faded roof");
        Assert.IsTrue(grid.IsCovered(62, 62, true, Indoor), "objects stay hidden under it");
        grid.FillOn(66, 66);
        Assert.IsFalse(grid.IsCovered(62, 62, true, Indoor), "a filled roof covers nothing");
        Assert.IsFalse(grid.IsFaded(66, 66), "a filled roof is not faded");
        // The west band (columns 0..2) leaves column 3 open.
        var west = new AreaRoofGrid([Roof(64, 64, 1)]);
        Assert.IsTrue(west.IsCovered(63, 61, false, Indoor));
        Assert.IsFalse(west.IsCovered(64, 61, false, Indoor));
    }

    [TestMethod]
    public void A_fill_floods_one_roof_and_stops_at_the_outer_edges_facing_it()
    {
        // A 3 × 3 roof (pieces as the first building: north-east outside, north, north-west outside / east, centre, west /
        // south-east outside, south, south-west outside along increasing x) with a second roof west of it.
        AreaRoofRecord[] house =
        [
            Roof(64, 64, 7), Roof(68, 64, 2), Roof(72, 64, 0),
            Roof(64, 68, 11), Roof(68, 68, 8), Roof(72, 68, 1),
            Roof(64, 72, 9), Roof(68, 72, 10), Roof(72, 72, 4),
            Roof(76, 68, 11),
        ];
        var grid = new AreaRoofGrid(house);
        var revision = grid.Revision;
        grid.Recalc(69, 69);
        Assert.AreEqual(9, grid.Records.Count(r => grid.State(r).Fill), "the whole house and nothing past its west edge");
        Assert.IsFalse(grid.State(house[9]).Fill);
        Assert.IsTrue(grid.Revision > revision);
        grid.FillOff(64, 64);
        Assert.AreEqual(0, grid.Records.Count(r => grid.State(r).Fill));
        revision = grid.Revision;
        grid.FillOff(64, 64);
        Assert.AreEqual(revision, grid.Revision, "no change, no revision");
    }

    [TestMethod]
    public void Slots_blocks_screen_positions_and_fade_corners_follow_the_original()
    {
        Assert.AreEqual(0, AreaRoofGrid.Slot(0));
        Assert.AreEqual(1 + 16, AreaRoofGrid.Slot(5 + 64 * 7));
        Assert.AreEqual(15 + 16 * 15, AreaRoofGrid.Slot(4095));
        Assert.AreEqual((66L, 70L), AreaRoofGrid.Normalize(67, 68));
        Assert.AreEqual((1000L - 120, 2200L - 200), AreaRoofGrid.ScreenPosition(65, 69, (x, y) => (x * 10 + 340, y * 25 + 450)));
        Assert.AreEqual(((byte)80, (byte)200, (byte)200, (byte)80), AreaRoofGrid.FadeCorners(0, Shades));
        Assert.AreEqual(((byte)255, (byte)255, (byte)255, (byte)255), AreaRoofGrid.FadeCorners(8, Shades));
        Assert.AreEqual(((byte)200, (byte)80, (byte)200, (byte)255), AreaRoofGrid.FadeCorners(11, Shades));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaRoofGrid.FadeCorners(13, Shades));
        Assert.AreEqual(12, AreaRoofGrid.Piece((10u << 28) | (3u << 14) | 1));
        var frame = new AreaVisualRecord(Roof(64, 64, 9).Visual, 0, 0, null!, "", 0, 0, 0, 0, 0, 0, 0, true, 300, 150, -95, 7, 3, 0, 0, 0, 100, 0, 0, "", "");
        Assert.AreEqual((0, 7, 0), AreaRoofGrid.HotSpot(frame), "a mirrored roof frame takes hot spot x 0 and offset x 0");
        Assert.AreEqual((-95, 7, 3), AreaRoofGrid.HotSpot(frame with { Mirrored = false }));
        Assert.IsNull(new AreaRoofGrid([]).Art(1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaRoofGrid([Roof(64, 64, 0), Roof(65, 64, 1)]));
    }
}
