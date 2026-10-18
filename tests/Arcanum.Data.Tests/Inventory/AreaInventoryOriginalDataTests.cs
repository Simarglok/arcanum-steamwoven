using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Inventory;

[TestClass]
public sealed class AreaInventoryOriginalDataTests
{
    private const uint NoDisplay = 0x10, NoDrop = 0x20, NoDecay = 0x20000;

    [TestMethod, TestCategory("RealData")]
    public void Original_crash_site_inventory_matches_the_item_research()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module)) Assert.Inconclusive("Opt-in original Area inputs are required.");
        using var owner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var result = AreaInventoryReader.Read(owner.LoadStrict());
        Assert.AreEqual(AreaInventoryFailure.None, result.Failure, $"refused at {result.SourceIndex}");
        var inventory = result.Inventory!;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Items = inventory.Items.Count, Holders = inventory.Holders.Count,
            Lists = inventory.Holders.Where(h => h.Items.Count > 0).Select(h => new { h.SourceIndex, h.Scope, h.Items }),
        }));

        // docs/research/crash-site-item-inventory.md section 1: the whole loaded area, then the core.
        Assert.AreEqual((53, 45), (inventory.Items.Count, inventory.Holders.Count));
        Assert.AreEqual(16, inventory.Holders.Sum(h => h.Items.Count));
        var core = inventory.Items.Where(i => i.Scope == AreaScope.Core).ToArray();
        Assert.AreEqual(52, core.Length);
        Assert.AreEqual(15, core.Count(i => i.InInventory));
        var ground = core.Where(i => !i.InInventory).ToArray();
        Assert.AreEqual((37, 21, 16), (ground.Length, ground.Count(i => i.Type == AreaItemType.Food), ground.Count(i => i.Type == AreaItemType.Generic)));
        Assert.IsTrue(ground.All(i => i.Scripts.Count == 0 && i.Identified && i.WorldX is not null));
        var byPrototype = ground.GroupBy(i => i.Prototype).ToDictionary(g => g.Key, g => g.Count());
        CollectionAssert.AreEquivalent(new[] { (10081, 8), (10082, 10), (10113, 3), (15089, 4), (15091, 4), (15102, 4), (15107, 4) },
            byPrototype.Select(p => (p.Key, p.Value)).ToArray());
        CollectionAssert.AreEquivalent(new[] { 3600, 3608, 5600 }, ground.Select(i => i.Name).Distinct().ToArray());
        CollectionAssert.AreEquivalent(new[] { 10061, 10062, 10093, 15069, 15071, 15082, 15087 }, ground.Select(i => i.Description).Distinct().ToArray());
        Assert.IsTrue(ground.All(i => (i.ItemFlags & (NoDrop | NoDecay | NoDisplay)) == 0), "no OIF_NO_DROP/NO_DECAY/NO_DISPLAY on the ground");

        var lists = new Dictionary<int, int[]>
        {
            [2907] = [1714, 2421, 2908], [6611] = [4459, 4699, 5821, 5820], [980] = [4458], [6460] = [70],
            [5699] = [3312, 7282], [4457] = [4698, 6177], [4200] = [4201], [1201] = [2527], [4453] = [], [6536] = [],
        };
        var coreHolders = inventory.Holders.Where(h => h.Scope == AreaScope.Core).ToArray();
        CollectionAssert.AreEquivalent(lists.Keys.ToArray(), coreHolders.Where(h => h.Items.Count > 0 || h.Type == AreaInventoryHolderType.Container).Select(h => h.SourceIndex).ToArray());
        foreach (var (holder, items) in lists)
        {
            Assert.IsTrue(inventory.TryGetHolder(holder, out var row), holder.ToString());
            CollectionAssert.AreEqual(items, row.Items.ToArray(), holder.ToString());
            Assert.IsTrue(inventory.ListOf(holder).Select((item, index) => item.Holder == holder && item.ListIndex == index && item.Scope == AreaScope.Core).All(ok => ok));
        }
        Assert.AreEqual(8, coreHolders.Count(h => h.Type == AreaInventoryHolderType.Npc && h.Items.Count > 0));

        // Virgil and the ring: list index 0, name class 2804 unique in the area, examine script only.
        Assert.IsTrue(inventory.TryGetHolder(2907, out var virgil));
        Assert.AreEqual((AreaInventoryHolderType.Npc, 31073, 17102), (virgil.Type, virgil.Description, virgil.Prototype));
        Assert.IsNull(virgil.Leader); Assert.IsNull(virgil.SubstituteInventory); Assert.IsNull(virgil.GoldItem);
        Assert.IsTrue(inventory.TryGetItem(1714, out var ring));
        Assert.AreEqual((AreaItemType.Armor, 2804, 30082, 0, 0, 0u), (ring.Type, ring.Name, ring.Description, ring.ListIndex!.Value, ring.InventoryLocation, ring.ItemFlags));
        Assert.AreEqual(1142, ring.Scripts.Single().Script);
        Assert.AreEqual(1, inventory.Items.Count(i => i.Name == 2804));
        Assert.IsTrue(inventory.TryGetItem(2908, out var worn)); Assert.AreEqual(1006, worn.InventoryLocation);
        Assert.IsTrue(inventory.TryGetItem(70, out var wielded)); Assert.AreEqual(1004, wielded.InventoryLocation);

        // Gold stacks are their holders' gold handles; NPC 6460 alone has an inventory source.
        Assert.IsTrue(inventory.TryGetHolder(6611, out var first)); Assert.IsTrue(inventory.TryGetHolder(980, out var second));
        Assert.AreEqual((4459, 4458), (first.GoldItem!.Value, second.GoldItem!.Value));
        Assert.IsTrue(inventory.TryGetItem(4459, out var gold21)); Assert.IsTrue(inventory.TryGetItem(4458, out var gold17));
        Assert.AreEqual((21, 17), (gold21.Quantity!.Value, gold17.Quantity!.Value));
        Assert.IsTrue(inventory.TryGetHolder(6460, out var armed)); Assert.AreEqual(84, armed.InventorySource);
        Assert.AreEqual(1, coreHolders.Count(h => h.Type != AreaInventoryHolderType.Container && h.InventorySource != 0));
        Assert.AreEqual(2, inventory.Items.Count(i => i.Quantity is not null));
        Assert.IsFalse(inventory.Items.Any(i => i.Type is AreaItemType.Key or AreaItemType.KeyRing or AreaItemType.Ammo));

        // Empty spawn-once, never-locked containers with key 0 and inventory sources 69 and 56.
        foreach (var (chest, source) in new[] { (4453, 69), (6536, 56) })
        {
            Assert.IsTrue(inventory.TryGetHolder(chest, out var row));
            Assert.AreEqual((AreaInventoryHolderType.Container, source, 0), (row.Type, row.InventorySource, row.ContainerKeyId!.Value));
            Assert.AreEqual(0x209u, row.ContainerFlags!.Value & 0x209u);
        }

        // Item attachments exist only on inventory items; two items carry NO_DECAY, the ring no item flag.
        CollectionAssert.AreEquivalent(new[] { 1714, 2527, 3312, 6177, 4201 }, core.Where(i => i.Scripts.Count > 0).Select(i => i.SourceIndex).ToArray());
        int[] ScriptsOf(int item) => inventory.TryGetItem(item, out var row) ? row.Scripts.Select(s => s.Script).Distinct().Order().ToArray() : [];
        CollectionAssert.AreEqual(new[] { 1327 }, ScriptsOf(3312)); CollectionAssert.AreEqual(new[] { 1118 }, ScriptsOf(6177));
        CollectionAssert.AreEqual(new[] { 2014 }, ScriptsOf(4201)); CollectionAssert.Contains(ScriptsOf(2527), 1341);
        // ARC-RES-055: the same script on the examine (0) and insert-item (21) points.
        CollectionAssert.AreEqual(new[] { new AreaInventoryScript(0, 1341), new AreaInventoryScript(21, 1341) },
            inventory.Items.Single(i => i.SourceIndex == 2527).Scripts.ToArray());
        CollectionAssert.AreEquivalent(new[] { 4201, 6177 }, core.Where(i => (i.ItemFlags & NoDecay) != 0).Select(i => i.SourceIndex).ToArray());

        // The one context list: holder 1382 with item 3488.
        Assert.AreEqual((1382, 3488), inventory.Holders.Where(h => h.Scope != AreaScope.Core && h.Items.Count > 0).Select(h => (h.SourceIndex, h.Items.Single())).Single());
        Assert.AreEqual(AreaInventoryFailure.None, AreaInventoryReader.Read(owner.LoadStrict()).Failure, "repeatable");
    }

    [TestMethod, TestCategory("RealData")]
    public void Original_cave_inventory_reconciles()
    {
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrEmpty(cave)) Assert.Inconclusive("Opt-in original inputs and ARC_CAVE_MOBILE are required.");
        using var owner = AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"), cave, Required("ARC_AREA_PROTOTYPES"));
        var result = AreaInventoryReader.Read(owner.LoadStrict());
        Assert.AreEqual(AreaInventoryFailure.None, result.Failure, $"refused at {result.SourceIndex}");
        var inventory = result.Inventory!;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Items = inventory.Items.Count, Holders = inventory.Holders.Count,
            Lists = inventory.Holders.Select(h => new { h.SourceIndex, h.Type, h.Items }),
            Ground = inventory.Items.Where(i => !i.InInventory).Select(i => new { i.SourceIndex, i.Type, i.Prototype }),
        }));
        // First observation (ARC-DATA-012): every cave item sits in one of four containers; seven NPCs hold nothing.
        Assert.AreEqual((28, 11), (inventory.Items.Count, inventory.Holders.Count));
        Assert.IsTrue(inventory.Items.All(i => i.InInventory));
        Assert.AreEqual(7, inventory.Holders.Count(h => h.Type == AreaInventoryHolderType.Npc && h.Items.Count == 0));
        var lists = new Dictionary<int, int[]>
        {
            [2] = [33, 13, 21, 3, 14, 34, 7, 24, 12, 37, 6, 11], [10] = [1, 5, 9], [15] = [29, 20, 28, 19, 16], [32] = [0, 36, 27, 31, 22, 18, 35, 38],
        };
        foreach (var (holder, items) in lists)
        {
            Assert.IsTrue(inventory.TryGetHolder(holder, out var row), holder.ToString());
            Assert.AreEqual(AreaInventoryHolderType.Container, row.Type);
            CollectionAssert.AreEqual(items, row.Items.ToArray(), holder.ToString());
        }
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new AssertInconclusiveException(name + " is required.");
}
