using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The item conditions in and ni through the item-holding port (ARC-DEV-194, ledger DCC6, IH1–IH3). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogItemHoldTests
{
    internal sealed class Holds(HashSet<int>? party = null, HashSet<int>? npc = null) : IDialogItemHoldPort
    {
        public List<(string Side, DialogItemQuery Query)> Asked { get; } = [];
        public bool PartyHolds(DialogItemQuery query)
        {
            Asked.Add(("party", query));
            return party?.Contains(query.ItemName) == true;
        }
        public bool NpcHolds(DialogItemQuery query)
        {
            Asked.Add(("npc", query));
            return npc?.Contains(query.ItemName) == true;
        }
    }

    private sealed class Throwing : IDialogItemHoldPort
    {
        public bool PartyHolds(DialogItemQuery query) => throw new InvalidOperationException();
        public bool NpcHolds(DialogItemQuery query) => throw new InvalidOperationException();
    }

    // Whether the one reply behind the condition is offered (a second reply with no condition keeps the NPC line from dead-ending).
    private static bool Offered(string condition, IDialogItemHoldPort? port)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        var conversation = Start(document, new Ports(), Owner(), itemHolds: port);
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        Assert.IsTrue(conversation.Options.All(option => option.Block is null), condition);
        return conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void A_non_negative_in_asks_the_pc_and_its_followers_and_ni_is_its_negation()
    {
        var holds = new Holds(party: [5], npc: [9]);
        Assert.IsTrue(Offered("in 5", holds));
        Assert.IsFalse(Offered("in 6", holds));
        Assert.IsFalse(Offered("ni 5", holds));
        Assert.IsTrue(Offered("ni 6", holds));
        Assert.IsFalse(Offered("in 9", holds), "the NPC's own item is not the party's");
        Assert.IsTrue(Offered("IN 5", holds), "a case is not distinguished");
        Assert.IsTrue(holds.Asked.All(ask => ask.Side == "party"));
        // The dialog's NPC (900) and PC (800) and the name asked for.
        Assert.AreEqual(new DialogItemQuery(new DialogParticipantId(900), new DialogParticipantId(800), 5), holds.Asked[0].Query);
    }

    [TestMethod]
    public void A_negative_in_asks_the_npc_and_its_substitute_for_the_positive_name_and_ni_is_its_negation()
    {
        var holds = new Holds(party: [5], npc: [9]);
        Assert.IsTrue(Offered("in -9", holds));
        Assert.IsFalse(Offered("in -5", holds), "the PC's item is not the NPC's");
        Assert.IsFalse(Offered("ni -9", holds));
        Assert.IsTrue(Offered("ni -5", holds));
        Assert.IsTrue(holds.Asked.All(ask => ask.Side == "npc"));
        CollectionAssert.AreEqual(new[] { 9, 5, 9, 5 }, holds.Asked.Select(ask => ask.Query.ItemName).ToArray());
    }

    [TestMethod]
    public void Zero_is_the_party_side_and_a_field_is_a_conjunction()
    {
        var holds = new Holds(party: [0, 5], npc: [9]);
        Assert.IsTrue(Offered("in 0", holds), "n = 0 is not negative: the party side, name 0");
        Assert.AreEqual("party", holds.Asked[0].Side);
        Assert.IsTrue(Offered("in 5, in -9, ni 7", holds));
        Assert.IsFalse(Offered("in 5, in -8", holds));
    }

    [TestMethod]
    public void A_plus_sign_and_the_most_negative_operand_are_damage()
    {
        foreach (var condition in new[] { "in +5", "ni +5", "in -2147483648", "ni -2147483648" })
        {
            var document = Read(Record("10", "?") + Record("11", "Ask.", iq: "1", condition: condition, response: "0"));
            var conversation = Start(document, new Ports(), Owner(), itemHolds: new Holds());
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, conversation.Failure?.Kind, condition);
        }
    }

    [TestMethod]
    public void A_throwing_port_fails_and_the_codes_need_the_port_while_the_in_action_is_unchanged()
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: "in 5", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start(document, new Ports(), Owner(), itemHolds: new Throwing()).Failure!.Kind);
        foreach (var code in new[] { "in 5", "in -5", "ni 5", "ni -5" })
        {
            var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: code, response: "0"));
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(gate, new Ports(), Owner()).Frontier!.Kind, code + " unadmitted without the port");
        }
        // The action IN keeps its own rules (negative: the NPC gives, through the inventory or world port); ni is not an action.
        var give = Start(Read(Record("10", "Hm.", action: "in -5")), new Ports(), Owner(), world: new World(), itemHolds: new Holds());
        Assert.IsNull(give.Failure);
        Assert.IsNull(give.Frontier);
        foreach (var action in new[] { "in 5", "ni 5" })
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: action)), new Ports(), Owner(), world: new World(), itemHolds: new Holds()).Frontier!.Kind, action);
    }
}
