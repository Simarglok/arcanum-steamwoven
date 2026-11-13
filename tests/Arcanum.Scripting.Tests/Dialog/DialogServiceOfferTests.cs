using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1798 (ledger DSV1–DSV5): the <c>u:N</c> skill service reply and the <c>z:N</c> spell reply as read in the executable (0x416C10 builds them with 0x4AE570, 0x419A00 and 0x419AC0,
/// 0x414810 states 0xE and 0xF open 0x419B50 and 0x419C30, the price screen 0x418A40 and the effects 0x419CB0 and 0x419D50), over invented generated-dialog files whose every entry names its own number and file.
/// </summary>
[TestClass]
public sealed class DialogServiceOfferTests
{
    private sealed class Healer : IDialogHealerPort
    {
        public DialogHealerFacts Facts { get; set; } = new(5, true, 4, 1, false, true);
        public bool ThrowOnFacts { get; set; }
        public List<int> Asked { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public List<string> Commands { get; } = [];
        DialogHealerFacts IDialogHealerPort.Facts(DialogHealerQuery query)
        {
            Asked.Add(query.Skill);
            return ThrowOnFacts ? throw new InvalidOperationException() : Facts;
        }
        public string? SkillName(int skill) => skill == 10 ? "Heal" : skill == 3 ? "Throwing" : null;
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

    private static string Reply(int number, string text, string response = "20", string action = "") =>
        $"{{{number}}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static DialogOption Only(DialogConversation conversation) => conversation.Options.Single();

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    private static string[] Texts(DialogConversation conversation) => conversation.Options.Select(o => o.Text).ToArray();

    [TestMethod]
    public void A_heal_service_reply_is_offered_only_when_the_npc_may_heal_the_pc()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply(11, "u:10"));
        var option = Only(conversation);
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Healer, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("900 pc2m Heal", option.Text, "PC line 900..999 with the skill's name");
        Assert.AreEqual(new DialogGeneratedMarker('u'), option.Marker);
        CollectionAssert.AreEqual(new[] { 10 }, rig.Healer.Asked, "0x4AE570 for skill 10");

        var refused = new Rig();
        refused.Healer.Facts = new(5, false, 4, 1, false, true);
        var other = refused.Start(Reply(11, "U:10") + Reply(12, "Plain."));
        CollectionAssert.AreEqual(new[] { "Plain." }, Texts(other), "the skill test answers non-zero: the reply is left out");
    }

    [TestMethod]
    public void The_left_out_service_reply_takes_a_slot_of_the_five_eligible_ones()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(5, false, 4, 1, false, true);
        var replies = Reply(11, "u:10") + string.Concat(Enumerable.Range(1, 5).Select(index => Reply(11 + index, $"Reply {index}.")));
        CollectionAssert.AreEqual(new[] { "Reply 1.", "Reply 2.", "Reply 3.", "Reply 4." }, Texts(rig.Start(replies)));
    }

    [TestMethod]
    public void The_repair_and_pick_locks_service_replies_are_never_offered_and_any_other_skill_always_is()
    {
        var rig = new Rig();
        foreach (var text in new[] { "u:12", "u:14", "U: 14", "u:+14", "u:014" })
        {
            CollectionAssert.AreEqual(new[] { "Plain." }, Texts(rig.Start(Reply(11, text) + Reply(12, "Plain."))), text);
        }
        Assert.AreEqual(0, rig.Healer.Asked.Count, "their test is answered without the host: the PC is no item, portal or container");
        var throwing = Only(rig.Start(Reply(11, "u:3")));
        Assert.AreEqual("900 pc2m Throwing", throwing.Text);
        Assert.AreEqual("900 pc2m ", Only(rig.Start(Reply(11, "u:5"))).Text, "a skill without a name shows an empty name");
        Assert.AreEqual(0, rig.Healer.Asked.Count, "only skill 10 is asked for");
    }

    [TestMethod]
    public void A_service_reply_without_the_healer_ports_stays_a_frontier_and_the_spell_reply_stays_blocked()
    {
        var bare = new Rig { WithHealer = false };
        var frontier = bare.Start(Reply(11, "u:10"));
        Assert.AreEqual((DialogConversationPhase.AtFrontier, DialogFrontierKind.UnsupportedGeneratedText), (frontier.Phase, frontier.Frontier!.Kind));
        var blocked = Only(bare.Start(Reply(11, "z:60")));
        Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported), (blocked.Block!.Kind, blocked.Transition));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, Only(new Rig().Start(Reply(11, "u:10", action: "FL 20"))).Block!.Kind, "a dialog transfer in the same reply is outside the subset");
    }

    [TestMethod]
    public void A_blocked_service_reply_keeps_its_test_and_its_named_phrase()
    {
        var refused = new Rig();
        refused.Healer.Facts = new(5, false, 4, 1, false, true);
        CollectionAssert.AreEqual(new[] { "Plain." }, Texts(refused.Start(Reply(11, "u:10", action: "FL 20") + Reply(12, "Plain."))),
            "the builder drops a reply the NPC cannot serve before any window or transfer of its action matters");
        var blocked = Only(new Rig().Start(Reply(11, "z:62", action: "FL 20")));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, blocked.Block!.Kind);
        Assert.AreEqual("1000 pc2m Spell62", blocked.Text, "a blocked spell reply still shows its named line");
    }

    [TestMethod]
    public void The_skill_service_is_quoted_by_the_named_skills_level_and_tier_paid_and_run()
    {
        var rig = new Rig(reaction: 0);
        rig.Healer.Facts = new(0, true, 3, 2, false, true);
        var conversation = rig.Start(Reply(11, "u:3", action: "MM 1"));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands, "the reply's action runs first");
        CollectionAssert.AreEqual(new[] { 3 }, rig.Healer.Asked, "only the price reads the skill the reply names (skill 3 passes the test)");
        Assert.AreEqual("1000 cls_m2m costs 30", conversation.NpcLine!.Text, "(level 3 + 2) × (tier 2 + 1) = 15, doubled on reaction 0");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, Texts(conversation));
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -30) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { "skill:3:True" }, rig.Healer.Commands);
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual("1000 cls_pc2m", Only(conversation).Text);
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "the reply's own response follows");
    }

    [TestMethod]
    public void A_followers_skill_service_runs_at_once_and_free_and_saying_no_leaves_by_the_response()
    {
        var led = new Rig();
        led.Healer.Facts = new(0, true, 2, 0, true, false);
        var follower = led.Start(Reply(11, "u:10"));
        Pick(follower, 1);
        CollectionAssert.AreEqual(new[] { "skill:10:False" }, led.Healer.Commands);
        Assert.AreEqual(0, led.Pay.Moved.Count);
        Assert.AreEqual("15000 cls_m2m", follower.NpcLine!.Text);

        var rig = new Rig();
        rig.Healer.Facts = new(0, true, 2, 0, false, true);
        var conversation = rig.Start(Reply(11, "u:10"));
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual(0, rig.Healer.Commands.Count + rig.Pay.Moved.Count);
    }

    [TestMethod]
    public void A_spell_reply_is_always_offered_and_sells_the_named_spell_for_100_on_the_reaction_scale()
    {
        var rig = new Rig(reaction: 50);
        rig.Healer.Facts = new(0, false, 0, 0, false, true);
        var conversation = rig.Start(Reply(11, "Z:62 sold", action: "MM 1"));
        var option = Only(conversation);
        Assert.AreEqual((DialogOptionTextKind.Generated, "1000 pc2m Spell62"), (option.TextKind, option.Text));
        Assert.AreEqual(new DialogGeneratedMarker('z'), option.Marker);
        Assert.AreEqual(0, rig.Healer.Asked.Count, "no filter: the spell reply never asks the skill test");
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands);
        Assert.AreEqual("1000 cls_m2m costs 100", conversation.NpcLine!.Text);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -100) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { "spell:62:True" }, rig.Healer.Commands, "no PC leads the NPC: the cast is free (flag 2)");
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);

        var led = new Rig();
        led.Healer.Facts = new(0, false, 0, 0, true, false);
        var follower = led.Start(Reply(11, "z:60"));
        Pick(follower, 1);
        CollectionAssert.AreEqual(new[] { "spell:60:False" }, led.Healer.Commands, "a follower casts it at once and free of charge");
        Assert.AreEqual(0, led.Pay.Moved.Count);
    }

    [TestMethod]
    public void A_spell_number_the_original_reads_with_atoi_is_kept_as_it_is()
    {
        var rig = new Rig();
        rig.Healer.Facts = new(0, false, 0, 0, true, false);
        var conversation = rig.Start(Reply(11, "z:x"));
        Assert.AreEqual("1000 pc2m Spell0", Only(conversation).Text, "no digits: spell 0");
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { "spell:0:False" }, rig.Healer.Commands);
    }

    [TestMethod]
    public void A_failing_port_fails_the_conversation()
    {
        var rig = new Rig { Healer = { ThrowOnFacts = true } };
        var conversation = rig.Start(Reply(11, "u:10"));
        Assert.AreEqual(DialogConversationPhase.Failed, conversation.Phase);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, conversation.Failure!.Kind);
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
