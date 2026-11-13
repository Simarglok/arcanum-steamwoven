using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1505 (ledger INS1, INS2): the NPC's generated line of the insult as read in the executable: an NPC node that is exactly <c>i:</c> says the NPC's
/// race line 1000 (0x416840 → 0x4185F0) and keeps its replies, and a Player <c>i:</c> reply that ends the talk shows the same line as the NPC's last words
/// (0x4191E0), over invented generated-dialog files whose entries name their own number and file.
/// </summary>
[TestClass]
public sealed class DialogInsultLineTests
{
    private sealed class Rig(int reaction = 50, bool withGreeter = true, bool unknownFacts = false)
    {
        public Ports Ports { get; } = new();
        public World World { get; } = new() { Reaction = DialogReactionReadResult.Of(reaction) };
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(unknownFacts ? null : DialogGreetingTests.Facts(),
            new DialogGreetingSources(DialogTrainerTests.Files(), new Arcanum.Data.Messages.MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);

        public DialogConversation Start(string nodes, DialogConversationOptions? options = null) =>
            DialogConversation.Start(Read(nodes),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(5), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Greeting: withGreeter ? Greeter : null), options);
    }

    // Node 10 is the NPC's (its text and action are the arguments), reply 11 goes on to node 20.
    private static string Nodes(string npcText = "I:", string npcAction = "", string reply = "{11}{Go on.}{}{1}{}{20}{}") =>
        $"{{10}}{{{npcText}}}{{}}{{}}{{}}{{}}{{{npcAction}}}{reply}{{20}}{{Next node.}}{{}}{{}}{{}}{{}}{{}}";

    [TestMethod]
    public void An_npc_node_that_is_exactly_i_says_the_race_line_1000_and_keeps_its_replies()
    {
        foreach (var marker in new[] { "i:", "I:" })
        {
            var rig = new Rig();
            var conversation = rig.Start(Nodes(marker, npcAction: "LF 3 1"));
            Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase, marker);
            Assert.AreEqual("1000 rce_m2m costs %d", conversation.NpcLine!.Text, "the NPC's race line 1000 of the file by the two genders (the PC's race is the NPC's, so the first block)");
            Assert.AreEqual((DialogLineOriginKind.Generated, 1000), (conversation.NpcLine.Origin!.Kind, conversation.NpcLine.Origin.Number));
            Assert.AreEqual(new DialogNodeId(10), conversation.NpcLine.Node);
            CollectionAssert.AreEqual(new[] { "LF:3:1:900" }, rig.Ports.Commands, "the node's action ran before its line");
            var option = conversation.Options.Single();
            Assert.AreEqual(("Go on.", DialogOptionTransition.Direct), (option.Text, option.Transition), "the node's replies are offered as for a plain line");
            Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(option.Token).Status);
            Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
            Assert.AreEqual("Next node.", conversation.NpcLine.Text);
        }
    }

    [TestMethod]
    public void Without_the_greeting_port_or_with_more_text_the_node_is_as_before()
    {
        var bare = new Rig(withGreeter: false).Start(Nodes());
        Assert.AreEqual(DialogConversationPhase.AtFrontier, bare.Phase);
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, bare.Frontier!.Kind, "no line source: the frontier of before");

        var literal = new Rig().Start(Nodes("i: more"));
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, literal.Phase, "only the exact marker is the insulted line; a longer text is literal");
        Assert.AreEqual("i: more", literal.NpcLine!.Text);
    }

    [TestMethod]
    public void A_target_planned_without_replies_keeps_the_frontier()
    {
        // A final-line target is spoken from its plan alone, which cannot hold a line picked when the node is entered.
        var rig = new Rig();
        var conversation = rig.Start(Nodes("Hello.", reply: "{11}{Last.}{}{1}{}{20}{fl 30}") + "{30}{I:}{}{}{}{}{}");
        Assert.AreEqual(DialogOptionTransition.FinalLine, conversation.Options.Single().Transition);
        Assert.AreEqual(DialogOptionBlockKind.TargetUnsupported, conversation.Options.Single().Block!.Kind);
    }

    [TestMethod]
    public void An_insult_that_ends_the_talk_shows_the_race_line_1000_as_the_last_words()
    {
        var rig = new Rig(reaction: 30);
        var conversation = rig.Start(Nodes("?", reply: "{11}{I:}{}{1}{}{-7}{}"));
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options.Single().Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.Insulted, null), conversation.End);
        Assert.AreEqual("1000 rce_m2m costs %d", conversation.NpcLine!.Text, "the NPC's race line 1000 replaces the line it said");
        Assert.AreEqual((DialogLineOriginKind.Generated, 1000), (conversation.NpcLine.Origin!.Kind, conversation.NpcLine.Origin.Number));
        Assert.AreEqual(0, conversation.Options.Count, "no replies after the last words");
    }

    [TestMethod]
    public void An_insult_that_does_not_end_the_talk_leaves_the_line_alone()
    {
        var rig = new Rig(reaction: 31);
        var conversation = rig.Start(Nodes("?", reply: "{11}{I:}{}{1}{}{20}{}"));
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options.Single().Token).Status);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "21 stays above 20");
        Assert.AreEqual("Next node.", conversation.NpcLine!.Text);
    }

    [TestMethod]
    public void A_hostile_refusal_speaks_the_same_line_when_the_npc_turns_on_the_pc()
    {
        // 0x413130: the check after a reply speaks 0x4185F0 with 1000 and ends the talk (state 4), as the insult does.
        var rig = new Rig();
        var conversation = DialogConversation.Start(Read(Nodes("?", reply: "{11}{Go.}{}{1}{}{20}{}")),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(5), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(rig.Ports, rig.Ports, Owner(), rig.World, Greeting: rig.Greeter, Hostile: new KillOnSight()));
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options.Single().Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.HostileRefusal, null), conversation.End);
        Assert.AreEqual("1000 rce_m2m costs %d", conversation.NpcLine!.Text);
    }

    private sealed class KillOnSight : IDialogHostilePort
    {
        public bool Hostile(DialogHostileQuery query) => true;
    }

    [TestMethod]
    public void A_host_that_does_not_know_the_facts_leaves_the_last_line_alone()
    {
        var rig = new Rig(reaction: 30, unknownFacts: true);
        var conversation = rig.Start(Nodes("?", reply: "{11}{I:}{}{1}{}{-7}{}"));
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options.Single().Token).Status);
        Assert.AreEqual(DialogEndKind.Insulted, conversation.End!.Kind);
        Assert.AreEqual("?", conversation.NpcLine!.Text, "no line could be picked: the NPC keeps what it said");
    }
}
