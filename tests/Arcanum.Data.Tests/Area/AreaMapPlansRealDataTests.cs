using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>Opt-in (DAT_ROOT = the game folder): every interior gets a whole-map plan and opens;
/// the maps listed below publish a complete snapshot (docs/world/whole-map-plans.md; every
/// interior but K'na Tha since ARC-DATA-023, all 80 since ARC-DATA-024). A map that starts publishing joins the list;
/// one that stops is a regression.</summary>
[TestClass]
public sealed class AreaMapPlansRealDataTests
{
    private static readonly string[] Complete =
    [
        "ashbury haunted castle", "ashbury-scienticians underground", "bates mansion lev 1", "bates mansion lev 2", "bates tunnel-fixed",
        "bessietoonesmine-fixed", "bmc", "caladon guardhouse basement", "caladon high security prison", "caladon sanders basement",
        "caladon trap disarm dungeon", "caladon-castle main floor", "caladon-mafia underground", "caladon-panarrii temple",
        "caladon-panarrii temple-basement", "caladons sewers", "castle s'nel n'fa", "cave of the bangellian scourge", "crash site cave",
        "crematorium-lev 1", "crematorium-lev 2", "crematorium-lev 3", "dernholm pits", "dernholm-castle", "drogs cave", "dungeon of the dragon pool",
        "elven ruins", "fal'kins maze", "iron clan - music box", "iron clan hq", "k'na tha", "mannox's hole", "pit of fires", "pschuylers lev 1-fixed",
        "pschuylers lev 2-fixed", "pschuylers lev 3-fixed", "qintarra", "qintarra-queens chambers", "roseborough inn2", "ruins of szabo",
        "scienticians underground lev 1", "scienticians underground lev 2", "shopmap", "shrouded hills-panarii temple lev 1",
        "stillwater giants cave", "stonecutter clan", "t'sen-ang", "tarant sewers-01", "tarant sewers-02", "tarant-city hall downstairs",
        "temple of the derian ka", "the lair of bellerogrim", "the pit-fixed", "thieves cave", "tulla interior", "tulla interior - battle - air",
        "tulla interior - battle - conveyance", "tulla interior - battle - divination", "tulla interior - battle - earth",
        "tulla interior - battle - fire", "tulla interior - battle - force", "tulla interior - battle - mental", "tulla interior - battle - meta",
        "tulla interior - battle - morph", "tulla interior - battle - nature", "tulla interior - battle - necro evil",
        "tulla interior - battle - necro good", "tulla interior - battle - phantasm", "tulla interior - battle - summoning",
        "tulla interior - battle - temporal", "tulla interior - battle - water", "uncharted cave", "vendigroth underground", "void",
        "void - arronax's interior", "void - cave of kriggyrd's falchion", "void-kerghans castle", "vollingers basement", "wheel clan", "wolf-cave",
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

        var complete = new List<string>(); var roofs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, $"{map.Name}: {plan.Detail}");
            Assert.AreEqual(AreaSectorSelection.AllSectors, plan.Plan!.Plan.Selection);
            using var owner = plan.Plan.Open();
            var inspection = owner.Inspect();
            Assert.IsNotNull(inspection.Snapshot, map.Name);
            if (inspection.IsComplete) complete.Add(map.Name);
            // ARC-RENDER-021: the roof blocks of the core sectors, every one admitted, with the installed shades.
            if (inspection.Snapshot!.Roofs.Count > 0)
            {
                roofs.Add(map.Name, inspection.Snapshot.Roofs.Count);
                Assert.AreEqual(new AreaRoofShades(255, 200, 80), inspection.Snapshot.RoofShades);
            }
            Assert.IsFalse(inspection.Issues.Any(i => i.SourceIndex >= 4096 && i.Stage is AreaStage.Appearance or AreaStage.Mapping or AreaStage.Frame), map.Name);
            // ARC-DATA-023: every installed name resolves; no complete interior draws bad art.
            if (inspection.IsComplete) Assert.IsFalse(inspection.Snapshot!.Visuals.Any(v => v.Policy == "original-badart-v1"), map.Name);
            // ARC-DATA-024: K'na Tha's 43,219 objects share 260 art ids; each distinct frame is decoded once (488 since
            // ARC-WORLD-051 also decodes the other frames of its multi-frame door and window ARTs: 11 more).
            if (map.Name == "k'na tha") Assert.AreEqual(488, inspection.Usage.FrameRequests);
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
        Assert.AreEqual("bates mansion lev 2=72, caladon-mafia underground=28, tulla interior=515, tulla interior - battle - air=132, tulla interior - battle - earth=184",
            string.Join(", ", roofs.Select(p => $"{p.Key}={p.Value}")));
        CollectionAssert.IsSubsetOf(Complete, complete, string.Join(", ", Complete.Except(complete)));
    }
}
