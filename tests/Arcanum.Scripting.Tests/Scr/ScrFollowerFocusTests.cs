using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The focus kinds every follower (2) and any follower (3; ARC-SCRIPT-2320, ledger FK1–FK4): the direct followers of the run's triggerer (0x44B4E0 over 0x4411C0), one list for both kinds, the first
/// 100 under the original's rule. Programs are invented; set auto-level scheme (one request per object of its operand) shows the list.</summary>
[TestClass]
public sealed class ScrFollowerFocusTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, EveryFollower = 2, AnyFollower = 3;

    private sealed class FollowerHost(FakeHost inner) : IScrHost, IScrFollowerQueries
    {
        public Dictionary<ScrObjectRef, List<ScrObjectRef>> Followers { get; } = new();
        public List<ScrObjectRef> Asked { get; } = [];
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
        public IReadOnlyList<ScrObjectRef> FollowersOf(ScrObjectRef leader) { Asked.Add(leader); return Followers.TryGetValue(leader, out var list) ? list.ToArray() : []; }
    }

    // S7 plus the scheme action and the two follower kinds; the guard conditions of S4 stay out (the fake host has no object queries).
    private static readonly ScrAdmissionSet Set = new("follower-test", ScrAdmissionSet.S7.Conditions.Append(ScrCatalogue.Condition.FollowingPc),
        ScrAdmissionSet.S7.Actions.Append(ScrCatalogue.Action.SetAutoLevelScheme), ScrAdmissionSet.S7.ValueKinds,
        ScrAdmissionSet.S7.FocusKinds.Concat([ScrFocusKind.EveryFollower, ScrFocusKind.AnyFollower]));

    private static readonly ScrObjectRef A = new(1, "npc.a"), B = new(1, "npc.b"), C3 = new(1, "npc.c");

    private static FollowerHost Host(byte kind)
    {
        var inner = new FakeHost();
        inner.Attach(Npc, Use, 700, Program(Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (kind, 0), N(7))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var host = new FollowerHost(inner);
        host.Followers[Pc] = [A, B, C3];
        host.Followers[Npc] = [new(1, "npc.other")];
        return host;
    }

    private static ScrRunResult Run(FollowerHost host, ScrObjectRef? triggerer, ScrRuleProfile? rules = null) =>
        ScrDispatcher.RunObject(new(Use, Npc, triggerer, null), host, new ScrRunOptions { Admission = Set, Rules = rules ?? ScrRuleProfile.Steamwoven });

    [TestMethod]
    [DataRow(EveryFollower)]
    [DataRow(AnyFollower)]
    public void Both_kinds_name_the_direct_followers_of_the_triggerer_in_order_and_not_of_the_attachee(byte kind)
    {
        var host = Host(kind);
        var run = Run(host, Pc);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, run.Refusal?.ToString());
        CollectionAssert.AreEqual(new[] { new ScrSchemeRequest(A, 7), new ScrSchemeRequest(B, 7), new ScrSchemeRequest(C3, 7) }, run.Effects.Schemes.ToArray());
        CollectionAssert.AreEqual(new[] { Pc }, host.Asked, "the triggerer is the leader asked; the attachee (an NPC with a follower of its own) is not");
    }

    [TestMethod]
    public void A_run_without_a_triggerer_has_no_followers_and_a_leader_without_followers_an_empty_list()
    {
        var host = Host(EveryFollower);
        Assert.AreEqual(0, Run(host, null).Effects.Schemes.Count);
        Assert.AreEqual(0, host.Asked.Count, "no triggerer: the host is not asked");
        host.Followers.Remove(Pc);
        Assert.AreEqual(0, Run(host, Pc).Effects.Schemes.Count);
    }

    [TestMethod]
    public void The_original_profile_keeps_the_first_hundred_followers_and_the_steamwoven_one_all()
    {
        var host = Host(EveryFollower);
        host.Followers[Pc] = Enumerable.Range(0, 130).Select(i => new ScrObjectRef(1, $"npc.f{i}")).ToList();
        Assert.AreEqual(ScrWorld.VicinityListCap, Run(host, Pc, ScrRuleProfile.Original).Effects.Schemes.Count);
        Assert.AreEqual(130, Run(host, Pc, ScrRuleProfile.Steamwoven).Effects.Schemes.Count);
    }

    [TestMethod]
    public void A_single_object_operand_takes_the_first_follower_and_none_for_an_empty_list()
    {
        // Following (0x4451B0 case 0): the first object's leader is the second object's; the first of "any follower" is the leader's first direct follower (0x44B960).
        var program = Program(L(C(ScrCatalogue.Condition.FollowingPc, (AnyFollower, 0), (Triggerer, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var inner = new FakeHost();
        inner.Attach(Npc, Use, 701, program);
        inner.Leaders[A] = Pc;
        var host = new FollowerHost(inner);
        host.Followers[Pc] = [A, B];
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host, Pc).Stop, "the first follower's leader is the triggerer");
        host.Followers[Pc] = [B, A];
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host, Pc).Stop, "the first follower is the one that decides");
    }

    [TestMethod]
    public void Only_the_follower_set_admits_the_kinds_and_its_host_must_answer_them()
    {
        var program = Program(Do(C(ScrCatalogue.Action.SetAutoLevelScheme, (EveryFollower, 0), N(1))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S60).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S60.IsSubsetOf(ScrAdmissionSet.S61));
        Assert.IsFalse(ScrAdmissionSet.S61.IsSubsetOf(ScrAdmissionSet.S60));
        Assert.IsTrue(ScrAdmissionSet.S61.FocusKinds.IsSupersetOf([ScrFocusKind.EveryFollower, ScrFocusKind.AnyFollower]));
        Assert.IsTrue(ScrAdmission.Admit(program, Set).IsAdmitted);
        var bare = new FakeHost();
        bare.Attach(Npc, Use, 702, program);
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(Use, Npc, Pc, null), bare, new ScrRunOptions { Admission = Set }));
        StringAssert.Contains(thrown.Message, "IScrFollowerQueries");
    }
}
