using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Traversal;

/// <summary>ARC-WORLD-018 whole-map edge rules (synthetic) and walkability of every complete
/// interior from its map-list start (opt-in, DAT_ROOT).</summary>
[TestClass]
public sealed class AreaWholeMapStepTests
{
    private const uint Door = (3u << 28) | (55u << 19) | (3u << 11) | (1u << 10);

    [TestMethod]
    public void Wall_pieces_split_into_doorways_windows_and_solid_edges()
    {
        foreach (var piece in new[] { 22, 25, 26, 29, 30, 31, 32 }) Assert.AreEqual(AreaStepEdgeRule.Pass, AreaStepInspector.WallPieceRule(piece).Rule, piece.ToString());
        foreach (var piece in new[] { 10, 13, 14, 17, 18, 19 }) Assert.AreEqual(("WINDOW_PIECE_JUMP", AreaStepEdgeRule.WindowJump), Swap(AreaStepInspector.WallPieceRule(piece)));
        foreach (var piece in new[] { 0, 9, 21, 23, 34 }) Assert.AreEqual(("WALL_PIECE_BLOCKS", AreaStepEdgeRule.Block), Swap(AreaStepInspector.WallPieceRule(piece)));
    }

    [TestMethod]
    public void A_portal_edge_follows_the_original_open_attempt_for_the_player_at_t0()
    {
        (string, AreaStepEdgeRule) Rule(uint art, long? flags, bool script = false) => Swap(AreaStepInspector.PortalEdgeRule(art, flags, script));
        Assert.AreEqual(("PORTAL_FLAGS_UNRESOLVED", AreaStepEdgeRule.Unknown), Rule(Door, null));
        Assert.AreEqual(("PORTAL_BUSTED", AreaStepEdgeRule.Pass), Rule(Door | 0x200, 0x1));
        Assert.AreEqual(("PORTAL_BUSTED", AreaStepEdgeRule.Pass), Rule(Door, 0x80 | 0x10 | 0x1));
        Assert.AreEqual(("PORTAL_OPEN", AreaStepEdgeRule.Pass), Rule(Door | (2u << 14), 0x10 | 0x1));
        Assert.AreEqual(("PORTAL_UNLOCK_SCRIPT", AreaStepEdgeRule.Unknown), Rule(Door, 0, script: true));
        Assert.AreEqual(("PORTAL_JAMMED_OR_HELD", AreaStepEdgeRule.Block), Rule(Door, 0x2));
        Assert.AreEqual(("PORTAL_JAMMED_OR_HELD", AreaStepEdgeRule.Block), Rule(Door, 0x4 | 0x8));
        // Unlocked: never-locked, or no LOCKED bit whatever the always-locked and hour bits say.
        Assert.AreEqual(("PORTAL_UNLOCKED_OPENS_ON_WALK", AreaStepEdgeRule.Pass), Rule(Door, 0x8 | 0x41));
        Assert.AreEqual(("PORTAL_UNLOCKED_OPENS_ON_WALK", AreaStepEdgeRule.Pass), Rule(Door, 0x10));
        Assert.AreEqual(("PORTAL_UNLOCKED_OPENS_ON_WALK", AreaStepEdgeRule.Pass), Rule(Door, 0x40));
        Assert.AreEqual(("PORTAL_UNLOCKED_OPENS_ON_WALK", AreaStepEdgeRule.Pass), Rule(Door, 0));
        Assert.AreEqual(("PORTAL_UNLOCKED_OPENS_ON_WALK", AreaStepEdgeRule.Pass), Rule(Door & ~(1u << 10), 0));
        // Locked: the hour may unlock it; always-locked removes the leaving shortcut; a plain lock leaves only.
        Assert.AreEqual(("PORTAL_LOCK_DEPENDS_ON_HOUR", AreaStepEdgeRule.Unknown), Rule(Door, 0x41));
        Assert.AreEqual(("PORTAL_LOCK_DEPENDS_ON_HOUR", AreaStepEdgeRule.Unknown), Rule(Door, 0x21 | 0x10));
        Assert.AreEqual(("PORTAL_ALWAYS_LOCKED_NO_KEY", AreaStepEdgeRule.Block), Rule(Door, 0x11));
        Assert.AreEqual(("PORTAL_LOCKED_LEAVING_ONLY", AreaStepEdgeRule.Unknown), Rule(Door, 0x1));
    }

    [TestMethod]
    public void Edge_rules_decide_cardinal_and_diagonal_legs()
    {
        // One open 3x3 patch; a single edge on the east side of the centre tile (edge 5 = +X).
        AreaStepAssessment Step(AreaStepEdgeRule rule, CardinalTraversalDirection direction, (long X, long Y) to)
        {
            var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
            for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
            var key = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 0, "o", 1);
            var walls = new Dictionary<AreaStepCell, AreaStepWallEdge[]> { [new(10, 10)] = [new(key, new(10, 10), 5, false, rule, "TEST_EDGE")] };
            var snapshot = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
                new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), walls);
            return snapshot.Assess(snapshot.Request(new(10, 10), new(to.X, to.Y), direction));
        }
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.Block, CardinalTraversalDirection.XPlus, (11, 10)).ReasonCode);
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.Legacy, CardinalTraversalDirection.XPlus, (11, 10)).ReasonCode);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(AreaStepEdgeRule.Pass, CardinalTraversalDirection.XPlus, (11, 10)).Disposition);
        var unknown = Step(AreaStepEdgeRule.Unknown, CardinalTraversalDirection.XPlus, (11, 10));
        Assert.AreEqual(CardinalTraversalCoverage.Unknown, unknown.Coverage);
        Assert.AreEqual("TEST_EDGE", unknown.Gaps.Single().ReasonCode);
        var jump = Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.XPlus, (11, 10));
        Assert.AreEqual("WINDOW_JUMP_UNMODELLED", jump.Gaps.Single().ReasonCode);
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.XPlusYPlus, (11, 11)).ReasonCode);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.YPlus, (10, 11)).Disposition);
    }

    [TestMethod]
    public void Whole_map_evidence_refuses_a_pinned_or_structural_snapshot()
    {
        using var f = new Arcanum.Data.Tests.Area.AreaIdentityTests.Fixture([(67108865, [])]);
        var area = f.Owner.LoadStrict();
        var stack = ContentStack.Open(new ContentMountPlan(Path.GetTempPath(), "none", [])).Stack;
        Assert.ThrowsExactly<AreaStepInvocationException>(() => AreaStepInspector.InspectWholeMap(area, stack!, null, 1));
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_complete_interior_is_walkable_from_its_map_list_start()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in whole-map step check; this is not a synthetic pass.");
        var opened = ContentStack.Open(ContentMountPlan.Discover(root).Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        int complete = 0, walkable = 0;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name).Plan!;
            using var owner = plan.Open();
            var inspection = owner.Inspect();
            if (!inspection.IsComplete) continue;
            complete++;
            var steps = AreaStepInspector.InspectWholeMap(inspection.Snapshot!, stack, map.AggregatePath, 1);
            Assert.AreEqual(0, steps.TerrainUnknownCells, map.Name);
            if (plan.StartTile is not { } start) continue;
            var reach = Reach(steps, new(start.X, start.Y));
            if (reach > 1) walkable++;
            if (map.Name == "crash site cave") Assert.AreEqual(506, reach, "the generic cave walks like the pinned cave profile");
        }
        Assert.AreEqual(54, complete);
        Assert.IsTrue(walkable >= 48, $"{walkable} of {complete} complete interiors walkable from their start");
    }

    private static int Reach(AreaStepSnapshot steps, AreaStepCell origin)
    {
        int[] dx = [-1, -1, -1, 0, 1, 1, 1, 0], dy = [-1, 0, 1, 1, 1, 0, -1, -1];
        var seen = new HashSet<AreaStepCell> { origin }; var queue = new Queue<AreaStepCell>([origin]);
        while (queue.TryDequeue(out var cell))
            for (var d = 0; d < 8; d++)
            {
                var next = new AreaStepCell(cell.X + dx[d], cell.Y + dy[d]);
                if (seen.Contains(next)) continue;
                var step = steps.Assess(steps.Request(cell, next, (CardinalTraversalDirection)d));
                if (step.Coverage == CardinalTraversalCoverage.Supported && step.Disposition == CardinalTraversalDisposition.Open) { seen.Add(next); queue.Enqueue(next); }
            }
        return seen.Count;
    }

    private static (string, AreaStepEdgeRule) Swap((AreaStepEdgeRule Rule, string Reason) value) => (value.Reason, value.Rule);
}
