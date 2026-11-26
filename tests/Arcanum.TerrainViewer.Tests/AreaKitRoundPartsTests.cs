using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-3101: the round parts the windows draw as separate arts over the kit's background: the table of their arts and rings, the cut and the shadow (the numbers the shader mirrors), on the table's own numbers (no original data).</summary>
[TestClass]
public sealed class AreaKitRoundPartsTests
{
    [TestMethod]
    public void The_table_holds_the_college_rings_and_the_round_tabs()
    {
        // The sixteen colleges, the four skill group buttons, the eight discipline buttons and the twelve small ringed buttons (ARC-UI-3347): nothing else, nothing twice (the lens rings are not round parts of the pages).
        var arts = AreaKitRoundParts.ArtNumbers;
        Assert.AreEqual(16 + 4 + 8 + 12, arts.Count);
        foreach (var ring in new[] { 145, 146, 147, 148, 299, 338, 339, 340, 757, 758, 823, 824 }) Assert.IsNotNull(AreaKitRoundParts.Of(ring), $"ringed button {ring}");
        // The character sheet's plus and minus stand in the sheet's sockets, not as round parts.
        foreach (var socket in new[] { 23, 24, 770, 771 }) Assert.IsNull(AreaKitRoundParts.Of(socket), $"sheet button {socket}");
        Assert.AreEqual(arts.Count, arts.Distinct().Count());
        foreach (var lens in new[] { 231, 232, 233, 198 }) Assert.IsNull(AreaKitRoundParts.Of(lens), $"lens ring {lens}: the surface carries the ring");
        foreach (var college in AreaCharacterSheetSpellsLayout.CollegeArtNumbers) Assert.IsNotNull(AreaKitRoundParts.Of(college), $"college ring {college}");
        for (var group = 0; group < AreaCharacterSheetPagesLayout.Groups; group++) Assert.IsNotNull(AreaKitRoundParts.Of(AreaCharacterSheetPagesLayout.GroupTabArtBase + group));
        for (var discipline = 0; discipline < AreaCharacterSheetPagesLayout.Disciplines; discipline++) Assert.IsNotNull(AreaKitRoundParts.Of(AreaCharacterSheetPagesLayout.DisciplineArtBase + discipline));
        Assert.IsNull(AreaKitRoundParts.Of(0));
        Assert.IsNull(AreaKitRoundParts.Of(AreaCharacterSheetSpellsLayout.BackgroundArt), "a window background is no round part");
    }

    [TestMethod]
    public void Every_ring_lies_inside_its_art_and_is_nearly_the_arts_own_circle()
    {
        foreach (var art in AreaKitRoundParts.ArtNumbers)
        {
            var part = AreaKitRoundParts.Of(art)!.Value;
            const float slack = 1.5f;
            Assert.IsTrue(part.Cx - part.CutRadius > -slack && part.Cx + part.CutRadius < part.Width + slack, $"art {art}: the cut circle stays in the art's width");
            Assert.IsTrue(part.Cy - part.CutRadius > -slack && part.Cy + part.CutRadius < part.Height + slack, $"art {art}: the cut circle stays in the art's height");
            Assert.AreEqual(part.Radius + AreaKitRoundParts.Trim, part.CutRadius, 1e-6f);
            Assert.IsTrue(Math.Abs(part.Cx - part.Width / 2f) < 2f && Math.Abs(part.Cy - part.Height / 2f) < 2f, $"art {art}: the ring is roughly centred");
        }
    }

    [TestMethod]
    public void The_cut_keeps_the_inside_and_leaves_the_corners_out_with_one_device_pixel_of_edge()
    {
        var lens = AreaKitRoundParts.Of(AreaCharacterSheetSpellsLayout.CollegeArt(0))!.Value;
        Assert.AreEqual(1f, AreaKitRoundParts.Coverage(lens, lens.Cx, lens.Cy, .3f), "the middle");
        Assert.AreEqual(1f, AreaKitRoundParts.Coverage(lens, lens.Cx + lens.Radius, lens.Cy, .3f), "the ring's gold");
        Assert.AreEqual(0f, AreaKitRoundParts.Coverage(lens, 0, 0, .3f), "the square's top left corner (old stone)");
        Assert.AreEqual(0f, AreaKitRoundParts.Coverage(lens, 23, 23, .3f), "the square's bottom right corner");
        Assert.AreEqual(.5f, AreaKitRoundParts.Coverage(lens, lens.Cx + lens.CutRadius, lens.Cy, .3f), 1e-4f, "half on the circle");
        Assert.AreEqual(1f, AreaKitRoundParts.Coverage(lens, lens.Cx + lens.CutRadius - .15f, lens.Cy, .3f), 1e-5f, "one half pixel inside");
        Assert.AreEqual(0f, AreaKitRoundParts.Coverage(lens, lens.Cx + lens.CutRadius + .15f, lens.Cy, .3f), 1e-5f, "one half pixel outside");
        // A finer device pixel narrows the edge; a coarser one widens it.
        Assert.IsTrue(AreaKitRoundParts.Coverage(lens, lens.Cx + lens.CutRadius + .1f, lens.Cy, 1f) > AreaKitRoundParts.Coverage(lens, lens.Cx + lens.CutRadius + .1f, lens.Cy, .1f));
    }

    [TestMethod]
    public void The_shadow_falls_down_and_right_outside_the_part_and_never_on_it()
    {
        var button = AreaKitRoundParts.Of(AreaCharacterSheetPagesLayout.DisciplineArtBase)!.Value;
        const float pixel = .3f;
        Assert.AreEqual(0f, AreaKitRoundParts.ShadowAt(button, button.Cx, button.Cy, pixel), 1e-6f, "none on the part");
        Assert.AreEqual(0f, AreaKitRoundParts.ShadowAt(button, button.Cx + button.Radius - 1, button.Cy, pixel), 1e-6f);
        var right = AreaKitRoundParts.ShadowAt(button, button.Cx + button.CutRadius + 2, button.Cy + 2, pixel);
        var left = AreaKitRoundParts.ShadowAt(button, button.Cx - button.CutRadius - 2, button.Cy - 2, pixel);
        var below = AreaKitRoundParts.ShadowAt(button, button.Cx + 2, button.Cy + button.CutRadius + 2, pixel);
        var above = AreaKitRoundParts.ShadowAt(button, button.Cx + 2, button.Cy - button.CutRadius - 2, pixel);
        Assert.IsTrue(right > left * 3 && below > above * 3, $"down and right is darker: right {right}, left {left}, below {below}, above {above}");
        Assert.IsTrue(right > .1f && right <= AreaKitRoundParts.ShadowStrength, "a visible shadow, never stronger than its strength");
        // Far from the part nothing is left; the node of a shadow reaches the offset and three sigma beyond the disc.
        Assert.IsTrue(AreaKitRoundParts.ShadowAt(button, button.Cx + button.CutRadius + AreaKitRoundParts.ShadowReach + 5, button.Cy, pixel) < .01f);
        Assert.AreEqual(13f / 3.6f + 3 * 16f / 3.6f, AreaKitRoundParts.ShadowReach, 1e-5f);
    }

    [TestMethod]
    public void A_socket_dome_stands_on_its_sockets_centre()
    {
        // ARC-UI-3347: the centres of the socket rings the screens draw, in the screen's own units (the HD pack's new character body 765, follower barter 825 and note pane 200).
        var sockets = new Dictionary<AreaNewCharacterArtButton, (float X, float Y)>
        {
            [AreaNewCharacterArtButton.GenderPrevious] = (52.97f, 291.04f), [AreaNewCharacterArtButton.GenderNext] = (265.96f, 291.2f),
            [AreaNewCharacterArtButton.RacePrevious] = (52.97f, 341.29f), [AreaNewCharacterArtButton.RaceNext] = (265.96f, 341.53f),
            [AreaNewCharacterArtButton.BackgroundPrevious] = (52.95f, 391.74f), [AreaNewCharacterArtButton.BackgroundNext] = (265.93f, 391.75f),
        };
        foreach (var (button, socket) in sockets)
        {
            var (art, x, y) = AreaNewCharacterArtLayout.Button(button);
            AssertCentre(art, x, y, socket);
        }
        AssertCentre(AreaBarterArtLayout.CycleLeftArt, AreaBarterArtLayout.CycleLeft.X, AreaBarterArtLayout.CycleLeft.Y, (165.97f, 66.1f));
        AssertCentre(AreaBarterArtLayout.CycleRightArt, AreaBarterArtLayout.CycleRight.X, AreaBarterArtLayout.CycleRight.Y, (264.08f, 66.01f));
        foreach (var marker in new[] { 145, 146, 147, 148 }) AssertCentre(marker, 10, 20, (21.61f, 31.18f));
        Assert.AreEqual((0f, 0f), AreaKitRoundParts.SocketShift(757, 10, 20), "no socket there: the table position");
        // Only the bare domes move and lose the shadow: a ringed button stays at its table position.
        foreach (var art in AreaKitRoundParts.ArtNumbers)
        {
            Assert.AreEqual(art is 145 or 146 or 147 or 148 or 757 or 758, AreaKitRoundParts.IsSocketDome(art), $"art {art}");
            if (!AreaKitRoundParts.IsSocketDome(art)) Assert.AreEqual((0f, 0f), AreaKitRoundParts.SocketShift(art, 42, 281), $"art {art}");
        }

        static void AssertCentre(int art, int x, int y, (float X, float Y) socket)
        {
            var part = AreaKitRoundParts.Of(art)!.Value;
            var (dx, dy) = AreaKitRoundParts.SocketShift(art, x, y);
            Assert.AreEqual(socket.X, x + dx + part.Cx, .005f, $"art {art} at {x},{y}: across");
            Assert.AreEqual(socket.Y, y + dy + part.Cy, .005f, $"art {art} at {x},{y}: down");
        }
    }

    [TestMethod]
    public void The_shader_carries_the_numbers_of_the_rules()
    {
        // The logistic slope of a Gaussian (1.702), the half-pixel edge and the shadow's complement of the cut are written in the shader as in the C# functions.
        StringAssert.Contains(AreaKitRoundParts.Shader, "1.702");
        StringAssert.Contains(AreaKitRoundParts.Shader, "0.5 - d / pixel");
        StringAssert.Contains(AreaKitRoundParts.Shader, "(1.0 - keep)");
        StringAssert.Contains(AreaKitRoundParts.Shader, "render_mode blend_premul_alpha");
        StringAssert.Contains(AreaKitRoundParts.Shader, "COLOR *= keep");
        StringAssert.Contains(AreaKitRoundParts.Shader, "COLOR.rgb *= COLOR.a");   // ARC-UI-3320: a kit face is straight alpha
    }
}
