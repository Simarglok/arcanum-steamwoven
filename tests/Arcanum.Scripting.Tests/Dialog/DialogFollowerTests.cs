using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The pa condition through the follower name port and the nk action through the kill port (ARC-DEV-227, ledger PN1–PN4). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogFollowerTests
{
    internal sealed class Followers(params int[] names) : IDialogFollowerNamePort
    {
        public List<DialogFollowerNameQuery> Asked { get; } = [];
        public bool Throw { get; init; }
        public bool HasFollowerNamed(DialogFollowerNameQuery query)
        {
            if (Throw) throw new InvalidOperationException("followers");
            Asked.Add(query);
            return names.Contains(query.Name);
        }
    }

    internal sealed class Killer : IDialogKillPort
    {
        public List<KillNpcCommand> Commands { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public DialogCommandResult Execute(KillNpcCommand command)
        {
            if (Throw) throw new InvalidOperationException("kill");
            Commands.Add(command);
            return Result;
        }
    }

    private static bool Offered(string condition, IDialogFollowerNamePort? port, out DialogConversation conversation, bool expectOk = true)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        conversation = Start(document, new Ports(), Owner(), followerNames: port);
        if (expectOk) Assert.IsNull(conversation.Failure, condition);
        return conversation.Frontier is null && conversation.Options.All(option => option.Block is null) && conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void A_non_negative_operand_asks_for_a_follower_with_that_name_and_a_negative_one_for_none()
    {
        var port = new Followers(6014, 8);
        Assert.IsTrue(Offered("pa 6014", port, out _));
        Assert.IsFalse(Offered("pa 7", port, out _));
        Assert.IsFalse(Offered("pa -6014", port, out _), "negative: passes when there is none");
        Assert.IsTrue(Offered("PA -7", port, out _));
        Assert.IsTrue(Offered("pa 0, pa 8", new Followers(0, 8), out _), "a field is a conjunction; the name 0 is a name");
        Assert.IsTrue(Offered("pa -0", new Followers(0), out _), "-0 is 0, not a negation");
        // The PC (800) is asked about the magnitude of the operand.
        port.Asked.Clear();
        Offered("pa -6014", port, out _);
        Assert.AreEqual(new DialogFollowerNameQuery(new DialogParticipantId(800), 6014), port.Asked.Single());
    }

    [TestMethod]
    public void A_plus_sign_the_most_negative_operand_a_missing_operand_and_a_missing_port_are_damage_or_outside_the_subset()
    {
        var port = new Followers(1);
        foreach (var field in new[] { "pa +1", "pa -2147483648", "pa" })
        {
            Assert.IsFalse(Offered(field, port, out var damage, expectOk: false), field);
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, damage.Failure?.Kind, field);
        }
        Offered("pa 1", null, out var without);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, without.Frontier?.Kind);
        // PA is no action, and the kill port alone does not admit it.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "pa 1")), new Ports(), Owner(), followerNames: port).Frontier!.Kind);
        var killOnly = Start(Read(Record("10", "?") + Record("11", "Ask.", iq: "1", condition: "pa 1", response: "0")), new Ports(), Owner(), kill: new Killer());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, killOnly.Frontier?.Kind);
    }

    [TestMethod]
    public void A_throwing_port_fails_the_conversation()
    {
        Offered("pa 1", new Followers(1) { Throw = true }, out var conversation, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind);
    }

    [TestMethod]
    public void Nk_kills_the_npc_in_field_order_in_an_npc_line_and_in_a_reply_and_the_talk_goes_on()
    {
        var killer = new Killer();
        var ports = new Ports();
        var line = Start(Read(Record("10", "Hm.", action: "lf 1 1, nk, lf 2 1") + Record("11", "Ok.", iq: "1", response: "0")), ports, Owner(), kill: killer);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, line.Phase, "the dialog is not ended by the kill");
        Assert.AreEqual(new KillNpcCommand(new DialogParticipantId(900), new DialogParticipantId(800)), killer.Commands.Single());
        CollectionAssert.AreEqual(new[] { "LF:1:1:900", "LF:2:1:900" }, ports.Commands, "the other actions run around it");

        var replyKiller = new Killer();
        var reply = Start(Read(Record("10", "?") + Record("11", "Die.", iq: "1", response: "0", action: "NK")), new Ports(), Owner(), kill: replyKiller);
        Assert.AreEqual(0, replyKiller.Commands.Count, "nothing before the reply is picked");
        reply.Select(reply.Options[0].Token);
        Assert.AreEqual(1, replyKiller.Commands.Count);
    }

    [TestMethod]
    public void Nk_takes_no_operand_needs_its_port_and_a_refusal_or_exception_fails_the_conversation()
    {
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "nk 1")), new Ports(), Owner(), kill: new Killer()).Frontier!.Kind, "a stray operand");
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "nk")), new Ports(), Owner()).Frontier!.Kind, "without the port");
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "?") + Record("11", "Ask.", iq: "1", condition: "nk", response: "0")), new Ports(), Owner(), kill: new Killer()).Frontier?.Kind, "no condition");

        var rejected = Start(Read(Record("10", "Hm.", action: "nk")), new Ports(), Owner(), kill: new Killer { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Last().Outcome);
        var thrown = Start(Read(Record("10", "Hm.", action: "nk")), new Ports(), Owner(), kill: new Killer { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
    }
}
