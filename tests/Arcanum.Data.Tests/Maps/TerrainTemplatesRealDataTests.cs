using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>
/// ARC-DATA-031 opt-in check on the installed original (DAT_ROOT = the game folder): every sector of every map resolves to
/// its own file or a template as the generated-wilderness census counted it (ARC-RES-034), and the Crash Site's generated
/// neighbours read and move into their sectors. Prints counts, coordinates and virtual paths only.
/// </summary>
[TestClass]
public sealed class TerrainTemplatesRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_sector_of_every_map_resolves_to_its_own_file_or_a_template()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);

        Assert.AreEqual(TerrainTypeStatus.Ready, catalog.TerrainTypes.Status, catalog.TerrainTypes.Detail);
        var types = catalog.TerrainTypes.Table!.Types;
        Assert.AreEqual(19, types.Count);
        CollectionAssert.AreEqual(new[] { 2, 9, 14, 16, 17, 18 }, types.Where(t => t.Blocked).Select(t => t.Number).ToArray());

        var used = new Dictionary<string, (WorldMap Map, WorldSectorSource Source)>(StringComparer.Ordinal); var usedMain = new HashSet<string>(StringComparer.Ordinal);
        long generatedOther = 0;
        foreach (var map in catalog.Maps)
        {
            var grid = catalog.ReadTerrain(map).Grid;
            Assert.IsNotNull(grid, map.Name);
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var y = 0; y < grid.Header.HeightSectors; y++)
                for (var x = 0; x < grid.Header.WidthSectors; x++)
                {
                    var source = catalog.ResolveSector(map, new(x, y), grid);
                    var key = source.Kind == WorldSectorSourceKind.Template ? source.Template!.Kind.ToString() : source.Kind.ToString();
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    if (source.Kind != WorldSectorSourceKind.Template) continue;
                    if (used.TryAdd(source.Path!, (map, source)))
                        Assert.IsTrue(stack.Resolve(source.Path!).Winner!.Source.Path.EndsWith("arcanum2.dat", StringComparison.OrdinalIgnoreCase), source.Path);
                    if (map.Name == "arcanum1-024-fixed") usedMain.Add(source.Path!);
                    else generatedOther++;
                }
            Assert.AreEqual(0, counts.GetValueOrDefault("Fill") + counts.GetValueOrDefault("Unresolved") + counts.GetValueOrDefault("OutsideMap"), map.Name);
            if (map.Name == "arcanum1-024-fixed")
            {
                Console.WriteLine("ARC-DATA-031 main " + string.Join(" ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
                Assert.AreEqual(621, counts["Own"]);
                Assert.AreEqual(3_954_569, counts["Plain"]);
                Assert.AreEqual(44_810, counts["Transition"]);
            }
        }
        Console.WriteLine($"ARC-DATA-031 templates main={usedMain.Count} all={used.Count} generatedOtherMaps={generatedOther}");
        Assert.AreEqual(208, usedMain.Count);
        Assert.AreEqual(212, used.Count);
        Assert.AreEqual(23_481 + 4_094 + 548 + 120, generatedOther);

        // Each used template read once at its first sector: every record is a wall or scenery without an object id and
        // lands inside that sector, as do the lights (ARC-RES-034 counted 29,561 objects and 128 lights in these files).
        long objects = 0, lights = 0;
        foreach (var (path, (map, source)) in used)
        {
            var sector = catalog.ReadResolvedSector(map, source).Sector!;
            Assert.IsNull(sector.ObjectIssue, path);
            Assert.IsTrue(sector.Objects.All(o => o.OwnIdKind == 0 && o.Type is 0 or 3), path);
            Assert.IsTrue(sector.Objects.All(o => WorldSectorCoordinate.Containing(o.Location!.Value) == source.Coordinate), path);
            Assert.IsTrue(sector.Lights.All(l => WorldSectorCoordinate.Containing(new((uint)l[2], (uint)l[3])) == source.Coordinate), path);
            objects += sector.Objects.Count; lights += sector.Lights.Count;
        }
        Console.WriteLine($"ARC-DATA-031 template objects={objects} lights={lights}");
        Assert.AreEqual(29_561, objects);
        Assert.AreEqual(128, lights);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_crash_site_ring_generates_its_five_template_sectors_in_place()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var map = catalog.Find("Arcanum1-024-fixed")!;
        var grid = catalog.ReadTerrain(map).Grid!;
        var generated = new Dictionary<WorldSectorCoordinate, string>();
        for (var y = 1288; y <= 1292; y++)
            for (var x = 1450; x <= 1454; x++)
            {
                var source = catalog.ResolveSector(map, new(x, y), grid);
                if (source.Kind == WorldSectorSourceKind.Own) continue;
                Assert.AreEqual(WorldSectorSourceKind.Template, source.Kind);
                generated[source.Coordinate] = source.Path!;
                var read = catalog.ReadResolvedSector(map, source);
                Assert.AreEqual(WorldSectorStatus.Read, read.Status, source.Path);
                var sector = read.Sector!;
                Assert.IsNull(sector.ObjectIssue, source.Path);
                Assert.IsTrue(sector.Objects.All(o => o.OwnIdKind == 0 && o.Type is 0 or 3), source.Path);
                Assert.IsTrue(sector.Objects.All(o => WorldSectorCoordinate.Containing(o.Location!.Value) == source.Coordinate), source.Path);
                Assert.IsTrue(sector.Lights.All(l => WorldSectorCoordinate.Containing(new((uint)l[2], (uint)l[3])) == source.Coordinate), source.Path);
                Console.WriteLine($"ARC-DATA-031 ring sector=({x},{y}) template={source.Path} objects={sector.Objects.Count} lights={sector.Lights.Count}");
            }
        CollectionAssert.AreEquivalent(new Dictionary<WorldSectorCoordinate, string>
        {
            [new(1452, 1288)] = "terrain/mountains/1.sec", [new(1454, 1288)] = "terrain/mountains/1.sec",
            [new(1453, 1288)] = "terrain/mountains/67108865.sec", [new(1450, 1290)] = "terrain/mountains/67108864.sec",
            [new(1450, 1291)] = "terrain/forest to mountains/3.sec",
        }.ToList(), generated.ToList());
    }
}
