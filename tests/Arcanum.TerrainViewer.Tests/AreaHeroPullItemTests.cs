using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-INPUT-1946 (0x4445A0): the Alt click moves a loose item within one tile onto the hero's tile (the session's ground cell), farther away nothing moves. Invented rows, cells and ids, no original data.</summary>
[TestClass]
public sealed class AreaHeroPullItemTests
{
    private static readonly CellCoord Hero = new(100, 100);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaObjectRecord Item(int index, long x, long y) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
    private static SessionItemFacts Facts() => new(new(SessionItemType.Generic, 15199, 6000, 6001), 3, 0, 0, null, []);

    [TestMethod]
    public void AnItemOneTileAwayLiesOnTheHeroTileAndOneTwoTilesAwayStaysAndNothingElseChanges()
    {
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord[] rows = [Item(0, 101, 101), Item(1, 102, 100)];
        var map = new AreaInventoryMap(1, new([], [new SessionSeedItem(Entity(0), Facts(), new CellCoord(101, 101)), new SessionSeedItem(Entity(1), Facts(), new CellCoord(102, 100))]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        Assert.AreEqual(GameSessionFailure.None, new AreaInventoryHost([map]).Attach(session));
        var pack = session.QueryPcInventory().Count;

        Assert.AreEqual(HeroImmediateResult.TooFar, AreaHeroPull.PullItem(session, Entity(1), Hero), "two tiles away: the hook is told, nothing moves");
        Assert.AreEqual(new CellCoord(102, 100), session.QueryGroundItem(Entity(1))!.Cell);
        Assert.AreEqual(HeroImmediateResult.Moved, AreaHeroPull.PullItem(session, Entity(0), Hero), "a diagonal neighbour is within one tile");
        Assert.AreEqual(Hero, session.QueryGroundItem(Entity(0))!.Cell, "the item lies on the hero's tile");
        Assert.AreEqual(pack, session.QueryPcInventory().Count, "the inventory is unchanged: the item is not picked up");
        Assert.AreEqual(HeroImmediateResult.Moved, AreaHeroPull.PullItem(session, Entity(0), Hero), "an item already on the hero's tile stays");
        Assert.AreEqual(HeroImmediateResult.TooFar, AreaHeroPull.PullItem(session, Entity(1), Hero), "the far item is still where it was");
        Assert.AreEqual(HeroImmediateResult.None, AreaHeroPull.PullItem(session, Entity(5), Hero), "an entity that lies nowhere");
        Assert.AreEqual(HeroImmediateResult.None, AreaHeroPull.PullItem(session, "not an entity", Hero), "an id the session cannot hold");
    }
}
