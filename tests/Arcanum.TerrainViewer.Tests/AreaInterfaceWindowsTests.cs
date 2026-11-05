using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-109 (MIG16 S2): the viewer's one interface mode stack and the hosts of its window modes. A window opens by asking the stack
/// for its mode and the stack's steps show and hide the panel; the cursor family and the modal test read the stack. Synthetic: the
/// panels are counters, no Godot, no original data.
/// </summary>
[TestClass]
public sealed class AreaInterfaceWindowsTests
{
    private static readonly InterfaceMode[] Windows = [.. Enum.GetValues<InterfaceMode>().Where(InterfaceModes.IsWindow)];

    private sealed class Panel(AreaInterfaceWindows windows, InterfaceMode mode)
    {
        public int Shown, Hidden;
        public bool? OnStackWhenShown, OnStackWhenHidden;
        public bool Visible => Shown > Hidden;
        public AreaWindowOpen Open() => windows.Open(mode,
            () => { Shown++; OnStackWhenShown = windows.IsOpen(mode); },
            () => { Hidden++; OnStackWhenHidden = windows.IsOpen(mode); });
    }

    [TestMethod]
    public void A_window_opens_from_the_enter_step_with_the_stack_already_holding_its_mode()
    {
        var windows = new AreaInterfaceWindows();
        Assert.IsFalse(windows.AnyOpen);
        Assert.AreEqual(InterfaceMode.Game, windows.Stack.Current);
        var logbook = new Panel(windows, InterfaceMode.Logbook);
        Assert.AreEqual(AreaWindowOpen.Opened, logbook.Open());
        Assert.AreEqual(1, logbook.Shown);
        Assert.AreEqual(0, logbook.Hidden);
        Assert.AreEqual(true, logbook.OnStackWhenShown, "the stack is in its final state when the panel is shown");
        Assert.IsTrue(windows.IsOpen(InterfaceMode.Logbook) && windows.AnyOpen);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Logbook }, windows.Stack.Modes.ToArray());
    }

    [TestMethod]
    public void A_window_closes_from_the_leave_step_and_can_open_again()
    {
        var windows = new AreaInterfaceWindows();
        var sheet = new Panel(windows, InterfaceMode.Character);
        Assert.AreEqual(AreaWindowOpen.Opened, sheet.Open());
        Assert.IsTrue(windows.Close(InterfaceMode.Character));
        Assert.AreEqual(1, sheet.Hidden);
        Assert.AreEqual(false, sheet.OnStackWhenHidden, "the stack has dropped the mode when the panel is hidden");
        Assert.IsFalse(sheet.Visible || windows.AnyOpen);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game }, windows.Stack.Modes.ToArray());
        Assert.AreEqual(AreaWindowOpen.Opened, sheet.Open(), "the registration of the first open is gone, the second is a fresh one");
        Assert.AreEqual(2, sheet.Shown);
        Assert.IsTrue(sheet.Visible);
    }

    [TestMethod]
    public void Every_window_mode_opens_and_closes_through_the_stack_with_the_gauntlet_cursor()
    {
        // The modes of slice S2: conversation 3, barter 4, world map 5, logbook 7, inventory 8, sheet 9, loot 10, steal 11, schematic 14,
        // reading 15; sleep 6 and the inventory kinds 19, 20 are window modes too, and open the same way.
        CollectionAssert.IsSubsetOf(new[]
        {
            InterfaceMode.Dialog, InterfaceMode.Barter, InterfaceMode.WorldMap, InterfaceMode.Logbook, InterfaceMode.Inventory, InterfaceMode.Character,
            InterfaceMode.Loot, InterfaceMode.Steal, InterfaceMode.Schematic, InterfaceMode.Book,
        }, Windows);
        foreach (var mode in Windows)
        {
            var windows = new AreaInterfaceWindows();
            Assert.AreEqual(AreaCursorMode.Game, AreaCursorArt.Mode(windows.Stack.Cursor), "at rest the game cursor");
            var panel = new Panel(windows, mode);
            Assert.AreEqual(AreaWindowOpen.Opened, panel.Open(), mode.ToString());
            Assert.AreEqual(mode, windows.Stack.Current);
            Assert.AreEqual(AreaCursorMode.Window, AreaCursorArt.Mode(windows.Stack.Cursor), mode + ": SA41 the gauntlet in every window mode");
            Assert.IsTrue(windows.Close(mode), mode.ToString());
            Assert.AreEqual((1, 1), (panel.Shown, panel.Hidden), mode + ": shown once, hidden once");
            Assert.AreEqual(InterfaceMode.Game, windows.Stack.Current);
            Assert.AreEqual(AreaCursorMode.Game, AreaCursorArt.Mode(windows.Stack.Cursor));
        }
    }

    [TestMethod]
    public void An_open_window_and_another_window_change_nothing()
    {
        var windows = new AreaInterfaceWindows();
        var inventory = new Panel(windows, InterfaceMode.Inventory);
        var logbook = new Panel(windows, InterfaceMode.Logbook);
        Assert.AreEqual(AreaWindowOpen.Opened, inventory.Open());
        Assert.AreEqual(AreaWindowOpen.AlreadyOpen, inventory.Open(), "the toggle is the viewer's End routine, not the open");
        Assert.AreEqual(AreaWindowOpen.AnotherWindowOpen, logbook.Open(), "a window-to-window move is slice S4's");
        Assert.AreEqual((1, 0), (inventory.Shown, inventory.Hidden));
        Assert.AreEqual((0, 0), (logbook.Shown, logbook.Hidden));
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Inventory }, windows.Stack.Modes.ToArray());
        Assert.IsFalse(windows.IsOpen(InterfaceMode.Logbook));
        // A refused open leaves no registration behind: once the inventory is closed the logbook opens as a fresh window.
        Assert.IsTrue(windows.Close(InterfaceMode.Inventory));
        Assert.AreEqual(AreaWindowOpen.Opened, logbook.Open());
        Assert.AreEqual((1, 0), (logbook.Shown, logbook.Hidden));
    }

    [TestMethod]
    public void Only_window_modes_open_and_a_mode_that_is_not_on_the_stack_does_not_close()
    {
        var windows = new AreaInterfaceWindows();
        var shown = 0;
        foreach (var mode in new[] { InterfaceMode.Game, InterfaceMode.SpellTarget, InterfaceMode.SkillTarget, InterfaceMode.QuantityPicker,
            InterfaceMode.Matchmaker, InterfaceMode.FollowerOrder, InterfaceMode.ItemTarget, InterfaceMode.Unused })
            Assert.AreEqual(AreaWindowOpen.Refused, windows.Open(mode, () => shown++, () => { }), mode + " is not a window mode");
        Assert.AreEqual(0, shown);
        Assert.AreEqual(1, windows.Stack.Depth);
        Assert.IsFalse(windows.Close(InterfaceMode.Logbook));
        Assert.IsFalse(windows.Close(InterfaceMode.SpellTarget));
        Assert.ThrowsExactly<ArgumentNullException>(() => windows.Open(InterfaceMode.Logbook, null!, () => { }));
    }

    [TestMethod]
    public void A_dialog_is_ended_by_the_pop_that_closes_it()
    {
        // Mode table, row 3: the dialog ends on a pop and is only suspended by a push. The viewer closes it by a pop, so the panel hides.
        var windows = new AreaInterfaceWindows();
        var dialog = new Panel(windows, InterfaceMode.Dialog);
        Assert.AreEqual(AreaWindowOpen.Opened, dialog.Open());
        Assert.AreEqual(InterfaceMode.Dialog, windows.Stack.Current);
        Assert.IsTrue(windows.Close(InterfaceMode.Dialog));
        Assert.AreEqual((1, 1), (dialog.Shown, dialog.Hidden));
        // A barter from a `b:` reply opens after the dialog ended, never over it.
        var barter = new Panel(windows, InterfaceMode.Barter);
        Assert.AreEqual(AreaWindowOpen.Opened, barter.Open());
        Assert.IsTrue(windows.Close(InterfaceMode.Barter));
        Assert.AreEqual((1, 1), (barter.Shown, barter.Hidden));
    }

    [TestMethod]
    public void A_stack_change_asked_from_a_show_or_hide_does_nothing_because_the_change_is_locked()
    {
        // The reason the show and hide only open and close the panel: the stack's lock (SA37) makes a nested change a no-op, so the viewer's
        // End routines (which may open the next window) run around the stack call, not inside it.
        var windows = new AreaInterfaceWindows();
        bool? closedFromShow = null;
        Assert.AreEqual(AreaWindowOpen.Opened, windows.Open(InterfaceMode.Logbook, () => closedFromShow = windows.Close(InterfaceMode.Logbook), () => { }));
        Assert.AreEqual(false, closedFromShow, "a close asked while the open runs is locked out");
        Assert.IsTrue(windows.IsOpen(InterfaceMode.Logbook));

        var fromHide = new List<AreaWindowOpen>();
        var next = new AreaInterfaceWindows();
        Assert.AreEqual(AreaWindowOpen.Opened, next.Open(InterfaceMode.Logbook, () => { },
            () => fromHide.Add(next.Open(InterfaceMode.Barter, () => { }, () => { }))));
        Assert.IsTrue(next.Close(InterfaceMode.Logbook));
        Assert.AreEqual(AreaWindowOpen.Refused, fromHide.Single(), "an open asked while the close runs is locked out");
        Assert.IsFalse(next.AnyOpen);
        Assert.AreEqual(AreaWindowOpen.Opened, next.Open(InterfaceMode.Barter, () => { }, () => { }), "outside the change it opens");
    }

    [TestMethod]
    public void The_cursor_mode_follows_the_cursor_family_of_the_stack()
    {
        Assert.AreEqual(AreaCursorMode.Game, AreaCursorArt.Mode(InterfaceCursor.Game));
        Assert.AreEqual(AreaCursorMode.SpellTarget, AreaCursorArt.Mode(InterfaceCursor.Spell));
        Assert.AreEqual(AreaCursorMode.SkillTarget, AreaCursorArt.Mode(InterfaceCursor.Skill));
        Assert.AreEqual(AreaCursorMode.Window, AreaCursorArt.Mode(InterfaceCursor.Gauntlet));
        // Every mode of the original gives the cursor mode and art the viewer already picked by mode (CU1): spell in 1 and 16, skill in 2,
        // the game cursor in 0 and 18, the gauntlet in the rest (a window).
        foreach (var mode in Enum.GetValues<InterfaceMode>())
        {
            var cursor = AreaCursorArt.Mode(InterfaceModes.Cursor(mode));
            var (expectedMode, expectedArt) = mode switch
            {
                InterfaceMode.SpellTarget or InterfaceMode.ItemTarget => (AreaCursorMode.SpellTarget, AreaCursorArt.Spell),
                InterfaceMode.SkillTarget => (AreaCursorMode.SkillTarget, AreaCursorArt.Skill),
                InterfaceMode.Game or InterfaceMode.FollowerOrder => (AreaCursorMode.Game, AreaCursorArt.Gauntlet),
                _ => (AreaCursorMode.Window, AreaCursorArt.Gauntlet),
            };
            Assert.AreEqual(expectedMode, cursor, mode.ToString());
            Assert.AreEqual(expectedArt, AreaCursorArt.Art(cursor, false, null, false), mode.ToString());
        }
    }
}
