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
