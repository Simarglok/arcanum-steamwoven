using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-019 plan factory on synthetic loose-file installations.</summary>
[TestClass]
public sealed class AreaMapPlansTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-mapplan-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Map_numbers_follow_the_list_until_its_first_gap()
    {
        Write("rules/maplist.mes", "{5000}{First, 1, 2}\r\n{5001}{Second, 3, 4, Type: START_MAP}\r\n{5003}{AfterGap, 5, 6}\r\n"u8.ToArray());
        using var stack = Stack();
        Assert.AreEqual((5001, 2), AreaMapPlans.CatalogueNumbers(stack, "second"));
        Assert.AreEqual((0, 0), AreaMapPlans.CatalogueNumbers(stack, "AfterGap"));
        Assert.AreEqual((0, 0), AreaMapPlans.CatalogueNumbers(stack, "missing"));
    }

    [TestMethod]
    public void Unknown_and_oversized_maps_are_refused_before_any_input_is_read()
    {
        Write("maps/big/map.prp", Properties());
        for (var i = 0; i <= AreaMapPlans.MaxSectorFiles; i++) Write($"maps/big/{i}.sec", [0]);
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        Assert.AreEqual(AreaMapPlanStatus.UnknownMap, AreaMapPlans.Create(stack, catalog, "absent").Status);
        Assert.AreEqual(AreaMapPlanStatus.TooLarge, AreaMapPlans.Create(stack, catalog, "big").Status);
    }

    [TestMethod]
    public void Limits_grow_with_the_map_and_stop_at_the_cap()
    {
        var baseline = new AreaLoaderOptions();
        var small = AreaMapPlans.Limits(1, 10, 5, 100, 3);
        Assert.AreEqual(baseline.MaxManagedBytes, small.MaxManagedBytes);
        Assert.AreEqual((1, 1, 5, 3, 11), (small.MaxSurvey, small.MaxPresentSectors, small.MaxAggregateRecords, small.MaxPrototypes, small.MaxObjects));
        var doubled = AreaMapPlans.Limits(40, 10, 5, 100, 3);
        Assert.AreEqual(baseline.MaxManagedBytes * 2, doubled.MaxManagedBytes);
        var huge = AreaMapPlans.Limits(256, 10_000_000, 5, 100, 3);
        Assert.AreEqual((long)Math.Ceiling(baseline.MaxManagedBytes * AreaMapPlans.MaxLimitScale), huge.MaxManagedBytes);
        Assert.AreEqual(int.MaxValue, AreaMapPlans.Limits(1, 10, 5, long.MaxValue, 3).MaxAggregateBytes);
        // Frame budgets are the cap for every whole map.
        Assert.AreEqual((long)Math.Ceiling(baseline.MaxFrameStoredBytes * AreaMapPlans.MaxLimitScale), small.MaxFrameStoredBytes);
        Assert.AreEqual(baseline.MaxFrameOperationBytes * 16, small.MaxFrameOperationBytes);
    }

    [TestMethod]
    public void Every_plan_reads_entries_up_to_sixteen_mebibytes()
    {
        // ARC-DATA-036: one entry cap for every plan, pinned profiles too.
        foreach (var selection in Enum.GetValues<AreaSectorSelection>().Where(s => s != AreaSectorSelection.Window))
            Assert.AreEqual(16L * 1048576, AreaDataOwner.EntryCap(new(1, 1, new(1, 1), 2, "maps/test", [], []) { Selection = selection }));
        Assert.AreEqual(16L * 1048576, AreaDataOwner.EntryCap(CaveProfile.Plan()));
        Assert.AreEqual(16L * 1048576, AreaDataOwner.EntryCap(OriginalProfile.Plan()));
    }

    [TestMethod]
    public void A_whole_map_without_a_stack_order_refuses_a_resource_held_by_both_base_archives()
    {
        var resources = new (string, byte[])[] { ("tile.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()),
            ("facade.mes", "{0}{fixture}"u8.ToArray()), ("scenery.mes", "{0}{fixture.art}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art()) };
        var second = Dat(("art/scenery/fixture.art", Art()));
        using var f = new Fixture([(67108865, [Record(Id(2, 11))])], resourceEntries: resources, secondResource: second, selection: AreaSectorSelection.AllSectors);
        var report = f.Owner.Inspect();
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.AmbiguousSource && i.Stage == AreaStage.Appearance));
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, "modules", "Mod", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] Properties()
    {
        var prp = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(prp, 2); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(8), 64); BinaryPrimitives.WriteInt64LittleEndian(prp.AsSpan(16), 64);
        return prp;
    }
}
