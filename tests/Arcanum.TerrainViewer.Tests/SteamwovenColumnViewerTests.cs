using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2890: the viewer's column mode, as plain numbers: the window hook and the dimmed world (the Godot side draws these rectangles). ARC-UI-3143: the wood of the gaps and the end caps are retired with the generated chrome.</summary>
[TestClass]
public sealed class SteamwovenColumnViewerTests
{
    private static readonly (int W, int H)[] Sizes = [(3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (1920, 1200), (3440, 1440), (1280, 800), (1366, 768)];

    private static SteamwovenHudPlan Plan(int w, int h, SteamwovenLayoutOptions options) => SteamwovenHud.Plan(w, h, options)!;

    [TestMethod]
    public void The_hud_plan_of_a_column_has_the_strips_scale_and_the_column_s_zone()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, SteamwovenLayoutOptions.ColumnFitted })
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, options);
            var column = plan.Column;
            Assert.AreEqual(column.UnitScale, plan.StripScale, 1e-9, $"{w}x{h}: the strips' scale is the column's unit scale");
            Assert.AreEqual(column.Column.X, plan.StripLeft, $"{w}x{h}: the strip stands in the column");
            Assert.AreEqual(column.Window.Zone, plan.WindowZone);
            Assert.AreEqual(plan.Layout.Middle.Y, column.TopStrip.Bottom, "the middle band starts under the top strip");
        }
    }

    [TestMethod]
    public void A_window_stands_in_the_column_zone_at_the_column_s_unit_scale()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, SteamwovenLayoutOptions.ColumnFitted })
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, options);
            var zone = plan.Column.Window.Zone;
            var body = SteamwovenWindowZone.Body(plan, 800, 400);
            var label = $"{w}x{h} k={options.Column.K} fit={options.Column.FitHeight}";
            Assert.AreEqual(plan.Column.UnitScale, body.Scale, 1e-9, label);
            // An 800 x 400 window fills the zone (within a pixel: the zone's edges are rounded from their own exact position).
            Assert.AreEqual(zone.X, body.X, 1.0, label);
            Assert.AreEqual(zone.Y, body.Y, 1.0, label);
            Assert.IsTrue(body.X >= plan.Column.Column.X - 1 && body.X + 800 * body.Scale <= plan.Column.Column.Right + 1, $"{label}: inside the column");
            // A screen of 800 x 600: its body (41 units down) lies in the zone where the frame allows, it is centred in the column and stays inside the frame.
            var screen = SteamwovenWindowZone.Screen(plan, 41);
            Assert.AreEqual(zone.X, screen.X, 1.0, label);
            Assert.IsTrue(screen.Y >= plan.Layout.Frame.Y - 1 && screen.Y + 600 * screen.Scale <= plan.Layout.Frame.Bottom + 1 || 600 * screen.Scale > plan.Layout.Frame.Height, label);
        }
    }

    [TestMethod]
    public void The_dimmed_world_is_the_frame_left_and_right_of_the_column()
    {
        foreach (var (w, h) in Sizes)
        {
            var plan = Plan(w, h, SteamwovenLayoutOptions.ColumnOriginal);
            var column = plan.Column;
            var world = SteamwovenColumnEdges.DimmedWorld(plan);
            Assert.AreEqual(2, world.Count, $"{w}x{h}");
            Assert.AreEqual(column.WorldLeft, world[0]);
            Assert.AreEqual(column.WorldRight, world[1]);
            // With the column and the world rectangles together the frame's width is covered.
            Assert.AreEqual(plan.Layout.Frame.Width, world[0].Width + column.Column.Width + world[1].Width, $"{w}x{h}");
        }
    }

    [TestMethod]
    public void The_dimming_values_are_options_with_a_default_and_a_range()
    {
        Assert.IsTrue(SteamwovenWorldDim.Default.IsValid);
        Assert.IsFalse(new SteamwovenWorldDim(1.2, 0).IsValid);
        Assert.IsFalse(new SteamwovenWorldDim(0, -0.1).IsValid);
        var options = SteamwovenLayoutOptions.ForColumn(SteamwovenColumnMode.KOriginal);
        Assert.IsTrue(options.IsValid);
        var changed = options with { Column = options.Column with { WorldDim = new SteamwovenWorldDim(0.6, 0.2) } };
        Assert.IsTrue(changed.IsValid);
        Assert.AreEqual(0.6, SteamwovenHud.Plan(1920, 1080, changed)!.Column.Mode.WorldDim.Darken);
        Assert.IsFalse((options with { Column = options.Column with { WorldDim = new SteamwovenWorldDim(2, 0) } }).IsValid);
    }
}
