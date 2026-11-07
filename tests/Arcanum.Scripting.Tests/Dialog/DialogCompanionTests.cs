using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The conditions fo and wt and the actions wa, lv, so and sc through the companion port (ARC-DEV-195, ledger CF1–CF7). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogCompanionTests
{
    internal sealed class Companion(bool led = false, bool jilted = false) : IDialogCompanionPort
    {
        public List<(string Fact, DialogPartyQuery Query)> Asked { get; } = [];
        public List<CompanionCommand> Commands { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public bool LedByPc(DialogPartyQuery query)
        {
            if (Throw) throw new InvalidOperationException("companion");
            Asked.Add(("led", query));
            return led;
        }
        public bool Jilted(DialogPartyQuery query)
        {
            if (Throw) throw new InvalidOperationException("companion");
            Asked.Add(("jilted", query));
            return jilted;
        }
        public DialogCommandResult Execute(CompanionCommand command)
        {
            if (Throw) throw new InvalidOperationException("companion");
            Commands.Add(command);
            return Result;
        }
    }

    // Whether the one reply behind the condition is offered (a second reply with no condition keeps the NPC line from dead-ending).
    private static bool Offered(string condition, IDialogCompanionPort? port, out DialogConversation conversation, bool expectOk = true)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        conversation = Start(document, new Ports(), Owner(), companion: port);
        if (expectOk) Assert.IsNull(conversation.Failure, condition);
        return conversation.Frontier is null && conversation.Options.All(option => option.Block is null) && conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void Fo_asks_whether_the_pc_leads_the_npc_and_wt_whether_it_is_jilted()
    {
        foreach (var (led, jilted) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var port = new Companion(led, jilted);
            Assert.AreEqual(led, Offered("fo 1", port, out _), $"fo 1 led {led}");
            Assert.AreEqual(!led, Offered("fo 0", port, out _), $"fo 0 led {led}");
            Assert.AreEqual(jilted, Offered("wt 1", port, out _), $"wt 1 jilted {jilted}");
            Assert.AreEqual(!jilted, Offered("WT 0", port, out _), $"wt 0 jilted {jilted}");
            Assert.AreEqual(led && jilted, Offered("fo 1, wt 1", port, out _), "a field is a conjunction");
        }
        var asked = new Companion(true);
        Offered("fo 1", asked, out _);
        // The dialog's NPC (900) and PC (800).
        Assert.AreEqual(("led", new DialogPartyQuery(new DialogParticipantId(900), new DialogParticipantId(800))), asked.Asked[0]);
    }

    [TestMethod]
    public void Another_operand_a_plus_sign_and_a_missing_port_are_outside_the_subset_or_damage()
    {
        // The source passes any other operand without a test (valid data outside this subset), as for ME.
        foreach (var field in new[] { "fo 2", "wt -1", "wt 7" })
        {
            Assert.IsFalse(Offered(field, new Companion(), out var conversation), field);
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier?.Kind, field);
        }
        foreach (var field in new[] { "fo +1", "wt" })
        {
            Assert.IsFalse(Offered(field, new Companion(), out var damage, expectOk: false), field);
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, damage.Failure?.Kind, field);
        }
        foreach (var field in new[] { "fo 1", "wt 0" })
        {
            Offered(field, null, out var conversation);
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier?.Kind, field + " without the port");
        }
        // FO and WT are no actions, and WA keeps its party-port condition.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "fo 1")), new Ports(), Owner(), companion: new Companion()).Frontier!.Kind);
    }

    [TestMethod]
    public void A_throwing_port_fails_the_conversation_on_a_condition()
    {
        Offered("fo 1", new Companion { Throw = true }, out var conversation, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind);
    }

    [TestMethod]
    public void The_four_actions_run_in_field_order_in_a_reply_and_in_an_npc_line()
    {
        var port = new Companion();
        var ports = new Ports();
        var line = Start(Read(Record("10", "Hm.", action: "lf 1 1, wa, lv, so, sc, lf 2 1") + Record("11", "Ok.", iq: "1", response: "0")), ports, Owner(), companion: port);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, line.Phase);
        CollectionAssert.AreEqual(new[] { DialogCompanionAction.Wait, DialogCompanionAction.Leave, DialogCompanionAction.SpreadOut, DialogCompanionAction.SpreadClear },
            port.Commands.Select(command => command.Action).ToArray());
        Assert.IsTrue(port.Commands.All(command => command.Npc == new DialogParticipantId(900) && command.PlayerCharacter == new DialogParticipantId(800)));
        CollectionAssert.AreEqual(new[] { "LF:1:1:900", "LF:2:1:900" }, ports.Commands, "the other actions run around them");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, line.Effects[1].Outcome);

        var replyPort = new Companion();
        var reply = Start(Read(Record("10", "?") + Record("11", "Wait here.", iq: "1", response: "0", action: "WA")), new Ports(), Owner(), companion: replyPort);
        Assert.AreEqual(0, replyPort.Commands.Count, "nothing before the reply is picked");
        reply.Select(reply.Options[0].Token);
        Assert.AreEqual(DialogCompanionAction.Wait, replyPort.Commands.Single().Action);
    }

    [TestMethod]
    public void An_operand_after_an_action_without_the_port_or_a_refusal_is_not_a_hand_over()
    {
        foreach (var field in new[] { "wa 1", "lv 0", "so 2", "sc -1" })
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: field)), new Ports(), Owner(), companion: new Companion()).Frontier!.Kind, field);
        foreach (var field in new[] { "wa", "lv", "so", "sc" })
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: field)), new Ports(), Owner()).Frontier!.Kind, field + " without the port");

        var rejected = Start(Read(Record("10", "Hm.", action: "wa")), new Ports(), Owner(), companion: new Companion { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);
        var thrown = Start(Read(Record("10", "Hm.", action: "lv")), new Ports(), Owner(), companion: new Companion { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
    }
}
