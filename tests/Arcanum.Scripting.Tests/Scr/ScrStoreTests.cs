using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The value kinds global variable (1), PC variable (5) and PC flag (6) of set S21 (ARC-DEV-100, ledger GV1–GV4): a run reads the
/// host's stores, writes land in an overlay the run reads back, and the changed values come back in the effects. Programs, objects and
/// numbers are invented.</summary>
[TestClass]
public sealed class ScrStoreTests
{
    private const byte GlobalVariable = 1, PcVariable = 5, PcFlag = 6, Triggerer = 0, Attachee = 1, Extra = 12, Player = 21;
    // The commands these programs use (control flow, arithmetic, global flags, call script) with the store kinds and the focus kinds that name a PC or a
    // chest; S21 itself also holds the commands that need the world, so its host would have to answer more.
    private static readonly ScrAdmissionSet Set = new("store", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions.Append(ScrCatalogue.Action.CallScript),
        ScrAdmissionSet.S1.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player, ScrFocusKind.LocalObject]);
    private static readonly ScrRunOptions Options = new() { Admission = Set };
    private static readonly ScrRunOptions Original = new() { Admission = Set, Rules = ScrRuleProfile.Original };

    /// <summary>The operand of a PC variable or flag: the focus kind in the high 16 bits, the index in the low 16.</summary>
    private static (byte, int) PcOp(byte kind, byte focus, int index) => (kind, (focus << 16) | index);
    private static (byte, int) G(int index) => (GlobalVariable, index);
    private static (byte, int) Loc(int index) => (Local, index);

    private sealed class StoreHost(FakeHost inner) : IScrHost, IScrStoreQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<int, int> Variables { get; } = new();
        public Dictionary<int, int> PcVariables { get; } = new();
        public HashSet<int> PcFlags { get; } = new();
        public List<string> Asked { get; } = new();
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
        public int GlobalVariable(int index) { Asked.Add($"gv {index}"); return Variables.GetValueOrDefault(index); }
        public int PcVariable(int index) { Asked.Add($"pv {index}"); return PcVariables.GetValueOrDefault(index); }
        public bool PcFlag(int index) { Asked.Add($"pf {index}"); return PcFlags.Contains(index); }
    }

    private static (ScrRunResult Run, StoreHost Host) Use(ScrProgram program, Action<StoreHost>? arrange = null, ScrRunOptions? options = null)
    {
        var host = new StoreHost(new FakeHost());
        host.Inner.Attach(Chest, ScrPoint.Use, 2793, program);
        arrange?.Invoke(host);
        return (ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, null), host, options ?? Options), host);
    }

    [TestMethod]
    public void A_global_variable_is_read_from_the_host_written_to_the_overlay_and_handed_back_changed()
    {
        var (run, _) = Use(Program(Do(C(ScrCatalogue.Action.Add, G(3), G(3), N(1))), Do(C(ScrCatalogue.Action.Add, G(3), G(3), G(3))), Do(C(ScrCatalogue.Action.Assign, Loc(0), G(3))),
            Do(C(ScrCatalogue.Action.Assign, G(4), N(0))), Do(C(ScrCatalogue.Action.Assign, G(5), Loc(0))), Do(C(1))), host => host.Variables[3] = 100);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(3, 202), new ScrVariableWrite(5, 202) }, run.Effects.GlobalVariables.ToArray(),
            "101 + 101 from the run's own write, copied through a local; variable 4 was written with its own value (0), which is no change");
        Assert.AreEqual(0, run.Effects.PcVariables.Count + run.Effects.PcFlags.Count);
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_global_variable_keeps_a_whole_32_bit_value_and_nothing_masks_it()
    {
        var (run, _) = Use(Program(Do(C(ScrCatalogue.Action.Assign, G(0), N(int.MaxValue))), Do(C(ScrCatalogue.Action.Add, G(0), G(0), N(1))),
            Do(C(ScrCatalogue.Action.Assign, G(1), N(-5))), Do(C(ScrCatalogue.Action.Assign, G(1999), N(0x12345678))), Do(C(1))));
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(0, int.MinValue), new ScrVariableWrite(1, -5), new ScrVariableWrite(1999, 0x12345678) },
            run.Effects.GlobalVariables.ToArray(), "GV1: a global variable is a plain 32-bit word (0x444C90); counters are the masked ones (X8)");
    }

    [TestMethod]
    public void A_condition_compares_a_global_variable_and_the_run_asks_the_host_once_per_unwritten_read()
    {
        var (run, host) = Use(Program(L(C(ScrCatalogue.Condition.LessOrEqual, N(50), G(7)), C(ScrCatalogue.Action.SetGlobalFlag, N(20)), C(ScrCatalogue.Action.SetGlobalFlag, N(21))),
            Do(C(1))), arrange: h => h.Variables[7] = 60);
        Assert.AreEqual(20, run.Effects.GlobalFlags.Single().Index, "50 is below 60");
        CollectionAssert.AreEqual(new[] { "gv 7" }, host.Asked);
    }

    [TestMethod]
    public void The_hero_s_variable_is_read_and_written_by_index_through_the_player_or_the_triggerer()
    {
        var (run, _) = Use(Program(Do(C(ScrCatalogue.Action.Assign, G(0), PcOp(PcVariable, Player, 2000))), Do(C(ScrCatalogue.Action.Add, PcOp(PcVariable, Triggerer, 65535), PcOp(PcVariable, Triggerer, 65535), N(42))),
            Do(C(ScrCatalogue.Action.Assign, PcOp(PcVariable, Player, 7), PcOp(PcVariable, Triggerer, 65535))), Do(C(1))), host => host.PcVariables[2000] = 9);
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(0, 9) }, run.Effects.GlobalVariables.ToArray());
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(7, 42), new ScrVariableWrite(65535, 42) }, run.Effects.PcVariables.ToArray(),
            "GV3: the low 16 bits are the index, the high 16 the object; the run reads its own write back");
    }

    [TestMethod]
    public void An_object_that_is_not_a_PC_reads_zero_and_ignores_a_write()
    {
        var (run, host) = Use(Program(Do(C(ScrCatalogue.Action.Assign, G(0), PcOp(PcVariable, Attachee, 3))), Do(C(ScrCatalogue.Action.Assign, PcOp(PcVariable, Attachee, 3), N(9))),
            Do(C(ScrCatalogue.Action.Assign, G(1), PcOp(PcFlag, Attachee, 3))), Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Attachee, 3), N(1))), Do(C(1))),
            h => { h.PcVariables[3] = 5; h.PcFlags.Add(3); });
        Assert.IsTrue(run.Effects.IsEmpty, "the chest is no PC (SA14): both reads give 0 (so variable 0 and 1 stay as they were) and both writes do nothing");
        Assert.IsFalse(host.Asked.Any(question => question.StartsWith('p')), "the host's PC store is never asked about it");
    }

    [TestMethod]
    public void A_PC_flag_is_bit_n_of_the_stored_word_and_a_write_sets_or_clears_that_flag()
    {
        var (run, host) = Use(Program(Do(C(ScrCatalogue.Action.Assign, G(0), PcOp(PcFlag, Player, 70))), Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 33), N(1))),
            Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 5), N(0))), Do(C(ScrCatalogue.Action.Assign, G(1), PcOp(PcFlag, Player, 33))), Do(C(1))),
            h => { h.PcFlags.Add(70); h.PcFlags.Add(5); });
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(0, 1), new ScrVariableWrite(1, 1) }, run.Effects.GlobalVariables.ToArray());
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(5, false), new ScrFlagWrite(33, true) }, run.Effects.PcFlags.ToArray());
        Assert.IsTrue(host.Asked.Contains("pf 70"));
    }

    [TestMethod]
    public void A_PC_flag_write_spills_into_the_word_under_the_Original_setting_as_a_global_flag_does()
    {
        var program = Program(Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 33), N(2))), Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 36), N(-1))), Do(C(1)));
        // 2 shifted to flag 33 sets flag 34 and gives flag 33 bit 0 of the value (0, as it was); -1 shifted to flag 36 sets every flag of the word from 36 up.
        var spilled = Use(program, options: Original).Run.Effects.PcFlags.ToArray();
        CollectionAssert.AreEqual(new[] { 34 }.Concat(Enumerable.Range(36, 28)).Select(i => new ScrFlagWrite(i, true)).ToArray(), spilled,
            "GV4: the same spill as a global flag (X10, 0x444E90): the value ORed, shifted, into the word; nothing leaves the 32-flag word");
        var plain = Use(program).Run.Effects.PcFlags.ToArray();
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(36, true) }, plain, "Steamwoven: one flag, bit 0 of the value (2 writes 0 to flag 33, which is already clear)");
    }

    [TestMethod]
    public void An_absent_object_of_a_PC_operand_stops_the_run_as_for_any_object_operand()
    {
        var (run, _) = Use(Program(Do(C(ScrCatalogue.Action.Assign, G(0), N(5))), Do(C(ScrCatalogue.Action.Assign, G(1), PcOp(PcVariable, Extra, 1))), Do(C(1))));
        Assert.AreEqual(ScrStop.MissingObject, run.Stop, "OV3: the original reads undefined data; the project stops the run");
        Assert.AreEqual(1, run.StopLine);
    }

    [TestMethod]
    public void A_called_script_shares_the_stores_of_its_caller()
    {
        var host = new StoreHost(new FakeHost());
        host.Inner.Attach(Chest, ScrPoint.Use, 2793, Program(Do(C(ScrCatalogue.Action.Assign, G(2), N(4))), Do(C(7, N(640), N(0), (Triggerer, 0), (Attachee, 0))),
            Do(C(ScrCatalogue.Action.Add, G(2), G(2), N(1))), Do(C(1))));
        host.Inner.Scripts[640] = ScrScriptLookup.Of(Program(Do(C(ScrCatalogue.Action.Multiply, G(2), G(2), N(10))), Do(C(ScrCatalogue.Action.Assign, PcOp(PcVariable, Triggerer, 8), G(2))), Do(C(1))));
        var run = ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, null), host, Options);
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(2, 41) }, run.Effects.GlobalVariables.ToArray(), "4, times 10 in the called script, plus 1");
        CollectionAssert.AreEqual(new[] { new ScrVariableWrite(8, 40) }, run.Effects.PcVariables.ToArray());
    }

    [TestMethod]
    public void Only_S21_and_the_sets_built_on_its_kinds_admit_the_store_kinds()
    {
        Assert.IsTrue(ScrAdmissionSet.S20.IsSubsetOf(ScrAdmissionSet.S21));
        Assert.IsFalse(ScrAdmissionSet.S21.IsSubsetOf(ScrAdmissionSet.S20));
        CollectionAssert.AreEquivalent(new[] { ScrValueKind.GlobalVariable, ScrValueKind.PcVariable, ScrValueKind.PcFlag }, ScrAdmissionSet.StoreValueKinds.ToArray());
        var program = Program(Do(C(ScrCatalogue.Action.Assign, G(0), N(1))), Do(C(1)));
        Assert.AreEqual(ScrRefusalCode.ValueKindNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S20).Refusal!.Code);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S21).IsAdmitted);
        var narrow = new ScrAdmissionSet("narrow", ScrAdmissionSet.S21.Conditions, ScrAdmissionSet.S21.Actions, ScrAdmissionSet.S1.ValueKinds.Append(ScrValueKind.PcFlag), ScrAdmissionSet.S21.FocusKinds);
        Assert.AreEqual(ScrRefusalCode.ValueKindNotAdmitted, ScrAdmission.Admit(program, narrow).Refusal!.Code, "a set names each kind it admits");
        var plain = new FakeHost();
        plain.Attach(Chest, ScrPoint.Use, 2793, program);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, null), plain, Options),
            "a set with the store kinds needs a host that answers them, checked before any event runs");
    }

    [TestMethod]
    public void A_global_variable_index_is_checked_and_a_PC_operand_names_a_focus_kind_the_set_admits()
    {
        var refused = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, G(2000), N(1))), Do(C(1))), ScrAdmissionSet.S21).Refusal!;
        Assert.AreEqual((ScrRefusalCode.IndexOutOfRange, 2000), (refused.Code, refused.Value ?? -1));
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, G(1999), N(1))), Do(C(1))), ScrAdmissionSet.S21).IsAdmitted);
        // Any of the 65,536 indexes, any admitted focus kind.
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 65535), N(1))), Do(C(1))), ScrAdmissionSet.S21).IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, G(0), PcOp(PcVariable, 11, 0))), Do(C(1))), ScrAdmissionSet.S21).IsAdmitted, "the object variable 0");
        var unknown = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, G(0), PcOp(PcVariable, 24, 0))), Do(C(1))), ScrAdmissionSet.S21).Refusal!;
        Assert.AreEqual(ScrRefusalCode.UnknownFocusKind, unknown.Code, "focus kind 24 does not exist");
        var negative = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, G(0), (PcVariable, -65536))), Do(C(1))), ScrAdmissionSet.S21).Refusal!;
        Assert.AreEqual(ScrRefusalCode.UnknownFocusKind, negative.Code, "the shift keeps the sign (sar 0x10): -1 is no focus kind");
        var narrow = new ScrAdmissionSet("narrow", ScrAdmissionSet.S21.Conditions, ScrAdmissionSet.S21.Actions, ScrAdmissionSet.S21.ValueKinds, [ScrFocusKind.Player]);
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Player, 1), N(1))), Do(C(1))), narrow).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.Assign, PcOp(PcFlag, Attachee, 1), N(1))), Do(C(1))), narrow).Refusal!.Code,
            "the object the store belongs to must be one the set resolves");
    }
}
