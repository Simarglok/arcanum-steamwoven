using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-1230 (UO2-UO6 of ARC-RES-1109): an item lying on the ground is pushed by the knockback host through the session's ground cell, in the run of an NPC; the cast map answers its weight (the "weight below 5" area test) and
/// its pushed cell. Invented rows, cells and ids, no original data.
/// </summary>
[TestClass]
public sealed class AreaKnockbackItemsTests
{
    private static readonly CellCoord Source = new(90, 100);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Item(int index, long x, long y) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(int weight) => new(new(SessionItemType.Generic, 15199, 6000, 6001), weight, 0, 0, null, []);

    // A light item (weight 3) on (100,100) and a heavy one (weight 40) on (110,100); the heavy one is a scroll's cousin of the same type.
    private static (GameSessionOwner Session, AreaCombatHost Combat, AreaObjectRecord[] Rows) World()
    {
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord[] rows = [Item(0, 100, 100), Item(1, 110, 100)];
        var map = new AreaInventoryMap(1, new([], [new SessionSeedItem(Entity(0), Facts(3), new CellCoord(100, 100)), new SessionSeedItem(Entity(1), Facts(40), new CellCoord(110, 100))]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        return (session, new AreaCombatHost(inventory), rows);
    }

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 128; x++) for (var y = 0; y < 128; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    // The knockback bodies over the items alone (no AI host): every tile is on the map; the test names the blocked tiles.
    private sealed class Bodies(AreaKnockbackItems items) : IAreaKnockbackViewer
    {
        public Func<CellCoord, bool> Blocked = _ => false;
        public readonly List<int> Bumps = [];
        public CellCoord? Tile(string target) => items.Tile(target);
        public int Rotation(string target) => 0;
        public bool Contains(CellCoord tile) => true;
        public KnockbackBlock Block(string target, CellCoord left, CellCoord tested) => Blocked(tested) ? KnockbackBlock.Terrain : KnockbackBlock.None;
        public bool Slide(string target, CellCoord tile, int ms, long nowMs) => items.Slide(target, tile);
        public void Bump(string target, int maximum) { Bumps.Add(maximum); items.Bump(target, maximum); }
    }

    private sealed class Run : IDisposable
    {
        public GameSessionOwner Session { get; }
        public Bodies Bodies { get; }
        public AreaKnockbackHost Host { get; }
        public FrameStep Step { get; }
        public List<KnockbackFinished> Ends { get; } = [];

        public Run()
        {
            Session = World().Session;
            Bodies = new(new AreaKnockbackItems(Session, (_, _, _) => KnockbackBlock.None, () => null));
            Host = new AreaKnockbackHost(Session, Bodies, (_, end) => Ends.Add(end));
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, () => Host) };
        }

        public void Go(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    // The target test of Unseen Force reads the item's weight (field 0x59, below 5) and its type; the cast map answers the weight the session keeps.
    [TestMethod]
    public void TheCastMapAnswersAnItemsWeightForTheAreaTest()
    {
        var (session, combat, rows) = World();
        using var _ = session;
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, combat, () => null, () => null);
        Assert.AreEqual(3, map.Object(Entity(0))!.Weight);
        Assert.AreEqual(40, map.Object(Entity(1))!.Weight);
        Assert.IsTrue(map.Object(Entity(0))!.IsItem);
    }

    // UO5: a pushed item is on the cell of the push for every cast-map query, and no longer on the cell its data holds.
    [TestMethod]
    public void APushedItemIsFoundOnItsNewCellAndNoMoreOnItsOldOne()
    {
        var (session, combat, rows) = World();
        using var _ = session;
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, combat, () => null, () => null);
        const int Items = 1 << 14;
        Assert.AreEqual(Entity(0), map.ObjectsAt(new(100, 100), Items).Single().Id);
        Assert.AreEqual(GameSessionDisposition.Changed, session.MoveGroundItem(session.Version, Entity(0), new(104, 100)).Disposition);
        Assert.AreEqual(0, map.ObjectsAt(new(100, 100), Items).Count, "no longer on its data's cell");
        var found = map.ObjectsAt(new(104, 100), Items).Single();
        Assert.AreEqual((Entity(0), new SpellLocation(104, 100)), (found.Id, found.Location));
        Assert.AreEqual(new SpellLocation(104, 100), map.Object(Entity(0))!.Location);
        CollectionAssert.AreEquivalent(new[] { Entity(0) }, map.ObjectsInSquare(new(104, 100), 1, Items).Select(o => o.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { Entity(0), Entity(1) }, map.ObjectsInSquare(new(105, 100), 6, Items).Select(o => o.Id).ToArray());
        Assert.AreEqual(3, map.Object(Entity(0))!.Weight);
        CollectionAssert.AreEquivalent(new[] { Entity(1) }, map.ObjectsAt(new(110, 100), Items).Select(o => o.Id).ToArray(), "the other item stays");
    }

    [TestMethod]
    public void AnItemIsPushedFourTilesAwayFromTheSourceInTheRunOfAnNpc()
    {
        using var r = new Run();
        Assert.IsTrue(r.Host.Start(Entity(0), Source), "an item of the map's ground starts a run");
        var run = r.Session.QueryGoals().RunsOf(Entity(0)).Single();
        Assert.AreEqual(GoalType.Knockback, run.Bottom.Goal);
        Assert.AreEqual(new CellCoord(104, 100), run.Bottom.Tile, "four tiles away from the source");
        r.Go(2000);
        Assert.AreEqual(KnockbackOutcome.Exhausted, r.Ends.Single().Outcome);
        Assert.AreEqual(new CellCoord(104, 100), r.Session.QueryGroundItem(Entity(0))!.Cell);
        Assert.AreEqual(0, r.Bodies.Bumps.Count);
        Assert.AreEqual(0, r.Session.QueryGoals().InUseCount, "the run ended");
        Assert.AreEqual(new CellCoord(110, 100), r.Session.QueryGroundItem(Entity(1))!.Cell, "another item stays");
    }

    // GT65: a blocked step leaves the item on the tile it left (it does not enter the blocked one) and bumps it with the steps left.
    [TestMethod]
    public void ABlockedPushLeavesTheItemOnTheLastFreeTileAndBumpsIt()
    {
        using var r = new Run();
        r.Bodies.Blocked = tile => tile.X >= 103;
        Assert.IsTrue(r.Host.Start(Entity(0), Source));
        r.Go(2000);
        Assert.AreEqual(KnockbackOutcome.Blocked, r.Ends.Single().Outcome);
        Assert.AreEqual(new CellCoord(102, 100), r.Session.QueryGroundItem(Entity(0))!.Cell);
        Assert.AreEqual(1, r.Bodies.Bumps.Count);
    }

    [TestMethod]
    public void AnItemThatLiesNowhereOnTheGroundIsNoBody()
    {
        using var r = new Run();
        Assert.IsFalse(r.Host.Start("oid:nothing", Source));
        Assert.IsFalse(r.Host.Start("kit:1", Source));
        Assert.IsNull(r.Bodies.Tile("kit:1"));
    }

    // The dispatch of the viewer's bodies: an id the AI host does not run, that lies on the ground, is an item; the rest is the host's.
    [TestMethod]
    public void TheViewerBodiesTakeAnItemThroughTheGroundCell()
    {
        using var session = World().Session;
        var items = new AreaKnockbackItems(session, (_, _, _) => KnockbackBlock.None, () => null);
        var bodies = new AreaAiKnockbackBodies(() => null, items);
        Assert.AreEqual(new CellCoord(100, 100), bodies.Tile(Entity(0)));
        Assert.IsNull(bodies.Tile("oid:nothing"));
        Assert.AreEqual(0, bodies.Rotation(Entity(0)));
        Assert.AreEqual(KnockbackBlock.None, bodies.Block(Entity(0), new(100, 100), new(101, 100)));
        Assert.IsTrue(bodies.Slide(Entity(0), new(101, 100), 35, 0));
        Assert.AreEqual(new CellCoord(101, 100), bodies.Tile(Entity(0)));
        Assert.IsNull(new AreaAiKnockbackBodies(() => null).Tile(Entity(0)), "without the items body an object is still not pushed");
    }
}
