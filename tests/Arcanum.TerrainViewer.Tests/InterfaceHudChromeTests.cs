using Arcanum.Data.Hd;
using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1821: the generated chrome of the HUD contents: the combat lamp panel, its end-turn button and the hotkey slot (synthetic frames, no game art).</summary>
[TestClass]
public sealed class InterfaceHudChromeTests
{
    private static ChromeRecipe Recipe(string file) => InterfaceChromeRecipes.All.Single(r => r.File == file);

    private static ChromeRecipe? Found(string file) => InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file, [1]), 0, 1, 1, new byte[4]));

    private static byte[] Opaque(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (70, 70, 76, 255);
        return rgba;
    }

    private static (int R, int G, int B, int A) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    private static bool Holds(ChromeSunk plate, double x, double y, double width, double height) =>
        x >= plate.X + 2 && y >= plate.Y + 2 && x + width <= plate.X + plate.Width - 2 && y + height <= plate.Y + plate.Height - 2;

    [TestMethod]
    public void The_lamp_panel_the_end_turn_button_and_the_hotkey_slot_have_recipes_and_the_pictures_have_none()
    {
        Assert.AreEqual((ChromeKind.Panel, ChromeMaterial.Slate, ChromeSides.All, ChromeHoles.None), (Recipe("ap_bar.art").Kind, Recipe("ap_bar.art").Base, Recipe("ap_bar.art").Rim, Recipe("ap_bar.art").Holes));
        Assert.AreEqual(ChromeKind.Button, Recipe("EndTurn_But.art").Kind);
        Assert.AreEqual((ChromeKind.Panel, ChromeMaterial.WoodDark), (Recipe("HKSlt1.ART").Kind, Recipe("HKSlt1.ART").Base));
        // Found by the art's name hash whatever its case.
        Assert.AreSame(Recipe("ap_bar.art"), Found("AP_BAR.ART"));
        Assert.AreSame(Recipe("EndTurn_But.art"), Found("endturn_but.art"));
        Assert.AreSame(Recipe("HKSlt1.ART"), Found("hkslt1.art"));
        // The lamps (state), the clock's sky, moon and needle (a scrolling picture), the light button (a state glyph) and the slot arts the bar never draws stay pictures.
        foreach (var file in new[] { "ap_green.art", "ap_orange.art", "ap_red.art", "clk_timestrip.art", "clk_moon1.art", "clk_moon8.art", "hktshoff.art", "hktshon.art", "hkslt0.art", "hkslt2.art", "hkslt9.art", "Pen_BullsEye.ART" })
            Assert.IsNull(Found(file), file);
    }

    [TestMethod]
    public void Every_part_of_the_lamp_panel_lies_inside_its_frame_and_a_channel_holds_every_lamp_and_a_seat_the_button()
    {
        var recipe = Recipe("ap_bar.art");
        foreach (var part in recipe.Parts)
        {
            var (x, y, w, h) = part switch
            {
                ChromeSunk p => ((double)p.X, p.Y, p.Width, p.Height),
                ChromeRivet p => (p.X - p.Radius, p.Y - p.Radius, 2 * p.Radius, 2 * p.Radius),
                _ => throw new AssertFailedException($"unexpected part {part}"),
            };
            // Inside the frame and clear of the slate rim (three units on every side).
            Assert.IsTrue(x >= 3 && y >= 3 && x + w <= AreaCombatLampLayout.PanelWidth - 3 && y + h <= AreaCombatLampLayout.PanelHeight - 3, $"{part.GetType().Name} at ({x}, {y}, {w} x {h}) lies inside the frame and clear of its rim");
        }
        var pits = recipe.Parts.OfType<ChromeSunk>().ToArray();
        Assert.AreEqual(ActionPointRules.LampCount / ActionPointRules.GroupSize + 1, pits.Length, "four channels and the button's seat");
        foreach (var x in AreaCombatLampLayout.LampX)
            Assert.IsTrue(pits.Any(pit => Holds(pit, x, AreaCombatLampLayout.LampY, AreaCombatLampLayout.LampWidth, AreaCombatLampLayout.LampHeight)), $"a channel holds the lamp at {x}");
        Assert.IsTrue(pits.Any(pit => Holds(pit, AreaCombatLampLayout.EndTurnX, AreaCombatLampLayout.EndTurnY, AreaCombatLampLayout.EndTurnWidth, AreaCombatLampLayout.EndTurnHeight)), "a seat holds the button");
        // One unlit bead for each lamp, centred on it, and the four rivets of the channels' ends; no pit touches another.
        var beads = recipe.Parts.OfType<ChromeRivet>().Where(r => r.Radius > 3).ToArray();
        Assert.AreEqual(ActionPointRules.LampCount, beads.Length);
        for (var i = 0; i < beads.Length; i++)
            Assert.AreEqual((AreaCombatLampLayout.LampX[i] + AreaCombatLampLayout.LampWidth / 2.0, AreaCombatLampLayout.LampY + AreaCombatLampLayout.LampHeight / 2.0), (beads[i].X, beads[i].Y), $"bead {i}");
        for (var a = 0; a < pits.Length; a++)
        for (var b = a + 1; b < pits.Length; b++)
            Assert.IsFalse(pits[a].X < pits[b].X + pits[b].Width && pits[b].X < pits[a].X + pits[a].Width && pits[a].Y < pits[b].Y + pits[b].Height && pits[b].Y < pits[a].Y + pits[a].Height, $"pits {a} and {b} do not meet");
    }

    [TestMethod]
    public void The_lamp_panel_and_the_slot_paint_in_bounds_deterministically_and_opaque_at_whole_and_fractional_scales_with_black_pits()
    {
        foreach (var (file, w, h) in new[] { ("ap_bar.art", AreaCombatLampLayout.PanelWidth, AreaCombatLampLayout.PanelHeight), ("HKSlt1.ART", 32, 32) })
        {
            var recipe = Recipe(file);
            var legacy = Opaque(w, h);
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.25 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque at {scale}");
                foreach (var pit in recipe.Parts.OfType<ChromeSunk>())
                {
                    // The corner of a pit between the beads (the seat's middle, a channel's end) is the pit fill, darker than any slate (the darkest sums to 68).
                    var (r, g, b, _) = Pixel(a, pw, (int)((pit.X + 3) * scale), (int)((pit.Y + pit.Height / 2.0) * scale));
                    Assert.IsTrue(r + g + b < 40, $"{file}: the pit at ({pit.X}, {pit.Y}) is black ({r}, {g}, {b}) at {scale}");
                }
            }
        }
    }

    [TestMethod]
    public void The_end_turn_button_keeps_its_silhouette_and_sinks_on_its_down_frame()
    {
        const int w = AreaCombatLampLayout.EndTurnWidth, h = AreaCombatLampLayout.EndTurnHeight;
        var legacy = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            var glyph = x >= 13 && x < 18 && y >= 6 && y < 9;
            var corner = (x < 3 || x >= w - 3) && (y < 3 || y >= h - 3); // a cut corner: the Legacy oval's outside
            (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = corner ? ((byte)0, (byte)0, (byte)0, (byte)0) : glyph ? ((byte)40, (byte)220, (byte)60, (byte)255) : ((byte)50, (byte)45, (byte)20, (byte)255);
        }
        var recipe = Recipe("EndTurn_But.art");
        foreach (var scale in new[] { 1.0, 2.0, 3.25 })
        {
            var idle = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
            var down = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 1, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
            Assert.AreEqual(pw * ph * 4, idle.Length);
            CollectionAssert.AreNotEqual(idle, down, $"down differs from idle at {scale}");
            var (_, g, _, a) = Pixel(idle, pw, pw / 2, ph / 2);
            Assert.AreEqual(255, a);
            Assert.AreEqual(0, Pixel(idle, pw, 0, 0).A, $"the cut corner stays clear at {scale}");
            Assert.AreEqual(0, Pixel(down, pw, pw - 1, ph - 1).A, $"the cut corner stays clear on the down frame at {scale}");
            Assert.IsTrue(g > 150, $"the glyph is kept at {scale}: green {g}");
        }
    }
}
