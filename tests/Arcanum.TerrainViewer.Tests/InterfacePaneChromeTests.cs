using Arcanum.Data.Hd;
using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1200: the generated chrome of the bottom strip's message window and its spell and skill panes, and of the Fate and Sleep windows (synthetic frames, no game art).</summary>
[TestClass]
public sealed class InterfacePaneChromeTests
{
    // The arts of the slice: the file name, the Legacy size and the art number of interface.mes (354 DialogueWindow, 8 spell_ro, 274 Skill_Rot, 292 Fate_Base, 565 Sleep_Base).
    private static readonly (string File, int Width, int Height, int Art)[] Panels =
        [("DialogueWindow.ART", 410, 107, 354), ("spell_ro.art", 410, 107, 8), ("Skill_Rot.ART", 410, 107, 274), ("Fate_Base.ART", 250, 237, 292), ("Sleep_Base.ART", 227, 152, 565)];

    private static ChromeRecipe Recipe(string file) => InterfaceChromeRecipes.All.Single(r => r.File == file);

    private static byte[] Opaque(int width, int height, params (int X, int Y, int Width, int Height)[] holes)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var hole = holes.Any(h => x >= h.X && x < h.X + h.Width && y >= h.Y && y < h.Y + h.Height);
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = hole ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)70, (byte)70, (byte)76, (byte)255);
        }
        return rgba;
    }

    private static (int R, int G, int B, int A) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    // The fill of a sunk plate: the plate less its two-unit frame.
    private static (int X0, int Y0, int X1, int Y1) Fill(ChromeSunk plate) => (plate.X + 2, plate.Y + 2, plate.X + plate.Width - 2, plate.Y + plate.Height - 2);

    private static bool Holds(IEnumerable<ChromeSunk> plates, int x, int y, int width, int height) =>
        plates.Select(Fill).Any(f => x >= f.X0 && y >= f.Y0 && x + width <= f.X1 && y + height <= f.Y1);

    private static IEnumerable<ChromeSunk> Sunk(string file) => Recipe(file).Parts.OfType<ChromeSunk>();

    [TestMethod]
    public void The_pane_fate_and_sleep_frames_have_recipes_and_the_arts_no_window_draws_have_none()
    {
        foreach (var (file, _, _, _) in Panels)
        {
            var recipe = Recipe(file);
            Assert.AreEqual(ChromeKind.Panel, recipe.Kind, file);
            Assert.AreEqual(ChromeMaterial.Slate, recipe.Base, file);
            Assert.AreEqual(ChromeHoles.None, recipe.Holes, file);
            // The recipes are found by the art's name hash whatever its case.
            Assert.AreSame(recipe, InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file.ToUpperInvariant(), [1, 2, 3]), 0, 1, 1, new byte[4])), file);
        }
        Assert.AreEqual(ChromeKind.Button, Recipe("FateBut.ART").Kind);
        // The Fate and Sleep windows share the button art 293; the other modes' panes (7 mess_rot, 128 skill_ro, 200 MpWt_Rot, 288 Combine_Rot, 290 Trap_Rot, 291 Broadcast_Rot), the multiplayer
        // move window (298, since ARC-UI-1884 the quantity pane, whose recipe is ARC-UI-1818's), the old sleep window (218 Slp_Main) are drawn by no window of this slice (the dialog box, 822 to 824, is ARC-UI-1819's).
        foreach (var file in new[] { "mess_rot.art", "skill_ro.art", "MpWt_Rot.ART", "Combine_Rot.ART", "Trap_Rot.ART", "Broadcast_Rot.art", "Slp_Main.ART" })
            Assert.IsNull(InterfaceChromeRecipes.For(HdInterfaceKey.Query(HdInterfaceKey.Identity(file, [1]), 0, 1, 1, new byte[4])), file);
    }

    [TestMethod]
    public void Every_part_of_a_pane_recipe_lies_inside_its_frame_and_no_kept_picture_cuts_into_a_generated_box()
    {
        foreach (var (file, w, h, _) in Panels)
        {
            var parts = Recipe(file).Parts;
            Assert.IsTrue(parts.Count > 0, file);
            foreach (var part in parts)
            {
                var (x, y, pw, ph) = part switch
                {
                    ChromeSunk p => (p.X, p.Y, p.Width, p.Height),
                    ChromeKeep p => (p.X, p.Y, p.Width, p.Height),
                    ChromeRivet p => ((int)Math.Floor(p.X - p.Radius), (int)Math.Floor(p.Y - p.Radius), (int)Math.Ceiling(2 * p.Radius), (int)Math.Ceiling(2 * p.Radius)),
                    _ => throw new AssertFailedException($"{file}: unexpected part {part}"),
                };
                Assert.IsTrue(x >= 0 && y >= 0 && x + pw <= w && y + ph <= h, $"{file}: {part.GetType().Name} at ({x}, {y}, {pw} x {ph}) lies inside {w} x {h}");
            }
            foreach (var keep in parts.OfType<ChromeKeep>())
            foreach (var sunk in parts.OfType<ChromeSunk>())
                Assert.IsFalse(keep.X < sunk.X + sunk.Width && sunk.X < keep.X + keep.Width && keep.Y < sunk.Y + sunk.Height && sunk.Y < keep.Y + keep.Height, $"{file}: a kept picture at ({keep.X}, {keep.Y}) cuts into a pit");
        }
    }

    [TestMethod]
    public void The_message_window_and_the_name_plaques_hold_the_text_areas_of_their_modes()
    {
        var (mx, my, mw, mh) = AreaMessageLayout.Window;
        Assert.IsTrue(Holds(Sunk("DialogueWindow.ART"), mx - AreaMessageLayout.BackgroundX, my - AreaMessageLayout.BackgroundY, mw, mh), "the message window's text area lies in its plate");
        // The pane's hover name is written into the mode's text area: mode 1 the spells', mode 2 the skills'.
        foreach (var (file, mode) in new[] { ("spell_ro.art", SpellPaneRules.BarMode), ("Skill_Rot.ART", SkillPaneRules.BarMode) })
        {
            var (x, y, w, hh) = AreaMessageLayout.PaneText(mode);
            Assert.IsTrue(Holds(Sunk(file), x - AreaMessageLayout.BackgroundX, y - AreaMessageLayout.BackgroundY, w, hh), $"{file}: the name area lies in the plaque");
        }
    }

    [TestMethod]
    public void The_spell_and_skill_panes_have_a_pit_under_every_icon_button_and_the_colleges_domes_stay_under_their_buttons()
    {
        var spells = Sunk("spell_ro.art").ToArray();
        foreach (var x in SpellPaneRules.SpellButtonX)
            Assert.IsTrue(Holds(spells, x - SpellPaneRules.BackgroundX, SpellPaneRules.SpellButtonY - SpellPaneRules.BackgroundY, SpellPaneRules.SpellButtonSize, SpellPaneRules.SpellButtonSize), $"a spell button at {x}");
        var skills = Sunk("Skill_Rot.ART").ToArray();
        foreach (var x in SkillPaneRules.ButtonX)
            Assert.IsTrue(Holds(skills, x - SkillPaneRules.BackgroundX, SkillPaneRules.ButtonY - SkillPaneRules.BackgroundY, 32, 32), $"a skill button at {x}");
        // A college button (24 x 24) covers its dome (kept as the original's picture); a college the hero has no rank in shows the dome.
        var domes = Recipe("spell_ro.art").Parts.OfType<ChromeKeep>().ToArray();
        Assert.AreEqual(SpellPaneRules.Colleges, domes.Length);
        for (var college = 0; college < SpellPaneRules.Colleges; college++)
        {
            var bx = SpellPaneRules.CollegeButtonX[college] - SpellPaneRules.BackgroundX;
            var by = SpellPaneRules.CollegeButtonY - SpellPaneRules.BackgroundY;
            var dome = domes[college];
            Assert.IsTrue(dome.Round && dome.X >= bx && dome.Y >= by && dome.X + dome.Width <= bx + SpellPaneRules.CollegeButtonSize && dome.Y + dome.Height <= by + SpellPaneRules.CollegeButtonSize, $"dome {college}");
        }
    }

    [TestMethod]
    public void The_fate_and_sleep_windows_have_a_bar_behind_every_second_row_and_the_buttons_of_those_rows_sit_in_it()
    {
        var fate = Sunk("Fate_Base.ART").ToArray();
        Assert.AreEqual(AreaFateLayout.Kinds / 2, fate.Length);
        for (var kind = 0; kind < AreaFateLayout.Kinds; kind++)
        {
            var (bx, by) = AreaFateLayout.ButtonAt(kind);
            Assert.AreEqual(kind % 2 == 1, Holds(fate, bx, by, 18, 13), $"fate kind {kind}: the button sits in a bar exactly on the odd rows");
        }
        var sleep = Sunk("Sleep_Base.ART").ToArray();
        Assert.AreEqual(4, sleep.Length);
        for (var row = 0; row < 8; row++)
        {
            var (bx, by) = AreaSleepLayout.ButtonAt(row);
            Assert.AreEqual(row % 2 == 1, Holds(sleep, bx, by, 18, 13), $"sleep row {row}");
        }
    }

    [TestMethod]
    public void The_pane_frames_paint_in_bounds_deterministically_opaque_at_whole_and_fractional_scales_with_darker_pits_and_the_fate_button_holes_clear()
    {
        foreach (var (file, w, h, _) in Panels)
        {
            var recipe = Recipe(file);
            var holes = file == "Fate_Base.ART" ? Enumerable.Range(0, AreaFateLayout.Kinds).Select(kind => (AreaFateLayout.ButtonAt(kind).X, AreaFateLayout.ButtonAt(kind).Y, 18, 13)).ToArray() : [];
            var legacy = Opaque(w, h, holes);
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.25 })
            {
                var a = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, a.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(a, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} deterministic at {scale}");
                foreach (var sunk in recipe.Parts.OfType<ChromeSunk>())
                {
                    // The middle of a pit is the pit fill (nearly black), darker than any slate (whose darkest colour sums to 68).
                    var (r, g, b, _) = Pixel(a, pw, (int)((sunk.X + sunk.Width / 2.0) * scale), (int)((sunk.Y + sunk.Height / 2.0) * scale));
                    Assert.IsTrue(r + g + b < 40, $"{file}: the pit at ({sunk.X}, {sunk.Y}) is black ({r}, {g}, {b}) at {scale}");
                }
                if (holes.Length == 0)
                    for (var i = 3; i < a.Length; i += 4) Assert.AreEqual(255, a[i], $"{file} is opaque at {scale}");
                foreach (var (hx, hy, hw, hh) in holes)
                {
                    // A button hole stays transparent in its middle (the button is drawn over it) and gets no rim of its own (ChromeHoles.None).
                    Assert.AreEqual(0, Pixel(a, pw, (int)((hx + hw / 2.0) * scale), (int)((hy + hh / 2.0) * scale)).A, $"{file}: the hole at ({hx}, {hy}) is clear at {scale}");
                    Assert.AreEqual(255, Pixel(a, pw, (int)((hx - 1) * scale), (int)((hy + hh / 2.0) * scale)).A, $"{file}: beside the hole at ({hx}, {hy}) is opaque at {scale}");
                }
            }
        }
    }

    [TestMethod]
    public void The_fate_button_keeps_its_silhouette_and_sinks_on_its_down_frame()
    {
        var legacy = new byte[18 * 13 * 4];
        for (var y = 0; y < 13; y++)
        for (var x = 0; x < 18; x++)
        {
            var i = (y * 18 + x) * 4;
            var lamp = x >= 8 && x < 11 && y >= 5 && y < 8;
            (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = lamp ? ((byte)40, (byte)220, (byte)60, (byte)255) : ((byte)50, (byte)45, (byte)20, (byte)255);
        }
        var recipe = Recipe("FateBut.ART");
        foreach (var scale in new[] { 1.0, 2.0, 3.25 })
        {
            var idle = InterfaceChromeRenderer.Render(recipe, 18, 13, legacy, [], 0, scale);
            var down = InterfaceChromeRenderer.Render(recipe, 18, 13, legacy, [], 1, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(18, 13, scale);
            Assert.AreEqual(pw * ph * 4, idle.Length);
            CollectionAssert.AreNotEqual(idle, down, $"down differs from idle at {scale}");
            Assert.AreEqual(255, Pixel(idle, pw, pw / 2, ph / 2).A);
            // The lamp's green stays on the generated face (the glyph is kept), the corner is the rim.
            var (_, g, _, _) = Pixel(idle, pw, pw / 2, ph / 2);
            Assert.IsTrue(g > 150, $"the lamp is kept at {scale}: green {g}");
        }
    }
}
