using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1165 (E5-c, ledger SQ1–SQ12): the story and time conditions and actions of set S34 on synthetic programs: daytime, the clock actions, global quest state, rumors, blessings and curses, quest
/// experience, the fate point and set PC quest unbotched. The meaning of the answers (the session's stores) is the host's; these tests pin what the interpreter asks, does with the answers and reads back.</summary>
[TestClass]
public sealed class ScrStoryTimeTests
{
    private const byte Triggerer = 0, Attachee = 1;
    private const int Script = 2810;
    private static readonly ScrAdmissionSet Set = new("test-story-time", ScrAdmissionSet.S2.Conditions.Concat(ScrAdmissionSet.StoryConditions),
        ScrAdmissionSet.S2.Actions.Concat(ScrAdmissionSet.StoryActions), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds, counterIndexWraps: true);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class StoryHost(FakeHost inner) : IScrHost, IScrStoryQueries
    {
        public FakeHost Inner { get; } = inner;
        public ScrGameTime? Clock { get; set; } = new(0, 12, 30);
        public Dictionary<int, int> Quests { get; } = new();
        public HashSet<(ScrObjectRef, int)> Known { get; } = new();
        public HashSet<int> Quelled { get; } = new();
        public HashSet<(ScrObjectRef, bool, int)> Held { get; } = new();
        public HashSet<ScrObjectRef> Unknown { get; } = new();

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => Inner.Script(number);
        public bool GlobalFlag(int index) => Inner.GlobalFlag(index);
        public int StoryState() => Inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => Inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => Inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => Inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => Inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => Inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => Inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => Inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => Inner.IsPc(critter);

        public ScrGameTime? Time() => Clock;
        public int GlobalQuestState(int quest) => Quests.TryGetValue(quest, out var state) ? state : 2;
        public bool? RumorKnown(ScrObjectRef obj, int rumor) => Unknown.Contains(obj) ? null : Known.Contains((obj, rumor));
        public bool RumorQuelled(int rumor) => Quelled.Contains(rumor);
        public bool? HasBlessing(ScrObjectRef obj, int number, bool curse) => Unknown.Contains(obj) ? null : Held.Contains((obj, curse, number));
    }

    private static StoryHost Host(ScrProgram program)
    {
        var host = new StoryHost(new FakeHost());
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        return host;
    }

    private static ScrRunResult Beat(StoryHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, Options);

    // The condition's true branch skips the default, its false branch runs it.
    private static ScrProgram Test(int condition, params (byte, int)[] operands) => Program(
        L(C(condition, operands), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrOutcome Holds(bool value) => value ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault;

    [TestMethod]
    public void Daytime_is_the_hours_six_to_seventeen()
    {
        // SQ1 (0x45ACF0): the hour from 6 to 17 is day, any other hour night.
        foreach (var (hour, expected) in new[] { (0, false), (5, false), (6, true), (12, true), (17, true), (18, false), (23, false) })
        {
            var host = Host(Test(ScrCatalogue.Condition.Daytime));
            host.Clock = new(3, hour, 59);
            Assert.AreEqual(Holds(expected), Beat(host).Outcome, $"hour {hour}");
        }
    }

    [TestMethod]
    public void The_clock_actions_store_the_day_hour_minute_and_hours_of_age()
    {
        // SQ5: day since startup is the day index plus one (0x45A8D0), hours since startup the whole hours of the age (0x45A890: day × 24 plus the hour).
        var clock = new ScrGameTime(41, 7, 9);
        foreach (var (action, expected) in new[] { (ScrCatalogue.Action.GetDaySinceStartup, 42), (ScrCatalogue.Action.GetCurrentHour, 7), (ScrCatalogue.Action.GetCurrentMinute, 9),
                     (ScrCatalogue.Action.GetHoursSinceStartup, 41 * 24 + 7) })
        {
            var host = Host(Program(Do(C(action, (Local, 0))), L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(expected)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
            host.Clock = clock;
            Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome, $"action {action}");
        }
        Assert.AreEqual(new ScrGameTime(41, 7, 9), clock);
        Assert.IsTrue(new ScrGameTime(0, 6, 0).IsDay && !new ScrGameTime(0, 18, 0).IsDay);
    }

    [TestMethod]
    public void A_clock_action_stores_into_a_counter_masked_to_its_byte()
    {
        // The original's store (0x44BB50) masks a counter write to its byte (X8): 10 days and 16 hours are 256 hours, which a byte holds as 0.
        var program = Program(Do(C(ScrCatalogue.Action.GetHoursSinceStartup, (Counter, 0))),
            L(C(ScrCatalogue.Condition.Equal, (Counter, 0), N(1)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host(program);
        host.Clock = new(10, 16, 0);
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
        host.Clock = new(10, 17, 0);
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome, "257 hours is 1 in a byte");
    }

    [TestMethod]
    public void A_clock_action_with_a_literal_store_is_refused_by_admission()
    {
        var host = Host(Program(Do(C(ScrCatalogue.Action.GetCurrentHour, N(0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.Refused, Beat(host).Outcome);
    }

    [TestMethod]
    public void A_host_with_no_clock_ends_the_dispatch_with_nothing_applied()
    {
        foreach (var program in new[]
                 {
                     Program(Do(C(ScrCatalogue.Action.SetGlobalFlag, N(5))), Do(C(ScrCatalogue.Action.GetCurrentHour, (Local, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))),
                     Program(Do(C(ScrCatalogue.Action.SetGlobalFlag, N(5))), L(C(ScrCatalogue.Condition.Daytime), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))),
                 })
        {
            var host = Host(program);
            host.Clock = null;
            var run = Beat(host);
            Assert.AreEqual(ScrStop.NotModelled, run.Stop);
            Assert.AreEqual(0, run.Effects.GlobalFlags.Count, "nothing is applied");
        }
    }

    [TestMethod]
    public void Global_quest_state_compares_the_quests_game_wide_state_with_the_value()
    {
        // SQ2 (case 7, 0x4C51A0): equal to the value.
        var host = Host(Test(ScrCatalogue.Condition.GlobalQuestState, N(1009), N(4)));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
        host.Quests[1009] = 4;
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome);
        host.Quests[1009] = 6;
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
        // The quest may come from a variable, and the default state is the host's (2 here).
        var byLocal = Host(Program(Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(1010))),
            L(C(ScrCatalogue.Condition.GlobalQuestState, (Local, 0), N(2)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(byLocal).Outcome);
    }

    [TestMethod]
    public void Set_quest_global_state_stores_only_completed_or_botched_and_the_run_reads_it_back()
    {
        // SQ7 (0x4C51C0): a request other than 4 or 6 changes nothing; 4 and 6 are stored and final.
        var program = Program(
            Do(C(ScrCatalogue.Action.SetGlobalQuestState, N(1009), N(3))),
            Do(C(ScrCatalogue.Action.SetGlobalQuestState, N(1009), N(4))),
            Do(C(ScrCatalogue.Action.SetGlobalQuestState, N(1009), N(6))),
            L(C(ScrCatalogue.Condition.GlobalQuestState, N(1009), N(4)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host(program);
        var run = Beat(host);
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "4 is final: the later 6 does not replace it");
        CollectionAssert.AreEqual(new[] { new ScrStateRequest(ScrStateKind.GlobalQuestState, null, 1009, 4) }, run.Effects.States.ToArray(),
            "only the request that changes the state as the run sees it is asked of the host");

        // A state that is already final is not asked again.
        host.Quests[1009] = 6;
        Assert.AreEqual(0, Beat(host).Effects.States.Count);
    }

    [TestMethod]
    public void Rumor_known_asks_the_first_object_and_quelled_is_game_wide()
    {
        // SQ3 (case 40): the first object, the rumor read as a value; SQ4 (case 41).
        var known = Host(Test(ScrCatalogue.Condition.RumorKnown, (Triggerer, 0), N(1066)));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(known).Outcome);
        known.Known.Add((Pc, 1066));
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(known).Outcome);
        known.Unknown.Add(Pc);
        Assert.AreEqual(ScrStop.MissingObject, Beat(known).Stop, "an object the host does not know stops the run");

        // A null handle gives false in the original (0x4C58D0), so an absent triggerer takes the false branch and does not stop the run.
        var absent = Host(Test(ScrCatalogue.Condition.RumorKnown, (Triggerer, 0), N(1066)));
        absent.Known.Add((Pc, 1066));
        var none = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), absent, Options);
        Assert.AreEqual(ScrOutcome.RunDefault, none.Outcome);
        Assert.AreNotEqual(ScrStop.MissingObject, none.Stop);

        var quelled = Host(Test(ScrCatalogue.Condition.RumorQuelled, N(1066)));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(quelled).Outcome);
        quelled.Quelled.Add(1066);
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(quelled).Outcome);
    }

    [TestMethod]
    public void Quell_rumor_asks_for_the_object_and_the_run_reads_the_rumor_as_quelled()
    {
        // SQ9 (0x4C5690): the rumor, then the first object operand 1 names; an absent object asks for nothing.
        var program = Program(Do(C(ScrCatalogue.Action.QuellRumor, N(1070), (Triggerer, 0))),
            L(C(ScrCatalogue.Condition.RumorQuelled, N(1070)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Beat(Host(program));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrStateRequest(ScrStateKind.QuellRumor, Pc, 1070) }, run.Effects.States.ToArray());
        var absent = Beat(Host(Program(Do(C(ScrCatalogue.Action.QuellRumor, N(1070), (12, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)))));
        Assert.AreEqual(0, absent.Effects.States.Count);
    }

    [TestMethod]
    public void Blessings_and_curses_are_tested_over_the_list_and_kept_apart()
    {
        // SQ6 (cases 8 and 9: 0x4C4280, 0x4C3DD0): the number is read as a value; a blessing and a curse of one number are two things.
        var blessing = Host(Test(ScrCatalogue.Condition.HasBlessing, (Triggerer, 0), N(55)));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(blessing).Outcome);
        blessing.Held.Add((Pc, true, 55));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(blessing).Outcome, "a curse is not a blessing");
        blessing.Held.Add((Pc, false, 55));
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(blessing).Outcome);
        var curse = Host(Test(ScrCatalogue.Condition.HasCurse, (Triggerer, 0), N(55)));
        curse.Held.Add((Pc, true, 55));
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(curse).Outcome);
        blessing.Unknown.Add(Pc);
        Assert.AreEqual(ScrStop.MissingObject, Beat(blessing).Stop);
    }

    [TestMethod]
    public void Blessing_and_curse_actions_ask_per_object_and_a_later_condition_reads_the_change_back()
    {
        // SQ8: add, remove (blessing 25, 26; curse 27, 28) of the number for each object of operand 1, in order; a PC's change is seen by the run's later conditions at once.
        var program = Program(
            Do(C(ScrCatalogue.Action.AddBlessing, N(52), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.AddCurse, N(52), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.RemoveBlessing, N(53), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.RemoveCurse, N(54), (Triggerer, 0))),
            L(C(ScrCatalogue.Condition.HasBlessing, (Triggerer, 0), N(52)), C(ScrCatalogue.Action.SetGlobalFlag, N(1)), C(0)),
            L(C(ScrCatalogue.Condition.HasCurse, (Triggerer, 0), N(52)), C(ScrCatalogue.Action.SetGlobalFlag, N(2)), C(0)),
            L(C(ScrCatalogue.Condition.HasBlessing, (Triggerer, 0), N(53)), C(ScrCatalogue.Action.SetGlobalFlag, N(3)), C(0)),
            Do(C(ScrCatalogue.Action.RemoveBlessing, N(52), (Triggerer, 0))),
            L(C(ScrCatalogue.Condition.HasBlessing, (Triggerer, 0), N(52)), C(ScrCatalogue.Action.SetGlobalFlag, N(4)), C(0)),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var host = Host(program);
        host.Held.Add((Pc, false, 53));
        var run = Beat(host);
        CollectionAssert.AreEqual(new[]
        {
            new ScrStateRequest(ScrStateKind.AddBlessing, Pc, 52), new ScrStateRequest(ScrStateKind.AddCurse, Pc, 52),
            new ScrStateRequest(ScrStateKind.RemoveBlessing, Pc, 53), new ScrStateRequest(ScrStateKind.RemoveCurse, Pc, 54),
            new ScrStateRequest(ScrStateKind.RemoveBlessing, Pc, 52),
        }, run.Effects.States.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, run.Effects.GlobalFlags.Select(flag => flag.Index).ToArray(), "the added blessing and curse were held, the removed ones were not");
    }

    [TestMethod]
    public void A_blessing_of_an_object_that_is_not_a_pc_is_asked_for_and_not_read_back()
    {
        // The host decides what an object is (BC3 to BC5: a PC only); the run does not pretend an NPC holds it.
        var program = Program(Do(C(ScrCatalogue.Action.AddBlessing, N(52), (Attachee, 0))),
            L(C(ScrCatalogue.Condition.HasBlessing, (Attachee, 0), N(52)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Beat(Host(program));
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrStateRequest(ScrStateKind.AddBlessing, Npc, 52) }, run.Effects.States.ToArray());
    }

    [TestMethod]
    public void Give_quest_experience_and_grant_one_fate_point_ask_for_the_first_object()
    {
        // SQ10 (0x45F110 over 0x4C53C0): the object, then the quest experience level; SQ11: stat 20 plus one for a PC. An absent object asks for nothing.
        var run = Beat(Host(Program(Do(C(ScrCatalogue.Action.GiveQuestExperience, (Triggerer, 0), N(7))), Do(C(ScrCatalogue.Action.GrantFatePoint, (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.GiveQuestExperience, (12, 0), N(7))), Do(C(ScrCatalogue.Action.GrantFatePoint, (12, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrStateRequest(ScrStateKind.QuestExperience, Pc, 7), new ScrStateRequest(ScrStateKind.FatePoint, Pc, 0),
        }, run.Effects.States.ToArray());
    }

    [TestMethod]
    public void Set_pc_quest_unbotched_asks_per_object_and_a_botched_quest_reads_as_accepted_after_it()
    {
        // SQ12 (0x4C5070): the objects of operand 0, then the quest; only a PC's botched quest (shared state 6) changes, back to 2.
        var program = Program(Do(C(ScrCatalogue.Action.SetPcQuestUnbotched, (Triggerer, 0), N(1040))),
            L(C(ScrCatalogue.Condition.GlobalQuestState, N(1040), N(2)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host(program);
        host.Quests[1040] = 6;
        var run = Beat(host);
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrStateRequest(ScrStateKind.QuestUnbotched, Pc, 1040) }, run.Effects.States.ToArray());
        // A quest that is not botched stays as it is.
        host.Quests[1040] = 4;
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
    }

    [TestMethod]
    public void The_story_commands_are_in_set_S34_and_S33_is_a_subset_of_it()
    {
        Assert.IsTrue(ScrAdmissionSet.S33.IsSubsetOf(ScrAdmissionSet.S34));
        Assert.IsFalse(ScrAdmissionSet.S34.IsSubsetOf(ScrAdmissionSet.S33));
        CollectionAssert.AreEquivalent(new[] { 1, 7, 8, 9, 40, 41 }, ScrAdmissionSet.StoryConditions.ToArray());
        CollectionAssert.AreEquivalent(new[] { 17, 25, 26, 27, 28, 51, 63, 96, 97, 98, 105, 117, 119 }, ScrAdmissionSet.StoryActions.ToArray());
        Assert.AreEqual(ScrAdmissionSet.S33.Conditions.Count + 6, ScrAdmissionSet.S34.Conditions.Count);
        Assert.AreEqual(ScrAdmissionSet.S33.Actions.Count + 13, ScrAdmissionSet.S34.Actions.Count);
    }

    [TestMethod]
    public void A_host_without_story_queries_is_refused_for_a_set_that_holds_them()
    {
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Heartbeat, Script, Test(ScrCatalogue.Condition.Daytime));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, Options));
    }

    [TestMethod]
    public void Set_global_quest_state_and_unbotch_need_a_host_with_story_queries_up_front()
    {
        // The two actions read the game-wide state before they write, so a set holding either (and no story condition) is refused by Validate, not by an exception mid-run.
        var set = new ScrAdmissionSet("test-quest-writes", ScrAdmissionSet.S2.Conditions, ScrAdmissionSet.S2.Actions.Append(ScrCatalogue.Action.SetGlobalQuestState), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Heartbeat, Script, Program(Do(C(ScrCatalogue.Action.SetGlobalQuestState, N(1001), N(4)))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, new ScrRunOptions { Admission = set }));
    }
}
