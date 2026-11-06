using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-157 (bug B23): get stat (33) of set S22 (ledger GS1–GS4). Programs and stat values are invented.</summary>
[TestClass]
public sealed class ScrStatTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    // The commands these programs use (control flow, arithmetic, call script) with get stat and the focus kinds that name one object each; S22
    // itself also holds the commands that need the world, so its host would have to answer more.
    private static readonly ScrAdmissionSet Set = new("stat", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions.Append(ScrCatalogue.Action.GetStat),
        ScrAdmissionSet.S1.ValueKinds, [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject]);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    /// <summary>A recording host that also answers stats.</summary>
    private sealed class StatHost(FakeHost inner) : IScrHost, IScrStatQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<(ScrObjectRef, int), int> Stats { get; } = new();
        public HashSet<ScrObjectRef> Dumb { get; } = new();
        public List<(ScrObjectRef, int)> AskedStats { get; } = new();
        public List<ScrObjectRef> AskedDumb { get; } = new();
        public int? EffectiveStat(ScrObjectRef obj, int stat) { AskedStats.Add((obj, stat)); return Stats.TryGetValue((obj, stat), out var value) ? value : null; }
        public bool IsDumbByBackground(ScrObjectRef obj) { AskedDumb.Add(obj); return Dumb.Contains(obj); }
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
    }

    private static Clause GetStat(int stat, byte focus, int counter) => C(ScrCatalogue.Action.GetStat, N(stat), (focus, 0), (Counter, counter));

    private static (ScrRunResult Run, StatHost Host) Run(ScrProgram program, Action<StatHost>? arrange = null)
    {
        var host = new StatHost(new FakeHost());
        host.Inner.Attach(Npc, Use, 2023, program);
        arrange?.Invoke(host);
        return (ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, Options), host);
    }

    [TestMethod]
    public void Get_stat_stores_the_hosts_effective_stat_of_the_named_object_and_goes_on()
    {
        var (run, host) = Run(Program(Do(GetStat(5, Triggerer, 0)), Do(GetStat(19, Attachee, 1)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h =>
        {
            h.Stats[(Pc, 5)] = 12;
            h.Stats[(Npc, 19)] = 77;
        });
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run goes on after each store");
        Assert.AreEqual(12u | 77u << 8, run.Effects.Writeback!.Slot.Counters, "the stats in counters 0 and 1");
        CollectionAssert.AreEqual(new[] { (Pc, 5), (Npc, 19) }, host.AskedStats, "the stat number as the script gives it, the object operand second");
        Assert.AreEqual(0, host.AskedDumb.Count, "the dumb test belongs to intelligence only");
    }

    [TestMethod]
    public void Get_stat_reads_a_negative_stat_as_the_host_gives_it()
    {
        var (run, _) = Run(Program(Do(C(ScrCatalogue.Action.GetStat, N(19), (Triggerer, 0), (Local, 0))), Do(C(ScrCatalogue.Action.Add, (Counter, 0), (Local, 0), N(300))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h => h.Stats[(Pc, 19)] = -100);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        Assert.AreEqual(200u, run.Effects.Writeback!.Slot.Counters & 0xFF, "an alignment of −100 plus 300, the value kept whole in the local variable");
    }

    [TestMethod]
    public void Intelligence_above_four_reads_one_for_a_dumb_background_and_nothing_else_changes()
    {
        var (run, host) = Run(Program(Do(GetStat(4, Triggerer, 0)), Do(GetStat(4, Attachee, 1)), Do(GetStat(5, Triggerer, 3)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h =>
        {
            h.Stats[(Pc, 4)] = 10; h.Dumb.Add(Pc);
            h.Stats[(Npc, 4)] = 4; h.Dumb.Add(Npc);
            h.Stats[(Pc, 5)] = 9;
        });
        Assert.AreEqual(1u | 4u << 8 | 9u << 24, run.Effects.Writeback!.Slot.Counters, "10 reads 1 for the dumb PC; 4 stays 4 (the test is asked only above 4); willpower is untouched");
        CollectionAssert.AreEqual(new[] { Pc }, host.AskedDumb, "asked once, for the one stat that is above 4");
    }

    [TestMethod]
    public void A_clever_background_keeps_the_intelligence()
    {
        var (run, host) = Run(Program(Do(GetStat(4, Triggerer, 0)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h => h.Stats[(Pc, 4)] = 10);
        Assert.AreEqual(10u, run.Effects.Writeback!.Slot.Counters & 0xFF);
        Assert.AreEqual(1, host.AskedDumb.Count);
    }

    [TestMethod]
    public void A_stat_the_host_cannot_answer_ends_the_dispatch_as_not_modelled_with_nothing_applied()
    {
        var (run, _) = Run(Program(Do(GetStat(5, Triggerer, 0)), Do(GetStat(6, Triggerer, 1)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h => h.Stats[(Pc, 5)] = 3);
        Assert.AreEqual(ScrStop.NotModelled, run.Stop);
        Assert.IsTrue(run.Effects.IsEmpty, "the first store is not applied either");
    }

    [TestMethod]
    public void Get_stat_on_an_absent_object_stops_the_run_and_its_store_must_not_be_a_number()
    {
        var (run, host) = Run(Program(Do(GetStat(3, Extra, 0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual((ScrStop.MissingObject, 0), (run.Stop, run.StopLine), "no extra object: the run stops at the read, as at every object read");
        Assert.AreEqual(0, host.AskedStats.Count);

        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetStat, N(3), (Triggerer, 0), N(5)))), Set);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 2), (literal.Refusal!.Code, literal.Refusal.Slot), "the store is operand 2");
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetStat, (Local, 3), (Triggerer, 0), (Local, 0)))), Set).IsAdmitted, "a computed stat number is admitted");
    }

    [TestMethod]
    public void Only_S22_and_the_sets_built_on_it_admit_get_stat_and_its_host_must_answer_it()
    {
        Assert.IsTrue(ScrAdmissionSet.S21.IsSubsetOf(ScrAdmissionSet.S22));
        Assert.IsFalse(ScrAdmissionSet.S22.IsSubsetOf(ScrAdmissionSet.S21));
        var program = Program(Do(GetStat(5, Triggerer, 0)), Do(C(1)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S21).Refusal!.Code);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S22).IsAdmitted);
        var plain = new FakeHost();
        plain.Attach(Npc, Use, 2023, program);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(Use, Npc, Pc, null), plain, Options),
            "a set with get stat needs a host that answers it, checked before any event runs");
    }
}
