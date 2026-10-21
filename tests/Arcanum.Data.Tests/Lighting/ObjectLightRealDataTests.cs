using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

/// <summary>
/// ARC-DATA-039 opt-in check of the installed original (DAT_ROOT = the game folder): the object lights of every interior and
/// of main-map windows at the Crash Site and at Shrouded Hills read through the content stack within the caps, and the
/// stationary lights of every interior read with animated ART and the no-shadow bit admitted. Only counts are printed.
/// </summary>
[TestClass]
public sealed class ObjectLightRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Object_and_stationary_lights_of_every_map_read_within_the_caps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var maxObjectAt = ""; int maxObject = 0, maxStationary = 0, maxFrames = 0, animated = 0, nocturnal = 0, total = 0;
        void Count(string what, AreaSnapshot snapshot)
        {
            var objects = ObjectLightReader.Read(stack, snapshot);
            var stationary = StationaryLightReader.Read(stack, snapshot);
            var key = $"objects {objects.Failure} / stationary {stationary.Failure}";
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
            Assert.AreEqual(LightingFailure.None, objects.Failure, what);
            Assert.AreEqual(LightingFailure.None, stationary.Failure, what);
            var source = objects.Source!;
            total += source.Lights.Count;
            if (source.Lights.Count > maxObject) (maxObject, maxObjectAt) = (source.Lights.Count, what);
            maxFrames = Math.Max(maxFrames, source.Frames.Count);
            nocturnal += source.Lights.Count(l => l.Nocturnal);
            if (stationary.Source is { } s)
            {
                maxStationary = Math.Max(maxStationary, s.Lights.Count);
                animated += s.Lights.Count(l => s.Frames[l.ArtNumber].Animated) + source.Lights.Count(l => source.Frames[l.ArtNumber].Animated);
            }
        }
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, map.Name);
            using var owner = plan.Plan!.Open();
            AreaSnapshot snapshot;
            try { snapshot = owner.LoadStrict(); }
            catch (AreaException) { outcomes["not strict"] = outcomes.GetValueOrDefault("not strict") + 1; continue; }
            Count(map.Name, snapshot);
        }
        foreach (var centre in new WorldSectorCoordinate[] { new(1452, 1290), new(1409, 1315) })
        {
            var window = AreaMapPlans.CreateWindow(stack, catalog, "arcanum1-024-fixed", centre);
            Assert.AreEqual(AreaMapPlanStatus.Ready, window.Status, centre.ToString());
            using var owner = window.Plan!.Open();
            Count(centre.ToString(), owner.LoadStrict());
        }
        foreach (var (outcome, count) in outcomes) Console.WriteLine($"ARC-DATA-039 lights {outcome}: {count}");
        Console.WriteLine($"ARC-DATA-039 object lights total={total} max={maxObject}/{ObjectLightReader.MaxLights} ({maxObjectAt}) nocturnal={nocturnal} " +
            $"frames={maxFrames}/{StationaryLightReader.MaxFrames} stationary max={maxStationary}/{StationaryLightReader.MaxLights} animated={animated}");
        Assert.IsTrue(total > 0);
    }
}
