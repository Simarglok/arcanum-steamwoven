using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

[TestClass]
public sealed class ScrAdmissionTests
{
    private const byte Counter = 0, GlobalVariable = 1, Local = 2, Number = 3, GlobalFlag = 4, PcFlag = 6;
    private static readonly ScrAdmissionSet S1 = ScrAdmissionSet.S1;

    [TestMethod]
    public void Admits_a_script_using_every_S1_type_and_value_kind()
    {
        var program = Program(
            L(C(3, (Number, 31)), A(3, (Number, 2)), A(8, (Number, 0))),
            L(C(4, (Counter, 3), (Local, 9)), A(10, (Local, 0), (GlobalFlag, 3199)), A(11, (Counter, 0), (Number, -5), (Local, 1))),
            L(C(5, (GlobalFlag, 0), (Number, 1)), A(12, (GlobalFlag, 5), (Local, 1), (Number, 2)), A(13, (Local, 2), (Local, 2), (Counter, 1))),
            L(C(43, (Number, 3199)), A(14, (Local, 3), (Local, 3), (Number, 0)), A(42, (Counter, 2))),
            L(C(0), A(43, (Local, 3)), A(0)),
            L(C(0), A(100, (Number, 0)), A(0)),
            L(C(0), A(101, (Number, 1)), A(0)),
            L(C(0), A(9, (Counter, 0)), A(0)),
            L(C(43, (Local, 4)), A(2), A(1)),
            L(C(0), A(3, (Number, 10)), A(0)));
        var result = ScrAdmission.Admit(program, S1);
        Assert.IsTrue(result.IsAdmitted, result.Refusal?.ToString());
        Assert.AreSame(program, result.Admitted!.Program);
        Assert.AreSame(S1, result.Admitted.Set);
        Assert.IsNull(result.Refusal);
    }

    [TestMethod]
    public void An_empty_script_is_admitted()
    {
        Assert.IsTrue(ScrAdmission.Admit(Program(), S1).IsAdmitted);
    }

    [TestMethod]
    public void Bytes_in_slots_a_type_never_reads_are_ignored()
    {
        // Return, goto and the always-true condition keep editor bytes in their other slots
        // (ARC-RES-045: nearly every clause); none of them is read.
        var program = Program(
            L(C(0, (99, -1), (PcFlag, 7), (200, 1)), A(3, (Number, 1), (250, 8), (PcFlag, 1)), A(0, (13, 13))),
            L(C(3, (Number, 2), (PcFlag, 99)), A(1, (17, 17)), A(2, (255, int.MaxValue))));
        Assert.IsTrue(ScrAdmission.Admit(program, S1).IsAdmitted);
    }

    [TestMethod]
    public void The_false_action_of_an_always_true_line_is_not_read()
    {
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(0), A(1), A(500))), S1).IsAdmitted);
        AssertRefusal(Program(L(C(3, (Number, 0)), A(1), A(500))), 0, ScrClauseSide.OnFalse, ScrRefusalCode.UnknownAction, 500);
    }

    [TestMethod]
    public void Types_outside_the_catalogue_and_outside_the_set_are_refused_with_their_number()
    {
        AssertRefusal(Program(L(C(53), A(1), A(1))), 0, ScrClauseSide.Condition, ScrRefusalCode.UnknownCondition, 53);
        AssertRefusal(Program(L(C(-1), A(1), A(1))), 0, ScrClauseSide.Condition, ScrRefusalCode.UnknownCondition, -1);
        AssertRefusal(Program(L(C(0), A(142), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.UnknownAction, 142);
        AssertRefusal(Program(L(C(0), A(1), A(0)), L(C(18, (21, 0), (1, 0)), A(1), A(2))), 1, ScrClauseSide.Condition, ScrRefusalCode.ConditionNotAdmitted, 18);
        AssertRefusal(Program(L(C(0), A(44, (21, 0), (Number, 5001), (Number, 1), (Number, 1)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ActionNotAdmitted, 44);
        AssertRefusal(Program(L(C(3, (Number, 1)), A(1), A(4, (Number, 1)))), 0, ScrClauseSide.OnFalse, ScrRefusalCode.ActionNotAdmitted, 4);
    }

    [TestMethod]
    public void Operand_kinds_are_checked_against_the_signature()
    {
        var r = AssertRefusal(Program(L(C(4, (Number, 1), (7, 1)), A(1), A(1))), 0, ScrClauseSide.Condition, ScrRefusalCode.UnknownValueKind, 4);
        Assert.AreEqual(1, r.Slot); Assert.AreEqual((byte)7, r.Kind);
        r = AssertRefusal(Program(L(C(0), A(10, (Local, 0), (GlobalVariable, 10)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ValueKindNotAdmitted, 10);
        Assert.AreEqual(1, r.Slot); Assert.AreEqual(GlobalVariable, r.Kind);
        AssertRefusal(Program(L(C(0), A(10, (Local, 0), (PcFlag, 0)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ValueKindNotAdmitted, 10);
        var objects = new ScrAdmissionSet("objects", [0, ScrCatalogue.Condition.FollowingPc], [1, 2], [], [ScrFocusKind.Attachee, ScrFocusKind.Player]);
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(18, (1, 0), (21, 0)), A(1), A(2))), objects).IsAdmitted);
        r = AssertRefusal(Program(L(C(18, (1, 0), (24, 0)), A(1), A(2))), 0, ScrClauseSide.Condition, ScrRefusalCode.UnknownFocusKind, 18, objects);
        Assert.AreEqual(1, r.Slot);
        AssertRefusal(Program(L(C(18, (0, 0), (21, 0)), A(1), A(2))), 0, ScrClauseSide.Condition, ScrRefusalCode.FocusKindNotAdmitted, 18, objects);
        var local = new ScrAdmissionSet("local", [0, ScrCatalogue.Condition.FollowingPc], [1, 2], [], [ScrFocusKind.LocalObject]);
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(18, (11, 9), (11, 0)), A(1), A(2))), local).IsAdmitted);
        r = AssertRefusal(Program(L(C(18, (11, 10), (11, 0)), A(1), A(2))), 0, ScrClauseSide.Condition, ScrRefusalCode.IndexOutOfRange, 18, local);
        Assert.AreEqual(10, r.Value);
    }

    [TestMethod]
    public void Literal_indices_are_checked_against_their_store()
    {
        foreach (var (clause, side, type, value) in new[]
        {
            (L(C(3, (Number, 32)), A(1), A(1)), ScrClauseSide.Condition, 3, 32),
            (L(C(3, (Number, -1)), A(1), A(1)), ScrClauseSide.Condition, 3, -1),
            (L(C(0), A(8, (Number, 32)), A(0)), ScrClauseSide.OnTrue, 8, 32),
            (L(C(0), A(9, (Number, -1)), A(0)), ScrClauseSide.OnTrue, 9, -1),
            (L(C(43, (Number, 3200)), A(1), A(1)), ScrClauseSide.Condition, 43, 3200),
            (L(C(0), A(100, (Number, -1)), A(0)), ScrClauseSide.OnTrue, 100, -1),
            (L(C(0), A(101, (Number, 3200)), A(0)), ScrClauseSide.OnTrue, 101, 3200),
            (L(C(4, (Counter, 4), (Number, 0)), A(1), A(1)), ScrClauseSide.Condition, 4, 4),
            (L(C(0), A(10, (Local, 10), (Number, 0)), A(0)), ScrClauseSide.OnTrue, 10, 10),
            (L(C(0), A(10, (Local, 0), (GlobalFlag, 3200)), A(0)), ScrClauseSide.OnTrue, 10, 3200),
            (L(C(0), A(43, (Counter, -1)), A(0)), ScrClauseSide.OnTrue, 43, -1),
        })
        {
            var r = AssertRefusal(Program(clause), 0, side, ScrRefusalCode.IndexOutOfRange, type);
            Assert.AreEqual(value, r.Value);
        }
        // A computed flag index is checked when it runs, not here.
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(3, (Local, 0)), A(8, (Counter, 1)), A(101, (Local, 9)))), S1).IsAdmitted);
        var withVariables = new ScrAdmissionSet("gv", [0], [10], [ScrValueKind.Number, ScrValueKind.GlobalVariable, ScrValueKind.LocalVariable], []);
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(0), A(10, (GlobalVariable, 1999), (Number, 1)), A(0))), withVariables).IsAdmitted);
        AssertRefusal(Program(L(C(0), A(10, (GlobalVariable, 2000), (Number, 1)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.IndexOutOfRange, 10, withVariables);
    }

    [TestMethod]
    public void Writes_into_a_number_literal_are_refused()
    {
        foreach (var type in new[] { 10, 11, 12, 13, 14, 42 })
        {
            var r = AssertRefusal(Program(L(C(0), A(type, (Number, 1), (Number, 1), (Number, 1)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.WriteToNumber, type);
            Assert.AreEqual(0, r.Slot); Assert.AreEqual(Number, r.Kind);
        }
        // Set story state reads its operand, so a literal is fine.
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(0), A(43, (Number, 15)), A(0))), S1).IsAdmitted);
    }

    [TestMethod]
    public void Gotos_stay_inside_the_script_or_go_to_its_end()
    {
        Assert.IsTrue(ScrAdmission.Admit(Program(L(C(0), A(3, (Number, 0)), A(0)), L(C(0), A(3, (Number, 2)), A(0))), S1).IsAdmitted);
        var r = AssertRefusal(Program(L(C(0), A(3, (Number, 2)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.GotoBeyondEnd, 3);
        Assert.AreEqual(2, r.Value);
        foreach (var code in new[] { -1, -2, -3, -4, int.MinValue })
            Assert.AreEqual(code, AssertRefusal(Program(L(C(3, (Number, 0)), A(1), A(3, (Number, code)))), 0, ScrClauseSide.OnFalse, ScrRefusalCode.NegativeGoto, 3).Value);
        var computed = AssertRefusal(Program(L(C(0), A(3, (Local, 0)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ComputedGoto, 3);
        Assert.AreEqual(0, computed.Slot); Assert.AreEqual(Local, computed.Kind);
        AssertRefusal(Program(L(C(0), A(3, (Counter, 0)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ComputedGoto, 3);
    }

    [TestMethod]
    public void The_first_refusal_in_line_and_clause_order_is_reported()
    {
        var program = Program(
            L(C(0), A(1), A(0)),
            L(C(18, (21, 0), (1, 0)), A(44), A(81)),
            L(C(99), A(1), A(1)));
        AssertRefusal(program, 1, ScrClauseSide.Condition, ScrRefusalCode.ConditionNotAdmitted, 18);
        AssertRefusal(Program(L(C(3, (Number, 1)), A(44), A(81))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.ActionNotAdmitted, 44);
        var r = AssertRefusal(Program(L(C(4, (GlobalVariable, 0), (PcFlag, 0)), A(1), A(1))), 0, ScrClauseSide.Condition, ScrRefusalCode.ValueKindNotAdmitted, 4);
        Assert.AreEqual(0, r.Slot);
        // Slot 0's write rule comes before slot 1's kind.
        r = AssertRefusal(Program(L(C(0), A(10, (Number, 1), (7, 1)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.WriteToNumber, 10);
        Assert.AreEqual(0, r.Slot);
        r = AssertRefusal(Program(L(C(0), A(10, (Local, 1), (7, 1)), A(0))), 0, ScrClauseSide.OnTrue, ScrRefusalCode.UnknownValueKind, 10);
        Assert.AreEqual(1, r.Slot);
    }

    [TestMethod]
    public void Admission_sets_accept_only_catalogue_types_and_are_immutable()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScrAdmissionSet("x", [53], [], [], []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScrAdmissionSet("x", [], [142], [], []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScrAdmissionSet("x", [], [], [(ScrValueKind)7], []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScrAdmissionSet("x", [], [], [], [(ScrFocusKind)24]));
        Assert.ThrowsExactly<ArgumentException>(() => new ScrAdmissionSet(" ", [], [], [], []));
        var conditions = new List<int> { 0 };
        var set = new ScrAdmissionSet("x", conditions, [1], [], []);
        conditions.Add(3);
        Assert.IsFalse(set.Conditions.Contains(3));
        Assert.AreEqual("S1", S1.Id);
        CollectionAssert.AreEquivalent(new[] { 0, 3, 4, 5, 43 }, S1.Conditions.ToArray());
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 8, 9, 10, 11, 12, 13, 14, 42, 43, 100, 101 }, S1.Actions.ToArray());
        CollectionAssert.AreEquivalent(new[] { ScrValueKind.Counter, ScrValueKind.LocalVariable, ScrValueKind.Number, ScrValueKind.GlobalFlag }, S1.ValueKinds.ToArray());
        Assert.AreEqual(0, S1.FocusKinds.Count);
        Assert.IsFalse(S1.Conditions is ISet<int> { IsReadOnly: false });
    }

    [TestMethod]
    public void Only_admission_creates_an_admitted_program_or_a_result()
    {
        foreach (var type in new[] { typeof(ScrAdmittedProgram), typeof(ScrAdmissionResult) })
        {
            Assert.AreEqual(0, type.GetConstructors().Length, type.Name);
            Assert.IsTrue(type.GetProperties().All(p => p.SetMethod is null || !p.SetMethod.IsPublic), type.Name);
        }
        var admitted = ScrAdmission.Admit(Program(L(C(0), A(1), A(0))), S1);
        Assert.IsNotNull(admitted.Admitted); Assert.IsNull(admitted.Refusal);
        var refused = ScrAdmission.Admit(Program(L(C(0), A(44), A(0))), S1);
        Assert.IsNull(refused.Admitted); Assert.IsNotNull(refused.Refusal);
    }

    private static ScrRefusal AssertRefusal(ScrProgram program, int line, ScrClauseSide side, ScrRefusalCode code, int type, ScrAdmissionSet? set = null)
    {
        var result = ScrAdmission.Admit(program, set ?? S1);
        Assert.IsFalse(result.IsAdmitted);
        Assert.IsNull(result.Admitted);
        var r = result.Refusal!;
        Assert.AreEqual((line, side, code, type), (r.Line, r.Side, r.Code, r.Type), r.ToString());
        return r;
    }

    private readonly record struct Clause(int Type, (byte Kind, int Value)[] Operands);
    private static Clause C(int type, params (byte, int)[] operands) => new(type, operands);
    private static Clause A(int type, params (byte, int)[] operands) => new(type, operands);
    private static (Clause, Clause, Clause) L(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);

    private static ScrProgram Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        var read = ScrProgram.Read(bytes);
        Assert.AreEqual(ScrReadStatus.Read, read.Status);
        return read.Program!;
    }
}
