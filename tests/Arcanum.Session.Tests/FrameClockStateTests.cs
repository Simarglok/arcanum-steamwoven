using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>MIG12 S3 (ARC-DEV-122): the session owns the three clocks (real, game, animation), admits frames, jumps game and animation
/// together, hands all three clocks to its event queue and saves the real and animation stamps (schema 70).</summary>
[TestClass]
public sealed class FrameClockStateTests
{
    private const long Start = 54_000_000;

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static void Clocks(GameSessionOwner owner, long real, long game, long animation, long world)
    {
        var clocks = owner.QueryFrameClocks();
        Assert.AreEqual((real, game, animation, world), (clocks.RealMs, clocks.GameMs, clocks.AnimationMs, clocks.WorldMs));
        Assert.AreEqual(game, owner.QueryClockSnapshot().GameTimestampMs, "the game stamp is the clock owner's");
    }

    // One whole frame in one step (MIG12 S4: the clocks move when the frame is spent, not when it is admitted).
    private static FrameAdmission Frame(GameSessionOwner owner, long hostMs, bool frozen = false)
    {
        var result = owner.BeginFrame(hostMs, frozen);
        Assert.AreEqual(FrameClockFailure.None, result.Failure);
        var frame = result.Frame!;
        if (frame.RemainingUnits > 0) Assert.IsTrue(frame.Step(frame.RemainingUnits).Ok);
        frame.End();
        return frame.Admission;
    }

    [TestMethod]
    public void A_new_session_starts_real_at_one_and_animation_at_the_start_time()
    {
        using var owner = Create();
        Clocks(owner, 1, Start, Start, 0);
        var capture = owner.Capture().Clock;
        Assert.AreEqual((1L, Start), (capture.RealMs, capture.AnimationMs));
    }

    [TestMethod]
    public void A_frame_moves_real_game_and_animation_together_when_it_is_spent()
    {
        using var owner = Create();
        Assert.AreEqual(default, Frame(owner, 1_000), "the first observation admits nothing");
        Assert.AreEqual(new FrameAdmission(100, 800, 800), Frame(owner, 1_100));
        Clocks(owner, 101, Start + 800, Start + 800, 100);
        // Short frames add up, a long one is cut to 250 ms.
        Assert.AreEqual(default, Frame(owner, 1_103));
        Assert.AreEqual(new FrameAdmission(6, 48, 48), Frame(owner, 1_106));
        Assert.AreEqual(new FrameAdmission(250, 2_000, 2_000), Frame(owner, 9_000));
        Clocks(owner, 357, Start + 2_848, Start + 2_848, 356);
    }

    [TestMethod]
    public void The_pause_stops_animation_and_game_but_not_real_and_a_frozen_frame_observes_nothing()
    {
        using var owner = Create();
        Frame(owner, 0);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        Assert.AreEqual(new FrameAdmission(100, 0, 0), Frame(owner, 100));
        Clocks(owner, 101, Start, Start, 0);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, default).Disposition);
        // A frozen frame (TL25) observes nothing; the next frame credits at most 250 ms.
        Assert.AreEqual(default, Frame(owner, 5_000, frozen: true));
        Clocks(owner, 101, Start, Start, 0);
        Assert.AreEqual(new FrameAdmission(250, 2_000, 2_000), Frame(owner, 9_000));
        Assert.AreEqual(FrameClockFailure.InvalidHostTime, owner.BeginFrame(-1).Failure);
        Clocks(owner, 351, Start + 2_000, Start + 2_000, 250);
    }

    [TestMethod]
    public void A_jump_moves_game_and_animation_together_and_leaves_real_and_world_time()
    {
        using var owner = Create();
        Frame(owner, 0);
        Frame(owner, 100);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        // A jump ignores the pause (TL26) and does not fast-forward world time.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 3_600_000).Disposition);
        Clocks(owner, 101, Start + 800 + 3_600_000, Start + 800 + 3_600_000, 100);
        // A zero jump is 1 ms; an explicit wait of the game clock is a jump too.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 0).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 9, ClockAdvanceReason.ExplicitWait).Disposition);
        Clocks(owner, 101, Start + 800 + 3_600_010, Start + 800 + 3_600_010, 100);
        // Refusals change nothing.
        var token = owner.QueryClockSnapshot().Token;
        Assert.AreEqual(ClockFailure.InvalidDuration, owner.JumpClocks(token, -1).Failure);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(token, default).Disposition);
        Assert.AreEqual(ClockFailure.StaleToken, owner.JumpClocks(token, 1_000).Failure);
        Clocks(owner, 101, Start + 800 + 3_600_010, Start + 800 + 3_600_010, 100);
    }

    [TestMethod]
    public void A_jump_the_animation_clock_cannot_take_is_refused_before_the_game_clock_moves()
    {
        using var source = Create();
        var top = FrameClockKeeper.MaxAdmittingStampMs;
        var text = GameSessionSnapshotCodec.Encode(source.Capture())
            .Replace($"\"animationMs\":{Start}}}", $"\"animationMs\":{top}}}");
        using var owner = Restore(Decode(text));
        var before = owner.QueryClockSnapshot();
        var refused = owner.JumpClocks(before.Token, long.MaxValue - top + 1);
        Assert.AreEqual((ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow), (refused.Disposition, refused.Failure));
        Assert.AreEqual(before, owner.QueryClockSnapshot());
        Assert.AreEqual(top, owner.QueryFrameClocks().AnimationMs);
        // A stale token is still reported as stale.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(before.Token, new(true, PauseReason.Menu)).Disposition);
        Assert.AreEqual(ClockFailure.StaleToken, owner.JumpClocks(before.Token, long.MaxValue - top + 1).Failure);
        // A saved stamp at the highest admitting value still takes frames; a jump past it refuses the next frame.
        Assert.AreEqual(default, Frame(owner, 0));
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, long.MaxValue - top).Disposition);
        Assert.AreEqual(long.MaxValue, owner.QueryFrameClocks().AnimationMs);
        Assert.AreEqual(FrameClockFailure.TimeOverflow, owner.BeginFrame(10).Failure);
        // A saved stamp above it would refuse every frame, so the decode refuses it.
        var above = GameSessionSnapshotCodec.TryDecode(text.Replace($"\"animationMs\":{top}}}", $"\"animationMs\":{top + 1}}}"));
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.clock.animationMs"), (above.Failure, above.Path));
        var realAbove = GameSessionSnapshotCodec.TryDecode(text.Replace("\"realMs\":1,", $"\"realMs\":{top + 1},"));
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.clock.realMs"), (realAbove.Failure, realAbove.Path));
    }

    [TestMethod]
    public void The_queue_is_handed_the_real_the_game_and_the_animation_clock()
    {
        using var owner = Create();
        Assert.AreEqual(new Arcanum.Simulation.Events.GameEventClocks(1, Start, Start), owner.QueryEventClocks());
        Frame(owner, 0);
        Frame(owner, 100);
        Assert.AreEqual(new Arcanum.Simulation.Events.GameEventClocks(101, Start + 800, Start + 800), owner.QueryEventClocks());
        // The session's own shorthand of a step of game time (internal) moves the game stamp alone, so the three differ.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 300, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(new Arcanum.Simulation.Events.GameEventClocks(101, Start + 1_100, Start + 800), owner.QueryEventClocks());
    }

    [TestMethod]
    public void The_real_and_animation_stamps_round_trip_and_a_restore_credits_nothing_at_its_first_frame()
    {
        using var owner = Create();
        Frame(owner, 0);
        Frame(owner, 200);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 5_000).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, $"\"realMs\":201,\"animationMs\":{Start + 1_600 + 5_000}}}");
        using var restored = Restore(Decode(text));
        Clocks(restored, 201, Start + 6_600, Start + 6_600, 0);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(default, Frame(restored, 10_000), "a load starts a fresh reference");
        Assert.AreEqual(new FrameAdmission(50, 400, 400), Frame(restored, 10_050));
    }

    [TestMethod]
    public void A_damaged_clock_section_is_refused_with_its_typed_failure()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(text.Replace("\"realMs\":1,", ""), SnapshotDecodeFailure.MissingField, "$.clock.realMs");
        Fails(text.Replace($",\"animationMs\":{Start}", ""), SnapshotDecodeFailure.MissingField, "$.clock.animationMs");
        Fails(text.Replace("\"realMs\":1,", "\"realMs\":0,"), SnapshotDecodeFailure.OutOfRange, "$.clock.realMs");
        Fails(text.Replace($"\"animationMs\":{Start}", "\"animationMs\":-1"), SnapshotDecodeFailure.OutOfRange, "$.clock.animationMs");
        Fails(text.Replace("\"realMs\":1,", "\"realMs\":\"1\","), SnapshotDecodeFailure.WrongType, "$.clock.realMs");
    }

    [TestMethod]
    public void The_sixty_nine_to_seventy_step_starts_real_at_one_and_sets_animation_to_the_saved_game_stamp()
    {
        using var owner = Create();
        // A game stamp other than the start: an ordinary game advance outside a frame moves the game clock alone.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 1_234, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(current, $"\"timestampMs\":{Start + 1_234},");
        var older = SnapshotUpgradeTests.WithoutTownMaps(current.Replace(NoGoals + NoSpells, "")).Replace("\"schemaVersion\":73,\"world\"", "\"schemaVersion\":69,\"world\"")
            .Replace($",\"realMs\":1,\"animationMs\":{Start}}}", "}");
        StringAssert.Contains(older, $"\"timestampMs\":{Start + 1_234},\"paused\":false,\"pauseReason\":\"None\",\"revision\":1}}");
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual((1L, Start + 1_234), (upgraded.Clock.RealMs, upgraded.Clock.AnimationMs));
        using var restored = Restore(upgraded);
        Clocks(restored, 1, Start + 1_234, Start + 1_234, 0);
        // The inverse used by the older steps' tests is exact for a text whose animation equals its game stamp.
        Assert.AreEqual(GameSessionSnapshotCodec.Encode(upgraded), GameSessionSnapshotCodec.Encode(Decode(SnapshotUpgradeTests.Schema69(GameSessionSnapshotCodec.Encode(upgraded)))));

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        // A schema 69 text that already carries a stamp is damage: the step adds them, never merges.
        Fails(older.Replace("\"revision\":1}", "\"revision\":1,\"realMs\":5}"), SnapshotDecodeFailure.UnknownField, "$.clock.realMs");
        Fails(older.Replace("\"revision\":1}", "\"revision\":1,\"animationMs\":5}"), SnapshotDecodeFailure.UnknownField, "$.clock.animationMs");
        Fails(older.Replace($"\"timestampMs\":{Start + 1_234},", ""), SnapshotDecodeFailure.MissingField, "$.clock.timestampMs");
        Fails(older.Replace($"\"timestampMs\":{Start + 1_234},", "\"timestampMs\":\"x\","), SnapshotDecodeFailure.WrongType, "$.clock.timestampMs");
        // A negative or fractional game stamp keeps the strict decode's failure on the game stamp, not the copied animation stamp.
        Fails(older.Replace($"\"timestampMs\":{Start + 1_234},", "\"timestampMs\":-5,"), SnapshotDecodeFailure.OutOfRange, "$.clock.timestampMs");
        var noClock = older.Replace(older[older.IndexOf("\"clock\":", StringComparison.Ordinal)..(older.IndexOf("\"flags\":", StringComparison.Ordinal))], "");
        Fails(noClock, SnapshotDecodeFailure.MissingField, "$.clock");
    }

    [TestMethod]
    public void The_queue_schedules_and_fires_its_game_events_on_the_game_clock_whatever_real_time_did()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(1, 0, null, null), 1_000).Disposition);
        Assert.AreEqual(Start + 1_000, owner.QueryEvents().Single().DeadlineMs);
        // Behind the pause real time moves by frames and game and animation time stand; the game-domain call waits for game time.
        Frame(owner, 0);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        for (var host = 250L; host <= 2_500; host += 250) Frame(owner, host);
        Clocks(owner, 2_501, Start, Start, 0);
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).ScriptCalls.Count);
        Assert.AreEqual(1, owner.QueryEvents().Count);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, default).Disposition);
        Frame(owner, 2_625);
        Assert.AreEqual(1, owner.ProcessEvents(new NoWorld()).ScriptCalls.Count);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }

    // MIG12 S4 (ARC-DEV-123): ordinary game time moves only inside an open frame, spent in steps that move the three clocks together.

    [TestMethod]
    public void No_public_call_moves_ordinary_game_time_outside_a_frame()
    {
        var clockCalls = typeof(GameSessionOwner).GetMethods().Where(m => m.Name is "AdvanceGameTime" or "AdvanceFrame").ToArray();
        Assert.AreEqual(0, clockCalls.Length, "the ordinary advance and the frame admission that moved clocks by themselves are gone");
        Assert.IsTrue(typeof(GameSessionOwner).GetMethods().Any(m => m.Name == "BeginFrame"));
        Assert.IsTrue(typeof(GameSessionOwner).GetMethods().Any(m => m.Name == "JumpClocks"));
    }

    [TestMethod]
    public void A_frame_is_spent_in_steps_that_move_real_game_and_animation_together()
    {
        using var owner = Create();
        Frame(owner, 0);
        var begun = owner.BeginFrame(100);
        var frame = begun.Frame!;
        Assert.AreEqual((new FrameAdmission(100, 800, 800), 800L, true), (frame.Admission, frame.TotalUnits, frame.IsOpen));
        Clocks(owner, 1, Start, Start, 0);
        foreach (var units in new long[] { 3, 5, 8, 100 })
        {
            var step = frame.Step(units);
            Assert.AreEqual((true, units), (step.Ok, step.GameMs));
        }
        Clocks(owner, 1 + 116 / 8, Start + 116, Start + 116, 14);
        Assert.AreEqual(684, frame.RemainingUnits);
        Assert.AreEqual(Start + 116, frame.Clocks.GameMs);
        Assert.IsTrue(frame.Step(684).Ok);
        frame.End();
        Clocks(owner, 101, Start + 800, Start + 800, 100);
        Assert.IsFalse(frame.IsOpen);
        // The frame is closed: a step is refused and moves nothing.
        Assert.AreEqual(FrameClockFailure.InvalidState, frame.Step(1).Failure);
        Clocks(owner, 101, Start + 800, Start + 800, 100);
    }

    [TestMethod]
    public void One_frame_is_open_at_a_time_and_a_step_has_a_valid_length()
    {
        using var owner = Create();
        Frame(owner, 0);
        var first = owner.BeginFrame(100).Frame!;
        Assert.AreEqual(FrameClockFailure.InvalidState, owner.BeginFrame(200).Failure, "a second frame while one is open");
        Assert.AreEqual(FrameClockFailure.InvalidDuration, first.Step(0).Failure);
        Assert.AreEqual(FrameClockFailure.InvalidDuration, first.Step(801).Failure);
        Clocks(owner, 1, Start, Start, 0);
        first.End();
        first.End();
        var second = owner.BeginFrame(200);
        Assert.AreEqual(FrameClockFailure.None, second.Failure);
        Assert.AreEqual(FrameClockFailure.InvalidState, first.Step(1).Failure, "an ended frame is not the open one");
        second.Frame!.End();
        // A refused admission opens no frame.
        Assert.AreEqual(FrameClockFailure.InvalidHostTime, owner.BeginFrame(-1).Failure);
        Assert.AreEqual(FrameClockFailure.None, owner.BeginFrame(300).Failure);
    }

    [TestMethod]
    public void The_pause_is_read_again_at_every_step_and_real_time_goes_on_behind_it()
    {
        using var owner = Create();
        Frame(owner, 0);
        var frame = owner.BeginFrame(100).Frame!;
        Assert.IsTrue(frame.Step(80).Ok);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        Assert.IsTrue(frame.Flags.Paused);
        var behind = frame.Step(80);
        Assert.AreEqual((true, 0L), (behind.Ok, behind.GameMs));
        Clocks(owner, 1 + 20, Start + 80, Start + 80, 10);
        frame.End();
    }

    [TestMethod]
    public void A_frame_names_the_units_to_a_deadline_by_the_flags_of_the_moment()
    {
        using var owner = Create();
        Frame(owner, 0);
        var frame = owner.BeginFrame(100).Frame!;
        Assert.AreEqual(10L, frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start + 10));
        Assert.AreEqual(12L, frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Animation, Start + 12));
        Assert.AreEqual((3 - 1) * 8L, frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 3));
        Assert.IsNull(frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start), "a deadline that is not ahead");
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(owner.QueryClockSnapshot().Token, new(true, PauseReason.Menu)).Disposition);
        Assert.IsNull(frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Game, Start + 10));
        Assert.IsNull(frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Animation, Start + 12));
        Assert.AreEqual(16L, frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 3));
        frame.End();
        Assert.IsNull(frame.UnitsUntil(Arcanum.Simulation.Events.GameEventDomain.Real, 3), "a closed frame names nothing");
    }

    [TestMethod]
    public void A_frozen_frame_is_empty_and_leaves_the_reference()
    {
        using var owner = Create();
        Frame(owner, 0);
        var frozen = owner.BeginFrame(5_000, frozen: true).Frame!;
        Assert.AreEqual((default(FrameAdmission), 0L), (frozen.Admission, frozen.TotalUnits));
        Assert.AreEqual(FrameClockFailure.InvalidDuration, frozen.Step(1).Failure);
        frozen.End();
        Assert.AreEqual(new FrameAdmission(250, 2_000, 2_000), Frame(owner, 9_000), "the next frame credits at most 250 ms");
    }
}
