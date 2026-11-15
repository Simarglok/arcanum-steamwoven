using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The focus kind every portal in the vicinity (19; ARC-SCRIPT-2041, ledger PV1–PV4): the loop for of the ground rune tile scripts walks the portals the host lists around the attachee, under the same cap as the scenery.</summary>
[TestClass]
public sealed class ScrPortalVicinityTests
{
    private const byte Attachee = 1, Triggerer = 0, Looped = 10, Portal = 19;
    private const int Script = 2213;

    private sealed class PortalHost(FakeHost inner) : IScrHost, IScrWorldQueries, IScrPortalQueries
    {
        public List<ScrObjectRef> Portals { get; } = [];
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
        public IReadOnlyList<ScrObjectRef> PortalsInVicinity(ScrObjectRef centre) { Centres.Add(centre); return Portals.ToArray(); }
        public int Random(int low, int high) => low;
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
    }

    private static readonly ScrAdmissionSet Set = new("portal-test", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd]), ScrAdmissionSet.S2.ValueKinds,
        ScrAdmissionSet.S3.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryPortalInVicinity]), counterIndexWraps: true);

    // Counts the looped portals in local 0 and stops with skip-default for exactly three of them (run-default otherwise).
    private static ScrProgram Counter3() => Program(
        Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
        Do(C(ScrCatalogue.Action.LoopFor, (Portal, 0))),
        Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
        Do(C(ScrCatalogue.Action.LoopEnd)),
        L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(3)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static PortalHost Host(int portals)
    {
        var inner = new FakeHost();
        inner.Scripts[Script] = ScrScriptLookup.Of(Counter3());
        var host = new PortalHost(inner);
        for (var i = 0; i < portals; i++) host.Portals.Add(new ScrObjectRef(1, $"portal:{i}"));
        return host;
    }

    private static ScrRunResult Run(PortalHost host, ScrRuleProfile? rules = null) =>
        ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), host, new ScrRunOptions { Admission = Set, Rules = rules ?? ScrRuleProfile.Steamwoven });

    [TestMethod]
    public void The_loop_for_walks_each_portal_the_host_lists_around_the_assigned_attachee()
    {
        var host = Host(3);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host).Stop, "three portals looped");
        CollectionAssert.AreEqual(new[] { Npc }, host.Centres, "the list is centred on the attachee the script assigned (the triggerer)");
    }

    [TestMethod]
    public void An_empty_portal_list_skips_the_loop_body()
    {
        var host = Host(0);
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host).Stop);
    }

    [TestMethod]
    public void The_original_profile_keeps_the_first_hundred_portals_and_the_steamwoven_one_all()
    {
        var inner = new FakeHost();
        inner.Scripts[Script] = ScrScriptLookup.Of(Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Portal, 0))),
            Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(ScrWorld.VicinityListCap)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        var host = new PortalHost(inner);
        for (var i = 0; i < 150; i++) host.Portals.Add(new ScrObjectRef(1, $"portal:{i}"));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Run(host, ScrRuleProfile.Original).Stop, "Original: the list is cut at the cap");
        Assert.AreEqual(ScrStop.ReturnRunDefault, Run(host, ScrRuleProfile.Steamwoven).Stop, "Steamwoven: every portal is looped");
    }

    [TestMethod]
    public void Only_the_portal_set_admits_the_focus_kind_and_its_host_must_answer_it()
    {
        var program = Counter3();
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S59).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S59.IsSubsetOf(ScrAdmissionSet.S60));
        Assert.IsFalse(ScrAdmissionSet.S60.IsSubsetOf(ScrAdmissionSet.S59));
        var admitted = ScrAdmission.Admit(program, Set);
        Assert.IsTrue(admitted.IsAdmitted, admitted.Refusal?.ToString());
        // The focus kind of "any portal" (20) is not admitted: the original builds one list for both kinds (PV1), no installed script asks the any form.
        Assert.IsFalse(ScrAdmissionSet.S60.FocusKinds.Contains(ScrFocusKind.AnyPortalInVicinity));
        var host = Host(1);
        Assert.IsNotNull(host);
        var bare = new FakeHost();
        bare.Scripts[Script] = ScrScriptLookup.Of(program);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunTile(new(new ScrSlot(Script, 0, 0), Npc), bare, new ScrRunOptions { Admission = Set }));
    }
}
