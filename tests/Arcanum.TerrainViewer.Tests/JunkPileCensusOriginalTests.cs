using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-063 census (DAT_ROOT = the game folder): the junk piles (containers of prototype 3043, description 3023)
/// the 80 interiors' data place, with their object flags; the session's piles are its own, and a map's piles are not ready.</summary>
[TestClass]
public sealed class JunkPileCensusOriginalTests
{
    private const int ContainerType = 2, ObjectFlagsSlot = 18, JunkPilePrototype = 3043;

    [TestMethod, TestCategory("RealData")]
    public void Junk_piles_the_interiors_place_are_counted()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } found ? found : null;
        if (root is null) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, piles = 0, blocking = 0;
        var onMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            using var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open();
            var inspection = owner.Inspect();
            if (!inspection.IsComplete) continue;
            maps++;
            foreach (var row in inspection.Snapshot!.Objects.Where(r => r.Scope == AreaScope.Core && r.Type == ContainerType && r.PrototypeNumber == JunkPilePrototype))
            {
                piles++;
                onMaps[map.Name] = onMaps.GetValueOrDefault(map.Name) + 1;
                var flags = row.EffectiveFields.FirstOrDefault(f => f.Slot == ObjectFlagsSlot && f.Presence == AreaPresence.Value)?.Number ?? 0;
                if ((flags & 0x400) == 0) blocking++;
            }
        }
        Console.WriteLine($"ARC-WORLD-063 census maps={maps} junk-piles={piles} blocking={blocking} on={string.Join(",", onMaps.Select(p => $"{p.Key}:{p.Value}"))}");
        Assert.AreEqual(80, maps);
        Assert.AreEqual(15, piles, "the documented census: 15 junk piles on 10 interiors");
        Assert.AreEqual(0, blocking, "every placed junk pile carries OF_NO_BLOCK (0x400)");
    }
}
