using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>
/// ARC-DATA-029 opt-in census of the installed original (DAT_ROOT = the game folder): every map's size against every
/// limit of the universal path. Prints one line per map and the largest value of each measure (only numbers; the table is
/// docs/world/limits-census.md) and fails when a cap keeps less than 1.5 times the largest installed value. The main world
/// map is reported, not planned: it exceeds <see cref="AreaMapPlans.MaxSectorFiles"/> by design (it is streamed).
/// </summary>
[TestClass]
public sealed class LimitsCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_map_fits_the_universal_limits_with_headroom()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var rows = new List<Dictionary<string, long>>();
        foreach (var map in catalog.Maps)
        {
            var row = new Dictionary<string, long>(StringComparer.Ordinal);
            long statics = 0, maxSectorBytes = 0; var prototypes = new HashSet<int>(); var npcs = 0L;
            foreach (var c in map.SectorFiles)
            {
                var sector = catalog.ReadSector(map, c).Sector;
                Assert.IsNotNull(sector, $"{map.Name} {c}");
                statics += sector.Objects.Count;
                foreach (var o in sector.Objects) { if (o.PrototypeNumber is { } n) prototypes.Add(n); if (o.Type == 16) npcs++; }
                maxSectorBytes = Math.Max(maxSectorBytes, stack.Resolve(sector.Path).Winner!.Length);
            }
            var mobiles = catalog.ReadMobiles(map).Objects;
            foreach (var o in mobiles) { if (o.PrototypeNumber is { } n) prototypes.Add(n); if (o.Type == 16) npcs++; }
            var xs = map.SectorFiles.Select(c => c.X).DefaultIfEmpty(); var ys = map.SectorFiles.Select(c => c.Y).DefaultIfEmpty();
            row["sectors"] = map.SectorFiles.Count; row["statics"] = statics; row["mobiles"] = mobiles.Count; row["objects"] = statics + mobiles.Count;
            row["npcs"] = npcs; row["prototypes"] = prototypes.Count; row["span"] = map.SectorFiles.Count == 0 ? 0 : Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min()) + 1;
            row["sectorBytes"] = maxSectorBytes; row["aggregateBytes"] = map.AggregatePath is { } a ? stack.Resolve(a).Winner!.Length : 0;
            if (map.Name != "arcanum1-024-fixed")
            {
                var plan = AreaMapPlans.Create(stack, catalog, map.Name);
                Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, map.Name);
                using var owner = plan.Plan!.Open();
                var snapshot = owner.LoadStrict();
                var usage = owner.Usage; var limits = plan.Plan.Limits;
                row["visuals"] = snapshot.Visuals.Count; row["frames"] = usage.FrameRequests; row["rgba"] = usage.FrameRgba;
                // Used per mille of each budget the plan derives: the cumulative domains at the end of the load, the build
                // phase and the retained ownership at their peaks (frame holds and memos included).
                row["managed%"] = usage.ManagedReserved * 1000 / limits.MaxManagedBytes;
                row["phase%"] = owner.PhaseHistory.Max(p => p.HighWater) * 1000 / limits.MaxBuildPhaseBytes;
                row["retained%"] = owner.RetainedHighWater * 1000 / limits.MaxRetainedBytes; row["requests%"] = usage.FrameRequests * 1000 / limits.MaxFrameRequests;
                row["stored%"] = usage.FrameStored * 1000 / limits.MaxFrameStoredBytes; row["decoded%"] = usage.FrameDecoded * 1000 / limits.MaxFrameDecodedBytes;
                row["rgba%"] = usage.FrameRgba * 1000 / limits.MaxFrameRgbaBytes; row["encoded%"] = usage.FrameEncoded * 1000 / limits.MaxFrameEncodedBytes;
                row["read%"] = usage.ReadReserved * 1000 / limits.MaxReadBytes;
                row["loose"] = plan.Plan.Plan.ResourceOrder.Count(source => source.Key.Role == AreaSourceRole.Loose);
                // A map may be unlit for a data reason (not in the map list, sectors without metadata, an animated light or
                // an unsupported flag), never because a cap refused it.
                var lighting = AreaLightingReader.Read(stack, snapshot);
                Assert.AreNotEqual(LightingFailure.LimitExceeded, lighting.Failure, map.Name);
                if (lighting.Source is { } l)
                {
                    row["lightSectors"] = l.SectorSchemes.Count; row["schemes"] = l.Schemes.Count;
                    var lights = StationaryLightReader.Read(stack, snapshot);
                    Assert.AreNotEqual(LightingFailure.LimitExceeded, lights.Failure, map.Name);
                    if (lights.Source is { } s) { row["lights"] = s.Lights.Count; row["lightFrames"] = s.Frames.Count; }
                    else Console.WriteLine($"ARC-DATA-029 unlit map={map.Name} lights={lights.Failure}");
                }
                else Console.WriteLine($"ARC-DATA-029 unlit map={map.Name} lighting={lighting.Failure}");
            }
            rows.Add(row);
            Console.WriteLine($"ARC-DATA-029 map={map.Name} " + string.Join(" ", row.Select(p => $"{p.Key}={p.Value}")));
        }
        foreach (var key in rows.SelectMany(r => r.Keys).Distinct())
            Console.WriteLine($"ARC-DATA-029 max {key}={rows.Max(r => r.GetValueOrDefault(key))} sum={rows.Sum(r => r.GetValueOrDefault(key))}");

        // Interiors (every row with a plan) against the whole-map plan, owner and lighting caps.
        var interiors = rows.Where(r => r.ContainsKey("visuals")).ToArray();
        Assert.AreEqual(catalog.Maps.Count - 1, interiors.Length);
        Headroom(interiors, "sectors", AreaMapPlans.MaxSectorFiles);
        Headroom(interiors, "loose", AreaMapPlans.MaxLooseResources, allowZero: true);
        Headroom(rows, "aggregateBytes", AreaMapPlans.MaxMemberBytes);
        foreach (var budget in new[] { "managed%", "phase%", "retained%", "requests%", "stored%", "decoded%", "rgba%", "encoded%", "read%" })
            Headroom(interiors, budget, 1000, allowZero: true);
        Headroom(interiors, "lightSectors", AreaLightingReader.MaxSectors);
        Headroom(interiors, "schemes", AreaLightingReader.MaxSchemes);
        Headroom(interiors, "lights", StationaryLightReader.MaxLights);
        Headroom(interiors, "lightFrames", StationaryLightReader.MaxFrames);
        var patch = stack.Resolve("rules/map.patch").Winner;
        Assert.IsNotNull(patch);
        Console.WriteLine($"ARC-DATA-029 mapPatchBytes={patch.Length}");
        Assert.IsTrue(patch.Length * 3 <= MapPatchFile.MaxBytes * 2L, $"map.patch {patch.Length}");
    }

    private static void Headroom(IReadOnlyCollection<Dictionary<string, long>> rows, string key, long cap, bool allowZero = false)
    {
        var largest = rows.MaxBy(r => r.GetValueOrDefault(key))!.GetValueOrDefault(key);
        Assert.IsTrue(allowZero || largest > 0, $"{key}: no value measured");
        Assert.IsTrue(largest * 3 <= cap * 2, $"{key}: largest {largest} keeps less than 1.5 times headroom under {cap}");
    }
}
