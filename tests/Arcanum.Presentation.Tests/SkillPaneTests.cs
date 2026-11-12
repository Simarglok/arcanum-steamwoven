using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-1113: the skill pane rules against the behaviour recorded in docs/research/ledger/spell-runs.md SKP1–SKP6 (the background, the
/// four buttons' places and arts); values from the ledger, expected results worked by hand.</summary>
[TestClass]
public sealed class SkillPaneTests
{
    [TestMethod]
    public void The_background_is_the_modes_art_at_the_spell_panes_place()
    {
        Assert.AreEqual((274, 196, 492), (SkillPaneRules.BackgroundArt, SkillPaneRules.BackgroundX, SkillPaneRules.BackgroundY));
        Assert.AreEqual((SpellPaneRules.BackgroundX, SpellPaneRules.BackgroundY), (SkillPaneRules.BackgroundX, SkillPaneRules.BackgroundY), "every mode's background is drawn at (196, 492)");
        CollectionAssert.AreEqual(new[] { 2 }, new[] { SkillPaneRules.BarMode });
        CollectionAssert.AreEqual(new[] { 274 }, SkillPaneRules.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void The_four_buttons_stand_in_one_row_63_pixels_apart()
    {
        CollectionAssert.AreEqual(new[] { 4 }, new[] { SkillPaneRules.Buttons });
        CollectionAssert.AreEqual(new[] { 288, 351, 414, 477 }, SkillPaneRules.ButtonX.ToArray());
        CollectionAssert.AreEqual(new[] { 514 }, new[] { SkillPaneRules.ButtonY });
        Assert.IsTrue(SkillPaneRules.ButtonX.Zip(SkillPaneRules.ButtonX.Skip(1)).All(pair => pair.Second - pair.First == 63));
    }

    [TestMethod]
    public void A_button_draws_the_hotkey_bars_skill_icon()
    {
        Assert.AreEqual(279, SkillPaneRules.ButtonArt(0), "prowling");
        Assert.AreEqual(280, SkillPaneRules.ButtonArt(1), "pick pocket");
        Assert.AreEqual(282, SkillPaneRules.ButtonArt(2), "disarm traps");
        Assert.AreEqual(278, SkillPaneRules.ButtonArt(3), "repair");
        for (var button = 0; button < SkillPaneRules.Buttons; button++) Assert.AreEqual(HotkeyBarRules.SkillIcon(button), SkillPaneRules.ButtonArt(button));
        Assert.IsNull(SkillPaneRules.ButtonArt(4));
        Assert.IsNull(SkillPaneRules.ButtonArt(-1));
        Assert.IsFalse(SkillPaneRules.ArtNumbers.Intersect(HotkeyBarRules.ArtNumbers).Any(), "the pane's own art is none of the hotkey bar's");
    }
}
