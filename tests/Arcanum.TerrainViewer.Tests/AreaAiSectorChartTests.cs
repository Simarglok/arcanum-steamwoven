using System.Collections.Immutable;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b): the AI host over the loaded-sector chart, over invented sectors (64 × 64 open cells each), no original data: a human NPC (0) at
/// world cell (62, 10) next to the border of sector (0, 0) and (1, 0). The scene's own evidence is a window of sector 0; the chart's evidence is what the
/// session's cache has loaded. The planners read the chart when the scene has one, a goal in an unloaded sector is touched (the session loads it, the chart
/// is told), a relocation may land in a loaded sector the window does not hold, and the viewer's chart driver follows the cache's slots on a task.
/// </summary>
[TestClass]
public sealed class AreaAiSectorChartTests
{
    private const int Map = 1;
    private static readonly CellCoord HeroCell = new(14, 20), NpcCell = new(62, 10);
    private const string Profile = "arc-data-019-whole-map-original-rules-v1";

    private static AreaObjectKey Key(int index) => new(new string('a', 64), Map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
    private static long At(int x, int y) => ((long)y << 32) | (uint)x;

    // The human NPC of the route tests' fixture (no waypoints) with the given standpoints.
    private static AreaObjectRecord Npc((int X, int Y) standpoint) =>
        new(Key(0), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, NpcCell.X, NpcCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(61, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), new(76, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(77, 0),
            Field(78, At(standpoint.X, standpoint.Y)), Field(79, At(standpoint.X, standpoint.Y)), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private static AreaStepSnapshot Sector(long sx, long sy)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = sx * 64; x < sx * 64 + 64; x++)
            for (var y = sy * 64; y < sy * 64 + 64; y++)
                terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new(new(AreaStepInputKind.Structural, Profile, [], $"sector-{sx}-{sy}"), 1, [(sx, sy)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private sealed class Scene : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public required GameSessionOwner Session { get; init; }
        public required AreaAiHost Host { get; init; }
        public AreaStepSnapshot? Loaded { get; set; }
        public int Touched { get; set; }
        public string Npc => Entity(0);
    }

    private static GameSessionOwner NewSession()
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(Map), HeroCell, NeighborDirection.XPlus), Hero: hero));
        return created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
    }

    /// <param name="own">The scene's own evidence; <paramref name="withChart"/> gives the scene a loaded-sector provider (the test sets <see cref="Scene.Loaded"/>).</param>
    private static Scene Open(AreaStepSnapshot own, bool withChart, (int X, int Y)? standpoint = null, bool cache = false)
    {
        var row = Npc(standpoint ?? (1, 1));
        var session = NewSession();
        var map = new AreaInventoryMap(Map, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [])], []), new Dictionary<string, AreaObjectRecord> { [Entity(0)] = row });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        if (cache)
        {
            var opened = AreaResidency.OpenWhole(session, new(Map), new("test", new(MapResidentRowsStatus.Ready, [new(Entity(0), SessionResidentKind.Npc, NpcCell)], ""), new(4, 4)), null);
            Assert.AreEqual(ResidencyStatus.Open, opened!.Residency.Status, opened.Residency.Detail);
            Assert.AreEqual(GameSessionFailure.None, session.TouchSectors(session.Version, new(Map), [NpcCell]).Failure);
        }
        Scene? scene = null;
        var aiScene = new AreaAiScene
        {
            RuntimeMap = Map, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = own, Npcs = [new(Entity(0), row, NpcCell)], Combat = combat, HeroCell = () => HeroCell,
            LoadedSteps = withChart ? () => scene!.Loaded : null, SectorsTouched = withChart ? () => scene!.Touched++ : null,
        };
        AreaAiHost? host = null;
        var events = new AreaEventWorld(() => combat, () => null, ai: () => host);
        host = new(session, aiScene, events, new AreaCombatWorld(Map, () => [], _ => null));
        return scene = new() { Session = session, Host = host };
    }

    private static AiPath? Plan(Scene scene, CellCoord goal) => scene.Host.PlanPath(scene.Npc, new(AiPathKind.ToTile, null, goal, 0, false, false));

    [TestMethod]
    public void The_planner_reads_the_loaded_chart_instead_of_the_scenes_evidence()
    {
        var both = AreaStepSnapshot.Union([Sector(0, 0), Sector(1, 0)]);
        var goal = new CellCoord(66, 10);
        using var window = Open(Sector(0, 0), withChart: false);
        Assert.IsNull(Plan(window, goal), "the window holds sector 0 only: the goal is outside its evidence");

        using var chart = Open(Sector(0, 0), withChart: true);
        Assert.IsNull(Plan(chart, goal), "a chart with no sector yet falls back to the scene's evidence");
        chart.Loaded = both;
        var path = Plan(chart, goal);
        Assert.IsNotNull(path);
        Assert.AreEqual(goal, path.Steps[^1]);
        Assert.IsTrue(path.Steps.Any(cell => cell.X >= 64), "the route crosses into sector 1");

        chart.Loaded = Sector(0, 0);
        Assert.IsNull(Plan(chart, goal), "the chart wins over the scene: a sector it does not hold is out");
        using var wide = Open(both, withChart: true);
        wide.Loaded = Sector(0, 0);
        Assert.IsNull(Plan(wide, goal), "even when the scene's own evidence holds it");
    }

    [TestMethod]
    public void A_goal_in_an_unloaded_sector_is_touched_once_and_the_chart_is_told()
    {
        using var scene = Open(Sector(0, 0), withChart: true, cache: true);
        var map = new SessionMapId(Map);
        Assert.AreEqual(false, scene.Session.QuerySectorLoaded(map, new(1, 0)));
        Assert.IsNull(Plan(scene, new(66, 10)), "the chart holds nothing of sector 1 yet");
        Assert.AreEqual(true, scene.Session.QuerySectorLoaded(map, new(1, 0)), "the planner's goal sector was locked in the cache");
        Assert.AreEqual(1, scene.Touched);
        Plan(scene, new(66, 11));
        Assert.AreEqual(1, scene.Touched, "a loaded sector is not touched again");
        Plan(scene, new(60, 10));
        Assert.AreEqual(1, scene.Touched, "nor the sector the NPC stands in");
        Assert.AreEqual(true, scene.Session.QuerySectorLoaded(map, new(0, 0)));
    }

    [TestMethod]
    public void No_chart_or_no_cache_touches_nothing()
    {
        using var noChart = Open(Sector(0, 0), withChart: false, cache: true);
        Plan(noChart, new(66, 10));
        Assert.AreEqual(false, noChart.Session.QuerySectorLoaded(new(Map), new(1, 0)), "a whole map's host reads its own evidence and touches nothing");
        using var noCache = Open(Sector(0, 0), withChart: true);
        Plan(noCache, new(66, 10));
        Assert.AreEqual(0, noCache.Touched);
        Assert.IsNull(noCache.Session.QuerySectorLoaded(new(Map), new(1, 0)), "no cache, no touch");
    }

    [TestMethod]
    public void A_relocation_may_land_in_a_loaded_sector_the_window_does_not_hold()
    {
        using var window = Open(Sector(0, 0), withChart: false, standpoint: (70, 10));
        window.Host.RunStandpoints(window.Npc);
        Assert.AreEqual(NpcCell, window.Host.Tile(window.Npc), "the standpoint is outside the window's evidence: held back");

        using var chart = Open(Sector(0, 0), withChart: true, standpoint: (70, 10));
        chart.Loaded = AreaStepSnapshot.Union([Sector(0, 0), Sector(1, 0)]);
        chart.Host.RunStandpoints(chart.Npc);
        Assert.AreEqual(new CellCoord(70, 10), chart.Host.Tile(chart.Npc), "a loaded sector holds it");
    }

    private sealed class Port(Action<ResidentSector>? onRead = null, Action? onDispose = null) : ISectorTerrain, IDisposable
    {
        public SectorTerrainRead Read(ResidentSector sector)
        {
            onRead?.Invoke(sector);
            return new(SectorTerrainStatus.Ready, new(sector, Sector(sector.X, sector.Y), []), "ok");
        }
        public void Dispose() => onDispose?.Invoke();
    }

    private static ImmutableArray<SectorCacheSlot> Slots(params (int X, int Y)[] sectors) =>
        [.. sectors.Select((s, i) => new SectorCacheSlot(SectorCacheRules.SectorId(s.X, s.Y), i + 1))];

    private static void WaitIdle(AreaSectorChart chart)
    {
        var until = Environment.TickCount64 + 20_000;
        while (!chart.Idle) { Assert.IsTrue(Environment.TickCount64 < until, "the chart's sync did not end"); Thread.Sleep(5); }
    }

    [TestMethod]
    public void The_driver_follows_the_cache_on_a_task_and_closes_its_port_when_it_is_dropped()
    {
        var reads = new List<ResidentSector>(); var opened = 0; var disposed = 0;
        var chart = new AreaSectorChart(() => { opened++; return new Port(reads.Add, () => disposed++); });
        Assert.IsNull(chart.Steps);
        chart.Request(Slots((3, 3), (4, 3)));
        WaitIdle(chart);
        Assert.AreEqual(8192, chart.Steps!.AdmittedCells);
        CollectionAssert.AreEquivalent(new[] { new ResidentSector(3, 3), new ResidentSector(4, 3) }, reads.ToArray());
        Assert.IsTrue(chart.Log.TryDequeue(out var line));
        StringAssert.Contains(line, "read=3,3 4,3");

        chart.Request(Slots((4, 3), (3, 3)));
        WaitIdle(chart);
        Assert.AreEqual(2, reads.Count, "the same sectors in another order are no new request");
        chart.Request(Slots((4, 3), (5, 3)));
        WaitIdle(chart);
        CollectionAssert.AreEquivalent(new[] { (4L, 3L), (5L, 3L) }, chart.Steps!.AdmittedSectors.ToArray());
        Assert.AreEqual(1, opened, "the port is opened once, on the task");
        Assert.AreEqual(0, disposed);

        chart.Dispose();
        var until = Environment.TickCount64 + 5_000;
        while (disposed == 0 && Environment.TickCount64 < until) Thread.Sleep(5);
        Assert.AreEqual(1, disposed, "the port is closed with the chart");
        chart.Request(Slots((9, 9)));
        Assert.IsTrue(chart.Idle, "a dropped chart starts nothing");
    }

    [TestMethod]
    public void A_sync_that_throws_is_not_repeated_for_the_same_sectors_every_frame()
    {
        var reads = 0;
        var chart = new AreaSectorChart(() => new Port(_ => { reads++; throw new IOException("lost"); }));
        chart.Request(Slots((3, 3)));
        WaitIdle(chart);
        Assert.AreEqual(1, reads);
        Assert.IsTrue(chart.Log.TryDequeue(out var line));
        StringAssert.Contains(line, "chart sync failed IOException");
        for (var frame = 0; frame < 5; frame++) { chart.Request(Slots((3, 3))); WaitIdle(chart); }
        Assert.AreEqual(1, reads, "the same set is not synced again");
        chart.Request(Slots((3, 3), (4, 3)));
        WaitIdle(chart);
        Assert.AreEqual(2, reads, "a changed set asks once more");
        chart.Dispose();
    }

    [TestMethod]
    public void A_port_that_cannot_open_gives_failed_reads_not_an_exception()
    {
        var chart = new AreaSectorChart(() => null);
        chart.Request(Slots((3, 3)));
        WaitIdle(chart);
        Assert.IsNull(chart.Steps);
        Assert.IsTrue(chart.Log.TryDequeue(out var line));
        StringAssert.Contains(line, "failed=3,3:Refused the sector port is unavailable");
        chart.Dispose();
    }
}
