using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The focus kinds every container in the vicinity (17) and any container in the vicinity (18; ARC-SCRIPT-2322, ledger CV1–CV5): the loop for walks the containers the host lists around the attachee, under the original's cap of 100.</summary>
[TestClass]
public sealed class ScrContainerVicinityTests
{
    private const byte Attachee = 1, Triggerer = 0, Looped = 10, Container = 17;
    private const int Script = 1596;

    private sealed class ContainerHost(FakeHost inner) : IScrHost, IScrWorldQueries, IScrContainerQueries, IScrObjectStateQueries
    {
        public FakeHost Inner => inner;
        public HashSet<ScrObjectRef> Off { get; } = [];
        public bool? IsDead(ScrObjectRef obj) => false;
        public bool? IsSwitchedOff(ScrObjectRef obj) => Off.Contains(obj);
        public List<ScrObjectRef> Containers { get; } = [];
        public List<ScrObjectRef> Centres { get; } = [];
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
        public IReadOnlyList<ScrObjectRef> ContainersInVicinity(ScrObjectRef centre) { Centres.Add(centre); return Containers.ToArray(); }
        public int Random(int low, int high) => low;
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
    }

    private static readonly ScrAdmissionSet Set = new("container-test", ScrAdmissionSet.S2.Conditions.Append(ScrCatalogue.Condition.IsSwitchedOff),
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd]), ScrAdmissionSet.S2.ValueKinds,
        ScrAdmissionSet.S3.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryContainerInVicinity, ScrFocusKind.AnyContainerInVicinity]), counterIndexWraps: true);

    // Counts the looped containers in local 0 and stops with skip-default for exactly three of them (run-default otherwise).
    private static ScrProgram Counter3() => Program(
        Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
        Do(C(ScrCatalogue.Action.LoopFor, (Container, 0))),
        Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.LoopEnd)),
        L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(3)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ContainerHost Host(int containers)
    {
        var inner = new FakeHost();
        inner.Scripts[Script] = ScrScriptLookup.Of(Counter3());
        var host = new ContainerHost(inner);
        for (var i = 0; i < containers; i++) host.Containers.Add(new ScrObjectRef(1, $"container:{i}"));
        return host;
    }

    private static ScrRunResult Run(ContainerHost host, ScrRuleProfile? rules = null) =>
        ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), host, new ScrRunOptions { Admission = Set, Rules = rules ?? ScrRuleProfile.Steamwoven });

    [TestMethod]
    public void The_loop_for_walks_each_container_the_host_lists_around_the_assigned_attachee()
    {
        var host = Host(3);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host).Stop, "three containers looped");
        CollectionAssert.AreEqual(new[] { Npc }, host.Centres, "the list is centred on the attachee the script assigned (the triggerer)");
    }

    [TestMethod]
    public void An_empty_container_list_skips_the_loop_body()
    {
        var host = Host(0);
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host).Stop);
    }

    [TestMethod]
    public void The_original_profile_keeps_the_first_hundred_containers_and_the_steamwoven_one_all()
    {
        var inner = new FakeHost();
        inner.Scripts[Script] = ScrScriptLookup.Of(Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Container, 0))),
            Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(ScrWorld.VicinityListCap)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        var host = new ContainerHost(inner);
        for (var i = 0; i < 150; i++) host.Containers.Add(new ScrObjectRef(1, $"container:{i}"));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host, ScrRuleProfile.Original).Stop, "Original: the list is cut at the cap");
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host, ScrRuleProfile.Steamwoven).Stop, "Steamwoven: every container is looped");
    }

    [TestMethod]
    public void The_any_form_is_the_same_list_as_the_every_form()
    {
        var host = Host(3);
        host.Inner.Scripts[Script] = ScrScriptLookup.Of(Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
            Do(C(ScrCatalogue.Action.LoopFor, (18, 0))),
            Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(3)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host).Stop, "kind 18 walks the same three containers");
    }

    [TestMethod]
    public void A_condition_needs_every_container_to_hold_for_kind_17_and_one_for_kind_18()
    {
        var host = Host(3);
        host.Off.Add(host.Containers[0]);
        ScrScriptLookup Cond(byte kind) => ScrScriptLookup.Of(Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            L(C(ScrCatalogue.Condition.IsSwitchedOff, (kind, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        host.Inner.Scripts[Script] = Cond(17);
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host).Stop, "kind 17: one of three is off, not every one");
        host.Inner.Scripts[Script] = Cond(18);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host).Stop, "kind 18: one off is enough");
        host.Off.Clear();
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host).Stop, "none off holds for neither");
    }

    [TestMethod]
    public void A_first_object_read_takes_the_first_container_of_the_list()
    {
        var host = Host(3);
        host.Inner.Scripts[Script] = ScrScriptLookup.Of(Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Container, 0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Container, 0))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Run(host);
        CollectionAssert.AreEqual(new[] { Npc, host.Containers[0] }, host.Centres, "the list is asked again around the first container the first read took");
    }

    [TestMethod]
    public void Only_the_container_set_admits_the_focus_kinds_and_its_host_must_answer_them()
    {
        var program = Counter3();
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S61).IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S62).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S61.IsSubsetOf(ScrAdmissionSet.S62));
        Assert.IsFalse(ScrAdmissionSet.S62.IsSubsetOf(ScrAdmissionSet.S61));
        Assert.IsTrue(ScrAdmissionSet.S62.FocusKinds.Contains(ScrFocusKind.EveryContainerInVicinity));
        Assert.IsTrue(ScrAdmissionSet.S62.FocusKinds.Contains(ScrFocusKind.AnyContainerInVicinity));
        // A set holding the focus kind (and no loop for) needs a host that answers it.
        var focusOnly = new ScrAdmissionSet("container-focus-only", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions, ScrAdmissionSet.S3.ValueKinds,
            ScrAdmissionSet.S3.FocusKinds.Append(ScrFocusKind.EveryContainerInVicinity));
        var bare = new FakeHost();
        bare.Scripts[Script] = ScrScriptLookup.Of(Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), bare, new ScrRunOptions { Admission = focusOnly }));
        StringAssert.Contains(thrown.Message, "IScrContainerQueries");
    }
}
