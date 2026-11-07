using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// WA and UW through the party port (ARC-WORLD-013). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogPartyTests
{
    private static readonly DialogParticipantId Pc = new(800);
    private static readonly DialogParticipantId Npc = new(900);

    [TestMethod]
    public void Wa_selects_replies_by_the_waiting_state()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "Come along again.", iq: "1", condition: "WA 1", response: "0")
            + Record("12", "Wait here.", iq: "1", condition: "wa 0", response: "0"));
        var party = new Party { Waiting = true };
        var conversation = Start(document, new Ports(), Owner(), party: party);
        Assert.AreEqual("Come along again.", conversation.Options.Single().Text);
        CollectionAssert.AreEqual(new[] { new DialogPartyQuery(Npc, Pc), new DialogPartyQuery(Npc, Pc) }, party.Queries, "one read per WA reply");

        party.Waiting = false;
        conversation.RefreshOptions();
        Assert.AreEqual("Wait here.", conversation.Options.Single().Text);
    }

    [TestMethod]
    public void Uw_continues_at_its_node_and_a_refusal_is_a_frontier()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "Follow me.", iq: "1", condition: "WA 1", response: "40", action: "LF 1 1, UW 0 30")
            + Record("30", "Again together.", action: "MM 2")
            + Record("40", "unused"));
        var party = new Party { Waiting = true };
        var world = new World();
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), world: world, party: party);
        var option = conversation.Options.Single();
        Assert.AreEqual(DialogOptionTransition.Join, option.Transition);
        Assert.AreEqual(new DialogNodeId(30), option.Target);

        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(option.Token).Status);
        Assert.AreEqual("Again together.", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "UW:900>800" }, party.Commands);
        CollectionAssert.AreEqual(new[] { "MM:2" }, world.Commands);
        CollectionAssert.AreEqual(new[] { "LF:1:1:900" }, ports.Commands);
        Assert.AreEqual(new UnwaitCommand(Npc, Pc), conversation.Effects[1].Command);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, conversation.Effects[1].Outcome);

        var refused = Start(document, new Ports(), Owner(), world: new World(), party: new Party { Waiting = true, Result = DialogJoinResult.Refused });
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, refused.Select(refused.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.JoinRefused, refused.Frontier!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, refused.Effects.Last().Outcome);

        var rejected = Start(document, new Ports(), Owner(), world: new World(), party: new Party { Waiting = true, Result = DialogJoinResult.Rejected });
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
    }

    [TestMethod]
    public void A_party_port_takes_jo_from_the_world_port()
    {
        var document = Read(Record("10", "?") + Record("11", "Come.", iq: "1", response: "0", action: "JO 0 30") + Record("30", "Together."));
        var party = new Party();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, party: party);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(new DialogNodeId(30), conversation.CurrentNode);
        CollectionAssert.AreEqual(new[] { "JO:900>800" }, party.Commands);
        Assert.AreEqual(0, world.Commands.Count);

        // JO needs only one of the two ports.
        var partyOnly = Start(document, new Ports(), Owner(), party: new Party());
        Assert.IsNull(partyOnly.Options[0].Block);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, partyOnly.Select(partyOnly.Options[0].Token).Status);
    }

    [TestMethod]
    public void Without_a_party_port_wa_and_uw_stay_unadmitted()
    {
        var uw = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: "UW 0 30") + Record("30", "!"));
        var blocked = Start(uw, new Ports(), Owner(), world: new World());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, blocked.Options[0].Block!.Kind);

        var wa = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "WA 1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(wa, new Ports(), Owner(), world: new World()).Frontier!.Kind);
    }

    [TestMethod]
    public void Unadmitted_party_forms_are_blocked_or_invalid()
    {
        foreach (var (action, kind) in new[]
        {
            ("UW 1 30", DialogOptionBlockKind.UnsupportedOperation),
            ("UW 0 30, MM 1", DialogOptionBlockKind.UnsupportedOperation),
            ("JO 0 30, UW 0 30", DialogOptionBlockKind.UnsupportedOperation),
            ("UW 0,30", DialogOptionBlockKind.UnsupportedOperation),
            ("UW 0 99", DialogOptionBlockKind.TargetInvalid),
            ("UW 0 11", DialogOptionBlockKind.TargetInvalid),
            ("WA 1", DialogOptionBlockKind.UnsupportedOperation),
        })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: action) + Record("30", "!"));
            var party = new Party();
            var conversation = Start(document, new Ports(), Owner(), world: new World(), party: party);
            Assert.AreEqual(kind, conversation.Options[0].Block?.Kind, action);
            Assert.AreEqual(0, party.Commands.Count, action);
        }
        foreach (var condition in new[] { "WA 2", "WA -1", "WA" })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0"));
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, Start(document, new Ports(), Owner(), party: new Party()).Failure!.Kind, condition);
        }
        var npcUnwait = Start(Read(Record("10", "?", action: "UW 0 10") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner(), party: new Party());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, npcUnwait.Frontier!.Kind);
    }

    [TestMethod]
    public void A_throwing_party_port_fails_the_conversation()
    {
        var wa = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "WA 1", response: "0"));
        var failed = Start(wa, new Ports(), Owner(), party: new Party { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.QueryException, failed.Failure!.Kind);

        var uw = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: "UW 0 30") + Record("30", "!"));
        var party = new Party();
        var conversation = Start(uw, new Ports(), Owner(), party: party);
        party.Throw = true;
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, conversation.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, conversation.Effects.Single().Outcome);
    }

    internal sealed class Party : IDialogPartyPort
    {
        public bool Waiting { get; set; }
        public bool Throw { get; set; }
        public DialogJoinResult Result { get; init; } = DialogJoinResult.Accepted;
        public List<DialogPartyQuery> Queries { get; } = [];
        public List<string> Commands { get; } = [];

        public bool IsWaiting(DialogPartyQuery query)
        {
            if (Throw) throw new InvalidOperationException("party");
            Queries.Add(query);
            return Waiting;
        }

        public DialogJoinResult Execute(JoinCommand command) => Run($"JO:{command.Npc.Value}>{command.PlayerCharacter.Value}");
        public DialogJoinResult Execute(UnwaitCommand command) => Run($"UW:{command.Npc.Value}>{command.PlayerCharacter.Value}");

        private DialogJoinResult Run(string command)
        {
            if (Throw) throw new InvalidOperationException("party");
            Commands.Add(command);
            return Result;
        }
    }
}
