using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The PC stat conditions ch, pe, le, al, ma, ta and na through the stat port (ARC-DEV-192, ledger DCC2, DCC3). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogStatTests
{
    internal sealed class Stats(Dictionary<int, int?> values) : IDialogStatPort
    {
        public List<DialogStatQuery> Asked { get; } = [];
        public int? Stat(DialogStatQuery query)
        {
            Asked.Add(query);
            return values.TryGetValue(query.Stat, out var value) ? value : null;
        }
    }

    // Whether the one reply behind the condition is offered: a second reply with no condition is always there, so the NPC line never dead-ends.
    private static bool Offered(string condition, Dictionary<int, int?> values, Stats? stats = null)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        var conversation = Start(document, new Ports(), Owner(), stats: stats ?? new Stats(values));
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        Assert.IsTrue(conversation.Options.All(option => option.Block is null), condition);
        return conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void Ch_pe_le_al_and_ma_follow_the_signed_rule_on_their_stat()
    {
        // The stat numbers of the getter 0x4B0490: 7 charisma, 5 perception, 17 level, 19 alignment, 16 aptitude (cases 1, 2, 20, 3, 4).
        foreach (var (code, stat) in new[] { ("ch", 7), ("pe", 5), ("le", 17), ("al", 19), ("ma", 16) })
        {
            foreach (var value in new[] { -20, -5, -4, 0, 4, 5, 6, 20 })
            {
                var values = new Dictionary<int, int?> { [stat] = value };
                Assert.AreEqual(value >= 5, Offered($"{code} 5", values), $"{code} {value}, n = 5");
                Assert.AreEqual(value <= 5, Offered($"{code} -5", values), $"{code} {value}, n = -5");
                Assert.AreEqual(value >= 0, Offered($"{code} 0", values), $"{code} {value}, n = 0");
            }
        }
        Assert.IsTrue(Offered("CH 5", new() { [7] = 5 }), "a case is not distinguished");
        Assert.IsTrue(Offered("ch5, le 2, al -50", new() { [7] = 9, [17] = 3, [19] = -100 }), "a field is a conjunction");
        Assert.IsFalse(Offered("ch5, le 2, al -50", new() { [7] = 9, [17] = 3, [19] = 100 }));
    }

    [TestMethod]
    public void Ta_is_the_signed_rule_on_the_negated_aptitude()
    {
        // 0x415231: the aptitude is negated first, so a positive operand asks for technology and the same stat answers ma from the other side.
        foreach (var aptitude in new[] { -9, -5, -4, 0, 4, 5, 9 })
        {
            var values = new Dictionary<int, int?> { [16] = aptitude };
            Assert.AreEqual(-aptitude >= 5, Offered("ta 5", values), $"ta {aptitude}, n = 5");
            Assert.AreEqual(-aptitude <= 5, Offered("ta -5", values), $"ta {aptitude}, n = -5");
            Assert.AreEqual(aptitude >= -5, Offered("ta -5", values), $"ta -5 at {aptitude}: -aptitude <= 5 is aptitude >= -5");
        }
    }

    [TestMethod]
    public void Na_is_the_alignment_against_the_negated_operand()
    {
        // 0x4159BC: n >= 0 passes at alignment >= -n; n < 0 at alignment <= n (not a mirror image: na 5 passes from -5 up, na -5 passes from -5 down).
        foreach (var alignment in new[] { -1000, -6, -5, -4, 0, 4, 5, 6, 1000 })
        {
            var values = new Dictionary<int, int?> { [19] = alignment };
            Assert.AreEqual(alignment >= -5, Offered("na 5", values), $"na 5 at {alignment}");
            Assert.AreEqual(alignment <= -5, Offered("na -5", values), $"na -5 at {alignment}");
            Assert.AreEqual(alignment >= 0, Offered("na 0", values), $"na 0 at {alignment}");
        }
    }

    [TestMethod]
    public void The_port_is_asked_for_the_pc_and_the_stat_number()
    {
        var stats = new Stats(new() { [7] = 10 });
        Assert.IsTrue(Offered("ch 8", [], stats));
        CollectionAssert.AreEqual(new[] { new DialogStatQuery(new DialogParticipantId(800), 7) }, stats.Asked);
    }

    [TestMethod]
    public void A_stat_the_owner_does_not_hold_fails_and_a_throwing_port_fails_and_the_codes_need_the_port()
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: "ch 5", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start(document, new Ports(), Owner(), stats: new Stats([])).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start(document, new Ports(), Owner(), stats: new Throwing()).Failure!.Kind);
        foreach (var code in new[] { "ch", "pe", "le", "al", "ma", "ta", "na" })
        {
            var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: $"{code} 1", response: "0"));
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(gate, new Ports(), Owner()).Frontier!.Kind, code + " unadmitted without a stat port");
        }
        // The AL action keeps its own rules; the stat codes other than al are not actions.
        var action = Start(Read(Record("10", "Hm.", action: "al +5")), new Ports(), Owner(), world: new World(), stats: new Stats([]));
        Assert.IsNull(action.Failure);
        Assert.IsNull(action.Frontier);
        foreach (var code in new[] { "ch 5", "na 5" })
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(Read(Record("10", "Hm.", action: code)), new Ports(), Owner(), stats: new Stats([])).Frontier!.Kind, code);
    }

    [TestMethod]
    public void A_plus_sign_and_the_most_negative_operand_are_damage_and_never_an_action()
    {
        foreach (var field in new[] { "ch +5", "al +5", "ch -2147483648", "al -2147483648", "na -2147483648", "ta -2147483648" })
        {
            var gate = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: field, response: "0"));
            var conversation = Start(gate, new Ports(), Owner(), world: new World(), stats: new Stats(new() { [7] = 9, [19] = 9, [16] = 9 }));
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, conversation.Failure?.Kind, field);
        }
    }

    private sealed class Throwing : IDialogStatPort
    {
        public int? Stat(DialogStatQuery query) => throw new InvalidOperationException();
    }
}
