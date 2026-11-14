using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1888: the combat lamp panel's original art version (ledger CH1, CH2, CT5): places from the executable's tables and the
/// states' arts.</summary>
[TestClass]
public sealed class AreaCombatLampLayoutTests
{
    [TestMethod]
    public void The_panel_the_lamps_and_the_button_are_the_originals_tables()
    {
        Assert.AreEqual((294, 222, 412, 356, 30), (AreaCombatLampLayout.PanelArt, AreaCombatLampLayout.PanelX, AreaCombatLampLayout.PanelY, AreaCombatLampLayout.PanelWidth, AreaCombatLampLayout.PanelHeight), "CH1: window 0x5CAA18");
        Assert.AreEqual((297, 320, 9, 31, 15), (AreaCombatLampLayout.EndTurnArt, AreaCombatLampLayout.EndTurnX, AreaCombatLampLayout.EndTurnY, AreaCombatLampLayout.EndTurnWidth, AreaCombatLampLayout.EndTurnHeight), "CT5: 0x5CAA38");
        Assert.AreEqual(ActionPointRules.LampCount, AreaCombatLampLayout.LampX.Count);
        // 0x5CAA48 read as hexadecimal: 16 23 30 3D 4A | 5D 6A 77 84 91 | A4 B1 BE CB D8 | EB F8 105 112 11F.
        CollectionAssert.AreEqual(new[] { 0x16, 0x23, 0x30, 0x3D, 0x4A, 0x5D, 0x6A, 0x77, 0x84, 0x91, 0xA4, 0xB1, 0xBE, 0xCB, 0xD8, 0xEB, 0xF8, 0x105, 0x112, 0x11F }, AreaCombatLampLayout.LampX.ToArray());
        for (var i = 1; i < ActionPointRules.LampCount; i++)
            Assert.AreEqual(i % ActionPointRules.GroupSize == 0 ? 19 : ActionPointRules.LampPitch, AreaCombatLampLayout.LampX[i] - AreaCombatLampLayout.LampX[i - 1], $"lamp {i}: 13 inside a group, 19 between groups");
    }

    [TestMethod]
    public void Every_part_lies_inside_the_panel()
    {
        Assert.IsTrue(AreaCombatLampLayout.LampX.All(x => x >= 0 && x + AreaCombatLampLayout.LampWidth <= AreaCombatLampLayout.PanelWidth));
        int[] bottoms = [AreaCombatLampLayout.LampY + AreaCombatLampLayout.LampHeight, AreaCombatLampLayout.EndTurnY + AreaCombatLampLayout.EndTurnHeight];
        Assert.IsTrue(bottoms.All(bottom => bottom <= AreaCombatLampLayout.PanelHeight), "the lamps and the button end above the panel's bottom");
        int[] rights = [AreaCombatLampLayout.EndTurnX + AreaCombatLampLayout.EndTurnWidth];
        Assert.IsTrue(rights.All(right => right <= AreaCombatLampLayout.PanelWidth), "the button ends inside the panel's right edge");
        Assert.IsTrue(AreaCombatLampLayout.LampX[^1] + AreaCombatLampLayout.LampWidth <= AreaCombatLampLayout.EndTurnX, "the last lamp ends before the button");
    }

    [TestMethod]
    public void The_shares_draw_their_own_arts_and_an_unlit_lamp_none()
    {
        Assert.AreEqual(295, AreaCombatLampLayout.LampArt(ActionPointLamp.Left));
        Assert.AreEqual(296, AreaCombatLampLayout.LampArt(ActionPointLamp.Cost));
        Assert.AreEqual(315, AreaCombatLampLayout.LampArt(ActionPointLamp.Shortfall));
        Assert.IsNull(AreaCombatLampLayout.LampArt(ActionPointLamp.Off));
        CollectionAssert.AreEqual(new[] { 294, 295, 296, 315, 297 }, AreaCombatLampLayout.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaCombatLampLayout.ArtNumbers.ToArray(), AreaGameBarContent.ArtNumbers.ToArray(), "the panel's arts are read with the bars");
    }
}
