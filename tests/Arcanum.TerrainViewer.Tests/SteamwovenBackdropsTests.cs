using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2576, ARC-UI-3143: the census family of an open window (which picks its kit background and the dim of the world beside it); the generated wood and its seam ratio (SWI-VER-5) are retired.</summary>
[TestClass]
public sealed class SteamwovenBackdropsTests
{
    [TestMethod]
    public void Each_window_mode_of_the_menu_families_has_its_census_family()
    {
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf(InterfaceMode.Inventory));
        foreach (var mode in new[] { InterfaceMode.Loot, InterfaceMode.Steal, InterfaceMode.Barter, InterfaceMode.InventoryKind5, InterfaceMode.InventoryKind6 })
            Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf(mode), mode.ToString());
        Assert.AreEqual(SteamwovenBackdropFamily.CharacterSheet, SteamwovenBackdrops.FamilyOf(InterfaceMode.Character));
        Assert.AreEqual(SteamwovenBackdropFamily.Logbook, SteamwovenBackdrops.FamilyOf(InterfaceMode.Logbook));
        Assert.AreEqual(SteamwovenBackdropFamily.Reading, SteamwovenBackdrops.FamilyOf(InterfaceMode.Book));
        Assert.AreEqual(SteamwovenBackdropFamily.WorldMap, SteamwovenBackdrops.FamilyOf(InterfaceMode.WorldMap));
        Assert.AreEqual(SteamwovenBackdropFamily.Schematic, SteamwovenBackdrops.FamilyOf(InterfaceMode.Schematic));
        // The world stays in view in the dialog and sleep views, and a target mode or the game is no window.
        foreach (var mode in new[] { InterfaceMode.Game, InterfaceMode.Dialog, InterfaceMode.Sleep, InterfaceMode.SpellTarget, InterfaceMode.SkillTarget, InterfaceMode.QuantityPicker, InterfaceMode.ItemTarget, InterfaceMode.FollowerOrder })
            Assert.IsNull(SteamwovenBackdrops.FamilyOf(mode), mode.ToString());
    }

    [TestMethod]
    public void The_window_nearest_the_top_of_the_stack_gives_the_family()
    {
        // A quantity picker over the inventory keeps the inventory's backdrop; a barter over a dialog takes the barter's; a dialog alone has none.
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Inventory, InterfaceMode.QuantityPicker]));
        Assert.AreEqual(SteamwovenBackdropFamily.Inventory, SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Dialog, InterfaceMode.Barter]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Dialog]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Inventory, InterfaceMode.Dialog]), "a dialog on top keeps the world in view");
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game, InterfaceMode.Logbook, InterfaceMode.Sleep]));
        Assert.IsNull(SteamwovenBackdrops.FamilyOf([InterfaceMode.Game]));
    }
}
