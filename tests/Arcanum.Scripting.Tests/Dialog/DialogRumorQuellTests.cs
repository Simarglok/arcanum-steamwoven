using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-UI-008: the RQ action quells a rumor through the rumor port, in action order; without the port it is a
/// static frontier. As a condition RQ is the quelled test of ARC-DEV-171 (DialogRumorConditionTests).</summary>
[TestClass]
public sealed class DialogRumorQuellTests
{
    [TestMethod]
    public void Rq_quells_through_the_rumor_port_in_action_order()
    {
        var document = Read(Record("10", "?", action: "RU 1001, rq 1001; QU 1 2") + Record("11", "Bye.", iq: "1", response: "0", action: "RQ 2999"));
        var ports = new Ports();
        var rumors = new Rumors(ports);
        var conversation = StartWith(document, ports, rumors);
        CollectionAssert.AreEqual(new[] { "RU:1001", "RQ:1001", "QU:1:2:900" }, ports.Commands);
        Assert.IsInstanceOfType<QuellRumorCommand>(conversation.Effects[1].Command);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects[1].Outcome);
        conversation.Select(conversation.Options[0].Token);
        CollectionAssert.AreEqual(new[] { "RU:1001", "RQ:1001", "QU:1:2:900", "RQ:2999" }, ports.Commands);
    }

    [TestMethod]
    public void Without_the_port_rq_is_a_static_frontier_and_a_refusal_stops_the_conversation()
    {
        var document = Read(Record("10", "?", action: "RU 1001, RQ 1001") + Record("11", "x", iq: "1", response: "0"));
        var ports = new Ports();
        var unavailable = StartWith(document, ports, null);
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, unavailable.Frontier!.Kind);
        Assert.AreEqual(1, unavailable.Frontier.Source!.Value.OperationOrdinal);
        Assert.AreEqual(0, ports.Commands.Count, "A static frontier precedes every effect.");

        var refusing = new Ports();
        var refused = StartWith(document, refusing, new Rumors(refusing) { Reject = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, refused.Failure!.Kind);
        CollectionAssert.AreEqual(new[] { "RU:1001" }, refusing.Commands);
    }

    [TestMethod]
    public void Rq_as_an_action_needs_an_unsigned_rumor_number()
    {
        var low = StartWith(Read(Record("10", "?", action: "RQ 999") + Record("11", "x", iq: "1", response: "0")), new Ports(), new Rumors(new Ports()));
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, low.Failure!.Kind);
        var signed = StartWith(Read(Record("10", "?", action: "RQ -1001") + Record("11", "x", iq: "1", response: "0")), new Ports(), new Rumors(new Ports()));
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, signed.Failure!.Kind);
    }

    private static DialogConversation StartWith(DlgDocument document, Ports ports, IDialogRumorPort? rumors) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), Rumors: rumors));

    private sealed class Rumors(Ports log) : IDialogRumorPort
    {
        public bool Reject { get; init; }

        public DialogCommandResult Execute(QuellRumorCommand command)
        {
            if (Reject) return DialogCommandResult.Rejected;
            log.Commands.Add($"RQ:{command.RumorId}");
            return DialogCommandResult.Applied;
        }

        public bool IsKnown(DialogRumorQuery query) => false;

        public bool IsQuelled(int rumor) => false;
    }
}
