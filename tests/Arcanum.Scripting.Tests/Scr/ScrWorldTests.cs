using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The world-object set S15 (ARC-WORLD-081): random, loop for and loop end over the vicinity's scenery, create object and a
/// counter operand with any index, on a synthetic replica of the plant generator's shape (ledger PG1–PG7).</summary>
[TestClass]
public sealed class ScrWorldTests
{
    private const byte Triggerer = 0, Attachee = 1, Looped = 10, Scenery = 15;
    private const int Marker = 1673, Script = 30285;
    private static readonly ScrObjectRef Tree = new(1, "sector:7:1"), Mark = new(1, "sector:7:2"), Rock = new(1, "sector:7:3");
    private static readonly ScrRunOptions Steamwoven = new() { Admission = ScrAdmissionSet.S15 };
    private static readonly ScrRunOptions Original = new() { Admission = ScrAdmissionSet.S15, Rules = ScrRuleProfile.Original };

    private sealed class WorldHost(FakeHost inner) : IScrHost, IScrObjectQueries, IScrWorldQueries, IScrSkillQueries, IScrItemQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, int> Names { get; } = new();
        public List<ScrObjectRef> Vicinity { get; } = new();
        public Queue<int> Draws { get; } = new();
        public List<(int Low, int High)> Asked { get; } = new();
        public List<ScrObjectRef> Centres { get; } = new();

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
        public int NameOf(ScrObjectRef obj) => Names.GetValueOrDefault(obj);
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) { Centres.Add(centre); return Vicinity.ToArray(); }
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => [];
        public int Random(int low, int high) { Asked.Add((low, high)); return Draws.Dequeue(); }
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
        // S15 holds S14's skill and item commands; the synthetic programs use none.
        public int SkillLevel(ScrObjectRef obj, int skill) => 0;
        public bool? IsIdentified(ScrObjectRef obj) => null;
        public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner) => null;
        public bool ItemsBeingRewielded() => false;
    }

    // The plant generator's shape (template-sector-plants.md, "The script"), written from its documented steps.
    private static ScrProgram Generator() => Program(
        Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.Add, (Local, 0), (Counter, 2), N(10000))),
        // The garbage counter index of the installed script: it reads counter 0 (PG6).
        Do(C(ScrCatalogue.Action.Add, (Local, 1), N(10000), (Counter, 2_012_780_696))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 4), N(0))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 5), N(0))),
        L(C(ScrCatalogue.Condition.LocalFlag, (Local, 4)), C(ScrCatalogue.Action.Add, (Local, 5), (Local, 5), N(1)), C(0)),
        L(C(ScrCatalogue.Condition.LessOrEqual, (Local, 4), N(30)), C(ScrCatalogue.Action.Add, (Local, 4), (Local, 4), N(1)), C(ScrCatalogue.Action.Goto, N(8))),
        Do(C(ScrCatalogue.Action.Goto, N(5))),
        L(C(ScrCatalogue.Condition.Equal, (Local, 5), N(0)), C(ScrCatalogue.Action.ReturnRunDefault), C(0)),
        Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
        L(C(ScrCatalogue.Condition.IsNamed, (Looped, 0), N(Marker)), C(0), C(ScrCatalogue.Action.Goto, N(18))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 6), (Local, 5))),
        Do(C(ScrCatalogue.Action.Random, N(1), N(100), (Local, 2))),
        Do(C(ScrCatalogue.Action.Random, N(100), N(1), (Local, 3))),
        L(C(ScrCatalogue.Condition.LessOrEqual, (Local, 2), (Counter, 3)), C(ScrCatalogue.Action.CreateObject, (Local, 0), (Looped, 0)), C(0)),
        L(C(ScrCatalogue.Condition.LessOrEqual, (Local, 3), (Counter, 1)), C(ScrCatalogue.Action.CreateObject, (Local, 1), (Looped, 0)), C(0)),
        Do(C(ScrCatalogue.Action.Subtract, (Local, 6), (Local, 6), N(1))),
        L(C(ScrCatalogue.Condition.Equal, (Local, 6), N(0)), C(ScrCatalogue.Action.Goto, N(18)), C(ScrCatalogue.Action.Goto, N(12))),
        Do(C(ScrCatalogue.Action.LoopEnd)),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // Counter 0 names product B (61: 10061), counter 1 its chance, counter 2 product A (81: 10081), counter 3 its chance.
    private const uint Counters = 61u | 30u << 8 | 81u << 16 | 100u << 24;

    private static WorldHost Host(params int[] draws)
    {
        var inner = new FakeHost();
        inner.Scripts[Script] = ScrScriptLookup.Of(Generator());
        var host = new WorldHost(inner);
        host.Vicinity.AddRange([Tree, Mark, Rock]);
        host.Names[Mark] = Marker;
        foreach (var draw in draws) host.Draws.Enqueue(draw);
        return host;
    }

    private static ScrRunResult Enter(WorldHost host, uint flags, ScrRunOptions? options = null) =>
        ScrDispatcher.RunSector(new(new(Script, flags, Counters), Pc), host, options ?? Steamwoven);

    [TestMethod]
    public void The_generator_draws_twice_per_set_flag_at_each_marker_and_creates_next_to_it()
    {
        // Two flags set: two rounds at the marker. Round 1: 50 ≤ 100 creates A, 20 ≤ 30 creates B; round 2: 100 creates A, 31 does not.
        var host = Host(50, 20, 100, 31);
        var run = Enter(host, 0b101);
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { new ScrCreateRequest(10081, Mark), new ScrCreateRequest(10061, Mark), new ScrCreateRequest(10081, Mark) },
            run.Effects.Creations.ToArray());
        // The bounds are passed in order whichever way the script names them (PG5), and the vicinity is centred on the assigned attachee.
        CollectionAssert.AreEqual(new[] { (1, 100), (1, 100), (1, 100), (1, 100) }, host.Asked);
        CollectionAssert.AreEqual(new[] { Pc }, host.Centres);
        Assert.IsNull(run.Effects.Writeback, "the generator changes nothing of its own");
    }

    [TestMethod]
    public void No_flag_set_returns_before_the_loop()
    {
        var host = Host();
        var run = Enter(host, 0);
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        Assert.AreEqual(0, run.Effects.Creations.Count);
        Assert.AreEqual(0, host.Centres.Count);
    }

    [TestMethod]
    public void A_counter_index_above_3_is_admitted_only_by_a_wrapping_set_and_reads_index_mod_4()
    {
        var program = Generator();
        var refused = ScrAdmission.Admit(program, ScrAdmissionSet.S14);
        Assert.IsFalse(refused.IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S15).IsAdmitted);
        Assert.IsFalse(ScrAdmissionSet.S15.IsSubsetOf(new ScrAdmissionSet("no-wrap", ScrAdmissionSet.S15.Conditions, ScrAdmissionSet.S15.Actions,
            ScrAdmissionSet.S15.ValueKinds, ScrAdmissionSet.S15.FocusKinds)));
        // Counter index 7 reads counter 3 and writes it.
        var host = Host();
        host.Inner.Scripts[5] = ScrScriptLookup.Of(Program(Do(C(ScrCatalogue.Action.Add, (Counter, 7), (Counter, 7), N(1))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var run = ScrDispatcher.RunSector(new(new(5, 0, 0x2A00_0000), Pc), host, Steamwoven);
        Assert.AreEqual(0x2B00_0000u, run.Effects.Writeback!.Slot.Counters);
    }

    [TestMethod]
    public void The_original_keeps_the_first_100_scenery_objects()
    {
        // A loop that creates next to every object of the vicinity.
        var program = Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
            Do(C(ScrCatalogue.Action.CreateObject, N(1), (Looped, 0))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host();
        host.Inner.Scripts[6] = ScrScriptLookup.Of(program);
        host.Vicinity.Clear();
        host.Vicinity.AddRange(Enumerable.Range(0, 150).Select(i => new ScrObjectRef(1, $"sector:7:{i}")));
        Assert.AreEqual(150, ScrDispatcher.RunSector(new(new(6, 0, 0), Pc), host, Steamwoven).Effects.Creations.Count);
        var original = ScrDispatcher.RunSector(new(new(6, 0, 0), Pc), host, Original).Effects.Creations;
        Assert.AreEqual(ScrWorld.VicinityListCap, original.Count);
        Assert.AreEqual(host.Vicinity[99], original[^1].Near);
    }

    [TestMethod]
    public void An_empty_list_goes_on_after_its_loop_end_and_stray_loop_commands_do_nothing()
    {
        // Line 1: loop end without a loop does nothing. Line 2: loop for over an empty vicinity skips past line 5 (the first "true"/loop end)
        // to line 6. The current looped object is none, so the create of line 7 makes nothing.
        var program = Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
            Do(C(ScrCatalogue.Action.CreateObject, N(1), (Looped, 0))),
            L(C(ScrCatalogue.Condition.LocalFlag, N(0)), C(ScrCatalogue.Action.LoopEnd), C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.CreateObject, N(2), (Looped, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(6, ScrAdmission.AfterLoopEnd(program, 2));
        var host = Host();
        host.Vicinity.Clear();
        host.Inner.Scripts[8] = ScrScriptLookup.Of(program);
        var run = ScrDispatcher.RunSector(new(new(8, 0, 0), Pc), host, Steamwoven);
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        Assert.AreEqual(0, run.Effects.Creations.Count);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 6, 7 }, run.Trace.Select(t => t.Line).ToArray());
        // Without a loop end the scan reads past the end: the run stops on a line outside the script.
        var open = Program(Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(3, ScrAdmission.AfterLoopEnd(open, 0));
    }

    [TestMethod]
    public void A_loop_for_inside_a_loop_does_nothing_and_the_last_object_stays_current()
    {
        var program = Program(
            Do(C(ScrCatalogue.Action.AssignObject, (Attachee, -1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
            Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
            Do(C(ScrCatalogue.Action.CreateObject, N(1), (Looped, 0))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.CreateObject, N(2), (Looped, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host();
        host.Inner.Scripts[9] = ScrScriptLookup.Of(program);
        var run = ScrDispatcher.RunSector(new(new(9, 0, 0), Pc), host, Steamwoven);
        CollectionAssert.AreEqual(new[] { new ScrCreateRequest(1, Tree), new(1, Mark), new(1, Rock), new(2, Rock) }, run.Effects.Creations.ToArray());
        Assert.AreEqual(1, host.Centres.Count, "the inner loop for fills no list");
    }

    [TestMethod]
    public void Random_stores_into_its_third_operand_and_a_number_there_refuses_the_script()
    {
        var host = Host(7);
        host.Inner.Scripts[10] = ScrScriptLookup.Of(Program(Do(C(ScrCatalogue.Action.Random, N(3), N(9), (Counter, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(7u, ScrDispatcher.RunSector(new(new(10, 0, 0), Pc), host, Steamwoven).Effects.Writeback!.Slot.Counters);
        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Random, N(3), N(9), N(0)))), ScrAdmissionSet.S15);
        Assert.AreEqual(ScrRefusalCode.WriteToNumber, literal.Refusal!.Code);
    }

    [TestMethod]
    public void A_world_set_needs_a_host_that_answers_the_world()
    {
        var host = new FakeHost();
        host.Scripts[Script] = ScrScriptLookup.Of(Generator());
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunSector(new(new(Script, 1, Counters), Pc), host, Steamwoven));
        Assert.IsTrue(ScrAdmissionSet.S14.IsSubsetOf(ScrAdmissionSet.S15));
    }

    [TestMethod]
    public void A_region_admission_follows_loop_ends_back_to_every_loop_for()
    {
        // From line 0 the region reaches the loop body through the loop end's jump back, and the line after the loop end.
        var program = Program(
            Do(C(ScrCatalogue.Action.Goto, N(2))),
            Do(C(CritterDisband)),
            Do(C(ScrCatalogue.Action.LoopFor, (Scenery, 0))),
            Do(C(ScrCatalogue.Action.LoopEnd)),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var region = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S15, 0);
        Assert.IsTrue(region.IsAdmitted);
        CollectionAssert.AreEqual(new[] { 0, 2, 3, 4 }, region.Admitted!.Region.ToArray());
    }
}
