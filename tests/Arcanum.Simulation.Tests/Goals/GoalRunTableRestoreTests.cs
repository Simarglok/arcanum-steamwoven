using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-208 (MIG14 S2): a table rebuilt from saved runs holds each in its own slot and nothing else.</summary>
[TestClass]
public sealed class GoalRunTableRestoreTests
{
    private static GoalRun Run(int slot, int id, string owner = "npc:a") =>
        new(owner, new GoalHandle(slot, id), new GoalFrame(GoalType.MoveToTile), dueMs: 1_000 + slot);

    [TestMethod]
    public void Runs_land_in_their_slots_with_the_saved_counter()
    {
        var table = GoalRunTable.Restore(9, [Run(7, 4), Run(0, 2, "npc:b")])!;
        Assert.AreEqual(9, table.NextRunId);
        Assert.AreEqual(2, table.InUseCount);
        Assert.AreEqual("npc:b", table[0]!.Owner);
        Assert.AreEqual("npc:a", table[7]!.Owner);
        Assert.IsNull(table[1]);
        Assert.IsTrue(table.TryGet(new GoalHandle(7, 4), out _));
        Assert.IsFalse(table.TryGet(new GoalHandle(7, 5), out _), "an old handle never matches");
        Assert.IsNull(table.SteppingSlot);
        Assert.IsFalse(table.ExhaustionPending);
    }

    [TestMethod]
    public void The_next_start_takes_the_lowest_free_slot_and_the_saved_run_id()
    {
        var table = GoalRunTable.Restore(9, [Run(0, 2), Run(2, 4)])!;
        var started = table.Allocate("npc:c", new GoalFrame(GoalType.MoveToTile));
        Assert.AreEqual(new GoalHandle(1, 9), started.Handle);
        Assert.AreEqual(10, started.Table.NextRunId);
    }

    [TestMethod]
    public void An_empty_save_is_the_empty_table_with_its_counter()
    {
        var table = GoalRunTable.Restore(1, [])!;
        Assert.AreEqual(0, table.InUseCount);
        Assert.AreEqual(1, table.NextRunId);
    }

    [TestMethod]
    public void Runs_that_cannot_be_a_table_are_refused()
    {
        Assert.IsNull(GoalRunTable.Restore(0, []), "a counter below 1");
        Assert.IsNull(GoalRunTable.Restore(5, [Run(GoalRunTable.SlotCount, 1)]), "a slot past the table");
        Assert.IsNull(GoalRunTable.Restore(5, [Run(-1, 1)]), "a negative slot");
        Assert.IsNull(GoalRunTable.Restore(5, [Run(3, 1), Run(3, 2)]), "two runs in one slot");
        Assert.IsNull(GoalRunTable.Restore(5, [Run(3, 0)]), "a run id below 1");
    }

    [TestMethod]
    public void The_whole_table_can_be_restored_and_is_then_full()
    {
        var table = GoalRunTable.Restore(1000, Enumerable.Range(0, GoalRunTable.SlotCount).Select(i => Run(i, i + 1)))!;
        Assert.AreEqual(GoalRunTable.SlotCount, table.InUseCount);
        var refused = table.Allocate("npc:x", new GoalFrame(GoalType.MoveToTile));
        Assert.IsNull(refused.Handle);
        Assert.IsTrue(refused.Table.ExhaustionPending);
    }
}
