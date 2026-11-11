using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

// ARC-MAGIC-038: the anti-teleport table as the original's module reads it (0x4BD9E0, 0x4BDB30, 0x4BDC60). Entries are invented.
[TestClass]
public sealed class AntiTeleportTableTests
{
    [TestMethod]
    public void Regions_run_from_100_and_maps_from_1000_each_to_the_first_gap()
    {
        var table = AntiTeleportTable.Create(new Dictionary<int, string>
        {
            [100] = "20, 30, 5", [101] = "-4,7, 1", [103] = "9, 9, 9",
            [1000] = "5069", [1001] = "5007", [1002] = "12", [1003] = "5000", [1005] = "5072",
        });
        CollectionAssert.AreEqual(new[] { new AntiTeleportRegion(20, 30, 320), new AntiTeleportRegion(-4, 7, 64) }, table.Regions.ToArray());
        // 5001 and above: minus 4999; below: plus 1.
        CollectionAssert.AreEqual(new[] { 70, 8, 13, 5001 }, table.DisabledMaps.ToArray());
    }

    [TestMethod]
    public void An_empty_file_gives_an_empty_module()
    {
        var table = AntiTeleportTable.Create(new Dictionary<int, string> { [5] = "x" });
        Assert.AreEqual(0, table.Regions.Count);
        Assert.AreEqual(0, table.DisabledMaps.Count);
    }

    [TestMethod]
    public void A_region_without_a_radius_reads_a_radius_of_zero()
    {
        var table = AntiTeleportTable.Create(new Dictionary<int, string> { [100] = "1, 2" });
        Assert.AreEqual(new AntiTeleportRegion(1, 2, 0), table.Regions.Single());
    }
}

/// <summary>Opt-in check of the installed module's anti-teleport table (DAT_ROOT = the game folder): counts only.</summary>
[TestClass]
public sealed class AntiTeleportTableRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_has_two_regions_and_five_disabled_maps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in anti-teleport check; this is not a synthetic pass.");
        var discovery = Arcanum.Data.Content.ContentMountPlan.Discover(root, "Arcanum");
        using var stack = Arcanum.Data.Content.ContentStack.Open(discovery.Plan!).Stack!;
        var load = AntiTeleportTable.Open(stack);
        Assert.AreEqual(AntiTeleportTableStatus.Read, load.Status, load.Detail);
        Assert.AreEqual(2, load.Table!.Regions.Count);
        Assert.AreEqual(5, load.Table.DisabledMaps.Count);
        Assert.IsTrue(load.Table.Regions.All(r => r.Radius > 0 && r.Radius % 64 == 0));
    }
}
