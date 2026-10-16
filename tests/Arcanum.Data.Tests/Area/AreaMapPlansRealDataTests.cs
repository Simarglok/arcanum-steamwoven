using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>Opt-in (DAT_ROOT = the game folder): every interior gets a whole-map plan and opens;
/// the maps listed below publish a complete snapshot (docs/world/whole-map-plans.md). A map that
/// starts publishing joins the list; one that stops is a regression.</summary>
[TestClass]
public sealed class AreaMapPlansRealDataTests
{
    private static readonly string[] Complete =
    [
        "ashbury-scienticians underground", "caladon guardhouse basement", "caladon sanders basement", "crash site cave", "crematorium-lev 1",
        "elven ruins", "iron clan - music box", "iron clan hq", "mannox's hole", "qintarra-queens chambers", "shopmap", "stillwater giants cave",
        "the pit-fixed", "thieves cave", "tulla interior - battle - conveyance", "uncharted cave", "void - cave of kriggyrd's falchion",
        "vollingers basement", "wolf-cave",
    ];

    [TestMethod, TestCategory("RealData")]
    public void Every_interior_opens_and_the_known_ones_publish_complete_snapshots()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in whole-map check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);

        var complete = new List<string>();
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, $"{map.Name}: {plan.Detail}");
            Assert.AreEqual(AreaSectorSelection.AllSectors, plan.Plan!.Plan.Selection);
            using var owner = plan.Plan.Open();
            var inspection = owner.Inspect();
            Assert.IsNotNull(inspection.Snapshot, map.Name);
            if (inspection.IsComplete) complete.Add(map.Name);
            if (map.Name == "crash site cave")
            {
                // The generic plan reaches what the pinned cave profile pins, without its exceptions.
                var cave = inspection.Snapshot!;
                Assert.AreEqual((5078, 79), (cave.CatalogueMap, cave.RuntimeMap));
                Assert.AreEqual(4096, cave.Reconciliation.CoreTerrain);
                Assert.AreEqual(138, cave.Reconciliation.Statics);
                Assert.AreEqual(39, cave.Reconciliation.Aggregate);
                Assert.AreEqual(194, cave.Reconciliation.Visuals);
            }
        }
        Assert.AreEqual(80, catalog.Maps.Count - 1);
        CollectionAssert.IsSubsetOf(Complete, complete, string.Join(", ", Complete.Except(complete)));
    }
}
