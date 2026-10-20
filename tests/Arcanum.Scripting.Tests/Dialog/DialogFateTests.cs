using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The FP action through the fate port (ARC-WORLD-054, ledger FT8). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogFateTests
{
    internal sealed class Fate : IDialogFatePort
    {
        public List<long> Granted { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;

        public DialogCommandResult Execute(FatePointCommand command)
        {
            Granted.Add(command.PlayerCharacter.Value);
            return Result;
        }
    }

    [TestMethod]
    public void Fp_gives_the_pc_a_fate_point_from_an_npc_line_and_a_reply()
    {
        var document = Read(Record("10", "?", action: "fp") + Record("11", "Thanks.", iq: "1", response: "20", action: "MM 1, FP")
            + Record("20", "Go well."));
        var fate = new Fate();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, fate: fate);
        Assert.IsNull(conversation.Failure);
        CollectionAssert.AreEqual(new long[] { 800 }, fate.Granted, "the NPC line's action");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        CollectionAssert.AreEqual(new long[] { 800, 800 }, fate.Granted);
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands);
        var receipts = conversation.Effects.Where(e => e.Command is FatePointCommand).ToArray();
        Assert.AreEqual(2, receipts.Length);
        Assert.IsTrue(receipts.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied && ((FatePointCommand)e.Command).PlayerCharacter.Value == 800));

        var rejected = Start(document, new Ports(), Owner(), world: new World(), fate: new Fate { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single(e => e.Command is FatePointCommand).Outcome);
    }

    [TestMethod]
    public void Fp_needs_the_fate_port_and_takes_no_operand()
    {
        var plain = Start(Read(Record("10", "?", action: "fp")), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, plain.Frontier!.Kind, "unadmitted without a fate port");
        foreach (var action in new[] { "fp 1", "fp1", "fp-1" })
        {
            var refused = Start(Read(Record("10", "?", action: action)), new Ports(), Owner(), fate: new Fate());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, refused.Frontier!.Kind, action);
        }
        var condition = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: "fp", response: "0"));
        var gate = Start(condition, new Ports(), Owner(), fate: new Fate());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, gate.Frontier!.Kind, "not a condition");
    }
}
