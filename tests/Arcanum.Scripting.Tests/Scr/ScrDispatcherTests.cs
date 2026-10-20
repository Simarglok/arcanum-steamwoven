using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The dispatcher core against the rows of docs/research/scr-event-dispatcher.md section 1 (E1–E11) and the
/// script anomalies of the verification ledger (X3, X7, X8, X10).</summary>
[TestClass]
public sealed class ScrDispatcherTests
{
    private const int Use = ScrPoint.Use, Dialog = ScrPoint.Dialog, Heartbeat = 19;
    private static readonly ScrRunOptions Original = new() { Rules = ScrRuleProfile.Original };

    private static ScrRunResult RunNpc(FakeHost host, int point = Heartbeat, int line = 0, ScrRunOptions? options = null) =>
        ScrDispatcher.RunObject(new(point, Npc, Pc, null, line), host, options);

    private static FakeHost With(ScrProgram program, int point = Heartbeat, uint localFlags = 0, uint counters = 0, int script = 100)
    {
        var host = new FakeHost();
        host.Attach(Npc, point, script, program, localFlags, counters);
        return host;
    }

    [TestMethod]
    public void No_slot_or_script_zero_runs_default_without_reading_a_script()
    {
        var host = new FakeHost();
        var r = RunNpc(host);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.NoScript), (r.Outcome, r.Stop));
        Assert.IsTrue(r.Effects.IsEmpty);
        Assert.IsNull(r.Script);
        host.Slots[(Npc, Heartbeat)] = new(0, 5, 5);
        r = RunNpc(host);
        Assert.AreEqual(ScrStop.NoScript, r.Stop);
        Assert.IsFalse(host.Calls.Any(c => c.StartsWith("script", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void A_use_event_gives_the_triggerer_a_trap_spot_first()
    {
        var host = With(Program(Do(C(2))), Use);
        host.Trap = ScrTrapCheck.Spotted;
        var r = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.TrapSpotted), (r.Outcome, r.Stop));
        Assert.IsFalse(host.Calls.Contains("script 100"));
        host.Trap = ScrTrapCheck.NotSpotted;
        Assert.AreEqual(ScrOutcome.RunDefault, ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host).Outcome);
        // No triggerer, no spot; other points never ask.
        host.Calls.Clear(); host.Trap = ScrTrapCheck.Spotted;
        Assert.AreEqual(ScrOutcome.RunDefault, ScrDispatcher.RunObject(new(Use, Npc, null, null), host).Outcome);
        Assert.IsFalse(host.Calls.Contains("trap"));
        var heartbeat = With(Program(Do(C(2)))); heartbeat.Trap = ScrTrapCheck.Spotted;
        Assert.AreEqual(ScrOutcome.RunDefault, RunNpc(heartbeat).Outcome);
        Assert.IsFalse(heartbeat.Calls.Contains("trap"));
    }

    [TestMethod]
    public void Missing_unreadable_and_trap_scripts_skip_default_before_running()
    {
        var host = new FakeHost();
        host.Slots[(Npc, Heartbeat)] = new(100, 0, 0);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.MissingScript), (RunNpc(host).Outcome, RunNpc(host).Stop));
        host.Scripts[100] = ScrScriptLookup.Unreadable;
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.UnreadableScript), (RunNpc(host).Outcome, RunNpc(host).Stop));
        foreach (var (trap, number) in new[] { (ScrCatalogue.FileFlag.NonmagicalTrap, 30000), (ScrCatalogue.FileFlag.MagicalTrap, 30007) })
        {
            var trapped = With(Program(trap, Do(C(100, N(5)))), script: number);
            var r = RunNpc(trapped);
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.TrapScript), (r.Outcome, r.Stop));
            Assert.IsTrue(r.Effects.IsEmpty);
        }
        // A trap is both: the number range and a trap file flag (original 0x4BCB70); either alone is an ordinary script.
        foreach (var (flags, number) in new[] { (ScrCatalogue.FileFlag.MagicalTrap, 100), (ScrCatalogue.FileFlag.MagicalTrap, 30008), (0u, 30000) })
            Assert.AreEqual(ScrStop.ReturnSkipDefault, RunNpc(With(Program(flags, Do(C(100, N(5))), Do(C(1))), script: number)).Stop, $"{flags} {number}");
    }

    [TestMethod]
    public void A_monster_generator_runs_default_without_running()
    {
        var host = With(Program(Do(C(100, N(5)))));
        host.Generator = true;
        var r = RunNpc(host);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.MonsterGenerator), (r.Outcome, r.Stop));
        Assert.IsTrue(r.Effects.IsEmpty);
    }

    [TestMethod]
    public void Dialog_gates_for_dead_surrendered_and_mind_controlled_npcs()
    {
        var plain = Program(Do(C(100, N(5))), Do(C(1)));
        foreach (var (state, stop) in new[] { (ScrDialogState.Dead, ScrStop.DeadWithoutSpeech), (ScrDialogState.Surrendered, ScrStop.SurrenderedWithoutSpeech) })
        {
            var host = With(plain, Dialog); host.Dialog = state;
            var r = RunNpc(host, Dialog);
            Assert.AreEqual((ScrOutcome.RunDefault, stop), (r.Outcome, r.Stop));
            Assert.IsTrue(r.Effects.IsEmpty);
        }
        foreach (var (state, flag) in new[] { (ScrDialogState.Dead, ScrCatalogue.FileFlag.DeathSpeech), (ScrDialogState.Surrendered, ScrCatalogue.FileFlag.SurrenderSpeech) })
        {
            var host = With(Program(flag, Do(C(100, N(5))), Do(C(1))), Dialog); host.Dialog = state;
            var r = RunNpc(host, Dialog);
            Assert.AreEqual(ScrStop.ReturnSkipDefault, r.Stop);
            Assert.AreEqual(5, r.Effects.GlobalFlags.Single().Index);
        }
        // The original tests each state on its own, dead first, then surrendered, then mind control.
        var both = With(Program(ScrCatalogue.FileFlag.SurrenderSpeech, Do(C(100, N(5))), Do(C(1))), Dialog);
        both.Scripts[ScrDispatcher.MindControlScript] = ScrScriptLookup.Of(Program(Do(C(100, N(6))), Do(C(1))));
        both.Dialog = ScrDialogState.Surrendered | ScrDialogState.MindControlled;
        var swapped = RunNpc(both, Dialog);
        Assert.AreEqual((ScrDispatcher.MindControlScript, 6), (swapped.Script!.Value, swapped.Effects.GlobalFlags.Single().Index));
        both.Dialog = ScrDialogState.Dead | ScrDialogState.MindControlled;
        Assert.AreEqual(ScrStop.DeadWithoutSpeech, RunNpc(both, Dialog).Stop);
        both.Dialog = ScrDialogState.Dead | ScrDialogState.Surrendered;
        Assert.AreEqual(ScrStop.DeadWithoutSpeech, RunNpc(both, Dialog).Stop);
        var neither = With(plain, Dialog); neither.Dialog = ScrDialogState.Dead | ScrDialogState.Surrendered;
        Assert.AreEqual(ScrStop.DeadWithoutSpeech, RunNpc(neither, Dialog).Stop, "dead is tested before surrendered");
        // The gates belong to the dialog point only.
        var heartbeat = With(plain); heartbeat.Dialog = ScrDialogState.Dead;
        Assert.AreEqual(ScrStop.ReturnSkipDefault, RunNpc(heartbeat).Stop);
        Assert.IsFalse(heartbeat.Calls.Contains("dialog"));
    }

    [TestMethod]
    public void A_mind_controlled_npc_runs_2188_on_its_own_slot_and_keeps_its_number()
    {
        // Own script would set flag 5; 2188 sets local flag 1 and removes itself.
        var host = With(Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(100, N(5))), Do(C(1))), Dialog, localFlags: 0x10, counters: 7);
        host.Scripts[ScrDispatcher.MindControlScript] = ScrScriptLookup.Of(Program(Do(C(8, N(1))), Do(C(5)), Do(C(2))));
        host.Dialog = ScrDialogState.MindControlled;
        var r = RunNpc(host, Dialog);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.ReturnRunDefault, ScrDispatcher.MindControlScript), (r.Outcome, r.Stop, r.Script));
        Assert.AreEqual(0, r.Effects.GlobalFlags.Count);
        Assert.AreEqual(new ScrSlot(100, 0x12, 7), r.Effects.Writeback!.Slot);
        // A missing 2188 runs default (the original's second file check, X5).
        host.Scripts.Remove(ScrDispatcher.MindControlScript);
        var missing = RunNpc(host, Dialog);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.MissingScript), (missing.Outcome, missing.Stop));
    }

    [TestMethod]
    public void Returns_give_their_default_and_clear_an_auto_removing_script()
    {
        var skip = RunNpc(With(Program(Do(C(1)))));
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault, 0), (skip.Outcome, skip.Stop, skip.StopLine));
        Assert.IsTrue(skip.Effects.IsEmpty);
        var run = RunNpc(With(Program(Do(C(0)), Do(C(2)))));
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.ReturnRunDefault, 1), (run.Outcome, run.Stop, run.StopLine));
        foreach (var (program, stop) in new[]
        {
            (Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(1))), ScrStop.ReturnSkipDefault),
            (Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(2))), ScrStop.ReturnRunDefault),
            (Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(0))), ScrStop.FellOffEnd),
        })
        {
            var r = RunNpc(With(program, localFlags: 3));
            Assert.AreEqual(stop, r.Stop);
            Assert.AreEqual(new ScrSlot(0, 3, 0), r.Effects.Writeback!.Slot);
        }
    }

    [TestMethod]
    public void Running_past_the_last_line_skips_default()
    {
        var r = RunNpc(With(Program(Do(C(0)), Do(C(8, N(0))))));
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.FellOffEnd, 1), (r.Outcome, r.Stop, r.StopLine));
        Assert.AreEqual(new ScrSlot(100, 1, 0), r.Effects.Writeback!.Slot);
    }

    [TestMethod]
    public void An_unreadable_line_and_the_budget_stop_without_auto_removal()
    {
        var auto = ScrCatalogue.FileFlag.AutoRemoving;
        // A goto to exactly the line count, a start line outside the script and an empty script.
        foreach (var (program, line, stopLine) in new[] { (Program(auto, Do(C(3, N(1)))), 0, 1), (Program(auto, Do(C(1))), 3, 3), (Program(auto), 0, 0) })
        {
            var r = RunNpc(With(program), line: line);
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.LineOutOfRange, stopLine), (r.Outcome, r.Stop, r.StopLine));
            Assert.IsNull(r.Effects.Writeback);
        }
        var loop = RunNpc(With(Program(auto, Do(C(11, (Local, 0), (Local, 0), N(1))), Do(C(3, N(0))))));
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.Budget), (loop.Outcome, loop.Stop));
        Assert.AreEqual(ScrRunOptions.OriginalLineBudget, loop.Trace.Count);
        Assert.IsNull(loop.Effects.Writeback);
        var short_ = RunNpc(With(Program(Do(C(3, N(0))))), options: new() { MaxExecutedLines = 7 });
        Assert.AreEqual((ScrStop.Budget, 7), (short_.Stop, short_.Trace.Count));
    }

    [TestMethod]
    public void Remove_and_change_this_script_take_effect_after_the_run()
    {
        // The run goes on after the removal and still sets the flag.
        var removed = RunNpc(With(Program(Do(C(5)), Do(C(100, N(9))), Do(C(1)))));
        Assert.AreEqual(0, removed.Effects.Writeback!.Slot.Script);
        Assert.AreEqual(9, removed.Effects.GlobalFlags.Single().Index);
        var changed = RunNpc(With(Program(ScrCatalogue.FileFlag.AutoRemoving, Do(C(6, N(321))), Do(C(1)))));
        Assert.AreEqual(321, changed.Effects.Writeback!.Slot.Script, "the change wins over auto-removal");
        // Computed −1 is the original's "no change"; any other computed value is stored as the original does.
        var none = RunNpc(With(Program(Do(C(12, (Local, 0), N(0), N(1))), Do(C(6, (Local, 0))), Do(C(1)))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, none.Stop);
        Assert.IsNull(none.Effects.Writeback);
        var odd = RunNpc(With(Program(Do(C(10, (Local, 0), N(100000))), Do(C(6, (Local, 0))), Do(C(1)))));
        Assert.AreEqual((ScrStop.ReturnSkipDefault, 100000), (odd.Stop, odd.Effects.Writeback!.Slot.Script));
    }

    [TestMethod]
    public void Local_flags_and_counters_are_written_back_only_when_changed()
    {
        var program = Program(L(C(3, N(4)), C(9, N(4)), C(8, N(4))), Do(C(11, (Counter, 2), (Counter, 2), N(1))), Do(C(1)));
        var r = RunNpc(With(program, localFlags: 0, counters: 0x00050000));
        Assert.AreEqual(new ScrSlot(100, 0x10, 0x00060000), r.Effects.Writeback!.Slot);
        Assert.AreEqual(ScrSource.Object, r.Effects.Writeback.Source);
        Assert.AreEqual((Npc, Heartbeat), (r.Effects.Writeback.Attachee!.Value, r.Effects.Writeback.Point));
        // Reading only changes nothing.
        var unchanged = RunNpc(With(Program(L(C(3, N(4)), C(1), C(2))), localFlags: 0x10));
        Assert.AreEqual(ScrOutcome.SkipDefault, unchanged.Outcome);
        Assert.IsTrue(unchanged.Effects.IsEmpty);
    }

    [TestMethod]
    public void Local_variables_start_at_zero_every_run()
    {
        var host = With(Program(Do(C(11, (Local, 3), (Local, 3), N(1))), L(C(4, (Local, 3), N(1)), C(2), C(1))));
        Assert.AreEqual(ScrOutcome.RunDefault, RunNpc(host).Outcome);
        Assert.AreEqual(ScrOutcome.RunDefault, RunNpc(host).Outcome);
    }

    [TestMethod]
    public void A_trap_object_whose_script_becomes_zero_is_destroyed_instead()
    {
        var host = With(Program(Do(C(5)), Do(C(1))), Use);
        host.TrapObject = true;
        var r = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host);
        Assert.AreEqual(Npc, r.Effects.DestroyTrapObject);
        Assert.IsNull(r.Effects.Writeback);
        // Any other change is an ordinary writeback.
        var flags = With(Program(Do(C(8, N(0))), Do(C(1))), Use); flags.TrapObject = true;
        var w = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), flags);
        Assert.IsNull(w.Effects.DestroyTrapObject);
        Assert.AreEqual(1u, w.Effects.Writeback!.Slot.LocalFlags);
    }

    [TestMethod]
    public void Counter_writes_are_masked_to_their_byte_in_both_modes()
    {
        foreach (var options in new[] { new ScrRunOptions(), Original })
        {
            var r = RunNpc(With(Program(Do(C(10, (Counter, 1), N(300))), Do(C(10, (Counter, 2), N(-1))), Do(C(1))), counters: 0x11000022), options: options);
            Assert.AreEqual(0x11FF2C22u, r.Effects.Writeback!.Slot.Counters, options.Rules.Name);
        }
    }

    [TestMethod]
    public void Divide_stores_the_quotient_and_nothing_for_zero_in_both_modes()
    {
        foreach (var options in new[] { new ScrRunOptions(), Original })
        {
            var r = RunNpc(With(Program(
                Do(C(14, (Local, 0), N(-7), N(2))),
                Do(C(10, (Local, 1), N(9))), Do(C(14, (Local, 1), N(5), N(0))),
                Do(C(14, (Local, 2), N(int.MinValue), N(-1))),
                L(C(4, (Local, 0), N(-3)), C(0), C(1)), L(C(4, (Local, 1), N(9)), C(0), C(1)), L(C(4, (Local, 2), N(int.MinValue)), C(2), C(1)))), options: options);
            Assert.AreEqual(ScrOutcome.RunDefault, r.Outcome, options.Rules.Name);
        }
    }

    [TestMethod]
    public void Arithmetic_wraps_like_32_bit_integers()
    {
        var r = RunNpc(With(Program(
            Do(C(11, (Local, 0), N(int.MaxValue), N(1))), Do(C(13, (Local, 1), N(65536), N(65536))), Do(C(12, (Local, 2), N(int.MinValue), N(1))),
            L(C(4, (Local, 0), N(int.MinValue)), C(0), C(1)), L(C(4, (Local, 1), N(0)), C(0), C(1)), L(C(4, (Local, 2), N(int.MaxValue)), C(2), C(1)))));
        Assert.AreEqual(ScrOutcome.RunDefault, r.Outcome);
    }

    [TestMethod]
    public void A_run_reads_its_own_global_flag_writes_and_reports_only_changes()
    {
        var host = With(Program(Do(C(100, N(7))), L(C(43, N(7)), C(101, N(8)), C(1)), Do(C(100, N(9))), Do(C(101, N(9))), Do(C(10, (GlobalFlag, 12), (GlobalFlag, 7))), Do(C(1))));
        host.Flags.Add(8);
        var r = RunNpc(host);
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(7, true), new ScrFlagWrite(8, false), new ScrFlagWrite(12, true) }, r.Effects.GlobalFlags.ToArray());
        Assert.IsTrue(host.Flags.SetEquals([8]), "the host is never written");
    }

    [TestMethod]
    public void Global_flag_writes_spill_into_the_word_only_in_the_original_mode()
    {
        // X10: 2 into flag 33 clears 33 and sets 34; −1 into flag 64 sets 64..95.
        var program = Program(Do(C(10, (GlobalFlag, 33), N(2))), Do(C(10, (GlobalFlag, 64), N(-1))), Do(C(10, (GlobalFlag, 100), N(3))), Do(C(1)));
        var steamwoven = RunNpc(With(program)).Effects.GlobalFlags;
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(64, true), new ScrFlagWrite(100, true) }, steamwoven.ToArray());
        var host = With(program); host.Flags.Add(33);
        var original = RunNpc(host, options: Original).Effects.GlobalFlags;
        var expected = new List<ScrFlagWrite> { new(33, false), new(34, true) };
        expected.AddRange(Enumerable.Range(64, 32).Select(i => new ScrFlagWrite(i, true)));
        expected.AddRange(new[] { new ScrFlagWrite(100, true), new ScrFlagWrite(101, true) });
        CollectionAssert.AreEqual(expected.ToArray(), original.ToArray());
        // Setting and clearing a flag by its own actions never spills.
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(40, true) }, RunNpc(With(Program(Do(C(100, N(40))), Do(C(1)))), options: Original).Effects.GlobalFlags.ToArray());
    }

    [TestMethod]
    public void Story_state_only_rises()
    {
        var host = With(Program(Do(C(43, N(10))), Do(C(43, N(4))), Do(C(42, (Local, 0))), L(C(4, (Local, 0), N(10)), C(2), C(1))));
        host.Story = 6;
        var r = RunNpc(host);
        Assert.AreEqual((ScrOutcome.RunDefault, 10), (r.Outcome, r.Effects.StoryState));
        host.Story = 12;
        var lower = RunNpc(host);
        Assert.AreEqual(ScrOutcome.SkipDefault, lower.Outcome);
        Assert.IsNull(lower.Effects.StoryState);
    }

    [TestMethod]
    public void A_computed_local_flag_number_wraps_and_a_global_one_outside_its_store_stops_the_run()
    {
        // Local flags: the original's shl uses the number's low five bits (0x4473C2), so 32 is flag 0 and −1 flag 31.
        var wrapped = RunNpc(With(Program(Do(C(10, (Local, 0), N(32))), Do(C(8, (Local, 0))), Do(C(10, (Local, 0), N(-1))), Do(C(8, (Local, 0))), Do(C(1)))));
        Assert.AreEqual(0x80000001u, wrapped.Effects.Writeback!.Slot.LocalFlags);
        var tested = RunNpc(With(Program(Do(C(10, (Local, 0), N(33))), L(C(3, (Local, 0)), C(2), C(1))), localFlags: 2));
        Assert.AreEqual(ScrOutcome.RunDefault, tested.Outcome);
        foreach (var (program, line) in new[]
        {
            (Program(Do(C(100, N(1))), Do(C(10, (Local, 0), N(3200))), Do(C(100, (Local, 0))), Do(C(2))), 2),
            (Program(Do(C(10, (Local, 0), N(-1))), L(C(43, (Local, 0)), C(2), C(2))), 1),
            (Program(Do(C(10, (Local, 0), N(5000))), L(C(43, (Local, 0)), C(2), C(2))), 1),
        })
        {
            var r = RunNpc(With(program));
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.IndexOutOfRange, line), (r.Outcome, r.Stop, r.StopLine));
        }
        // What happened before the stop is kept, as the original applies effects as it goes.
        var kept = RunNpc(With(Program(Do(C(100, N(1))), Do(C(10, (Local, 0), N(3200))), Do(C(100, (Local, 0))))));
        Assert.AreEqual(1, kept.Effects.GlobalFlags.Single().Index);
    }

    [TestMethod]
    public void Tile_changes_persist_in_Steamwoven_and_are_discarded_in_Original()
    {
        var host = new FakeHost();
        host.Scripts[2498] = ScrScriptLookup.Of(Program(Do(C(100, N(2593))), Do(C(5)), Do(C(1))));
        var e = new ScrTileEvent(new(2498, 0, 0), Pc);
        var steamwoven = ScrDispatcher.RunTile(e, host);
        Assert.AreEqual(new ScrWriteback(ScrSource.Tile, null, Use, new(0, 0, 0)), steamwoven.Effects.Writeback);
        var original = ScrDispatcher.RunTile(e, host, Original);
        Assert.IsNull(original.Effects.Writeback);
        // Global effects are not part of the discarded record.
        Assert.AreEqual(2593, original.Effects.GlobalFlags.Single().Index);
        Assert.AreEqual(ScrStop.NoScript, ScrDispatcher.RunTile(new(new(0, 0, 0), Pc), host).Stop);
    }

    [TestMethod]
    public void Sector_changes_persist_in_both_modes()
    {
        var host = new FakeHost();
        host.Scripts[77] = ScrScriptLookup.Of(Program(Do(C(8, N(2))), Do(C(5)), Do(C(2))));
        foreach (var options in new[] { new ScrRunOptions(), Original })
        {
            var r = ScrDispatcher.RunSector(new(new(77, 0, 0), Pc), host, options);
            Assert.AreEqual((ScrOutcome.RunDefault, new ScrWriteback(ScrSource.Sector, null, Use, new(0, 4, 0))), (r.Outcome, r.Effects.Writeback));
        }
        host.Scripts[30001] = ScrScriptLookup.Of(Program(ScrCatalogue.FileFlag.MagicalTrap, Do(C(2))));
        Assert.AreEqual(ScrStop.TrapScript, ScrDispatcher.RunSector(new(new(30001, 0, 0), Pc), host).Stop);
        Assert.AreEqual(ScrStop.MissingScript, ScrDispatcher.RunSector(new(new(79, 0, 0), Pc), host).Stop);
    }

    [TestMethod]
    public void A_script_outside_the_admitted_set_is_refused_before_anything_runs()
    {
        var host = With(Program(Do(C(100, N(5))), Do(C(44, (21, 0), N(5001), N(1), N(1))), Do(C(1))));
        var r = RunNpc(host);
        Assert.AreEqual((ScrOutcome.Refused, ScrStop.Refused, 100), (r.Outcome, r.Stop, r.Script));
        Assert.AreEqual((1, ScrRefusalCode.ActionNotAdmitted, 44), (r.Refusal!.Line, r.Refusal.Code, r.Refusal.Type));
        Assert.IsTrue(r.Effects.IsEmpty);
        Assert.AreEqual(0, r.Trace.Count);
        // A narrower set refuses what S2 runs.
        var s1 = RunNpc(With(Program(Do(C(5)), Do(C(1)))), options: new() { Admission = ScrAdmissionSet.S1 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted), (s1.Outcome, s1.Refusal!.Code));
    }

    [TestMethod]
    public void Invalid_events_and_options_are_typed()
    {
        var host = With(Program(Do(C(2))));
        foreach (var e in new ScrObjectEvent[] { new(-1, Npc, Pc, null), new(36, Npc, Pc, null), new(ScrPoint.DialogOverride, Npc, Pc, null), new(Heartbeat, Npc, Pc, null, -1) })
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.InvalidEvent), (ScrDispatcher.RunObject(e, host).Outcome, ScrDispatcher.RunObject(e, host).Stop));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RunNpc(host, options: new() { MaxExecutedLines = 0 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RunNpc(host, options: new() { MaxExecutedLines = 1001 }));
        var wider = new ScrAdmissionSet("wider", [0], [1, 2, 23], [ScrValueKind.Number], [ScrFocusKind.Player]);
        Assert.ThrowsExactly<ArgumentException>(() => RunNpc(host, options: new() { Admission = wider }));
        // A default object reference is not a valid role.
        foreach (var e in new ScrObjectEvent[] { new(Heartbeat, default, Pc, null), new(Heartbeat, Npc, default(ScrObjectRef), null), new(Heartbeat, Npc, Pc, default(ScrObjectRef)) })
            Assert.AreEqual(ScrStop.InvalidEvent, ScrDispatcher.RunObject(e, host).Stop);
        Assert.AreEqual(ScrStop.InvalidEvent, ScrDispatcher.RunTile(new(new(5, 0, 0), default), host).Stop);
        Assert.AreEqual(ScrStop.InvalidEvent, ScrDispatcher.RunSector(new(new(5, 0, 0), default), host).Stop);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScrObjectRef(0, "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new ScrObjectRef(1, "a b"));
        Assert.ThrowsExactly<ArgumentException>(() => new ScrObjectRef(1, new string('a', 129)));
    }

    [TestMethod]
    public void The_trace_lists_each_executed_line_with_its_branch_and_successor()
    {
        var host = With(Program(L(C(3, N(0)), C(3, N(2)), C(0)), Do(C(1)), L(C(43, N(5)), C(1), C(2))), localFlags: 0);
        var r = RunNpc(host, line: 0);
        CollectionAssert.AreEqual(new[]
        {
            new ScrTraceStep(0, 3, false, 0, 1),
            new ScrTraceStep(1, 0, true, 1, null),
        }, r.Trace.ToArray());
        host.Slots[(Npc, Heartbeat)] = new(100, 1, 0);
        CollectionAssert.AreEqual(new[] { new ScrTraceStep(0, 3, true, 3, 2), new ScrTraceStep(2, 43, false, 2, null) }, RunNpc(host).Trace.ToArray());
    }

    [TestMethod]
    public void Profiles_have_the_mode_names_and_switches()
    {
        Assert.AreEqual("steamwoven", ScrRuleProfile.Steamwoven.Name);
        Assert.AreEqual("original", ScrRuleProfile.Original.Name);
        Assert.IsFalse(ScrRuleProfile.Steamwoven.GlobalFlagWriteSpills || ScrRuleProfile.Steamwoven.TileScriptChangesDiscarded);
        Assert.IsTrue(ScrRuleProfile.Original.GlobalFlagWriteSpills && ScrRuleProfile.Original.TileScriptChangesDiscarded);
        Assert.AreSame(ScrRuleProfile.Original, ScrRuleProfile.FromName("original"));
        Assert.IsNull(ScrRuleProfile.FromName("Original"));
        CollectionAssert.AreEqual(new[] { ScrRuleProfile.Steamwoven, ScrRuleProfile.Original }, ScrRuleProfile.All.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S1.IsSubsetOf(ScrAdmissionSet.S2));
        Assert.IsFalse(ScrAdmissionSet.S2.IsSubsetOf(ScrAdmissionSet.S1));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S1.Actions.Append(5).Append(6).ToArray(), ScrAdmissionSet.S2.Actions.ToArray());
    }
}
