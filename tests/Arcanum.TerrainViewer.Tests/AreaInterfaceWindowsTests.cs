using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-109 (MIG16 S2): the viewer's one interface mode stack and the hosts of its window modes. A window opens by asking the stack
/// for its mode and the stack's steps show and hide the panel; the cursor family and the modal test read the stack. Synthetic: the
/// panels are counters, no Godot, no original data. ARC-DEV-110 (MIG16 S3): the same host opens the target modes (spell 1, skill 2, item
/// use 16) and the overlays (quantity picker 13, follower order 18); their leave steps run the host given at the open.
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

    // ---- ARC-DEV-110 (MIG16 S3): target modes and overlays ----

    private sealed class Target(AreaInterfaceWindows windows)
    {
        public int Left;
        public InterfaceModeStep? Step;
        public bool? OnStackWhenLeft;
        public AreaTargetOpen OpenTarget(InterfaceMode mode, bool overInventory = false) => windows.OpenTarget(mode, overInventory, step =>
        {
            Left++; Step = step; OnStackWhenLeft = windows.IsOpen(mode);
        });
        public AreaTargetOpen OpenOverlay(InterfaceMode mode) => windows.OpenOverlay(mode, step =>
        {
            Left++; Step = step; OnStackWhenLeft = windows.IsOpen(mode);
        });
    }

    private static string StackOf(AreaInterfaceWindows windows) => string.Join(">", windows.Stack.Modes.Select(m => (int)m));

    [TestMethod]
    public void A_spell_target_opens_over_the_base_with_the_spell_cursor_and_its_leave_step_runs_the_host_once()
    {
        var windows = new AreaInterfaceWindows();
        var spell = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, spell.OpenTarget(InterfaceMode.SpellTarget));
        Assert.AreEqual("0>1", StackOf(windows));
        Assert.AreEqual(InterfaceCursor.Spell, windows.Stack.Cursor);
        Assert.IsFalse(windows.AnyOpen, "a target mode is not a window: the modal test does not count it");
        Assert.AreEqual(0, spell.Left);
        Assert.AreEqual(AreaTargetOpen.AlreadyOpen, spell.OpenTarget(InterfaceMode.SpellTarget));
        Assert.IsTrue(windows.Close(InterfaceMode.SpellTarget));
        Assert.AreEqual(1, spell.Left);
        Assert.AreEqual(false, spell.OnStackWhenLeft, "the stack has dropped the mode when its host runs");
        Assert.IsTrue(spell.Step is { Leave: true, Popped: true, Mode: InterfaceMode.SpellTarget });
        Assert.AreEqual("0", StackOf(windows));
        Assert.IsFalse(windows.Close(InterfaceMode.SpellTarget), "nothing to close");
        Assert.AreEqual(1, spell.Left);
    }

    [TestMethod]
    public void The_skill_and_item_targets_pick_their_cursor_and_the_game_view_acts_in_them()
    {
        var windows = new AreaInterfaceWindows();
        var skill = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, skill.OpenTarget(InterfaceMode.SkillTarget));
        Assert.AreEqual(AreaCursorMode.SkillTarget, AreaCursorArt.Mode(windows.Stack.Cursor));
        Assert.IsTrue(windows.Stack.GameViewActs());
        Assert.IsTrue(windows.Close(InterfaceMode.SkillTarget));
        Assert.AreEqual("0", StackOf(windows), "a skill target pops once more (SA38), which at the base keeps the game view");

        var item = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, item.OpenTarget(InterfaceMode.ItemTarget));
        Assert.AreEqual(AreaCursorMode.SpellTarget, AreaCursorArt.Mode(windows.Stack.Cursor), "item use shows the spell cursor (SA41)");
        Assert.IsTrue(windows.Stack.GameViewActs());
        Assert.IsTrue(windows.Close(InterfaceMode.ItemTarget));
        Assert.AreEqual(1, item.Left);
    }

    [TestMethod]
    public void A_target_with_the_inventory_bit_sits_over_the_inventory_and_a_spell_or_skill_takes_the_window_with_it_on_leaving()
    {
        foreach (var mode in new[] { InterfaceMode.SpellTarget, InterfaceMode.SkillTarget })
        {
            var windows = new AreaInterfaceWindows();
            var inventory = new Panel(windows, InterfaceMode.Inventory);
            Assert.AreEqual(AreaWindowOpen.Opened, inventory.Open());
            var target = new Target(windows);
            Assert.AreEqual(AreaTargetOpen.Opened, target.OpenTarget(mode, overInventory: true), mode.ToString());
            Assert.AreEqual("0>8>" + (int)mode, StackOf(windows));
            Assert.AreEqual(0, inventory.Hidden, "the inventory keeps its window under the target (mode table row 8)");
            Assert.IsTrue(windows.Close(mode));
            Assert.AreEqual("0", StackOf(windows), mode + " pops once more (SA38): the inventory below goes too");
            Assert.AreEqual(1, inventory.Hidden);
            Assert.AreEqual(1, target.Left);
        }
    }

    [TestMethod]
    public void The_item_target_over_the_inventory_pops_once_and_leaves_the_inventory_open()
    {
        // Read in 1.0.7.4 (0x551D4E): the item target's leave case does not ask for the second pop; only 1 and 2 do.
        var windows = new AreaInterfaceWindows();
        var inventory = new Panel(windows, InterfaceMode.Inventory);
        inventory.Open();
        var item = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, item.OpenTarget(InterfaceMode.ItemTarget, overInventory: true));
        Assert.AreEqual("0>8>16", StackOf(windows));
        Assert.IsTrue(windows.Close(InterfaceMode.ItemTarget));
        Assert.AreEqual("0>8", StackOf(windows));
        Assert.AreEqual(0, inventory.Hidden);
        Assert.IsTrue(inventory.Visible && windows.IsOpen(InterfaceMode.Inventory));
        Assert.AreEqual(1, item.Left);
    }

    [TestMethod]
    public void A_target_without_the_inventory_bit_does_not_replace_an_open_window_and_one_over_the_inventory_needs_it_on_top()
    {
        var windows = new AreaInterfaceWindows();
        new Panel(windows, InterfaceMode.Logbook).Open();
        var target = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.WindowOpen, target.OpenTarget(InterfaceMode.SpellTarget));
        Assert.AreEqual(AreaTargetOpen.Refused, target.OpenTarget(InterfaceMode.SpellTarget, overInventory: true), "the logbook is not an inventory window");
        Assert.AreEqual("0>7", StackOf(windows));
        Assert.AreEqual(0, target.Left);
        Assert.IsFalse(windows.IsOpen(InterfaceMode.SpellTarget));
        // The host takes target modes and overlays only.
        Assert.AreEqual(AreaTargetOpen.Refused, target.OpenTarget(InterfaceMode.Logbook));
        Assert.AreEqual(AreaTargetOpen.Refused, target.OpenOverlay(InterfaceMode.Matchmaker));
        Assert.AreEqual(AreaTargetOpen.Refused, target.OpenOverlay(InterfaceMode.QuantityPicker), "the picker needs an inventory window on top");
    }

    [TestMethod]
    public void A_new_target_replaces_the_one_open_and_runs_its_host_first()
    {
        var windows = new AreaInterfaceWindows();
        var spell = new Target(windows);
        var skill = new Target(windows);
        spell.OpenTarget(InterfaceMode.SpellTarget);
        Assert.AreEqual(AreaTargetOpen.Opened, skill.OpenTarget(InterfaceMode.SkillTarget));
        Assert.AreEqual("0>2", StackOf(windows), "one target mode at a time");
        Assert.AreEqual(1, spell.Left);
        Assert.AreEqual(0, skill.Left);
        Assert.IsFalse(windows.IsOpen(InterfaceMode.SpellTarget));
    }

    [TestMethod]
    public void A_window_that_opens_over_a_target_replaces_it_and_the_target_leaves_first()
    {
        var windows = new AreaInterfaceWindows();
        var item = new Target(windows);
        item.OpenTarget(InterfaceMode.ItemTarget);
        var order = new List<string>();
        Assert.AreEqual(AreaWindowOpen.Opened, windows.Open(InterfaceMode.Logbook, () => order.Add("show"), () => order.Add("hide")));
        Assert.AreEqual(1, item.Left, "the stack's replace pops the target and its host runs");
        Assert.AreEqual("0>7", StackOf(windows));
        CollectionAssert.AreEqual(new[] { "show" }, order);
    }

    [TestMethod]
    public void The_quantity_picker_sits_over_an_inventory_window_which_keeps_its_window()
    {
        var windows = new AreaInterfaceWindows();
        var loot = new Panel(windows, InterfaceMode.Loot);
        loot.Open();
        var picker = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, picker.OpenOverlay(InterfaceMode.QuantityPicker));
        Assert.AreEqual("0>10>13", StackOf(windows));
        Assert.AreEqual(0, loot.Hidden);
        Assert.AreEqual(AreaCursorMode.Window, AreaCursorArt.Mode(windows.Stack.Cursor));
        Assert.IsFalse(windows.Stack.GameViewActs(), "the picker is not a game-view mode (SA44)");
        Assert.IsTrue(windows.Close(InterfaceMode.QuantityPicker));
        Assert.AreEqual("0>10", StackOf(windows));
        Assert.AreEqual(1, picker.Left);
        Assert.IsTrue(loot.Visible);
    }

    [TestMethod]
    public void Closing_the_window_under_the_quantity_picker_pops_through_it_and_the_picker_leaves_first()
    {
        var windows = new AreaInterfaceWindows();
        var inventory = new Panel(windows, InterfaceMode.Inventory);
        inventory.Open();
        var picker = new Target(windows);
        picker.OpenOverlay(InterfaceMode.QuantityPicker);
        Assert.IsTrue(windows.Close(InterfaceMode.Inventory));
        Assert.AreEqual("0", StackOf(windows));
        Assert.AreEqual(1, picker.Left, "the question goes with the window");
        Assert.AreEqual(1, inventory.Hidden);
    }

    [TestMethod]
    public void The_follower_order_is_pushed_over_the_base_with_the_game_cursor()
    {
        var windows = new AreaInterfaceWindows();
        var order = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Opened, order.OpenOverlay(InterfaceMode.FollowerOrder));
        Assert.AreEqual("0>18", StackOf(windows));
        Assert.AreEqual(AreaCursorMode.Game, AreaCursorArt.Mode(windows.Stack.Cursor));
        Assert.IsTrue(windows.Stack.GameViewActs());
        Assert.IsTrue(windows.Close(InterfaceMode.FollowerOrder));
        Assert.AreEqual("0", StackOf(windows));
        Assert.AreEqual(1, order.Left);
    }

    [TestMethod]
    public void The_held_item_is_put_back_by_the_leave_steps_that_ask_for_it()
    {
        var windows = new AreaInterfaceWindows();
        var putBack = 0;
        windows.PutHeldItemBack = () => putBack++;
        var inventory = new Panel(windows, InterfaceMode.Inventory);

        // A window opens over the base: the pop of the base runs the leave case of mode 0, which puts a held item back.
        inventory.Open();
        Assert.IsTrue(putBack > 0);
        // An item lifted off the inventory onto the game cursor stays held when the window closes (the leave of 8 asks only while
        // its window stays, going to 1, 2, 13 or 16).
        putBack = 0;
        Assert.IsTrue(windows.Close(InterfaceMode.Inventory));
        Assert.AreEqual(0, putBack);

        var target = new Target(windows);
        target.OpenTarget(InterfaceMode.SpellTarget);
        putBack = 0;
        windows.Close(InterfaceMode.SpellTarget);
        Assert.AreEqual(2, putBack, "the leave of the spell target puts it back, and so does the leave of 0 of its repeat pop at the base");

        inventory.Open();
        target.OpenOverlay(InterfaceMode.QuantityPicker);
        putBack = 0;
        windows.Close(InterfaceMode.QuantityPicker);
        Assert.AreEqual(0, putBack, "the quantity picker's leave does not (and the inventory below is only entered)");
    }

    [TestMethod]
    public void A_host_cannot_change_the_stack_while_its_leave_step_runs()
    {
        var windows = new AreaInterfaceWindows();
        AreaTargetOpen? nested = null;
        string? stackInside = null;
        // Mode 2 is not on the stack, so an open asked from the leave step of mode 1 would succeed outside a change: here the lock refuses it.
        windows.OpenTarget(InterfaceMode.SpellTarget, overInventory: false, _ =>
        {
            nested = windows.OpenTarget(InterfaceMode.SkillTarget, overInventory: false);
            stackInside = StackOf(windows);
        });
        Assert.IsTrue(windows.Close(InterfaceMode.SpellTarget));
        Assert.AreEqual(AreaTargetOpen.Refused, nested, "a change asked while the leave step runs is locked out");
        Assert.AreEqual("0", stackInside);
        Assert.AreEqual("0", StackOf(windows));
        Assert.IsFalse(windows.IsOpen(InterfaceMode.SkillTarget));
        // A host given no callback still leaves cleanly, and the mode can open again.
        Assert.AreEqual(AreaTargetOpen.Opened, windows.OpenTarget(InterfaceMode.SkillTarget, overInventory: false));
        Assert.IsTrue(windows.Close(InterfaceMode.SkillTarget));
        Assert.AreEqual(AreaTargetOpen.Opened, windows.OpenTarget(InterfaceMode.SkillTarget, overInventory: false));
    }

    [TestMethod]
    public void A_push_over_a_window_that_the_leave_case_would_close_is_refused_so_no_panel_hides_without_its_End_routine()
    {
        // Loot closes for item use (mode table row 10), barter and steal close for everything but the picker (rows 4 and 11).
        foreach (var (window, mode) in new[]
        {
            (InterfaceMode.Loot, InterfaceMode.ItemTarget), (InterfaceMode.Barter, InterfaceMode.SpellTarget),
            (InterfaceMode.Steal, InterfaceMode.SkillTarget), (InterfaceMode.Barter, InterfaceMode.ItemTarget),
        })
        {
            var windows = new AreaInterfaceWindows();
            var panel = new Panel(windows, window);
            panel.Open();
            var target = new Target(windows);
            Assert.AreEqual(AreaTargetOpen.Refused, target.OpenTarget(mode, overInventory: true), $"{mode} over {window}");
            Assert.AreEqual("0>" + (int)window, StackOf(windows));
            Assert.AreEqual(0, panel.Hidden);
            Assert.IsTrue(panel.Visible);
        }
        // The picker is the one mode they all keep their window for.
        var barter = new AreaInterfaceWindows();
        new Panel(barter, InterfaceMode.Barter).Open();
        Assert.AreEqual(AreaTargetOpen.Opened, new Target(barter).OpenOverlay(InterfaceMode.QuantityPicker));
    }

    [TestMethod]
    public void The_follower_order_is_refused_over_a_window_that_would_close_and_over_a_spell_or_skill_target()
    {
        var windows = new AreaInterfaceWindows();
        var inventory = new Panel(windows, InterfaceMode.Inventory);
        inventory.Open();
        var order = new Target(windows);
        Assert.AreEqual(AreaTargetOpen.Refused, order.OpenOverlay(InterfaceMode.FollowerOrder), "the inventory's leave case closes its window for mode 18");
        Assert.AreEqual("0>8", StackOf(windows));
        Assert.AreEqual(0, inventory.Hidden);
        Assert.AreEqual(0, order.Left);

        var targets = new AreaInterfaceWindows();
        var spell = new Target(targets);
        spell.OpenTarget(InterfaceMode.SpellTarget);
        // A push over mode 1 would be popped at once by the spell target's repeat (SA38) and would run the order's host and the held-item put-back.
        Assert.AreEqual(AreaTargetOpen.Refused, new Target(targets).OpenOverlay(InterfaceMode.FollowerOrder));
        Assert.AreEqual("0>1", StackOf(targets));
        Assert.AreEqual(0, spell.Left);
        Assert.AreEqual(AreaTargetOpen.AlreadyOpen, new Target(targets).OpenTarget(InterfaceMode.SpellTarget));

        var item = new AreaInterfaceWindows();
        new Target(item).OpenTarget(InterfaceMode.ItemTarget);
        var later = new Target(item);
        Assert.AreEqual(AreaTargetOpen.Opened, later.OpenOverlay(InterfaceMode.FollowerOrder), "mode 16 asks for no repeat pop");
        Assert.AreEqual(AreaTargetOpen.AlreadyOpen, later.OpenOverlay(InterfaceMode.FollowerOrder));
        Assert.AreEqual("0>16>18", StackOf(item));
    }
    // ARC-DEV-111 (MIG16 S4, SA44): the input gates read the stack.
    [TestMethod]
    public void The_game_view_acts_in_modes_0_1_2_16_and_18_and_every_window_owns_the_mouse()
    {
        var windows = new AreaInterfaceWindows();
        Assert.IsTrue(windows.GameViewActs(false));
        Assert.IsFalse(windows.GameViewActs(true), "another participant's turn stops the view in every mode");
        foreach (var target in new[] { InterfaceMode.SpellTarget, InterfaceMode.SkillTarget, InterfaceMode.ItemTarget })
        {
            var host = new AreaInterfaceWindows();
            Assert.AreEqual(AreaTargetOpen.Opened, new Target(host).OpenTarget(target));
            Assert.IsTrue(host.GameViewActs(false), $"{target} lets the game view act");
            Assert.IsFalse(host.GameViewActs(true));
        }
        var order = new AreaInterfaceWindows();
        Assert.AreEqual(AreaTargetOpen.Opened, new Target(order).OpenOverlay(InterfaceMode.FollowerOrder));
        Assert.IsTrue(order.GameViewActs(false));
        foreach (var window in Windows)
        {
            var host = new AreaInterfaceWindows();
            Assert.AreEqual(AreaWindowOpen.Opened, new Panel(host, window).Open());
            // The original's table (SA44): only the dialog (3) lets the game view act among the windows; a host may keep the mouse for it.
            Assert.AreEqual(window == InterfaceMode.Dialog, host.GameViewActs(false), $"{window}: the original's table");
            Assert.IsFalse(host.GameViewActs(false, dialogOwnsMouse: true), $"{window} owns the mouse of a host that keeps it for its dialog");
        }
        var picker = new AreaInterfaceWindows();
        new Panel(picker, InterfaceMode.Inventory).Open();
        Assert.AreEqual(AreaTargetOpen.Opened, new Target(picker).OpenOverlay(InterfaceMode.QuantityPicker));
        Assert.IsFalse(picker.GameViewActs(false), "the quantity picker is no game-view mode");
    }

    [TestMethod]
    public void The_follower_order_keys_run_in_modes_0_1_2_and_18_and_not_on_another_participants_turn()
    {
        var windows = new AreaInterfaceWindows();
        Assert.IsTrue(windows.FollowerOrderKeys(false));
        Assert.IsFalse(windows.FollowerOrderKeys(true));
        Assert.IsTrue(windows.HotkeyKeys, "the number row works in every mode (SA44)");
        var spell = new AreaInterfaceWindows();
        new Target(spell).OpenTarget(InterfaceMode.SpellTarget);
        Assert.IsTrue(spell.FollowerOrderKeys(false));
        var skill = new AreaInterfaceWindows();
        new Target(skill).OpenTarget(InterfaceMode.SkillTarget);
        Assert.IsTrue(skill.FollowerOrderKeys(false));
        var order = new AreaInterfaceWindows();
        new Target(order).OpenOverlay(InterfaceMode.FollowerOrder);
        Assert.IsTrue(order.FollowerOrderKeys(false));
        var item = new AreaInterfaceWindows();
        new Target(item).OpenTarget(InterfaceMode.ItemTarget);
        Assert.IsFalse(item.FollowerOrderKeys(false), "mode 16 is not in the order-key set");
        foreach (var window in Windows)
        {
            var host = new AreaInterfaceWindows();
            new Panel(host, window).Open();
            Assert.IsFalse(host.FollowerOrderKeys(false), $"{window}: no follower order keys");
        }
    }

    [TestMethod]
    public void Scrolling_runs_in_modes_0_1_2_3_5_16_and_18_only()
    {
        Assert.IsTrue(new AreaInterfaceWindows().ScrollsView(false));
        Assert.IsFalse(new AreaInterfaceWindows().ScrollsView(true));
        var map = new AreaInterfaceWindows();
        new Panel(map, InterfaceMode.WorldMap).Open();
        Assert.IsTrue(map.ScrollsView(false), "the world map scrolls (mode 5)");
        var logbook = new AreaInterfaceWindows();
        new Panel(logbook, InterfaceMode.Logbook).Open();
        Assert.IsFalse(logbook.ScrollsView(false));
    }

    // ARC-DEV-111 (SA52): the openers refuse for the hero's state, as the original's do.
    [TestMethod]
    public void A_dead_hero_is_refused_by_the_openers_the_table_names_and_an_unconscious_one_by_the_world_map_and_the_dialog()
    {
        foreach (var window in new[] { InterfaceMode.Logbook, InterfaceMode.Character, InterfaceMode.Book, InterfaceMode.Schematic, InterfaceMode.WorldMap, InterfaceMode.Dialog, InterfaceMode.Inventory })
        {
            var host = new AreaInterfaceWindows();
            Assert.AreEqual(AreaWindowOpen.Refused, host.Open(window, () => { }, () => { }, heroDead: true), $"{window} refuses a dead hero");
            Assert.IsFalse(host.AnyOpen);
        }
        foreach (var window in new[] { InterfaceMode.Barter, InterfaceMode.Loot, InterfaceMode.Steal })
            Assert.AreEqual(AreaWindowOpen.Opened, new AreaInterfaceWindows().Open(window, () => { }, () => { }, heroDead: true), $"{window} tests no hero state");
        foreach (var window in new[] { InterfaceMode.WorldMap, InterfaceMode.Dialog })
            Assert.AreEqual(AreaWindowOpen.Refused, new AreaInterfaceWindows().Open(window, () => { }, () => { }, heroUnconscious: true), $"{window} refuses an unconscious hero");
        foreach (var window in new[] { InterfaceMode.Logbook, InterfaceMode.Character, InterfaceMode.Inventory, InterfaceMode.Schematic })
            Assert.AreEqual(AreaWindowOpen.Opened, new AreaInterfaceWindows().Open(window, () => { }, () => { }, heroUnconscious: true), $"{window} opens for an unconscious hero");
    }
}
