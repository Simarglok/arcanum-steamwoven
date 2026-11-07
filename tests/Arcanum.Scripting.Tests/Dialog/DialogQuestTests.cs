using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The quest log conditions qu, qa and qb through the quest port (ARC-DEV-180, ledger DCC4). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogQuestTests
{
    internal sealed class Quests(Dictionary<int, int> states) : IDialogQuestPort
    {
        public List<(long Pc, int Quest)> Asked { get; } = [];
        public int State(DialogQuestQuery query)
        {
            Asked.Add((query.PlayerCharacter.Value, query.Quest));
            return states.GetValueOrDefault(query.Quest);
        }
    }

    // Whether the one reply behind the condition is offered: a second reply with no condition is always there, so the NPC line never dead-ends.
    private static bool Offered(int state, string condition, Quests? quests = null)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        var conversation = Start(document, new Ports(), Owner(), quests: quests ?? new Quests(new() { [1005] = state }));
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        Assert.IsTrue(conversation.Options.All(option => option.Block is null), condition);
        return conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void The_three_conditions_compare_the_state_with_the_second_operand()
    {
        // qu: equal (0x4152D8 jne), qa: at least (0x41584E jl), qb: at most (0x41573D jg); signed compares (DCC4).
        foreach (var state in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            Assert.AreEqual(state == 3, Offered(state, "qu1005 3"), $"qu state {state}");
            Assert.AreEqual(state >= 3, Offered(state, "qa1005 3"), $"qa state {state}");
            Assert.AreEqual(state <= 3, Offered(state, "qb1005 3"), $"qb state {state}");
        }
        Assert.IsTrue(Offered(0, "qb1005 0"), "an untouched quest is at most Unknown");
        Assert.IsFalse(Offered(0, "qb1005 -1"), "a negative bound is below every state");
        Assert.IsTrue(Offered(0, "qa1005 -1"));
        Assert.IsFalse(Offered(0, "qu1005 -1"));
    }

    [TestMethod]
    public void The_second_operand_is_read_as_the_source_reads_it()
    {
        // 0x4167C0 (DS3): spaces before the second operand are optional and a missing one reads 0; a case is not distinguished.
        Assert.IsTrue(Offered(4, "qa1005 4"));
        Assert.IsTrue(Offered(4, "QA1005 4"));
        Assert.IsTrue(Offered(4, "qa 1005 4"));
        Assert.IsTrue(Offered(4, "qa1005   4"));
        Assert.IsTrue(Offered(0, "qu1005"), "a missing second operand is 0");
        Assert.IsFalse(Offered(2, "qu1005"));
        Assert.IsTrue(Offered(4, "qa1005 4, qb1005 4, qu1005 4"), "the operations of a field are a conjunction");
        Assert.IsFalse(Offered(4, "qa1005 4, qb1005 3"));
    }

    [TestMethod]
    public void The_port_is_asked_for_the_pc_and_the_quest()
    {
        var quests = new Quests(new() { [1005] = 2 });
        Assert.IsTrue(Offered(2, "qu1005 2", quests));
        CollectionAssert.AreEqual(new[] { (800L, 1005) }, quests.Asked, "the dialog's PC and the quest number");
    }

    [TestMethod]
    public void A_quest_number_outside_the_quests_is_outside_the_subset()
    {
        // The getter indexes its tables from 1000 without a range test (0x4C51A0): valid data, but no state the session can answer.
        foreach (var field in new[] { "qu999 1", "qa2000 1", "qb0 1", "qu1 1", "qa-1005 1" })
        {
            var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: field, response: "0"));
            var conversation = Start(gate, new Ports(), Owner(), quests: new Quests([]));
            Assert.IsTrue(conversation.Frontier is not null || conversation.Failure is not null || conversation.Options.Any(o => o.Block is not null), field);
        }
        Assert.IsTrue(Offered(0, "qu1000 0", new Quests([])));
        Assert.IsTrue(Offered(0, "qu1999 0", new Quests([])));
    }

    [TestMethod]
    public void The_conditions_need_the_quest_port_and_the_action_keeps_its_own_rules()
    {
        var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: "qu1005 2", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(gate, new Ports(), Owner()).Frontier!.Kind, "unadmitted without a quest port");
        // The QU action is admitted without the port and still needs its two operands; qa and qb are not actions.
        var action = Start(Read(Record("10", "?", action: "qu 1005 2")), new Ports(), Owner(), quests: new Quests([]));
        Assert.IsNull(action.Failure);
        Assert.IsNull(action.Frontier);
        var notAction = Start(Read(Record("10", "?", action: "qa 1005 2")), new Ports(), Owner(), quests: new Quests([]));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, notAction.Frontier!.Kind);
    }

    [TestMethod]
    public void A_throwing_port_fails_the_conversation()
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: "qu1005 2", response: "0"));
        var conversation = Start(document, new Ports(), Owner(), quests: new Throwing());
        Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind);
    }

    private sealed class Throwing : IDialogQuestPort
    {
        public int State(DialogQuestQuery query) => throw new InvalidOperationException();
    }
}
