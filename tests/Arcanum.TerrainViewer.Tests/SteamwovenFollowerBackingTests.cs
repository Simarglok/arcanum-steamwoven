using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2925 (AC3): the brass plate behind the follower group's show/hide button.</summary>
[TestClass]
public sealed class SteamwovenFollowerBackingTests
{
    [TestMethod]
    public void The_plate_is_the_show_hide_button_with_margins_flush_with_the_left_edge()
    {
        // FP1: show/hide 67 x 13 at (0, 428): the plate is 70 x 17 at (0, 426).
        Assert.AreEqual((0f, 426f, 70f, 17f), SteamwovenFollowerBacking.Plate(AreaFollowerPanelRules.ToggleRect));
        // The hidden-interface place (587) moves the plate with the button.
        Assert.AreEqual((0f, 585f, 70f, 17f), SteamwovenFollowerBacking.Plate(AreaFollowerPanelRules.HiddenToggleRect));
    }

    [TestMethod]
    public void The_layers_nest_from_the_outline_to_the_body_and_run_off_the_screen_edge_on_the_left()
    {
        var layers = SteamwovenFollowerBacking.Layers(AreaFollowerPanelRules.ToggleRect);
        Assert.AreEqual(3, layers.Count);
        Assert.AreEqual((0f, 426f, 70f, 17f, 0), layers[0], "the outline is the plate");
        Assert.AreEqual((-2f, 426.5f, 71.5f, 16f, 1), layers[1]);
        Assert.AreEqual((-2f, 427f, 71f, 15f, 2), layers[2]);
        // Right edges step in by one tone width each; every layer encloses the button.
        Assert.AreEqual(70f, layers[0].X + layers[0].Width);
        Assert.AreEqual(69.5f, layers[1].X + layers[1].Width);
        Assert.AreEqual(69f, layers[2].X + layers[2].Width);
        foreach (var layer in layers)
            Assert.IsTrue(layer.Y <= 428 && layer.Y + layer.Height >= 441 && layer.X <= 0 && layer.X + layer.Width >= 67);
    }

    [TestMethod]
    public void The_plate_shows_in_the_column_mode_with_the_bars_drawn_only()
    {
        Assert.IsTrue(SteamwovenFollowerBacking.Shown(true, true));
        Assert.IsFalse(SteamwovenFollowerBacking.Shown(false, true), "the Original look and the full-width layout are unchanged");
        Assert.IsFalse(SteamwovenFollowerBacking.Shown(true, false));
    }
}
