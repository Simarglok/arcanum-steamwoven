using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-UI-1739 (ledger NP1–NP8): the <c>p:</c> newspaper reply as read in the executable (0x416C10 builds it, 0x414810 states 0x19 to 0x1d → 0x41A520 greets, 0x41A620 says the headline, 0x41A700 lists the back issues,
/// 0x41A880 and 0x418A40 ask the price, 0x418B30 takes it, 0x41A8C0 gives the issue), over invented generated-dialog files and invented issue, gold and reaction fixtures.
/// </summary>
[TestClass]
public sealed class DialogNewspaperTests
{
    private sealed class Stand(int head, params int[] back) : IDialogNewspaperStandPort
    {
        public int Head { get; set; } = head;
        public List<int> Back { get; } = [.. back];
        public Dictionary<int, string> Headlines { get; } = new() { [5000] = "Fire in the port", [1001] = "Old news!", [1002] = "Older news?", [1003] = "Oldest" };
        public List<int> Given { get; } = [];
        public bool Throw { get; set; }
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public DialogNewspaperIssues Issues() => Throw ? throw new InvalidOperationException() : new(Head, Back);
        public string Headline(int issue) => Throw ? throw new InvalidOperationException() : Headlines.GetValueOrDefault(issue, string.Empty);

        public DialogCommandResult Execute(GiveNewspaperCommand command)
        {
            Assert.AreEqual((new DialogParticipantId(900), new DialogParticipantId(800)), (command.Npc, command.PlayerCharacter));
            Given.Add(command.Issue);
            return Result;
        }
    }

    private sealed class Purse(int gold) : IDialogPcGoldPort
    {
        public int Gold { get; set; } = gold;
        public int PcGold(DialogGoldQuery query) => Gold;
    }

    private sealed class Pay : IDialogGoldPort
    {
        public List<(long Pc, long Npc, int Amount)> Moved { get; } = [];
        public int Total(DialogGoldQuery query) => 1000;

        public DialogCommandResult Execute(GoldCommand command)
        {
            Moved.Add((command.PlayerCharacter.Value, command.Npc.Value, command.Amount));
            return DialogCommandResult.Applied;
        }
    }

    private sealed class Rig(int gold = 500, int reaction = 0, int head = 5000, params int[] back)
    {
        public Stand Stand { get; } = new(head, back);
        public Purse Purse { get; } = new(gold);
        public Pay Pay { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts(), new DialogGreetingSources(Files(), new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        public bool WithStand { get; init; } = true;
        public bool WithMoney { get; init; } = true;

        public DialogConversation Start(string replies, int callerLine = 0, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Gold: WithMoney ? Pay : null, Greeting: Greeter, Purse: WithMoney ? Purse : null,
                    NewspaperStand: WithStand ? Stand : null), options);
    }

    // The files: every range the talk reads holds its first entry; the text is the number and the file, the quote's line (1000 of an NPC class file) holds %d.
    private static GeneratedDialogFiles Files()
    {
        var numbers = new[] { 1, 100, 600, 800, 1000, 1900, 2000, 2100, 2200, 4200, 4300, 4400, 4500, 15000 };
        return GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count).Select(index =>
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            return (MesFile?)Mes(string.Concat(numbers.Select(number => $"{{{number}}}{{{number} {name}{(number == 1000 && !name.Contains("pc2") ? " %d" : string.Empty)}}}")));
        }).ToArray());
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static string Reply(string text, string response = "20", string action = "") => $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    private static string[] Texts(DialogConversation conversation) => conversation.Options.Select(o => o.Text).ToArray();

    [TestMethod]
    public void A_p_reply_is_a_generated_phrase_and_a_missing_port_leaves_it_blocked()
    {
        var rig = new Rig();
        var option = rig.Start(Reply("p:")).Options.Single();
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Newspaper, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("1900 pc2m", option.Text);
        Assert.AreEqual(new DialogGeneratedMarker('p'), option.Marker);
        Assert.AreEqual("1900 pc2m", rig.Start(Reply("P:")).Options.Single().Text, "either case");

        foreach (var blocked in new[] { new Rig { WithStand = false }, new Rig { WithMoney = false } })
        {
            var stuck = blocked.Start(Reply("p:")).Options.Single();
            Assert.AreEqual(DialogOptionTransition.Unsupported, stuck.Transition);
            Assert.IsNotNull(stuck.Block);
        }
        var plain = rig.Start(Reply("p: more")).Options.Single();
        Assert.AreEqual((DialogOptionTransition.Direct, "p: more"), (plain.Transition, plain.Text), "only the exact marker is the reply: more text is an ordinary line");
    }

    [TestMethod]
    public void Picking_it_opens_the_greeting_with_four_ways_on_and_the_shown_issue_is_not_read_for_the_text()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Assert.AreEqual("4200 npc_m2m", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "2200 pc2m", "2000 pc2m", "2100 pc2m", "800 pc2m" }, Texts(conversation));
        Assert.IsTrue(conversation.Options.All(o => o.Transition == DialogOptionTransition.NewspaperStep || o.Transition == DialogOptionTransition.Direct));
        Assert.AreEqual(0, conversation.Effects.Count, "nothing is bought or written by looking");
    }

    [TestMethod]
    public void The_headline_is_said_with_a_closing_stop_and_a_line_of_4300_and_yes_goes_to_the_price()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual("Fire in the port. 4300 npc_m2m", conversation.NpcLine!.Text, "no stop in the headline: one is added, then a space");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, Texts(conversation));
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m 4", conversation.NpcLine!.Text, "reaction 0: factor 200 of the base price 2");

        rig.Stand.Headlines[5000] = "Calm day!";
        var calm = rig.Start(Reply("p:"));
        Pick(calm, 1);
        Pick(calm, 2);
        Assert.AreEqual("Calm day! 4300 npc_m2m", calm.NpcLine!.Text, "a headline that ends in ! ? or . gets only the space");

        rig.Stand.Headlines[5000] = string.Empty;
        var empty = rig.Start(Reply("p:"));
        Pick(empty, 1);
        Pick(empty, 2);
        Assert.AreEqual("4300 npc_m2m", empty.NpcLine!.Text, "an empty headline adds nothing");
    }

    [TestMethod]
    public void The_back_issues_are_options_with_their_headlines_and_none_gives_line_4400()
    {
        var rig = new Rig(head: 5000, back: [1001, 1003]);
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 3);
        Assert.AreEqual("4500 npc_m2m", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "Old news!", "Oldest", "800 pc2m" }, Texts(conversation));
        Pick(conversation, 2);
        Assert.AreEqual("1000 cls_m2m 4", conversation.NpcLine!.Text, "an issue goes to the price screen");

        var none = new Rig();
        var empty = none.Start(Reply("p:"));
        Pick(empty, 1);
        Pick(empty, 3);
        Assert.AreEqual("4400 npc_m2m", empty.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "600 pc2m" }, Texts(empty));
        Pick(empty, 1);
        Assert.AreEqual(new DialogNodeId(20), empty.CurrentNode, "the single answer follows the reply's response");
    }

    [TestMethod]
    public void Yes_pays_the_npc_gives_the_issue_and_closes_with_the_class_lines()
    {
        var rig = new Rig(gold: 20, reaction: 0);
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m 4", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, Texts(conversation));
        Assert.AreEqual(0, rig.Pay.Moved.Count + rig.Stand.Given.Count, "nothing happens before yes");
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -4) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { 5000 }, rig.Stand.Given);
        CollectionAssert.AreEqual(new[] { typeof(GoldCommand), typeof(GiveNewspaperCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());
        Assert.AreEqual("15000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual(1, conversation.Options.Count);
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "leaving follows the reply's response");
    }

    [TestMethod]
    public void A_back_issue_is_given_by_its_own_number_and_no_leaves_by_the_response()
    {
        var rig = new Rig(back: [1001, 1002]);
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 3);
        Pick(conversation, 2);
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { 1002 }, rig.Stand.Given);

        var no = new Rig();
        var declined = no.Start(Reply("p:"));
        Pick(declined, 1);
        Pick(declined, 1);
        Pick(declined, 2);
        Assert.AreEqual(new DialogNodeId(20), declined.CurrentNode);
        Assert.AreEqual(0, no.Pay.Moved.Count + no.Stand.Given.Count + declined.Effects.Count);

        var leave = new Rig().Start(Reply("p:"));
        Pick(leave, 1);
        Pick(leave, 4);
        Assert.AreEqual(new DialogNodeId(20), leave.CurrentNode, "the fourth way out follows the reply's response too");
    }

    [TestMethod]
    public void Too_little_own_gold_is_line_2000_and_nothing_is_given()
    {
        var rig = new Rig(gold: 3);
        var conversation = rig.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("2000 cls_m2m", conversation.NpcLine!.Text);
        Assert.AreEqual(0, rig.Pay.Moved.Count + rig.Stand.Given.Count);
        Assert.AreEqual(0, conversation.Effects.Count);
        CollectionAssert.AreEqual(new[] { "600 pc2m" }, Texts(conversation));
    }

    [TestMethod]
    public void Failures_are_typed_and_a_refused_issue_is_a_rejected_command()
    {
        var refused = new Rig { };
        refused.Stand.Result = DialogCommandResult.Rejected;
        var conversation = refused.Start(Reply("p:"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, conversation.Failure!.Kind);

        var throwing = new Rig();
        var talk = throwing.Start(Reply("p:"));
        throwing.Stand.Throw = true;
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, talk.Failure!.Kind);

        var crowded = new Rig(back: [1, 2, 3, 4, 5]);
        var many = crowded.Start(Reply("p:"));
        many.Select(many.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, many.Failure!.Kind, "the table keeps four reserved words");
    }

    [TestMethod]
    public void The_stops_of_a_headline_are_added_as_the_original_does()
    {
        Assert.AreEqual("Fire. ", DialogConversation.PunctuatedHeadline("Fire"));
        Assert.AreEqual("Fire! ", DialogConversation.PunctuatedHeadline("Fire!"));
        Assert.AreEqual("Fire? ", DialogConversation.PunctuatedHeadline("Fire?"));
        Assert.AreEqual("Fire. ", DialogConversation.PunctuatedHeadline("Fire."));
        Assert.AreEqual(string.Empty, DialogConversation.PunctuatedHeadline(string.Empty));
    }
}
