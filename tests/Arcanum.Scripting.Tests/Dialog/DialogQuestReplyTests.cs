using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The q:N reply through the quest rules table (ARC-SCRIPT-1532, ledger DQR1-DQR5). Records and table rows are invented synthetic fixtures.
[TestClass]
public sealed class DialogQuestReplyTests
{
    internal sealed class Table(QuestReplyTable table) : IDialogQuestReplyPort
    {
        public bool Throws { get; set; }
        public List<(int Quest, int Index)> Asked { get; } = [];

        public int? Field(int quest, int index)
        {
            if (Throws) throw new InvalidOperationException("synthetic");
            Asked.Add((quest, index));
            return table.Field(quest, index);
        }
    }

    // Quest 1000: experience level 1, a second number 0, then the normal, low-reaction and low-Intelligence groups of seven records (states 0..6).
    private static int[] Row(int normal = 40, int lowReaction = 50, int lowIntelligence = 60) =>
        [1, 0, .. Enumerable.Range(normal, 7), .. Enumerable.Range(lowReaction, 7), .. Enumerable.Range(lowIntelligence, 7)];

    private static Table Rules(int[]? row = null, int quest = 1000) => new(QuestReplyTable.Create(new Dictionary<int, int[]> { [quest] = row ?? Row() }));

    // NPC 10 offers the q: reply 11 (its response 99 and action are not used); the records it can choose live under NPC 20.
    private static string Records(string question = "Q:1000", string extra = "")
    {
        var text = new StringBuilder(Record("10", "?") + Record("11", question, iq: "1", response: "99", action: "MM 7") + Record("20", "Later."));
        foreach (var id in Enumerable.Range(40, 27)) text.Append(Record(id.ToString(), $"T{id}", iq: "1", response: "0"));
        return text + extra;
    }

    private static DialogConversation Run(int state, int reaction = 50, int intelligence = 8, Table? rules = null, string? records = null, DialogQuestTests.Quests? quests = null,
        World? world = null)
    {
        world ??= new World { Reaction = DialogReactionReadResult.Of(reaction) };
        return Start(Read(records ?? Records()), new Ports(), Owner(), intelligence: intelligence, world: world, quests: quests ?? new DialogQuestTests.Quests(new() { [1000] = state }),
            questReplies: rules ?? Rules());
    }

    [TestMethod]
    public void The_reply_shows_the_record_the_table_names_for_the_quest_state()
    {
        for (var state = 0; state < 7; state++)
        {
            var conversation = Run(state);
            Assert.IsNull(conversation.Failure);
            Assert.IsNull(conversation.Frontier);
            var option = conversation.Options.Single();
            Assert.AreEqual($"T{40 + state}", option.Text, $"state {state}");
            Assert.AreEqual((DialogOptionTextKind.Record, (DialogOptionBlock?)null), (option.TextKind, option.Block));
            Assert.AreEqual(new DialogResponseId(11), option.ResponseId, "the option keeps the q: record's identity");
        }
    }

    [TestMethod]
    public void A_low_reaction_or_a_dull_PC_reads_the_other_groups_and_the_Intelligence_is_asked_first()
    {
        Assert.AreEqual("T40", Run(0, reaction: 20).Options.Single().Text, "reaction 20 is not below the limit");
        Assert.AreEqual("T52", Run(2, reaction: 19).Options.Single().Text, "reaction below 20: group 9");
        var world = new World { Reaction = DialogReactionReadResult.Of(5) };
        Assert.AreEqual("T65", Run(5, reaction: 5, intelligence: 4, world: world).Options.Single().Text, "Intelligence below 5: group 16");
        Assert.AreEqual(0, world.Reads, "a dull PC never asks for the reaction");
        Assert.AreEqual("T40", Run(0, reaction: 5, intelligence: 5).Options.Single().Text.Replace("T50", "T40"), "Intelligence 5 is not dull (reaction 5 reads group 9)");
    }

    [TestMethod]
    public void The_reply_asks_the_quest_state_of_the_numbered_quest_and_the_field_of_the_table()
    {
        var quests = new DialogQuestTests.Quests(new() { [1000] = 3 });
        var rules = Rules();
        var conversation = Run(3, quests: quests, rules: rules);
        Assert.AreEqual("T43", conversation.Options.Single().Text);
        CollectionAssert.AreEqual(new[] { (800L, 1000) }, quests.Asked);
        CollectionAssert.AreEqual(new[] { (1000, 5) }, rules.Asked);
    }

    [TestMethod]
    public void Only_the_q_record_carries_the_gates_the_chosen_record_brings_text_response_and_action()
    {
        var records = Records(extra: Record("70", "Gated away.", alternate: "0", iq: "99", response: "20", action: "MM 3"));
        var conversation = Run(0, records: records, rules: Rules(Row(normal: 70)));
        var option = conversation.Options.Single();
        Assert.AreEqual("Gated away.", option.Text, "the chosen record's own gender and IQ are not tested");
        Assert.AreEqual((DialogOptionTransition.Direct, new DialogNodeId(20)), (option.Transition, option.Target));
        // The q: record's own gates still apply.
        var gated = Run(0, records: Records("Q:1000").Replace("{11}{Q:1000}{}{1}", "{11}{Q:1000}{}{20}"));
        Assert.IsTrue(gated.Options.Single().TextKind == DialogOptionTextKind.Fallback, "the q: record's IQ gate hides the reply: only the farewell is left");
    }

    [TestMethod]
    public void Picking_the_option_runs_the_chosen_records_action_then_follows_its_response()
    {
        var records = Records(extra: Record("70", "Take it.", iq: "1", response: "20", action: "MM 3"));
        var world = new World();
        var conversation = Run(0, records: records, rules: Rules(Row(normal: 70)), world: world);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options.Single().Token).Status);
        CollectionAssert.AreEqual(new[] { "MM:3" }, world.Commands, "the q: record's own action MM 7 does not run");
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void A_chosen_minus_one_drops_the_reply_but_it_still_takes_one_of_the_five_places()
    {
        var drop = Run(0, rules: Rules([1, 0, -1, -1, -1, -1, -1, -1, -1, .. new int[14]]));
        Assert.AreEqual(DialogOptionTextKind.Fallback, drop.Options.Single().TextKind, "no reply left: the farewell");

        // Five q: replies, all dropped, in front of a sixth reply: the source's list of five eligible records is already full.
        var text = new StringBuilder(Record("10", "?"));
        for (var id = 11; id < 16; id++) text.Append(Record(id.ToString(), "Q:1000", iq: "1", response: "0"));
        text.Append(Record("16", "Sixth.", iq: "1", response: "0"));
        var full = Run(0, records: text.ToString(), rules: Rules([1, 0, -1, -1, -1, -1, -1, -1, -1, .. new int[14]]));
        Assert.AreEqual(DialogOptionTextKind.Fallback, full.Options.Single().TextKind, "the sixth reply is cut by the cap");
        // Four dropped ones leave room for the fifth and the sixth is not reached either; with three dropped the fifth-place reply is offered.
        var some = new StringBuilder(Record("10", "?"));
        for (var id = 11; id < 14; id++) some.Append(Record(id.ToString(), "Q:1000", iq: "1", response: "0"));
        some.Append(Record("14", "Fourth.", iq: "1", response: "0") + Record("15", "Fifth.", iq: "1", response: "0") + Record("16", "Sixth.", iq: "1", response: "0"));
        var offered = Run(0, records: some.ToString(), rules: Rules([1, 0, -1, -1, -1, -1, -1, -1, -1, .. new int[14]]));
        CollectionAssert.AreEqual(new[] { "Fourth.", "Fifth." }, offered.Options.Select(option => option.Text).ToArray());
    }

    [TestMethod]
    public void Without_a_port_or_for_a_number_outside_the_table_range_the_reply_stays_a_frontier()
    {
        var states = new DialogQuestTests.Quests(new() { [1000] = 0 });
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(Read(Records()), new Ports(), Owner(), world: new World(), quests: states).Frontier!.Kind, "no table port");
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(Read(Records()), new Ports(), Owner(), quests: states, questReplies: Rules()).Frontier!.Kind, "no world port");
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(Read(Records()), new Ports(), Owner(), world: new World(), questReplies: Rules()).Frontier!.Kind, "no quest port");
        foreach (var text in new[] { "Q:999", "q:2000", "q:", "q:10x0", "q:-1001" })
            Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Run(0, records: Records(text)).Frontier!.Kind, text);
        Assert.IsNull(Run(0, records: Records("q: 1000")).Frontier, "the number may follow spaces; the marker is case-blind");
    }

    [TestMethod]
    public void A_chosen_record_that_cannot_be_offered_leaves_the_option_blocked()
    {
        // A missing record, an NPC record, the q: record itself and another q: reply.
        foreach (var (name, id) in new[] { ("missing", 77), ("npc", 20), ("itself", 11) })
        {
            var option = Run(0, rules: Rules(Row(normal: id)), records: Records()).Options.Single();
            Assert.AreEqual(DialogOptionBlockKind.TargetInvalid, option.Block!.Kind, name);
        }
        var loop = Run(0, rules: Rules(Row(normal: 70)), records: Records(extra: Record("70", "q:1000", iq: "1", response: "0"))).Options.Single();
        Assert.AreEqual(DialogOptionBlockKind.TargetInvalid, loop.Block!.Kind, "a chain that comes back to a record already chosen is a loop");
        var short_ = Run(1, rules: Rules([1, 0, 40])).Options.Single();
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, short_.Block!.Kind, "the table holds no field for that state");
        var noRow = Run(0, rules: Rules(quest: 1001)).Options.Single();
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, noRow.Block!.Kind, "the quest has no row");
    }

    [TestMethod]
    public void A_chosen_q_record_is_chosen_through_in_turn_with_its_own_quest_and_without_its_gates()
    {
        // Record 70 is a q: reply of quest 1001 (a gate of IQ 99 is not tested); quest 1001 is in state 1 and names record 41.
        var rules = new Table(QuestReplyTable.Create(new Dictionary<int, int[]> { [1000] = Row(normal: 70), [1001] = Row(normal: 40) }));
        var quests = new DialogQuestTests.Quests(new() { [1000] = 0, [1001] = 1 });
        var conversation = Run(0, rules: rules, quests: quests, records: Records(extra: Record("70", "q:1001", iq: "99", response: "0")));
        Assert.AreEqual("T41", conversation.Options.Single().Text);
        CollectionAssert.AreEqual(new[] { (800L, 1000), (800L, 1001) }, quests.Asked);
    }

    [TestMethod]
    public void Broken_port_results_fail_the_conversation_and_an_unknown_reaction_stops_it()
    {
        var throwing = Rules();
        throwing.Throws = true;
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Run(0, rules: throwing).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Run(7).Failure!.Kind, "a quest state outside 0..6");
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Run(-1).Failure!.Kind);
        var unknown = Run(0, world: new World { Reaction = DialogReactionReadResult.Unknown() });
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, unknown.Frontier!.Kind);
    }
}
