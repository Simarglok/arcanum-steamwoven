using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Traversal;

/// <summary>ARC-DATA-034/035 on a synthetic installation: step evidence and lighting of a window whose core sector is generated
/// from a terrain template held by a base archive or as a loose file, bound to the snapshot's payload; a changed template is
/// refused.</summary>
[TestClass]
public sealed class AreaWindowStepTests
{
    private const string Map = "test";
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-windowstep-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void A_generated_core_sector_is_inspected_at_its_real_coordinates_from_its_template()
    {
        Install(Sector());
        using var stack = Stack();
        var (snapshot, plan) = Load(stack);
        var core = snapshot.Sectors.Single(s => s.Scope == AreaScope.Core);
        Assert.AreEqual((new AreaSectorCoordinate(0, 0), (AreaInputKey?)new AreaInputKey(AreaSourceRole.TerrainTemplate)), (core.Coordinate, core.Template));
        Assert.AreEqual("terrain/grass/0.sec", snapshot.Window!.Sectors.Single(s => s.Scope == AreaScope.Core).TemplatePath);

        var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 3);
        Assert.AreEqual(4096, steps.AdmittedCells);
        CollectionAssert.AreEqual(new[] { (0L, 0L) }, steps.AdmittedSectors.ToArray());
        Assert.IsNotNull(steps.Terrain(new(63, 63)));
        Assert.IsNull(steps.Terrain(new(64, 0)));
        Assert.IsTrue(steps.InputIdentity.Sources.Any(s => s.Role == "area-terraintemplate-0"));
    }

    [TestMethod]
    public void A_template_changed_since_loading_is_refused()
    {
        Install(Sector());
        using var stack = Stack();
        var (snapshot, plan) = Load(stack);
        // The archive is rewritten with another template payload after the snapshot was built.
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Base(Sector(Record(Id(0)))));
        var error = Assert.ThrowsExactly<AreaStepInvocationException>(() => AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 3));
        Assert.AreEqual((AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Terrain), (error.Code, error.Stage));
    }

    [TestMethod]
    public void A_loose_template_is_read_through_the_stack_for_evidence_and_lighting()
    {
        // ARC-DATA-035: a template the stack holds as a loose file, with its own light scheme 5 (the map default is 2).
        Install(Sector());
        Loose("data/terrain/grass/0.sec", WithScheme(Sector(), 5));
        using var stack = Stack();
        var (snapshot, plan) = Load(stack);
        var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 3);
        Assert.AreEqual(4096, steps.AdmittedCells);
        var lighting = Arcanum.Data.Lighting.AreaLightingReader.Read(stack, snapshot);
        Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.None, lighting.Failure);
        Assert.AreEqual(5, lighting.Source!.SectorSchemes[new AreaSectorCoordinate(0, 0).Packed]);
    }

    [TestMethod]
    public void A_generated_core_sector_takes_its_light_scheme_from_its_template()
    {
        // Scheme word 0 is the map default's sentinel; 5 is the template's own scheme.
        foreach (var (scheme, expected) in new[] { (0, 0), (5, 5) })
        {
            Install(WithScheme(Sector(), scheme));
            using var stack = Stack();
            var (snapshot, _) = Load(stack);
            var lighting = Arcanum.Data.Lighting.AreaLightingReader.Read(stack, snapshot);
            Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.None, lighting.Failure, $"scheme {scheme}");
            Assert.AreEqual(expected, lighting.Source!.SectorSchemes[new AreaSectorCoordinate(0, 0).Packed]);
            Assert.AreEqual(2, lighting.Source.MapDefaultScheme);
        }
    }

    [TestMethod]
    public void Lighting_refuses_a_template_changed_since_loading_or_missing_and_the_module_only_read()
    {
        Install(Sector());
        using (var stack = Stack())
        {
            var (snapshot, _) = Load(stack);
            File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Base(WithScheme(Sector(), 5)));
            using var changed = Stack();
            Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.SectorIdentityMismatch, Arcanum.Data.Lighting.AreaLightingReader.Read(changed, snapshot).Failure);
            File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), BaseWithout("terrain/grass/0.sec"));
            using var missing = Stack();
            Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.MissingResource, Arcanum.Data.Lighting.AreaLightingReader.Read(missing, snapshot).Failure);
            // The pinned profiles' module-only read generates no sector.
            using var module = DatArchive.Open(Path.Combine(_root, "modules", "Mod.dat"));
            Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.SectorIdentityMismatch,
                new Arcanum.Data.Lighting.AreaLightingReader.ArchiveFiles(module).Template("terrain/grass/0.sec", out _));
        }
    }

    [TestMethod]
    public void A_window_route_crosses_sectors_through_windows_loaded_on_demand()
    {
        // ARC-WORLD-042: from sector (0, 0) to sector (2, 2) of the open 3×3-sector map, diagonally, one window per step's sector.
        Install(Sector());
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var route = AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(180, 170));
        Assert.AreEqual((AreaWindowRouteStatus.Found, "ROUTE_WINDOWS"), (route.Status, route.Detail));
        Assert.AreEqual(171, route.Cells.Count); // the start plus 170 steps (the Chebyshev distance on an open map)
        Assert.AreEqual((new AreaStepCell(10, 10), new AreaStepCell(180, 170)), (route.Cells[0], route.Cells[^1]));
        for (var i = 1; i < route.Cells.Count; i++)
            Assert.AreEqual(1L, Math.Max(Math.Abs(route.Cells[i].X - route.Cells[i - 1].X), Math.Abs(route.Cells[i].Y - route.Cells[i - 1].Y)));
        Assert.IsTrue(route.WindowLoads >= 2, $"loads {route.WindowLoads}");

        // One resident window still finds it (evicted windows are reloaded); one window load in all does not.
        var single = AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(180, 170), new(maxResidentWindows: 1));
        Assert.AreEqual((AreaWindowRouteStatus.Found, 171), (single.Status, single.Cells.Count));
        var loads = AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(180, 170), new(maxWindowLoads: 1));
        Assert.AreEqual((AreaWindowRouteStatus.LimitExceeded, "ROUTE_WINDOW_LIMIT"), (loads.Status, loads.Detail));
        var expansions = AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(180, 170), new(maxExpansions: 5));
        Assert.AreEqual((AreaWindowRouteStatus.LimitExceeded, "ROUTE_EXPANSION_LIMIT", 5), (expansions.Status, expansions.Detail, expansions.Expansions));
    }

    [TestMethod]
    public void A_window_route_refuses_a_start_or_a_goal_off_the_map()
    {
        Install(Sector());
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        Assert.AreEqual("ROUTE_START_NOT_ADMITTED", AreaWindowRoute.Plan(stack, catalog, Map, new(-5, 10), new(20, 20)).Detail);
        Assert.AreEqual((AreaWindowRouteStatus.Found, 1), (AreaWindowRoute.Plan(stack, catalog, Map, new(5, 5), new(5, 5)) is var at ? (at.Status, at.Cells.Count) : default));
        // Sector (3, 0) is off the 3×3-sector map: the goal is refused before any search.
        var off = AreaWindowRoute.Plan(stack, catalog, Map, new(150, 10), new(200, 10));
        Assert.AreEqual((AreaWindowRouteStatus.Refused, "ROUTE_GOAL_NOT_ADMITTED", 0), (off.Status, off.Detail, off.Expansions));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AreaWindowRouteLimits(maxResidentWindows: 0));
    }

    [TestMethod]
    public void A_window_route_walks_by_the_host_s_step_verdict()
    {
        // ARC-WORLD-060: a host's verdict (here: every step onto column 100 refused but row 50) replaces the T0 disposition, in the
        // search and in the goal's pre-check.
        Install(Sector());
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        bool Open(AreaStepAssessment assessment, AreaStepCell cell, CardinalTraversalDirection direction)
        {
            var next = AreaWindowRoute.Step(cell, direction);
            return assessment.Disposition == CardinalTraversalDisposition.Open && (next.X != 100 || next.Y == 50);
        }
        var route = AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(180, 170), stepOpen: Open);
        Assert.AreEqual(AreaWindowRouteStatus.Found, route.Status, route.Detail);
        CollectionAssert.AreEqual(new[] { new AreaStepCell(100, 50) }, route.Cells.Where(c => c.X == 100).ToArray());
        Assert.AreEqual("ROUTE_GOAL_UNENTERABLE",
            AreaWindowRoute.Plan(stack, catalog, Map, new(10, 10), new(100, 60), stepOpen: Open).Detail);
    }

    private static byte[] BaseWithout(string name)
    {
        using var archive = DatArchive.Open(new MemoryStream(Base(Sector())));
        return Dat([.. archive.Entries.Where(e => System.Text.Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).TrimEnd('\0') != name)
            .Select(e => (System.Text.Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).TrimEnd('\0'), archive.ReadEntry(e)))]);
    }

    /// <summary>The sector of <see cref="AreaIdentityTests.Sector"/> (format 3) with its light scheme word set.</summary>
    private static byte[] WithScheme(byte[] sector, int scheme)
    {
        // Light count, 4,096 tiles, roof discriminator, placeholder, tile scripts, sector script, then townmap, aptitude, scheme.
        BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(4 + 16384 + 4 + 4 + 4 + 12 + 8), scheme);
        return sector;
    }

    private static (AreaSnapshot Snapshot, AreaMapPlan Plan) Load(ContentStack stack)
    {
        // Radius 0 around (0, 0): the generated core sector (0, 0) and its ring inside the 3×3-sector map.
        var result = AreaMapPlans.CreateWindow(stack, WorldCatalog.Open(stack), Map, new(0, 0), radius: 0);
        Assert.AreEqual(AreaMapPlanStatus.Ready, result.Status, result.Detail);
        using var owner = result.Plan!.Open();
        return (owner.LoadStrict(), result.Plan);
    }

    /// <summary>A 3×3-sector map: its own file at (1, 1), the rest generated from the grass template in arcanum1.dat.</summary>
    private void Install(byte[] template)
    {
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), Dat());
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Base(template));
        File.WriteAllBytes(Path.Combine(_root, "arcanum2.dat"), Dat(("rules/other.mes", "{0}{x}"u8.ToArray())));
        Directory.CreateDirectory(Path.Combine(_root, "modules"));
        var tdf = new byte[32 + 9 * 2];
        BinaryPrimitives.WriteSingleLittleEndian(tdf, 1.2f); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(8), 3); BinaryPrimitives.WriteInt64LittleEndian(tdf.AsSpan(16), 3);
        var prp = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), 3 * 64); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 3 * 64);
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat([.. new[] { ($"maps/{Map}/67108865.sec", Sector()), ($"maps/{Map}/map.prp", prp),
            ($"maps/{Map}/terrain.tdf", tdf), ("rules/maplist.mes", "{5000}{test, 64, 64}"u8.ToArray()),
            ($"maps/{Map}/mapinfo.txt", "LightScheme:2\r\n"u8.ToArray()) }.OrderBy(e => e.Item1, StringComparer.Ordinal)]));
        Loose("data/proto/007001 - test.pro", Prototype());
    }

    /// <summary>arcanum1.dat: the name tables, tile ART, effects and terrain types a window and its evidence read, and the template.</summary>
    private static byte[] Base(byte[] template)
    {
        var table = "{0}{fixture}"u8.ToArray();
        var entries = new List<(string, byte[])>
        {
            ("art/container/container.mes", table), ("art/facade/facadename.mes", table), ("art/item/item_ground.mes", table),
            ("art/monster/monster.mes", table), ("art/portal/portal.mes", table), ("art/scenery/scenery.mes", "{0}{fixture.art}"u8.ToArray()),
            ("art/scenery/fixture.art", Art()), ("art/tile/stonebse0a.art", Art()),
            ("art/tile/tilename.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()),
            ("art/unique_npc/unique_npc.mes", table), ("art/wall/structure.mes", table), ("art/wall/wallname.mes", table),
            ("rules/effect.mes", "{1}{x}"u8.ToArray()), ("terrain/grass/0.sec", template), ("terrain/terrain.mes", "{0}{grass}"u8.ToArray()),
            ("rules/gameinit.mes", "{4}{1885}{5}{15}"u8.ToArray()), ("rules/lighting schemes.mes", "{2}{Lighting Two}{5}{Lighting Five}"u8.ToArray()),
            ("rules/lighting two.mes", Rows(2)), ("rules/lighting five.mes", Rows(5)),
        };
        return Dat([.. entries.OrderBy(e => e.Item1, StringComparer.Ordinal)]);
    }

    private static byte[] Rows(int value) => System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 24).Select(h => $"{{{h}}}{{{value},{value},{value},1,1,1}}\n")));

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Loose(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
