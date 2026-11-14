using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

// ARC-RENDER-2064: the wisp glow table (keyed by creature prototype) and where a wisp's light stands. Synthetic rows only.
[TestClass]
public sealed class AreaWispGlowsTests
{
    private static AreaObjectRecord Row(int type, int prototype, long? x, long? y, params AreaFieldValue[] fields)
        => new(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, prototype, 600L + prototype, new string('b', 64), 100), 0, type, prototype, "p", "p", "r", "s", AreaScope.Core, null, x, y,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);

    [TestMethod]
    public void TableHoldsTheFourWispPrototypesWithDistinctTones()
    {
        CollectionAssert.AreEquivalent(new[] { 28471, 28472, 28473, 28474 }, AreaWispGlows.Table.Select(glow => glow.Prototype).ToArray());
        Assert.AreEqual(4, AreaWispGlows.Table.Select(glow => (glow.Red, glow.Green, glow.Blue)).Distinct().Count());
        Assert.IsTrue(AreaWispGlows.Table.All(glow => glow.Radius > 0 && glow.Strength is > 0 and <= 1));
        Assert.IsNull(AreaWispGlows.Of(28470));
        Assert.AreEqual((byte)0x99, AreaWispGlows.Of(28471)!.Red);
    }

    [TestMethod]
    public void RowsKeepOnlyNpcRowsOfAWispPrototypeWithACell()
    {
        var wisp = Row(AreaNpcRouteReader.NpcType, 28471, 100, 200);
        var rows = AreaWispGlows.Rows([wisp, Row(AreaNpcRouteReader.NpcType, 28470, 100, 200), Row(2, 28471, 100, 200), Row(AreaNpcRouteReader.NpcType, 28472, null, null)]);
        Assert.AreEqual(1, rows.Count);
        Assert.AreSame(wisp, rows[0].Row);
        Assert.AreEqual(28471, rows[0].Glow.Prototype);
    }

    [TestMethod]
    public void LightStandsAtTheRowsCellAndOffsetsUntilAPoseMovesIt()
    {
        var row = Row(AreaNpcRouteReader.NpcType, 28473, 100, 200, new AreaFieldValue(2, AreaPresence.Value, AreaValueOrigin.Instance, 5, [], null), new AreaFieldValue(3, AreaPresence.Value, AreaValueOrigin.Instance, -7, [], null));
        var glow = AreaWispGlows.Of(28473)!;
        var home = AreaWispGlows.Place(row, glow, null);
        Assert.AreEqual((100L, 200L, 5, -7), (home.CellX, home.CellY, home.OffsetX, home.OffsetY));
        var pose = new AreaCritterPose(row.Key, 103, 198, 11, 13);
        var moved = AreaWispGlows.Place(row, glow, pose);
        // The same arithmetic as a critter's carried light (LT10): the pose's cell, and the offsets that put the light the pose's shift from the T0 anchor.
        var (cx, cy, ox, oy) = AreaCritterLights.Stand(100, 200, 5, -7, pose);
        Assert.AreEqual((cx, cy, ox, oy), (moved.CellX, moved.CellY, moved.OffsetX, moved.OffsetY));
        Assert.AreEqual((103L, 198L), (moved.CellX, moved.CellY));
    }
}
