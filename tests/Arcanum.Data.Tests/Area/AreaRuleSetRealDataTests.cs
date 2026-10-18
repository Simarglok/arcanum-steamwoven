using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>
/// ARC-DATA-036 opt-in checks on the installed original (DAT_ROOT = the game folder, and the pinned inputs
/// ARC_AREA_MODULE, ARC_AREA_RESOURCE1, ARC_AREA_RESOURCE2, ARC_AREA_MOBILE, ARC_CAVE_MOBILE, ARC_AREA_PROTOTYPES): the
/// pinned profiles, built by the rules of every plan, reproduce the universal path. The cave profile equals the cave's
/// whole-map plan, and each profile's endpoint is the one use transition the map's transition data give in its core.
/// Prints counts and digests only.
/// </summary>
[TestClass]
public sealed class AreaRuleSetRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_cave_profile_reproduces_the_caves_whole_map_plan()
    {
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrEmpty(cave) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT")))
            Assert.Inconclusive("DAT_ROOT and the pinned cave inputs are required.");
        using var stack = Stack();
        var planned = AreaMapPlans.Create(stack, WorldCatalog.Open(stack), "Crash Site Cave");
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, planned.Detail);
        using var owner = planned.Plan!.Open();
        var whole = owner.LoadStrict();
        using var pinnedOwner = AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            cave, Required("ARC_AREA_PROTOTYPES"));
        var pinned = pinnedOwner.LoadStrict();
        Print("whole", whole); Print("pinned", pinned);

        Assert.AreEqual("arc-data-019-whole-map-original-rules-v1", whole.Profile);
        Assert.AreEqual((pinned.CatalogueMap, pinned.RuntimeMap), (whole.CatalogueMap, whole.RuntimeMap));
        CollectionAssert.AreEquivalent(Sectors(pinned), Sectors(whole));
        CollectionAssert.AreEquivalent(Terrain(pinned), Terrain(whole));
        CollectionAssert.AreEquivalent(Objects(pinned, AreaSourceKind.Static), Objects(whole, AreaSourceKind.Static));
        CollectionAssert.AreEquivalent(Objects(pinned, AreaSourceKind.Mobile), Objects(whole, AreaSourceKind.Mobile));
        // Every visual: the same frame, read the same way, by object record and place.
        CollectionAssert.AreEquivalent(Visuals(pinned), Visuals(whole));
        Assert.AreEqual(CaveProfile.ExpectedCounts.Visuals, Visuals(whole).Count);
    }

    [TestMethod, TestCategory("RealData")]
    [DataRow("Arcanum1-024-fixed", false)]
    [DataRow("Crash Site Cave", true)]
    public void A_profile_endpoint_is_the_maps_use_transition_in_its_core(string map, bool isCave)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE")))
            Assert.Inconclusive("DAT_ROOT and the pinned inputs are required.");
        using var pinnedOwner = isCave
            ? AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"), Required("ARC_CAVE_MOBILE"), Required("ARC_AREA_PROTOTYPES"))
            : AreaOriginalValidator.OpenFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"), Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var pinned = pinnedOwner.LoadStrict();
        using var stack = Stack();
        using var scripts = ScriptContent.Over(stack);
        var catalog = WorldCatalog.Open(stack);
        var transitions = MapTransitions.Read(catalog, MapList.Read(stack), catalog.Find(map)!, scripts);
        var core = pinned.Sectors.Where(s => s.Scope == AreaScope.Core).Select(s => s.Coordinate).ToHashSet();
        var derived = AreaEndpoints.FromTransitions(transitions, core.Contains);
        Console.WriteLine($"ARC-DATA-036 {map} core={core.Count} endpoints={derived.Count}");
        CollectionAssert.AreEqual((isCave ? CaveProfile.Endpoints : OriginalProfile.Endpoints).ToArray(), derived.ToArray());
        CollectionAssert.AreEqual(derived.ToArray(), pinned.Endpoints.ToArray());
    }

    private static List<string> Sectors(AreaSnapshot area) =>
        area.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => $"{s.Coordinate.X},{s.Coordinate.Y}:{s.PayloadSha256}:{s.LightCount}").ToList();

    private static List<string> Terrain(AreaSnapshot area) =>
        area.Terrain.Where(t => t.Scope == AreaScope.Core).Select(t => $"{t.WorldX},{t.WorldY}:{t.RawArt}").ToList();

    private static List<string> Objects(AreaSnapshot area, AreaSourceKind kind) =>
        area.Objects.Where(o => o.Key.Kind == kind && o.Scope == AreaScope.Core).Select(o => $"{o.RawSha256}@{o.WorldX},{o.WorldY}:{o.Disposition}").ToList();

    private static List<string> Visuals(AreaSnapshot area)
    {
        var objects = area.Objects.ToDictionary(o => o.Key);
        return area.Visuals.Select(v => (v.Key.Object is { } key ? $"{objects[key].RawSha256}@{objects[key].WorldX},{objects[key].WorldY}" : $"terrain:{v.LookupArt}") +
            $":{v.PayloadSha256}:{v.ViewLength}:{v.PhysicalGroup}:{v.Frame}:{v.EffectivePalette}:{v.Mirrored}:{v.Policy}:{v.RgbaSha256}").ToList();
    }

    private static void Print(string name, AreaSnapshot area) =>
        Console.WriteLine($"ARC-DATA-036 {name} sectors={area.Sectors.Count(s => s.Present)} terrain={area.Terrain.Count} objects={area.Objects.Count} " +
            $"visuals={area.Visuals.Count} endpoints={area.Endpoints.Count}");

    private static ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(Environment.GetEnvironmentVariable("DAT_ROOT")!);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
        : throw new AssertInconclusiveException($"{name} is required.");
}
