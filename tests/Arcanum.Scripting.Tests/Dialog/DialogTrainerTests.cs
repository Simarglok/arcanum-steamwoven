using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1150 (ledger DTR1–DTR9): the <c>t:</c> trainer reply as read in the executable (0x416C10 builds it, 0x414810 state 5, 0x418CA0 the menu,
/// 0x418DE0 a skill's answer, 0x418A40 the price, 0x418B30 the payment, 0x418F30 the training), over invented generated-dialog files whose every entry
/// names its own number and file, and invented skill, gold and reaction fixtures.
/// </summary>
[TestClass]
public sealed class DialogTrainerTests
{
    private sealed class Trainer(int gold) : IDialogTrainerPort
    {
        public int Gold { get; set; } = gold;
        public string? SkillName(int skill) => skill is >= 0 and <= 15 ? $"Skill{skill}" : null;
        public int PcGold(DialogGoldQuery query) => Gold;
    }

    private sealed class Pay(int gold) : IDialogGoldPort
    {
        public List<(long Pc, long Npc, int Amount)> Moved { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public int Total(DialogGoldQuery query) => gold;

        public DialogCommandResult Execute(GoldCommand command)
        {
            Moved.Add((command.PlayerCharacter.Value, command.Npc.Value, command.Amount));
            return Result;
        }
    }

    private sealed class Rig(int gold = 150, int reaction = 50)
    {
        public Trainer Trainer { get; } = new(gold);
        public Pay Pay { get; } = new(gold);
        public DialogSkillTests.Skills Skills { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts(), new DialogGreetingSources(Files(), new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        public bool WithTrainer { get; init; } = true;
        public bool WithGreeter { get; init; } = true;

        public DialogConversation Start(string replies, int callerLine = 0) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Skills: Skills, Gold: Pay, Greeting: WithGreeter ? Greeter : null,
                    Trainer: WithTrainer ? Trainer : null));
    }

    // Reply 11 is the trainer's; "response" 20 is the node after it.
    private static string TrainerReply(string text = "T:9, 11", string response = "20", string action = "") =>
        $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static DialogOption Only(DialogConversation conversation) => conversation.Options.Single();

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    [TestMethod]
    public void The_number_list_is_read_as_the_original_reads_it()
    {
        CollectionAssert.AreEqual(new[] { 9, 11 }, DialogConversation.ParseTrainerSkills("T:9, 11"));
        CollectionAssert.AreEqual(new[] { 4, 5, 6, 7 }, DialogConversation.ParseTrainerSkills("t: 4, 5, 6, 7"));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, DialogConversation.ParseTrainerSkills("T:1-3"));
        CollectionAssert.AreEqual(new[] { 12, 14, 15 }, DialogConversation.ParseTrainerSkills("T: 12, 14, 15"));
        CollectionAssert.AreEqual(new[] { 0 }, DialogConversation.ParseTrainerSkills("T:"), "an empty list reads one 0, as atoi of nothing does");
        Assert.IsNull(DialogConversation.ParseTrainerSkills("T:16"), "no skill");
        CollectionAssert.AreEqual(new[] { 0, 1 }, DialogConversation.ParseTrainerSkills("T:-1"), "the dash is found first: the range of an empty number (0) to 1");
        Assert.IsNull(DialogConversation.ParseTrainerSkills("T:1,2,3,4,5"), "five skills and the way out do not fit the five options");
        CollectionAssert.AreEqual(new[] { 0, 2 }, DialogConversation.ParseNumberList("0,2"));
        CollectionAssert.AreEqual(new[] { 5, 6 }, DialogConversation.ParseNumberList("5-6"));
        Assert.AreEqual(0, DialogConversation.ParseNumberList("9-3").Count, "a descending range holds nothing");
        Assert.AreEqual(100, DialogConversation.ParseNumberList("1-500").Count, "the list stops at 100 numbers");
    }

    [TestMethod]
    public void The_price_is_the_base_price_on_the_reaction_scale()
    {
        // 0x4C1150: the reaction counts at most 100; factor 200 below 0, 200 - 2r below 50, else 120 - trunc(2r / 5).
        Assert.AreEqual(200, DialogConversation.ReactionPrice(-40, 100));
        Assert.AreEqual(200, DialogConversation.ReactionPrice(0, 100));
        Assert.AreEqual(102, DialogConversation.ReactionPrice(49, 100));
        Assert.AreEqual(100, DialogConversation.ReactionPrice(50, 100));
        Assert.AreEqual(98, DialogConversation.ReactionPrice(55, 100));
        Assert.AreEqual(80, DialogConversation.ReactionPrice(100, 100));
        Assert.AreEqual(80, DialogConversation.ReactionPrice(900, 100), "above 100 counts as 100");
        Assert.AreEqual(2, DialogConversation.ReactionPrice(50, 1), "a scaled price of 1 is shown as 2 (0x418A40)");
        Assert.AreEqual("Pay 100 gold, 5% off.", DialogConversation.FormatPrice("Pay %d gold, 5%% off.", 100));
    }

    [TestMethod]
    public void A_t_reply_needs_the_trainer_the_skill_world_and_gold_ports_and_stays_blocked_without_them()
    {
        var rig = new Rig();
        var option = Only(rig.Start(TrainerReply()));
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Trainer, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("500 pc2m", option.Text, "the phrase is PC line 500..599; the numbers after the marker are not shown");
        Assert.AreEqual(new DialogGeneratedMarker('t'), option.Marker);

        var blocked = new Rig { WithTrainer = false }.Start(TrainerReply());
        Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported), (Only(blocked).Block!.Kind, Only(blocked).Transition));
        Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported),
            (Only(rig.Start(TrainerReply("T:16"))).Block!.Kind, Only(rig.Start(TrainerReply("T:16"))).Transition), "a number that is no skill stays outside the subset");

        var transfer = Only(rig.Start(TrainerReply(action: "FL 20")));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, transfer.Block!.Kind, "a dialog transfer in the same reply is outside the subset");
    }

    [TestMethod]
    public void Picking_the_reply_runs_its_action_then_shows_the_menu_of_the_listed_skills_and_the_way_out()
    {
        var rig = new Rig();
        var conversation = rig.Start(TrainerReply(action: "MM 1"));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands, "the action runs first");
        Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode, "the talk does not leave the node");
        Assert.AreEqual((DialogLineOriginKind.Generated, 3000), (conversation.NpcLine!.Origin!.Kind, conversation.NpcLine.Origin.Number));
        Assert.AreEqual("3000 cls_m2m", conversation.NpcLine.Text, "the NPC's class line 3000 (the greeting's class generator)");
        CollectionAssert.AreEqual(new[] { "Skill9", "Skill11", "800 pc2m" }, conversation.Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { DialogOptionTransition.TrainerStep, DialogOptionTransition.TrainerStep, DialogOptionTransition.Direct },
            conversation.Options.Select(o => o.Transition).ToArray(), "the way out carries the reply's own response");
        Assert.AreEqual(new DialogNodeId(20), conversation.Options[2].Target);
        Assert.AreEqual(0, rig.Skills.Commands.Count + rig.Pay.Moved.Count);
    }

    [TestMethod]
    public void The_way_out_follows_the_replys_own_response_without_running_its_action_again()
    {
        var rig = new Rig();
        var direct = rig.Start(TrainerReply(action: "MM 1"));
        Pick(direct, 1);
        Pick(direct, 3);
        Assert.AreEqual((new DialogNodeId(20), 1), (direct.CurrentNode, rig.World.Commands.Count), "the next node; the action ran once");

        var exit = new Rig().Start(TrainerReply(response: "0"), callerLine: 4);
        Pick(exit, 1);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, exit.Select(exit.Options[^1].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), exit.End);

        var line = new Rig().Start(TrainerReply(response: "-7"));
        Pick(line, 1);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, line.Select(line.Options[^1].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(7)), line.End);
    }

    [TestMethod]
    public void A_skill_the_pc_is_already_trained_in_is_answered_with_line_4000()
    {
        var rig = new Rig();
        rig.Skills.Trainings[9] = 1;
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("4000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual("600 pc2m", Only(conversation).Text);
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "the answer leads to the reply's response");
        Assert.AreEqual(0, rig.Skills.Commands.Count + rig.Pay.Moved.Count);
    }

    [TestMethod]
    public void A_skill_the_pc_cannot_be_taught_yet_is_answered_with_line_5000()
    {
        var rig = new Rig();
        rig.Skills.Levels[9] = 0;
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("5000 cls_m2m", conversation.NpcLine!.Text, "the setter refuses the first tier below level 1 (ledger SR1)");
        Assert.AreEqual(0, rig.Skills.Commands.Count, "the probe writes nothing here");
    }

    [TestMethod]
    public void A_teachable_skill_is_quoted_on_the_reaction_scale_and_the_replies_are_yes_and_no()
    {
        var rig = new Rig(reaction: 0);
        rig.Skills.Levels[11] = 2;
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual("1000 cls_m2m costs 200", conversation.NpcLine!.Text, "reaction 0: factor 200, the %d of the line is the price");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, conversation.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual((DialogOptionTransition.TrainerStep, DialogOptionTransition.Direct), (conversation.Options[0].Transition, conversation.Options[1].Transition));
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "no leaves by the reply's response");
        Assert.AreEqual(0, rig.Pay.Moved.Count + rig.Skills.Commands.Count);
    }

    [TestMethod]
    public void Paying_hands_the_price_to_the_npc_then_trains_the_first_tier_and_says_line_6000()
    {
        var rig = new Rig(gold: 150, reaction: 50);
        rig.Skills.Levels[11] = 3;
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual("1000 cls_m2m costs 100", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -100) }, rig.Pay.Moved, "the hero pays the NPC the price through the gold port");
        CollectionAssert.AreEqual(new[] { "TR:800:11:1" }, rig.Skills.Commands);
        Assert.AreEqual("6000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual("1000 cls_pc2m", Only(conversation).Text, "the answer after a training is the PC class line 1000");
        CollectionAssert.AreEqual(new[] { typeof(GoldCommand), typeof(SkillTrainingCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());
        Assert.IsTrue(conversation.Effects.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void A_tech_skill_is_numbered_as_the_source_numbers_it()
    {
        var rig = new Rig();
        rig.Skills.Levels[13] = 1;
        var conversation = rig.Start(TrainerReply("T:13"));
        Pick(conversation, 1);
        Assert.AreEqual("Skill13", conversation.Options[0].Text);
        Pick(conversation, 1);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "TR:800:13:1" }, rig.Skills.Commands);
    }

    [TestMethod]
    public void Too_little_gold_is_answered_with_line_2000_and_nothing_is_paid()
    {
        var rig = new Rig(gold: 99, reaction: 50);
        rig.Skills.Levels[9] = 2;
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m costs 100", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        Assert.AreEqual("2000 cls_m2m", conversation.NpcLine!.Text, "the hero's own gold must cover the whole price");
        Assert.AreEqual(0, rig.Pay.Moved.Count + rig.Skills.Commands.Count + conversation.Effects.Count);
        rig.Trainer.Gold = 100;
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void A_refused_payment_or_training_fails_the_conversation_and_an_unknown_reaction_stops_at_the_frontier()
    {
        var rejected = new Rig();
        rejected.Skills.Levels[9] = 2;
        rejected.Pay.Result = DialogCommandResult.Rejected;
        var conversation = rejected.Start(TrainerReply());
        Pick(conversation, 1);
        Pick(conversation, 1);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, conversation.Failure!.Kind);
        Assert.AreEqual(0, rejected.Skills.Commands.Count, "no training after a refused payment");

        var unknown = new Rig();
        unknown.Skills.Levels[9] = 2;
        unknown.World.Reaction = DialogReactionReadResult.Unknown();
        var stopped = unknown.Start(TrainerReply());
        Pick(stopped, 1);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, stopped.Select(stopped.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, stopped.Frontier!.Kind);
    }

    [TestMethod]
    public void A_rebuild_keeps_the_screen_and_old_tokens_go_stale()
    {
        var rig = new Rig();
        var conversation = rig.Start(TrainerReply());
        Pick(conversation, 1);
        var stale = conversation.Options[0].Token;
        var texts = conversation.Options.Select(o => o.Text).ToArray();
        var rolls = rig.Greeter.Asked.Count;
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.RefreshOptions().Status);
        CollectionAssert.AreEqual(texts, conversation.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(rolls, rig.Greeter.Asked.Count, "a rebuild rolls nothing");
        Assert.AreEqual(DialogConversationSelectStatus.NotOffered, conversation.Select(stale).Status);
        Assert.IsTrue(conversation.Close());
        Assert.AreEqual(DialogEndKind.ClosedByHost, conversation.End!.Kind);
    }

    [TestMethod]
    public void Without_the_greeting_port_the_talk_works_with_empty_lines()
    {
        var rig = new Rig { WithGreeter = false };
        rig.Skills.Levels[9] = 2;
        var conversation = rig.Start(TrainerReply("T:9"));
        Assert.AreEqual(string.Empty, Only(conversation).Text);
        Pick(conversation, 1);
        Assert.AreEqual(string.Empty, conversation.NpcLine!.Text);
        Assert.AreEqual("Skill9", conversation.Options[0].Text, "the skill names come from the trainer port");
        Pick(conversation, 1);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "TR:800:9:1" }, rig.Skills.Commands);
    }

    private static GeneratedDialogFiles Files()
    {
        // Every file holds the first entry of every range and class block the talk reads; the text is the number and the file, and the
        // quote's NPC line holds the price's %d.
        var numbers = new SortedSet<int> { 1, 100, 500, 600, 800 };
        for (var group = 1000; group <= 6000; group += 1000) numbers.Add(group);
        numbers.Add(1000);
        return GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count).Select(index =>
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            return (MesFile?)Mes(string.Concat(numbers.Select(number =>
                $"{{{number}}}{{{number} {name}{(number == 1000 && !name.Contains("pc2") ? " costs %d" : string.Empty)}}}")));
        }).ToArray());
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;
}
