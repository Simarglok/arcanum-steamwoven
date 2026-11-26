using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-3416: the area canvas moves each command's kept state to the slot that draws it next.</summary>
[TestClass]
public sealed class AreaSlotAssignmentTests
{
    // The states are named by letters; applies one submission and answers the states in slot order.
    private static string Submit(AreaSlotAssignment<char> slots, ref string states, string wanted)
    {
        var source = slots.Assign(wanted.ToCharArray(), states.Length);
        var before = states;
        var next = new char[source.Length];
        for (var index = 0; index < source.Length; index++) next[index] = before[source[index]];
        CollectionAssert.AreEquivalent(before.ToCharArray(), next, "every state stays with exactly one slot");
        return states = new(next);
    }

    [TestMethod]
    public void A_command_keeps_its_state_when_one_enters_before_it()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCD";
        Assert.AreEqual("ABCD", Submit(slots, ref states, "xyz"));
        // 'q' enters first: x, y and z move one slot on with their states and q takes the spare one.
        Assert.AreEqual("DABC", Submit(slots, ref states, "qxyz"));
    }

    [TestMethod]
    public void A_command_keeps_its_state_when_one_leaves_before_it()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCD";
        Submit(slots, ref states, "wxyz");
        var next = Submit(slots, ref states, "xz");
        Assert.AreEqual("BD", next[..2]);
        // The next submission still knows where the moved states are.
        Assert.AreEqual("DB", Submit(slots, ref states, "zx")[..2]);
    }

    [TestMethod]
    public void An_unchanged_submission_moves_nothing()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCDE";
        Submit(slots, ref states, "wxyz");
        Assert.AreEqual("ABCDE", Submit(slots, ref states, "wxyz"));
    }

    [TestMethod]
    public void A_new_command_takes_its_slots_own_state_when_that_is_free()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABC";
        Submit(slots, ref states, "xyz");
        Assert.AreEqual("ABC", Submit(slots, ref states, "xqz"));
    }

    [TestMethod]
    public void A_key_drawn_twice_keeps_one_of_its_states()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCD";
        Submit(slots, ref states, "xxy");
        Assert.AreEqual('C', Submit(slots, ref states, "yxx")[0]);
    }

    [TestMethod]
    public void The_keys_of_dropped_slots_are_forgotten()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCD";
        Submit(slots, ref states, "xyz");
        // The pool shrank below the slot that held z.
        states = states[..2];
        Assert.AreEqual("AB", Submit(slots, ref states, "z"));
    }

    [TestMethod]
    public void A_reset_forgets_every_key()
    {
        var slots = new AreaSlotAssignment<char>();
        var states = "ABCD";
        Submit(slots, ref states, "xy");
        slots.Reset();
        Assert.AreEqual("ABCD", Submit(slots, ref states, "yx"));
    }

    [TestMethod]
    public void Fewer_slots_than_commands_is_refused()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AreaSlotAssignment<char>().Assign(['x', 'y'], 1));
}
