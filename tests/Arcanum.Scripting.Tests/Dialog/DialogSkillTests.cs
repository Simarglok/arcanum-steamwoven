using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The skill gates PS, HA, SK, TR and the actions TR and ET through the skill port (ARC-SKILLS-004, ledger DS1–DS5). Records and
// phrases are invented synthetic fixtures; skill numbers are the source's (0..11 basic, 12..15 tech).
[TestClass]
public sealed class DialogSkillTests
{
    private static readonly DialogParticipantId Pc = new(800);

    internal sealed class Skills : IDialogSkillPort
    {
        public Dictionary<int, int> Levels { get; } = [];
        public Dictionary<int, int> Trainings { get; } = [];
        public List<DialogSkillQuery> Queries { get; } = [];
        public List<string> Commands { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;
        public int? BadValue { get; set; }

        public int Level(DialogSkillQuery query)
        {
            Queries.Add(query);
            return BadValue ?? Levels.GetValueOrDefault(query.Skill);
        }

        public int Training(DialogSkillQuery query)
        {
            Queries.Add(query);
            return BadValue ?? Trainings.GetValueOrDefault(query.Skill);
        }

        public DialogCommandResult Execute(SkillTrainingCommand command)
        {
            Commands.Add($"TR:{command.PlayerCharacter.Value}:{command.Skill}:{command.Training}");
            return Result;
        }
    }

    private static string[] Offered(string condition, Skills skills)
    {
        var document = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: condition, response: "0") + Record("12", "Always.", iq: "1", response: "0"));
        var conversation = Start(document, new Ports(), Owner(), skills: skills);
        Assert.IsNull(conversation.Failure, condition);
        Assert.IsNull(conversation.Frontier, condition);
        return [.. conversation.Options.Select(option => option.Text)];
    }

    [TestMethod]
    public void Persuasion_and_haggle_gates_compare_the_effective_level()
    {
        var skills = new Skills { Levels = { [11] = 8, [9] = 4 } };
        foreach (var (condition, open) in new[]
        {
            ("PS 8", true), ("ps 9", false), ("PS -8", true), ("PS -7", false), ("ps0", true), ("PS -0", true),
            ("HA 4", true), ("ha5", false), ("HA -4", true), ("ha-3", false), ("ha 4, ps 9", false), ("ha 4, ps 8", true),
        })
            Assert.AreEqual(open, Offered(condition, skills).Contains("Yes."), condition);
        CollectionAssert.Contains(skills.Queries, new DialogSkillQuery(Pc, 11));
        CollectionAssert.Contains(skills.Queries, new DialogSkillQuery(Pc, 9));
    }

    [TestMethod]
    public void Sk_and_tr_gates_read_a_skill_level_or_training_with_the_sources_second_operand()
    {
        var skills = new Skills { Levels = { [2] = 8, [13] = 12, [9] = 5 }, Trainings = { [4] = 1, [14] = 2 } };
        foreach (var (condition, open) in new[]
        {
            ("SK 2 8", true), ("sk2 9", false), ("SK 2 -8", true), ("SK 2 -7", false), ("SK 13 12", true), ("sk 13 13", false),
            ("TR 4 1", true), ("tr4 2", false), ("tr4-1", true), ("tr4 -0", true), ("tr 14 2", true), ("TR 14 -1", false),
            // A missing second operand reads 0, and a skill the source does not have reads level 0, training none.
            ("tr41, tr4 -1", true), ("tr 4,", true), ("tr 4 sk 2 8", true), ("tr 4 sk 2 9", false), ("sk 2 ha 4", true), ("sk 30 1", false), ("sk 30 0", true),
            ("tr 99 -0", true),
        })
            Assert.AreEqual(open, Offered(condition, skills).Contains("Yes."), condition);
        CollectionAssert.Contains(skills.Queries, new DialogSkillQuery(Pc, 41), "an out-of-range number is asked as it is");
    }

    [TestMethod]
    public void The_tr_action_sets_the_pcs_training_and_the_dialog_goes_on()
    {
        var document = Read(Record("10", "?", action: "tr 4 1") + Record("11", "Teach me.", iq: "1", response: "20", action: "TR 12 2, MM 1")
            + Record("20", "Done."));
        var skills = new Skills();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, skills: skills);
        CollectionAssert.AreEqual(new[] { "TR:800:4:1" }, skills.Commands, "an NPC line's action trains the PC too");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual("Done.", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "TR:800:4:1", "TR:800:12:2" }, skills.Commands);
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands);

        var rejected = Start(document, new Ports(), Owner(), world: new World(), skills: new Skills { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
    }

    [TestMethod]
    public void Et_continues_at_its_line_for_a_trained_skill_of_level_nine_and_refuses_otherwise()
    {
        var document = Read(Record("10", "?") + Record("11", "Teach me more.", iq: "1", response: "40", action: "LF 1 1, et 2 30")
            + Record("30", "Then listen.") + Record("40", "unused"));
        var conversation = Start(document, new Ports(), Owner(), skills: new Skills { Levels = { [2] = 9 }, Trainings = { [2] = 1 } });
        var option = conversation.Options.Single();
        Assert.AreEqual((DialogOptionTransition.ExpertTeacher, new DialogNodeId(30)), (option.Transition, option.Target));
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(option.Token).Status);
        Assert.AreEqual("Then listen.", conversation.NpcLine!.Text);

        foreach (var (level, training) in new[] { (8, 3), (20, 0) })
        {
            var refused = Start(document, new Ports(), Owner(), skills: new Skills { Levels = { [2] = level }, Trainings = { [2] = training } });
            Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, refused.Select(refused.Options[0].Token).Status, $"{level} {training}");
            Assert.AreEqual(DialogFrontierKind.ExpertTeacherRefused, refused.Frontier!.Kind);
        }
    }

    [TestMethod]
    public void Without_a_skill_port_the_skill_operations_stay_unadmitted_and_bad_forms_are_refused()
    {
        foreach (var condition in new[] { "PS 3", "HA 3", "SK 2 3", "TR 2 1" })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0"));
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(document, new Ports(), Owner()).Frontier!.Kind, condition);
        }
        var et = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: "ET 2 30") + Record("30", "!"));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, Start(et, new Ports(), Owner()).Options[0].Block!.Kind);

        foreach (var (action, kind) in new[]
        {
            ("ET 2 30, MM 1", DialogOptionBlockKind.UnsupportedOperation),
            ("ET 2 0", DialogOptionBlockKind.InvalidOperation),
            ("ET -2 30", DialogOptionBlockKind.InvalidOperation),
            ("ET 2 99", DialogOptionBlockKind.TargetInvalid),
            ("TR +2 1", DialogOptionBlockKind.InvalidOperation),
            ("TR 2 +1", DialogOptionBlockKind.InvalidOperation),
        })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: action) + Record("30", "!"));
            Assert.AreEqual(kind, Start(document, new Ports(), Owner(), skills: new Skills()).Options[0].Block?.Kind, action);
        }
        var npcTeacher = Start(Read(Record("10", "?", action: "ET 2 10") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner(), skills: new Skills());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, npcTeacher.Frontier!.Kind, "ET only ends a player action");
        // A stray number where an operation should start is outside the subset, as the source's scanner skips it.
        var stray = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "ha4, -4, lf1 1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(stray, new Ports(), Owner(), skills: new Skills()).Frontier!.Kind);
        foreach (var condition in new[] { "PS +3", "SK -1 3", "SK 2 x", "SK 2 -", "tr4-" })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0"));
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, Start(document, new Ports(), Owner(), skills: new Skills()).Failure!.Kind, condition);
        }
    }

    private sealed class Throwing : IDialogSkillPort
    {
        public int Level(DialogSkillQuery query) => throw new InvalidOperationException("no hero");
        public int Training(DialogSkillQuery query) => throw new InvalidOperationException("no hero");
        public DialogCommandResult Execute(SkillTrainingCommand command) => throw new InvalidOperationException("no hero");
    }

    [TestMethod]
    public void A_skill_port_that_throws_is_a_query_failure_or_an_indeterminate_command()
    {
        var gate = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "SK 2 1", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start(gate, new Ports(), Owner(), skills: new Throwing()).Failure!.Kind);
        var action = Read(Record("10", "?", action: "TR 2 1") + Record("11", "x", iq: "1", response: "0"));
        var conversation = Start(action, new Ports(), Owner(), skills: new Throwing());
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, conversation.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, conversation.Effects.Single().Outcome);
    }

    [TestMethod]
    public void A_skill_port_answer_outside_its_range_is_an_invalid_query_result()
    {
        foreach (var bad in new[] { -1, 21 })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "PS 1", response: "0"));
            Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start(document, new Ports(), Owner(), skills: new Skills { BadValue = bad }).Failure!.Kind);
        }
        var training = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "TR 1 1", response: "0"));
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start(training, new Ports(), Owner(), skills: new Skills { BadValue = 4 }).Failure!.Kind);
    }
}
