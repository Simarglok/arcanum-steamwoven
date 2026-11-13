using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1533 (ledger DIR1–DIR9): the <c>d:</c> and <c>x:</c> directions replies as read in the executable (0x416C10 builds them and leaves them out while the PC
/// knows every area, 0x414810 states 0x12 and 0x15 → 0x419E70 pages the unknown areas, 0x41A0F0 and 0x41A230 ask the price, 0x41A150 and 0x41A290 tell the way), over
/// invented generated-dialog files and invented area, gold and reaction fixtures.
/// </summary>
[TestClass]
public sealed class DialogDirectionsTests
{
    private sealed class Directions(Dictionary<int, string> names) : IDialogDirectionsPort
    {
        public DialogBearing? Way { get; set; } = new(3, 50 * 0xC60L);
        public List<int> Asked { get; } = [];
        public bool Throw { get; set; }
        public string? AreaName(int area) => Throw ? throw new InvalidOperationException() : names.GetValueOrDefault(area);

        public DialogBearing? Bearing(DialogParticipantId npc, int area)
        {
            Assert.AreEqual(new DialogParticipantId(900), npc);
            Asked.Add(area);
            return Throw ? throw new InvalidOperationException() : Way;
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

    private sealed class Rig(int gold = 500, int reaction = 50, bool led = false, params int[] known)
    {
        public Directions Directions { get; } = new(new() { [1] = "Alpha", [2] = "Beta", [3] = "Gamma", [4] = "Delta", [5] = "Epsilon", [6] = "Zeta" });
        public DialogStoryAreaTests.Areas Areas { get; } = new(known);
        public Purse Purse { get; } = new(gold);
        public Pay Pay { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public Ports Ports { get; } = new();
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(DialogGreetingTests.Facts() with { NpcLedByPc = led },
            new DialogGreetingSources(Files(), new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        public bool WithDirections { get; init; } = true;
        public bool WithMoney { get; init; } = true;

        public DialogConversation Start(string replies, int callerLine = 0, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Areas: Areas, Gold: WithMoney ? Pay : null, Greeting: Greeter, Purse: WithMoney ? Purse : null,
                    Directions: WithDirections ? Directions : null), options);
    }

    // The files: every range the talk reads holds its first entries; the text is the number and the file, the quote's and the x: lines (700 and up) hold %d.
    private static GeneratedDialogFiles Files()
    {
        var numbers = new SortedSet<int> { 1, 100, 500, 600, 800, 1000, 1300, 1400, 2000 };
        for (var line = 200; line <= 470; line += 10) numbers.Add(line);
        for (var line = 600; line <= 790; line += 10) numbers.Add(line);
        return GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count).Select(index =>
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            return (MesFile?)Mes(string.Concat(numbers.Select(number =>
                $"{{{number}}}{{{number} {name}{(number == 1000 && !name.Contains("pc2") || number >= 700 && name.StartsWith("npc", StringComparison.Ordinal) ? " %d" : string.Empty)}}}")));
        }).ToArray());
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static string Reply(string text, string response = "20", string action = "") => $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    private static string[] Texts(DialogConversation conversation) => conversation.Options.Select(o => o.Text).ToArray();

    [TestMethod]
    public void The_arguments_are_read_like_the_original_reads_them()
    {
        var plain = DialogConversation.ParseDirectionsReply("d:$150,10-12")!.Value;
        Assert.AreEqual(150, plain.Price);
        CollectionAssert.AreEqual(new[] { 10, 11, 12 }, plain.Listed);
        CollectionAssert.AreEqual(new[] { 10, 11, 12 }, plain.Visible);
        Assert.IsNull(DialogConversation.ParseDirectionsReply("X:7,$0,3"), "the offer reads the list after the first comma, here $0 and 3: area 0 is outside the session's areas");
        Assert.IsNull(DialogConversation.ParseDirectionsReply("d:$0,0, 5"), "area 0 is outside the session's areas");
        Assert.IsNull(DialogConversation.ParseDirectionsReply("d:150,5"), "no price mark: the original reads address 1");
        Assert.IsNull(DialogConversation.ParseDirectionsReply("d:$150"), "no list");
        Assert.IsNull(DialogConversation.ParseDirectionsReply("d:$0,4096"), "an area outside 1..4095 is outside the subset");
    }

    [TestMethod]
    public void A_d_reply_is_a_generated_phrase_and_a_missing_port_leaves_it_a_frontier()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply("d:$0,1-3"));
        var option = conversation.Options.Single();
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Directions, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("1300 pc2m", option.Text);
        Assert.AreEqual(new DialogGeneratedMarker('d'), option.Marker);
        Assert.AreEqual("1400 pc2m", rig.Start(Reply("x:$0,1-3")).Options.Single().Text);

        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, new Rig { WithDirections = false }.Start(Reply("d:$0,1")).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, rig.Start(Reply("d:$0,4096")).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, new Rig { WithMoney = false }.Start(Reply("d:$5,1")).Frontier!.Kind, "a paying reply needs the money ports");
        Assert.AreEqual(DialogOptionTransition.Directions, new Rig { WithMoney = false }.Start(Reply("d:$0,1")).Options.Single().Transition, "a free one does not");
    }

    [TestMethod]
    public void The_reply_is_left_out_while_the_pc_knows_every_listed_area_and_the_hidden_one_still_counts_for_the_cap()
    {
        var rig = new Rig(known: [1, 2]);
        var hidden = rig.Start(Reply("d:$0,1-2"));
        Assert.AreEqual("400 pc2m", hidden.Options.Single().Text, "only the farewell is left");
        Assert.AreEqual(DialogOptionTextKind.Fallback, hidden.Options.Single().TextKind);
        var shown = new Rig(known: [1, 2]).Start(Reply("d:$0,1-3"));
        Assert.AreEqual(DialogOptionTransition.Directions, shown.Options.Single().Transition);

        // Five replies pass their conditions; the hidden one takes a slot of the five, so the sixth is never offered.
        var replies = Reply("d:$0,1-2") + string.Concat(Enumerable.Range(0, 5).Select(i => $"{{{12 + i}}}{{Plain {i}.}}{{}}{{1}}{{}}{{20}}{{}}"));
        var capped = new Rig(known: [1, 2]).Start(replies);
        CollectionAssert.AreEqual(new[] { "Plain 0.", "Plain 1.", "Plain 2.", "Plain 3." }, Texts(capped));
    }

    [TestMethod]
    public void The_page_lists_four_unknown_areas_by_name_then_more_then_the_way_out()
    {
        var rig = new Rig(known: [2]);
        var conversation = rig.Start(Reply("d:$0,1-6", action: "MM 9"));
        Pick(conversation, 1);
        Assert.AreEqual("100 npc_m2m", conversation.NpcLine!.Text, "the NPC's own line is the range 100..199");
        CollectionAssert.AreEqual(new[] { "Alpha", "Gamma", "Delta", "Epsilon", "600 pc2m" }, Texts(conversation), "area 2 is known and left out; the fifth entry is the more option");
        Assert.IsTrue(conversation.Options.Take(4).All(o => o.Transition == DialogOptionTransition.DirectionsStep && o.Marker == new DialogGeneratedMarker('d')));
        Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode, "the talk stays in the node");
        Pick(conversation, 5);
        CollectionAssert.AreEqual(new[] { "Zeta", "800 pc2m" }, Texts(conversation), "the last page ends with the way out");
        Assert.AreEqual(DialogOptionTransition.Direct, conversation.Options[1].Transition, "the way out follows the reply's response");
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);

        var four = new Rig().Start(Reply("d:$0,1-4"));
        Pick(four, 1);
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Gamma", "Delta", "800 pc2m" }, Texts(four), "exactly four areas need no more option");
        var x = new Rig().Start(Reply("x:$0,1"));
        Pick(x, 1);
        Assert.AreEqual("500 npc_m2m", x.NpcLine!.Text, "x: reads the range 500..599");
    }

    [TestMethod]
    public void A_d_way_is_the_line_of_the_direction_and_the_distance_range_and_leaves_the_area_unknown()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply("d:$0,1-2"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("330 npc_m2m", conversation.NpcLine!.Text, "50 units: the range 300 plus 10 per direction 3");
        Assert.AreEqual("1000 cls_pc2m", conversation.Options.Single().Text, "the only answer is the PC class line 1000");
        Assert.AreEqual(0, rig.World.Commands.Count, "d: does not make the area known");
        CollectionAssert.AreEqual(new[] { 1 }, rig.Directions.Asked);
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);

        foreach (var (distance, line) in new[] { (0L, "200"), (2 * 0xC60L - 1, "200"), (2 * 0xC60L, "300"), (99 * 0xC60L + 3000, "300"), (100 * 0xC60L, "400") })
        {
            var near = new Rig();
            near.Directions.Way = new DialogBearing(0, distance);
            var talk = near.Start(Reply("d:$0,1"));
            Pick(talk, 1);
            Pick(talk, 1);
            Assert.AreEqual($"{line} npc_m2m", talk.NpcLine!.Text, $"distance {distance}");
        }
    }

    [TestMethod]
    public void An_x_way_prints_the_distance_from_two_units_and_makes_the_area_known()
    {
        var rig = new Rig();
        var conversation = rig.Start(Reply("x:$0,1-2"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("730 npc_m2m 50", conversation.NpcLine!.Text, "the range 700 plus 10 per direction 3, the distance in units");
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands.ToArray());
        CollectionAssert.AreEqual(new[] { typeof(MarkAreaCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());

        var close = new Rig();
        close.Directions.Way = new DialogBearing(7, 5);
        var talk = close.Start(Reply("x:$0,2"));
        Pick(talk, 1);
        Pick(talk, 1);
        Assert.AreEqual("670 npc_m2m", talk.NpcLine!.Text, "under 2 units: the range 600, the text as it is");
        CollectionAssert.AreEqual(new[] { "MM:2" }, close.World.Commands.ToArray());
    }

    [TestMethod]
    public void A_price_is_asked_on_the_reaction_scale_and_yes_pays_the_npc_then_tells_the_way()
    {
        var rig = new Rig(gold: 20, reaction: 0);
        var conversation = rig.Start(Reply("x:$10,1"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Assert.AreEqual("1000 cls_m2m 20", conversation.NpcLine!.Text, "reaction 0: factor 200, the base price 10 is quoted as 20");
        CollectionAssert.AreEqual(new[] { "1 pc2m", "100 pc2m" }, Texts(conversation));
        Assert.AreEqual(0, rig.Pay.Moved.Count + rig.World.Commands.Count, "nothing happens before yes");
        Pick(conversation, 1);
        CollectionAssert.AreEqual(new[] { (800L, 900L, -20) }, rig.Pay.Moved);
        CollectionAssert.AreEqual(new[] { "MM:1" }, rig.World.Commands.ToArray());
        CollectionAssert.AreEqual(new[] { typeof(GoldCommand), typeof(MarkAreaCommand) }, conversation.Effects.Select(e => e.Command.GetType()).ToArray());
        Assert.AreEqual("730 npc_m2m 50", conversation.NpcLine!.Text);
    }

    [TestMethod]
    public void No_leaves_by_the_replys_response_too_little_gold_is_line_2000_and_a_follower_is_never_charged()
    {
        var no = new Rig();
        var conversation = no.Start(Reply("d:$10,1"));
        Pick(conversation, 1);
        Pick(conversation, 1);
        Pick(conversation, 2);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual(0, no.Pay.Moved.Count + conversation.Effects.Count);

        var poor = new Rig(gold: 9);
        var talk = poor.Start(Reply("d:$10,1"));
        Pick(talk, 1);
        Pick(talk, 1);
        Pick(talk, 1);
        Assert.AreEqual("2000 cls_m2m", talk.NpcLine!.Text, "the hero's own gold must cover the price");
        Assert.AreEqual(0, poor.Pay.Moved.Count);

        var led = new Rig(gold: 0, led: true);
        var free = led.Start(Reply("d:$10,1"));
        Pick(free, 1);
        Pick(free, 1);
        Assert.AreEqual("330 npc_m2m", free.NpcLine!.Text, "an NPC led by the PC tells the way for nothing");
        Assert.AreEqual(0, led.Pay.Moved.Count);
    }

    [TestMethod]
    public void Failures_are_typed_and_a_failed_way_never_follows_a_spent_price()
    {
        var nameless = new Rig().Start(Reply("d:$0,7"));
        nameless.Select(nameless.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, nameless.Failure!.Kind, "an area outside the table has no name");

        var lost = new Rig();
        var talk = lost.Start(Reply("d:$10,1"));
        Pick(talk, 1);
        Pick(talk, 1);
        lost.Directions.Way = null;
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, talk.Failure!.Kind);
        Assert.AreEqual(0, lost.Pay.Moved.Count + lost.World.Commands.Count);

        var bad = new Rig();
        bad.Directions.Way = new DialogBearing(8, 0);
        var wrong = bad.Start(Reply("d:$0,1"));
        Pick(wrong, 1);
        wrong.Select(wrong.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, wrong.Failure!.Kind);

        var throwing = new Rig();
        throwing.Directions.Throw = true;
        var thrown = throwing.Start(Reply("d:$0,1"));
        thrown.Select(thrown.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, thrown.Failure!.Kind);

        var tight = new Rig();
        var limited = tight.Start(Reply("x:$10,1"), options: new DialogConversationOptions { MaxEffects = 1 });
        Pick(limited, 1);
        Pick(limited, 1);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, limited.Select(limited.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, limited.Frontier!.Kind);
        Assert.AreEqual(0, tight.Pay.Moved.Count + tight.World.Commands.Count, "nothing is paid when both effects do not fit");
    }

    [TestMethod]
    public void A_dialog_transfer_in_the_same_reply_blocks_it()
    {
        var blocked = new Rig().Start(Reply("d:$0,1", action: "FL 20")).Options.Single();
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, blocked.Block!.Kind);
    }
}
