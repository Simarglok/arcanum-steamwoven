using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

// ARC-WORLD-056: the area table as the original's area module reads it (ledger WT1). Entries are invented.
[TestClass]
public sealed class WorldAreaTableTests
{
    [TestMethod]
    public void Entries_give_location_offsets_name_description_and_radius()
    {
        var table = WorldAreaTable.Create([
            "0, 0, 0, 0 /Nowhere/The unknown area./Radius:7",
            "90226,84176, 0, -5 /Town/A town.",
            "1, 2, 3, 4 /Mines/Old mines./Radius:-1",
            "5, 6, 0, 0 /Ruins/Old ruins./ Radius:10",
            "7, 8, 0, 0 /Broken",
            "9, 10",
        ]);
        var a = table.Areas;
        Assert.AreEqual(6, a.Count);
        Assert.AreEqual((0L, true), (a[0].Radius, a[0].Complete));
        Assert.AreEqual(new WorldArea(1, new(90226, 84176), 0, -5, "Town", "A town.", WorldAreaTable.DefaultRadius, true), a[1]);
        Assert.AreEqual(-64L, a[2].Radius);
        Assert.AreEqual(640L, a[3].Radius);
        Assert.AreEqual(("Broken", false, 0L), (a[4].Name, a[4].Complete, a[4].Radius));
        Assert.AreEqual((new WorldLocation(9, 10), false), (a[5].Location, a[5].Complete));
    }
}

/// <summary>Opt-in check of the installed module's area table (DAT_ROOT = the game folder): ARC-RES-068's data facts.</summary>
[TestClass]
public sealed class WorldAreaTableRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_has_82_areas_with_their_radii()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in area check; this is not a synthetic pass.");
        var discovery = Arcanum.Data.Content.ContentMountPlan.Discover(root, "Arcanum");
        using var stack = Arcanum.Data.Content.ContentStack.Open(discovery.Plan!).Stack!;
        var load = WorldAreaTable.Open(stack);
        Assert.AreEqual(WorldAreaTableStatus.Read, load.Status, load.Detail);
        var areas = load.Table!.Areas;
        Assert.AreEqual(82, areas.Count);
        Assert.IsTrue(areas.All(a => a.Complete));
        Assert.AreEqual(16, areas.Count(a => a.Radius < 0));
        Assert.AreEqual(new WorldLocation(102768, 116894), areas.Single(a => a.Name == "Caladon").Location);
        // 39 entries name no radius; a few name 5 sectors, the same 320 tiles.
        Assert.IsTrue(areas.Count(a => a.Radius == WorldAreaTable.DefaultRadius) >= 39);
    }
}
