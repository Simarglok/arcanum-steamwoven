using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-3101: the round parts the windows draw as separate arts over the kit's background: the table of their arts and rings, the cut and the shadow (the numbers the shader mirrors), on the table's own numbers (no original data).</summary>
[TestClass]
public sealed class AreaKitRoundPartsTests
{
    [TestMethod]
    public void The_table_holds_the_lens_rings_the_college_rings_and_the_round_tabs()
    {
        // The four lens rings, the sixteen colleges, the four skill group buttons and the eight discipline buttons: nothing else, nothing twice.
        var arts = AreaKitRoundParts.ArtNumbers;
        Assert.AreEqual(4 + 16 + 4 + 8, arts.Count);
        Assert.AreEqual(arts.Count, arts.Distinct().Count());
        foreach (var lens in new[] { AreaInventoryHole.BarterArt, AreaInventoryHole.LootArt, AreaInventoryHole.InventoryArt, AreaCharacterSheetArtLayout.LensArt, AreaLogbookArtLayout.LensArt })
            Assert.IsNotNull(AreaKitRoundParts.Of(lens), $"lens ring {lens}");
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
            // A button's ring lies in its art; a lens ring is wider than its 89 x 89 square (the square clips it at the four edges' middles, as in the original).
            var slack = part.Width == 89 ? 6f : 1.5f;
            Assert.IsTrue(part.Cx - part.CutRadius > -slack && part.Cx + part.CutRadius < part.Width + slack, $"art {art}: the cut circle stays in the art's width");
            Assert.IsTrue(part.Cy - part.CutRadius > -slack && part.Cy + part.CutRadius < part.Height + slack, $"art {art}: the cut circle stays in the art's height");
            Assert.AreEqual(part.Radius + AreaKitRoundParts.Trim, part.CutRadius, 1e-6f);
            Assert.IsTrue(Math.Abs(part.Cx - part.Width / 2f) < 2f && Math.Abs(part.Cy - part.Height / 2f) < 2f, $"art {art}: the ring is roughly centred");
        }
    }

    [TestMethod]
    public void The_cut_keeps_the_inside_and_leaves_the_corners_out_with_one_device_pixel_of_edge()
    {
        var lens = AreaKitRoundParts.Of(AreaInventoryHole.LootArt)!.Value;
        Assert.AreEqual(1f, AreaKitRoundParts.Coverage(lens, lens.Cx, lens.Cy, .3f), "the middle");
        Assert.AreEqual(1f, AreaKitRoundParts.Coverage(lens, lens.Cx + lens.Radius, lens.Cy, .3f), "the ring's gold");
        Assert.AreEqual(0f, AreaKitRoundParts.Coverage(lens, 1, 1, .3f), "the square's top left corner (old wood)");
        Assert.AreEqual(0f, AreaKitRoundParts.Coverage(lens, 88, 88, .3f), "the square's bottom right corner");
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
        Assert.AreEqual(0f, AreaKitRoundParts.ShadowAt(button, button.Cx, button.Cy, pixel), 1e-6f, "none on the part (a lens ring's hole shows the game view)");
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
    public void The_shader_carries_the_numbers_of_the_rules()
    {
        // The logistic slope of a Gaussian (1.702), the half-pixel edge and the shadow's complement of the cut are written in the shader as in the C# functions.
        StringAssert.Contains(AreaKitRoundParts.Shader, "1.702");
        StringAssert.Contains(AreaKitRoundParts.Shader, "0.5 - d / pixel");
        StringAssert.Contains(AreaKitRoundParts.Shader, "(1.0 - keep)");
        StringAssert.Contains(AreaKitRoundParts.Shader, "COLOR.a *= keep");
    }
}
