using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>
/// ARC-DATA-032 opt-in checks on the installed original (DAT_ROOT = the game folder; the Crash Site comparison also needs
/// the pinned exterior inputs ARC_AREA_MODULE, ARC_AREA_RESOURCE1, ARC_AREA_RESOURCE2, ARC_AREA_MOBILE, ARC_AREA_PROTOTYPES).
/// Windows of the main map load through the universal path; the Crash Site window reproduces the pinned exterior's core.
/// Prints counts, coordinates and digests only.
/// </summary>
[TestClass]
public sealed class AreaWindowRealDataTests
{
    private const string MainMap = "Arcanum1-024-fixed";

    [TestMethod, TestCategory("RealData")]
    public void The_crash_site_window_reproduces_the_pinned_exterior_core()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT")))
            Assert.Inconclusive("DAT_ROOT and the pinned exterior inputs are required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(1452, 1290));
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, planned.Detail);
        using var owner = planned.Plan!.Open();
        var window = owner.LoadStrict();
        using var pinnedOwner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var pinned = pinnedOwner.LoadStrict();
        Print("window", window); Print("pinned", pinned);

        Assert.AreEqual("arc-data-032-window-original-rules-v1", window.Profile);
        // The same nine core sectors with the same payloads.
        CollectionAssert.AreEquivalent(Sectors(pinned, AreaScope.Core), Sectors(window, AreaScope.Core));
        Assert.AreEqual(9, Sectors(window, AreaScope.Core).Count);
        // The pinned profile's eleven present context sectors, and the five it has no file for, generated from templates.
        var pinnedContext = Sectors(pinned, AreaScope.Context);
        var windowContext = Sectors(window, AreaScope.Context);
        Assert.AreEqual(11, pinnedContext.Count);
        CollectionAssert.AreEquivalent(pinnedContext, windowContext);
        CollectionAssert.AreEquivalent(new[] { "1452,1288", "1453,1288", "1454,1288", "1450,1290", "1450,1291" },
            window.Sectors.Where(s => s.Template is not null).Select(s => $"{s.Coordinate.X},{s.Coordinate.Y}").ToArray());
        Assert.IsTrue(window.Sectors.Where(s => s.Template is not null).All(s => s.Present && s.Scope == AreaScope.Context && s.DatOrdinal is null));
        Assert.AreEqual(25, window.Sectors.Count(s => s.Present));
        // The same visuals: the five generated mountain sectors add terrain only, drawn from frames the core already uses. The
        // window also decodes the other frames of its animated scenery (ARC-RENDER-035), which the pinned profile does not.
        var drawn = window.Visuals.Where(v => v.Key.Layer != AreaLayerKind.SceneryFrame).ToList();
        Assert.AreEqual(pinned.Visuals.Count, drawn.Count);
        CollectionAssert.AreEquivalent(pinned.Visuals.Select(v => v.RgbaSha256).ToList(), drawn.Select(v => v.RgbaSha256).ToList());
        var animated = window.Visuals.Where(v => v.Key.Layer == AreaLayerKind.SceneryFrame).GroupBy(v => v.Key.Object).ToList();
        Assert.AreEqual((4, 76), (animated.Count, animated.Sum(g => g.Count())), "the wreck's fires: animated scenery and their other frames");
        Assert.AreEqual(pinned.Objects.Count, window.Objects.Count);
        // Every core terrain cell.
        CollectionAssert.AreEquivalent(Terrain(pinned), Terrain(window));
        Assert.AreEqual(36_864, Terrain(window).Count);
        // The core static and mobile objects, by record and place.
        CollectionAssert.AreEquivalent(Objects(pinned, AreaSourceKind.Static), Objects(window, AreaSourceKind.Static));
        CollectionAssert.AreEquivalent(Objects(pinned, AreaSourceKind.Mobile), Objects(window, AreaSourceKind.Mobile));
        Assert.AreEqual(426, Objects(window, AreaSourceKind.Static).Count);
        Assert.AreEqual(88, Objects(window, AreaSourceKind.Mobile).Count);
    }

    [TestMethod, TestCategory("RealData")]
    [DataRow(1409, 1314, 0, "town: the largest cluster of hand-made sectors")]
    [DataRow(1384, 239, 25, "elven forest wilderness: every sector generated")]
    [DataRow(1450, 1290, 13, "a generated centre beside the Crash Site")]
    public void Main_map_windows_load_complete_through_the_universal_path(int x, int y, int templates, string what)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT"))) Assert.Inconclusive("DAT_ROOT is required.");
        using var stack = Stack();
        var catalog = WorldCatalog.Open(stack);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(x, y));
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, $"{what}: {planned.Detail}");
        Assert.IsTrue(planned.Plan!.Limits.MaxSurvey <= 25 && planned.Plan.Limits.MaxPresentSectors <= 25, what);
        using var owner = planned.Plan.Open();
        var window = owner.LoadStrict();
        Print(what, window);
        Assert.AreEqual(9, window.Sectors.Count(s => s.Scope == AreaScope.Core && s.Present), what);
        Assert.AreEqual(25, window.Sectors.Count(s => s.Present), what);
        Assert.AreEqual(templates, window.Sectors.Count(s => s.Template is not null), what);
        Assert.AreEqual(9 * 4096, window.Terrain.Count(t => t.Scope == AreaScope.Core), what);
        // Objects and lights of generated sectors stand inside them.
        foreach (var sector in window.Sectors.Where(s => s.Template is not null))
        {
            Assert.IsTrue(sector.Lights.All(l => (uint)l[2] >> 6 == sector.Coordinate.X && (uint)l[3] >> 6 == sector.Coordinate.Y), what);
            Assert.IsTrue(window.Objects.Where(o => o.Key.Kind == AreaSourceKind.Static && o.Key.Sector == sector.Coordinate.Packed)
                .All(o => o.WorldX >> 6 == sector.Coordinate.X && o.WorldY >> 6 == sector.Coordinate.Y), what);
        }
        // ARC-DATA-035: the lighting of every window reads, generated core sectors included (their scheme word from the template).
        var lighting = Arcanum.Data.Lighting.AreaLightingReader.Read(stack, window);
        var lights = Arcanum.Data.Lighting.StationaryLightReader.Read(stack, window);
        Console.WriteLine($"ARC-DATA-032 {what} lighting={lighting.Failure} stationary={lights.Failure}");
        Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.None, lighting.Failure, what);
        Assert.AreEqual(Arcanum.Data.Lighting.LightingFailure.None, lights.Failure, what);
        Assert.AreEqual(9, lighting.Source!.SectorSchemes.Count, what);
        if (templates == 25)
        {
            Assert.IsTrue(window.Objects.Any(o => o.Key.Kind == AreaSourceKind.Static), what);
            Assert.IsTrue(window.Sectors.Any(s => s.Lights.Count > 0), what);
        }
    }

    private static List<string> Sectors(AreaSnapshot area, AreaScope scope) =>
        area.Sectors.Where(s => s.Scope == scope && s.Present && s.Template is null).Select(s => $"{s.Coordinate.X},{s.Coordinate.Y}:{s.PayloadSha256}").ToList();

    private static List<string> Terrain(AreaSnapshot area) =>
        area.Terrain.Where(t => t.Scope == AreaScope.Core).Select(t => $"{t.WorldX},{t.WorldY}:{t.RawArt}").ToList();

    private static List<string> Objects(AreaSnapshot area, AreaSourceKind kind) =>
        area.Objects.Where(o => o.Key.Kind == kind && o.Scope == AreaScope.Core).Select(o => $"{o.RawSha256}@{o.WorldX},{o.WorldY}").ToList();

    private static void Print(string name, AreaSnapshot area) =>
        Console.WriteLine($"ARC-DATA-032 {name} sectors={area.Sectors.Count} present={area.Sectors.Count(s => s.Present)} templates={area.Sectors.Count(s => s.Template is not null)} " +
            $"terrain={area.Terrain.Count} objects={area.Objects.Count} static={area.Objects.Count(o => o.Key.Kind == AreaSourceKind.Static)} " +
            $"mobileCore={area.Objects.Count(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope == AreaScope.Core)} mobileContext={area.Objects.Count(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope == AreaScope.Context)} " +
            $"visuals={area.Visuals.Count}");

    private static ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(Environment.GetEnvironmentVariable("DAT_ROOT")!);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
        : throw new AssertInconclusiveException($"{name} is required.");
}
