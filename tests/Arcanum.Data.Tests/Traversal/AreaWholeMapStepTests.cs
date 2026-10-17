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
        // Locked: the hour may unlock it; always-locked removes the leaving shortcut; otherwise the step decides (ARC-RES-049).
        Assert.AreEqual(("PORTAL_LOCK_DEPENDS_ON_HOUR", AreaStepEdgeRule.Leaving), Rule(Door, 0x41));
        Assert.AreEqual(("PORTAL_LOCK_DEPENDS_ON_HOUR", AreaStepEdgeRule.Unknown), Rule(Door, 0x21 | 0x10));
        Assert.AreEqual(("PORTAL_ALWAYS_LOCKED_NO_KEY", AreaStepEdgeRule.Block), Rule(Door, 0x11));
        Assert.AreEqual(("PORTAL_LOCKED_LEAVING_ONLY", AreaStepEdgeRule.Leaving), Rule(Door, 0x1));
    }

    [TestMethod]
    public void A_locked_portal_opens_only_for_a_step_both_open_attempts_call_leaving()
    {
        // ARC-RES-049 (original 1.0.7.4): x <= 10 is indoor, x >= 11 outdoor, unless a cell is listed as the other kind.
        const uint IndoorTile = 0, OutdoorTile = 1u << 8;
        var portal = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 0, "portal", 1);
        AreaStepAssessment Step((long X, long Y) at, int rawRotation, (long X, long Y) from, CardinalTraversalDirection direction, (long X, long Y) to,
            string reason = "PORTAL_LOCKED_LEAVING_ONLY", (long X, long Y)[]? flipped = null)
        {
            var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
            for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            {
                var indoor = x <= 10 ^ (flipped ?? []).Contains((x, y));
                terrain[new(x, y)] = new(new(x, y), indoor ? IndoorTile : OutdoorTile, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
            }
            var walls = new Dictionary<AreaStepCell, AreaStepWallEdge[]>
            { [new(at.X, at.Y)] = [new(portal, new(at.X, at.Y), rawRotation | 1, false, AreaStepEdgeRule.Leaving, reason, rawRotation)] };
            var snapshot = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
                new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), walls);
            return snapshot.Assess(snapshot.Request(new(from.X, from.Y), new(to.X, to.Y), direction));
        }
        string Verdict(AreaStepAssessment a) => a.Coverage != CardinalTraversalCoverage.Supported ? "Unknown:" + a.Gaps.Single().ReasonCode
            : a.Disposition == CardinalTraversalDisposition.Open ? "Open" : a.ReasonCode;
        const CardinalTraversalDirection East = CardinalTraversalDirection.XPlus, West = CardinalTraversalDirection.XMinus;

        // Portal on the indoor tile, edge 5 to the outdoor tile: leaving from its own tile opens, entering is locked.
        Assert.AreEqual("Open", Verdict(Step((10, 10), 5, (10, 10), East, (11, 10))));
        Assert.AreEqual("WALL_EDGE_BLOCKED", Verdict(Step((10, 10), 5, (11, 10), West, (10, 10))));
        // Portal on the outdoor tile facing in (rotation 1): the original exchanges the tiles for a walker from across,
        // so leaving the building through it opens (CE never counts that side as leaving); entering stays locked
        // although the goal on the portal's own tile tests rotation 1 in reverse (the traversal refuses first).
        Assert.AreEqual("Open", Verdict(Step((11, 10), 1, (10, 10), East, (11, 10))));
        Assert.AreEqual("WALL_EDGE_BLOCKED", Verdict(Step((11, 10), 1, (11, 10), West, (10, 10))));
        // Raw rotation 0..2 on its own indoor tile: the traversal calls it leaving, the goal (direction 5) does not.
        Assert.AreEqual("WALL_EDGE_BLOCKED", Verdict(Step((10, 10), 1, (10, 10), West, (9, 10), flipped: [(9, 10)])));
        Assert.AreEqual("Open", Verdict(Step((10, 10), 3, (10, 10), CardinalTraversalDirection.YPlus, (10, 11), flipped: [(10, 11)])));
        // An even raw rotation tests the diagonal tile it points at, not the tile across the edge.
        Assert.AreEqual("Open", Verdict(Step((10, 10), 4, (10, 10), East, (11, 10), flipped: [(11, 10)])));
        Assert.AreEqual("WALL_EDGE_BLOCKED", Verdict(Step((10, 10), 4, (10, 10), East, (11, 10), flipped: [(11, 11)])));
        // Diagonal steps: each leg across the edge decides from its own side.
        Assert.AreEqual("Open", Verdict(Step((10, 10), 5, (10, 10), CardinalTraversalDirection.XPlusYPlus, (11, 11))));
        Assert.AreEqual("WALL_EDGE_BLOCKED", Verdict(Step((10, 10), 5, (11, 11), CardinalTraversalDirection.XMinusYMinus, (10, 10))));
        // The goal looks from the step's source, not the leg's: diagonal direction 6 to the portal also exchanges.
        Assert.AreEqual("Open", Verdict(Step((11, 10), 1, (10, 11), CardinalTraversalDirection.XPlusYMinus, (11, 10))));
        // A lock that depends on the hour opens for a leaving step and is Unknown otherwise.
        Assert.AreEqual("Open", Verdict(Step((10, 10), 5, (10, 10), East, (11, 10), "PORTAL_LOCK_DEPENDS_ON_HOUR")));
        Assert.AreEqual("Unknown:PORTAL_LOCK_DEPENDS_ON_HOUR", Verdict(Step((10, 10), 5, (11, 10), West, (10, 10), "PORTAL_LOCK_DEPENDS_ON_HOUR")));
        // The tile the rotation points at lies outside the evidence.
        Assert.AreEqual("Unknown:PORTAL_LEAVING_TILE_UNRESOLVED", Verdict(Step((62, 63), 4, (62, 63), East, (63, 63))));
    }

    [TestMethod]
    public void Edge_rules_decide_cardinal_and_diagonal_legs()
    {
        // One open 3x3 patch; a single edge on the east side of the centre tile (edge 5 = +X).
        AreaStepAssessment Step(AreaStepEdgeRule rule, CardinalTraversalDirection direction, (long X, long Y) to) => StepFrom(rule, (10, 10), direction, to);
        AreaStepAssessment StepFrom(AreaStepEdgeRule rule, (long X, long Y) from, CardinalTraversalDirection direction, (long X, long Y) to)
        {
            var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
            for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
            var key = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 0, "o", 1);
            var walls = new Dictionary<AreaStepCell, AreaStepWallEdge[]> { [new(10, 10)] = [new(key, new(10, 10), 5, false, rule, "TEST_EDGE")] };
            var snapshot = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
                new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), walls);
            return snapshot.Assess(snapshot.Request(new(from.X, from.Y), new(to.X, to.Y), direction));
        }
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.Block, CardinalTraversalDirection.XPlus, (11, 10)).ReasonCode);
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.Legacy, CardinalTraversalDirection.XPlus, (11, 10)).ReasonCode);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(AreaStepEdgeRule.Pass, CardinalTraversalDirection.XPlus, (11, 10)).Disposition);
        var unknown = Step(AreaStepEdgeRule.Unknown, CardinalTraversalDirection.XPlus, (11, 10));
        Assert.AreEqual(CardinalTraversalCoverage.Unknown, unknown.Coverage);
        Assert.AreEqual("TEST_EDGE", unknown.Gaps.Single().ReasonCode);
        // ARC-WORLD-021: the cardinal step jumps the window, from either side of the edge.
        var key = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 0, "o", 1);
        var jump = Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.XPlus, (11, 10));
        Assert.AreEqual((CardinalTraversalDisposition.Open, "WINDOW_JUMP", 0), (jump.Disposition!.Value, jump.ReasonCode, jump.Gaps.Count));
        Assert.AreEqual(key, jump.WindowJump);
        var back = StepFrom(AreaStepEdgeRule.WindowJump, (11, 10), CardinalTraversalDirection.XMinus, (10, 10));
        Assert.AreEqual(("WINDOW_JUMP", key), (back.ReasonCode, back.WindowJump));
        Assert.IsNull(Step(AreaStepEdgeRule.Pass, CardinalTraversalDirection.XPlus, (11, 10)).WindowJump);
        Assert.AreEqual("WALL_EDGE_BLOCKED", Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.XPlusYPlus, (11, 11)).ReasonCode);
        Assert.AreEqual(CardinalTraversalDisposition.Open, Step(AreaStepEdgeRule.WindowJump, CardinalTraversalDirection.YPlus, (10, 11)).Disposition);
    }

    [TestMethod]
    public void A_window_step_still_yields_to_its_portal_and_to_the_destination()
    {
        // ARC-WORLD-021: a window piece on the +X edge of (10,10), plus a portal on the same edge or a destination blocker.
        var window = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 0, "window", 1);
        var portal = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 1, "portal", 1);
        var crate = new AreaObjectKey("s", 1, AreaSourceKind.Static, 0, 0, 2, "crate", 1);
        AreaStepAssessment Step(AreaStepEdgeRule? portalRule, bool terrainBlocked = false, CardinalTraversalDisposition? crateAt = null)
        {
            var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
            for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported,
                    terrainBlocked && x == 11 && y == 10 ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
            var edges = new List<AreaStepWallEdge> { new(window, new(10, 10), 5, true, AreaStepEdgeRule.WindowJump, "WINDOW_PIECE_JUMP") };
            if (portalRule is { } rule) edges.Add(new(portal, new(10, 10), 5, false, rule, "TEST_PORTAL"));
            var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>();
            if (crateAt is { } disposition)
                objects[new(11, 10)] = [new(crate, new(11, 10), 3, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.Applicable, disposition, "TEST_CRATE")];
            var snapshot = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
                objects, new Dictionary<AreaStepCell, AreaStepWallEdge[]> { [new(10, 10)] = edges.ToArray() });
            return snapshot.Assess(snapshot.Request(new(10, 10), new(11, 10), CardinalTraversalDirection.XPlus));
        }
        Assert.AreEqual(("WALL_EDGE_BLOCKED", (AreaObjectKey?)null), (Step(AreaStepEdgeRule.Block).ReasonCode, Step(AreaStepEdgeRule.Block).WindowJump), "a jammed or held window portal blocks");
        var unknown = Step(AreaStepEdgeRule.Unknown);
        Assert.AreEqual((CardinalTraversalCoverage.Unknown, "REQUIRED_EVIDENCE_UNAVAILABLE", (AreaObjectKey?)window), (unknown.Coverage, unknown.ReasonCode, unknown.WindowJump),
            "an Unknown portal leaves the step Unknown, still naming the window");
        Assert.AreEqual(("WINDOW_JUMP", (AreaObjectKey?)window), (Step(AreaStepEdgeRule.Pass).ReasonCode, Step(AreaStepEdgeRule.Pass).WindowJump), "an openable window portal lets the jump through");
        Assert.AreEqual("DESTINATION_TERRAIN_BLOCKED", Step(null, terrainBlocked: true).ReasonCode);
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", Step(null, crateAt: CardinalTraversalDisposition.Blocked).ReasonCode);
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
        int complete = 0, walkable = 0, jumps = 0, jumpMaps = 0;
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
            var (reach, windowJumps) = Reach(steps, new(start.X, start.Y));
            if (reach > 1) walkable++;
            jumps += windowJumps; if (windowJumps > 0) { jumpMaps++; Console.WriteLine($"window jumps: {map.Name} {windowJumps}"); }
            if (map.Name == "crash site cave") Assert.AreEqual(506, reach, "the generic cave walks like the pinned cave profile");
        }
        Assert.AreEqual(54, complete);
        Assert.IsTrue(walkable >= 48, $"{walkable} of {complete} complete interiors walkable from their start");
        // ARC-WORLD-021: directed Open window-jump steps out of the cells reachable from the starts.
        Console.WriteLine($"whole-map interiors complete={complete} walkable={walkable} window-jump-steps={jumps} maps-with-jumps={jumpMaps}");
        Assert.AreEqual((22, 2), (jumps, jumpMaps), "directed window-jump steps out of the reachable cells, and the interiors that have them");
    }

    // Cells reachable from the origin, and the directed Open window-jump steps out of every reached cell.
    private static (int Cells, int WindowJumps) Reach(AreaStepSnapshot steps, AreaStepCell origin)
    {
        int[] dx = [-1, -1, -1, 0, 1, 1, 1, 0], dy = [-1, 0, 1, 1, 1, 0, -1, -1];
        var jumps = 0;
        var seen = new HashSet<AreaStepCell> { origin }; var queue = new Queue<AreaStepCell>([origin]);
        while (queue.TryDequeue(out var cell))
            for (var d = 0; d < 8; d++)
            {
                var next = new AreaStepCell(cell.X + dx[d], cell.Y + dy[d]);
                if (!steps.IsAdmitted(next)) continue;
                var step = steps.Assess(steps.Request(cell, next, (CardinalTraversalDirection)d));
                if (step.Coverage != CardinalTraversalCoverage.Supported || step.Disposition != CardinalTraversalDisposition.Open) continue;
                if (step.WindowJump is not null) jumps++;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        return (seen.Count, jumps);
    }

    private static (string, AreaStepEdgeRule) Swap((AreaStepEdgeRule Rule, string Reason) value) => (value.Reason, value.Rule);
}
