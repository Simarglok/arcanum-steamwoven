using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Deferred SCR calls on the session's event queue (ARC-SCRIPT-010). Entity ids are invented.
[TestClass]
public sealed class SessionScriptCallTests
{
    private const long T0 = 54_000_000;
    private static readonly SessionScriptRole Pc = new(new(1), "pc"), Npc = new(new(1), "oid:npc");

    private sealed class Scripts : ISessionScriptWorld
    {
        public HashSet<(int, string)> Objects { get; } = [(1, "pc"), (1, "oid:npc")];
        public List<SessionScriptCall> Ran { get; } = [];
        public bool Accept { get; set; } = true;
        public Func<SessionScriptCall, bool>? During { get; set; }
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => Objects.Contains((map.RuntimeMap, entity));
        public bool RunScriptCall(SessionScriptCall call) { Ran.Add(call); return (During?.Invoke(call) ?? true) && Accept; }
    }

    private sealed class LifeOnly : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Session()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void Schedule(GameSessionOwner owner, SessionScriptCall call, long delay) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, call, delay).Disposition);

    [TestMethod]
    public void A_call_fires_once_its_game_delay_has_passed_and_the_host_runs_it()
    {
        using var owner = Session();
        var revision = owner.Version.Revision;
        Schedule(owner, new(2023, 4, Pc, Npc), 80_000);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        var pending = owner.QueryScriptCalls().Single();
        Assert.AreEqual((T0 + 80_000, new SessionScriptCall(2023, 4, Pc, Npc)), (pending.DeadlineMs, pending.Call));
        var world = new Scripts();
        Advance(owner, 79_999);
        Assert.AreEqual(0, owner.ProcessEvents(world).ScriptCalls.Count);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual((new SessionScriptCall(2023, 4, Pc, Npc), true), (pass.ScriptCalls.Single().Call, pass.ScriptCalls.Single().Ran));
        CollectionAssert.AreEqual(new[] { new SessionScriptCall(2023, 4, Pc, Npc) }, world.Ran);
        Assert.AreEqual(0, owner.QueryScriptCalls().Count);
    }

    [TestMethod]
    public void Absent_roles_are_kept_and_a_call_naming_a_missing_object_is_dropped()
    {
        using var owner = Session();
        Schedule(owner, new(7, 0, null, Pc), 1);
        Schedule(owner, new(8, 0, Pc, new(new(2), "oid:gone")), 1);
        Advance(owner, 1);
        var world = new Scripts();
        var pass = owner.ProcessEvents(world);
        CollectionAssert.AreEqual(new[] { new SessionScriptCall(7, 0, null, Pc) }, world.Ran);
        Assert.AreEqual(1, pass.Pass!.Outcomes.Count(o => o.Kind == GameEventOutcomeKind.EntityMissing));
    }

    [TestMethod]
    public void A_host_that_cannot_run_scripts_consumes_due_calls_unrun()
    {
        using var owner = Session();
        Schedule(owner, new(7, 0, Pc, Npc), 1);
        Advance(owner, 1);
        // A life-only host cannot confirm the objects either, so the call is dropped; with an absent-role call it is
        // handed to the handler, which reports it as not run.
        Assert.AreEqual(0, owner.ProcessEvents(new LifeOnly()).ScriptCalls.Count);
        Schedule(owner, new(9, 0, null, null), 1);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(new LifeOnly());
        Assert.AreEqual((9, false), (pass.ScriptCalls.Single().Call.Script, pass.ScriptCalls.Single().Ran));
        Assert.AreEqual(0, owner.QueryScriptCalls().Count);
        // A refusal by a script host is reported too.
        Schedule(owner, new(10, 0, Pc, Npc), 1);
        Advance(owner, 1);
        Assert.IsFalse(owner.ProcessEvents(new Scripts { Accept = false }).ScriptCalls.Single().Ran);
    }

    [TestMethod]
    public void A_call_scheduled_while_a_call_runs_waits_for_the_next_pass()
    {
        using var owner = Session();
        Schedule(owner, new(1, 0, Pc, Npc), 1);
        Advance(owner, 2);
        var world = new Scripts();
        world.During = call =>
        {
            if (call.Script == 1) Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(2, 0, Pc, Npc), 1).Disposition);
            return true;
        };
        owner.ProcessEvents(world);
        CollectionAssert.AreEqual(new[] { 1 }, world.Ran.Select(c => c.Script).ToArray());
        Advance(owner, 1);
        owner.ProcessEvents(world);
        CollectionAssert.AreEqual(new[] { 1, 2 }, world.Ran.Select(c => c.Script).ToArray());
    }

    [TestMethod]
    public void Remove_takes_the_first_pending_call_of_the_script_and_attachee()
    {
        using var owner = Session();
        Schedule(owner, new(5, 1, Pc, Npc), 3000);
        Schedule(owner, new(5, 2, Pc, Npc), 1000);
        Schedule(owner, new(5, 3, Pc, null), 500);
        Schedule(owner, new(6, 4, Pc, Npc), 100);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveScriptCall(owner.Version, 5, Npc).Disposition);
        // The earliest matching one (line 2) left; the call without attachee and the other script stay.
        CollectionAssert.AreEqual(new[] { 4, 3, 1 }, owner.QueryScriptCalls().Select(c => c.Call.Line).ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveScriptCall(owner.Version, 5, null).Disposition);
        CollectionAssert.AreEqual(new[] { 4, 1 }, owner.QueryScriptCalls().Select(c => c.Call.Line).ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveScriptCall(owner.Version, 7, Npc).Disposition);
    }

    [TestMethod]
    public void Scheduling_validates_its_input_and_the_version()
    {
        using var owner = Session();
        foreach (var (call, delay) in new (SessionScriptCall, long)[]
        {
            (new(1, 0, Pc, Npc), 0), (new(1, 0, Pc, Npc), SessionEventKinds.MaxScriptCallDelayMs + 1),
            (new(1, 0, new(new(0), "pc"), Npc), 1), (new(1, 0, Pc, new(new(1), "-")), 1), (new(1, 0, Pc, new(new(1), "a b")), 1),
        })
            Assert.AreEqual(GameSessionFailure.InvalidEvents, owner.ScheduleScriptCall(owner.Version, call, delay).Failure, $"{call} {delay}");
        var stale = owner.Version;
        Schedule(owner, new(1, 0, Pc, Npc), 1);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ScheduleScriptCall(stale, new(1, 0, Pc, Npc), 1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RemoveScriptCall(stale, 1, Npc).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(1, 0, Pc, Npc), SessionEventKinds.MaxScriptCallDelayMs).Disposition);
    }

    [TestMethod]
    public void Pending_calls_round_trip_through_canonical_text_and_fire_after_a_restore()
    {
        using var owner = Session();
        Schedule(owner, new(2023, 4, Pc, Npc), 80_000);
        Schedule(owner, new(7, 0, null, Pc), 5_000);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"kind\":\"scr.call\"");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryScriptCalls().ToArray(), restored.QueryScriptCalls().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Advance(restored, 80_000);
        var world = new Scripts();
        restored.ProcessEvents(world);
        CollectionAssert.AreEqual(new[] { 7, 2023 }, world.Ran.Select(c => c.Script).ToArray());
    }

    [TestMethod]
    public void Restore_refuses_a_call_no_session_could_have_saved()
    {
        using var owner = Session();
        Schedule(owner, new(2023, 4, Pc, Npc), 80_000);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var deadline = (T0 + 80_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var bad in new[]
        {
            text.Replace($"\"deadlineMs\":{deadline}", $"\"deadlineMs\":{T0 + 80_001}"),
            text.Replace($"[{T0},80000,2023,4]", $"[{T0},0,2023,4]").Replace($"\"deadlineMs\":{deadline}", $"\"deadlineMs\":{T0}"),
            text.Replace($"[{T0},80000,2023,4]", $"[{T0 + 1},80000,2023,4]").Replace($"\"deadlineMs\":{deadline}", $"\"deadlineMs\":{T0 + 80_001}"),
        })
        {
            Assert.AreNotEqual(text, bad, "the edit applies");
            var result = GameSessionOwner.Restore(Decode(bad), RestoreOptions());
            Assert.AreEqual(GameSessionFailure.InvalidEvents, result.Failure);
        }
    }

    [TestMethod]
    public void Player_and_follower_roles_travel_with_the_actor_and_other_roles_stay()
    {
        using var owner = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var exterior = owner.QueryActor().Map;
        SessionScriptRole Pcr = new(exterior, SessionEventKinds.PlayerEntity), Virgil = new(exterior, "virgil"), Wolf = new(exterior, "oid:wolf");
        Schedule(owner, new(1, 0, Pcr, Virgil), 10);
        Schedule(owner, new(2, 0, Virgil, Wolf), 10);
        Schedule(owner, new(3, 0, null, Wolf), 10);
        // A follower role stored on a third map is not on the map being left, so it stays there.
        Schedule(owner, new(4, 0, null, Virgil with { Map = new(5) }), 10);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        CollectionAssert.AreEqual(new[]
        {
            new SessionScriptCall(1, 0, Pcr with { Map = Cave }, Virgil with { Map = Cave }),
            new SessionScriptCall(2, 0, Virgil with { Map = Cave }, Wolf),
            new SessionScriptCall(3, 0, null, Wolf),
            new SessionScriptCall(4, 0, null, Virgil with { Map = new(5) }),
        }, owner.QueryScriptCalls().Select(c => c.Call).ToArray());
        // Remove matches the attachee by entity id, whatever map either stores.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveScriptCall(owner.Version, 1, new(exterior, "virgil")).Disposition);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, owner.QueryScriptCalls().Select(c => c.Call.Script).ToArray());
    }

    [TestMethod]
    public void The_call_share_is_counted_on_its_own_and_removal_moves_one_revision()
    {
        using var owner = Session();
        for (var i = 0; i < SessionEventKinds.MaxScriptCalls; i++) Schedule(owner, new(i + 1, 0, Pc, Npc), 1000);
        // The queue still has room (no life timers), so only the call share refuses the next one.
        Assert.IsTrue(owner.QueryEvents().Count < SessionEventKinds.MaxPending);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, owner.ScheduleScriptCall(owner.Version, new(1, 0, Pc, Npc), 1000).Failure);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveScriptCall(owner.Version, 1, Npc).Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RemoveScriptCall(owner.Version, 1, Npc).Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        // A save holding more calls than the share is refused.
        Schedule(owner, new(1, 0, Pc, Npc), 1000);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var last = owner.QueryEvents().Max(e => e.Sequence);
        var one = $"{{\"sequence\":{last},";
        var start = text.IndexOf(one, StringComparison.Ordinal);
        var end = text.IndexOf('}', text.IndexOf("\"values\"", start, StringComparison.Ordinal)) + 1;
        var copy = text[start..end].Replace($"\"sequence\":{last},", $"\"sequence\":{last + 1},");
        var extra = text.Insert(end, "," + copy).Replace($"\"nextSequence\":{last + 1}", $"\"nextSequence\":{last + 2}");
        Assert.AreNotEqual(text, extra);
        Assert.AreEqual(GameSessionFailure.EventBudgetExceeded, GameSessionOwner.Restore(Decode(extra), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void Restore_refuses_a_call_role_no_session_could_have_saved()
    {
        using var owner = Session();
        Schedule(owner, new(2023, 4, Pc, Npc), 80_000);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        foreach (var bad in new[] { text.Replace("{\"map\":1,\"entity\":\"oid:npc\"}", "{\"map\":0,\"entity\":\"oid:npc\"}"),
                                    text.Replace("\"entity\":\"oid:npc\"", "\"entity\":\"a b\"") })
        {
            Assert.AreNotEqual(text, bad, "the edit applies");
            var decoded = GameSessionSnapshotCodec.TryDecode(bad);
            Assert.IsTrue(decoded.Snapshot is null || GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure == GameSessionFailure.InvalidEvents);
        }
    }

    // End to end: a run's call script in goes into the session, fires after its delay and runs the called script, whose
    // global flag lands in the session (flag 9 is in the fixture's flag domain).
    [TestMethod]
    public void A_dispatcher_call_in_is_scheduled_fires_and_runs_through_the_dispatcher()
    {
        using var owner = Session();
        var host = new DispatchHost(owner);
        host.Scripts[100] = Program((54, [(3, 2023), (3, 1), (0, 0), (1, 0), (3, 10)]), (1, []));
        host.Scripts[2023] = Program((1, []), (100, [(3, 9)]), (2, []));
        var run = ScrDispatcher.RunObject(new(19, new(1, "oid:npc"), new(1, "pc"), null), host);
        var request = run.Effects.Calls.Single();
        Assert.AreEqual((ScrCallKind.Schedule, 80_000L), (request.Kind, request.DelayMs));
        Schedule(owner, new(request.Script, request.Line, Role(request.Triggerer), Role(request.Attachee)), request.DelayMs);
        Advance(owner, 80_000);
        var pass = owner.ProcessEvents(host);
        Assert.IsTrue(pass.ScriptCalls.Single().Ran);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.ReturnRunDefault, 2023), (host.Last!.Outcome, host.Last.Stop, host.Last.Script), string.Join(";", host.Last.Trace));
        Assert.AreEqual(9, host.Last.Effects.GlobalFlags.Single().Index);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, host.Receipts.Single(), "the call's flag lands in the session");
        Assert.IsTrue(host.GlobalFlag(9));
    }

    private static SessionScriptRole? Role(ScrObjectRef? r) => r is { } o ? new(new(o.Map), o.Code) : null;

    private static ScrProgram Program(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var at = 64 + line * 132 + 44;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), lines[line].Action);
            for (var k = 0; k < lines[line].Operands.Length; k++)
            {
                bytes[at + 4 + k] = lines[line].Operands[k].Kind;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 4 * k), lines[line].Operands[k].Value);
            }
        }
        return ScrProgram.Read(bytes).Program!;
    }

    /// <summary>A minimal host: one NPC slot, scripts by number, the session's global flags; calls run RunCall.</summary>
    private sealed class DispatchHost(GameSessionOwner owner) : IScrHost, ISessionScriptWorld
    {
        public Dictionary<int, ScrProgram> Scripts { get; } = new();
        public ScrRunResult? Last { get; private set; }
        public List<GlobalFlagWriteDisposition> Receipts { get; } = new();
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => point == 19 ? new(100, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Scripts.TryGetValue(number, out var p) ? ScrScriptLookup.Of(p) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) =>
            owner.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), owner.Flags.Version, new FlagId(index))).Value == true;
        public int StoryState() => 0;
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(1, "pc");
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == "pc";
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => entity is "pc" or "oid:npc";
        public bool RunScriptCall(SessionScriptCall call)
        {
            ScrObjectRef? Ref(SessionScriptRole? r) => r is { } o ? new(o.Map.RuntimeMap, o.Entity) : null;
            Last = ScrDispatcher.RunCall(new(call.Script, call.Line, Ref(call.Triggerer), Ref(call.Attachee)), this);
            foreach (var write in Last.Effects.GlobalFlags)
                Receipts.Add(owner.Flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), owner.Flags.Version, new FlagId(write.Index), write.Value)).Disposition);
            return Last.Outcome != ScrOutcome.Refused;
        }
    }
}
