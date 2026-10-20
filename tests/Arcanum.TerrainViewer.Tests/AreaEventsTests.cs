using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-039 (event-queue slice E3): the AREA viewer's pump of the session's event queue. Entity ids, facts and
/// scripts are invented.</summary>
[TestClass]
public sealed class AreaEventsTests
{
    private const string Wolf = "oid:wolf";
    private static readonly LifeFacts WolfFacts = new();

    private sealed class Critters : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => entity == Wolf ? WolfFacts : null;
    }

    private sealed class Calls : ISessionScriptWorld
    {
        public required GameSessionOwner Session { get; init; }
        public List<(long At, SessionScriptCall Call)> Ran { get; } = [];
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call)
        {
            Ran.Add((Session.QueryClockSnapshot().GameTimestampMs, call));
            // Each run asks for the next one, as a script that calls itself again in 25 s would.
            if (call.Line < 6) Session.ScheduleScriptCall(Session.Version, call with { Line = call.Line + 1 }, 25_000);
            return true;
        }
    }

    private sealed class Objects : IAreaEventObjects
    {
        // The wolf's T0 row is on map 1; the session may say it stands on another map (a follower moved with the PC).
        public LifeFacts? Facts(int runtimeMap, string entity) => runtimeMap == 1 && entity == Wolf ? WolfFacts : null;
        public bool HasObject(int runtimeMap, string entity) => runtimeMap == 1 && entity is Wolf or "oid:chest";
        public int? HomeMap(string entity) => entity is Wolf or "oid:chest" ? 1 : null;
    }

    [TestMethod]
    public void A_follower_moved_to_another_map_keeps_its_facts_and_its_calls_roles()
    {
        var world = new AreaEventWorld(() => new Objects(), () => null);
        Assert.AreSame(WolfFacts, world.CritterFacts(new(1), Wolf));
        Assert.AreSame(WolfFacts, world.CritterFacts(new(79), Wolf), "answered from its home map's data");
        Assert.IsNull(world.CritterFacts(new(1), "oid:chest"), "an object without combat fields");
        Assert.IsNull(world.CritterFacts(new(79), "oid:ghost"));
        Assert.IsTrue(world.ObjectExists(new(79), Wolf));
        Assert.IsTrue(world.ObjectExists(new(79), SessionEventKinds.PlayerEntity));
        Assert.IsFalse(world.ObjectExists(new(1), "oid:ghost"));
        Assert.IsNull(new AreaEventWorld(() => null, () => null).CritterFacts(new(1), Wolf), "no combat host yet");
        Assert.IsFalse(world.RunScriptCall(new(5, 0, null, null)));
        Assert.AreEqual((1, "no script host on this map"), (world.CallRefusals, world.LastCallRefusal));
    }

    private static GameSessionOwner Wounded()
    {
        var session = ConversationSessionFixture.Create();
        var hit = session.ApplyDamage(session.Version, SessionLifeTarget.Critter(Wolf, WolfFacts, LifeState.Unharmed),
            new LifeDamage { HitPoints = 5, Fatigue = 25, Poison = 30 });
        Assert.AreEqual(GameSessionDisposition.Changed, hit.Disposition);
        return session;
    }

    // Everything the time split could change: the critter's life, the pending events and the clock.
    private static string Trace(GameSessionOwner session) =>
        $"{session.QueryLifeRecord(Wolf)?.State} @ {session.QueryClockSnapshot().GameTimestampMs} | "
        + string.Join(", ", session.QueryEvents().Select(e => $"{e.Kind}@{e.DeadlineMs}:{string.Join('/', e.Payload.Values)}"));

    [TestMethod]
    public void The_same_game_time_gives_the_same_state_however_the_frames_split_it()
    {
        var limits = new AreaEventPumpLimits { MaxStepsPerAdvance = 4096 };
        var splits = new[] { new long[] { 7_200_000 }, Enumerable.Repeat(133L, 7_200_000 / 133).Append(7_200_000 % 133).ToArray(),
            Enumerable.Range(0, 400).Select(i => (long)(i * 37 % 1000 + 1)).ToArray() };
        splits[2] = splits[2].Append(7_200_000 - splits[2].Sum()).ToArray();
        var traces = new List<string>();
        foreach (var split in splits)
        {
            using var session = Wounded();
            var pump = new AreaEventPump(session, new Critters(), limits);
            foreach (var ms in split) pump.Advance(ms);
            traces.Add(Trace(session));
        }
        Assert.AreEqual(1, traces.Distinct().Count(), string.Join("\n", traces));
        StringAssert.Contains(traces[0], "@ " + (54_000_000 + 7_200_000), "two hours went by");

        // Without the steps a coarse frame fires a timer late and its next one later, so the outcome follows the frame rate.
        using var naive = Wounded();
        foreach (var ms in new[] { 3_600_000L, 3_600_000L })
        {
            naive.AdvanceGameTime(naive.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance);
            naive.ProcessEvents(new Critters());
        }
        Assert.AreNotEqual(traces[0], Trace(naive));
    }

    [TestMethod]
    public void A_wait_is_one_jump_whose_timers_fire_once_late()
    {
        using var stepped = Wounded();
        new AreaEventPump(stepped, new Critters(), new() { MaxStepsPerAdvance = 4096 }).Advance(3_600_000);
        using var waited = Wounded();
        var pump = new AreaEventPump(waited, new Critters());
        var report = pump.Jump(3_600_000);
        Assert.AreEqual((1, 4), (report.Steps, report.Fired), "the fatigue, resting, poison damage and poison recovery timers, once each");
        Assert.AreNotEqual(Trace(stepped), Trace(waited), "a jump is not replayed period by period");
        Assert.AreEqual(54_000_000 + 3_600_000, waited.QueryClockSnapshot().GameTimestampMs);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pump.Jump(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pump.Advance(-1));
    }

    [TestMethod]
    public void A_paused_clock_moves_nothing_and_the_step_bound_reports_late_firing()
    {
        using var session = Wounded();
        var clock = session.QueryClockSnapshot();
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(clock.Token, new(true, PauseReason.Menu)).Disposition);
        var pump = new AreaEventPump(session, new Critters(), new() { MaxStepsPerAdvance = 2 });
        var paused = pump.Advance(600_000);
        Assert.AreEqual((0, 1, clock.GameTimestampMs), (paused.Steps, paused.Passes, session.QueryClockSnapshot().GameTimestampMs));
        Assert.AreEqual(clock.GameTimestampMs + 15_000, pump.NextDeadline(clock.GameTimestampMs), "the poison damage timer");
        Assert.AreEqual(ClockMutationDisposition.Changed, session.SetPaused(session.QueryClockSnapshot().Token, default).Disposition);

        // Two steps: to the first deadline, then the rest of the 600 s at once; later timers fire late.
        var bounded = pump.Advance(600_000);
        Assert.AreEqual((2, true), (bounded.Steps, bounded.Coalesced));
        StringAssert.Contains(bounded.Status(session.QueryClockSnapshot().GameTimestampMs), "late (step bound)");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AreaEventPump(session, new Critters(), new() { MaxPassesPerStep = 0 }));
    }

    [TestMethod]
    public void Deferred_calls_run_at_their_deadlines_and_a_call_from_a_call_waits_for_a_later_step()
    {
        using var session = ConversationSessionFixture.Create();
        var world = new Calls { Session = session };
        var start = session.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleScriptCall(session.Version,
            new(790, 4, new(new(1), SessionEventKinds.PlayerEntity), null), 10_000).Disposition);
        var pump = new AreaEventPump(session, world);
        for (var frame = 0; frame < 400; frame++) pump.Advance(333);
        // Each at its own deadline although no frame ends there; the third asks for nothing more.
        CollectionAssert.AreEqual(new[] { 10_000L, 35_000, 60_000 }, world.Ran.Select(r => r.At - start).ToArray());
        CollectionAssert.AreEqual(new[] { 4, 5, 6 }, world.Ran.Select(r => r.Call.Line).ToArray());
        Assert.AreEqual(0, session.QueryScriptCalls().Count);
    }

    [TestMethod]
    public void A_viewer_dialog_script_schedules_a_call_the_pump_then_runs_through_the_dispatcher()
    {
        var root = Directory.CreateTempSubdirectory("arc-world-039-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr"));
            // 790: call script 791 at line 0 in 10 s (80 s of game time) with the triggerer and the attachee; 791: set global flag 9.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00790caller.scr"), Program((54, [(3, 791), (3, 0), (0, 0), (1, 0), (3, 10)]), (1, [])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00791called.scr"), Program((100, [(3, 9)]), (1, [])));
            // 793: remove the pending call of script 791 for the attachee; 794: start a dialog (the viewer does not run it).
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00793remove.scr"), Program((125, [(3, 791), (1, 0)]), (1, [])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00794talk.scr"), Program((4, [(3, 1), (0, 0)]), (1, [])));
            // 792: a float line, which the viewer's set does not admit.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00792float.scr"), Program((23, [(3, 1), (1, 0)]), (1, [])));
            using var content = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat")).Content!;
            using var source = new AreaOwnerFixture(motionBodies: true);
            var snapshot = source.Owner.LoadStrict();
            using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
            var host = new AreaConversationHost(snapshot, content, session);
            var pc = new SessionScriptRole(new(snapshot.RuntimeMap), SessionEventKinds.PlayerEntity);
            var npc = new SessionScriptRole(new(snapshot.RuntimeMap), "oid:caller");

            Assert.IsTrue(host.RunCall(new(790, 0, pc, npc), out var detail), detail);
            var pending = session.QueryScriptCalls().Single();
            Assert.AreEqual(new SessionScriptCall(791, 0, pc, npc), pending.Call);

            var world = new AreaEventWorld(() => null, () => host);
            Assert.IsTrue(world.ObjectExists(pc.Map, SessionEventKinds.PlayerEntity));
            Assert.IsFalse(world.ObjectExists(npc.Map, npc.Entity), "no combat host: no object rows");
            // The attachee is unknown to this world, so the call is dropped when it comes due (10 s of the action are 80 s of
            // game time).
            var dropped = new AreaEventPump(session, world).Advance(80_000);
            Assert.AreEqual((0, 1), (dropped.ScriptCalls, dropped.Dropped), dropped.ToString());

            // A remove script call of the same script and attachee takes the pending call off the queue.
            Assert.IsTrue(host.RunCall(new(790, 0, pc, npc), out detail), detail);
            Assert.IsTrue(host.RunCall(new(793, 0, pc, npc), out detail), detail);
            Assert.AreEqual(0, session.QueryScriptCalls().Count);
            // A call that would start a conversation applies nothing.
            Assert.IsFalse(host.RunCall(new(794, 0, pc, npc), out detail));
            StringAssert.Contains(detail, "starts a conversation");

            Assert.IsTrue(host.RunCall(new(790, 0, pc, null), out detail), detail);
            var ran = new AreaEventPump(session, world).Advance(80_000);
            Assert.AreEqual((1, 1), (ran.ScriptCalls, ran.ScriptCallsRun), world.LastCallRefusal);
            Assert.IsTrue(Flag(session, 9));
            Assert.AreEqual(0, session.QueryScriptCalls().Count);

            // A call the viewer cannot run is consumed and its reason kept.
            Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleScriptCall(session.Version, new(792, 0, pc, null), 1_000).Disposition);
            var missing = new AreaEventPump(session, world).Advance(1_000);
            Assert.AreEqual((1, 0), (missing.ScriptCalls, missing.ScriptCallsRun));
            StringAssert.Contains(world.LastCallRefusal, "ActionNotAdmitted");
            Assert.IsFalse(new AreaEventWorld(() => null, () => null).RunScriptCall(new(791, 0, pc, null)));
        }
        finally { root.Delete(true); }
    }

    private static bool Flag(GameSessionOwner session, int index) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new(index))).Value == true;

    // A synthetic SCR file: one action per line, each with (kind, value) operands.
    private static byte[] Program(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var clause = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(clause, lines[line].Action);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                clause[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(clause[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        Assert.AreEqual(ScrReadStatus.Read, ScrProgram.Read(bytes).Status);
        return bytes;
    }
}
