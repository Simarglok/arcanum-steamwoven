using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-WORLD-044: dialog RP through the reputation port (ledger RP8): as a condition n > 0 holds n and n ≤ 0 lacks −n;
/// as an action n > 0 adds n and n ≤ 0 removes −n; a skip is recorded and the dialog goes on.</summary>
[TestClass]
public sealed class DialogReputationTests
{
    [TestMethod]
    public void Rp_conditions_test_held_and_lacked_reputations()
    {
        var document = Read(Record("10", "?")
            + Record("11", "has", iq: "1", condition: "RP 1001", response: "0")
            + Record("12", "lacks", iq: "1", condition: "rp -1001", response: "0")
            + Record("13", "lacks other", iq: "1", condition: "RP -1002", response: "0")
            + Record("14", "zero", iq: "1", condition: "RP 0", response: "0"));
        var held = new Reputations { Held = { 1001 } };
        CollectionAssert.AreEqual(new[] { "has", "lacks other", "zero" }, StartWith(document, new Ports(), held).Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 1001, 1001, 1002, 0 }, held.Queries.ToArray(), "RP 0 asks for 0, which is never held");
        CollectionAssert.AreEqual(new[] { "lacks", "lacks other", "zero" }, StartWith(document, new Ports(), new Reputations()).Options.Select(o => o.Text).ToArray());
    }

    [TestMethod]
    public void Rp_actions_add_and_remove_in_action_order_and_a_skip_goes_on()
    {
        var document = Read(Record("10", "?", action: "RP 1001, QU 1 1, rp -1001, RP 2, RP -3")
            + Record("11", "x", iq: "1", response: "0", action: "RP 1500"));
        var ports = new Ports();
        var reputations = new Reputations { SkipBelow = 1000 };
        var conversation = StartWith(document, ports, reputations);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { "+1001", "-1001", "+2", "-3" }, reputations.Commands.ToArray());
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, ports.Commands);
        CollectionAssert.AreEqual(new[] { DialogCommandAttemptOutcome.Applied, DialogCommandAttemptOutcome.Applied, DialogCommandAttemptOutcome.Applied,
            DialogCommandAttemptOutcome.Skipped, DialogCommandAttemptOutcome.Skipped }, conversation.Effects.Select(e => e.Outcome).ToArray());
        Assert.AreEqual(new ReputationCommand(new DialogParticipantId(800), 1001, true), conversation.Effects[0].Command);
        Assert.AreEqual(new ReputationCommand(new DialogParticipantId(800), 1001, false), conversation.Effects[2].Command);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual("+1500", reputations.Commands[^1]);
    }

    [TestMethod]
    public void A_refused_or_throwing_rp_fails_and_without_the_port_rp_is_unadmitted()
    {
        var document = Read(Record("10", "?", action: "RP 1001") + Record("11", "x", iq: "1", response: "0"));
        var refused = StartWith(document, new Ports(), new Reputations { Reject = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, refused.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, refused.Effects.Single().Outcome);
        var thrown = StartWith(document, new Ports(), new Reputations { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);

        var without = StartWith(document, new Ports(), null);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, without.Frontier!.Kind);
        var condition = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RP 1001", response: "0")), new Ports(), null);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, condition.Frontier!.Kind);
        var throwing = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RP 1001", response: "0")), new Ports(), new Reputations { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.QueryException, throwing.Failure!.Kind);
        var signed = StartWith(Read(Record("10", "?", action: "RP +1001") + Record("11", "x", iq: "1", response: "0")), new Ports(), new Reputations());
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, signed.Failure!.Kind);
    }

    private static DialogConversation StartWith(DlgDocument document, Ports ports, IDialogReputationPort? reputations) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), Reputations: reputations));

    private sealed class Reputations : IDialogReputationPort
    {
        public HashSet<int> Held { get; } = [];
        public List<int> Queries { get; } = [];
        public List<string> Commands { get; } = [];
        public int? SkipBelow { get; init; }
        public bool Reject { get; init; }
        public bool Throw { get; init; }

        public bool HasReputation(DialogReputationQuery query)
        {
            if (Throw) throw new InvalidOperationException("Synthetic query failure.");
            Assert.AreEqual(new DialogParticipantId(800), query.PlayerCharacter);
            Queries.Add(query.Reputation);
            return Held.Contains(query.Reputation);
        }

        public DialogReputationResult Execute(ReputationCommand command)
        {
            if (Throw) throw new InvalidOperationException("Synthetic command failure.");
            if (Reject) return DialogReputationResult.Rejected;
            Commands.Add((command.Add ? "+" : "-") + command.Reputation);
            if (command.Reputation < SkipBelow) return DialogReputationResult.Skipped;
            if (command.Add) Held.Add(command.Reputation); else Held.Remove(command.Reputation);
            return DialogReputationResult.Applied;
        }
    }
}
