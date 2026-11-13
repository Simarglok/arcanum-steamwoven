using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Inventory;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1654 census, opt-in (<c>DAT_ROOT</c>, the game folder): how full the installed NPC lists are as the original's room test (0x466510 → 0x4664C0) sees them. Every NPC holder of every installed whole map: its
/// items outside the worn and quick slots, the cells their rectangles take (the inventory art's frame in 32-pixel cells, 0x461780; 1 x 1 when the art does not read), and whether
/// a free place is left for items of a few sizes (<see cref="InventoryGridRules.HolderFreeSlot"/>). Counts only; no row, location or text is printed.
/// </summary>
[TestClass]
public sealed class NpcGridCensusRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_npc_lists_leave_room_for_the_items_they_pick_up()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        using var icons = AreaItemIcons.Open(Path.Combine(root, "modules", "Arcanum.dat"), out var detail) ?? throw new AssertFailedException(detail);
        var sizes = new (string Name, int Width, int Height)[] { ("1x1", 1, 1), ("1x2", 1, 2), ("2x2", 2, 2), ("2x3", 2, 3), ("3x3", 3, 3), ("1x4", 1, 4) };
        var noRoom = sizes.ToDictionary(size => size.Name, _ => 0);
        int notReady = 0, maps = 0, npcs = 0, withItems = 0, artRead = 0, artMissing = 0, outside = 0, overlapping = 0, maxCells = 0, maxItems = 0;
        var cellHistogram = new SortedDictionary<int, int>();
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) continue;
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { notReady++; continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            var read = AreaInventoryReader.Read(snapshot);
            if (read.Inventory is not { } inventory) continue;
            maps++;
            var rows = snapshot.Objects.ToDictionary(row => row.Key);
            foreach (var holder in inventory.Holders.Where(holder => holder.Type == AreaInventoryHolderType.Npc))
            {
                npcs++;
                var list = new List<InventoryGridRules.HolderListItem>();
                foreach (var item in inventory.ListOf(holder.SourceIndex))
                {
                    var frame = icons.InventoryFrame(new SessionItemKind((SessionItemType)item.Type, item.Prototype, item.Name, 0), CombatFieldReader.Read(rows[item.Key]).Fields).Frame;
                    if (frame is null) artMissing++; else artRead++;
                    var (width, height) = InventoryGridRules.CellsOf(frame is null ? null : ((uint)frame.Width, (uint)frame.Height));
                    list.Add(new(item.InventoryLocation, width, height));
                }
                var grid = list.Where(item => item.Location is not (>= 1000 and <= 1008 or >= 2000 and <= 2009)).ToArray();
                if (grid.Length > 0) withItems++;
                maxItems = Math.Max(maxItems, grid.Length);
                // The occupied cells as the original's flat buffer marks them, and the rectangles that leave the grid or meet another one.
                var cells = new int[InventoryGridRules.ContainerRows * InventoryGridRules.Columns];
                foreach (var item in grid.Where(item => item.Location >= 0))
                {
                    if (item.Location % InventoryGridRules.Columns + item.Width > InventoryGridRules.Columns || item.Location / InventoryGridRules.Columns + item.Height > InventoryGridRules.CritterRows) outside++;
                    for (var row = 0; row < item.Height; row++)
                        for (var column = 0; column < item.Width; column++)
                            if (item.Location + row * InventoryGridRules.Columns + column is var cell && cell < cells.Length && cells[cell]++ > 0) overlapping++;
                }
                var used = cells.Take(InventoryGridRules.CritterRows * InventoryGridRules.Columns).Count(cell => cell > 0);
                maxCells = Math.Max(maxCells, used);
                cellHistogram[used / 20 * 20] = cellHistogram.GetValueOrDefault(used / 20 * 20) + 1;
                foreach (var size in sizes)
                    if (InventoryGridRules.HolderFreeSlot(list, InventoryGridRules.CritterRows, size.Width, size.Height) < 0) noRoom[size.Name]++;
            }
        }
        Console.WriteLine($"ARC-AI-1654 census maps={maps} mapsNotReady={notReady} npcs={npcs} withGridItems={withItems} maxGridItems={maxItems} maxCellsUsed={maxCells} artRead={artRead} artMissing={artMissing} " +
            $"rectanglesLeavingGrid={outside} overlappingCells={overlapping}");
        Console.WriteLine("ARC-AI-1654 census cells used (buckets of 20): " + string.Join(", ", cellHistogram.Select(pair => $"{pair.Key}-{pair.Key + 19}: {pair.Value}")));
        Console.WriteLine("ARC-AI-1654 census lists with no place for: " + string.Join(", ", noRoom.Select(pair => $"{pair.Key} {pair.Value}")));
        Assert.IsTrue(npcs > 0);
    }
}
