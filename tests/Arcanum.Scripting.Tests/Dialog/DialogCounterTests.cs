using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The lc condition and action through the counter port (ARC-DEV-196, ledger KC1–KC4). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogCounterTests
{
    internal sealed class Counters(int[] values) : IDialogCounterPort
    {
        public List<DialogCounterQuery> Asked { get; } = [];
        public List<CounterWriteCommand> Commands { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public int Read(DialogCounterQuery query)
        {
            if (Throw) throw new InvalidOperationException("counters");
            Asked.Add(query);
            return values[query.Index];
        }
        public DialogCommandResult Execute(CounterWriteCommand command)
        {
            if (Throw) throw new InvalidOperationException("counters");
            Commands.Add(command);
            return Result;
        }
    }

    private static bool Offered(string condition, IDialogCounterPort? port, out DialogConversation conversation, bool expectOk = true)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        conversation = Start(document, new Ports(), Owner(), counters: port);
        if (expectOk) Assert.IsNull(conversation.Failure, condition);
        return conversation.Frontier is null && conversation.Options.All(option => option.Block is null) && conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void The_condition_holds_when_the_counter_equals_the_second_operand()
    {
        var port = new Counters([0, 7, 255, 3]);
        Assert.IsTrue(Offered("lc 0 0", port, out _), "an untouched counter is 0");
        Assert.IsFalse(Offered("lc 0 1", port, out _));
        Assert.IsTrue(Offered("lc 1 7", port, out _));
        Assert.IsFalse(Offered("lc 1 8", port, out _), "equality, not a comparison");
        Assert.IsTrue(Offered("LC 2 255", port, out _), "a case is not distinguished");
        Assert.IsFalse(Offered("lc 2 -1", port, out _), "the counter is never negative");
        Assert.IsFalse(Offered("lc 3 259", port, out _), "259 is no byte: the condition compares the whole number");
        Assert.IsTrue(Offered("lc 1 7, lc 3 3", port, out _), "a field is a conjunction");
        Assert.IsFalse(Offered("lc 1 7, lc 3 4", port, out _));
        // The dialog's NPC (900) and the index.
        Assert.AreEqual(new DialogCounterQuery(new DialogParticipantId(900), 0), port.Asked[0]);
    }

    [TestMethod]
    public void The_action_stores_the_low_eight_bits_of_the_second_operand_in_field_order()
    {
        var port = new Counters([0, 0, 0, 0]);
        var ports = new Ports();
        var line = Start(Read(Record("10", "Hm.", action: "lf 1 1, lc 2 77, lc 3 300, lc 0 -1, lf 2 1") + Record("11", "Ok.", iq: "1", response: "0")), ports, Owner(), counters: port);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, line.Phase);
        CollectionAssert.AreEqual(new[] { new CounterWriteCommand(new(900), 2, 77), new CounterWriteCommand(new(900), 3, 44), new CounterWriteCommand(new(900), 0, 255) }, port.Commands);
        CollectionAssert.AreEqual(new[] { "LF:1:1:900", "LF:2:1:900" }, ports.Commands, "the other actions run around them");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, line.Effects[1].Outcome);

        var reply = Start(Read(Record("10", "?") + Record("11", "Count.", iq: "1", response: "0", action: "lc 1 5")), new Ports(), Owner(), counters: port);
        Assert.AreEqual(3, port.Commands.Count, "nothing before the reply is picked");
        reply.Select(reply.Options[0].Token);
        Assert.AreEqual(new CounterWriteCommand(new(900), 1, 5), port.Commands.Last());
    }

    [TestMethod]
    public void An_index_beyond_the_four_counters_wraps_modulo_four_as_the_shift_count_does()
    {
        // The shift count 8·i is taken modulo 32 (KC1), so index 4 is counter 0 and a negative index counts from the end (ARC-DEV-228).
        var port = new Counters([10, 11, 12, 13]);
        foreach (var (index, counter) in new[] { (4, 0), (5, 1), (7, 3), (36, 0), (-1, 3), (-4, 0), (-5, 3), (int.MaxValue, 3), (int.MinValue, 0) })
        {
            port.Asked.Clear();
            Assert.IsTrue(Offered($"lc {index} {10 + counter}", port, out _), $"lc {index}");
            Assert.AreEqual(counter, port.Asked[0].Index, $"lc {index}");
        }
        Assert.IsFalse(Offered("lc 4 11", port, out _), "index 4 is counter 0, not counter 1");

        var ports = new Ports();
        var line = Start(Read(Record("10", "Hm.", action: "lc 4 5, lc -1 6, lc 21 7") + Record("11", "Ok.", iq: "1", response: "0")), ports, Owner(), counters: port);
        Assert.IsNull(line.Failure);
        CollectionAssert.AreEqual(new[] { new CounterWriteCommand(new(900), 0, 5), new CounterWriteCommand(new(900), 3, 6), new CounterWriteCommand(new(900), 1, 7) },
            port.Commands.TakeLast(3).ToArray());
    }

    [TestMethod]
    public void A_missing_second_operand_reads_zero_as_the_source_does()
    {
        // The source reads the second operand after the first (0x4167C0) and a missing one is 0, as for sk (KC1, KC2).
        var port = new Counters([0, 4, 0, 0]);
        Assert.IsFalse(Offered("lc 1", port, out _), "counter 1 holds 4, not 0");
        Assert.IsTrue(Offered("lc 2", port, out _), "counter 2 holds 0");
        Assert.IsTrue(Offered("lc21", new Counters([9, 0, 0, 0]), out _), "digits and no space: index 21 wraps to counter 1, v = 0");
        var line = Start(Read(Record("10", "?") + Record("11", "Clear.", iq: "1", response: "0", action: "lc 3")), new Ports(), Owner(), counters: port);
        line.Select(line.Options[0].Token);
        Assert.AreEqual(new CounterWriteCommand(new(900), 3, 0), port.Commands.Last());
    }

    [TestMethod]
    public void A_plus_sign_and_a_missing_port_are_damage_or_outside_the_subset()
    {
        var port = new Counters([0, 0, 0, 0]);
        foreach (var field in new[] { "lc +1 0", "lc 1 +0", "lc" })
        {
            Assert.IsFalse(Offered(field, port, out var damage, expectOk: false), field);
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, damage.Failure?.Kind, field);
        }
        Offered("lc 0 0", null, out var without);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, without.Frontier?.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "lc 0 1")), new Ports(), Owner()).Frontier!.Kind);
    }

    [TestMethod]
    public void A_throwing_or_out_of_range_query_and_a_refusing_or_throwing_action_fail_the_conversation()
    {
        Offered("lc 0 0", new Counters([0, 0, 0, 0]) { Throw = true }, out var thrown, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, thrown.Failure!.Kind);
        Offered("lc 0 0", new Counters([256, 0, 0, 0]), out var invalid, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, invalid.Failure!.Kind);
        var rejected = Start(Read(Record("10", "Hm.", action: "lc 1 2")), new Ports(), Owner(), counters: new Counters([0, 0, 0, 0]) { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);
        var failed = Start(Read(Record("10", "Hm.", action: "lc 1 2")), new Ports(), Owner(), counters: new Counters([0, 0, 0, 0]) { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, failed.Failure!.Kind);
    }
}
