using Arcanum.Data.Content;
using Arcanum.Data.Inventory;
using Arcanum.Data.Maps;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-2351 (ledger AR8): how many installed NPCs show an armour part in their stored current art (field 1) against the body armour (slot 1006) their map data lists in their inventory.</summary>
[TestClass]
public sealed class AreaBodyArmorArtCensusTests
{
    /// <summary>Opt-in (DAT_ROOT = the game folder): on every whole map, the NPC rows by the armour part of their stored art (0 or more) and by whether their data lists a body armour; counts only.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Installed_npc_art_armour_part_against_the_body_armour_their_data_lists()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        int maps = 0, npcs = 0, partWithArmour = 0, partWithout = 0, bareWithArmour = 0, bareWithout = 0, withoutInventory = 0;
        foreach (var map in WorldCatalog.Open(stack).Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            Arcanum.Data.Area.AreaSnapshot snapshot;
            try
            {
                snapshot = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module,
                    "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
                    "--game-root", root, "--area-map", map.Name]), default).Area!.Snapshot;
            }
            catch (Exception error) when (error is InvalidOperationException or Arcanum.Data.Area.AreaException) { continue; }
            maps++;
            var inventory = AreaInventoryReader.Read(snapshot).Inventory;
            foreach (var row in snapshot.Objects.Where(row => row.Type == 16))
            {
                if (row.EffectiveFields.FirstOrDefault(f => f.Slot == 0) is not { Number: { } art }) continue;
                npcs++;
                var part = SoundCueRules.CritterArtArmor(unchecked((uint)art));
                if (inventory is null || !inventory.TryGetHolder(row.Key.SourceIndex, out var holder)) { withoutInventory++; continue; }
                var wearsArmour = holder.Items.Any(index => inventory.TryGetItem(index, out var item) && item.InventoryLocation == (int)EquipmentSlot.Armor);
                if (part > 0) { if (wearsArmour) partWithArmour++; else partWithout++; }
                else if (wearsArmour) bareWithArmour++; else bareWithout++;
            }
        }
        Console.WriteLine($"ARC-SCRIPT-2351 maps={maps} npc rows={npcs} (no holder {withoutInventory}): art part>0 with data armour {partWithArmour}, art part>0 without {partWithout}, art part 0 with data armour {bareWithArmour}, art part 0 without {bareWithout}");
        // First observation 2026-11-16 (installed 1.0.7.4 data): 80 maps load, 2236 NPC rows with a stored art, 95 of them with no inventory holder (counted in none of the four).
        Assert.AreEqual((80, 2236, 95), (maps, npcs, withoutInventory), "the installed data's NPC rows changed");
        Assert.AreEqual((171, 53, 1, 1916), (partWithArmour, partWithout, bareWithArmour, bareWithout), "the census changed: update ledger AR8");
    }
}
