using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2575: the one hook the menu windows take their place from: the window zone of the column plan at its unit scale (the fitted column: frame height / 600).</summary>
[TestClass]
public sealed class SteamwovenWindowZoneTests
{
    [TestMethod]
    [DataRow(3840, 2160, 3.6)]
    [DataRow(2560, 1440, 2.4)]
    [DataRow(1920, 1080, 1.8)]
    [DataRow(2560, 1600, 2.6667)]
    [DataRow(1366, 768, 1.2800)]
    [DataRow(3440, 1440, 2.4)]
    [DataRow(1024, 768, 1.28)]
    public void A_window_body_fills_the_zone_at_the_layout_unit_scale(int width, int height, double unit)
    {
        var plan = SteamwovenHud.Plan(width, height)!;
        var zone = plan.WindowZone;
        var place = SteamwovenWindowZone.Body(width, height, 800, 400)!.Value;
        Assert.AreEqual(unit, place.Scale, 0.01);
        Assert.AreEqual(plan.Layout.Scale.ToDouble() * 3.6, place.Scale, 1e-9);
        Assert.AreEqual(plan.Column.UnitScale, place.Scale, 1e-12);
        // The 800 x 400 body is the zone: its top left is the zone's (the zone is rounded to whole pixels, the body is not), and it ends within a pixel of the zone's edges.
        Assert.AreEqual(zone.X, place.X, 1.0);
        Assert.AreEqual(zone.Y, place.Y, 1.0);
        Assert.IsTrue(Math.Abs(place.X + 800 * place.Scale - zone.Right) <= 1.5, "right edge");
        Assert.IsTrue(Math.Abs(place.Y + 400 * place.Scale - zone.Bottom) <= 1.5, "bottom edge");
    }

    [TestMethod]
    public void A_window_lies_in_the_middle_band_and_inside_the_frame()
    {
        foreach (var (width, height) in new[] { (3840, 2160), (1920, 1080), (1920, 1200), (3440, 1440), (5120, 1440), (1024, 768), (1366, 768) })
        {
            var layout = SteamwovenHud.Plan(width, height)!.Layout;
            var place = SteamwovenWindowZone.Body(width, height, 800, 400)!.Value;
            var (left, top) = ((int)Math.Floor(place.X), (int)Math.Floor(place.Y));
            var (right, bottom) = ((int)Math.Ceiling(place.X + 800 * place.Scale), (int)Math.Ceiling(place.Y + 400 * place.Scale));
            Assert.IsTrue(left >= layout.Frame.X && right <= layout.Frame.Right, $"[{width}x{height}] the window is in the frame {layout.Frame}, never under a bar ({left}..{right})");
            Assert.IsTrue(top >= layout.Middle.Y && bottom <= layout.Middle.Bottom, $"[{width}x{height}] the window is in the middle band {layout.Middle} ({top}..{bottom})");
        }
    }

    [TestMethod]
    public void A_shorter_window_is_centred_in_the_zone()
    {
        // The reading window (399 units high) and any smaller one keep the zone's centre.
        var zone = SteamwovenHud.Plan(1920, 1080)!.WindowZone;
        var place = SteamwovenWindowZone.Body(1920, 1080, 800, 300)!.Value;
        Assert.AreEqual(zone.Y + zone.Height / 2.0, place.Y + 150 * place.Scale, 1.0);
        Assert.AreEqual(zone.X + zone.Width / 2.0, place.X + 400 * place.Scale, 1.0);
    }

    [TestMethod]
    public void The_pointer_maps_through_the_drawn_window_at_the_new_scale()
    {
        // The coordinate mapping a Godot view does for its children: window unit = (pointer - origin) / scale. The window's centre pixel is its unit (400, 200); a corner is the corner.
        foreach (var (width, height) in new[] { (1920, 1080), (3440, 1440), (1024, 768) })
        {
            var place = SteamwovenWindowZone.Body(width, height, 800, 400)!.Value;
            var centre = (X: place.X + 400 * place.Scale, Y: place.Y + 200 * place.Scale);
            Assert.AreEqual(400, (centre.X - place.X) / place.Scale, 1e-9);
            Assert.AreEqual(200, (centre.Y - place.Y) / place.Scale, 1e-9);
            var plan = SteamwovenHud.Plan(width, height)!;
            var layout = plan.Layout;
            Assert.AreEqual(plan.WindowZone.X + plan.WindowZone.Width / 2.0, centre.X, 1.0, $"[{width}x{height}] the window is centred on the column");
            Assert.AreEqual(layout.Frame.X + layout.Frame.Width / 2.0, centre.X, 1.0);
        }
    }

    [TestMethod]
    public void A_screen_puts_its_body_in_the_zone_and_stays_in_the_frame()
    {
        // In the fitted column the 600-unit screen is as high as the frame at 16:10 and at 16:9: it fills it, and its 800 x 400 body (at 41) lies in the zone (the zone is the middle band).
        foreach (var (width, height) in new[] { (1920, 1200), (1920, 1080), (3440, 1440) })
        {
            var plan = SteamwovenHud.Plan(width, height)!;
            var screen = SteamwovenWindowZone.Screen(width, height, 41)!.Value;
            var label = $"{width}x{height}";
            Assert.AreEqual(plan.WindowZone.Y, screen.Y + 41 * screen.Scale, 1.0, label);
            Assert.AreEqual(plan.WindowZone.X, screen.X, 1.0, label);
            Assert.AreEqual(plan.Layout.Frame.Height, 600 * screen.Scale, 1.0, label);
            Assert.IsTrue(screen.Y >= plan.Layout.Frame.Y - 1 && screen.Y + 600 * screen.Scale <= plan.Layout.Frame.Bottom + 1, label);
            // A screen is never wider than the frame.
            Assert.IsTrue(screen.X >= plan.Layout.Frame.X && screen.X + 800 * screen.Scale <= plan.Layout.Frame.Right + 1, label);
        }
    }

    [TestMethod]
    public void No_place_where_the_layout_is_not_supported()
    {
        Assert.IsNull(SteamwovenWindowZone.Body(800, 450, 800, 400));
        Assert.IsNull(SteamwovenWindowZone.Screen(800, 450, 41));
    }
}
