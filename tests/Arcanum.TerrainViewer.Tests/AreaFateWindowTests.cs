using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1112: the Fate Point window (ledger FW1–FW6): the layout, the lines, the toggle buttons that follow the hero's fate and the open decision.</summary>
[TestClass]
public sealed class AreaFateWindowTests
{
    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        Assert.AreEqual((292, 293, 229, 12), (AreaFateLayout.BackgroundArt, AreaFateLayout.ButtonArt, AreaFateLayout.FontArt, AreaFateLayout.Kinds), "FW1 arts");
        Assert.AreEqual((0, 41), (AreaFateLayout.ScreenX, AreaFateLayout.ScreenY), "FW1 the window's place");
        Assert.AreEqual((223, 4), AreaFateLayout.ButtonAt(0), "FW2 the first button");
        Assert.AreEqual((223, 4 + 18 * 11), AreaFateLayout.ButtonAt(11), "FW2 the last button (0x5CAB98..)");
        Assert.AreEqual((11, 3), AreaFateLayout.TextAt(0), "FW2 the first line");
        Assert.AreEqual((11, 3 + 18 * 11), AreaFateLayout.TextAt(11), "FW2 the last line");
        CollectionAssert.AreEqual(new[] { 292, 293, 229 }, AreaFateLayout.ArtNumbers.ToArray());
        Assert.AreEqual((255, 255, 255), AreaFateLayout.TextColour, "FW3 the font takes entry 255 of the colour tables");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaFateLayout.ButtonAt(12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaFateLayout.TextAt(-1));
        // The original's table has one row per kind at a step of 18, no two overlapping.
        for (var kind = 1; kind < AreaFateLayout.Kinds; kind++)
            Assert.AreEqual(18, AreaFateLayout.ButtonAt(kind).Y - AreaFateLayout.ButtonAt(kind - 1).Y, $"kind {kind}");
    }

    [TestMethod]
    public void A_point_in_a_button_finds_its_kind()
    {
        Assert.AreEqual(0, AreaFateLayout.KindAt(223, 4, 16, 16), "the top left of the first button");
        Assert.AreEqual(11, AreaFateLayout.KindAt(238, 4 + 18 * 11 + 15, 16, 16), "the bottom right of the last");
        Assert.IsNull(AreaFateLayout.KindAt(222, 5, 16, 16), "left of the buttons");
        Assert.IsNull(AreaFateLayout.KindAt(230, 4 + 16, 16, 16), "between two rows");
        Assert.IsNull(AreaFateLayout.KindAt(230, 4, 0, 16), "an art that did not read has no buttons");
    }

    [TestMethod]
    public void The_lines_are_the_projects_names_without_a_game_file()
    {
        var texts = AreaFateTexts.Project("test");
        Assert.IsFalse(texts.FromGame);
        Assert.AreEqual(0, texts.GameCount);
        var names = Enumerable.Range(0, AreaFateLayout.Kinds).Select(texts.Line).ToArray();
        Assert.AreEqual("Full heal", names[(int)FateKind.FullHeal]);
        Assert.AreEqual("Disarm traps", names[(int)FateKind.DisarmTraps]);
        Assert.AreEqual(AreaFateLayout.Kinds, names.Distinct().Count(), "twelve different names");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.Line(12));
        var empty = AreaFateContent.Empty("no game folder");
        Assert.IsFalse(empty.Ready);
        Assert.AreEqual("no game folder", empty.Texts.Detail);
    }

    [TestMethod]
    public void A_press_flips_a_button_and_plays_the_toggle_sounds()
    {
        var buttons = new AreaFateButtons();
        var on = buttons.Press(3);
        Assert.AreEqual((3, true), (on.Kind, on.On), "turning on arms the kind");
        CollectionAssert.AreEqual(new[] { AreaGameBarLayout.PressSound }, on.Sounds.ToArray(), "3000 turning on");
        Assert.IsTrue(buttons.IsOn(3));
        Assert.AreEqual(AreaBarButtonLook.Down, buttons.Look(3));
        var off = buttons.Press(3);
        Assert.AreEqual((3, false), (off.Kind, off.On), "turning off undoes it");
        CollectionAssert.AreEqual(new[] { AreaGameBarLayout.ReleaseSound }, off.Sounds.ToArray(), "3001 turning off");
        Assert.IsFalse(buttons.IsOn(3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buttons.Press(12));
    }

    [TestMethod]
    public void The_hovered_button_shows_lit_and_a_down_button_stays_down()
    {
        var buttons = new AreaFateButtons();
        buttons.Enter(5);
        Assert.AreEqual(AreaBarButtonLook.Hovered, buttons.Look(5));
        Assert.AreEqual(AreaBarButtonLook.Idle, buttons.Look(4));
        buttons.Press(5);
        Assert.AreEqual(AreaBarButtonLook.Down, buttons.Look(5), "a toggle that is on stays down under the pointer (GB4)");
        buttons.Exit(4);
        Assert.AreEqual(5, buttons.Hovered, "another button's leave changes nothing");
        buttons.Exit(5);
        Assert.IsNull(buttons.Hovered);
        // The frames of a three-frame art: idle 0, down 1, lit 2.
        Assert.AreEqual(1, AreaGameBarLayout.Frame(AreaBarButtonLook.Down, 3));
        Assert.AreEqual(2, AreaGameBarLayout.Frame(AreaBarButtonLook.Hovered, 3));
    }

    [TestMethod]
    public void The_buttons_follow_the_armed_kinds_and_the_full_heal_never_stays_down()
    {
        var buttons = new AreaFateButtons();
        // Armed kinds 2 (critical hit) and 11 (disarm traps): bits 1 << (kind - 1).
        buttons.Sync(new FateState(5, (1 << 1) | (1 << 10)));
        for (var kind = 0; kind < AreaFateLayout.Kinds; kind++)
            Assert.AreEqual(kind is 2 or 11, buttons.IsOn(kind), $"kind {kind}");
        // A refused press pops back: the host syncs again from the unchanged fate.
        buttons.Press(0);
        Assert.IsTrue(buttons.IsOn(0), "the press flipped it");
        buttons.Sync(new FateState(0, 0));
        Assert.IsFalse(buttons.IsOn(0), "the full heal is never armed: its button pops up (the callback 0x56FF40)");
        buttons.Sync(null);
        Assert.IsFalse(Enumerable.Range(0, AreaFateLayout.Kinds).Any(buttons.IsOn), "without a hero every button is up");
        buttons.Press(7); buttons.Enter(7);
        buttons.Reset();
        Assert.IsFalse(buttons.IsOn(7));
        Assert.IsNull(buttons.Hovered);
    }

    [TestMethod]
    public void Arming_through_the_rules_matches_the_buttons()
    {
        // The window's cycle: a press arms (a point spent), the next press undoes (the point back); at no points the arm is refused and the button pops back.
        var fate = new FateState(1, 0);
        var buttons = new AreaFateButtons();
        var press = buttons.Press((int)FateKind.CriticalHit);
        var armed = FateRules.Arm(fate, (FateKind)press.Kind!);
        Assert.AreEqual(FateStatus.Armed, armed.Status);
        buttons.Sync(armed.After);
        Assert.IsTrue(buttons.IsOn((int)FateKind.CriticalHit));
        Assert.AreEqual(0, armed.After.Points);
        press = buttons.Press((int)FateKind.Gambling);
        var refused = FateRules.Arm(armed.After, (FateKind)press.Kind!);
        Assert.AreEqual(FateStatus.NoPoints, refused.Status);
        buttons.Sync(armed.After);
        Assert.IsFalse(buttons.IsOn((int)FateKind.Gambling), "the refused press popped back");
        press = buttons.Press((int)FateKind.CriticalHit);
        Assert.IsFalse(press.On);
        var undone = FateRules.Undo(armed.After, (FateKind)press.Kind!);
        Assert.AreEqual((FateStatus.Undone, 1), (undone.Status, undone.After.Points));
        buttons.Sync(undone.After);
        Assert.IsFalse(buttons.IsOn((int)FateKind.CriticalHit));
    }

    [TestMethod]
    public void The_window_opens_for_a_living_hero_and_the_second_call_closes_it()
    {
        Assert.AreEqual(AreaFateWindowRules.Toggle.Opened, AreaFateWindowRules.Decide(open: false, hasHero: true, dead: false));
        Assert.AreEqual(AreaFateWindowRules.Toggle.Closed, AreaFateWindowRules.Decide(open: true, hasHero: true, dead: false), "an open window closes (0x56FBF0)");
        Assert.AreEqual(AreaFateWindowRules.Toggle.Closed, AreaFateWindowRules.Decide(open: true, hasHero: false, dead: true), "the close needs no hero");
        Assert.AreEqual(AreaFateWindowRules.Toggle.RefusedDead, AreaFateWindowRules.Decide(open: false, hasHero: true, dead: true), "a dead hero (less than one hit point) does not open it");
        Assert.AreEqual(AreaFateWindowRules.Toggle.RefusedNoHero, AreaFateWindowRules.Decide(open: false, hasHero: false, dead: false));
    }
}
