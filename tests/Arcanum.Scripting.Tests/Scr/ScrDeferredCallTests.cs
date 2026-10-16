using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Call script in, remove script call and the firing of a deferred call (ARC-SCRIPT-010).</summary>
[TestClass]
public sealed class ScrDeferredCallTests
{
    private const byte Triggerer = 0, Attachee = 1, Player = 21, Extra = 12;
    private static readonly ScrRunOptions Original = new() { Rules = ScrRuleProfile.Original };

    private static FakeHost With(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Npc, 19, 100, program);
        return host;
    }

    private static ScrRunResult Run(FakeHost host, ScrRunOptions? options = null) => ScrDispatcher.RunObject(new(19, Npc, Pc, null), host, options);

    [TestMethod]
    public void Call_in_asks_for_a_call_with_its_roles_and_game_delay()
    {
        var host = With(Program(Do(C(54, N(2023), N(4), (Triggerer, 0), (Attachee, 0), N(10))), Do(C(54, N(7), N(0), (Extra, 0), (Player, 0), N(1))), Do(C(1))));
        var r = Run(host);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, r.Stop);
        CollectionAssert.AreEqual(new[]
        {
            new ScrCallRequest(ScrCallKind.Schedule, 2023, 4, Pc, Npc, 80_000),
            // An absent role is stored as absent (the original stores a null handle).
            new ScrCallRequest(ScrCallKind.Schedule, 7, 0, null, Pc, 8_000),
        }, r.Effects.Calls.ToArray());
        Assert.IsNull(r.Effects.Writeback);
    }

    [TestMethod]
    public void The_delay_wraps_only_in_the_original_mode()
    {
        foreach (var (seconds, steamwoven, original) in new[]
        {
            (10, 80_000L, 80_000L),
            (0, 1L, 1L),
            (536_870, 4_294_960_000L, 4_294_960_000L),
            (536_871, 4_294_968_000L, 704L),
            (-1, 1L, 4_294_959_296L),
            (int.MaxValue, int.MaxValue * 8000L, unchecked((uint)(int.MaxValue * 1000) * 8u)),
        })
        {
            var program = Program(Do(C(54, N(5), N(0), (Triggerer, 0), (Attachee, 0), N(seconds))), Do(C(1)));
            Assert.AreEqual(steamwoven, Run(With(program)).Effects.Calls.Single().DelayMs, $"steamwoven {seconds}");
            Assert.AreEqual(original, Run(With(program), Original).Effects.Calls.Single().DelayMs, $"original {seconds}");
        }
    }

    [TestMethod]
    public void Remove_script_call_names_the_script_and_the_attachee_in_action_order()
    {
        var host = With(Program(Do(C(54, N(5), N(0), (Triggerer, 0), (Attachee, 0), N(1))), Do(C(125, N(5), (Attachee, 0))), Do(C(125, N(6), (Extra, 0))), Do(C(1))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrCallRequest(ScrCallKind.Schedule, 5, 0, Pc, Npc, 8_000),
            new ScrCallRequest(ScrCallKind.Remove, 5, 0, null, Npc, 0),
            new ScrCallRequest(ScrCallKind.Remove, 6, 0, null, null, 0),
        }, Run(host).Effects.Calls.ToArray());
    }

    [TestMethod]
    public void A_firing_call_runs_from_the_file_header_at_its_line_and_writes_nothing_back()
    {
        var host = new FakeHost();
        // Header: local flag 2 set, counter 0 = 7. Line 1: local flag 2 → set flag 9, counter 0 == 7 → run default.
        host.Scripts[2023] = ScrScriptLookup.Of(Program(0, ProgramLines()));
        host.Scripts[2023] = ScrScriptLookup.Of(WithHeader(host.Scripts[2023].Program!, 4, 7));
        var r = ScrDispatcher.RunCall(new(2023, 1, Pc, Npc), host);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.ReturnRunDefault, 2023), (r.Outcome, r.Stop, r.Script));
        Assert.AreEqual(9, r.Effects.GlobalFlags.Single().Index);
        Assert.IsNull(r.Effects.Writeback, "a call has no attachment to write back");
        // Missing, trap and generator cases end as for an object event; a bad line ends on an unreadable line.
        Assert.AreEqual(ScrStop.MissingScript, ScrDispatcher.RunCall(new(9999, 0, Pc, Npc), host).Stop);
        Assert.AreEqual(ScrStop.LineOutOfRange, ScrDispatcher.RunCall(new(2023, 40, Pc, Npc), host).Stop);
        Assert.AreEqual(ScrStop.InvalidEvent, ScrDispatcher.RunCall(new(2023, -1, Pc, Npc), host).Stop);
        Assert.AreEqual(ScrStop.NoScript, ScrDispatcher.RunCall(new(0, 0, Pc, Npc), host).Stop);
        host.Scripts[30002] = ScrScriptLookup.Of(Program(ScrCatalogue.FileFlag.NonmagicalTrap, Do(C(2))));
        Assert.AreEqual(ScrStop.TrapScript, ScrDispatcher.RunCall(new(30002, 0, Pc, Npc), host).Stop);
        host.Generator = true;
        Assert.AreEqual(ScrStop.MonsterGenerator, ScrDispatcher.RunCall(new(2023, 1, Pc, Npc), host).Stop);
        Assert.AreEqual(ScrStop.ReturnRunDefault, ScrDispatcher.RunCall(new(2023, 1, Pc, null), host).Stop, "no attachee, no generator check");
        // An absent role in the call stops the run only where an operand reads it.
        host.Generator = false;
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunCall(new(2023, 3, null, Npc), host).Stop);
    }

    [TestMethod]
    public void The_S5_set_and_call_at_staying_outside()
    {
        Assert.IsTrue(ScrAdmissionSet.S3.IsSubsetOf(ScrAdmissionSet.S5));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S3.Actions.Append(54).Append(125).ToArray(), ScrAdmissionSet.S5.Actions.ToArray());
        Assert.AreSame(ScrAdmissionSet.S5, new ScrRunOptions().Admission);
        var at = Run(With(Program(Do(C(55, N(5), N(0), (Triggerer, 0), (Attachee, 0), N(3600))), Do(C(1)))));
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, 55), (at.Outcome, at.Refusal!.Code, at.Refusal.Type));
        Assert.IsTrue(ScrRuleProfile.Original.CallDelayWraps);
        Assert.IsFalse(ScrRuleProfile.Steamwoven.CallDelayWraps);
    }

    private static (Clause, Clause, Clause)[] ProgramLines() =>
    [
        Do(C(1)),
        L(C(3, N(2)), C(100, N(9)), C(1)),
        L(C(4, (0, 0), N(7)), C(2), C(1)),
        L(C(39, (Triggerer, 0)), C(2), C(1)),
    ];

    private static ScrProgram WithHeader(ScrProgram program, uint flags, uint counters)
    {
        // Rebuild the bytes with a header: the reader is the only way to make a program.
        var bytes = new byte[64 + program.Count * 132];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), counters);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), program.Count);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), program.Count);
        for (var line = 0; line < program.Count; line++)
            foreach (var (offset, clause) in new[] { (0, program.Lines[line].Condition), (44, program.Lines[line].OnTrue), (88, program.Lines[line].OnFalse) })
            {
                var at = 64 + line * 132 + offset;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), clause.Type);
                for (var i = 0; i < 8; i++) { bytes[at + 4 + i] = clause.Kind(i); System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 4 * i), clause.Value(i)); }
            }
        return ScrProgram.Read(bytes).Program!;
    }
}
