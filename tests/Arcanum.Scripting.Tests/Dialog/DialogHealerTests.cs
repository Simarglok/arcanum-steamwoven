using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1732 (ledger HLR1–HLR12): the <c>h:</c> healer reply as read in the executable (0x416C10 builds it, 0x414810 state 11, 0x419830 the menu, 0x419A00 and 0x419AC0
/// the generated options, 0x419B50 and 0x419C30 the price, 0x419CB0 and 0x419D50 the effects), over invented generated-dialog files whose every entry names its own number and file.
/// </summary>
[TestClass]
public sealed class DialogHealerTests
{
    private sealed class Healer : IDialogHealerPort
    {
        public DialogHealerFacts Facts { get; set; } = new(5, true, 4, 1, false, true);
        public bool ThrowOnFacts { get; set; }
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public List<string> Commands { get; } = [];
        DialogHealerFacts IDialogHealerPort.Facts(DialogHealerQuery query) => ThrowOnFacts ? throw new InvalidOperationException() : Facts;
        public string? SkillName(int skill) => skill == 10 ? "Heal" : null;
        public string? SpellName(int spell) => $"Spell{spell}";

        public DialogCommandResult Execute(HealerSkillCommand command)
        {
            Commands.Add($"skill:{command.Skill}:{command.NoPcLeader}");
            return Result;
        }

        public DialogCommandResult Execute(HealerSpellCommand command)
        {
            Commands.Add($"spell:{command.Spell}:{command.Free}");
            return Result;
        }
    }

    private sealed class Pay : IDialogGoldPort, IDialogPcGoldPort
    {
        public int Gold { get; set; } = 1000;
        public List<(long Pc, long Npc, int Amount)> Moved { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public int Total(DialogGoldQuery query) => Gold;
        public int PcGold(DialogGoldQuery query) => Gold;

        public DialogCommandResult Execute(GoldCommand command)
        {
            Moved.Add((command.PlayerCharacter.Value, command.Npc.Value, command.Amount));
            return Result;
        }
    }

    private sealed class Rig(int reaction = 50)
    {
        public Healer Healer { get; } = new();
        public Pay Pay { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts(), new DialogGreetingSources(Files(), new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        public bool WithHealer { get; init; } = true;

        public DialogConversation Start(string replies, int callerLine = 0, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Gold: Pay, Greeting: Greeter, Purse: Pay, Healer: WithHealer ? Healer : null), options);
    }

    // Reply 11 is the healer's; "response" 20 is the node after it.
    private static string HealerReply(string text = "H:", string response = "20", string action = "") =>
        $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static DialogOption Only(DialogConversation conversation) => conversation.Options.Single();

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    private static string[] Texts(DialogConversation conversation) => conversation.Options.Select(o => o.Text).ToArray();

    private static DialogConversation Open(Rig rig, string action = "")
    {
        var conversation = rig.Start(HealerReply(action: action));
        Pick(conversation, 1);
        return conversation;
    }

    [TestMethod]
    public void An_h_reply_needs_the_healer_world_gold_greeting_and_purse_ports_and_stays_blocked_without_them()
    {
        var rig = new Rig();
        var option = Only(rig.Start(HealerReply()));
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Healer, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("700 pc2m", option.Text, "the phrase is PC line 700..799");
        Assert.AreEqual(new DialogGeneratedMarker('h'), option.Marker);

        var blocked = Only(new Rig { WithHealer = false }.Start(HealerReply()));
        Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported), (blocked.Block!.Kind, blocked.Transition));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, Only(rig.Start(HealerReply(action: "FL 20"))).Block!.Kind, "a dialog transfer in the same reply is outside the subset");
    }

    [TestMethod]
    public void Picking_the_reply_runs_its_action_then_shows_the_menu_of_a_full_healer()
    {
        var rig = new Rig();
        var conversation = Open(rig, "MM 1");
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands, "the action runs first");
        Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode, "the talk does not leave the node");
        Assert.AreEqual((DialogLineOriginKind.Generated, 14000), (conversation.NpcLine!.Origin!.Kind, conversation.NpcLine.Origin.Number));
        Assert.AreEqual("14000 cls_m2m", conversation.NpcLine.Text);
        CollectionAssert.AreEqual(new[] { "900 pc2m Heal", "1100 pc2m", "1200 pc2m", "1000 pc2m Spell64", "800 pc2m" }, Texts(conversation),
            "the skill service (PC line 900 with the skill's name), the two submenus, the spell 64 (PC line 1000 with the spell's name) and the way out");
        CollectionAssert.AreEqual(new[] { DialogOptionTransition.HealerStep, DialogOptionTransition.HealerStep, DialogOptionTransition.HealerStep, DialogOptionTransition.HealerStep, DialogOptionTransition.Direct },
            conversation.Options.Select(o => o.Transition).ToArray());
        Assert.AreEqual(new DialogNodeId(20), conversation.Options[4].Target, "the way out carries the reply's own response");
        Assert.AreEqual(0, rig.Healer.Commands.Count + rig.Pay.Moved.Count);
    }

    [TestMethod]
    public void The_options_follow_the_rank_and_the_skill_test()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(0, false, 0, 0, false, true);
        CollectionAssert.AreEqual(new[] { "800 pc2m" }, Texts(Open(rig)), "a rank of 0 and no skill: only the way out");
        rig.Healer.Facts = new(0, true, 0, 0, false, true);
        CollectionAssert.AreEqual(new[] { "900 pc2m Heal", "800 pc2m" }, Texts(Open(rig)));
        rig.Healer.Facts = new(1, false, 0, 0, false, true);
        CollectionAssert.AreEqual(new[] { "1100 pc2m", "800 pc2m" }, Texts(Open(rig)), "rank 1: the first submenu only");
        rig.Healer.Facts = new(4, false, 0, 0, false, true);
        CollectionAssert.AreEqual(new[] { "1100 pc2m", "1200 pc2m", "800 pc2m" }, Texts(Open(rig)), "the spell 64 needs the rank to be exactly 5");
    }

    [TestMethod]
    public void The_submenus_offer_the_spells_60_62_and_61_by_rank()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(3, false, 0, 0, false, true);
        var conversation = Open(rig);
        Pick(conversation, 1);
        Assert.AreEqual("14000 cls_m2m", conversation.NpcLine!.Text, "a submenu has the same NPC line");
        CollectionAssert.AreEqual(new[] { "1000 pc2m Spell60", "1000 pc2m Spell62", "800 pc2m" }, Texts(conversation), "rank above 0: spell 60; rank above 2: spell 62");
        Pick(conversation, 3);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "leaving a submenu takes the reply's response out of the talk");

        var second = Open(rig);
        Pick(second, 2);
        CollectionAssert.AreEqual(new[] { "1000 pc2m Spell61", "800 pc2m" }, Texts(second), "the second submenu holds the spell 61 (rank above 1)");

        rig.Healer.Facts = new(2, false, 0, 0, false, true);
        var low = Open(rig);
        Pick(low, 1);
        CollectionAssert.AreEqual(new[] { "1000 pc2m Spell60", "800 pc2m" }, Texts(low), "rank 2 has no spell 62");
    }

    [TestMethod]
    public void The_skill_service_is_quoted_by_level_and_tier_on_the_reaction_scale_then_paid_and_run()
    {
        var rig = new Rig(reaction: 0);
        rig.Healer.Facts = new(0, true, 3, 2, false, true);
        var conversation = Open(rig);
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m costs 30", conversation.NpcLine!.Text, "(level 3 + 2) × (tier 2 + 1) = 15, doubled on reaction 0");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, Texts(conversation));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -30) }, rig.Pay.Moved, "the hero pays the NPC through the gold port");
        CollectionAssert.AreEqual(new[] { "skill:10:True" }, rig.Healer.Commands);
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual("1000 cls_pc2m", Only(conversation).Text, "the only way on is the PC class line 1000");
        CollectionAssert.AreEqual(new[] { typeof(GoldCommand), typeof(HealerSkillCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "the reply's own response follows");
    }

    [TestMethod]
    public void A_spell_costs_100_on_the_reaction_scale_and_runs_with_the_free_flag_of_a_leaderless_npc()
    {
        var rig = new Rig(reaction: 50);
        rig.Healer.Facts = new(3, false, 0, 0, false, true);
        var conversation = Open(rig);
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual("1000 cls_m2m costs 100", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -100) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { "spell:62:True" }, rig.Healer.Commands, "no PC leads the NPC: the cast is free (flag 2)");
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);

        var led = new Rig();
        led.Healer.Facts = new(3, false, 0, 0, true, false);
        var follower = Open(led);
        Pick(follower, 1);
        Pick(follower, 1);
        CollectionAssert.AreEqual(new[] { "spell:60:False" }, led.Healer.Commands, "the PC's follower heals at once, free of charge, and the cast is its own (no free flag)");
        Assert.AreEqual(0, led.Pay.Moved.Count, "the NPC's own leader is the PC: no price screen, no payment");
        Assert.AreEqual("15000 cls_m2m", follower.NpcLine!.Text);
        Assert.AreEqual(typeof(HealerSpellCommand), follower.Effects.Single().Command.GetType());
    }

    [TestMethod]
    public void A_followers_skill_service_runs_without_a_price()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(0, true, 2, 0, true, false);
        var conversation = Open(rig);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "skill:10:False" }, rig.Healer.Commands);
        Assert.AreEqual(0, rig.Pay.Moved.Count);
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);
    }

    [TestMethod]
    public void Saying_no_to_the_price_leaves_by_the_replys_response_and_too_little_gold_gives_line_2000()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(0, true, 2, 0, false, true);
        var conversation = Open(rig);
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual(0, rig.Healer.Commands.Count + rig.Pay.Moved.Count);

        var poor = new Rig();
        poor.Healer.Facts = new(0, true, 2, 0, false, true);
        poor.Pay.Gold = 3;
        var talk = Open(poor);
        Pick(talk, 1);
        Assert.AreEqual("1000 cls_m2m costs 4", talk.NpcLine!.Text);
        Pick(talk, 1);
        Assert.AreEqual("2000 cls_m2m", talk.NpcLine!.Text, "the hero's own gold must cover the whole price");
        Assert.AreEqual(0, poor.Pay.Moved.Count + poor.Healer.Commands.Count + talk.Effects.Count);
    }

    [TestMethod]
    public void A_refused_payment_or_effect_fails_the_conversation_and_the_effect_budget_is_checked_first()
    {
        var refused = new Rig();
        refused.Healer.Facts = new(0, true, 2, 0, false, true);
        refused.Pay.Result = DialogCommandResult.Rejected;
        var paying = Open(refused);
        Pick(paying, 1);
        paying.Select(paying.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, paying.Failure!.Kind);
        Assert.AreEqual(0, refused.Healer.Commands.Count, "no effect after a refused payment");

        var host = new Rig();
        host.Healer.Facts = new(0, true, 2, 0, true, false);
        host.Healer.Result = DialogCommandResult.Rejected;
        var talk = Open(host);
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, talk.Failure!.Kind, "a host that cannot start the effect");

        var tight = new Rig();
        tight.Healer.Facts = new(0, true, 2, 0, false, true);
        var limited = tight.Start(HealerReply(), options: new DialogConversationOptions { MaxEffects = 1 });
        Pick(limited, 1);
        Pick(limited, 1);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, limited.Select(limited.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, limited.Frontier!.Kind);
        Assert.AreEqual(0, tight.Pay.Moved.Count, "nothing is paid when the payment and the effect do not both fit");
    }

    [TestMethod]
    public void A_throwing_or_impossible_facts_read_is_a_typed_failure_and_an_unknown_reaction_stops_at_the_frontier()
    {
        var throwing = new Rig();
        throwing.Healer.ThrowOnFacts = true;
        var conversation = throwing.Start(HealerReply());
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind);

        var impossible = new Rig();
        impossible.Healer.Facts = new(6, false, 0, 0, false, true);
        var bad = impossible.Start(HealerReply());
        bad.Select(bad.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, bad.Failure!.Kind);

        var unknown = new Rig();
        unknown.Healer.Facts = new(0, true, 2, 0, false, true);
        unknown.World.Reaction = DialogReactionReadResult.Unknown();
        var stopped = Open(unknown);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, stopped.Select(stopped.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, stopped.Frontier!.Kind);
    }

    [TestMethod]
    public void The_way_out_follows_the_replys_response_and_a_rebuild_keeps_the_screen()
    {
        var exit = new Rig().Start(HealerReply(response: "0"), callerLine: 4);
        Pick(exit, 1);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, exit.Select(exit.Options[^1].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), exit.End);

        var line = new Rig().Start(HealerReply(response: "-7"));
        Pick(line, 1);
        line.Select(line.Options[^1].Token);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(7)), line.End);

        var rig = new Rig();
        var conversation = Open(rig);
        var stale = conversation.Options[0].Token;
        var texts = Texts(conversation);
        var rolls = rig.Greeter.Asked.Count;
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.RefreshOptions().Status);
        CollectionAssert.AreEqual(texts, Texts(conversation));
        Assert.AreEqual(rolls, rig.Greeter.Asked.Count, "a rebuild rolls nothing");
        Assert.AreEqual(DialogConversationSelectStatus.NotOffered, conversation.Select(stale).Status);
    }

    [TestMethod]
    public void A_name_fills_the_first_percent_s_as_printf_does()
    {
        Assert.AreEqual("Heal me with Mending, 5%.", DialogConversation.FormatName("Heal me with %s, 5%%.", "Mending"));
        Assert.AreEqual("No name.", DialogConversation.FormatName("No name.", "x"));
        Assert.AreEqual("a b %s", DialogConversation.FormatName("a %s %s", "b"));
    }

    private static GeneratedDialogFiles Files()
    {
        // Every file holds the first entry of every range and class block the talk reads; the text is the number and the file, the quote's NPC line holds the price's %d
        // and the PC lines 900 and 1000 hold the %s of a name.
        var numbers = new SortedSet<int> { 1, 100, 500, 600, 700, 800, 900, 1000, 1100, 1200 };
        for (var group = 1000; group <= 7000; group += 1000) numbers.Add(group);
        numbers.Add(14000);
        numbers.Add(15000);
        return GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count).Select(index =>
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            return (MesFile?)Mes(string.Concat(numbers.Select(number =>
                $"{{{number}}}{{{number} {name}{(number == 1000 && !name.Contains("pc2") ? " costs %d" : string.Empty)}{(number is 900 or 1000 && name == "pc2m" ? " %s" : string.Empty)}}}")));
        }).ToArray());
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;
}
