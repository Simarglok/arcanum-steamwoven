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
            // The original raises a 0 ms count to 1 before the × 8 (sub_45A950): 0 seconds is 8 ms.
            (0, 8L, 8L),
            (536_870, 4_294_960_000L, 4_294_960_000L),
            (536_871, 4_294_968_000L, 704L),
            (-1, 8L, 4_294_959_296L),
            (int.MaxValue, 17_179_869_176_000L, 4_294_959_296L),
            // 536,870,912 × 1000 is a multiple of 2^32: the 32-bit count is 0, raised to 1, so 8 ms in the original.
            (536_870_912, 4_294_967_296_000L, 8L),
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
    public void A_conversation_started_by_a_call_stores_the_runs_flags_in_the_attachees_use_slot()
    {
        var host = new FakeHost();
        // Header flags 0; the call sets local flag 3 and starts DLG node 5.
        host.Scripts[1699] = ScrScriptLookup.Of(Program(Do(C(8, N(3))), Do(C(4, N(5)))));
        host.Slots[(Npc, ScrPoint.Use)] = new(4242, 0, 0x11);
        var r = ScrDispatcher.RunCall(new(1699, 0, Pc, Npc), host);
        Assert.AreEqual(ScrStop.Dialog, r.Stop);
        Assert.AreEqual(new ScrDialogStart(Pc, Npc, 1699, 1, 5) { Point = ScrPoint.Use }, r.Effects.Dialog);
        // The use slot keeps its number and takes the run's flags and counters (the header's counter 0 replaces 0x11).
        Assert.AreEqual(new ScrWriteback(ScrSource.Object, Npc, ScrPoint.Use, new(4242, 8, 0)), r.Effects.Writeback);
        // No slot yet: one is created with number 0; no PC: no conversation, no write; no change: no write.
        host.Slots.Clear();
        Assert.AreEqual(new ScrSlot(0, 8, 0), ScrDispatcher.RunCall(new(1699, 0, Pc, Npc), host).Effects.Writeback!.Slot);
        Assert.IsNull(ScrDispatcher.RunCall(new(1699, 0, Npc, Npc), host).Effects.Writeback);
        host.Slots[(Npc, ScrPoint.Use)] = new(4242, 8, 0x22);
        Assert.AreEqual(new ScrSlot(4242, 8, 0), ScrDispatcher.RunCall(new(1699, 0, Pc, Npc), host).Effects.Writeback!.Slot, "counters alone differ");
        host.Slots[(Npc, ScrPoint.Use)] = new(4242, 8, 0);
        Assert.IsNull(ScrDispatcher.RunCall(new(1699, 0, Pc, Npc), host).Effects.Writeback);
        Assert.IsNull(ScrDispatcher.RunCall(new(1699, 0, Pc, null), host).Effects.Writeback, "no attachee, no slot");
    }

    [TestMethod]
    public void The_S5_set_takes_call_at_beside_call_in()
    {
        Assert.IsTrue(ScrAdmissionSet.S3.IsSubsetOf(ScrAdmissionSet.S5));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S3.Actions.Append(54).Append(55).Append(125).ToArray(), ScrAdmissionSet.S5.Actions.ToArray());
        Assert.AreSame(ScrAdmissionSet.S5, new ScrRunOptions().Admission);
        Assert.IsTrue(ScrRuleProfile.Original.CallDelayWraps);
        Assert.IsFalse(ScrRuleProfile.Steamwoven.CallDelayWraps);
    }

    // ARC-SCRIPT-2824 (ledger TQ17, TE2): call script at schedules the call for the operand's time of day, counted from the start hour (15 for the installed startup), in game seconds
    // (no factor 8); the roles and the line are call in's.
    private static ScrGameTime At(int hour, int minute, int second = 0, int startHour = 15) =>
        new(0, hour, minute, ((hour * 60L + minute) * 60 + second) * 1000, startHour);

    private static long CallAt(int operand, ScrGameTime now, ScrRunOptions? options = null)
    {
        var host = With(Program(Do(C(55, N(5), N(0), (Triggerer, 0), (Attachee, 0), N(operand))), Do(C(1))));
        host.Clock = now;
        return Run(host, options).Effects.Calls.Single().DelayMs;
    }

    [TestMethod]
    public void Call_at_waits_for_the_operands_time_of_day_counted_from_the_start_hour()
    {
        // At the start hour the clock reads 0: operand 3600 is an hour away, with no factor 8.
        Assert.AreEqual(3_600_000L, CallAt(3600, At(15, 0)));
        // 15:30 reads 1800: operand 3600 is 30 minutes away; operand 600 (already past) is the next day's, 23 h 40 min away.
        Assert.AreEqual(1_800_000L, CallAt(3600, At(15, 30)));
        Assert.AreEqual((86_400 - 1_800 + 600) * 1000L, CallAt(600, At(15, 30)));
        // Midnight reads 9 hours (32,400 s) into the cycle of a 15:00 start: operand 43200 is 3 h 0 min away.
        Assert.AreEqual((43_200 - 32_400) * 1000L, CallAt(43_200, At(0, 0)));
        // 14:59:59 reads 86399: operand 0 is one second away; operand 86399 (the same second) fires in 1 ms, not tomorrow.
        Assert.AreEqual(1_000L, CallAt(0, At(14, 59, 59)));
        Assert.AreEqual(1L, CallAt(86_399, At(14, 59, 59)));
        // A target equal to now is 0 seconds, raised to 1 ms (sub_45A950).
        Assert.AreEqual(1L, CallAt(0, At(15, 0)));
        Assert.AreEqual(1L, CallAt(7_200, At(17, 0)));
        // The whole seconds count: 15:00:00.9 still reads 0 (the clock's milliseconds are divided by 1000).
        Assert.AreEqual(1L, CallAt(0, new ScrGameTime(0, 15, 0, 54_000_900, 15)));
        // The day index changes nothing, and a start hour of 0 counts from midnight.
        Assert.AreEqual(3_600_000L, CallAt(3600, new ScrGameTime(40, 15, 0, 54_000_000, 15)));
        Assert.AreEqual(3_600_000L, CallAt(7200, At(1, 0, 0, 0)));
        // An operand over a day is reduced to the day (the C remainder): 90000 is 3600.
        Assert.AreEqual(3_600_000L, CallAt(90_000, At(15, 0)));
    }

    [TestMethod]
    public void Call_at_with_a_negative_operand_follows_the_rule_mode()
    {
        // Steamwoven reduces the operand to a day first: -3600 is 82800, so at the start hour the call is 23 hours away.
        Assert.AreEqual(82_800_000L, CallAt(-3600, At(15, 0)));
        // Original keeps the signed remainder: -3600 - 0 is negative, one day is added once (82800 s), the same here; a remainder that stays negative after the day is read as the
        // unsigned 32-bit millisecond count (a huge delay).
        Assert.AreEqual(82_800_000L, CallAt(-3600, At(15, 0), Original));
        Assert.AreEqual((long)unchecked((uint)((-86_399 - 86_399 + 86_400) * 1000)), CallAt(-86_399, At(14, 59, 59), Original));
        Assert.AreEqual(1_000L, CallAt(-86_399, At(15, 0)), "-86399 reduces to 1");
    }

    [TestMethod]
    public void Call_at_needs_a_clock_and_gives_the_same_roles_and_line_as_call_in()
    {
        var host = With(Program(Do(C(55, N(2023), N(4), (Triggerer, 0), (Attachee, 0), N(3600))), Do(C(55, N(7), N(0), (Extra, 0), (Player, 0), N(3600))), Do(C(1))));
        // A host with no clock ends the dispatch with nothing applied (as the clock actions do).
        var none = Run(host);
        Assert.AreEqual(ScrStop.NotModelled, none.Stop);
        Assert.AreEqual(0, none.Effects.Calls.Count);
        host.Clock = At(15, 0);
        CollectionAssert.AreEqual(new[]
        {
            new ScrCallRequest(ScrCallKind.Schedule, 2023, 4, Pc, Npc, 3_600_000),
            new ScrCallRequest(ScrCallKind.Schedule, 7, 0, null, Pc, 3_600_000),
        }, Run(host).Effects.Calls.ToArray());
    }

    [TestMethod]
    public void The_time_of_day_without_the_clocks_own_milliseconds_reads_the_hour_and_minute()
    {
        // A host that gives the hour and minute only (the story clock tests') reads the minute's start.
        Assert.AreEqual(54_000, new ScrGameTime(0, 15, 0, -1, 0).SecondsSinceStartHour());
        Assert.AreEqual(0, new ScrGameTime(0, 15, 0, -1, 15).SecondsSinceStartHour());
        Assert.AreEqual(43_200 + 60, new ScrGameTime(3, 3, 1, -1, 15).SecondsSinceStartHour());
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
