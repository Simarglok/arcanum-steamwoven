using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1304 (ledger RMR1–RMR9): the <c>r:</c> rumor reply as read in the executable (0x416C10 builds it, 0x414810 state 8, 0x418FC0 draws the
/// rumor, 0x418A40 and 0x418B30 the price, 0x4190E0 tells and learns it, 0x4C5700 reads its text), over invented generated-dialog and rumor files and
/// invented rumor, gold and reaction fixtures.
/// </summary>
[TestClass]
public sealed class DialogRumorTests
{
    private sealed class Rumors : IDialogRumorPort
    {
        public HashSet<int> Known { get; } = [];
        public HashSet<int> Quelled { get; } = [];
        public bool Throw { get; set; }
        public DialogCommandResult Execute(QuellRumorCommand command) => DialogCommandResult.Applied;

        public bool IsKnown(DialogRumorQuery query) => Throw ? throw new InvalidOperationException() : Known.Contains(query.Rumor);
        public bool IsQuelled(int rumor) => Throw ? throw new InvalidOperationException() : Quelled.Contains(rumor);
    }

    private sealed class Purse(int gold) : IDialogPcGoldPort
    {
        public int Gold { get; set; } = gold;
        public bool Throws { get; set; }
        public int PcGold(DialogGoldQuery query) => Throws ? throw new InvalidOperationException() : Gold;
    }

    private sealed class Pay : IDialogGoldPort
    {
        public List<(long Pc, long Npc, int Amount)> Moved { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public int Total(DialogGoldQuery query) => 1000;

        public DialogCommandResult Execute(GoldCommand command)
        {
            Moved.Add((command.PlayerCharacter.Value, command.Npc.Value, command.Amount));
            return Result;
        }
    }

    private sealed class Rig(int gold = 150, int reaction = 50, int[]? rolls = null)
    {
        public Rumors Rumors { get; } = new();
        public Purse Purse { get; } = new(gold);
        public Pay Pay { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts(), new DialogGreetingSources(DialogTrainerTests.Files(), new MesFile?[4], null, RumorLines()),
            rolls ?? [], DialogRuleProfile.Steamwoven, []);
        public bool WithRumors { get; init; } = true;
        public bool WithGreeter { get; init; } = true;
        public bool WithMoney { get; init; } = true;

        public DialogConversation Start(string replies, int callerLine = 0, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), WithMoney ? World : null, Rumors: WithRumors ? Rumors : null, Gold: WithMoney ? Pay : null,
                    Greeting: WithGreeter ? Greeter : null, Purse: WithMoney ? Purse : null), options);
    }

    // The rumor files: the male-to-male one tells rumors 1000 (class 0 and class 1 versions) and 1001 (class-less only); the others are empty.
    private static IReadOnlyList<MesFile?> RumorLines() =>
        [Mes("{20001}{Tale of 1000, @pcname@.}{20002}{Class one of 1000.}{20021}{Tale of 1001.}{20041}{Tale of 1002 by @npcname@.}"), null, null, null];

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    // Reply 11 is the rumor's; "response" 20 is the node after it.
    private static string RumorReply(string text = "R:$0, 1000-1002", string response = "20", string action = "") =>
        $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static DialogOption Only(DialogConversation conversation) => conversation.Options.Single();

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    [TestMethod]
    public void The_arguments_are_read_like_the_original_reads_them()
    {
        var plain = DialogConversation.ParseRumorReply("R:$0,1000")!.Value;
        Assert.AreEqual(0, plain.Price);
        CollectionAssert.AreEqual(new[] { 1000 }, plain.Rumors);
        var wrapped = DialogConversation.ParseRumorReply("r:($10, 1100-1102)")!.Value;
        Assert.AreEqual(10, wrapped.Price);
        CollectionAssert.AreEqual(new[] { 1100, 1101, 1102 }, wrapped.Rumors, "parentheses around the numbers are ignored");
        CollectionAssert.AreEqual(new[] { 1000, 1001, 1002 }, DialogConversation.ParseRumorReply("R: $0, 1000 - 1002")!.Value.Rumors, "spaces around the dash are read away by atoi");
        CollectionAssert.AreEqual(new[] { 1000, 1005 }, DialogConversation.ParseRumorReply("R:$-3,1000,1005")!.Value.Rumors);
        Assert.AreEqual(-3, DialogConversation.ParseRumorReply("R:$-3,1000")!.Value.Price, "a price below 1 tells the rumor without asking");
        Assert.AreEqual(0, DialogConversation.ParseRumorReply("R:$0,1005-1000")!.Value.Rumors.Length, "a descending range names nothing");
        Assert.IsNull(DialogConversation.ParseRumorReply("R:1000"), "no price mark: the original reads address 1");
        Assert.IsNull(DialogConversation.ParseRumorReply("R:$5"), "no list");
        Assert.IsNull(DialogConversation.ParseRumorReply("R:$0,999"), "a rumor outside 1000..2999 is outside the subset");
        Assert.IsNull(DialogConversation.ParseRumorReply("R:$0,1000,3000"));
    }

    [TestMethod]
    public void An_r_reply_is_a_generated_phrase_that_needs_the_rumor_and_greeting_ports_and_for_a_price_the_money_ports()
    {
        var rig = new Rig();
        var option = Only(rig.Start(RumorReply()));
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Rumor, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("2000 cls_pc2m", option.Text, "the phrase is the PC's class line 2000; the arguments are not shown");
        Assert.AreEqual(new DialogGeneratedMarker('r'), option.Marker);

        foreach (var blocked in new[] { new Rig { WithRumors = false }, new Rig { WithGreeter = false } })
            Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported),
                (Only(blocked.Start(RumorReply())).Block!.Kind, Only(blocked.Start(RumorReply())).Transition));

        var free = Only(new Rig { WithMoney = false }.Start(RumorReply()));
        Assert.AreEqual(DialogOptionTransition.Rumor, free.Transition, "a free rumor needs no money port");
        var paid = Only(new Rig { WithMoney = false }.Start(RumorReply("R:$10,1000")));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, paid.Block!.Kind, "a paid one needs the price and the payment");
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, Only(rig.Start(RumorReply("R:$0,999"))).Block!.Kind);
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, Only(rig.Start(RumorReply(action: "FL 20"))).Block!.Kind, "a dialog transfer in the same reply is outside the subset");
    }

    [TestMethod]
    public void A_free_rumor_is_drawn_among_the_listed_told_in_the_npcs_words_and_written_to_the_logbook()
    {
        var rig = new Rig(rolls: [1]);
        var conversation = rig.Start(RumorReply(action: "MM 1"));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands.Concat(rig.Ports.Commands.Where(c => !c.StartsWith("RU:"))).ToArray(), "the reply's action runs first");
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.Rumor, 0, 2) }, rig.Greeter.Asked.Where(a => a.Item1 == DialogGreetingRoll.Rumor).ToArray(), "one draw over the three candidates");
        Assert.AreEqual("Tale of 1001.", conversation.NpcLine!.Text, "candidate 1 is rumor 1001, read from the class-less message 20021");
        Assert.AreEqual((DialogLineOriginKind.Rumor, 1001), (conversation.NpcLine.Origin!.Kind, conversation.NpcLine.Origin.Number));
        Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode, "the talk stays in the node");
        CollectionAssert.AreEqual(new[] { "RU:1001" }, rig.Ports.Commands.Where(c => c.StartsWith("RU:")).ToArray());
        Assert.AreEqual(0, rig.Pay.Moved.Count);
        var answer = Only(conversation);
        Assert.AreEqual(("1000 cls_pc2m", DialogOptionTransition.Direct), (answer.Text, answer.Transition), "the only answer is the PC class line 1000 with the reply's own response");
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void The_tale_substitutes_the_names_and_the_first_candidate_needs_no_draw()
    {
        var rig = new Rig();
        var conversation = rig.Start(RumorReply("R:$0,1000"));
        Pick(conversation, 1);
        Assert.AreEqual("Tale of 1000, Ada.", conversation.NpcLine!.Text);
        Assert.AreEqual(0, rig.Greeter.Asked.Count(a => a.Item1 == DialogGreetingRoll.Rumor), "equal bounds draw nothing (0x4CEF00)");
        var other = new Rig();
        Pick(other.Start(RumorReply("R:$0,1002")), 1);
        Assert.AreEqual(1, other.Ports.Commands.Count);
    }

    [TestMethod]
    public void Known_and_quelled_rumors_are_not_drawn()
    {
        var rig = new Rig();
        rig.Rumors.Known.Add(1000);
        rig.Rumors.Quelled.Add(1001);
        var conversation = rig.Start(RumorReply());
        Pick(conversation, 1);
        Assert.AreEqual("Tale of 1002 by Bram.", conversation.NpcLine!.Text, "only rumor 1002 is left, so there is no draw");
        CollectionAssert.AreEqual(new[] { "RU:1002" }, rig.Ports.Commands.ToArray());
        Assert.AreEqual(0, rig.Greeter.Asked.Count(a => a.Item1 == DialogGreetingRoll.Rumor));
    }

    [TestMethod]
    public void With_nothing_left_the_npc_says_line_7000_and_the_only_answer_leads_on()
    {
        var rig = new Rig();
        rig.Rumors.Known.Add(1000);
        rig.Rumors.Quelled.Add(1001);
        var conversation = rig.Start(RumorReply("R:$0,1000-1001"));
        Pick(conversation, 1);
        Assert.AreEqual("7000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual("600 cls_pc2m".Replace("cls_", string.Empty), Only(conversation).Text);
        Assert.AreEqual(0, rig.Ports.Commands.Count, "nothing is written");
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);

        var empty = new Rig().Start(RumorReply("R:$0,1005-1000"));
        Pick(empty, 1);
        Assert.AreEqual("7000 cls_m2m", empty.NpcLine!.Text, "an empty list is the same");
    }

    [TestMethod]
    public void A_price_is_asked_on_the_reaction_scale_and_yes_pays_the_npc_then_tells_and_writes_the_rumor()
    {
        var rig = new Rig(gold: 20, reaction: 0);
        var conversation = rig.Start(RumorReply("R:$10,1000"));
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m costs 20", conversation.NpcLine!.Text, "reaction 0: factor 200, the base price 10 is quoted as 20");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, conversation.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual((DialogOptionTransition.RumorStep, DialogOptionTransition.Direct), (conversation.Options[0].Transition, conversation.Options[1].Transition));
        Assert.AreEqual(0, rig.Ports.Commands.Count + rig.Pay.Moved.Count, "nothing happens before yes");
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -20) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { "RU:1000" }, rig.Ports.Commands.ToArray());
        Assert.AreEqual("Tale of 1000, Ada.", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { typeof(GoldCommand), typeof(RumorLogCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void No_leaves_by_the_replys_response_and_too_little_gold_is_line_2000()
    {
        var no = new Rig();
        var conversation = no.Start(RumorReply("R:$10,1000"));
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual(0, no.Ports.Commands.Count + no.Pay.Moved.Count);

        var poor = new Rig(gold: 9, reaction: 50);
        var talk = poor.Start(RumorReply("R:$10,1000"));
        Pick(talk, 1);
        Assert.AreEqual("1000 cls_m2m costs 10", talk.NpcLine!.Text);
        Pick(talk, 1);
        Assert.AreEqual("2000 cls_m2m", talk.NpcLine!.Text, "the hero's own gold must cover the whole price");
        Assert.AreEqual(0, poor.Pay.Moved.Count + talk.Effects.Count);
        poor.Purse.Gold = 10;
        Pick(talk, 1);
        Assert.AreEqual(new DialogNodeId(20), talk.CurrentNode, "the notice leads on without paying");
    }

    [TestMethod]
    public void Failures_and_frontiers_are_typed_and_nothing_is_written_after_a_refusal()
    {
        var rejected = new Rig();
        rejected.Pay.Result = DialogCommandResult.Rejected;
        var conversation = rejected.Start(RumorReply("R:$10,1000"));
        Pick(conversation, 1);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, conversation.Failure!.Kind);
        Assert.AreEqual(0, rejected.Ports.Commands.Count, "the rumor is not told after a refused payment");

        var learnRefused = new Rig();
        var refusing = DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + RumorReply("R:$0,1000") + "{20}{Next node.}{}{}{}{}{}"),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(new Ports { RejectAt = 1 }, new Ports { RejectAt = 1 }, Owner(), learnRefused.World, Rumors: learnRefused.Rumors, Gold: learnRefused.Pay,
                Greeting: learnRefused.Greeter, Purse: learnRefused.Purse));
        refusing.Select(refusing.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, refusing.Failure!.Kind);

        var unknown = new Rig();
        unknown.World.Reaction = DialogReactionReadResult.Unknown();
        var stopped = unknown.Start(RumorReply("R:$10,1000"));
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, stopped.Select(stopped.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, stopped.Frontier!.Kind);

        var throwing = new Rig();
        throwing.Rumors.Throw = true;
        var failed = throwing.Start(RumorReply("R:$0,1000"));
        failed.Select(failed.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, failed.Failure!.Kind);

        var noGold = new Rig();
        noGold.Purse.Throws = true;
        var broke = noGold.Start(RumorReply("R:$10,1000"));
        Pick(broke, 1);
        broke.Select(broke.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, broke.Failure!.Kind);
    }

    [TestMethod]
    public void A_broken_draw_and_the_effect_budget_stop_before_any_write()
    {
        var out_of_range = new Rig(rolls: [7]);
        var conversation = out_of_range.Start(RumorReply());
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, conversation.Failure!.Kind);

        var refused = new Rig();
        var refusing = refused.Start(RumorReply());
        refused.Greeter.Refuse = true;
        refusing.Select(refusing.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, refusing.Failure!.Kind);

        var tight = new Rig();
        var limited = tight.Start(RumorReply("R:$10,1000"), options: new DialogConversationOptions { MaxEffects = 1 });
        Pick(limited, 1);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, limited.Select(limited.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, limited.Frontier!.Kind);
        Assert.AreEqual(0, tight.Pay.Moved.Count + tight.Ports.Commands.Count, "nothing is paid when both effects do not fit");
    }

    [TestMethod]
    public void A_rebuild_keeps_the_screen_and_the_way_out_follows_the_reply_without_running_it_again()
    {
        var rig = new Rig();
        var conversation = rig.Start(RumorReply("R:$10,1000", action: "MM 1"));
        Pick(conversation, 1);
        var stale = conversation.Options[0].Token;
        var texts = conversation.Options.Select(o => o.Text).ToArray();
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.RefreshOptions().Status);
        CollectionAssert.AreEqual(texts, conversation.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(DialogConversationSelectStatus.NotOffered, conversation.Select(stale).Status);
        Pick(conversation, 2);
        Assert.AreEqual((new DialogNodeId(20), 1), (conversation.CurrentNode, rig.World.Commands.Count), "the action ran once");

        var exit = new Rig().Start(RumorReply("R:$0,1000", response: "0"), callerLine: 4);
        Pick(exit, 1);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, exit.Select(exit.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), exit.End);
    }

    [TestMethod]
    public void A_rumors_text_comes_from_the_file_of_the_two_genders_and_the_class_message_before_the_classless_one()
    {
        var files = new MesFile?[]
        {
            Mes("{20001}{m2m a}{20003}{m2m class 2}"), Mes("{20001}{m2f}"), Mes("{20001}{f2f}"), Mes("{20001}{f2m}"),
        };
        var sources = new DialogGreetingSources(DialogTrainerTests.Files(), new MesFile?[4], null, files);
        var names = new DialogSpeakerNames("Ada", "Bram");
        string Tale(bool npcMale, bool pcMale, int socialClass, int rumor = 1000) => DialogGreeting.RumorTale(rumor,
            new DialogGreetingFacts(npcMale, pcMale, false, socialClass, 0, 0, DialogReactionCategory.Neutral, false), sources, new DialogGreetingTests.FakeGreeter(null, sources, [], DialogRuleProfile.Steamwoven, []),
            names, DialogTextEncoding.Windows1252, 1000).Text;
        Assert.AreEqual("m2m a", Tale(true, true, 0));
        Assert.AreEqual("m2m class 2", Tale(true, true, 2), "message 20 x rumor + class + 1");
        Assert.AreEqual("m2m a", Tale(true, true, 5), "a class without its own message falls back to the class-less one");
        Assert.AreEqual("m2f", Tale(true, false, 0));
        Assert.AreEqual("f2f", Tale(false, false, 0));
        Assert.AreEqual("f2m", Tale(false, true, 0));
        Assert.AreEqual(string.Empty, Tale(true, true, 0, rumor: 1001), "a rumor without a message is an empty text");
        var none = DialogGreeting.RumorTale(1000, new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, false),
            new DialogGreetingSources(DialogTrainerTests.Files(), new MesFile?[4], null), new DialogGreetingTests.FakeGreeter(null, sources, [], DialogRuleProfile.Steamwoven, []), names,
            DialogTextEncoding.Windows1252, 1000);
        Assert.AreEqual((string.Empty, DialogLineOriginKind.Empty), (none.Text, none.Origin!.Kind), "without the rumor files the text is empty");
    }
}
