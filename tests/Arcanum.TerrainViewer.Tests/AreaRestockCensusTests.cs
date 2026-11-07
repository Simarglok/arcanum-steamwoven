using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-093: what the restock rules meet in the installed maps (counts only).
[TestClass]
public sealed class AreaRestockCensusTests
{
    /// <summary>Opt-in (DAT_ROOT = the game folder): on every whole map, the containers and NPCs with an inventory source, the containers'
    /// restock flags (0x200 generated once, 0x400 regenerates alone), the persistent items (item flag 0x2000) and the merchants' substitute
    /// containers. First observation 2026-10-28.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_restock_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var loaded = AreaBarterData.Load(module);
        Assert.AreEqual(AreaBarterDataStatus.Ready, loaded.Status, loaded.Detail);
        var data = loaded.Data!;
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        // ARC-WORLD-093: every set as the fixed reader admits it (1..count reachable, a parsing buy line allowed).
        var sets = Enumerable.Range(1, data.Sources.Count).GroupBy(set => InventorySourceRules.Read(data.Sources, data.Buy, set, data.Existing.Contains).Status)
            .ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("sets: " + string.Join(", ", sets.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));
        // First observation 2026-10-28: 84 of 123 sets read and the shape admission refused 39 (Malformed). ARC-DEV-161 (B40): 38 of them were
        // sets the original reads whole (a negative coin minimum such as -100,250, a trailing comma, a space after a comma), among them the
        // Crash Site chests' source 69; now 122 read and only set 94 (a pair the original reads shifted by a space) is refused.
        Assert.AreEqual((122, 1, 123), (sets.GetValueOrDefault(InventorySourceStatus.Parsed), sets.GetValueOrDefault(InventorySourceStatus.Malformed),
            sets.Values.Sum()), "the census changed: update the restock contract");
        Assert.AreEqual(InventorySourceStatus.Malformed, InventorySourceRules.Read(data.Sources, data.Buy, 94, data.Existing.Contains).Status);
        // The Crash Site's two spawn-once chests (sources 56 and 69) and the other treasure sets with a negative coin minimum.
        foreach (var set in new[] { 56, 69, 70, 71 })
            Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(data.Sources, data.Buy, set, data.Existing.Contains).Status, $"chest set {set}");
        var treasure = InventorySourceRules.Read(data.Sources, data.Buy, 69, data.Existing.Contains).Set!;
        Assert.AreEqual((-100, 250), (treasure.MinCoins, treasure.MaxCoins));
        Assert.AreEqual(new InventorySourceEntry(30, 15183), treasure.Items[^1], "the line after the trailing comma of 1,8273,");
        foreach (var set in new[] { 5, 7, 19, 105 })
            Assert.AreEqual(InventorySourceStatus.Parsed, InventorySourceRules.Read(data.Sources, data.Buy, set, data.Existing.Contains).Status, $"merchant set {set}");
        var merchants = new List<string>();
        // ARC-WORLD-103: the maps whose NPCs regenerate on their own (a source of their own), for the redress observation.
        var npcSources = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int cont = 0, contSrc = 0, c200 = 0, c400 = 0, c200src = 0, c400src = 0, npcs = 0, npcSrc = 0, persistent = 0, items = 0;
        foreach (var map in WorldCatalog.Open(stack).Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            TerrainLoadResult result;
            try
            {
                result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module,
                    "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
                    "--game-root", root, "--area-map", map.Name]), default);
            }
            catch (Exception error) when (error is InvalidOperationException or Arcanum.Data.Area.AreaException) { continue; }
            if (Arcanum.Data.Inventory.AreaInventoryReader.Read(result.Area!.Snapshot).Inventory is not { } inventory) continue;
            foreach (var item in inventory.Items) { items++; if (Arcanum.Simulation.Items.RestockRules.Keeps(item.ItemFlags)) persistent++; }
            foreach (var h in inventory.Holders)
            {
                if (h.Type == Arcanum.Data.Inventory.AreaInventoryHolderType.Npc)
                {
                    npcs++;
                    if (h.InventorySource != 0) { npcSrc++; npcSources[map.Name] = npcSources.GetValueOrDefault(map.Name) + 1; }
                    var container = h.SubstituteInventory?.SourceIndex is { } index && inventory.TryGetHolder(index, out var found) ? found : null;
                    if (container is not null && data.BuyList(container.InventorySource).Status is InventorySourceBuyStatus.Listed or InventorySourceBuyStatus.Everything)
                        merchants.Add($"{map.Name}: own source {h.InventorySource}, container source {container.InventorySource} flags 0x{container.ContainerFlags:X}");
                }
                else if (h.ContainerFlags is { } f)
                {
                    cont++;
                    if (h.InventorySource != 0) contSrc++;
                    if ((f & Arcanum.Simulation.Items.RestockRules.GeneratedOnceFlag) != 0) { c200++; if (h.InventorySource != 0) c200src++; }
                    if ((f & Arcanum.Simulation.Items.RestockRules.RegeneratesAloneFlag) != 0) { c400++; if (h.InventorySource != 0) c400src++; }
                }
            }
        }
        Console.WriteLine($"containers {cont} with source {contSrc}; 0x200 {c200} ({c200src} with source); 0x400 {c400} ({c400src} with source); npcs {npcs} with source {npcSrc}; items {items} persistent {persistent}");
        Console.WriteLine("merchants: " + string.Join("; ", merchants));
        Console.WriteLine("npcs with a source by map: " + string.Join("; ", npcSources.Select(p => $"{p.Key} {p.Value}")));
        Assert.AreEqual((434, 139, 116, 116, 0, 0), (cont, contSrc, c200, c200src, c400, c400src), "the census changed: update the restock contract");
        Assert.AreEqual((2120, 238, 2362, 42), (npcs, npcSrc, items, persistent), "the census changed: update the restock contract");
        // The eight merchants (wheel clan 3, shopmap 1, qintarra 4: those maps load now) have no source of their own and containers without either restock flag.
        CollectionAssert.AreEquivalent(new[]
        {
            "shopmap: own source 0, container source 19 flags 0x9", "wheel clan: own source 0, container source 5 flags 0x1",
            "qintarra: own source 0, container source 16 flags 0x9", "qintarra: own source 0, container source 4 flags 0x9",
            "qintarra: own source 0, container source 109 flags 0x9", "qintarra: own source 0, container source 109 flags 0x9",
            "wheel clan: own source 0, container source 105 flags 0x1", "wheel clan: own source 0, container source 7 flags 0x1",
        }, merchants, "the census changed: update the restock contract");
    }
}
