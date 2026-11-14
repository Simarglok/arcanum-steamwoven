using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1736: the follower panel's show/hide button, scroll arrows, offset and the order menu's geometry and row states (ledger FP1–FP13).</summary>
[TestClass]
public sealed class AreaFollowerPanelRulesTests
{
    [TestMethod]
    public void The_offset_stays_zero_up_to_six_followers_and_clamps_at_count_minus_six()
    {
        foreach (var count in new[] { 0, 1, 6 }) Assert.AreEqual(0, AreaFollowerPanelRules.ClampOffset(5, count), $"FP3: {count} followers do not scroll");
        Assert.AreEqual(1, AreaFollowerPanelRules.ClampOffset(5, 7), "FP3: at most count - 6");
        Assert.AreEqual(3, AreaFollowerPanelRules.ClampOffset(3, 9));
        Assert.AreEqual(0, AreaFollowerPanelRules.ClampOffset(-2, 9));
    }

    [TestMethod]
    public void The_arrows_exist_only_with_the_panel_shown_and_seven_followers_and_the_ends_have_their_own_look()
    {
        Assert.IsFalse(AreaFollowerPanelRules.ArrowsShown(true, 6), "FP5");
        Assert.IsTrue(AreaFollowerPanelRules.ArrowsShown(true, 7));
        Assert.IsFalse(AreaFollowerPanelRules.ArrowsShown(false, 9), "hidden: no arrows");
        Assert.AreEqual(507, AreaFollowerPanelRules.UpArtAt(0), "up at offset 0 shows 507");
        Assert.AreEqual(506, AreaFollowerPanelRules.UpArtAt(1));
        Assert.AreEqual(508, AreaFollowerPanelRules.DownArtAt(0, 7), "down below the last offset shows 508");
        Assert.AreEqual(509, AreaFollowerPanelRules.DownArtAt(1, 7), "down at the last offset (count - 6) shows 509");
        Assert.AreEqual(501, AreaFollowerPanelRules.ToggleArt(true));
        Assert.AreEqual(502, AreaFollowerPanelRules.ToggleArt(false));
        Assert.IsFalse(AreaFollowerPanelRules.ToggleShown(0), "0x56B850: the button needs a follower");
        Assert.IsTrue(AreaFollowerPanelRules.ToggleShown(1));
    }

    [TestMethod]
    public void A_release_on_an_arrow_moves_the_offset_by_one_follower_and_stops_at_the_ends()
    {
        Assert.AreEqual(0, AreaFollowerPanelRules.ScrollUp(0, 8), "FP5: up is held at 0");
        Assert.AreEqual(1, AreaFollowerPanelRules.ScrollDown(0, 8));
        Assert.AreEqual(2, AreaFollowerPanelRules.ScrollDown(2, 8), "down is held at count - 6");
        Assert.AreEqual(1, AreaFollowerPanelRules.ScrollUp(2, 8));
    }

    [TestMethod]
    public void The_six_frames_show_offset_to_offset_plus_five_and_none_while_hidden()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, AreaFollowerPanelRules.VisibleIndexes(true, 0, 3).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, AreaFollowerPanelRules.VisibleIndexes(true, 1, 7).ToArray(), "FP3");
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, AreaFollowerPanelRules.VisibleIndexes(true, 9, 7).ToArray(), "a stale offset clamps");
        Assert.AreEqual(0, AreaFollowerPanelRules.VisibleIndexes(false, 0, 7).Count, "FP5: hiding hides the portrait windows");
        Assert.AreEqual(0, AreaFollowerPanelRules.VisibleIndexes(true, 0, 0).Count);
    }

    [TestMethod]
    public void The_buttons_and_the_menu_stand_where_the_original_puts_them()
    {
        Assert.AreEqual((0, 428, 67, 13), AreaFollowerPanelRules.ToggleRect, "FP1");
        Assert.AreEqual((0, 415, 33, 13), AreaFollowerPanelRules.UpRect);
        Assert.AreEqual((33, 415, 34, 13), AreaFollowerPanelRules.DownRect);
        Assert.AreEqual((51, 50), AreaFollowerPanelRules.MenuOrigin(0), "FP7: the slot's right edge, its y");
        Assert.AreEqual((51, 360), AreaFollowerPanelRules.MenuOrigin(5), "FP7: no clamp, the menu of slot 5 runs on");
        Assert.AreEqual((10, 3, 118, 15), AreaFollowerPanelRules.MenuRow(FollowerMenuItem.Walk), "FP8");
        Assert.AreEqual((10, 3 + 18 * 7, 118, 15), AreaFollowerPanelRules.MenuRow(FollowerMenuItem.CharacterSheet));
        CollectionAssert.AreEqual(new[] { 501, 502, 506, 507, 508, 509, 826 }, AreaFollowerPanelRules.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void With_the_interface_bars_hidden_the_buttons_stand_at_587_and_574_and_the_bars_drawn_places_are_kept()
    {
        Assert.AreEqual((0, 587, 67, 13), AreaFollowerPanelRules.ToggleRectFor(false), "FP1, SZ11: the show/hide button at y 587");
        Assert.AreEqual((0, 574, 33, 13), AreaFollowerPanelRules.UpRectFor(false), "the up arrow at y 574");
        Assert.AreEqual((33, 574, 34, 13), AreaFollowerPanelRules.DownRectFor(false), "the down arrow at y 574");
        Assert.AreEqual(AreaFollowerPanelRules.ToggleRect, AreaFollowerPanelRules.ToggleRectFor(true));
        Assert.AreEqual(AreaFollowerPanelRules.UpRect, AreaFollowerPanelRules.UpRectFor(true));
        Assert.AreEqual(AreaFollowerPanelRules.DownRect, AreaFollowerPanelRules.DownRectFor(true));
        Assert.AreEqual(600, AreaFollowerPanelRules.HiddenToggleRect.Y + AreaFollowerPanelRules.HiddenToggleRect.Height, "the hidden button ends at the screen's bottom");
    }

    [TestMethod]
    public void Wait_is_greyed_for_a_mind_controlled_follower_and_inventory_when_it_cannot_be_opened()
    {
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
        {
            Assert.AreEqual(FollowerMenuRowState.Normal, AreaFollowerPanelRules.RowState(item, false, true), $"{item}: normal by default");
            var expected = item switch { FollowerMenuItem.Wait => FollowerMenuRowState.Greyed, _ => FollowerMenuRowState.Normal };
            Assert.AreEqual(expected, AreaFollowerPanelRules.RowState(item, true, true), $"{item}: mind controlled (FP8)");
            expected = item switch { FollowerMenuItem.Inventory => FollowerMenuRowState.Greyed, _ => FollowerMenuRowState.Normal };
            Assert.AreEqual(expected, AreaFollowerPanelRules.RowState(item, false, false), $"{item}: cannot be opened (FP11)");
        }
        Assert.AreEqual(FollowerMenuRowState.Normal, AreaFollowerPanelRules.RowState(FollowerMenuItem.CharacterSheet, true, false), "FP8: the sheet is never greyed");
    }

    [TestMethod]
    public void The_dialog_holds_the_panel_drawn_and_inert_only_under_the_setting_with_nothing_else_covering()
    {
        // ARC-UI-2069 (DB1, DB2): Original keeps the portraits drawn under the dialog; a cover or another window hides them as before.
        Assert.IsTrue(AreaFollowerPanelRules.HeldUnderDialog(dialogBarStays: true, dialogOnTop: true, otherCover: false));
        Assert.IsFalse(AreaFollowerPanelRules.HeldUnderDialog(false, true, false), "Steamwoven hides the panel under the dialog");
        Assert.IsFalse(AreaFollowerPanelRules.HeldUnderDialog(true, false, false), "another window on top hides it");
        Assert.IsFalse(AreaFollowerPanelRules.HeldUnderDialog(true, true, true), "a menu, palette, save list or cover over the dialog hides it");
    }
}
