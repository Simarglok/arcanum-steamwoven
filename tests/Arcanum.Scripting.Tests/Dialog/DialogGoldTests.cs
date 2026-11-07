using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The money code $$ as a condition and an action through the gold port (ARC-DEV-179, ledger DCC5, DAC2). Records and phrases are invented
// synthetic fixtures.
[TestClass]
public sealed class DialogGoldTests
{
    internal sealed class Gold(int total) : IDialogGoldPort
    {
        public List<(long Pc, long Npc, int Amount)> Moved { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public int Total(DialogGoldQuery query) => total;

        public DialogCommandResult Execute(GoldCommand command)
        {
            Moved.Add((command.PlayerCharacter.Value, command.Npc.Value, command.Amount));
            return Result;
        }
    }

    // Whether the one reply behind the condition is offered: a second reply with no condition is always there, so the NPC line never dead-ends.
    private static bool Offered(int total, string condition)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Pay.", iq: "1", condition: condition, response: "0"));
        var conversation = Start(document, new Ports(), Owner(), gold: new Gold(total));
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        Assert.IsTrue(conversation.Options.All(option => option.Block is null), condition);
        return conversation.Options.Any(option => option.Text == "Pay.");
    }

    [TestMethod]
    public void The_condition_is_the_signed_rule_on_the_total()
    {
        // n >= 0: the total is at least n; n < 0: at most -n; both inclusive (0x4150D0 case 10, DCC5).
        Assert.IsTrue(Offered(100, "$$ 100"));
        Assert.IsTrue(Offered(101, "$$ 100"));
        Assert.IsFalse(Offered(99, "$$ 100"));
        Assert.IsTrue(Offered(0, "$$ 0"), "zero is a floor of zero: always true");
        Assert.IsTrue(Offered(100, "$$-100"));
        Assert.IsTrue(Offered(50, "$$ -100"));
        Assert.IsFalse(Offered(101, "$$ -100"));
        Assert.IsFalse(Offered(int.MinValue, "$$ 5"), "a wrapped negative sum is below any floor");
    }

    [TestMethod]
    public void The_condition_joins_the_other_operations_of_the_field()
    {
        Assert.IsTrue(Offered(50, "$$ 10, gf 3 0"));
        Assert.IsFalse(Offered(5, "$$ 10, gf 3 0"));
        Assert.IsFalse(Offered(50, "$$ 10, gf 3 1"), "gf 3 is clear: the field fails after the gold held");
    }

    [TestMethod]
    public void The_action_pays_or_receives_by_the_sign_and_zero_changes_nothing()
    {
        var document = Read(Record("10", "?", action: "$$ 25") + Record("11", "Here.", iq: "1", response: "20", action: "$$ -30, mm 1")
            + Record("20", "Thanks.", action: "$$ 0"));
        var gold = new Gold(0);
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, gold: gold);
        Assert.IsNull(conversation.Failure);
        CollectionAssert.AreEqual(new[] { (800L, 900L, 25) }, gold.Moved, "the NPC line's action");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { (800L, 900L, 25), (800L, 900L, -30), (800L, 900L, 0) }, gold.Moved);
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands, "the string goes on after the money operation");
        var receipts = conversation.Effects.Where(e => e.Command is GoldCommand).ToArray();
        Assert.AreEqual(3, receipts.Length);
        Assert.IsTrue(receipts.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
    }

    [TestMethod]
    public void A_rejected_payment_fails_the_command_and_a_thrown_one_is_indeterminate()
    {
        var document = Read(Record("10", "?", action: "$$ -5"));
        var rejected = Start(document, new Ports(), Owner(), gold: new Gold(0) { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single(e => e.Command is GoldCommand).Outcome);
    }

    [TestMethod]
    public void Money_needs_the_gold_port_and_reads_an_operand()
    {
        var plain = Start(Read(Record("10", "?", action: "$$ 3")), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, plain.Frontier!.Kind, "unadmitted without a gold port");
        var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: "$$ 3", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(gate, new Ports(), Owner()).Frontier!.Kind, "a condition outside the subset stops the step");
        var blocked = Start(Read(Record("10", "?") + Record("11", "Pay.", iq: "1", response: "0", action: "$$ -3")), new Ports(), Owner());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, blocked.Options.Single().Block!.Kind, "an action outside the subset blocks the reply");
        foreach (var field in new[] { "$$", "$$ x", "$$ 99999999999", "$$ -2147483648", "$$ 5 5" })
        {
            var broken = Start(Read(Record("10", "?", action: field)), new Ports(), Owner(), gold: new Gold(0));
            Assert.IsTrue(broken.Frontier is not null || broken.Failure is not null, field);
        }
    }
}
