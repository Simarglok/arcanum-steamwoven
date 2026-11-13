using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1535 (ledger INS3): the NPC lines <c>m:$price,node</c> and <c>r:$price,list</c> as read in the executable (0x416840 → 0x418A40 and 0x418FC0): the price
/// screen whose yes pays and follows the line's node and whose no follows the node's own response, and the rumor draw with the node's own response in the place of a
/// reply's, over invented generated-dialog and rumor files and invented gold, rumor and reaction fixtures.
/// </summary>
[TestClass]
public sealed class DialogNpcTalkTests
{
    private sealed class Rumors : IDialogRumorPort
    {
        public HashSet<int> Known { get; } = [];
        public DialogCommandResult Execute(QuellRumorCommand command) => DialogCommandResult.Applied;
        public bool IsKnown(DialogRumorQuery query) => Known.Contains(query.Rumor);
        public bool IsQuelled(int rumor) => false;
    }

    private sealed class Purse(int gold) : IDialogPcGoldPort
    {
        public int Gold { get; set; } = gold;
        public int PcGold(DialogGoldQuery query) => Gold;
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

    private sealed class Rig(int gold = 150, int reaction = 50)
    {
        public Rumors Rumors { get; } = new();
        public Purse Purse { get; } = new(gold);
        public Pay Pay { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts(),
            new DialogGreetingSources(DialogTrainerTests.Files(), new MesFile?[4], null, [Mes("{20001}{Tale of 1000, @pcname@.}"), null, null, null]),
            [], DialogRuleProfile.Steamwoven, []);
        public bool WithMoney { get; init; } = true;
        public bool WithGreeter { get; init; } = true;
        public bool WithRumors { get; init; } = true;

        public DialogConversation Start(string nodes, int callerLine = 0, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read(nodes),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), WithMoney ? World : null, Rumors: WithRumors ? Rumors : null, Gold: WithMoney ? Pay : null,
                    Greeting: WithGreeter ? Greeter : null, Purse: WithMoney ? Purse : null), options);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    // Node 10 is the NPC's (its text, response and action are the arguments), reply 11 would go on to node 21; node 20 is where the node's own response leads,
    // node 30 where a yes leads, 40 is a Player record.
    private static string Nodes(string text, string response = "20", string action = "", string extra = "") =>
        $"{{10}}{{{text}}}{{}}{{}}{{}}{{{response}}}{{{action}}}{{11}}{{Never shown.}}{{}}{{1}}{{}}{{21}}{{}}"
        + "{20}{Own node.}{}{}{}{}{}{21}{Reply node.}{}{}{}{}{}{30}{Yes node.}{}{}{}{}{}{40}{A reply.}{}{1}{}{20}{}" + extra;

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    [TestMethod]
    public void The_price_line_is_read_like_the_original_reads_it()
    {
        Assert.AreEqual((100, 30), DialogConversation.ParseNpcPriceLine("m:$100,30"));
        Assert.AreEqual((5, -7), DialogConversation.ParseNpcPriceLine("M: x$ 5 , -7"), "atoi skips white space; other characters before the marks do not matter");
        Assert.AreEqual((0, 0), DialogConversation.ParseNpcPriceLine("m:$,"), "empty numbers read 0");
        Assert.IsNull(DialogConversation.ParseNpcPriceLine("m:100,30"), "no price mark: the original reads address 1");
        Assert.IsNull(DialogConversation.ParseNpcPriceLine("m:$100"), "no node");
        Assert.IsNull(DialogConversation.ParseNpcPriceLine("m:,30$5"), "the comma must come after the price mark");
        Assert.IsNull(DialogConversation.ParseNpcPriceLine("m:$-5,30"), "a negative price is outside the subset");
    }

    [TestMethod]
    public void An_m_node_runs_its_action_then_shows_the_price_screen_and_builds_none_of_its_replies()
    {
        foreach (var marker in new[] { "m:$100,30", "M:$100,30 and more" })
        {
            var rig = new Rig(reaction: 0);
            var conversation = rig.Start(Nodes(marker, action: "MM 1"));
            Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase, marker);
            CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands, "the node's action ran first");
            Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode);
            Assert.AreEqual("1000 cls_m2m costs 200", conversation.NpcLine!.Text, "the NPC's class line 1000 with the price 100 on the reaction scale (reaction 0: factor 200)");
            Assert.AreEqual((DialogLineOriginKind.Generated, 1000), (conversation.NpcLine.Origin!.Kind, conversation.NpcLine.Origin.Number));
            CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, conversation.Options.Select(o => o.Text).ToArray(), "PC lines 1..99 and 100..199, not the node's reply");
            Assert.AreEqual((DialogOptionTransition.TrainerStep, DialogOptionTransition.Direct), (conversation.Options[0].Transition, conversation.Options[1].Transition));
            Assert.AreEqual(new DialogNodeId(20), conversation.Options[1].Target, "no follows the node's own response");
            Assert.AreEqual(new DialogGeneratedMarker('m'), conversation.Options[0].Marker);
            Assert.AreEqual(0, rig.Pay.Moved.Count, "nothing is paid before yes");
        }
    }

    [TestMethod]
    public void Yes_pays_the_npc_and_follows_the_line_s_node_without_the_node_s_own_response()
    {
        var rig = new Rig(gold: 150, reaction: 50);
        var conversation = rig.Start(Nodes("m:$100,30"));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -100) }, rig.Pay.Moved, "the hero pays the NPC the price through the gold port");
        Assert.AreEqual(new DialogNodeId(30), conversation.CurrentNode);
        Assert.AreEqual("Yes node.", conversation.NpcLine!.Text);
        Assert.IsTrue(conversation.Effects.Single().Command is GoldCommand);
    }

    [TestMethod]
    public void No_follows_the_node_s_own_response_and_too_little_gold_is_line_2000_that_leads_there()
    {
        var no = new Rig();
        var conversation = no.Start(Nodes("m:$100,30"));
        Pick(conversation, 2);
        Assert.AreEqual((new DialogNodeId(20), "Own node."), (conversation.CurrentNode, conversation.NpcLine!.Text));
        Assert.AreEqual(0, no.Pay.Moved.Count);

        var poor = new Rig(gold: 99);
        var talk = poor.Start(Nodes("m:$100,30"));
        Pick(talk, 1);
        Assert.AreEqual("2000 cls_m2m", talk.NpcLine!.Text, "the hero's own gold must cover the whole price");
        Assert.AreEqual(0, poor.Pay.Moved.Count + talk.Effects.Count);
        poor.Purse.Gold = 100;
        Pick(talk, 1);
        Assert.AreEqual(new DialogNodeId(20), talk.CurrentNode, "the notice leads to the node's own response, never to the yes node");
    }

    [TestMethod]
    public void A_response_of_zero_ends_the_talk_and_a_negative_one_is_a_script_line()
    {
        var end = new Rig().Start(Nodes("m:$100,0", response: "0"), callerLine: 4);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, end.Select(end.Options[1].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), end.End, "no with an empty response returns to the caller's next line");

        var yes = new Rig();
        var paid = yes.Start(Nodes("m:$100,0", response: "-7"), callerLine: 4);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, paid.Select(paid.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), paid.End, "yes with node 0 ends the talk");
        Assert.AreEqual(1, yes.Pay.Moved.Count, "the price was paid first");

        var script = new Rig().Start(Nodes("m:$100,-9", response: "-7"), callerLine: 4);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, script.Select(script.Options[1].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(7)), script.End, "no follows -7");

        var yesLine = new Rig().Start(Nodes("m:$100,-9", response: "-7"), callerLine: 4);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, yesLine.Select(yesLine.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(9)), yesLine.End, "yes follows -9");
    }

    [TestMethod]
    public void A_free_price_moves_no_gold_and_the_price_of_one_is_quoted_as_two()
    {
        var free = new Rig();
        var conversation = free.Start(Nodes("m:$0,30"));
        Assert.AreEqual("1000 cls_m2m costs 0", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        Assert.AreEqual(0, free.Pay.Moved.Count + conversation.Effects.Count, "a price of 0 moves nothing");
        Assert.AreEqual(new DialogNodeId(30), conversation.CurrentNode);

        var one = new Rig(reaction: 50).Start(Nodes("m:$1,30"));
        Assert.AreEqual("1000 cls_m2m costs 2", one.NpcLine!.Text, "a scaled price of 1 is shown as 2 (0x418A40)");
    }

    [TestMethod]
    public void An_m_node_without_its_ports_or_with_arguments_outside_the_subset_stays_a_frontier()
    {
        foreach (var rig in new[] { new Rig { WithMoney = false }, new Rig { WithGreeter = false } })
        {
            var stopped = rig.Start(Nodes("m:$100,30"));
            Assert.AreEqual(DialogConversationPhase.AtFrontier, stopped.Phase);
            Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, stopped.Frontier!.Kind);
        }
        foreach (var text in new[] { "m:100,30", "m:$100", "m:$-1,30" })
        {
            var stopped = new Rig().Start(Nodes(text));
            Assert.AreEqual((DialogConversationPhase.AtFrontier, DialogFrontierKind.UnsupportedGeneratedText), (stopped.Phase, stopped.Frontier!.Kind), text);
        }
    }

    [TestMethod]
    public void The_follows_are_planned_before_the_action_runs()
    {
        var missing = new Rig();
        var failed = missing.Start(Nodes("m:$100,99", action: "MM 1"));
        Assert.AreEqual(DialogConversationFailureKind.MissingTarget, failed.Failure!.Kind);
        Assert.AreEqual(0, missing.World.Commands.Count, "a yes node that does not exist fails the step before its action runs");

        var player = new Rig().Start(Nodes("m:$100,40"));
        Assert.AreEqual(DialogConversationFailureKind.WrongTargetRole, player.Failure!.Kind, "the yes node must be an NPC node");

        var ownMissing = new Rig().Start(Nodes("m:$100,30", response: "99"));
        Assert.AreEqual(DialogConversationFailureKind.MissingTarget, ownMissing.Failure!.Kind, "so must the node's own response");

        var chained = new Rig().Start(Nodes("m:$100,30").Replace("{30}{Yes node.}", "{30}{m:$1,20}"));
        Assert.AreEqual((DialogConversationPhase.AtFrontier, DialogFrontierKind.UnsupportedGeneratedText), (chained.Phase, chained.Frontier!.Kind),
            "a follow that is itself a generated line stays a frontier (the follows are planned one level deep)");
    }

    [TestMethod]
    public void The_effect_budget_is_checked_before_the_payment_and_a_refused_payment_fails()
    {
        var tight = new Rig();
        var limited = tight.Start(Nodes("m:$100,30", action: "MM 1"), options: new DialogConversationOptions { MaxEffects = 1 });
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, limited.Select(limited.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, limited.Frontier!.Kind);
        Assert.AreEqual(0, tight.Pay.Moved.Count, "nothing is paid when the payment does not fit");

        var rejected = new Rig();
        rejected.Pay.Result = DialogCommandResult.Rejected;
        var conversation = rejected.Start(Nodes("m:$100,30"));
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, conversation.Failure!.Kind);
    }

    [TestMethod]
    public void An_r_node_draws_the_rumor_tells_it_and_the_only_answer_follows_the_node_s_own_response()
    {
        var rig = new Rig();
        var conversation = rig.Start(Nodes("r:$0,1000", action: "MM 1"));
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands, "the node's action ran first");
        Assert.AreEqual("Tale of 1000, Ada.", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "RU:1000" }, rig.Ports.Commands.Where(c => c.StartsWith("RU:")).ToArray(), "the rumor is written to the PC's logbook");
        var answer = conversation.Options.Single();
        Assert.AreEqual(("1000 cls_pc2m", DialogOptionTransition.Direct, new DialogNodeId(20)), (answer.Text, answer.Transition, answer.Target), "the node's replies are not built");
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void A_priced_r_node_asks_the_price_first_and_no_leaves_by_the_node_s_own_response()
    {
        var rig = new Rig(gold: 20, reaction: 0);
        var conversation = rig.Start(Nodes("r:$10,1000"));
        Assert.AreEqual("1000 cls_m2m costs 20", conversation.NpcLine!.Text, "the base price 10 on the reaction scale (factor 200)");
        Assert.AreEqual((DialogOptionTransition.RumorStep, DialogOptionTransition.Direct), (conversation.Options[0].Transition, conversation.Options[1].Transition));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -20) }, rig.Pay.Moved);
        Assert.AreEqual("Tale of 1000, Ada.", conversation.NpcLine!.Text);

        var no = new Rig();
        var declined = no.Start(Nodes("r:$10,1000"));
        Pick(declined, 2);
        Assert.AreEqual(new DialogNodeId(20), declined.CurrentNode);
        Assert.AreEqual(0, no.Pay.Moved.Count + no.Ports.Commands.Count);

        var known = new Rig();
        known.Rumors.Known.Add(1000);
        var none = known.Start(Nodes("r:$0,1000"));
        Assert.AreEqual("7000 cls_m2m", none.NpcLine!.Text, "nothing left to tell is line 7000, and the way on is the node's own response");
        Pick(none, 1);
        Assert.AreEqual(new DialogNodeId(20), none.CurrentNode);
    }

    [TestMethod]
    public void An_r_node_without_its_ports_or_with_a_rumor_outside_the_subset_stays_a_frontier()
    {
        foreach (var rig in new[] { new Rig { WithRumors = false }, new Rig { WithGreeter = false }, new Rig { WithMoney = false } })
        {
            var stopped = rig.Start(Nodes(rig.WithMoney ? "r:$0,1000" : "r:$10,1000"));
            Assert.AreEqual((DialogConversationPhase.AtFrontier, DialogFrontierKind.UnsupportedGeneratedText), (stopped.Phase, stopped.Frontier!.Kind));
        }
        Assert.AreEqual(DialogConversationPhase.AtFrontier, new Rig().Start(Nodes("r:$0,999")).Phase);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, new Rig { WithMoney = false }.Start(Nodes("r:$0,1000")).Phase, "a free rumor needs no money port");
    }

    [TestMethod]
    public void A_node_that_is_entered_by_a_reply_runs_the_line_like_the_first_node()
    {
        var rig = new Rig();
        var conversation = rig.Start("{10}{Hello.}{}{}{}{}{}{11}{Pay up.}{}{1}{}{20}{}{20}{m:$100,30}{}{}{}{}{}{30}{Paid.}{}{}{}{}{}");
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual("1000 cls_m2m costs 100", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        Assert.AreEqual((new DialogNodeId(30), "Paid."), (conversation.CurrentNode, conversation.NpcLine!.Text));
    }
}
