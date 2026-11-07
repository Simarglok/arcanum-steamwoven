using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The non-negative IN action through the item-take port (ARC-DEV-199, ledger DAC5, TK1–TK3). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogItemTakeTests
{
    internal sealed class Take : IDialogItemTakePort
    {
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public List<TakeItemCommand> Commands { get; } = [];

        public DialogCommandResult Execute(TakeItemCommand command)
        {
            if (Throw) throw new InvalidOperationException("take");
            Commands.Add(command);
            return Result;
        }
    }

    private static DlgDocument Doc(string action) => Read(Record("10", "Hand it over.", action: action) + Record("11", "Here.", iq: "1", response: "0"));

    [TestMethod]
    public void A_non_negative_in_hands_the_pcs_item_to_the_npc_through_the_port()
    {
        var take = new Take();
        var ports = new Ports();
        var conversation = Start(Doc("LF 1 1, IN 2804, LF 2 1"), ports, Owner(), itemTake: take);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { new TakeItemCommand(new(900), new(800), 2804) }, take.Commands);
        CollectionAssert.AreEqual(new[] { "LF:1:1:900", "LF:2:1:900" }, ports.Commands, "the actions run in order around it");
        Assert.AreEqual(new TakeItemCommand(new(900), new(800), 2804), conversation.Effects[1].Command);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects[1].Outcome);
        // A zero name is a name like any other; a case is not distinguished.
        var zero = Start(Doc("in 0"), new Ports(), Owner(), itemTake: take);
        Assert.IsNull(zero.Failure);
        Assert.AreEqual(0, take.Commands.Last().ItemName);
    }

    [TestMethod]
    public void A_player_reply_action_hands_the_item_over_when_the_reply_is_picked_and_a_plus_sign_or_the_most_negative_operand_is_not_a_hand_over()
    {
        var take = new Take();
        var document = Read(Record("10", "?") + Record("11", "Here you are.", iq: "1", response: "0", action: "IN 77"));
        var conversation = Start(document, new Ports(), Owner(), itemTake: take);
        Assert.AreEqual(0, take.Commands.Count, "nothing before the reply is picked");
        conversation.Select(conversation.Options[0].Token);
        CollectionAssert.AreEqual(new[] { new TakeItemCommand(new(900), new(800), 77) }, take.Commands);

        foreach (var field in new[] { "IN +5", "IN -2147483648" })
        {
            var refused = Start(Doc(field), new Ports(), Owner(), itemTake: new Take());
            Assert.IsTrue(refused.Failure is not null || refused.Frontier is not null, field);
        }
    }

    [TestMethod]
    public void The_negative_in_stays_with_the_inventory_port_and_the_two_ports_are_independent()
    {
        var take = new Take();
        var give = Start(Doc("IN -2804"), new Ports(), Owner(), itemTake: take);
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, give.Frontier!.Kind, "the take port does not admit the negative form");
        Assert.AreEqual(0, take.Commands.Count);
        var withoutPort = Start(Doc("IN 2804"), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, withoutPort.Frontier!.Kind, "without the port the non-negative form is outside the subset");
        Assert.AreEqual(0, withoutPort.Effects.Count);
    }

    [TestMethod]
    public void A_refusing_or_throwing_port_fails_the_conversation()
    {
        var rejected = Start(Doc("IN 2804"), new Ports(), Owner(), itemTake: new Take { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);
        var thrown = Start(Doc("IN 2804"), new Ports(), Owner(), itemTake: new Take { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, thrown.Effects.Last().Outcome);
    }
}
