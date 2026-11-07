using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The story state code ss (condition and action), the known areas condition ar and the map area condition ia (ARC-DEV-191, ledger DCC7, DAC7, SS1).
// Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogStoryAreaTests
{
    internal sealed class Story(int state) : IDialogStoryPort
    {
        public int Current { get; private set; } = state;
        public List<StoryStateCommand> Commands { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public int State() => Current;
        public DialogCommandResult Execute(StoryStateCommand command)
        {
            Commands.Add(command);
            if (command.Value > Current) Current = command.Value;
            return Result;
        }
    }

    internal sealed class Areas(params int[] known) : IDialogAreaPort
    {
        public List<DialogAreaQuery> Asked { get; } = [];
        public bool Known(DialogAreaQuery query)
        {
            Asked.Add(query);
            return known.Contains(query.Area);
        }
    }

    internal sealed class MapArea(int? area) : IDialogMapAreaPort
    {
        public int? Area(DialogParticipantId playerCharacter) => area;
    }

    private static bool Offered(string condition, IDialogStoryPort? story = null, IDialogAreaPort? areas = null, IDialogMapAreaPort? mapArea = null)
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: condition, response: "0"));
        var conversation = Start(document, new Ports(), Owner(), story: story, areas: areas, mapArea: mapArea);
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        Assert.IsTrue(conversation.Options.All(option => option.Block is null), condition);
        return conversation.Options.Any(option => option.Text == "Ask.");
    }

    [TestMethod]
    public void The_story_state_condition_follows_the_signed_rule()
    {
        // 0x4158FF: n >= 0 passes at state >= n, n < 0 at state <= -n (DS2).
        foreach (var state in new[] { 0, 1, 4, 5, 6, 40 })
        {
            Assert.AreEqual(state >= 5, Offered("ss 5", new Story(state)), $"state {state}, ss 5");
            Assert.AreEqual(state <= 5, Offered("ss -5", new Story(state)), $"state {state}, ss -5");
            Assert.IsTrue(Offered("ss 0", new Story(state)));
        }
        Assert.IsTrue(Offered("ss -0", new Story(0)));
        Assert.IsTrue(Offered("SS5, ss 3", new Story(7)), "a field is a conjunction");
    }

    [TestMethod]
    public void The_story_state_action_goes_to_the_port_and_the_port_keeps_the_larger()
    {
        var story = new Story(3);
        var document = Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "0", action: "ss 8, ss 2, lf 3 1"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), story: story);
        Assert.IsNull(conversation.Options[0].Block);
        conversation.Select(conversation.Options[0].Token);
        Assert.IsNull(conversation.Failure);
        CollectionAssert.AreEqual(new[] { 8, 2 }, story.Commands.Select(c => c.Value).ToArray());
        Assert.AreEqual(800, story.Commands[0].PlayerCharacter.Value);
        Assert.AreEqual(8, story.Current);
        // The same code works in an NPC line's action.
        var line = Start(Read(Record("10", "Hm.", action: "ss 20") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner(), story: story);
        Assert.IsNull(line.Failure);
        Assert.AreEqual(20, story.Current);
    }

    [TestMethod]
    public void A_rejecting_story_port_fails_the_conversation()
    {
        var story = new Story(0) { Result = DialogCommandResult.Rejected };
        var document = Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "0", action: "ss 8"));
        var conversation = Start(document, new Ports(), Owner(), story: story);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, conversation.Failure!.Kind);
    }

    [TestMethod]
    public void The_known_area_condition_asks_the_port_with_the_sign_rule()
    {
        var areas = new Areas(7, 120);
        // 0x4159FE: n > 0 needs the area known; n <= 0 needs the area -n not known.
        Assert.IsTrue(Offered("ar 7", areas: areas));
        Assert.IsFalse(Offered("ar 8", areas: areas));
        Assert.IsFalse(Offered("ar -7", areas: areas));
        Assert.IsTrue(Offered("ar -8", areas: areas));
        Assert.IsTrue(Offered("ar7, ar -9, ar 120", areas: areas));
        var asked = new Areas();
        Offered("ar -8", areas: asked);
        CollectionAssert.AreEqual(new[] { new DialogAreaQuery(new DialogParticipantId(800), 8) }, asked.Asked, "the dialog's PC and the area");
    }

    [TestMethod]
    public void An_area_outside_the_sessions_areas_is_outside_the_subset()
    {
        // The original indexes a byte per area without a range test (0x4CAF50): area 0 and numbers beyond 4095 are valid data with no answer here.
        foreach (var field in new[] { "ar 0", "ar 4096", "ar -4096", "ar -0" })
        {
            var document = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: field, response: "0"));
            var conversation = Start(document, new Ports(), Owner(), areas: new Areas());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier?.Kind, field);
        }
        Assert.IsTrue(Offered("ar 4095", areas: new Areas(4095)));
        Assert.IsTrue(Offered("ar 1", areas: new Areas(1)));
    }

    [TestMethod]
    public void The_map_area_condition_compares_the_area_of_the_pcs_map()
    {
        // 0x415A78: n > 0 passes when the area equals n; n <= 0 when it differs from -n (so "ia 0" passes on any map with an area).
        Assert.IsTrue(Offered("ia 3", mapArea: new MapArea(3)));
        Assert.IsFalse(Offered("ia 3", mapArea: new MapArea(4)));
        Assert.IsFalse(Offered("ia -3", mapArea: new MapArea(3)));
        Assert.IsTrue(Offered("ia -3", mapArea: new MapArea(0)));
        Assert.IsTrue(Offered("ia 0", mapArea: new MapArea(5)));
        Assert.IsFalse(Offered("ia 0", mapArea: new MapArea(0)));
    }

    [TestMethod]
    public void A_map_without_an_answer_fails_the_conversation_and_a_throwing_port_too()
    {
        var document = Read(Record("10", "?") + Record("11", "Always.", iq: "1", response: "0") + Record("12", "Ask.", iq: "1", condition: "ia 3", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start(document, new Ports(), Owner(), mapArea: new MapArea(null)).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start(document, new Ports(), Owner(), mapArea: new Throwing()).Failure!.Kind);
    }

    [TestMethod]
    public void Each_code_needs_its_port_and_the_condition_forms_are_not_actions()
    {
        foreach (var field in new[] { "ss 1", "ar 1", "ia 1" })
        {
            var document = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: field, response: "0"));
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(document, new Ports(), Owner()).Frontier!.Kind, field + " unadmitted without its port");
        }
        var statement = Start(Read(Record("10", "Hm.", action: "ss 4")), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, statement.Frontier!.Kind, "the ss action needs the story port too");
        // ar and ia are not actions, ss is both.
        foreach (var action in new[] { "ar 1", "ia 1" })
        {
            var conversation = Start(Read(Record("10", "Hm.", action: action)), new Ports(), Owner(), areas: new Areas(), mapArea: new MapArea(1));
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, conversation.Frontier!.Kind, action);
        }
    }

    private sealed class Throwing : IDialogMapAreaPort
    {
        public int? Area(DialogParticipantId playerCharacter) => throw new InvalidOperationException();
    }
}
