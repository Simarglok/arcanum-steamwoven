using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The focus kinds every one in the group (13) and anyone in the group (14; ARC-SCRIPT-2319, ledger GF1–GF7): the object list of the triggerer's direct followers with the triggerer itself last, the followers answered by the host.</summary>
[TestClass]
public sealed class ScrGroupFocusTests
{
    private const byte Triggerer = 0, Looped = 10, EveryGroup = 13, AnyGroup = 14;
    private const int Script = 1160;

    private sealed class GroupHost(FakeHost inner) : IScrHost, IScrWorldQueries, IScrGroupQueries
    {
        public FakeHost Inner => inner;
        public List<ScrObjectRef> Followers { get; } = [];
        public List<ScrObjectRef> Leaders { get; } = [];
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => inner.Script(number);
        public bool GlobalFlag(int index) => inner.GlobalFlag(index);
        public int StoryState() => inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => inner.IsPc(critter);
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => [];
        public int Random(int low, int high) => low;
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
        public IReadOnlyList<ScrObjectRef> FollowersOf(ScrObjectRef leader) { Leaders.Add(leader); return Followers.ToArray(); }
    }

    private static readonly ScrAdmissionSet Set = new("group-test", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd]), ScrAdmissionSet.S2.ValueKinds,
        ScrAdmissionSet.S3.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryoneInGroup, ScrFocusKind.AnyoneInGroup]), counterIndexWraps: true);

    private static ScrRunResult Run(GroupHost host, ScrProgram program, ScrRuleProfile? rules = null)
    {
        host.Inner.Scripts[Script] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), host, new ScrRunOptions { Admission = Set, Rules = rules ?? ScrRuleProfile.Steamwoven });
    }

    private static GroupHost Host(int followers)
    {
        var host = new GroupHost(new FakeHost());
        for (var i = 0; i < followers; i++) host.Followers.Add(new ScrObjectRef(1, $"follower:{i}"));
        return host;
    }

    [TestMethod]
    public void Only_the_group_set_admits_the_kinds_and_its_host_must_answer_them()
    {
        Assert.IsTrue(ScrAdmissionSet.S60.IsSubsetOf(ScrAdmissionSet.S61));
        Assert.IsFalse(ScrAdmissionSet.S61.IsSubsetOf(ScrAdmissionSet.S60));
        Assert.IsTrue(ScrAdmissionSet.S61.FocusKinds.Contains(ScrFocusKind.EveryoneInGroup) && ScrAdmissionSet.S61.FocusKinds.Contains(ScrFocusKind.AnyoneInGroup));
        Assert.IsFalse(ScrAdmissionSet.S60.FocusKinds.Contains(ScrFocusKind.EveryoneInGroup));
        // A set holding the kinds, no loop for (so no world queries are asked for), needs a host that answers the group.
        var focusOnly = new ScrAdmissionSet("group-focus-only", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions, ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds.Append(ScrFocusKind.EveryoneInGroup));
        var bare = new FakeHost();
        bare.Scripts[Script] = ScrScriptLookup.Of(Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), bare, new ScrRunOptions { Admission = focusOnly }));
        StringAssert.Contains(thrown.Message, "IScrGroupQueries");
    }

    private static ScrProgram CountGroup(byte kind, int expected) => Program(
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
        Do(C(ScrCatalogue.Action.LoopFor, (kind, 0))),
        Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.LoopEnd)),
        L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(expected)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void The_group_is_the_triggerers_followers_then_the_triggerer()
    {
        var host = Host(2);
        var program = CountGroup(EveryGroup, 3);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host, program).Stop, "two followers and the triggerer");
        CollectionAssert.AreEqual(new[] { Npc }, host.Leaders, "the group is the triggerer's");
    }

    [TestMethod]
    public void A_triggerer_without_followers_is_its_own_group_and_the_any_kind_builds_the_same_list()
    {
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(Host(0), CountGroup(EveryGroup, 1)).Stop, "the triggerer alone");
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(Host(2), CountGroup(AnyGroup, 3)).Stop, "kind 14 builds kind 13's list");
    }

    [TestMethod]
    public void The_original_profile_cuts_the_followers_at_a_hundred_before_the_triggerer_and_the_steamwoven_one_does_not()
    {
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(Host(150), CountGroup(EveryGroup, ScrWorld.VicinityListCap + 1), ScrRuleProfile.Original).Stop);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(Host(150), CountGroup(EveryGroup, 151), ScrRuleProfile.Steamwoven).Stop);
    }

    [TestMethod]
    public void Only_the_five_guard_conditions_take_a_group_kind_and_test_each_object_of_the_list()
    {
        var withConditions = new ScrAdmissionSet("group-conditions", ScrAdmissionSet.S4.Conditions.Append(ScrCatalogue.Condition.FollowingPc), ScrAdmissionSet.S4.Actions, ScrAdmissionSet.S4.ValueKinds, ScrAdmissionSet.S4.FocusKinds.Concat([ScrFocusKind.EveryoneInGroup, ScrFocusKind.AnyoneInGroup]));
        ScrProgram One(int condition) => Program(L(C(condition, (EveryGroup, 0), N(1000), N(1)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.IsTrue(ScrAdmission.Admit(One(ScrCatalogue.Condition.PcQuestState), withConditions).IsAdmitted);
        var refused = ScrAdmission.Admit(One(ScrCatalogue.Condition.FollowingPc), withConditions);
        Assert.IsFalse(refused.IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, refused.Refusal!.Code);
    }
}
