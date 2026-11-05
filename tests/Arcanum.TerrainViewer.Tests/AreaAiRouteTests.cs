using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b): the AI host's fold steps over invented rows, no original data: a human NPC (0) at world cell (10, 20) on a 32 × 32 open map
/// whose rows carry the route fields (waypoint list and index, day and night standpoints, NPC flags, teleport map). The host's waypoint and
/// standpoint steps (<see cref="AreaAiHost.RunWaypoints"/>, <see cref="AreaAiHost.RunStandpoints"/>) put the NPC on its waypoint or standpoint, store
/// the waypoint index for the host's life and record the place in the session, so a rebuilt host stands the NPC there again.
/// </summary>
[TestClass]
public sealed class AreaAiRouteTests
{
    private const int Map = 1;
    private static readonly CellCoord HeroCell = new(14, 20), NpcCell = new(10, 20);
    private const uint Day = NpcRouteFlags.WaypointsDay, Night = NpcRouteFlags.WaypointsNight;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), Map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
    private static long At(int x, int y) => ((long)y << 32) | (uint)x;

    private static AreaFieldValue Waypoints(params (int X, int Y)[] points) =>
        new(76, points.Length == 0 ? AreaPresence.Empty : AreaPresence.Array, AreaValueOrigin.Instance, null,
            [.. points.Select((p, key) => new AreaSparseElement(key, [p.X, p.Y]))], null);

    // The human NPC of the throw tests' fixture plus the route fields (slots 61, 69, 76..79).
    private static AreaObjectRecord Npc(uint flags, (int X, int Y)[] waypoints, int index, (int X, int Y) day, (int X, int Y) night, int teleportMap = 0) =>
        new(Key(0), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, NpcCell.X, NpcCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(61, teleportMap), Field(63, 0), Field(69, flags), Field(71, 0), Field(74, 10), Field(75, 10), Waypoints(waypoints), Field(77, index),
            Field(78, At(day.X, day.Y)), Field(79, At(night.X, night.Y)), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, AreaEventWorld Events, Func<AreaAiHost> Rebuild, string Npc);

    private static Scene Open(AreaObjectRecord row, Func<CellCoord?>? hero = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var heroDefinition = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(Map), HeroCell, NeighborDirection.XPlus), Hero: heroDefinition));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var map = new AreaInventoryMap(Map, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [])], []),
            new Dictionary<string, AreaObjectRecord> { [Entity(0)] = row });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var scene = new AreaAiScene
        {
            RuntimeMap = Map, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), row, NpcCell)], Combat = combat,
            HeroCell = hero ?? (() => HeroCell),
        };
        AreaAiHost? host = null;
        var events = new AreaEventWorld(() => combat, () => null, ai: () => host);
        AreaAiHost Build() => new(session, scene, events, new AreaCombatWorld(Map, () => [], _ => null));
        host = Build();
        return new(session, host, events, Build, Entity(0));
    }

    private static CellCoord Cell(Scene scene) => scene.Host.Tile(scene.Npc)!.Value;

    [TestMethod]
    public void The_standpoint_step_stands_a_far_npc_on_its_standpoint_and_the_session_keeps_the_place()
    {
        var scene = Open(Npc(0, [], 0, (20, 25), (20, 25)));
        Assert.IsFalse(scene.Host.RunWaypoints(scene.Npc), "no waypoint list: the step does nothing and answers 0");
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(new CellCoord(20, 25), Cell(scene), scene.Host.TraceOf(scene.Npc)?.Route);
        var place = scene.Session.QueryNpcPlaces(new SessionMapId(Map)).Single();
        Assert.AreEqual((scene.Npc, new CellCoord(20, 25)), (place.Entity, place.World));
        Assert.IsNull(scene.Host.RunOf(scene.Npc));
        Assert.AreEqual(new CellCoord(20, 25), scene.Rebuild().Tile(scene.Npc), "a host built again stands the NPC on the recorded place");
    }

    [TestMethod]
    public void An_npc_on_its_standpoint_is_not_moved_and_a_standpoint_beside_it_leaves_it_in_place()
    {
        var scene = Open(Npc(0, [], 0, (10, 20), (10, 20)));
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(NpcCell, Cell(scene));
        Assert.AreEqual(0, scene.Session.QueryNpcPlaces(new SessionMapId(Map)).Count, "nothing recorded: nothing moved");
        Assert.AreEqual("standpoint AtStandpoint", scene.Host.TraceOf(scene.Npc)!.Route);
        var beside = Open(Npc(0, [], 0, (11, 21), (11, 21)));
        beside.Host.RunStandpoints(beside.Npc);
        Assert.AreEqual(NpcCell, Cell(beside), "one tile away is within the standpoint radius");
    }

    [TestMethod]
    public void The_waypoint_step_walks_the_list_one_waypoint_per_fold_and_the_host_keeps_the_index()
    {
        var scene = Open(Npc(Day | Night, [(20, 20), (25, 20), (30, 20)], 0, (1, 1), (1, 1)));
        Assert.AreEqual(0, scene.Host.RouteIndexOf(scene.Npc));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual((new CellCoord(20, 20), 0), (Cell(scene), scene.Host.RouteIndexOf(scene.Npc)), "far from waypoint 0: stands on it, the index stays");
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual((new CellCoord(25, 20), 1), (Cell(scene), scene.Host.RouteIndexOf(scene.Npc)), "on waypoint 0: reached, moves to waypoint 1");
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual((new CellCoord(20, 20), 0), (Cell(scene), scene.Host.RouteIndexOf(scene.Npc)), "after the last waypoint the list wraps to 0");
        Assert.IsTrue(scene.Host.TraceOf(scene.Npc)!.Route!.StartsWith("waypoint Relocate: ", StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_wake_up_tick_runs_the_waypoint_step_and_then_the_standpoint_step_only_when_it_did_nothing()
    {
        // The session's own tick (the fold's first tick of S4): the wake-up marker runs the steps through the host's world.
        var withList = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)));
        var steps = WakeUp(withList);
        Assert.IsTrue(steps.HasFlag(AiTickSteps.Waypoints));
        Assert.IsFalse(steps.HasFlag(AiTickSteps.Standpoints), "the waypoint step returned 1");
        Assert.AreEqual(new CellCoord(20, 20), Cell(withList), withList.Host.TraceOf(withList.Npc)?.Route);

        var none = Open(Npc(0, [], 0, (3, 3), (3, 3)));
        steps = WakeUp(none);
        Assert.IsTrue(steps.HasFlag(AiTickSteps.Standpoints), "no list: the standpoint step ran");
        Assert.AreEqual(new CellCoord(3, 3), Cell(none));
    }

    private static AiTickSteps WakeUp(Scene scene)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.ScheduleAiTick(scene.Session.Version, new() { Entity = scene.Npc, Start = SessionAiTickStart.WakeUp }).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, scene.Session.AdvanceGameTime(scene.Session.QueryClockSnapshot().Token, 1, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var pass = scene.Session.ProcessEvents(scene.Events);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        return pass.AiTicks.Single().Tick!.Steps;
    }

    [TestMethod]
    public void A_target_the_shown_map_cannot_hold_leaves_the_npc_where_it_stands_but_the_index_still_moves()
    {
        // Outside the 32 × 32 chart; on the hero's own cell.
        var outside = Open(Npc(Day | Night, [(10, 28), (99, 99)], 0, (1, 1), (1, 1)));
        Assert.IsTrue(outside.Host.RunWaypoints(outside.Npc));
        Assert.AreEqual((new CellCoord(10, 28), 0), (Cell(outside), outside.Host.RouteIndexOf(outside.Npc)));
        Assert.IsTrue(outside.Host.RunWaypoints(outside.Npc));
        Assert.AreEqual((new CellCoord(10, 28), 1), (Cell(outside), outside.Host.RouteIndexOf(outside.Npc)), "waypoint 1 lies off the chart: not held");
        StringAssert.Contains(outside.Host.TraceOf(outside.Npc)!.Route, "not held");

        var hero = Open(Npc(Day | Night, [(14, 20)], 0, (1, 1), (1, 1)));
        Assert.IsTrue(hero.Host.RunWaypoints(hero.Npc));
        Assert.AreEqual(NpcCell, Cell(hero), "the hero's cell is not taken");
    }

    [TestMethod]
    public void A_teleport_map_of_another_map_makes_a_request_the_host_does_not_run()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1), teleportMap: 9));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc), "the original's step answers 1");
        Assert.AreEqual(NpcCell, Cell(scene), "MIG11 owns the request: the NPC stays");
        StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Route, "RequestTeleport to map 9");
        Assert.AreEqual(0, scene.Session.QueryNpcPlaces(new SessionMapId(Map)).Count);
    }

    [TestMethod]
    public void A_hidden_or_unreadable_npc_has_no_route_step()
    {
        var hidden = Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)) with { EffectiveFields = [.. Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)).EffectiveFields.Where(f => f.Slot != 18), Field(18, 0x2)] };
        var scene = Open(hidden);
        Assert.IsFalse(scene.Host.RunWaypoints(scene.Npc));
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(NpcCell, Cell(scene), "switched off: the original's step answers 0 and moves nothing");

        // A waypoint stored as three words is a route the reader refuses: no step, no exception.
        var bad = Npc(Day | Night, [], 0, (1, 1), (1, 1));
        bad = bad with { EffectiveFields = [.. bad.EffectiveFields.Where(f => f.Slot != 76), new(76, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [1, 2, 3])], null)] };
        var unreadable = Open(bad);
        Assert.IsFalse(unreadable.Host.RunWaypoints(unreadable.Npc));
        unreadable.Host.RunStandpoints(unreadable.Npc);
        Assert.AreEqual(NpcCell, Cell(unreadable));
        Assert.IsNull(unreadable.Host.RouteIndexOf(unreadable.Npc));
    }
}
