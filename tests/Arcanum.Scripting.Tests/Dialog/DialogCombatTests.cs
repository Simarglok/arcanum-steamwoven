using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The co action through the combat port (ARC-DEV-190, ledger SU13, DAC1, CA1–CA2). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogCombatTests
{
    internal sealed class Combat : IDialogCombatPort
    {
        public List<StartCombatCommand> Started { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public StartCombatCommand? Seen => Started.SingleOrDefault();
        public DialogCommandResult Execute(StartCombatCommand command)
        {
            Started.Add(command);
            return Result;
        }
    }

    // A reply with the action, offered next to the fixed target node 20 ("Bye.") and the final line node 40, selected.
    private static DialogConversation Selected(string action, Ports ports, Combat? combat, IDialogCombatPort? port = null)
    {
        var document = Read(Record("10", "?") + Record("11", "Attack.", iq: "1", response: "20", action: action)
            + Record("20", "Bye.") + Record("40", "Farewell.", action: "qu 1005 2"));
        var conversation = Start(document, ports, Owner(), world: new World(), combat: port ?? combat);
        Assert.IsNull(conversation.Failure, action);
        Assert.IsNull(conversation.Options[0].Block, action);
        conversation.Select(conversation.Options[0].Token);
        return conversation;
    }

    [TestMethod]
    public void Co_asks_the_port_once_for_the_pc_attacking_the_npc()
    {
        var combat = new Combat();
        var conversation = Selected("co", new Ports(), combat);
        Assert.IsNull(conversation.Failure);
        // The dialog's PC (800) attacks the NPC (900): the original's attacker is the PC handle, the victim the NPC handle (SU13).
        Assert.AreEqual(new StartCombatCommand(new DialogParticipantId(900), new DialogParticipantId(800)), combat.Seen);
        var effect = conversation.Effects.Single(e => e.Command is StartCombatCommand);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, effect.Outcome);
        Assert.AreEqual("Bye.", conversation.NpcLine!.Text, "the reply's own transition still follows");
    }

    [TestMethod]
    public void The_attack_event_runs_at_the_end_of_the_string_and_once()
    {
        // co sets a flag (0x415ECC) and the string goes on: the later operations run first, the event after the last one (0x416703).
        var combat = new Combat();
        var ports = new Ports();
        var conversation = Selected("lf 3 1, co, lf 4 1, co", ports, combat);
        CollectionAssert.AreEqual(new[] { "LF:3:1:900", "LF:4:1:900" }, ports.Commands);
        Assert.AreEqual(1, combat.Started.Count);
        Assert.IsInstanceOfType<StartCombatCommand>(conversation.Effects[^1].Command, "the event is the last effect of the string");
        Assert.AreEqual(3, conversation.Effects.Count(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
    }

    [TestMethod]
    public void The_operand_is_read_and_ignored_as_the_source_reads_it()
    {
        // The scanner reads a number after every code (DS1) and the co case ignores it.
        foreach (var action in new[] { "co", "CO", "co 0", "co 5", "co5", "co,", "co ", "co fl 40", "fl 40 co" })
        {
            var combat = new Combat();
            var conversation = Selected(action, new Ports(), combat);
            Assert.AreEqual(1, combat.Started.Count, action);
            Assert.IsNull(conversation.Failure, action);
        }
    }

    [TestMethod]
    public void A_final_line_in_the_string_speaks_before_the_attack_event()
    {
        // fl enters the final line at once (0x413A30, its node's action runs) and the event follows at the string's end, whatever the order.
        foreach (var action in new[] { "co, fl 40", "fl 40, co", "fl 40 ,co", "co, fl 40, co" })
        {
            var combat = new Combat();
            var ports = new Ports();
            var conversation = Selected(action, ports, combat);
            Assert.IsNull(conversation.Failure, action);
            Assert.AreEqual(DialogEndKind.NpcFinalLine, conversation.End!.Kind, action);
            Assert.AreEqual("Farewell.", conversation.NpcLine!.Text, action);
            CollectionAssert.AreEqual(new[] { "QU:1005:2:900" }, ports.Commands, action);
            Assert.AreEqual(1, combat.Started.Count, action);
            Assert.IsInstanceOfType<StartCombatCommand>(conversation.Effects[^1].Command, action + ": the event follows the final line's action");
        }
    }

    [TestMethod]
    public void An_npc_line_with_co_runs_the_event_at_its_own_string_end()
    {
        var combat = new Combat();
        var ports = new Ports();
        var document = Read(Record("10", "Die!", action: "lf 3 1, co") + Record("11", "Wait.", iq: "1", response: "0"));
        var conversation = Start(document, ports, Owner(), combat: combat);
        Assert.IsNull(conversation.Failure);
        Assert.AreEqual(1, combat.Started.Count);
        CollectionAssert.AreEqual(new[] { "LF:3:1:900" }, ports.Commands);
        Assert.IsInstanceOfType<StartCombatCommand>(conversation.Effects[^1].Command);
    }

    [TestMethod]
    public void Co_needs_the_port_and_nothing_else_follows_a_dialog_transfer()
    {
        // Unadmitted without a port.
        var plain = Start(Read(Record("10", "?") + Record("11", "Attack.", iq: "1", response: "0", action: "co")), new Ports(), Owner());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, plain.Options[0].Block!.Kind);
        // After a final line only a co is kept (the original goes on with any operation; the tail is outside the subset).
        foreach (var action in new[] { "fl 40, lf 3 1", "fl 40, co, lf 3 1", "fl 40, fl 40, co", "jo 0 30, co", "uw 0 30, co" })
        {
            var document = Read(Record("10", "?") + Record("11", "Attack.", iq: "1", response: "0", action: action) + Record("30", "Hm.") + Record("40", "Bye."));
            var conversation = Start(document, new Ports(), Owner(), world: new World(), party: null, combat: new Combat());
            Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, conversation.Options[0].Block?.Kind, action);
        }
    }

    [TestMethod]
    public void A_rejecting_or_throwing_port_fails_the_conversation_and_a_failed_string_runs_no_event()
    {
        var rejected = Selected("co", new Ports(), null, new Combat { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);
        var thrown = Selected("co", new Ports(), null, new Throwing());
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, thrown.Effects.Last().Outcome);
        // An operation before the co that the owner rejects stops the string: the event does not run.
        var combat = new Combat();
        var ports = new Ports { RejectAt = 1 };
        var failed = Selected("lf 3 1, co", ports, combat);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, failed.Failure!.Kind);
        Assert.AreEqual(0, combat.Started.Count);
    }

    private sealed class Throwing : IDialogCombatPort
    {
        public DialogCommandResult Execute(StartCombatCommand command) => throw new InvalidOperationException();
    }
}
