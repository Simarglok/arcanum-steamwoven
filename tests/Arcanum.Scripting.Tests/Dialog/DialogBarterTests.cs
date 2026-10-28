using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The b: reply through the barter port (ARC-UI-050, ledger BT7). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogBarterTests
{
    internal sealed class Barter : IDialogBarterPort
    {
        public List<(long Npc, long Pc)> Opened { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;

        public DialogCommandResult Execute(OpenBarterCommand command)
        {
            Opened.Add((command.Npc.Value, command.PlayerCharacter.Value));
            return Result;
        }
    }

    [TestMethod]
    public void A_b_reply_runs_its_action_then_ends_for_the_barter_window_without_following_its_value()
    {
        var document = Read(Record("10", "Anything else?") + Record("11", "b:", iq: "1", response: "20", action: "MM 1") + Record("20", "Never spoken."));
        var barter = new Barter();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, barter: barter);
        Assert.AreEqual((DialogOptionTransition.Barter, DialogOptionTextKind.Generated, (DialogOptionBlock?)null),
            (conversation.Options[0].Transition, conversation.Options[0].TextKind, conversation.Options[0].Block));
        Assert.IsNull(conversation.Options[0].Target);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.BarterWindow, null), conversation.End);
        CollectionAssert.AreEqual(new[] { (900L, 800L) }, barter.Opened, "the NPC as merchant, the PC as buyer");
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands, "the reply's action runs first");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects.Single(e => e.Command is OpenBarterCommand).Outcome);

        var rejected = Start(document, new Ports(), Owner(), world: new World(), barter: new Barter { Result = DialogCommandResult.Rejected });
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
    }

    [TestMethod]
    public void A_b_reply_needs_the_port_an_exact_marker_and_no_dialog_transfer()
    {
        var reply = Read(Record("10", "?") + Record("11", "b:", iq: "1", response: "77"));
        var plain = Start(reply, new Ports(), Owner());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, plain.Options[0].Block!.Kind, "unadmitted without a barter port");
        var missing = Start(reply, new Ports(), Owner(), barter: new Barter());
        Assert.AreEqual((DialogOptionTransition.Barter, (DialogConversationFailure?)null), (missing.Options[0].Transition, missing.Failure),
            "a missing target does not fail: the value is not followed");
        var upper = Start(Read(Record("10", "?") + Record("11", "B:", iq: "1", response: "0")), new Ports(), Owner(), barter: new Barter());
        Assert.AreEqual(DialogOptionTransition.Barter, upper.Options[0].Transition, "the marker letter in either case, as every generated marker here");
        var withFinal = Start(Read(Record("10", "?") + Record("11", "b:", iq: "1", response: "0", action: "fl 20") + Record("20", "Bye.")),
            new Ports(), Owner(), barter: new Barter());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, withFinal.Options[0].Block!.Kind, "a dialog transfer in the same reply");
    }
}
