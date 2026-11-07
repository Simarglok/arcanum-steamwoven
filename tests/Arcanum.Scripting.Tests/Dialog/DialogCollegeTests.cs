using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The sc condition through the college port (ARC-DEV-197, ledger CK1–CK3). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogCollegeTests
{
    internal sealed class Colleges(Dictionary<int, int?> ranks) : IDialogCollegePort
    {
        public List<DialogCollegeQuery> Asked { get; } = [];
        public bool Throw { get; init; }
        public int? Rank(DialogCollegeQuery query)
        {
            if (Throw) throw new InvalidOperationException("colleges");
            Asked.Add(query);
            return ranks.TryGetValue(query.College, out var rank) ? rank : null;
        }
    }

    private static bool Offered(string condition, IDialogCollegePort? port, out DialogConversation conversation, bool expectOk = true)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        conversation = Start(document, new Ports(), Owner(), colleges: port);
        if (expectOk) Assert.IsNull(conversation.Failure, condition);
        return conversation.Frontier is null && conversation.Options.All(option => option.Block is null) && conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void A_positive_threshold_is_a_minimum_and_zero_or_below_a_maximum_of_its_negation()
    {
        // The branch 0x415AC7: v above 0 passes at rank >= v; otherwise v is negated and the rank must not exceed it (so v = 0 asks for rank 0).
        foreach (var rank in new[] { 0, 1, 2, 3, 5 })
        {
            var port = new Colleges(new() { [4] = rank });
            foreach (var v in new[] { -5, -2, -1, 0, 1, 2, 3, 5, 6 })
                Assert.AreEqual(v > 0 ? rank >= v : rank <= -v, Offered($"sc 4 {v}", port, out _), $"rank {rank}, sc 4 {v}");
        }
        Assert.IsTrue(Offered("SC 4 2", new Colleges(new() { [4] = 2 }), out _), "a case is not distinguished");
        Assert.IsTrue(Offered("sc 0 0, sc 15 1", new Colleges(new() { [0] = 0, [15] = 1 }), out _), "a field is a conjunction, and the college 0 and 15 are the first and last");
        Assert.IsFalse(Offered("sc 0 0, sc 15 2", new Colleges(new() { [0] = 0, [15] = 1 }), out _));
    }

    [TestMethod]
    public void A_missing_second_operand_reads_zero_as_sk_does_and_the_port_gets_the_pc_and_the_college()
    {
        var port = new Colleges(new() { [3] = 0 });
        Assert.IsTrue(Offered("sc 3", port, out _), "v = 0: rank must be 0");
        Assert.AreEqual(new DialogCollegeQuery(new DialogParticipantId(800), 3), port.Asked[0]);
        Assert.IsFalse(Offered("sc 3", new Colleges(new() { [3] = 1 }), out _));
    }

    [TestMethod]
    public void A_college_beyond_the_sixteen_a_plus_sign_and_the_most_negative_operand_are_outside_the_subset_or_damage()
    {
        var port = new Colleges(new() { [4] = 1 });
        foreach (var field in new[] { "sc 16 1", "sc 99 0" })
        {
            Assert.IsFalse(Offered(field, port, out var conversation), field);
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier?.Kind, field);
        }
        foreach (var field in new[] { "sc -1 1", "sc +4 1", "sc 4 +1", "sc 4 -2147483648" })
        {
            Assert.IsFalse(Offered(field, port, out var damage, expectOk: false), field);
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, damage.Failure?.Kind, field);
        }
        Offered("sc 4 1", null, out var without);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, without.Frontier?.Kind, "without the port");
        // The companion port alone does not admit the condition sc (it admits the action of that name).
        var companionOnly = Start(Read(Record("10", "?") + Record("11", "Ask.", iq: "1", condition: "sc 4 1", response: "0")), new Ports(), Owner(), companion: new DialogCompanionTests.Companion());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, companionOnly.Frontier?.Kind, "companion port only");
        // The extreme thresholds neither overflow nor wrap.
        Assert.IsFalse(Offered("sc 4 2147483647", port, out _));
        Assert.IsTrue(Offered("sc 4 -2147483647", port, out _));
    }

    [TestMethod]
    public void A_throwing_port_or_a_rank_the_owner_does_not_hold_fails_the_conversation_and_the_sc_action_is_the_companions()
    {
        Offered("sc 4 1", new Colleges([]) { Throw = true }, out var thrown, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, thrown.Failure!.Kind);
        Offered("sc 4 1", new Colleges([]), out var invalid, expectOk: false);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, invalid.Failure!.Kind);
        // The college port does not admit the action sc (clear spread-out, the companion port's).
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: "sc")), new Ports(), Owner(), colleges: new Colleges([])).Frontier!.Kind);
    }
}
