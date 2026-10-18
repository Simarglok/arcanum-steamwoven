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
        // The AREA publication gate: the human actor with its run and vault ART (ARC-WORLD-020/021).
        Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed, result.ActorPlan?.Profile.Id);
        Assert.IsNotNull(result.ActorPlan!.Profile.Run);
        Assert.IsNotNull(result.ActorPlan.Profile.Vault);
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
        (string Map, long Value) frames = default, bytes = default, span = default;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open())
                if (!owner.Inspect().IsComplete) continue;
            try
            {
                var area = AreaLoadOperation.Execute(1, Arguments(root, map.Name), default).Area!;
                loaded.Add(map.Name);
                if (area.Frames.Count > frames.Value) frames = (map.Name, area.Frames.Count);
                var rgba = area.Frames.Values.Sum(frame => (long)frame.Width * frame.Height * 4);
                if (rgba > bytes.Value) bytes = (map.Name, rgba);
                if ((area.Plan.Chart.MaximumCell + 1) / 64 > span.Value) span = (map.Name, (area.Plan.Chart.MaximumCell + 1) / 64);
            }
            catch (Exception exception) when (exception is InvalidOperationException or AreaException) { failed.Add($"{map.Name}: {exception.Message}"); }
        }
        // Since ARC-RENDER-018 the wading, multiply and constant-alpha bodies are drawn, so every complete interior loads;
        // since ARC-DATA-023 every interior but K'na Tha is complete, and since ARC-DATA-024 K'na Tha too.
        Assert.AreEqual(0, failed.Count, string.Join("; ", failed));
        Assert.AreEqual(80, loaded.Count, string.Join(", ", loaded));
        // ARC-DATA-029: the map-mode caps keep at least 1.5 times the largest interior (docs/world/limits-census.md).
        Console.WriteLine($"ARC-DATA-029 viewer frames={frames} rgba={bytes} chart={span}");
        Assert.IsTrue(frames.Value * 3 <= AreaRepositoryPolicy.MaxMapFrames * 2L, $"{frames}");
        Assert.IsTrue(bytes.Value * 3 <= AreaRepositoryPolicy.MaxMapCpuBytes * 2, $"{bytes}");
        Assert.IsTrue(span.Value * 3 <= AreaMapLoadOperation.MaxChartSectors * 2L, $"{span}");
    }

    [TestMethod, TestCategory("RealData")]
    [DataRow("caladons sewers", 20, 0, 0), DataRow("tarant sewers-02", 102, 0, 0), DataRow("void - arronax's interior", 0, 12, 0),
        DataRow("void-kerghans castle", 0, 42, 0), DataRow("bessietoonesmine-fixed", 0, 0, 1)]
    public void Wading_multiply_and_constant_alpha_bodies_are_drawn(string map, int wading, int multiply, int constant)
    {
        var plan = AreaLoadOperation.Execute(1, Arguments(Root(), map), default).Area!.Plan;
        var bodies = plan.Commands.Where(command => command.Identity is not null).ToArray();
        Assert.AreEqual(wading, bodies.Count(command => command.WadeRows > 0));
        Assert.AreEqual(multiply, bodies.Count(command => command.Multiply && command.Unlit));
        Assert.AreEqual(constant, bodies.Count(command => command.Alpha == 95 && command.Unlit && !command.Multiply));
        Assert.IsTrue(bodies.Where(command => command.WadeRows > 0).All(command => command.WadeRows == Math.Min(AreaScenePlan.WadingRows, command.Destination.Height)));
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root)
            return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in map load check; this is not a synthetic pass.");
        return string.Empty;
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
