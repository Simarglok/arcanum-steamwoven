using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-2304: the magick weapon pane rules against the behaviour recorded in docs/research/ledger/ui-audio.md BM6 and
/// docs/research/ledger/spell-runs.md IP1–IP10 (the background, the strip's button, the five slot places, the icons, the toggle); values
/// from the ledger, expected results worked by hand.</summary>
[TestClass]
public sealed class MagickWeaponPaneTests
{
    [TestMethod]
    public void The_background_is_mode_8s_art_at_every_modes_place()
    {
        Assert.AreEqual((564, 196, 492), (MagickWeaponPaneRules.BackgroundArt, MagickWeaponPaneRules.BackgroundX, MagickWeaponPaneRules.BackgroundY));
        Assert.AreEqual((SpellPaneRules.BackgroundX, SpellPaneRules.BackgroundY), (MagickWeaponPaneRules.BackgroundX, MagickWeaponPaneRules.BackgroundY));
        CollectionAssert.AreEqual(new[] { 8 }, new[] { MagickWeaponPaneRules.BarMode });
    }

    [TestMethod]
    public void The_weapon_button_stands_on_the_bottom_strip()
    {
        Assert.AreEqual((563, 161, 443), (MagickWeaponPaneRules.ButtonArt, MagickWeaponPaneRules.ButtonX, MagickWeaponPaneRules.ButtonY));
        CollectionAssert.AreEquivalent(new[] { 564, 563, 36 }, MagickWeaponPaneRules.ArtNumbers.ToArray());
        Assert.IsFalse(MagickWeaponPaneRules.ArtNumbers.Intersect(SpellPaneRules.ArtNumbers).Any(), "the pane's own arts are none of the spell pane's");
    }

    [TestMethod]
    public void The_five_slot_buttons_stand_in_one_row()
    {
        CollectionAssert.AreEqual(new[] { 353, 402, 451, 501, 550 }, MagickWeaponPaneRules.SlotButtonX.ToArray());
        CollectionAssert.AreEqual(new[] { 542 }, new[] { MagickWeaponPaneRules.SlotButtonY });
        Assert.AreEqual(5, MagickWeaponPaneRules.SlotButtonX.Count, "one place per item spell slot (spell fields 100..104)");
        Assert.IsTrue(MagickWeaponPaneRules.SlotButtonX.Zip(MagickWeaponPaneRules.SlotButtonX.Skip(1)).All(pair => pair.Second - pair.First is 49 or 50));
    }

    [TestMethod]
    public void A_slot_draws_its_spells_icon_and_another_spell_the_generic_one()
    {
        Assert.AreEqual(12, MagickWeaponPaneRules.SpellIcon(0, technological: false), "spell 0, conveyance rank 1");
        Assert.AreEqual(127, MagickWeaponPaneRules.SpellIcon(79, technological: false), "spell 79, temporal rank 5");
        for (var spell = 0; spell < 80; spell++) Assert.AreEqual(HotkeyBarRules.SpellIcon(spell), MagickWeaponPaneRules.SpellIcon(spell, technological: true));
        Assert.AreEqual(36, MagickWeaponPaneRules.SpellIcon(80, technological: false));
        Assert.AreEqual(35, MagickWeaponPaneRules.SpellIcon(80, technological: true));
        Assert.AreEqual(36, MagickWeaponPaneRules.SpellIcon(-1, technological: false));
    }

    [TestMethod]
    public void The_weapon_button_toggles_mode_8()
    {
        Assert.AreEqual(8, MagickWeaponPaneRules.ModeAfterButton(0));
        Assert.AreEqual(8, MagickWeaponPaneRules.ModeAfterButton(1), "from the spells mode the pane replaces it");
        Assert.AreEqual(8, MagickWeaponPaneRules.ModeAfterButton(2));
        Assert.AreEqual(0, MagickWeaponPaneRules.ModeAfterButton(8), "a second release gives the message mode");
    }
}
