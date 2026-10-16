using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Scripts.ScrFixture;

namespace Arcanum.Data.Tests.Scripts;

[TestClass]
public sealed class ScrProgramTests
{
    [TestMethod]
    public void Reads_the_header_and_every_clause_with_all_eight_operands()
    {
        var bytes = Script(0x80000001u, 0x04030201u, ScrCatalogue.FileFlag.AutoRemoving | ScrCatalogue.FileFlag.TeleportTrigger, 7,
            (Clause.Of(3, N(5)), Clause.Of(3, N(1)), Clause.Of(1)),
            (Clause.Of(0), Clause.Of(10, (2, 9), (0, 3), (4, 3199), (1, -7), (6, 1), (5, 2), (21, 3), (255, int.MinValue)), Clause.Of(77, (9, 9))));
        var result = ScrProgram.Read(bytes);
        Assert.AreEqual(ScrReadStatus.Read, result.Status);
        var program = result.Program!;
        Assert.AreEqual(0x80000001u, program.InitialLocalFlags);
        Assert.AreEqual(0x04030201u, program.InitialCounters);
        Assert.AreEqual(0x104u, program.FileFlags);
        Assert.AreEqual(7, program.Capacity);
        Assert.AreEqual(2, program.Count);
        var first = program.Lines[0];
        Assert.AreEqual(0, first.Number);
        Assert.AreEqual(3, first.Condition.Type);
        Assert.AreEqual(3, first.Condition.Kind(0));
        Assert.AreEqual(5, first.Condition.Value(0));
        Assert.AreEqual(1, first.OnFalse.Type);
        var second = program.Lines[1];
        Assert.AreEqual(1, second.Number);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 4, 1, 6, 5, 21, 255 }, second.OnTrue.Kinds.ToArray());
        CollectionAssert.AreEqual(new[] { 9, 3, 3199, -7, 1, 2, 3, int.MinValue }, second.OnTrue.Values.ToArray());
        // Bytes a type never reads are kept as stored.
        Assert.AreEqual(77, second.OnFalse.Type);
        Assert.AreEqual(9, second.OnFalse.Kind(0));
    }

    [TestMethod]
    public void An_empty_script_is_a_valid_program()
    {
        var result = ScrProgram.Read(Script());
        Assert.AreEqual(ScrReadStatus.Read, result.Status);
        Assert.AreEqual(0, result.Program!.Count);
    }

    [TestMethod]
    public void Framing_is_strict_and_typed()
    {
        var valid = Script((Clause.Of(0), Clause.Of(1), Clause.Of(0)));
        Assert.AreEqual(ScrReadStatus.Truncated, ScrProgram.Read(valid.AsSpan(0, 63)).Status);
        Assert.AreEqual(ScrReadStatus.LengthMismatch, ScrProgram.Read(valid.AsSpan(0, valid.Length - 1)).Status);
        Assert.AreEqual(ScrReadStatus.LengthMismatch, ScrProgram.Read([.. valid, 0]).Status);
        Assert.AreEqual(ScrReadStatus.NegativeCount, ScrProgram.Read(With(valid, 52, -1)).Status);
        Assert.AreEqual(ScrReadStatus.CapacityBelowCount, ScrProgram.Read(With(valid, 56, 0)).Status);
        Assert.AreEqual(ScrReadStatus.LengthMismatch, ScrProgram.Read(With(valid, 52, 2, 56, 2)).Status);
        Assert.AreEqual(ScrReadStatus.LengthMismatch, ScrProgram.Read(With(valid, 52, int.MaxValue, 56, int.MaxValue)).Status);
        Assert.AreEqual(ScrReadStatus.TooLarge, ScrProgram.Read(valid, new() { MaxBytes = valid.Length - 1 }).Status);
        Assert.AreEqual(ScrReadStatus.Read, ScrProgram.Read(valid, new() { MaxBytes = valid.Length, MaxLines = 1 }).Status);
        var two = Script((Clause.Of(0), Clause.Of(0), Clause.Of(0)), (Clause.Of(0), Clause.Of(1), Clause.Of(0)));
        Assert.AreEqual(ScrReadStatus.TooManyLines, ScrProgram.Read(two, new() { MaxLines = 1 }).Status);
        // A malformed header is reported before the line limit.
        Assert.AreEqual(ScrReadStatus.CapacityBelowCount, ScrProgram.Read(With(two, 56, 1), new() { MaxLines = 1 }).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScrProgram.Read(valid, new() { MaxLines = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScrProgram.Read(valid, new() { MaxBytes = 63 }));
    }

    [TestMethod]
    public void Program_does_not_alias_the_payload()
    {
        var bytes = Script((Clause.Of(3, N(4)), Clause.Of(8, N(4)), Clause.Of(9, N(4))));
        var program = ScrProgram.Read(bytes).Program!;
        bytes.AsSpan().Fill(0xEE);
        Assert.AreEqual(3, program.Lines[0].Condition.Type);
        Assert.AreEqual(4, program.Lines[0].OnTrue.Value(0));
        Assert.AreEqual(ScrProgram.Read(Script((Clause.Of(3, N(4)), Clause.Of(8, N(4)), Clause.Of(9, N(4))))).Program!.Lines[0].OnTrue, program.Lines[0].OnTrue);
        Assert.IsFalse(program.Lines is ScrLine[]);
        Assert.IsFalse(program.Lines[0].OnTrue.Kinds is byte[]);
    }

    [TestMethod]
    public void Catalogue_has_the_documented_types_and_signatures()
    {
        Assert.AreEqual(7, Enum.GetValues<ScrValueKind>().Length);
        Assert.AreEqual(24, Enum.GetValues<ScrFocusKind>().Length);
        for (var t = 0; t < 53; t++) Assert.IsNotNull(ScrCatalogue.Signature(ScrClauseSide.Condition, t));
        for (var t = 0; t < 142; t++) Assert.IsNotNull(ScrCatalogue.Signature(ScrClauseSide.OnTrue, t));
        Assert.IsNull(ScrCatalogue.Signature(ScrClauseSide.Condition, 53));
        Assert.IsNull(ScrCatalogue.Signature(ScrClauseSide.OnFalse, 142));
        Assert.IsNull(ScrCatalogue.Signature(ScrClauseSide.OnTrue, -1));
        static string Text(ScrClauseSide side, int type) => string.Concat(ScrCatalogue.Signature(side, type)!.Select(r => r switch { ScrOperandRole.Number => 'n', ScrOperandRole.Object => 'o', _ => '.' }));
        Assert.AreEqual("........", Text(ScrClauseSide.Condition, ScrCatalogue.Condition.True));
        Assert.AreEqual("n.......", Text(ScrClauseSide.Condition, ScrCatalogue.Condition.LocalFlag));
        Assert.AreEqual("nn......", Text(ScrClauseSide.Condition, ScrCatalogue.Condition.Equal));
        Assert.AreEqual("oo......", Text(ScrClauseSide.Condition, ScrCatalogue.Condition.FollowingPc));
        Assert.AreEqual("onnn....", Text(ScrClauseSide.Condition, 34));
        Assert.AreEqual("nnoo....", Text(ScrClauseSide.OnTrue, ScrCatalogue.Action.CallScript));
        Assert.AreEqual("onnn....", Text(ScrClauseSide.OnTrue, ScrCatalogue.Action.Teleport));
        Assert.AreEqual("onno....", Text(ScrClauseSide.OnTrue, ScrCatalogue.Action.CallScriptEx));
        Assert.AreEqual("nnnonnn.", Text(ScrClauseSide.OnTrue, ScrCatalogue.Action.FadeAndTeleport));
        Assert.AreEqual("nnn.....", Text(ScrClauseSide.OnFalse, ScrCatalogue.Action.Add));
        Assert.AreEqual("onnn....", Text(ScrClauseSide.OnTrue, 141));
        // Every signature fits the eight operand slots.
        Assert.IsTrue(Enumerable.Range(0, 142).All(t => ScrCatalogue.Signature(ScrClauseSide.OnTrue, t)!.Count == 8));
    }

    [TestMethod]
    public void Every_signature_matches_the_reviewed_table()
    {
        // Golden table, cross-checked against how the pinned CE interpreter reads each operand
        // (ARC-RES-045 and the ARC-SCRIPT-007 review; action 86 keeps the editor signature, X9).
        const string conditions = "- - on n nn nn onn nn on on oo o o o o o o on oo on on on o o oo o o o oo oo o o onn on onnn on o o o o on n o n oon nn n o on on - o o";
        const string actions = "- - - n n - n nnoo n n nn nnn nnn nnn nnn oo onn nn o - - oo o no nn no no no no oon oon oon on non on on oo nnn on on n noo n n onnn onn onn non ono no no no no on nnoon nnoon o o o on onn no on on no no no o o onn on oon on on onn on on onn on ooonn nooon onno n no on nn o nn o on n noon on o on onn n n n onn n n nnnonnn nnnn no n nn onn onn o n on on n no oo no o ono on no ono ono n no no no o on o no n - on on no no non onn on onnn onnn";
        static string Text(ScrClauseSide side, int type)
        {
            var text = string.Concat(ScrCatalogue.Signature(side, type)!.Select(r => r switch { ScrOperandRole.Number => "n", ScrOperandRole.Object => "o", _ => "" }));
            return text.Length == 0 ? "-" : text;
        }
        CollectionAssert.AreEqual(conditions.Split(' '), Enumerable.Range(0, 53).Select(t => Text(ScrClauseSide.Condition, t)).ToArray());
        CollectionAssert.AreEqual(actions.Split(' '), Enumerable.Range(0, 142).Select(t => Text(ScrClauseSide.OnTrue, t)).ToArray());
        // Roles fill from slot 0 with no gap.
        Assert.IsTrue(Enumerable.Range(0, 142).All(t => ScrCatalogue.Signature(ScrClauseSide.OnTrue, t)!.SkipWhile(r => r != ScrOperandRole.Unused).All(r => r == ScrOperandRole.Unused)));
        Assert.AreEqual(0, typeof(ScrReadResult).GetConstructors().Length);
    }

    private static byte[] With(byte[] bytes, params int[] offsetValues)
    {
        var copy = bytes.ToArray();
        for (var i = 0; i < offsetValues.Length; i += 2) BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offsetValues[i]), offsetValues[i + 1]);
        return copy;
    }
}
