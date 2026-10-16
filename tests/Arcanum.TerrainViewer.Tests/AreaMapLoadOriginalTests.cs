using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-RENDER-017 check (DAT_ROOT = the game folder): interiors load through the viewer's
/// map path with a drawable plan, step evidence and a spawn the walker can leave.</summary>
[TestClass]
public sealed class AreaMapLoadOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    [DataRow("crash site cave"), DataRow("elven ruins"), DataRow("bates mansion lev 1"), DataRow("caladon-castle main floor")]
    public void An_interior_loads_with_a_drawable_plan_and_a_walkable_spawn(string map)
    {
        var result = AreaLoadOperation.Execute(5, Arguments(Root(), map), default);
        var area = result.Area!;
        Assert.IsTrue(area.Snapshot.Profile.StartsWith("arc-data-019-", StringComparison.Ordinal));
        Assert.IsTrue(area.Plan.DrawableBodies > 0 && area.Frames.Count > 0, $"{area.Plan.DrawableBodies} bodies, {area.Frames.Count} frames");
        var steps = result.AreaStepSnapshot!;
        Assert.AreEqual(5, steps.EvidenceEpoch);
        var spawn = area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn);
        Assert.IsTrue(steps.IsAdmitted(spawn));
        // The actor's grid session accepts the spawn.
        _ = new AreaGridSession(steps, Arcanum.Simulation.Movement.SimulationTime.At(0), chart: area.Plan.Chart, start: area.Plan.Chart.Spawn);
        if (map == "crash site cave") Assert.AreEqual(new Arcanum.Data.Traversal.AreaStepCell(2087, 2018), spawn);
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_interior_the_strict_plan_reads_completely_loads_in_the_viewer()
    {
        var root = Root();
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var loaded = new List<string>(); var failed = new List<string>();
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open())
                if (!owner.Inspect().IsComplete) continue;
            try { _ = AreaLoadOperation.Execute(1, Arguments(root, map.Name), default); loaded.Add(map.Name); }
            catch (Exception exception) when (exception is InvalidOperationException or AreaException) { failed.Add($"{map.Name}: {exception.Message}"); }
        }
        // Not drawn yet (follow-up ARC-RENDER-018): wading bodies in the sewers (OF_WADING), multiply-blended
        // scenery in the void maps (blit 0x40) and one constant-alpha critter (blit 0x100, alpha 95).
        CollectionAssert.AreEqual(new[] { "bessietoonesmine-fixed", "caladons sewers", "tarant sewers-02", "void - arronax's interior",
            "void-kerghans castle" }.Select(m => m + ": AREA_UNSUPPORTED_BODY_STATE").ToArray(), failed, string.Join("; ", failed));
        Assert.AreEqual(49, loaded.Count, string.Join(", ", loaded));
    }

    private static string Root() => Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root ? root
        : throw new AssertFailedException("DAT_ROOT is required for the opt-in map load check; this is not a synthetic pass.");

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
