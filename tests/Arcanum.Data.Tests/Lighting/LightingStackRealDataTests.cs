using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

/// <summary>
/// ARC-DATA-027 opt-in check of the installed original (DAT_ROOT = the game folder): every interior's lighting read
/// through the content stack equals the pinned module-archive read the map viewer used before (module archive for the
/// ambient, <c>arcanum2.dat</c> for the stationary lights), and the lighting caps hold. Only numbers and hashes are
/// compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class LightingStackRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_interior_lights_the_same_through_the_stack_within_the_caps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var options = new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };
        using var module = DatArchive.Open(stack.Plan.SearchOrder.Single(s => s.Role == ContentSourceRole.ModuleArchive).Path, options);
        using var resource2 = DatArchive.Open(Path.Combine(root, "arcanum2.dat"), options);

        var lit = 0; var maxima = (Sectors: 0, Schemes: 0, Lights: 0, Frames: 0);
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, map.Name);
            using var owner = plan.Plan!.Open();
            AreaSnapshot snapshot;
            try { snapshot = owner.LoadStrict(); }
            catch (AreaException) { outcomes["not strict"] = outcomes.GetValueOrDefault("not strict") + 1; continue; }

            var (ambient, ambientDat) = (AreaLightingReader.Read(stack, snapshot), AreaLightingReader.Read(module, snapshot));
            Assert.AreEqual(ambientDat.Failure, ambient.Failure, map.Name);
            var key = $"ambient {ambient.Failure}";
            if (ambient.Source is { } a)
            {
                var b = ambientDat.Source!;
                Assert.AreEqual((b.MapDefaultScheme, b.MapInfoSha256, b.Startup), (a.MapDefaultScheme, a.MapInfoSha256, a.Startup), map.Name);
                CollectionAssert.AreEqual(b.SectorSchemes.ToArray(), a.SectorSchemes.ToArray(), map.Name);
                CollectionAssert.AreEqual(b.Schemes.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray(), a.Schemes.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray(), map.Name);
                maxima = (Math.Max(maxima.Sectors, a.SectorSchemes.Count), Math.Max(maxima.Schemes, a.Schemes.Count), maxima.Lights, maxima.Frames);
                var (lights, lightsDat) = (StationaryLightReader.Read(stack, snapshot), StationaryLightReader.Read(resource2, snapshot));
                Assert.AreEqual(lightsDat.Failure, lights.Failure, map.Name);
                key += $" / lights {lights.Failure}";
                if (lights.Source is { } l)
                {
                    lit++;
                    Assert.AreEqual(lightsDat.Source!.NamesSha256, l.NamesSha256, map.Name);
                    CollectionAssert.AreEqual(lightsDat.Source.Frames.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray(), l.Frames.Select(p => (p.Key, p.Value.PayloadSha256)).ToArray(), map.Name);
                    maxima = (maxima.Sectors, maxima.Schemes, Math.Max(maxima.Lights, l.Lights.Count), Math.Max(maxima.Frames, l.Frames.Count));
                }
            }
            outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
        }
        foreach (var (outcome, count) in outcomes) Console.WriteLine($"ARC-DATA-027 lighting {outcome}: {count}");
        Console.WriteLine($"ARC-DATA-027 lighting maxima sectors={maxima.Sectors}/{AreaLightingReader.MaxSectors} schemes={maxima.Schemes}/{AreaLightingReader.MaxSchemes} " +
            $"lights={maxima.Lights}/{StationaryLightReader.MaxLights} frames={maxima.Frames}/{StationaryLightReader.MaxFrames}");
        Assert.IsTrue(lit > 0);
    }
}
