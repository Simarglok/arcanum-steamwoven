using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder) against the
/// counts of docs/research/data-census.md: every map, sector, object and mobile reads.</summary>
[TestClass]
public sealed class WorldCatalogRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_map_of_the_installed_module_reads_without_refusals()
    {
        using var stack = Open("Arcanum");
        var catalog = WorldCatalog.Open(stack);

        Assert.AreEqual(0, catalog.Issues.Count, string.Join("; ", catalog.Issues));
        Assert.AreEqual(81, catalog.Maps.Count);
        Assert.AreEqual(1423, catalog.PrototypeNumbers.Count);
        Assert.AreEqual(1680, catalog.Maps.Sum(m => m.SectorFiles.Count));
        Assert.AreEqual(45, catalog.Maps.Sum(m => m.JumpPoints.Count));
        Assert.IsTrue(catalog.Maps.All(m => m.Terrain is not null && m.Info is not null && m.AggregatePath is not null));
        // Only the editor's two side files are unrecognised; nothing is malformed.
        Assert.AreEqual(2, catalog.Maps.Sum(m => m.Issues.Count), string.Join("; ", catalog.Maps.SelectMany(m => m.Issues)));
        Assert.IsTrue(catalog.Maps.SelectMany(m => m.Issues).All(i => i.Code == WorldIssueCode.UnrecognisedFile));

        long objects = 0, mobiles = 0; var versions = new int[5];
        foreach (var map in catalog.Maps)
        {
            foreach (var coordinate in map.SectorFiles)
            {
                var result = catalog.ReadSector(map, coordinate);
                Assert.AreEqual(WorldSectorStatus.Read, result.Status, $"{map.Name} {coordinate}: {result.Issue}");
                Assert.IsNull(result.Sector!.ObjectIssue, $"{map.Name} {coordinate}: {result.Sector.ObjectIssue}");
                objects += result.Sector.Objects.Count; versions[result.Sector.Version]++;
            }
            var mobile = catalog.ReadMobiles(map);
            Assert.AreEqual(WorldMobileSource.Aggregate, mobile.Source);
            Assert.AreEqual(0, mobile.Issues.Count, $"{map.Name}: {string.Join("; ", mobile.Issues)}");
            mobiles += mobile.Objects.Count;
        }
        Assert.AreEqual(263_902, objects);
        Assert.AreEqual(12_723, mobiles);
        CollectionAssert.AreEqual(new[] { 18, 2, 6, 169, 1485 }, versions);

        var main = catalog.Find("arcanum1-024-fixed")!;
        Assert.AreEqual(128_000, main.Properties.WidthTiles);
        var grid = catalog.ReadTerrain(main);
        Assert.IsNull(grid.Issue);
        Assert.AreEqual(2000, grid.Grid!.Header.WidthSectors);
        Assert.AreEqual((ushort)0x2102, grid.Grid[1452, 1290]);
        Assert.IsTrue(main.HasSectorFile(WorldSectorCoordinate.Containing(new(92958, 82592))));
        Assert.IsTrue(catalog.PrototypeNumbers.All(n => catalog.ReadPrototype(n).Issue is null));
    }

    [TestMethod, TestCategory("RealData")]
    public void A_second_module_reads_the_same_way_and_a_map_without_an_aggregate_falls_back_to_editor_files()
    {
        using var stack = Open("Vormantown");
        var catalog = WorldCatalog.Open(stack);
        Assert.AreEqual(3, catalog.Maps.Count);
        var objects = catalog.Maps.SelectMany(m => m.SectorFiles.Select(c => catalog.ReadSector(m, c))).ToArray();
        Assert.IsTrue(objects.All(r => r.Status == WorldSectorStatus.Read && r.Sector!.ObjectIssue is null));
        Assert.AreEqual(25_113, objects.Sum(r => r.Sector!.Objects.Count));
        var sources = catalog.Maps.Select(m => catalog.ReadMobiles(m)).ToArray();
        Assert.AreEqual(5_806, sources.Sum(s => s.Objects.Count));
        Assert.AreEqual(1, sources.Count(s => s.Source == WorldMobileSource.Editor));
        Assert.IsTrue(sources.All(s => s.Issues.Count == 0));
    }

    private static ContentStack Open(string module)
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in world catalogue check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root, module);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }
}
