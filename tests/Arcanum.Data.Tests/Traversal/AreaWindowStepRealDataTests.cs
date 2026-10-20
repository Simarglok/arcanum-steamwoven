using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Traversal;

/// <summary>
/// ARC-DATA-034 opt-in checks on the installed original (DAT_ROOT; the Crash Site comparison also needs the pinned
/// exterior inputs ARC_AREA_MODULE, ARC_AREA_RESOURCE1, ARC_AREA_RESOURCE2, ARC_AREA_MOBILE, ARC_AREA_PROTOTYPES,
/// ARC_AREA_EFFECTS, ARC_AREA_PATCH): step evidence of main-map windows, generated core sectors included. Prints counts only.
/// </summary>
[TestClass]
public sealed class AreaWindowStepRealDataTests
{
    private const string MainMap = "Arcanum1-024-fixed";
    private static readonly AreaStepCell Spawn = new(92934, 82561);

    [TestMethod, TestCategory("RealData")]
    public void The_crash_site_window_steps_like_the_pinned_exterior()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT")))
            Assert.Inconclusive("DAT_ROOT and the pinned exterior inputs are required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(1452, 1290));
        using var owner = planned.Plan!.Open();
        var window = AreaStepInspector.InspectWholeMap(owner.LoadStrict(), stack, planned.Plan.Map.AggregatePath, 7);
        using var pinnedOwner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var pinned = AreaStepInspector.ValidateOriginalData(pinnedOwner.LoadStrict(), module, Required("ARC_AREA_RESOURCE2"), Required("ARC_AREA_EFFECTS"),
            Required("ARC_AREA_PATCH"), Required("ARC_AREA_MOBILE"), 7);
        Print("window", window); Print("pinned", pinned);

        Assert.AreEqual(pinned.AdmittedCells, window.AdmittedCells);
        CollectionAssert.AreEquivalent(pinned.AdmittedSectors.ToList(), window.AdmittedSectors.ToList());
        var cells = pinned.AdmittedSectors.SelectMany(s => Enumerable.Range(0, 4096).Select(i => new AreaStepCell(s.X * 64 + i % 64, s.Y * 64 + i / 64))).ToArray();
        var terrain = cells.Count(c => Terrain(pinned.Terrain(c)) != Terrain(window.Terrain(c)));
        var objects = Evaluations(pinned).Except(Evaluations(window)).ToList();
        var extra = Evaluations(window).Except(Evaluations(pinned)).ToList();
        Console.WriteLine($"ARC-DATA-034 terrain differences={terrain} objects only pinned={objects.Count} only window={extra.Count}");
        foreach (var line in objects.Concat(extra).Take(20)) Console.WriteLine("ARC-DATA-034 diff " + line);
        // The same evidence on every core cell and for every object; the window's whole-map edge rules change nothing here
        // (no wall or portal edge in the Crash Site core).
        Assert.AreEqual(0, terrain);
        Assert.AreEqual(0, objects.Count + extra.Count);
        Assert.AreEqual(pinned.ObjectEvaluations.Count(), window.ObjectEvaluations.Count());
        Assert.AreEqual((0, 0, 0), (window.TerrainUnknownCells, window.UnknownObjects, window.WallEdges));
        var reach = Reach(window);
        Console.WriteLine($"ARC-DATA-034 reach window={reach} pinned={Reach(pinned)}");
        // The pinned exterior's frozen reach from the spawn (research F8).
        Assert.AreEqual(7150, reach);
        Assert.AreEqual(Reach(pinned), reach);
    }

    [TestMethod, TestCategory("RealData")]
    [DataRow(1409, 1314, 0, 717, "town")]
    [DataRow(1384, 239, 9, 3539, "elven forest wilderness")]
    [DataRow(1450, 1290, 5, 0, "generated centre")]
    public void Main_map_windows_build_step_evidence(int x, int y, int generatedCore, int walls, string what)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT"))) Assert.Inconclusive("DAT_ROOT is required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(x, y));
        using var owner = planned.Plan!.Open();
        var steps = AreaStepInspector.InspectWholeMap(owner.LoadStrict(), stack, planned.Plan.Map.AggregatePath, 7);
        Print(what, steps);
        Assert.AreEqual(9 * 4096, steps.AdmittedCells, what);
        var reasons = Evaluations(steps).Where(e => e.Contains("|Unknown|", StringComparison.Ordinal)).Select(e => e.Split('|')[^1]).GroupBy(r => r)
            .Select(g => $"{g.Key}={g.Count()}");
        Console.WriteLine($"ARC-DATA-034 {what} unknown reasons: {string.Join(", ", reasons)}");
        // No gap: terrain is known everywhere, generated sectors included, and so is every object (ARC-DATA-035: the town's
        // critter with a speed effect, which the hit-point rule does not read, is no longer Unknown).
        Assert.AreEqual(0, steps.TerrainUnknownCells, what);
        Assert.AreEqual(0, steps.UnknownObjects, what);
        // The generated core sectors and the wall edges.
        var generated = planned.Plan.Plan.Window!.Sectors.Count(s => s.Scope == AreaScope.Core && s.Template is not null);
        Console.WriteLine($"ARC-DATA-034 {what} generated core={generated}");
        Assert.AreEqual(generatedCore, generated, what);
        Assert.AreEqual(walls, steps.WallEdges, what);
        Assert.AreEqual(generated > 0, steps.InputIdentity.Sources.Any(s => s.Role.StartsWith("area-terraintemplate-", StringComparison.Ordinal)), what);
    }

    [TestMethod, TestCategory("RealData")]
    public void A_window_route_walks_from_the_crash_site_to_shrouded_hills()
    {
        // ARC-WORLD-042: the new-game start (the map-list start tile) to the middle of the Shrouded Hills town sector
        // (1409, 1314), through the generated wilderness south and west of the Crash Site plateau.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT"))) Assert.Inconclusive("DAT_ROOT is required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        AreaStepCell from = new(92958, 82592), to = new(1409 * 64 + 32, 1314 * 64 + 32);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var route = AreaWindowRoute.Plan(stack, catalog, MainMap, from, to);
        var sectors = route.Cells.Select(c => (c.X >> 6, c.Y >> 6)).Distinct().Count();
        Console.WriteLine($"ARC-WORLD-042 route {route.Status} {route.Detail} cells={route.Cells.Count} sectors={sectors} windows={route.WindowLoads} " +
            $"expansions={route.Expansions} seconds={clock.Elapsed.TotalSeconds:F0}");
        Assert.AreEqual(AreaWindowRouteStatus.Found, route.Status, route.Detail);
        Assert.AreEqual((from, to), (route.Cells[0], route.Cells[^1]));
        for (var i = 1; i < route.Cells.Count; i++)
            Assert.AreEqual(1L, Math.Max(Math.Abs(route.Cells[i].X - route.Cells[i - 1].X), Math.Abs(route.Cells[i].Y - route.Cells[i - 1].Y)), $"step {i}");
        // Far longer than the straight line (2,750 cells): the way off the plateau goes south first.
        Assert.IsTrue(route.Cells.Count > 2751 && sectors > 40, $"cells {route.Cells.Count} sectors {sectors}");
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_arrival_on_the_main_map_opens_a_complete_window()
    {
        // ARC-DATA-038: every transition of any listed map into the main map (runtime 1), guarded or not, arrives in a window
        // that loads complete, with step evidence admitting the arrival. A window around Caladon (1603, 1825) held scenery whose
        // ART id is 0xFFFFFFFF and was refused whole before.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT"))) Assert.Inconclusive("DAT_ROOT is required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var list = MapList.Read(stack);
        using var scripts = Arcanum.Data.Scripts.ScriptContent.Over(stack);
        var arrivals = new List<WorldLocation>();
        foreach (var map in catalog.Maps.Where(m => m.SectorFiles.Count > 0 && list.Find(m.Name) is not null))
            arrivals.AddRange(MapTransitions.Read(catalog, list, map, scripts).Sites.Where(s => s.DestinationRuntime == 1).Select(s => s.Arrival));
        var centres = arrivals.Distinct().GroupBy(a => WorldSectorCoordinate.Containing(a)).ToArray();
        Console.WriteLine($"ARC-DATA-038 arrivals={arrivals.Count} distinct={arrivals.Distinct().Count()} windows={centres.Length}");
        Assert.IsTrue(centres.Length > 1, "arrivals beyond the Crash Site");
        foreach (var centre in centres)
        {
            var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, centre.Key);
            Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, $"{centre.Key} {planned.Detail}");
            using var owner = planned.Plan!.Open();
            var inspection = owner.Inspect();
            Assert.IsTrue(inspection.IsComplete, $"{centre.Key}: {string.Join(", ", inspection.Issues)}");
            var steps = AreaStepInspector.InspectWholeMap(inspection.Snapshot!, stack, planned.Plan.Map.AggregatePath, 7);
            foreach (var arrival in centre) Assert.IsTrue(steps.IsAdmitted(new(arrival.X, arrival.Y)), $"{centre.Key} {arrival}");
        }
    }

    private static string Terrain(AreaStepTerrainEvidence? e) => e is null ? "-" : $"{e.RawArt}|{e.Coverage}|{e.Disposition}|{e.Source}|{e.ReasonCode}";

    private static IEnumerable<string> Evaluations(AreaStepSnapshot s) =>
        s.ObjectEvaluations.Select(e => $"{e.Cell}|{e.Key.Kind}|{e.Key.Sector}|{e.Key.SourceIndex}|{e.Key.Offset}|{e.ObjectType}|{e.Coverage}|{e.Disposition}|{e.Reason}");

    private static int Reach(AreaStepSnapshot snapshot)
    {
        var reached = new HashSet<AreaStepCell> { Spawn }; var queue = new Queue<AreaStepCell>([Spawn]);
        while (queue.Count != 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in Enum.GetValues<CardinalTraversalDirection>())
            {
                var next = Step(cell, direction);
                if (snapshot.Assess(snapshot.Request(cell, next, direction)).Disposition == CardinalTraversalDisposition.Open && reached.Add(next)) queue.Enqueue(next);
            }
        }
        return reached.Count;
    }

    private static AreaStepCell Step(AreaStepCell cell, CardinalTraversalDirection direction) => direction switch
    {
        CardinalTraversalDirection.XMinusYMinus => new(cell.X - 1, cell.Y - 1),
        CardinalTraversalDirection.XMinus => new(cell.X - 1, cell.Y),
        CardinalTraversalDirection.XMinusYPlus => new(cell.X - 1, cell.Y + 1),
        CardinalTraversalDirection.YPlus => new(cell.X, cell.Y + 1),
        CardinalTraversalDirection.XPlusYPlus => new(cell.X + 1, cell.Y + 1),
        CardinalTraversalDirection.XPlus => new(cell.X + 1, cell.Y),
        CardinalTraversalDirection.XPlusYMinus => new(cell.X + 1, cell.Y - 1),
        _ => new(cell.X, cell.Y - 1),
    };

    private static void Print(string name, AreaStepSnapshot s) =>
        Console.WriteLine($"ARC-DATA-034 {name} cells={s.AdmittedCells} terrainBlocked={s.TerrainBlockedCells} terrainUnknown={s.TerrainUnknownCells} " +
            $"objectBlockers={s.ObjectBlockerCells} unknownObjects={s.UnknownObjects} walls={s.WallEdges}");

    private static ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(Environment.GetEnvironmentVariable("DAT_ROOT")!);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
        : throw new AssertInconclusiveException($"{name} is required.");
}
