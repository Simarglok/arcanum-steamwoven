using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1363: the sleep and wait window in the original's look (ledger SZ13, SWB6, SWB7): the layout and the push buttons.</summary>
[TestClass]
public sealed class AreaSleepWindowTests
{
    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        Assert.AreEqual((565, 293, 229), (AreaSleepLayout.BackgroundArt, AreaSleepLayout.ButtonArt, AreaSleepLayout.FontArt), "SWB6 arts");
        Assert.AreEqual((573, 42), (AreaSleepLayout.ScreenX, AreaSleepLayout.ScreenY), "SZ13 the window's place");
        Assert.AreEqual((8, 3), AreaSleepLayout.ButtonAt(0), "SWB7 the first button (0x5CB2C0)");
        Assert.AreEqual((8, 3 + 18 * 7), AreaSleepLayout.ButtonAt(7), "SWB7 the eighth button (0x5CB330)");
        Assert.AreEqual((36, 2, 164, 18), AreaSleepLayout.TextField(0, 200), "the first line's rectangle: width less 36, 18 high (0x57B4F0)");
        Assert.AreEqual((36, 2 + 18 * 7, 164, 18), AreaSleepLayout.TextField(7, 200));
        Assert.AreEqual(0, AreaSleepLayout.TextField(0, 20).Width, "a narrow window has no negative width");
        CollectionAssert.AreEqual(new[] { 565, 293, 229 }, AreaSleepLayout.ArtNumbers.ToArray());
        Assert.AreEqual((255, 255, 255), AreaSleepLayout.TextColour, "the font style takes entry 255 of each colour table (0x57B080)");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSleepLayout.ButtonAt(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSleepLayout.TextField(-1, 200));
        for (var row = 1; row < 8; row++)
            Assert.AreEqual(18, AreaSleepLayout.ButtonAt(row).Y - AreaSleepLayout.ButtonAt(row - 1).Y, $"row {row}");
    }

    [TestMethod]
    public void A_point_in_a_button_finds_its_row()
    {
        Assert.AreEqual(0, AreaSleepLayout.RowAt(8, 3, 18, 18, 8), "the top left of the first button");
        Assert.AreEqual(7, AreaSleepLayout.RowAt(25, 3 + 18 * 7 + 17, 18, 18, 8), "the bottom right of the last");
        Assert.IsNull(AreaSleepLayout.RowAt(7, 4, 18, 18, 8), "left of the buttons");
        Assert.IsNull(AreaSleepLayout.RowAt(10, 3 + 18 * 7, 18, 18, 7), "the wait window has seven rows: no eighth button");
        Assert.IsNull(AreaSleepLayout.RowAt(10, 4, 0, 18, 8), "an art that did not read has no buttons");
    }

    [TestMethod]
    public void Content_without_a_game_folder_is_not_ready()
    {
        var empty = AreaSleepContent.Empty("no game folder");
        Assert.IsFalse(empty.Ready);
        Assert.AreEqual("no game folder", empty.Detail);
    }

    [TestMethod]
    public void A_press_goes_down_and_the_release_over_it_acts()
    {
        var buttons = new AreaSleepButtons();
        buttons.Enter(2);
        Assert.AreEqual(AreaBarButtonLook.Hovered, buttons.Look(2), "the pointer lights the button");
        Assert.AreEqual(AreaBarButtonLook.Idle, buttons.Look(3));
        var press = buttons.Press(2);
        CollectionAssert.AreEqual(new[] { AreaGameBarLayout.PressSound }, press.Sounds.ToArray(), "GB6 press sound 3000");
        Assert.IsNull(press.Act, "a press does not act yet");
        Assert.AreEqual(AreaBarButtonLook.Down, buttons.Look(2));
        var release = buttons.Release(2);
        CollectionAssert.AreEqual(new[] { AreaGameBarLayout.ReleaseSound }, release.Sounds.ToArray(), "GB6 release sound 3001");
        Assert.AreEqual(2, release.Act);
        Assert.IsNull(buttons.Held);
    }

    [TestMethod]
    public void A_release_elsewhere_only_lets_the_button_go()
    {
        var buttons = new AreaSleepButtons();
        buttons.Press(1);
        buttons.Exit(1);
        Assert.AreEqual(AreaBarButtonLook.Idle, buttons.Look(1), "a held button shows idle away from the pointer");
        buttons.Enter(4);
        Assert.IsNull(buttons.Hovered, "another button takes no pointer while one is held");
        var release = buttons.Release(4);
        Assert.IsNull(release.Act, "the release over another button acts on neither");
        Assert.AreEqual(0, release.Sounds.Count);
        Assert.AreSame(AreaSleepEffect.None, buttons.Release(1), "a release with nothing held does nothing");
    }

    [TestMethod]
    public void The_controllers_focus_lights_a_row_and_the_pointer_wins()
    {
        var buttons = new AreaSleepButtons { Focus = 3 };
        Assert.AreEqual(AreaBarButtonLook.Hovered, buttons.Look(3));
        buttons.Enter(5);
        Assert.AreEqual(AreaBarButtonLook.Idle, buttons.Look(3), "with the pointer on a button the focus row is not lit");
        Assert.AreEqual(AreaBarButtonLook.Hovered, buttons.Look(5));
        buttons.Reset();
        Assert.IsNull(buttons.Focus);
        Assert.IsNull(buttons.Hovered);
    }
}
