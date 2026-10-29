using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-050: the talk set's float line (23, ledger FL1–FL5) and get area (84, GA1–GA3) of <see cref="ScrAdmissionSet.S19"/>.</summary>
[TestClass]
public sealed class ScrTalkCommandTests
{
    private const byte Triggerer = 0, Attachee = 1, ExtraObject = 12, Everyone = 8;
    private const int Talk = 1952;
    private static readonly ScrObjectRef Other = new(1, "npc.2");
    // The commands of S19 these runs need: S3 with float line, get area and the everyone-in-vicinity list.
    private static readonly ScrRunOptions Options = new()
    {
        Admission = new("talk", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Concat([ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea]),
            ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds.Append(ScrFocusKind.EveryoneInVicinity)),
    };

    private sealed class TalkHost(FakeHost inner) : IScrHost, IScrObjectQueries, IScrWorldQueries, IScrAreaQueries
    {
        public FakeHost Inner { get; } = inner;
        public List<ScrObjectRef> Critters { get; } = new();
        public List<ScrObjectRef> Centres { get; } = new();
        public int? Area { get; set; } = 7;
        public List<ScrObjectRef> AreaAsked { get; } = new();

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
        public int QuestState(ScrObjectRef pc, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) { Centres.Add(centre); return Critters.ToArray(); }
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
        public int Random(int low, int high) => high;
        public int? AreaOf(ScrObjectRef obj) { AreaAsked.Add(obj); return Area; }
    }

    private static TalkHost Host(ScrProgram program, params ScrObjectRef[] critters)
    {
        var inner = new FakeHost();
        inner.Attach(Npc, ScrPoint.Dialog, Talk, program);
        var host = new TalkHost(inner);
        host.Critters.AddRange(critters);
        return host;
    }

    private static ScrRunResult Speak(TalkHost host, ScrObjectRef? triggerer = null) =>
        ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, triggerer ?? Pc, Other), host, Options);

    [TestMethod]
    public void S19_adds_float_line_and_get_area_to_S18()
    {
        Assert.IsTrue(ScrAdmissionSet.S18.IsSubsetOf(ScrAdmissionSet.S19));
        Assert.IsFalse(ScrAdmissionSet.S18.Actions.Contains(ScrCatalogue.Action.FloatLine) || ScrAdmissionSet.S18.Actions.Contains(ScrCatalogue.Action.GetArea));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea },
            ScrAdmissionSet.S19.Actions.Except(ScrAdmissionSet.S18.Actions).ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S18.Conditions.ToArray(), ScrAdmissionSet.S19.Conditions.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S19.CounterIndexWraps);
    }

    [TestMethod]
    public void A_float_line_floats_its_line_over_each_object_with_the_pc_around_the_triggerer()
    {
        // 1952's shape: float line 1 over the attachee, then return skip default (the talk does not open).
        var program = Program(Do(C(ScrCatalogue.Action.FloatLine, N(1), (Attachee, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var host = Host(program, Npc, Other, Pc);
        var run = Speak(host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop));
        CollectionAssert.AreEqual(new[] { new ScrFloatLine(Talk, 1, Npc, Pc) }, run.Effects.Floats.ToArray());
        // FL3: the rectangle is the triggerer's, read once.
        CollectionAssert.AreEqual(new[] { Pc }, host.Centres);
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_float_line_over_a_list_floats_once_per_object_in_list_order()
    {
        var program = Program(Do(C(ScrCatalogue.Action.FloatLine, (Local, 0), (Everyone, 0))));
        var host = Host(program, Npc, Other, Pc);
        var run = Speak(host);
        // Local variable 0 is 0 at the start of every run; the list is the attachee's vicinity (TG4), the PC is the triggerer's first PC.
        CollectionAssert.AreEqual(new[] { new ScrFloatLine(Talk, 0, Npc, Pc), new ScrFloatLine(Talk, 0, Other, Pc), new ScrFloatLine(Talk, 0, Pc, Pc) },
            run.Effects.Floats.ToArray());
        CollectionAssert.AreEqual(new[] { Pc, Npc }, host.Centres, "the PC's rectangle first, then the list around the attachee");
    }

    [TestMethod]
    public void Without_a_pc_around_the_triggerer_or_without_a_triggerer_nothing_floats()
    {
        var program = Program(Do(C(ScrCatalogue.Action.FloatLine, N(1), (Attachee, 0))));
        var alone = Host(program, Npc, Other);
        Assert.AreEqual(0, Speak(alone).Effects.Floats.Count);
        var none = Host(program, Pc);
        var run = ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, null, null), none, Options);
        Assert.AreEqual(0, run.Effects.Floats.Count);
        Assert.AreEqual(0, none.Centres.Count, "no triggerer: no rectangle is read");
        // An absent object floats nothing and does not stop the run (an empty list).
        var empty = Host(Program(Do(C(ScrCatalogue.Action.FloatLine, N(1), (ExtraObject, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))), Pc);
        var noExtra = ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), empty, Options);
        Assert.AreEqual((ScrStop.ReturnRunDefault, 0), (noExtra.Stop, noExtra.Effects.Floats.Count));
    }

    [TestMethod]
    public void Get_area_stores_the_hosts_area_of_the_first_object()
    {
        // 1934's shape: get area of the attachee into local variable 0, then compare it with 7.
        var program = Program(
            Do(C(ScrCatalogue.Action.GetArea, (Attachee, 0), (Local, 0))),
            L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(7)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host(program, Pc);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, Speak(host).Stop);
        CollectionAssert.AreEqual(new[] { Npc }, host.AreaAsked);
        host.Area = 3;
        Assert.AreEqual(ScrStop.ReturnRunDefault, Speak(host).Stop);
        // A host that cannot answer (the start map's location rule) ends the dispatch as not modelled with nothing applied, a float line
        // and a flag write before it included; an absent object stops the run as at every single-object read.
        var unanswered = Host(Program(
            Do(C(ScrCatalogue.Action.SetGlobalFlag, N(5))),
            Do(C(ScrCatalogue.Action.FloatLine, N(1), (Attachee, 0))),
            Do(C(ScrCatalogue.Action.GetArea, (Attachee, 0), (Local, 0)))), Pc);
        unanswered.Area = null;
        var ended = Speak(unanswered);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.NotModelled), (ended.Outcome, ended.Stop));
        Assert.IsTrue(ended.Effects.IsEmpty);
        var absent = Host(Program(Do(C(ScrCatalogue.Action.GetArea, (ExtraObject, 0), (Local, 0)))), Pc);
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), absent, Options).Stop);
        Assert.AreEqual(0, absent.AreaAsked.Count);
        // GA1: the area goes into operand 1, so a literal number there refuses the script.
        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetArea, (Attachee, 0), N(0)))), ScrAdmissionSet.S19);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 1), (literal.Refusal!.Code, literal.Refusal.Slot));
    }

    [TestMethod]
    public void A_set_with_these_commands_needs_a_host_that_answers_them()
    {
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Dialog, Talk, Program(Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var area = new ScrAdmissionSet("area", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Append(ScrCatalogue.Action.GetArea),
            ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        var floats = new ScrAdmissionSet("float", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Append(ScrCatalogue.Action.FloatLine),
            ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        foreach (var set in new[] { area, floats })
            Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), plain, new() { Admission = set }), set.Id);
        var beyond = new ScrAdmissionSet("beyond", ScrAdmissionSet.S19.Conditions, ScrAdmissionSet.S19.Actions.Append(CritterDisband),
            ScrAdmissionSet.S19.ValueKinds, ScrAdmissionSet.S19.FocusKinds, counterIndexWraps: true);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), Host(Program(Do(C(ScrCatalogue.Action.ReturnSkipDefault))), Pc), new() { Admission = beyond }));
    }
}
