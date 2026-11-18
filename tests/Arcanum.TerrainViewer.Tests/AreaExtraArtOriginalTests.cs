using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-RENDER-033 census (DAT_ROOT = the game folder): on every interior the strict plan reads, each scenery or
/// trap with a destroyed ART (field 24) and each item held in an inventory resolves and decodes its art through the viewer's
/// extra art.</summary>
[TestClass]
public sealed class AreaExtraArtOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_destroyed_ART_and_held_items_ground_ART_of_the_interiors_reads()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } found ? found : null;
        if (root is null) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        using var art = new AreaExtraArt(ContentStack.Open(discovery.Plan!).Stack!, new(MaxArts: 65536, MaxRgbaBytes: 1L << 32));
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, destroyed = 0, held = 0;
        var failed = new List<string>(); var destroyedMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        uint? Number(AreaObjectRecord row, int slot) =>
            row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : null;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            using var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open();
            var inspection = owner.Inspect();
            if (!inspection.IsComplete) continue;
            maps++;
            foreach (var row in inspection.Snapshot!.Objects.Where(r => r.Scope == AreaScope.Core))
            {
                uint? aid = null;
                if (row.Type is 3 or 17 && Number(row, 24) is { } d && !AreaDataOwner.NamesNoArt(d) && d != Number(row, 0))
                { aid = d; destroyed++; destroyedMaps[map.Name] = destroyedMaps.GetValueOrDefault(map.Name) + 1; }
                else if (row.Type is >= 5 and <= 14 && Number(row, 18) is { } flags && (flags & 0x1000) != 0 && Number(row, 0) is { } ground
                    && !AreaDataOwner.NamesNoArt(ground)) { aid = ground; held++; }
                if (aid is { } id && art.Load(id) is { Status: not AreaExtraArtStatus.Ready } load) failed.Add($"{map.Name} type {row.Type} 0x{id:X8} {load.Status} {load.Detail}");
            }
        }
        Console.WriteLine($"ARC-RENDER-033 census maps={maps} destroyed={destroyed} held={held} arts={art.Loads.Count} failed={failed.Count}"
            + $" destroyed-maps={string.Join(",", destroyedMaps.Take(12).Select(p => $"{p.Key}:{p.Value}"))}");
        Assert.AreEqual(80, maps);
        Assert.AreEqual(0, failed.Count, string.Join("; ", failed.Take(20)));
    }
    /// <summary>ARC-SCRIPT-2670 (opt-in, DAT_ROOT = the game folder): the busted arts of the non-respawning sceneries (type flag 0x20 clear, field 24 set) by their frame count, which decides the restart the break's art write runs
    /// (0x43EB30 -> 0x43F710: several frames start the loop again, one frame kills the object's runs), and how many of those sceneries animate before the break (scenery flag 0x1 clear, the T0 art of several frames).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Busted_arts_of_the_non_respawning_sceneries_by_frame_count()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } found ? found : null;
        if (root is null) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census; this is not a synthetic pass."); return; }
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        using var art = new AreaExtraArt(ContentStack.Open(discovery.Plan!).Stack!, new(MaxArts: 65536, MaxRgbaBytes: 1L << 32));
        var catalog = WorldCatalog.Open(stack);
        uint? Number(AreaObjectRecord row, int slot) =>
            row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : null;
        int sceneries = 0, bustable = 0, one = 0, several = 0, animated = 0, unread = 0;
        var byFrames = new SortedDictionary<int, int>();
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            using var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open();
            var inspection = owner.Inspect();
            if (!inspection.IsComplete) continue;
            foreach (var row in inspection.Snapshot!.Objects.Where(r => r.Scope == AreaScope.Core && r.Type == 3))
            {
                sceneries++;
                if (((Number(row, 36) ?? 0) & 0x20) != 0 || Number(row, 24) is not { } d || AreaDataOwner.NamesNoArt(d)) continue;
                bustable++;
                if (art.Load(d) is not { Status: AreaExtraArtStatus.Ready, Frame: { } head }) { unread++; continue; }
                var frames = d >> 28 == 4 ? Math.Clamp(head.FramesPerGroup, 1, 32) : 1;
                byFrames[frames] = byFrames.GetValueOrDefault(frames) + 1;
                if (frames > 1) several++; else one++;
                if (((Number(row, 36) ?? 0) & 0x1) == 0 && Number(row, 0) is { } own && !AreaDataOwner.NamesNoArt(own) && art.Load(own) is { Frame: { } ownHead } && ownHead.FramesPerGroup > 1) animated++;
            }
        }
        Console.WriteLine($"ARC-SCRIPT-2670 census sceneries={sceneries} bustable={bustable} busted-one-frame={one} busted-several-frames={several} animated-before={animated} unread={unread} by-frames={string.Join(",", byFrames.Select(p => $"{p.Key}:{p.Value}"))}");
        Assert.AreEqual(0, unread);
        // First observation 2026-11-18: of 49534 core sceneries only 4 bust (no respawn flag, a destroyed art); all four busted arts are one frame, so the restart kills the object's runs and no installed art restarts a loop; one of the four animates before the break.
        Assert.AreEqual((4, 4, 0, 1), (bustable, one, several, animated), "the census changed: update the busted-art restart notes (ledger OD7, BS3c)");
    }
}
