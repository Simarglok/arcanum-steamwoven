using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Call script, assign object, get object type and the object variables of set S11 (ARC-SCRIPT-018, ledger
/// OV1–OV4). Programs, objects and numbers are invented.</summary>
[TestClass]
public sealed class ScrObjectVariableTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12, LocalObject = 11, LocalVariable = 2;
    private static readonly ScrRunOptions S11 = new() { Admission = ScrAdmissionSet.S11 };
    private static (byte, int) Var(int index) => (LocalVariable, index);
    private static (byte, int) Obj(int index) => (LocalObject, index);

    /// <summary>A recording host that also answers object types.</summary>
    private sealed class TypeHost(FakeHost inner) : IScrHost, IScrObjectQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, int> Types { get; } = new() { [Pc] = 15, [Npc] = 16, [Chest] = 2 };
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
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => Types.TryGetValue(obj, out var type) ? type : null;
    }

    private static TypeHost With(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Chest, ScrPoint.Use, 2793, program);
        return new TypeHost(host);
    }

    private static ScrRunResult Use(IScrHost host, ScrRunOptions? options = null, ScrObjectRef? extra = null) =>
        ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, extra), host, options ?? S11);

    // A program with header local flags and counters (the bytes a called script starts from, OV1).
    private static ScrProgram WithHeader(ScrProgram program, uint localFlags, uint counters)
    {
        var bytes = new byte[64 + program.Count * 132];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, localFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), counters);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), program.FileFlags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), program.Count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), program.Count);
        for (var line = 0; line < program.Count; line++)
            foreach (var (offset, clause) in new[] { (0, program.Lines[line].Condition), (44, program.Lines[line].OnTrue), (88, program.Lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < ScrClause.Operands; i++) { slot[4 + i] = clause.Kind(i); BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Value(i)); }
            }
        return ScrProgram.Read(bytes).Program!;
    }

    [TestMethod]
    public void Assign_object_and_get_object_type_fill_the_run_s_object_and_number_stores()
    {
        // obj0 := attachee; var0 := type(obj0); if var0 == 2 set flag 7; obj1 := triggerer; var1 := type(obj1); if var1 == 15 set flag 8.
        var host = With(Program(Do(C(15, Obj(0), (Attachee, 0))), Do(C(34, Obj(0), Var(0))), L(C(ScrCatalogue.Condition.Equal, Var(0), N(2)), C(100, N(7)), C(0)),
            Do(C(15, Obj(1), (Triggerer, 0))), Do(C(34, Obj(1), Var(1))), L(C(ScrCatalogue.Condition.Equal, Var(1), N(15)), C(100, N(8)), C(0)), Do(C(1))));
        var run = Use(host);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { 7, 8 }, run.Effects.GlobalFlags.Select(f => f.Index).ToArray());
    }

    [TestMethod]
    public void Assign_object_can_replace_the_run_s_own_roles()
    {
        // triggerer := attachee, then teleport the triggerer; extra := triggerer (now the chest), then play a sound on the extra.
        var host = With(Program(Do(C(15, (Triggerer, 0), (Attachee, 0))), Do(C(44, (Triggerer, 0), N(5001), N(1), N(2))),
            Do(C(15, (Extra, 0), (Triggerer, 0))), Do(C(83, N(4000), (Extra, 0))), Do(C(15, (21, 0), (Attachee, 0))), Do(C(1))));
        var run = Use(host);
        Assert.AreEqual(Chest, run.Effects.Teleports.Single().Object, "the triggerer is the chest now");
        Assert.AreEqual(Chest, run.Effects.Sounds.Single().Object);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "a store the original does not write (the player) is ignored");
    }

    [TestMethod]
    public void An_unset_object_variable_names_no_object()
    {
        // An absent extra object assigned into obj2 leaves it empty; sound on it plays nothing, its type stops the run.
        var host = With(Program(Do(C(15, Obj(2), (Extra, 0))), Do(C(83, N(4000), Obj(2))), Do(C(34, Obj(3), Var(0))), Do(C(1))));
        var run = Use(host);
        Assert.AreEqual((ScrStop.MissingObject, 2), (run.Stop, run.StopLine));
        Assert.AreEqual(0, run.Effects.Sounds.Count);
        host.Types.Remove(Chest);
        var unknown = With(Program(Do(C(34, (Attachee, 0), Var(0))), Do(C(1))));
        unknown.Types.Remove(Chest);
        Assert.AreEqual(ScrStop.MissingObject, Use(unknown).Stop, "an object the host does not know");
    }

    [TestMethod]
    public void Admission_refuses_a_literal_type_store_and_an_object_variable_outside_ten()
    {
        var literal = ScrAdmission.Admit(Program(Do(C(34, (Attachee, 0), N(3)))), ScrAdmissionSet.S11);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 1), (literal.Refusal!.Code, literal.Refusal.Slot));
        var outside = ScrAdmission.Admit(Program(Do(C(15, Obj(10), (Attachee, 0)))), ScrAdmissionSet.S11);
        Assert.AreEqual(ScrRefusalCode.IndexOutOfRange, outside.Refusal!.Code);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(Program(Do(C(7, N(1), N(0), (Triggerer, 0), (Attachee, 0)))), ScrAdmissionSet.S10).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S10.IsSubsetOf(ScrAdmissionSet.S11));
    }

    [TestMethod]
    public void A_set_with_get_object_type_needs_object_queries()
    {
        var plain = new FakeHost();
        plain.Attach(Chest, ScrPoint.Use, 2793, Program(Do(C(1))));
        var types = new ScrAdmissionSet("types", ScrAdmissionSet.S5.Conditions, ScrAdmissionSet.S5.Actions.Append(34), ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => Use(plain, new() { Admission = types }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Use(With(Program(Do(C(1)))), S11 with { MaxCallDepth = 0 }));
    }

    [TestMethod]
    public void A_called_script_runs_with_its_own_header_and_roles_and_the_caller_goes_on()
    {
        // The callee (600) from line 1: it sees the caller's flag write, prints a line on its attachee, sets a local flag of its
        // own and returns skip default; the caller then sets flag 9.
        var callee = WithHeader(Program(Do(C(100, N(50))), L(C(ScrCatalogue.Condition.GlobalFlag, N(20)), C(100, N(21)), C(0)), Do(C(24, N(3), N(0))), Do(C(8, N(4))),
            L(C(ScrCatalogue.Condition.LocalFlag, N(1)), C(100, N(22)), C(0)), Do(C(1))), 0b10, 0);
        var host = With(Program(Do(C(100, N(20))), Do(C(7, N(600), N(1), (Attachee, 0), (Triggerer, 0))), Do(C(100, N(9))), Do(C(1))));
        host.Inner.Scripts[600] = ScrScriptLookup.Of(callee);
        var run = Use(host);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { 9, 20, 21, 22 }, run.Effects.GlobalFlags.Select(f => f.Index).ToArray(),
            "line 0 of the callee is skipped, the caller's flag 20 is seen, the header's local flag 1 holds, the caller goes on");
        Assert.AreEqual(new ScrPrintLine(600, 3, 0), run.Effects.Lines.Single(), "the callee's own DLG, on its attachee (the PC)");
        Assert.IsNull(run.Effects.Writeback, "the caller's slot is unchanged; the callee's local flag is not kept");
    }

    [TestMethod]
    public void A_missing_callee_runs_nothing_and_a_refused_or_too_deep_one_ends_the_dispatch()
    {
        var host = With(Program(Do(C(7, N(601), N(0), (Triggerer, 0), (Attachee, 0))), Do(C(100, N(9))), Do(C(1))));
        Assert.AreEqual(9, Use(host).Effects.GlobalFlags.Single().Index, "a missing script returns at once");

        host.Inner.Scripts[601] = ScrScriptLookup.Of(Program(Do(C(FloatLine))));
        var refused = Use(host);
        Assert.AreEqual((ScrOutcome.Refused, 601, ScrRefusalCode.ActionNotAdmitted), (refused.Outcome, refused.Script, refused.Refusal!.Code));
        Assert.AreSame(ScrEffects.None, refused.Effects);

        // 602 sets a flag and calls itself: the bound ends the whole dispatch with nothing applied.
        host.Inner.Scripts[601] = ScrScriptLookup.Of(Program(Do(C(100, N(30))), Do(C(7, N(601), N(0), (Triggerer, 0), (Attachee, 0))), Do(C(1))));
        var deep = Use(host, S11 with { MaxCallDepth = 4 });
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.CallDepth), (deep.Outcome, deep.Stop));
        Assert.AreSame(ScrEffects.None, deep.Effects);
    }
}
