using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-108 (MIG16, TM13): the interface mode stack and its tables against the rows of the original-scripts-authority reading
/// (SA37–SA41, SA44, SA52): one change function, a window opener that pops and pushes, target modes that pop the mode below them too,
/// and a lock that makes a nested change do nothing. Pure logic, no original data.
/// </summary>
[TestClass]
public sealed class InterfaceModeStackTests
{
    private static InterfaceMode[] Of(InterfaceModeStack stack) => [.. stack.Modes];

    private sealed class Recorder(Func<InterfaceModeStack?>? nested = null) : IInterfaceModeObserver
    {
        public readonly List<InterfaceModeStep> Steps = [];
        public InterfaceModeChange? Nested;
        public void Step(InterfaceModeStep step)
        {
            Steps.Add(step);
            if (nested?.Invoke() is { } stack && Nested is null) Nested = stack.Push(InterfaceMode.Logbook);
        }
    }

    [TestMethod]
    public void AStackStartsAsTheGameViewAlone()
    {
        var stack = new InterfaceModeStack();
        Assert.AreEqual(InterfaceMode.Game, stack.Current);
        Assert.AreEqual(1, stack.Depth);
        Assert.IsTrue(stack.GameViewActs());
    }

    [TestMethod]
    public void PushRunsTheOldModesLeaveCaseThenTheNewModesEnterCase()
    {
        var stack = new InterfaceModeStack();
        var change = stack.Push(InterfaceMode.Logbook);
        Assert.AreEqual(InterfaceModeStatus.Applied, change.Status);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Logbook }, Of(stack));
        Assert.AreEqual(2, change.Steps.Count);
        Assert.AreEqual(new InterfaceModeStep(true, InterfaceMode.Game, InterfaceMode.Logbook, false), change.Steps[0]);
        Assert.AreEqual(new InterfaceModeStep(false, InterfaceMode.Logbook, InterfaceMode.Game, false), change.Steps[1]);
    }

    [TestMethod]
    public void PopTakesTheModeBelowAndAPopAtTheBaseLeavesTheGameView()
    {
        var stack = new InterfaceModeStack();
        stack.Push(InterfaceMode.Character);
        var pop = stack.Pop();
        Assert.AreEqual(InterfaceMode.Game, stack.Current);
        Assert.IsTrue(pop.Steps[0].Popped);
        Assert.IsTrue(pop.Steps[0].ClosesWindow);
        var atBase = stack.Pop();
        Assert.AreEqual(InterfaceModeStatus.Applied, atBase.Status);
        Assert.AreEqual(1, stack.Depth);
        Assert.AreEqual(InterfaceMode.Game, stack.Current);
    }

    [TestMethod]
    public void ModeZeroAndUndefinedModesCannotBePushed()
    {
        var stack = new InterfaceModeStack();
        Assert.AreEqual(InterfaceModeStatus.InvalidMode, stack.Push(InterfaceMode.Game).Status);
        Assert.AreEqual(InterfaceModeStatus.InvalidMode, stack.Push((InterfaceMode)21).Status);
        Assert.AreEqual(InterfaceModeStatus.InvalidMode, stack.Push((InterfaceMode)(-1)).Status);
        Assert.AreEqual(1, stack.Depth);
    }

    [TestMethod]
    public void AWindowOpenerReplacesTheTopMode()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Logbook);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Logbook }, Of(stack));
        var change = stack.OpenWindow(InterfaceMode.Character);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Character }, Of(stack));
        // The logbook's leave case closes its window, then the character window enters.
        Assert.IsTrue(change.Steps.Any(s => s.Leave && s.Mode == InterfaceMode.Logbook && s.ClosesWindow));
        Assert.AreEqual(InterfaceMode.Character, change.Steps[^1].Mode);
    }

    [TestMethod]
    public void AWindowOpenerClosesItsWindowWhenItIsAlreadyOnTop()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Inventory);
        var again = stack.OpenWindow(InterfaceMode.Inventory);
        Assert.AreEqual(InterfaceModeStatus.Applied, again.Status);
        Assert.AreEqual(1, stack.Depth);
    }

    [TestMethod]
    public void OnlyWindowModesCanBeOpenedAsWindows()
    {
        var stack = new InterfaceModeStack();
        foreach (var mode in new[] { InterfaceMode.Game, InterfaceMode.SpellTarget, InterfaceMode.Unused, InterfaceMode.QuantityPicker, InterfaceMode.FollowerOrder })
            Assert.AreEqual(InterfaceModeStatus.InvalidMode, stack.OpenWindow(mode).Status, mode.ToString());
        Assert.AreEqual(1, stack.Depth);
    }

    [TestMethod]
    public void OpenersRefuseForADeadOrUnconsciousHeroAsTheRowsSay()
    {
        var stack = new InterfaceModeStack();
        foreach (var mode in new[] { InterfaceMode.Logbook, InterfaceMode.Character, InterfaceMode.Book, InterfaceMode.Schematic, InterfaceMode.WorldMap,
                     InterfaceMode.Sleep, InterfaceMode.Dialog, InterfaceMode.Inventory })
            Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenWindow(mode, heroDead: true).Status, mode.ToString());
        // Unconscious: only the spell, skill, item use, world map, sleep and dialog openers refuse.
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenWindow(InterfaceMode.WorldMap, heroUnconscious: true).Status);
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenWindow(InterfaceMode.Sleep, heroUnconscious: true).Status);
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenWindow(InterfaceMode.Dialog, heroUnconscious: true).Status);
        Assert.AreEqual(InterfaceModeStatus.Applied, stack.OpenWindow(InterfaceMode.Inventory, heroUnconscious: true).Status);
        Assert.AreEqual(InterfaceMode.Inventory, stack.Current);
        // A refusal changes nothing: the dead hero's attempt to open the logbook over the inventory leaves the inventory.
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenWindow(InterfaceMode.Logbook, heroDead: true).Status);
        Assert.AreEqual(InterfaceMode.Inventory, stack.Current);
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenTarget(InterfaceMode.SpellTarget, overInventory: true, heroUnconscious: true).Status);
        Assert.AreEqual(InterfaceModeStatus.Refused, stack.OpenTarget(InterfaceMode.ItemTarget, overInventory: true, heroDead: true).Status);
        // The follower order and the quantity picker test nothing.
        Assert.AreEqual(InterfaceModeStatus.Applied, stack.OpenOverlay(InterfaceMode.FollowerOrder).Status);
    }

    [TestMethod]
    public void ATargetModeOverTheInventoryPopsTheInventoryToo()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Inventory);
        var open = stack.OpenTarget(InterfaceMode.SpellTarget, overInventory: true);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Inventory, InterfaceMode.SpellTarget }, Of(stack));
        // The inventory keeps its window while a target mode sits over it.
        Assert.IsFalse(open.Steps[0].ClosesWindow);
        Assert.AreEqual(InterfaceCursor.Spell, stack.Cursor);
        Assert.IsFalse(stack.GameViewActs(anotherHasTheTurn: true));
        Assert.IsTrue(stack.GameViewActs());

        var close = stack.Pop();
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game }, Of(stack));
        var leaves = close.Steps.Where(s => s.Leave).ToArray();
        CollectionAssert.AreEqual(new[] { InterfaceMode.SpellTarget, InterfaceMode.Inventory }, leaves.Select(s => s.Mode).ToArray());
        Assert.IsTrue(leaves.All(s => s.Popped));
        // The inventory is left for the game view, so its window closes.
        Assert.IsTrue(leaves[1].ClosesWindow);
    }

    [TestMethod]
    public void ASkillTargetFromTheGameViewPopsTheBaseTooWhichChangesNothing()
    {
        var stack = new InterfaceModeStack();
        stack.OpenTarget(InterfaceMode.SkillTarget, overInventory: false);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.SkillTarget }, Of(stack));
        Assert.AreEqual(InterfaceCursor.Skill, stack.Cursor);
        var close = stack.Pop();
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game }, Of(stack));
        // The skill target's leave asks for a second pop, which at the base leaves mode 0 and still runs its cases.
        CollectionAssert.AreEqual(new[]
        {
            new InterfaceModeStep(true, InterfaceMode.SkillTarget, InterfaceMode.Game, true), new InterfaceModeStep(false, InterfaceMode.Game, InterfaceMode.SkillTarget, true),
            new InterfaceModeStep(true, InterfaceMode.Game, InterfaceMode.Game, true), new InterfaceModeStep(false, InterfaceMode.Game, InterfaceMode.Game, true),
        }, close.Steps.ToArray());
    }

    [TestMethod]
    public void AWindowUnderAnOverlayIsClosedByItsOpener()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Inventory);
        stack.OpenTarget(InterfaceMode.SpellTarget, overInventory: true);
        var close = stack.OpenWindow(InterfaceMode.Inventory);
        Assert.AreEqual(InterfaceModeStatus.Applied, close.Status);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game }, Of(stack));

        stack.OpenWindow(InterfaceMode.Barter);
        stack.OpenOverlay(InterfaceMode.QuantityPicker);
        stack.OpenWindow(InterfaceMode.Barter);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game }, Of(stack));
    }

    [TestMethod]
    public void AnItemTargetPutsAHeldItemBackWhenLeft()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Inventory);
        stack.OpenTarget(InterfaceMode.ItemTarget, overInventory: true);
        Assert.AreEqual(InterfaceCursor.Spell, stack.Cursor);
        Assert.IsTrue(stack.GameViewActs());
        var close = stack.Pop();
        Assert.IsTrue(close.Steps.First(s => s.Leave && s.Mode == InterfaceMode.ItemTarget).PutsHeldItemBack);
        // Mode 16 does not ask for the second pop (only 1 and 2 do): the inventory stays.
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Inventory }, Of(stack));
    }

    [TestMethod]
    public void TheQuantityPickerSitsOverAnInventoryWhichKeepsItsWindow()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Barter);
        var open = stack.OpenOverlay(InterfaceMode.QuantityPicker);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Barter, InterfaceMode.QuantityPicker }, Of(stack));
        Assert.IsFalse(open.Steps[0].ClosesWindow);
        Assert.IsFalse(stack.GameViewActs());
        stack.Pop();
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Barter }, Of(stack));
    }

    [TestMethod]
    public void ADialogIsEndedByAPopAndResumedWhenReturnedToByAPop()
    {
        var stack = new InterfaceModeStack();
        stack.OpenWindow(InterfaceMode.Dialog);
        var ended = stack.Pop();
        var leave = ended.Steps.First(s => s.Leave && s.Mode == InterfaceMode.Dialog);
        Assert.IsTrue(leave.Popped);
        Assert.AreEqual(InterfaceMode.Game, stack.Current);

        // A mode pushed over a dialog only suspends it (a raw push: no opener takes this route), and returning to it by a pop resumes it.
        stack.OpenWindow(InterfaceMode.Dialog);
        var pushed = stack.Push(InterfaceMode.Barter);
        var suspend = pushed.Steps.First(s => s.Leave && s.Mode == InterfaceMode.Dialog);
        Assert.IsFalse(suspend.Popped);
        var back = stack.Pop();
        Assert.AreEqual(InterfaceMode.Dialog, stack.Current);
        var resume = back.Steps.First(s => !s.Leave && s.Mode == InterfaceMode.Dialog);
        Assert.IsTrue(resume.Popped);
        Assert.IsTrue(stack.GameViewActs());
    }

    [TestMethod]
    public void ANestedChangeFromAnObserverIsLockedOutAndTheStateIsAlreadyNew()
    {
        InterfaceModeStack? stack = null;
        var recorder = new Recorder(() => stack);
        stack = new InterfaceModeStack(recorder);
        stack.OpenWindow(InterfaceMode.Character);
        Assert.AreEqual(InterfaceModeStatus.Locked, recorder.Nested!.Status);
        CollectionAssert.AreEqual(new[] { InterfaceMode.Game, InterfaceMode.Character }, Of(stack));
        // The lock is gone once the change is done.
        Assert.AreEqual(InterfaceModeStatus.Applied, stack.Pop().Status);
        // OpenWindow = a pop and a push (two cases each), then the pop: six steps in all.
        Assert.AreEqual(6, recorder.Steps.Count);
    }

    [TestMethod]
    public void ResetClearsTheStackWithoutAnyCase()
    {
        var recorder = new Recorder();
        var stack = new InterfaceModeStack(recorder);
        stack.OpenWindow(InterfaceMode.Inventory);
        var before = recorder.Steps.Count;
        stack.Reset();
        Assert.AreEqual(1, stack.Depth);
        Assert.AreEqual(before, recorder.Steps.Count);
    }

    [TestMethod]
    public void ThePushIsBoundedByTheLimitAndTheStackIsUnchangedWhenItIsReached()
    {
        var stack = new InterfaceModeStack(limits: new(MaxDepth: 3));
        Assert.AreEqual(InterfaceModeStatus.Applied, stack.Push(InterfaceMode.Inventory).Status);
        Assert.AreEqual(InterfaceModeStatus.Applied, stack.OpenOverlay(InterfaceMode.QuantityPicker).Status);
        Assert.AreEqual(InterfaceModeStatus.DepthExceeded, stack.Push(InterfaceMode.Matchmaker).Status);
        Assert.AreEqual(3, stack.Depth);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new InterfaceModeStack(limits: new(MaxDepth: 1)));
    }

    [TestMethod]
    public void TheModeTablesFollowTheReading()
    {
        var all = Enumerable.Range(0, InterfaceModes.Count).Select(i => (InterfaceMode)i).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 16, 18 }, all.Where(m => InterfaceModes.GameViewActs(m)).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 5, 16, 18 }, all.Where(m => InterfaceModes.ScrollsView(m)).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 18 }, all.Where(m => InterfaceModes.FollowerOrderKeys(m)).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 4, 5, 7, 8, 9, 10, 11, 14, 15, 19, 20 }, all.Where(InterfaceModes.RestoresHiddenBar).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 14, 15, 19, 20 }, all.Where(InterfaceModes.IsWindow).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 16 }, all.Where(m => InterfaceModes.Cursor(m) == InterfaceCursor.Spell).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 2 }, all.Where(m => InterfaceModes.Cursor(m) == InterfaceCursor.Skill).Select(m => (int)m).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 18 }, all.Where(m => InterfaceModes.Cursor(m) == InterfaceCursor.Game).Select(m => (int)m).ToArray());
        Assert.IsFalse(InterfaceModes.GameViewActs(InterfaceMode.Game, anotherHasTheTurn: true));
        Assert.IsFalse(InterfaceModes.ScrollsView(InterfaceMode.Game, anotherHasTheTurn: true));
        Assert.IsFalse(InterfaceModes.FollowerOrderKeys(InterfaceMode.Game, anotherHasTheTurn: true));
    }

    [TestMethod]
    public void AHeldItemIsPutBackByEveryLeaveCaseThatAsksForItAsReadInTheExecutable()
    {
        // 1.0.7.4, 0x551A80 (ARC-DEV-110): every leave case sets the flag that asks for it except 12, 13 and 17; the item target (16)
        // calls the put-back itself; the inventory (8) asks only while its window stays (going to 1, 2, 13 or 16).
        var all = Enumerable.Range(0, InterfaceModes.Count).Select(i => (InterfaceMode)i).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 14, 15, 16, 18, 19, 20 },
            all.Where(m => m != InterfaceMode.Inventory && InterfaceModes.LeavePutsHeldItemBack(m, InterfaceMode.Game)).Select(m => (int)m).ToArray());
        foreach (var next in all.Where(m => m != InterfaceMode.Game))
            Assert.AreEqual(next is InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.QuantityPicker or InterfaceMode.ItemTarget,
                InterfaceModes.LeavePutsHeldItemBack(InterfaceMode.Inventory, next), "inventory to " + next);
        Assert.IsFalse(InterfaceModes.LeavePutsHeldItemBack(InterfaceMode.Inventory, InterfaceMode.Game), "an item lifted off the inventory stays held when its window closes");

        // A window opened from the base pops the base (leave case 0) and pushes: the held item goes back; closing the inventory does not.
        var stack = new InterfaceModeStack();
        var open = stack.OpenWindow(InterfaceMode.Logbook);
        Assert.IsTrue(open.Steps.Any(s => s.PutsHeldItemBack));
        stack.OpenWindow(InterfaceMode.Logbook);
        stack.OpenWindow(InterfaceMode.Inventory);
        var close = stack.OpenWindow(InterfaceMode.Inventory);
        Assert.IsFalse(close.Steps.Any(s => s.PutsHeldItemBack));
        // A spell target left by a pop puts it back (and the repeat pop at the base runs the leave case of 0 once more).
        stack.OpenTarget(InterfaceMode.SpellTarget, overInventory: false);
        Assert.IsTrue(stack.Pop().Steps.Count(s => s.PutsHeldItemBack) >= 1);
        // The quantity picker's leave does not.
        stack.OpenWindow(InterfaceMode.Inventory);
        stack.OpenOverlay(InterfaceMode.QuantityPicker);
        Assert.IsFalse(stack.Pop().Steps.Any(s => s.PutsHeldItemBack));
    }

    [TestMethod]
    public void WindowsKeepTheirWindowOnlyWhereTheModeTableSays()
    {
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Inventory, InterfaceMode.SpellTarget));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Inventory, InterfaceMode.SkillTarget));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Inventory, InterfaceMode.QuantityPicker));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Inventory, InterfaceMode.ItemTarget));
        Assert.IsTrue(InterfaceModes.LeaveClosesWindow(InterfaceMode.Inventory, InterfaceMode.Game));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Loot, InterfaceMode.SpellTarget));
        Assert.IsTrue(InterfaceModes.LeaveClosesWindow(InterfaceMode.Loot, InterfaceMode.ItemTarget));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Barter, InterfaceMode.QuantityPicker));
        Assert.IsTrue(InterfaceModes.LeaveClosesWindow(InterfaceMode.Steal, InterfaceMode.Game));
        Assert.IsTrue(InterfaceModes.LeaveClosesWindow(InterfaceMode.Logbook, InterfaceMode.QuantityPicker));
        Assert.IsFalse(InterfaceModes.LeaveClosesWindow(InterfaceMode.Dialog, InterfaceMode.Game));
    }
}
