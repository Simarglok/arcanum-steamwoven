using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Traversal;

/// <summary>ARC-DATA-034 on a synthetic installation: step evidence of a window whose core sector is generated from a
/// terrain template held by a base archive, bound to the snapshot's payload; a changed or loose template is refused.</summary>
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
    public void A_template_changed_since_loading_or_held_as_a_loose_file_is_refused()
    {
        Install(Sector());
        using (var stack = Stack())
        {
            var (snapshot, plan) = Load(stack);
            // The archive is rewritten with another template payload after the snapshot was built.
            File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Base(Sector(Record(Id(0)))));
            var error = Assert.ThrowsExactly<AreaStepInvocationException>(() => AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 3));
            Assert.AreEqual((AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Terrain), (error.Code, error.Stage));
        }
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Base(Sector()));
        Loose("data/terrain/grass/0.sec", Sector());
        using var loose = Stack();
        var (looseSnapshot, loosePlan) = Load(loose);
        var refused = Assert.ThrowsExactly<AreaStepInvocationException>(() => AreaStepInspector.InspectWholeMap(looseSnapshot, loose, loosePlan.Map.AggregatePath, 3));
        Assert.AreEqual((AreaStepInvocationCode.InputUnavailable, AreaStepInvocationStage.Terrain), (refused.Code, refused.Stage));
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
            ($"maps/{Map}/terrain.tdf", tdf), ("rules/maplist.mes", "{5000}{test, 64, 64}"u8.ToArray()) }.OrderBy(e => e.Item1, StringComparer.Ordinal)]));
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
        };
        return Dat([.. entries.OrderBy(e => e.Item1, StringComparer.Ordinal)]);
    }

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
