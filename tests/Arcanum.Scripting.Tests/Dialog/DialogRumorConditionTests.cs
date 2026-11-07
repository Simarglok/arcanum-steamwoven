using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-DEV-171 (bug B17): the RU and RQ reply conditions (0x4150D0, cases 17 and 18, ledger RU1–RU2). The plane-wreck dialog
/// (DLG 1696) is a clever and a dull reply to Virgil's remark, then an ogre line whose replies are gated by RU 2015 and RU -2015 for
/// each intelligence: before this task the unadmitted RU condition stopped the second line for every hero, so the reply gate by
/// intelligence was never the cause.</summary>
[TestClass]
public sealed class DialogRumorConditionTests
{
    // The shape of DLG 1696 with invented texts: node 10 asks, a clever (iq 5) and a dull (iq -4) reply lead to node 20, whose replies
    // are the clever and dull pair of "knows" and "does not know" the rumor.
    private static readonly string Wreck =
        Record("10", "What is this?")
        + Record("11", "It looks like the machine.", iq: "5", response: "20")
        + Record("12", "Uh...shoot blimp!", iq: "-4", response: "20")
        + Record("20", "An ogre among the wreckage?")
        + Record("21", "Clever, knows.", iq: "5", condition: "ru 2015", response: "0")
        + Record("22", "Clever, does not know.", iq: "5", condition: "ru -2015", response: "0")
        + Record("23", "Dull, knows.", iq: "-4", condition: "ru 2015", response: "0")
        + Record("24", "Dull, does not know.", iq: "-4", condition: "ru -2015", response: "0");

    [TestMethod]
    [DataRow(8, false, "It looks like the machine.", true, "Clever, knows.")]
    [DataRow(8, false, "It looks like the machine.", false, "Clever, does not know.")]
    [DataRow(3, false, "Uh...shoot blimp!", true, "Dull, knows.")]
    [DataRow(3, false, "Uh...shoot blimp!", false, "Dull, does not know.")]
    // A dumb hero above intelligence 4 reads as 1 (ledger GS2): the dull replies.
    [DataRow(9, true, "Uh...shoot blimp!", true, "Dull, knows.")]
    public void The_wreck_conversation_reaches_its_second_line_for_a_clever_and_a_dull_hero(
        int intelligence, bool dumb, string firstReply, bool knowsRumor, string secondReply)
    {
        var rumors = new Rumors();
        if (knowsRumor) rumors.Known.Add(2015);
        var conversation = StartWith(Read(Wreck), intelligence, dumb, rumors);

        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { firstReply }, conversation.Options.Select(o => o.Text).ToArray());
        Assert.IsNull(conversation.Options.Single().Block, "the second line is inside the subset, so the reply is not blocked");

        var step = conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, step.Status);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { secondReply }, conversation.Options.Select(o => o.Text).ToArray());
        Assert.IsTrue(rumors.Queries.All(q => q == new DialogRumorQuery(new DialogParticipantId(800), 2015)), "the PC is asked about rumor 2015");

        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogConversationPhase.Ended, conversation.Phase);
    }

    [TestMethod]
    public void Ru_holds_for_a_known_rumor_and_a_negative_number_for_an_unknown_one()
    {
        var document = Read(Record("10", "?")
            + Record("11", "known", iq: "1", condition: "RU 1500", response: "0")
            + Record("12", "not known", iq: "1", condition: "ru -1500", response: "0")
            + Record("13", "other known", iq: "1", condition: "ru 2999", response: "0")
            + Record("14", "other not known", iq: "1", condition: "ru -1000", response: "0")
            + Record("15", "both", iq: "1", condition: "RU 1500, ru 2999", response: "0"));
        static string[] Offered(DlgDocument document, Rumors rumors) => StartWith(document, 8, false, rumors).Options.Select(o => o.Text).ToArray();

        var rumors = new Rumors();
        rumors.Known.Add(1500);
        CollectionAssert.AreEqual(new[] { "known", "other not known" }, Offered(document, rumors));
        rumors.Known.Add(2999);
        rumors.Known.Add(1000);
        CollectionAssert.AreEqual(new[] { "known", "other known", "both" }, Offered(document, rumors));
        CollectionAssert.AreEqual(new[] { "not known", "other not known" }, Offered(document, new Rumors()));
    }

    [TestMethod]
    public void Rq_holds_for_a_quelled_rumor_and_a_negative_number_for_a_rumor_still_believed()
    {
        // DLG 1906 and 1929 ask for "ru n, rq -n": the PC knows the rumor and it is not proven false.
        var document = Read(Record("10", "?")
            + Record("11", "believed", iq: "1", condition: "ru 1130, rq -1130", response: "0")
            + Record("12", "quelled", iq: "1", condition: "rq 1130", response: "0")
            + Record("13", "never quelled", iq: "1", condition: "RQ -1131", response: "0"));
        var rumors = new Rumors();
        rumors.Known.Add(1130);
        CollectionAssert.AreEqual(new[] { "believed", "never quelled" }, StartWith(document, 8, false, rumors).Options.Select(o => o.Text).ToArray());
        rumors.Quelled.Add(1130);
        CollectionAssert.AreEqual(new[] { "quelled", "never quelled" }, StartWith(document, 8, false, rumors).Options.Select(o => o.Text).ToArray());
        Assert.IsTrue(rumors.QuelledQueries.All(rumor => rumor is 1130 or 1131), "the quelled set is asked by the rumor number alone");
    }

    [TestMethod]
    public void Without_the_rumor_port_the_conditions_are_a_static_frontier()
    {
        foreach (var condition in new[] { "RU 1500", "ru -1500", "RQ 1500", "rq -1500" })
        {
            var conversation = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), 8, false, null);
            Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, conversation.Frontier!.Kind, condition);
        }
    }

    [TestMethod]
    public void A_rumor_number_outside_the_sessions_range_is_outside_the_subset_and_a_throwing_port_fails_the_conversation()
    {
        foreach (var condition in new[] { "RU 999", "ru -999", "RU 3000", "RQ -3000", "RU 0", "RQ 0", "ru -2147483648" })
        {
            var conversation = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), 8, false, new Rumors());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier!.Kind, condition);
        }
        // A '+' sign is invalid (the grammar's policy); a stray second operand is outside the subset (ARC-SKILLS-004).
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation,
            StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RU +1500", response: "0")), 8, false, new Rumors()).Failure!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation,
            StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RQ 1500 1", response: "0")), 8, false, new Rumors()).Frontier!.Kind);
        foreach (var condition in new[] { "RU 1500", "RQ 1500" })
        {
            var conversation = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), 8, false, new Rumors { Throw = true });
            Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind, condition);
        }
    }

    [TestMethod]
    public void The_intelligence_gate_itself_picks_the_dull_reply_at_four_or_less_and_the_clever_one_from_five()
    {
        // The reply gate read in the executable (0x414F50): iq >= 0 needs at least iq, iq < 0 at most -iq, both inclusive.
        var document = Read(Record("10", "?") + Record("11", "clever", iq: "5", response: "0") + Record("12", "dull", iq: "-4", response: "0"));
        foreach (var (intelligence, dumb, expected) in new[] { (1, false, "dull"), (4, false, "dull"), (5, false, "clever"), (20, false, "clever"), (20, true, "dull") })
            CollectionAssert.AreEqual(new[] { expected }, StartWith(document, intelligence, dumb, new Rumors()).Options.Select(o => o.Text).ToArray(), $"{intelligence} {dumb}");
    }

    private static DialogConversation StartWith(DlgDocument document, int intelligence, bool dumb, IDialogRumorPort? rumors) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, intelligence, dumb, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(new Ports(), new Ports(), Owner(), Rumors: rumors));

    private sealed class Rumors : IDialogRumorPort
    {
        public HashSet<int> Known { get; } = [];
        public HashSet<int> Quelled { get; } = [];
        public List<DialogRumorQuery> Queries { get; } = [];
        public List<int> QuelledQueries { get; } = [];
        public bool Throw { get; init; }

        public DialogCommandResult Execute(QuellRumorCommand command) => DialogCommandResult.Applied;

        public bool IsKnown(DialogRumorQuery query)
        {
            if (Throw) throw new InvalidOperationException("Synthetic query failure.");
            Queries.Add(query);
            return Known.Contains(query.Rumor);
        }

        public bool IsQuelled(int rumor)
        {
            if (Throw) throw new InvalidOperationException("Synthetic query failure.");
            QuelledQueries.Add(rumor);
            return Quelled.Contains(rumor);
        }
    }
}
