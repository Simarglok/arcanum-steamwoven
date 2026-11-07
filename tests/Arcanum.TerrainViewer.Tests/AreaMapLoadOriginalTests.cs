using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    [TestMethod, TestCategory("RealData")]
    [DataRow("bmc", 115), DataRow("ashbury haunted castle", 76), DataRow("k'na tha", 86)]
    public void Additive_bodies_are_drawn_unlit(string map, int additive)
    {
        // ARC-RENDER-026: the original draws an additive body with the base palette, unlit (ledger BL2).
        var bodies = AreaLoadOperation.Execute(1, Arguments(Root(), map), default).Area!.Plan.Commands.Where(command => command.Identity is not null).ToArray();
        Assert.AreEqual(additive, bodies.Count(command => command.Additive && command.Unlit));
        Assert.AreEqual(0, bodies.Count(command => command.Additive && !command.Unlit));
    }

    [TestMethod, TestCategory("RealData")]
    public void A_map_opens_under_the_data_rules_its_arguments_carry()
    {
        // ARC-DATA-037 (MP7): the viewer's map load applies the map patch under DataRules, Steamwoven without any.
        long[]?[] Dialogs(Arcanum.Data.Fixes.DataFixProfile? rules) =>
            AreaLoadOperation.Execute(1, Arguments(Root(), "iron clan hq") with { DataRules = rules }, default).Area!.Snapshot.Objects
                .Where(o => o.MapPatch is not null && o.Type == 16).OrderBy(o => o.Key.ObjectIdSha256, StringComparer.Ordinal)
                .Select(o => o.EffectiveFields.SingleOrDefault(f => f.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == 9)?.Words.ToArray()).ToArray();
        var original = Dialogs(Arcanum.Data.Fixes.DataFixProfile.Original);
        Assert.AreEqual(4, original.Length);
        Assert.IsTrue(original.All(d => d is { Length: 3 } && d[2] != 0), "Original keeps every dialog script");
        foreach (var rules in new[] { null, Arcanum.Data.Fixes.DataFixProfile.Steamwoven })
        {
            var emptied = Dialogs(rules);
            Assert.AreEqual(4, emptied.Length);
            Assert.IsTrue(emptied.All(d => d is [0, 0, 0]), "Steamwoven empties the slot");
        }
    }

    [TestMethod, TestCategory("RealData")]
    public void The_tub_on_the_near_wall_tile_of_the_shrouded_hills_building_is_drawn_before_that_wall()
    {
        // ARC-DEV-173: the town sector (1409, 1314) of the main map. A building's tub (prototype 4032, tile 90215,84130) stands on a tile of
        // its near-side wall (rotation 2..5, ranks (38, -2)); the original's tile list draws that wall after the tub. The plan drew the tub
        // last because a wall ranked as an object without offsets, (20, -20).
        var root = Root();
        var args = TerrainViewerArguments.Parse(["--mode", "crash-site-area",
            "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
            "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", "Arcanum1-024-fixed", "--area-map-sector", "1409,1314"]);
        var area = AreaLoadOperation.Execute(1, args, default).Area!;
        var records = area.Snapshot.Objects.ToDictionary(o => o.Key);
        var commands = area.Plan.Commands.Select((command, index) => (command, index)).Where(t => t.command.Identity is not null).ToArray();
        var tub = commands.Single(t => t.command.MapX == 90215 && t.command.MapY == 84130 && records[t.command.Identity!.Value].PrototypeNumber == 4032);
        var walls = commands.Where(t => t.command.MapX == 90215 && t.command.MapY == 84130 && records[t.command.Identity!.Value].Type == 0).ToArray();
        Assert.IsTrue(walls.Length > 0);
        foreach (var wall in walls)
        {
            Assert.AreEqual((38, -2), (wall.command.FirstRank, wall.command.SecondRank));
            Assert.IsTrue(tub.index < wall.index, $"tub {tub.index} wall {wall.index}");
        }
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
