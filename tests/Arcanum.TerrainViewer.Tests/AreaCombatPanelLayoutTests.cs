using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-124 (ledger OH12): the combat panel of a hovered critter (0x554830) as the message window lays it out.</summary>
[TestClass]
public sealed class AreaCombatPanelLayoutTests
{
    private static AreaMessageEntry Window(AreaCombatPanel? panel) => new(AreaMessageKind.Object, "Rat") { Icon = 786, CombatPanel = panel };

    [TestMethod]
    public void The_icons_are_the_executables_table_in_its_order_with_its_bit_values()
    {
        // The masks 0x5C732C and the arts 0x5C734C: Pen_MSR, Pen_Range, Pen_Perception, Pen_Cover, Pen_Light, Pen_Injury, BlockedShot, Magic-Tech-Penalty.
        CollectionAssert.AreEqual(new[] { 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x20000, 0x40000 }, AreaCombatPanelLayout.Icons.Select(icon => icon.Flag).ToArray());
        CollectionAssert.AreEqual(new[] { 586, 588, 587, 583, 585, 584, 845, 846 }, AreaCombatPanelLayout.Icons.Select(icon => icon.Art).ToArray());
        Assert.AreEqual((0x60FC0, 0x20080), (AreaCombatPanelLayout.IconBits, AreaCombatPanelLayout.ImpossibleBits));
        Assert.AreEqual(AreaCombatPanelLayout.IconBits, AreaCombatPanelLayout.Icons.Aggregate(0, (all, icon) => all | icon.Flag));
        CollectionAssert.AreEqual(new[] { (210, 525), (248, 525), (210, 545), (248, 545), (210, 565), (248, 565) }, AreaCombatPanelLayout.Slots.ToArray());
        Assert.AreEqual((582, 230, 20), (AreaCombatPanelLayout.BackgroundArt, AreaCombatPanelLayout.NumberFontArt, AreaCombatPanelLayout.ToHitMessage));
    }

    [TestMethod]
    public void The_flag_word_is_the_reasons_bits_and_the_magick_tech_bit()
    {
        Assert.AreEqual(0, AreaCombatPanelLayout.Flags(new(75, SkillDifficultyReasons.None, false)));
        Assert.AreEqual(0x40 | 0x200 | 0x40000,
            AreaCombatPanelLayout.Flags(new(10, SkillDifficultyReasons.WeaponTooHeavy | SkillDifficultyReasons.Cover, true)));
        Assert.AreEqual(0x20080, AreaCombatPanelLayout.Flags(new(0, SkillDifficultyReasons.OutOfRange | SkillDifficultyReasons.BlockedLine, false)));
    }

    [TestMethod]
    public void Without_a_term_the_label_stands_above_the_number_and_the_bullseye_alone_is_drawn()
    {
        var panel = AreaCombatPanelLayout.Panel(new(75, SkillDifficultyReasons.None, false), "To Hit:");
        Assert.IsFalse(AreaCombatPanelLayout.ShowsIcons(panel.Flags));
        var texts = AreaCombatPanelLayout.Texts(panel);
        Assert.AreEqual(2, texts.Count);
        Assert.AreEqual(((215, 526, 64, 64), AreaMessageFont.Text, "To Hit:", true), (texts[0].Rect, texts[0].Font, texts[0].Text, texts[0].Centre));
        Assert.AreEqual(((215, 544, 64, 64), AreaMessageFont.Number, "75%", true), (texts[1].Rect, texts[1].Font, texts[1].Text, texts[1].Centre));
        CollectionAssert.AreEqual(new[] { new AreaMessagePlacedArt(582, 207, 498) }, AreaCombatPanelLayout.Arts(panel).ToArray());
    }

    [TestMethod]
    public void With_a_term_the_number_is_on_top_and_each_term_has_its_icon_in_the_next_place()
    {
        var panel = AreaCombatPanelLayout.Panel(new(41, SkillDifficultyReasons.DistancePenalty | SkillDifficultyReasons.Cover | SkillDifficultyReasons.Injury, true), "To Hit:");
        Assert.IsTrue(AreaCombatPanelLayout.ShowsIcons(panel.Flags));
        var texts = AreaCombatPanelLayout.Texts(panel);
        Assert.AreEqual(1, texts.Count, "no label with icons");
        Assert.AreEqual(((215, 507, 64, 64), AreaMessageFont.Number, "41%"), (texts[0].Rect, texts[0].Font, texts[0].Text));
        // Distance 587, cover 583, injury 584, magick or technology 846 in the table's order (0x100, 0x200, 0x800, 0x40000), in the first four places.
        CollectionAssert.AreEqual(new[]
        {
            new AreaMessagePlacedArt(582, 207, 498), new(587, 210, 525), new(583, 248, 525), new(584, 210, 545), new(846, 248, 545),
        }, AreaCombatPanelLayout.Arts(panel).ToArray());
    }

    [TestMethod]
    public void Only_the_first_six_terms_of_the_table_are_drawn()
    {
        var all = AreaCombatPanelLayout.PlacedIcons(0x60FC0);
        Assert.AreEqual(6, all.Count);
        CollectionAssert.AreEqual(new[] { 586, 588, 587, 583, 585, 584 }, all.Select(icon => icon.Art).ToArray());
        Assert.AreEqual((248, 565), (all[5].X, all[5].Y));
    }

    [TestMethod]
    public void A_term_alone_in_the_magick_bit_still_draws_icons_and_a_zero_chance_prints_zero()
    {
        var panel = AreaCombatPanelLayout.Panel(new(0, SkillDifficultyReasons.None, true), "To Hit:");
        Assert.IsTrue(AreaCombatPanelLayout.ShowsIcons(panel.Flags));
        Assert.AreEqual("0%", AreaCombatPanelLayout.Texts(panel)[0].Text);
        Assert.AreEqual("125%", AreaCombatPanelLayout.PercentText(125), "not clamped above");
        Assert.AreEqual("-3%", AreaCombatPanelLayout.PercentText(-3));
    }

    [TestMethod]
    public void The_object_window_with_a_panel_takes_its_place_of_the_image_and_keeps_the_name_and_the_second_line()
    {
        var panel = AreaCombatPanelLayout.Panel(new(75, SkillDifficultyReasons.None, false), "To Hit:");
        var entry = Window(panel) with { SecondLine = "Reaction: 50 (Neutral)", SecondValue = "Level: 3" };
        var draw = AreaMessageLayout.Draw(entry);
        Assert.IsNull(draw.Icon, "the panel takes the image's place");
        Assert.IsNotNull(draw.Arts);
        Assert.AreEqual(582, draw.Arts![0].Art);
        CollectionAssert.AreEqual(new[] { "Rat", "Reaction: 50 (Neutral)", "Level: 3", "To Hit:", "75%" }, draw.Texts.Select(text => text.Text).ToArray());
        // Without a panel the image stays and there are no further arts.
        var plain = AreaMessageLayout.Draw(Window(null));
        Assert.AreEqual(786, plain.Icon);
        Assert.IsNull(plain.Arts);
    }

    [TestMethod]
    public void The_panels_art_is_loaded_with_the_message_windows_art_and_the_project_has_a_label_text()
    {
        foreach (var art in AreaCombatPanelLayout.ArtNumbers) Assert.IsTrue(AreaMessageLayout.ArtNumbers.Contains(art), $"art {art}");
        Assert.AreEqual("To Hit:", AreaGameBarTexts.Project("test").Text(AreaCombatPanelLayout.ToHitMessage));
    }
}
