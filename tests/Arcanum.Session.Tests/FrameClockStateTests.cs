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

    private static FrameAdmission Frame(GameSessionOwner owner, long hostMs, bool frozen = false)
    {
        var result = owner.AdvanceFrame(hostMs, frozen);
        Assert.AreEqual(FrameClockFailure.None, result.Failure);
        return result.Admission;
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
    public void A_frame_moves_real_and_animation_and_hands_the_game_part_to_the_caller()
    {
        using var owner = Create();
        Assert.AreEqual(default, Frame(owner, 1_000), "the first observation admits nothing");
        Assert.AreEqual(new FrameAdmission(100, 800, 800), Frame(owner, 1_100));
        Clocks(owner, 101, Start, Start + 800, 100);
        // The game part is the pump's to apply; an ordinary advance moves game time alone.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 800, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Clocks(owner, 101, Start + 800, Start + 800, 100);
        // Short frames add up, a long one is cut to 250 ms.
        Assert.AreEqual(default, Frame(owner, 1_103));
        Assert.AreEqual(new FrameAdmission(6, 48, 48), Frame(owner, 1_106));
        Assert.AreEqual(new FrameAdmission(250, 2_000, 2_000), Frame(owner, 9_000));
        Clocks(owner, 357, Start + 800, Start + 2_848, 356);
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
        Assert.AreEqual(FrameClockFailure.InvalidHostTime, owner.AdvanceFrame(-1).Failure);
        Clocks(owner, 351, Start, Start + 2_000, 250);
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
        Clocks(owner, 101, Start + 3_600_000, Start + 800 + 3_600_000, 100);
        // A zero jump is 1 ms; an explicit wait of the game clock is a jump too.
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 0).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 9, ClockAdvanceReason.ExplicitWait).Disposition);
        Clocks(owner, 101, Start + 3_600_010, Start + 800 + 3_600_010, 100);
        // Refusals change nothing.
        var token = owner.QueryClockSnapshot().Token;
        Assert.AreEqual(ClockFailure.InvalidDuration, owner.JumpClocks(token, -1).Failure);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.SetPaused(token, default).Disposition);
        Assert.AreEqual(ClockFailure.StaleToken, owner.JumpClocks(token, 1_000).Failure);
        Clocks(owner, 101, Start + 3_600_010, Start + 800 + 3_600_010, 100);
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
        Assert.AreEqual(FrameClockFailure.None, owner.AdvanceFrame(0).Failure);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, long.MaxValue - top).Disposition);
        Assert.AreEqual(long.MaxValue, owner.QueryFrameClocks().AnimationMs);
        Assert.AreEqual(FrameClockFailure.TimeOverflow, owner.AdvanceFrame(10).Failure);
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
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 300, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(new Arcanum.Simulation.Events.GameEventClocks(101, Start + 300, Start + 800), owner.QueryEventClocks());
    }

    [TestMethod]
    public void The_real_and_animation_stamps_round_trip_and_a_restore_credits_nothing_at_its_first_frame()
    {
        using var owner = Create();
        Frame(owner, 0);
        Frame(owner, 200);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 5_000).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, $"\"revision\":1,\"realMs\":201,\"animationMs\":{Start + 1_600 + 5_000}}}");
        using var restored = Restore(Decode(text));
        Clocks(restored, 201, Start + 5_000, Start + 6_600, 0);
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
        var older = current.Replace("\"schemaVersion\":70,\"world\"", "\"schemaVersion\":69,\"world\"")
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
    public void The_queue_schedules_and_fires_its_game_events_on_the_game_clock_whatever_real_and_animation_time_did()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(1, 0, null, null), 1_000).Disposition);
        Assert.AreEqual(Start + 1_000, owner.QueryEvents().Single().DeadlineMs);
        // Real and animation time move by frames; the game-domain call waits for game time.
        Frame(owner, 0);
        for (var host = 250L; host <= 2_500; host += 250) Frame(owner, host);
        Assert.AreEqual(0, owner.ProcessEvents(new NoWorld()).ScriptCalls.Count);
        Assert.AreEqual(1, owner.QueryEvents().Count);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 1_000).Disposition);
        Assert.AreEqual(1, owner.ProcessEvents(new NoWorld()).ScriptCalls.Count);
        Assert.AreEqual(0, owner.QueryEvents().Count);
    }
}
