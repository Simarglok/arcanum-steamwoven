using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Set PC quest state, mark map location, set rumor and add or remove reputation of set S13 (ARC-SCRIPT-019, ledger
/// SS2–SS5), and the story state the run reads and raises (SS1). Programs, objects and numbers are invented.</summary>
[TestClass]
public sealed class ScrStoryTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12, Player = 21;
    // S7 plus the story actions: no guard condition, no get skill, so the plain recording host runs it.
    private static readonly ScrRunOptions Story = new()
    {
        Admission = new("story", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S7.Actions.Concat([ScrCatalogue.Action.SetPcQuestState,
            ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor, ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation]),
            ScrAdmissionSet.S7.ValueKinds, ScrAdmissionSet.S7.FocusKinds),
    };

    private static ScrRunResult Use(ScrProgram program, int story = 0, ScrObjectRef? extra = null)
    {
        var host = new FakeHost { Story = story };
        host.Attach(Chest, ScrPoint.Use, 2793, program);
        return ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, extra), host, Story);
    }

    [TestMethod]
    public void Each_story_action_asks_for_its_change_in_action_order()
    {
        var run = Use(Program(Do(C(16, (Triggerer, 0), N(1016), N(4))), Do(C(49, N(44), (Player, 0))), Do(C(50, N(2107), (Triggerer, 0))),
            Do(C(72, (Triggerer, 0), N(1005))), Do(C(73, (Player, 0), N(1006))), Do(C(1))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[]
        {
            new ScrStateRequest(ScrStateKind.QuestState, Pc, 1016, 4, Chest),
            new ScrStateRequest(ScrStateKind.KnownArea, Pc, 44),
            new ScrStateRequest(ScrStateKind.Rumor, Pc, 2107),
            new ScrStateRequest(ScrStateKind.AddReputation, Pc, 1005),
            new ScrStateRequest(ScrStateKind.RemoveReputation, Pc, 1006),
        }, run.Effects.States.ToArray(), "the quest's giver is the run's attachee (SS2)");
    }

    [TestMethod]
    public void An_absent_object_asks_for_nothing_and_the_run_goes_on()
    {
        var run = Use(Program(Do(C(16, (Extra, 0), N(1016), N(2))), Do(C(49, N(44), (Extra, 0))), Do(C(50, N(2107), (Extra, 0))),
            Do(C(72, (Extra, 0), N(1005))), Do(C(100, N(9))), Do(C(1))));
        Assert.AreEqual(0, run.Effects.States.Count);
        Assert.AreEqual(9, run.Effects.GlobalFlags.Single().Index);
    }

    [TestMethod]
    public void Story_state_is_read_from_the_host_and_only_raised()
    {
        var get = ScrCatalogue.Action.GetStoryState; var set = ScrCatalogue.Action.SetStoryState;
        var options = new ScrRunOptions { Admission = ScrAdmissionSet.S13 };
        var host = new TypeHostForStory(new FakeHost { Story = 12 });
        host.Inner.Attach(Chest, ScrPoint.Use, 2793, Program(Do(C(set, N(10))), Do(C(get, (2, 0))), L(C(ScrCatalogue.Condition.Equal, (2, 0), N(12)), C(set, N(15)), C(0)), Do(C(1))));
        var run = ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, null), host, options);
        Assert.AreEqual(15, run.Effects.StoryState, "10 is below 12 and does not lower it; the read sees 12, then 15 is asked for");
        Assert.IsTrue(ScrAdmissionSet.S12.IsSubsetOf(ScrAdmissionSet.S13));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(Program(Do(C(16, (Triggerer, 0), N(1016), N(4)))), ScrAdmissionSet.S12).Refusal!.Code);
    }

    /// <summary>S13 holds the guard conditions and get skill, so its host answers object and skill queries.</summary>
    private sealed class TypeHostForStory(FakeHost inner) : IScrHost, IScrObjectQueries, IScrSkillQueries
    {
        public FakeHost Inner { get; } = inner;
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => Inner.Script(number);
        public bool GlobalFlag(int index) => Inner.GlobalFlag(index);
        public int StoryState() => Inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => Inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => Inner.IsPc(critter);
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
        public int SkillLevel(ScrObjectRef obj, int skill) => 0;
    }
}
