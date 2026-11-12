using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;
using static Arcanum.Scripting.Tests.Dialog.DialogStoryAreaTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1321 (ledger STY1–STY4): the <c>c:</c> story reply as read in the executable (0x416C10 builds it, 0x418460 its PC phrase, 0x414810 state 0x18 and
/// 0x41A3E0 run it, 0x41A440 picks the NPC's line by the story state), over invented generated-dialog files and an invented story-state fixture.
/// </summary>
[TestClass]
public sealed class DialogStoryLineTests
{
    private const int NpcRace = 3;

    private sealed class Rig(int state = 2, DialogGreetingFacts? facts = null, DlgDocument? overrides = null, int[]? phraseRolls = null)
    {
        public Story Story { get; } = new(state);
        public Ports Ports { get; } = new();
        public World World { get; } = new();
        public bool WithStory { get; init; } = true;
        public bool WithGreeter { get; init; } = true;
        public DialogGreetingTests.FakeGreeter Greeter { get; } = new(facts ?? DialogGreetingTests.Facts() with { NpcRace = NpcRace },
            new DialogGreetingSources(Files(), new MesFile?[4], overrides), [], DialogRuleProfile.Steamwoven, [], phraseRolls);

        public DialogConversation Start(string replies, int callerLine = 0) =>
            DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
                new DialogConversationRequest(new DialogNodeId(10),
                    new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(callerLine), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(Ports, Ports, Owner(), World, Story: WithStory ? Story : null, Greeting: WithGreeter ? Greeter : null));
    }

    // Every PC line file holds 1700..1705 (text = number + file); the story files hold race 3 + 100 × state (states 0..2, text = number + file); the male-to-male one a
    // name token; the female-to-male one lacks state 2.
    private static GeneratedDialogFiles Files()
    {
        var all = new MesFile?[GeneratedDialogFiles.Count];
        for (var index = 0; index < all.Length; index++)
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            var numbers = new SortedSet<int> { 1000, 1100, 1150, 1050 };
            for (var state = 0; state <= 5; state++) numbers.Add(1700 + state);
            if (name.StartsWith("sto_", StringComparison.Ordinal)) for (var state = 0; state <= 2; state++) numbers.Add(NpcRace + 100 * state);
            if (name == "sto_f2m") numbers.Remove(NpcRace + 200);
            all[index] = Mes(string.Concat(numbers.Select(number =>
                $"{{{number}}}{{{number} {name}{(name == "sto_m2m" && number == NpcRace ? " @pcname@/@npcname@" : string.Empty)}}}")));
        }
        return GeneratedDialogFiles.Create(all);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    // Reply 11 is the story's; "response" 20 is the node after it.
    private static string StoryReply(string text = "C:", string response = "20", string action = "") =>
        $"{{11}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{{action}}}";

    private static DialogOption Only(DialogConversation conversation) => conversation.Options.Single();

    private static void Pick(DialogConversation conversation, int number) =>
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[number - 1].Token).Status);

    [TestMethod]
    public void A_c_reply_is_a_generated_phrase_of_the_pc_line_1700_plus_the_story_state_and_needs_the_story_and_greeting_ports()
    {
        var option = Only(new Rig(state: 2).Start(StoryReply()));
        Assert.AreEqual((DialogOptionTextKind.Generated, DialogOptionTransition.Story, (DialogOptionBlock?)null), (option.TextKind, option.Transition, option.Block));
        Assert.AreEqual("1702 pc2m", option.Text, "the single PC line 1700 + state 2 of the file by the NPC's gender");
        Assert.AreEqual(new DialogGeneratedMarker('c'), option.Marker);
        Assert.AreEqual("1700 pc2m", Only(new Rig(state: 0).Start(StoryReply())).Text);
        Assert.AreEqual("1702 pc2m", Only(new Rig(state: 2).Start(StoryReply("c: anything the original does not read"))).Text, "the prefix alone decides; the rest is not read");

        foreach (var blocked in new[] { new Rig { WithStory = false }, new Rig { WithGreeter = false } })
        {
            var option2 = Only(blocked.Start(StoryReply()));
            Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported), (option2.Block!.Kind, option2.Transition));
        }
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, Only(new Rig().Start(StoryReply(action: "FL 20"))).Block!.Kind, "a dialog transfer in the same reply is outside the subset");
    }

    [TestMethod]
    public void Picking_it_runs_the_action_then_tells_the_story_line_of_the_story_state_the_action_left()
    {
        var rig = new Rig(state: 0);
        var conversation = rig.Start(StoryReply(action: "SS 2"));
        Assert.AreEqual("1700 pc2m", Only(conversation).Text, "the phrase was built at the state before the action");
        Pick(conversation, 1);
        Assert.AreEqual(2, rig.Story.Current, "the reply's own action ran first");
        Assert.AreEqual($"{NpcRace + 200} sto_m2m", conversation.NpcLine!.Text, "message NPC race + 100 x state 2, of the file m2m");
        Assert.AreEqual((DialogLineOriginKind.Generated, NpcRace + 200), (conversation.NpcLine.Origin!.Kind, conversation.NpcLine.Origin.Number));
        Assert.AreEqual(new DialogNodeId(10), conversation.CurrentNode, "the talk stays in the node");
        var answer = Only(conversation);
        Assert.AreEqual(("1000 cls_pc2m", DialogOptionTransition.Direct, new DialogGeneratedMarker('c')), (answer.Text, answer.Transition, answer.Marker),
            "the only answer is the PC class line 1000 with the reply's own response");
        Pick(conversation, 1);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
    }

    [TestMethod]
    public void The_line_is_numbered_by_the_race_and_the_story_state_and_names_are_substituted()
    {
        var rig = new Rig(state: 0);
        var conversation = rig.Start(StoryReply());
        Pick(conversation, 1);
        Assert.AreEqual($"{NpcRace} sto_m2m Ada/Bram", conversation.NpcLine!.Text, "state 0: the NPC's race alone, names as for the other lines");
        Assert.AreEqual(0, rig.Greeter.Asked.Count(a => a.Item1 == DialogGreetingRoll.Line), "the story line is fetched by number: no line draw");
    }

    [TestMethod]
    public void The_file_follows_the_two_genders_and_a_missing_entry_reads_like_a_missing_generated_line()
    {
        var f2f = new Rig(state: 1, facts: DialogGreetingTests.Facts() with { NpcMale = false, PcMale = false, NpcRace = NpcRace });
        var tale = f2f.Start(StoryReply());
        Pick(tale, 1);
        Assert.AreEqual($"{NpcRace + 100} sto_f2f", tale.NpcLine!.Text);

        var f2m = new Rig(state: 1, facts: DialogGreetingTests.Facts() with { NpcMale = false, PcMale = true, NpcRace = NpcRace });
        var m2f = new Rig(state: 1, facts: DialogGreetingTests.Facts() with { NpcMale = true, PcMale = false, NpcRace = NpcRace });
        foreach (var (rig, file) in new[] { (f2m, "sto_f2m"), (m2f, "sto_m2f") })
        {
            var talk = rig.Start(StoryReply());
            Pick(talk, 1);
            Assert.AreEqual($"{NpcRace + 100} {file}", talk.NpcLine!.Text);
        }

        var missing = new Rig(state: 2, facts: DialogGreetingTests.Facts() with { NpcMale = false, PcMale = true, NpcRace = NpcRace });
        var gap = missing.Start(StoryReply());
        Pick(gap, 1);
        Assert.AreEqual(DialogLineOriginKind.MissingLine, gap.NpcLine!.Origin!.Kind, "the female-to-male file holds no entry for state 2");
        Assert.AreEqual(DialogGreeting.MissingLine(NpcRace + 200, GeneratedDialogFile.StoryFemaleToMale), gap.NpcLine.Text);
    }

    [TestMethod]
    public void The_npcs_own_override_record_10000_plus_the_state_comes_first_by_the_pcs_gender()
    {
        var overrides = Read(Record("10002", "Override for the man", alternate: "Override for the lady"));
        var man = new Rig(state: 2, overrides: overrides).Start(StoryReply());
        Pick(man, 1);
        Assert.AreEqual(("Override for the man", DialogLineOriginKind.Override, 10002), (man.NpcLine!.Text, man.NpcLine.Origin!.Kind, man.NpcLine.Origin.Number));
        var lady = new Rig(state: 2, facts: DialogGreetingTests.Facts() with { PcMale = false, NpcRace = NpcRace }, overrides: overrides).Start(StoryReply());
        Pick(lady, 1);
        Assert.AreEqual("Override for the lady", lady.NpcLine!.Text);
        var other = new Rig(state: 1, overrides: overrides).Start(StoryReply());
        Pick(other, 1);
        Assert.AreEqual($"{NpcRace + 100} sto_m2m", other.NpcLine!.Text, "another state has no override record: the story file");
    }

    [TestMethod]
    public void Failures_and_frontiers_are_typed_and_a_story_state_outside_the_session_is_refused()
    {
        var throwing = new Rig();
        var story = new ThrowingStory();
        var failed = DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + StoryReply() + "{20}{Next node.}{}{}{}{}{}"),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(throwing.Ports, throwing.Ports, Owner(), throwing.World, Story: story, Greeting: throwing.Greeter));
        Assert.AreEqual(DialogConversationFailureKind.QueryException, failed.Failure!.Kind, "a throwing story port fails the conversation when the phrase is built");

        var negative = new Rig(state: -1).Start(StoryReply());
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, negative.Failure!.Kind);

        var refused = new Rig();
        var talk = refused.Start(StoryReply());
        refused.Greeter.Refuse = true;
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, talk.Failure!.Kind);

        var rejected = DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + StoryReply(action: "LF 5 1") + "{20}{Next node.}{}{}{}{}{}"),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(new Ports { RejectAt = 1 }, new Ports { RejectAt = 1 }, Owner(), new World(), Story: new Story(0), Greeting: new Rig().Greeter));
        rejected.Select(rejected.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind, "a refused action never reaches the story line");
    }

    private sealed class ThrowingStory : IDialogStoryPort
    {
        public int State() => throw new InvalidOperationException();
        public DialogCommandResult Execute(StoryStateCommand command) => DialogCommandResult.Applied;
    }

    [TestMethod]
    public void A_rebuild_keeps_the_screen_and_the_way_out_follows_the_reply_without_running_it_again()
    {
        var rig = new Rig(state: 1);
        var conversation = rig.Start(StoryReply(action: "MM 1"));
        Pick(conversation, 1);
        var stale = conversation.Options[0].Token;
        var line = conversation.NpcLine!.Text;
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.RefreshOptions().Status);
        Assert.AreEqual(line, conversation.NpcLine!.Text);
        Assert.AreEqual(DialogConversationSelectStatus.NotOffered, conversation.Select(stale).Status);
        Pick(conversation, 1);
        Assert.AreEqual((new DialogNodeId(20), 1), (conversation.CurrentNode, rig.World.Commands.Count), "the action ran once");

        var exit = new Rig().Start(StoryReply(response: "0"), callerLine: 4);
        Pick(exit, 1);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, exit.Select(exit.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), exit.End);
    }

    [TestMethod]
    public void The_story_files_name_follow_the_handle_order_pairs()
    {
        Assert.AreEqual(GeneratedDialogFile.StoryMaleToMale, GeneratedDialogFiles.StoryLineFile(true, true));
        Assert.AreEqual(GeneratedDialogFile.StoryMaleToFemale, GeneratedDialogFiles.StoryLineFile(true, false));
        Assert.AreEqual(GeneratedDialogFile.StoryFemaleToFemale, GeneratedDialogFiles.StoryLineFile(false, false));
        Assert.AreEqual(GeneratedDialogFile.StoryFemaleToMale, GeneratedDialogFiles.StoryLineFile(false, true));
    }
}
