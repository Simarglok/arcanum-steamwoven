using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.ChangeOfPlace;
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

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, AreaEventWorld Events, Func<AreaAiHost> Rebuild, string Npc, Walks Walks);

    /// <summary>ARC-DEV-116: the companion's own walk as the host sees it (a jump moves it at once).</summary>
    private sealed class Walks(AreaAiNpc companion) : IAreaAiFollowers
    {
        public readonly Dictionary<string, CellCoord> Cells = new() { [companion.Entity] = companion.Origin };
        public IReadOnlyList<AreaAiNpc> Npcs { get; } = [companion];
        public bool Step(string entity, CellCoord next, bool running) { Cells[entity] = next; return true; }
        public void Face(string entity, CellCoord toward) { }
        public bool Jump(string entity, CellCoord cell) { Cells[entity] = cell; return true; }
    }

    // ARC-DEV-116: with companion the NPC is a companion of the shown map (a free one unless led), run as the host runs followers.
    private static Scene Open(AreaObjectRecord row, Func<CellCoord?>? hero = null, RuleMode rules = RuleMode.Steamwoven, bool companion = false, bool led = false,
        Func<int, bool>? mapListed = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var heroDefinition = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(Map), HeroCell, NeighborDirection.XPlus), Hero: heroDefinition, Rules: rules));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var map = new AreaInventoryMap(Map, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [])], []),
            new Dictionary<string, AreaObjectRecord> { [Entity(0)] = row });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        if (companion)
        {
            Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(0)).Disposition);
            if (!led) Assert.AreEqual(GameSessionDisposition.Changed, session.LeaveParty(session.Version, Entity(0), new PartyLeave(Force: true)).Disposition);
        }
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var walks = new Walks(new(Entity(0), row, NpcCell));
        var scene = new AreaAiScene
        {
            RuntimeMap = Map, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = companion ? [] : [new(Entity(0), row, NpcCell)], Combat = combat,
            HeroCell = hero ?? (() => HeroCell), MapListed = mapListed, Homecomings = [],
            Companions = companion ? () => [(Entity(0), Key(0), walks.Cells[Entity(0)])] : null,
            Followers = companion ? walks : null,
        };
        AreaAiHost? host = null;
        var events = new AreaEventWorld(() => combat, () => null, ai: () => host);
        AreaAiHost Build() => new(session, scene, events, new AreaCombatWorld(Map, () => [], _ => null));
        host = Build();
        return new(session, host, events, Build, Entity(0), walks);
    }

    private static CellCoord Cell(Scene scene) => scene.Host.Tile(scene.Npc)!.Value;
    private static readonly SessionMapId Home = new(9);

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
        Assert.AreEqual("standpoint AtStandpoint [teleport map 1]", scene.Host.TraceOf(scene.Npc)!.Route, "the step wrote the open map as the teleport map (WS3)");
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
    public void A_rebuilt_host_goes_on_from_the_waypoint_the_saved_place_stands_on()
    {
        var scene = Open(Npc(Day | Night, [(20, 20), (25, 20), (30, 20)], 0, (1, 1), (1, 1)));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual((new CellCoord(25, 20), 1), (Cell(scene), scene.Host.RouteIndexOf(scene.Npc)));
        var rebuilt = scene.Rebuild();
        Assert.AreEqual(new CellCoord(25, 20), rebuilt.Tile(scene.Npc), "the saved place is restored");
        Assert.AreEqual(1, rebuilt.RouteIndexOf(scene.Npc), "the index is recovered from the waypoint it stands on, not the row's 0");
        Assert.IsTrue(rebuilt.RunWaypoints(scene.Npc));
        Assert.AreEqual((new CellCoord(30, 20), 2), (rebuilt.Tile(scene.Npc)!.Value, rebuilt.RouteIndexOf(scene.Npc)), "the next fold moves on to waypoint 2");
        // An NPC that stands off every waypoint falls back to the row's index.
        var plain = Open(Npc(Day | Night, [(20, 20), (25, 20)], 1, (1, 1), (1, 1)));
        Assert.AreEqual(1, plain.Host.RouteIndexOf(plain.Npc));
    }

    [TestMethod]
    public void A_relocation_ends_the_npcs_run()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)));
        scene.Host.PerformAttack(scene.Npc, Arcanum.Simulation.Ai.AiSchedulerRules.Pc);
        Assert.IsNotNull(scene.Host.RunOf(scene.Npc), "the attack starter gave the NPC a run: " + scene.Host.TraceOf(scene.Npc)?.Perform);
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.IsNull(scene.Host.RunOf(scene.Npc), "the relocation interrupts the run (priority 4)");
        Assert.AreEqual(new CellCoord(20, 20), Cell(scene));
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
    public void A_teleport_map_of_another_map_asks_for_the_npcs_change_of_place_and_leaves_it_standing_until_the_ping()
    {
        // The row's own teleport map (the stored object's field 243) is the baseline.
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1), teleportMap: 9), mapListed: map => map is Map or 9);
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc), "the original's step answers 1");
        Assert.AreEqual(NpcCell, Cell(scene), "the request moves nothing at once");
        StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Route, "RequestTeleport to map 9 (20,20): queued");
        CollectionAssert.AreEqual(new[] { scene.Npc }, scene.Host.PendingHomecomings.ToArray());
        Assert.IsNull(scene.Session.QueryChangeOfPlace(), "Steamwoven makes the request with the next frame's flush");
        Assert.IsTrue(scene.Host.FlushHomecomings());
        var request = scene.Session.QueryChangeOfPlace()!;
        Assert.AreEqual((scene.Npc, ChangeOfPlaceCaller.NpcWaypoint, 9, new CellCoord(20, 20), ChangeOfPlaceFlags.None, false),
            (request.Entity, request.Caller, request.DestinationMap, request.Destination, request.Flags, request.IsPlayerCharacter), "WS6: the NPC alone, flags 0");
        Assert.AreEqual(0, scene.Host.PendingHomecomings.Count);
        Assert.AreEqual(0, scene.Session.QueryNpcPlaces(new SessionMapId(Map)).Count);
        Assert.IsFalse(scene.Host.FlushHomecomings(), "nothing left to ask for");
    }

    [TestMethod]
    public void The_sessions_teleport_map_wins_over_the_rows_and_a_map_the_viewer_does_not_list_drops_the_request()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)), mapListed: map => map == Map);
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.EnsureTeleportMap(scene.Session.Version, scene.Npc, Home).Disposition);
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Route, "RequestTeleport to map 9");
        Assert.IsFalse(scene.Host.FlushHomecomings(), "map 9 is not in the viewer's list: the project refuses the request (CD1)");
        StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Route, "DestinationNotListed");
        Assert.IsNull(scene.Session.QueryChangeOfPlace());
        Assert.AreEqual(Home.RuntimeMap, scene.Session.QueryTeleportMap(scene.Npc), "a refused request leaves the tie as it is");
    }

    [TestMethod]
    public void The_first_step_of_a_leaderless_npc_writes_the_open_map_as_its_teleport_map_once()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)));
        Assert.IsNull(scene.Session.QueryTeleportMap(scene.Npc));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual(Map, scene.Session.QueryTeleportMap(scene.Npc), "WS3: the getter writes the open map while the field is 0");
        StringAssert.Contains(scene.Host.TraceOf(scene.Npc)!.Route, "[teleport map 1]");
        // The step that ends on the day or night flag writes it too (WS3, WS1); a later step never rewrites it.
        var flagless = Open(Npc(0, [(20, 20)], 0, (1, 1), (1, 1)));
        Assert.IsFalse(flagless.Host.RunWaypoints(flagless.Npc));
        Assert.AreEqual(Map, flagless.Session.QueryTeleportMap(flagless.Npc));
        var version = flagless.Session.Version;
        flagless.Host.RunWaypoints(flagless.Npc);
        Assert.AreEqual(version, flagless.Session.Version, "written once");
        // An NPC with no waypoint list still gets the tie from the standpoint step (the getter's second caller).
        var none = Open(Npc(0, [], 0, (10, 20), (10, 20)));
        none.Host.RunStandpoints(none.Npc);
        Assert.AreEqual(Map, none.Session.QueryTeleportMap(none.Npc), "the standpoint step is a getter caller too");
    }

    [TestMethod]
    public void A_former_follower_on_the_map_it_is_tied_to_is_put_on_its_waypoint_through_its_walk()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)), companion: true);
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual(new CellCoord(20, 20), scene.Walks.Cells[scene.Npc], scene.Host.TraceOf(scene.Npc)?.Route);
        Assert.AreEqual(0, scene.Host.PendingHomecomings.Count);
        Assert.AreEqual(Map, scene.Session.QueryTeleportMap(scene.Npc));
    }

    [TestMethod]
    public void A_former_follower_tied_to_another_map_is_sent_home_by_a_request_and_a_following_one_takes_no_step()
    {
        var scene = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)), companion: true, mapListed: map => map is Map or 9);
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.EnsureTeleportMap(scene.Session.Version, scene.Npc, Home).Disposition);
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc));
        Assert.AreEqual(NpcCell, scene.Walks.Cells[scene.Npc], "it stands where it was until the ping");
        Assert.IsTrue(scene.Host.FlushHomecomings());
        Assert.AreEqual((scene.Npc, 9), (scene.Session.QueryChangeOfPlace()!.Entity, scene.Session.QueryChangeOfPlace()!.DestinationMap));

        // The hero's follower has a leader: the standpoint step is not its own and nothing is asked.
        var led = Open(Npc(0, [], 0, (1, 1), (1, 1)), companion: true, led: true, mapListed: map => map is Map or 9);
        Assert.AreEqual(GameSessionDisposition.Changed, led.Session.EnsureTeleportMap(led.Session.Version, led.Npc, Home).Disposition);
        led.Host.RunStandpoints(led.Npc);
        Assert.AreEqual(0, led.Host.PendingHomecomings.Count);
        Assert.AreEqual(NpcCell, led.Walks.Cells[led.Npc]);
    }

    [TestMethod]
    public void A_former_follower_waits_for_the_slot_and_a_rebuilt_host_keeps_it_in_Steamwoven_but_overwrites_in_the_Original_setting()
    {
        var steamwoven = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)), mapListed: map => map is Map or 9);
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.Session.EnsureTeleportMap(steamwoven.Session.Version, steamwoven.Npc, Home).Disposition);
        Assert.IsTrue(steamwoven.Host.RunWaypoints(steamwoven.Npc));
        Assert.IsTrue(steamwoven.Host.RunWaypoints(steamwoven.Npc), "the same NPC asking again keeps one request");
        Assert.AreEqual(1, steamwoven.Host.PendingHomecomings.Count);
        // The slot holds a request already (a hero's, or another NPC's): the NPC's waits for the next frame.
        Assert.IsTrue(steamwoven.Session.RequestChangeOfPlace(Entity(5), ChangeOfPlaceCaller.NpcWaypoint, 9, new(1, 1), ChangeOfPlaceFlags.None, 0, _ => true).Accepted);
        Assert.IsFalse(steamwoven.Host.FlushHomecomings(), "the slot is not free");
        Assert.AreEqual(Entity(5), steamwoven.Session.QueryChangeOfPlace()!.Entity, "nothing was lost");
        Assert.AreEqual(1, steamwoven.Host.PendingHomecomings.Count);
        // The queue is the scene's: a host built again goes on with it (a companion that went home rebuilds the host).
        var rebuilt = steamwoven.Rebuild();
        Assert.AreEqual(1, rebuilt.PendingHomecomings.Count);
        Assert.IsFalse(rebuilt.FlushHomecomings(), "the rebuilt host sees the same slot and the same waiting request");
        Assert.AreEqual(1, rebuilt.PendingHomecomings.Count);

        // The Original setting makes the request at once, over a pending one (RL21: the first is lost).
        var original = Open(Npc(Day | Night, [(20, 20)], 0, (1, 1), (1, 1)), rules: RuleMode.Original, mapListed: map => map is Map or 9);
        Assert.AreEqual(GameSessionDisposition.Changed, original.Session.EnsureTeleportMap(original.Session.Version, original.Npc, Home).Disposition);
        Assert.IsTrue(original.Session.RequestChangeOfPlace(Entity(5), ChangeOfPlaceCaller.NpcWaypoint, 9, new(1, 1), ChangeOfPlaceFlags.None, 0, _ => true).Accepted);
        Assert.IsTrue(original.Host.RunWaypoints(original.Npc));
        StringAssert.Contains(original.Host.TraceOf(original.Npc)!.Route, $"Overwrote (lost {Entity(5)})");
        Assert.AreEqual(original.Npc, original.Session.QueryChangeOfPlace()!.Entity);
        Assert.AreEqual(0, original.Host.PendingHomecomings.Count);
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
