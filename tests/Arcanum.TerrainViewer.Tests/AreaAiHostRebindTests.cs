using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-119 (MIG9 S5c): the AI host is the map's, not the window's. A window move gives the running host the next window's scene (<see cref="AreaAiHost.Rebind"/>):
/// its goal runs and movers stay, the NPCs of the window it left stay with it, the new window's rows and providers are used, a mover takes the new window's record key,
/// and a map with a loaded-sector chart places a resident anywhere on the map. A planned path touches the sectors it goes through (the corridor from the NPC to the goal),
/// at most <see cref="AreaAiHost.PathTouchLimit"/> unloaded ones. Invented sectors (64 × 64 open cells), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostRebindTests
{
    private const int Map = 1;
    private static readonly CellCoord HeroCell = new(58, 12), CellA = new(62, 10), CellB = new(130, 10);
    private const string Profile = "arc-data-019-whole-map-original-rules-v1";

    private static AreaObjectKey Key(int index) => new(new string('a', 64), Map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
    private static long At(int x, int y) => ((long)y << 32) | (uint)x;

    // The human NPC of the route tests' fixture (no waypoints) standing on a cell, with a standpoint; the row's key is Key(keyIndex).
    private static AreaObjectRecord Npc(int keyIndex, CellCoord cell, (int X, int Y) standpoint) =>
        new(Key(keyIndex), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, cell.X, cell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(61, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), new(76, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(77, 0),
            Field(78, At(standpoint.X, standpoint.Y)), Field(79, At(standpoint.X, standpoint.Y)), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    internal static AreaStepSnapshot Sector(long sx, long sy)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = sx * 64; x < sx * 64 + 64; x++)
            for (var y = sy * 64; y < sy * 64 + 64; y++)
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new(new(AreaStepInputKind.Structural, Profile, [], $"sector-{sx}-{sy}"), 1, [(sx, sy)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    internal sealed class Fixture : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public required GameSessionOwner Session { get; init; }
        public required AreaInventoryHost Inventory { get; init; }
        public required AreaCombatHost Combat { get; init; }
        public required AreaObjectRecord RowA { get; init; }
        public required AreaObjectRecord RowB { get; init; }
        public AreaStepSnapshot? Loaded { get; set; }
        public int Touched { get; set; }
        public AreaAiHost? Host { get; set; }
        public AreaEventWorld? Events { get; private set; }
        public string A => Entity(0);
        public string B => Entity(1);

        /// <summary>One window's scene: its chart origin, own evidence and the NPCs its rows hold; the loaded-sector chart when <paramref name="withChart"/>.</summary>
        public AreaAiScene Scene(long originX, AreaStepSnapshot own, IReadOnlyList<AreaAiNpc> npcs, bool withChart = false) => new()
        {
            RuntimeMap = Map, Chart = new AreaChart(originX, 0, new(0, 0), "TEST") { MinimumCell = -64, MaximumCell = 127 }, Steps = own, Npcs = npcs, Combat = Combat,
            HeroCell = () => HeroCell,
            LoadedSteps = withChart ? () => Loaded : null, SectorsTouched = withChart ? () => Touched++ : null,
        };

        public AreaAiHost Build(AreaAiScene scene)
        {
            var events = Events = new AreaEventWorld(() => Combat, () => null, ai: () => Host);
            return Host = new AreaAiHost(Session, scene, events, new AreaCombatWorld(Map, () => [], _ => null));
        }
    }

    internal static Fixture Open((int X, int Y)? standpointA = null)
    {
        var rowA = Npc(0, CellA, standpointA ?? (1, 1));
        var rowB = Npc(1, CellB, (1, 1));
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(Map), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var map = new AreaInventoryMap(Map, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], []), new(Entity(1), SessionHolderKind.Npc, false, false, [], [])], []),
            new Dictionary<string, AreaObjectRecord> { [Entity(0)] = rowA, [Entity(1)] = rowB });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        return new() { Session = session, Inventory = inventory, Combat = new AreaCombatHost(inventory, session: () => session), RowA = rowA, RowB = rowB };
    }

    internal static AreaAiNpc NpcA(Fixture f, AreaObjectRecord? row = null) => new(f.A, row ?? f.RowA, CellA);
    private static AreaAiNpc NpcB(Fixture f) => new(f.B, f.RowB, CellB);

    [TestMethod]
    public void A_window_move_keeps_the_runs_the_movers_and_the_npcs_of_the_window_left()
    {
        using var f = Open();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        var run = host.RunOf(f.A);
        Assert.IsNotNull(run, "the attack starter gave the NPC a run: " + host.TraceOf(f.A)?.Perform);
        Assert.IsNull(host.Tile(f.B), "the first window holds no row of B");
        host.Perform(f.B);
        Assert.IsNull(host.RunOf(f.B), "an NPC no window has held yet is answered as unknown: no run, no failure");

        var second = f.Scene(64, AreaStepSnapshot.Union([Sector(1, 0), Sector(2, 0)]), [NpcB(f)]);
        host.Rebind(second);

        Assert.AreSame(run, host.RunOf(f.A), "the NPC's goal run is the map's: a window move does not end it");
        Assert.AreEqual(CellA, host.Tile(f.A), "the NPC of the window left is still known, where it stands");
        Assert.AreEqual(CellB, host.Tile(f.B), "the new window's NPC is known");
        CollectionAssert.AreEquivalent(new[] { f.A, f.B }, host.Scene.Npcs.Select(n => n.Entity).ToArray());
        Assert.AreEqual(second.Chart, host.Scene.Chart, "the chart, the evidence and the providers are the new window's");
        Assert.AreSame(second.Steps, host.Scene.Steps);
        var path = host.PlanPath(f.B, new(AiPathKind.ToTile, null, new CellCoord(140, 10), 0, false, false));
        Assert.IsNotNull(path, "B plans over the new window's evidence and chart");
        Assert.AreEqual(new CellCoord(140, 10), path.Steps[^1], "paths stay in world cells whatever the chart origin");
    }

    [TestMethod]
    public void A_mover_takes_the_record_key_of_the_window_that_holds_its_npc_again_and_keeps_its_cell()
    {
        using var f = Open(standpointA: (40, 40));
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        host.RunStandpoints(f.A);
        Assert.AreEqual(new CellCoord(40, 40), host.Tile(f.A), host.TraceOf(f.A)?.Route);
        Assert.AreEqual(Key(0), host.Movers.Of(f.A)!.Key);

        var other = Npc(5, CellA, (40, 40));
        host.Rebind(f.Scene(0, Sector(0, 0), [NpcA(f, other)]));
        Assert.AreEqual(Key(5), host.Movers.Of(f.A)!.Key, "the T0 record the overlay hides is the new window's");
        Assert.AreEqual(new CellCoord(40, 40), host.Tile(f.A), "where the NPC stands is the host's");
        Assert.IsTrue(host.Movers.Moved.Contains(Key(5)));
    }

    [TestMethod]
    public void A_host_serves_one_map()
    {
        using var f = Open();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        var elsewhere = f.Scene(0, Sector(0, 0), [NpcA(f)]) with { RuntimeMap = Map + 1 };
        Assert.Throws<ArgumentException>(() => host.Rebind(elsewhere));
        Assert.AreEqual(Map, host.Scene.RuntimeMap);
    }

    [TestMethod]
    public void A_resident_may_be_placed_anywhere_on_the_map_when_the_loaded_chart_holds_the_cell_not_only_inside_the_windows_chart()
    {
        // The standpoint (150, 10) is in sector 2, past the window chart's cells (-64 to 127).
        using var window = Open(standpointA: (150, 10));
        var wholeMap = window.Build(window.Scene(0, AreaStepSnapshot.Union([Sector(0, 0), Sector(2, 0)]), [NpcA(window)]));
        wholeMap.RunStandpoints(window.A);
        Assert.AreEqual(CellA, wholeMap.Tile(window.A), "a map without a loaded-sector chart keeps a resident inside its chart");

        using var chart = Open(standpointA: (150, 10));
        chart.Loaded = AreaStepSnapshot.Union([Sector(0, 0), Sector(1, 0), Sector(2, 0)]);
        var host = chart.Build(chart.Scene(0, Sector(0, 0), [NpcA(chart)], withChart: true));
        host.RunStandpoints(chart.A);
        Assert.AreEqual(new CellCoord(150, 10), host.Tile(chart.A), host.TraceOf(chart.A)?.Route);

        using var unloaded = Open(standpointA: (150, 10));
        unloaded.Loaded = Sector(0, 0);
        var held = unloaded.Build(unloaded.Scene(0, Sector(0, 0), [NpcA(unloaded)], withChart: true));
        held.RunStandpoints(unloaded.A);
        Assert.AreEqual(CellA, held.Tile(unloaded.A), "a sector the chart has not loaded does not hold it");
    }

    // A session with a sector cache over a whole map of 8 × 8 sectors, the NPC's sector loaded.
    private static Fixture WithCache()
    {
        var f = Open();
        var opened = AreaResidency.OpenWhole(f.Session, new(Map), new("test", new(MapResidentRowsStatus.Ready, [new(f.A, SessionResidentKind.Npc, CellA)], ""), new(8, 8)), null);
        Assert.AreEqual(ResidencyStatus.Open, opened!.Residency.Status, opened.Residency.Detail);
        Assert.AreEqual(GameSessionFailure.None, f.Session.TouchSectors(f.Session.Version, new(Map), [CellA]).Failure);
        return f;
    }

    private static bool? Loaded(Fixture f, int sx, int sy) => f.Session.QuerySectorLoaded(new(Map), new(sx, sy));

    [TestMethod]
    public void A_planned_path_touches_the_sectors_it_goes_through_not_only_the_goals()
    {
        using var f = WithCache();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)], withChart: true));
        Assert.IsNull(host.PlanPath(f.A, new(AiPathKind.ToTile, null, new CellCoord(200, 10), 0, false, false)), "the chart holds nothing of the sectors yet");
        Assert.AreEqual(true, Loaded(f, 1, 0), "the sector between the NPC and the goal is locked");
        Assert.AreEqual(true, Loaded(f, 2, 0));
        Assert.AreEqual(true, Loaded(f, 3, 0), "and the goal's");
        Assert.AreEqual(false, Loaded(f, 4, 0), "nothing beyond the goal");
        Assert.AreEqual(false, Loaded(f, 1, 1), "nor beside the line");
        Assert.AreEqual(1, f.Touched, "the chart is told once per plan");
        host.PlanPath(f.A, new(AiPathKind.ToTile, null, new CellCoord(200, 10), 0, false, false));
        Assert.AreEqual(1, f.Touched, "a corridor of loaded sectors touches nothing");
    }

    [TestMethod]
    public void A_long_path_loads_the_nearest_sectors_first_and_at_most_the_limit_at_once()
    {
        using var f = WithCache();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)], withChart: true));
        var far = new AiPathRequest(AiPathKind.ToTile, null, new CellCoord(500, 10), 0, false, false);
        host.PlanPath(f.A, far);
        var loaded = Enumerable.Range(1, 7).Where(x => Loaded(f, x, 0) == true).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, AreaAiHost.PathTouchLimit).ToArray(), loaded, "the four nearest to the NPC");
        host.PlanPath(f.A, far);
        Assert.AreEqual(true, Loaded(f, 7, 0), "the next plan loads the rest of the corridor, the goal's sector included");
        Assert.AreEqual(2, f.Touched);
    }

    [TestMethod]
    public void An_npc_a_window_adds_stands_where_the_session_recorded_it_and_its_route_steps_run_once_a_window_holds_it()
    {
        using var f = Open();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        host.RunStandpoints(f.B);
        Assert.IsNull(host.Tile(f.B), "no window has held B: the fold's wake-up step finds no row");
        Assert.AreEqual(GameSessionFailure.None, f.Session.RecordNpcPlace(f.Session.Version, new(Map), f.B, new CellCoord(140, 10), NeighborDirection.XPlus).Failure);
        host.Rebind(f.Scene(64, AreaStepSnapshot.Union([Sector(1, 0), Sector(2, 0)]), [NpcB(f)]));
        Assert.AreEqual(new CellCoord(140, 10), host.Tile(f.B), "the place the session holds, as a host built for the window would restore it");

        using var g = Open();
        var gate = g.Build(g.Scene(0, Sector(0, 0), [NpcA(g)]));
        gate.RunStandpoints(g.B);
        var standing = Npc(1, CellB, (135, 10));
        gate.Rebind(g.Scene(64, AreaStepSnapshot.Union([Sector(1, 0), Sector(2, 0)]), [new(g.B, standing, CellB)]));
        gate.RunStandpoints(g.B);
        Assert.AreEqual(new CellCoord(135, 10), gate.Tile(g.B), "the earlier miss was not kept: " + gate.TraceOf(g.B)?.Route);
    }

    [TestMethod]
    public void The_host_forgets_an_npc_that_stands_still_and_runs_nothing_when_its_window_is_left_and_takes_it_back_with_the_window()
    {
        using var f = Open();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        var second = f.Scene(64, AreaStepSnapshot.Union([Sector(1, 0), Sector(2, 0)]), [NpcB(f)]);
        host.Rebind(second);
        Assert.IsNull(host.Tile(f.A), "A runs nothing and never moved: its row goes with its window");
        CollectionAssert.AreEqual(new[] { f.B }, host.Scene.Npcs.Select(n => n.Entity).ToArray());
        host.Rebind(f.Scene(0, Sector(0, 0), [NpcA(f)]));
        Assert.AreEqual(CellA, host.Tile(f.A));
        CollectionAssert.AreEqual(new[] { f.A }, host.Scene.Npcs.Select(n => n.Entity).ToArray());
    }

    // Loads 16 other sectors, so the least recent one (the NPC's, touched at the open) leaves the 16 slots.
    private static void EvictSectorZero(Fixture f) =>
        Assert.AreEqual(GameSessionFailure.None, f.Session.TouchSectors(f.Session.Version, new(Map),
            [.. Enumerable.Range(0, 8).SelectMany(x => new[] { 1, 2 }.Select(y => new CellCoord(x * 64, y * 64)))]).Failure);

    [TestMethod]
    public void The_runs_of_an_npc_whose_sector_was_evicted_wait_and_planning_does_not_load_its_own_sector()
    {
        using var f = WithCache();
        var host = f.Build(f.Scene(0, Sector(0, 0), [NpcA(f)], withChart: true));
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsNotNull(host.RunOf(f.A), host.TraceOf(f.A)?.Perform);
        EvictSectorZero(f);
        Assert.AreEqual(false, Loaded(f, 0, 0));
        Assert.AreEqual(0, host.Pump(1_000_000), "no step for an NPC that is not alive in the cache");
        host.PlanPath(f.A, new(AiPathKind.ToTile, null, new CellCoord(200, 10), 0, false, false));
        Assert.AreEqual(true, Loaded(f, 1, 0), "the corridor beyond the NPC's sector is touched");
        Assert.AreEqual(false, Loaded(f, 0, 0), "its own sector is the cache's to fold, not the planner's");
        Assert.AreEqual(GameSessionFailure.None, f.Session.TouchSectors(f.Session.Version, new(Map), [CellA]).Failure);
        Assert.IsTrue(host.Pump(2_000_000) > 0, "the run goes on once the sector is loaded");
    }

    [TestMethod]
    public void A_map_without_a_loaded_chart_touches_nothing()
    {
        using var noChart = WithCache();
        var host = noChart.Build(noChart.Scene(0, Sector(0, 0), [NpcA(noChart)]));
        host.PlanPath(noChart.A, new(AiPathKind.ToTile, null, new CellCoord(200, 10), 0, false, false));
        Assert.AreEqual(false, Loaded(noChart, 1, 0), "a whole map reads its own evidence and touches nothing");
        Assert.AreEqual(0, noChart.Touched);
    }
}
