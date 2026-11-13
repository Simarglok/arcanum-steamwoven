using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1364 (ledger GB15): the lit state of the top strip's buttons and the Map button's face, as the callbacks 0x556A90 and 0x556B70 keep it.</summary>
[TestClass]
public sealed class AreaBarLightsTests
{
    [TestMethod]
    public void The_two_art_tables_are_the_originals()
    {
        CollectionAssert.AreEqual(new[] { 169, 187, 193, 194, 186 }, AreaBarLights.IdleArt.ToArray(), "0x5C6524: Character, Logbook, town map, world map, Inventory");
        CollectionAssert.AreEqual(new[] { 561, 560, 558, 195, 559 }, AreaBarLights.LitArt.ToArray(), "0x5C6510");
        CollectionAssert.AreEquivalent(new[] { 169, 187, 193, 194, 186, 561, 560, 558, 195, 559 }, AreaBarLights.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void The_buttons_start_idle_and_the_map_button_with_the_town_art()
    {
        var lights = new AreaBarLights();
        Assert.AreEqual(169, lights.ArtOf(AreaBarButton.Character));
        Assert.AreEqual(187, lights.ArtOf(AreaBarButton.Logbook));
        Assert.AreEqual(186, lights.ArtOf(AreaBarButton.Inventory));
        Assert.AreEqual(193, lights.ArtOf(AreaBarButton.Map), "the mode starts at 2 (0x549B70)");
        Assert.IsNull(lights.ArtOf(AreaBarButton.Sleep), "a button the lights do not touch keeps its layout art");
        Assert.IsNull(lights.ArtOf(AreaBarButton.Combat));
    }

    [TestMethod]
    public void A_light_shows_the_lit_art_unless_the_slots_window_is_open_and_the_flag_clear_always_puts_it_out()
    {
        var lights = new AreaBarLights();
        Assert.IsFalse(lights.Light(AreaLightSlot.Character, true, windowOpen: true), "the sheet is open: nothing is stored (0x55A220)");
        Assert.AreEqual(169, lights.ArtOf(AreaBarButton.Character));
        Assert.IsTrue(lights.Light(AreaLightSlot.Character, true, windowOpen: false));
        Assert.AreEqual(561, lights.ArtOf(AreaBarButton.Character), "Char_ON");
        Assert.IsFalse(lights.Light(AreaLightSlot.Character, true, windowOpen: false), "lit again: no change");
        Assert.IsTrue(lights.Light(AreaLightSlot.Character, false, windowOpen: true), "the flag clear stores the idle art whatever is open");
        Assert.AreEqual(169, lights.ArtOf(AreaBarButton.Character));
        Assert.IsFalse(lights.Light(AreaLightSlot.Character, false, windowOpen: false), "idle already");
        foreach (var (slot, button, lit) in new[] { (AreaLightSlot.Logbook, AreaBarButton.Logbook, 560), (AreaLightSlot.Inventory, AreaBarButton.Inventory, 559) })
        {
            Assert.IsTrue(lights.Light(slot, true, false));
            Assert.AreEqual(lit, lights.ArtOf(button), slot.ToString());
            Assert.IsTrue(lights.IsLit(slot));
        }
    }

    [TestMethod]
    public void The_map_button_draws_the_slot_its_mode_names_and_a_light_waits_for_its_mode()
    {
        var lights = new AreaBarLights();
        Assert.IsTrue(lights.Light(AreaLightSlot.WorldMap, true, false), "a new area lights the world map slot (0x4CAFD0) whatever the mode");
        Assert.AreEqual(193, lights.ArtOf(AreaBarButton.Map), "the town mode draws slot 2 (0x556B90 case 2), still idle");
        Assert.IsTrue(lights.SetMapMode(AreaLightSlot.WorldMap), "0x556B70(3)");
        Assert.AreEqual(195, lights.ArtOf(AreaBarButton.Map), "the world mode draws the lit world slot");
        Assert.IsTrue(lights.Light(AreaLightSlot.WorldMap, false, true), "opening the world map puts it out (0x5607E0)");
        Assert.AreEqual(194, lights.ArtOf(AreaBarButton.Map), "the idle world map art");
        Assert.IsFalse(lights.SetMapMode(AreaLightSlot.WorldMap), "unchanged");
        Assert.IsTrue(lights.SetMapMode(AreaLightSlot.TownMap));
        Assert.AreEqual(193, lights.ArtOf(AreaBarButton.Map));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => lights.SetMapMode(AreaLightSlot.Character));
    }

    [TestMethod]
    public void The_bar_keeps_the_lights_when_the_pointer_state_resets_and_reset_restores_the_start()
    {
        var bar = new AreaGameBar();
        bar.Lights.Light(AreaLightSlot.Logbook, true, false);
        bar.Lights.SetMapMode(AreaLightSlot.WorldMap);
        bar.Reset();
        Assert.IsTrue(bar.Lights.IsLit(AreaLightSlot.Logbook), "the pointer reset leaves the lights");
        Assert.IsTrue(bar.Lights.Reset());
        Assert.IsFalse(bar.Lights.IsLit(AreaLightSlot.Logbook));
        Assert.AreEqual(AreaLightSlot.TownMap, bar.Lights.MapMode);
        Assert.IsFalse(bar.Lights.Reset(), "nothing left to reset");
    }
}
