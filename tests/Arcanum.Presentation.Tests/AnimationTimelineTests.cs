using Arcanum.Presentation.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

[TestClass]
public sealed class AnimationTimelineTests
{
    [TestMethod]
    public void New_and_reset_assets_are_paused_at_frame_zero_and_preserve_controls()
    {
        var timeline = new AnimationTimeline(4, 12);
        timeline.SetMode(PlaybackMode.Once);
        timeline.SetSpeed(PlaybackSpeed.Double);
        timeline.SetManualFramesPerSecond(20);
        timeline.Play();
        timeline.Advance(0.1d);

        timeline.ResetAsset(3, -8);

        Assert.AreEqual(3, timeline.FrameCount);
        Assert.AreEqual(-8, timeline.SourceFramesPerSecond);
        Assert.AreEqual(0, timeline.CurrentFrame);
        Assert.IsFalse(timeline.IsPlaying);
        Assert.IsFalse(timeline.IsCompleted);
        Assert.AreEqual(PlaybackMode.Once, timeline.Mode);
        Assert.AreEqual(PlaybackSpeed.Double, timeline.Speed);
        Assert.AreEqual(20, timeline.ManualFramesPerSecond);
        Assert.AreEqual(40d, timeline.EffectiveFramesPerSecond);
    }

    [TestMethod]
    public void Loop_uses_subframe_exact_and_multiple_boundaries()
    {
        var timeline = new AnimationTimeline(4, 10);
        Assert.IsTrue(timeline.Play());

        AssertResult(timeline.Advance(0.09d), TimelineAdvanceStatus.NoChange, 0);
        AssertResult(timeline.Advance(0.01d), TimelineAdvanceStatus.FrameChanged, 1);
        AssertResult(timeline.Advance(0.2d), TimelineAdvanceStatus.FrameChanged, 3);
        AssertResult(timeline.Advance(0.1d), TimelineAdvanceStatus.FrameChanged, 0);
    }

    [TestMethod]
    public void Loop_accumulates_fractional_irregular_time_and_reduces_multi_cycle_stalls()
    {
        var timeline = new AnimationTimeline(5, 4);
        timeline.Play();

        AssertResult(timeline.Advance(0.12d), TimelineAdvanceStatus.NoChange, 0);
        AssertResult(timeline.Advance(0.13d), TimelineAdvanceStatus.FrameChanged, 1);
        AssertResult(timeline.Advance(5d), TimelineAdvanceStatus.NoChange, 1);
        AssertResult(timeline.Advance(5.375d), TimelineAdvanceStatus.FrameChanged, 2);
        var hugeStall = timeline.Advance(double.MaxValue);
        Assert.IsTrue(hugeStall.Status is TimelineAdvanceStatus.NoChange or TimelineAdvanceStatus.FrameChanged);
        Assert.IsTrue(hugeStall.CurrentFrame is >= 0 and < 5);
    }

    [TestMethod]
    public void Once_gives_the_last_frame_its_full_interval_then_completes_and_replays_from_zero()
    {
        var timeline = new AnimationTimeline(3, 10);
        timeline.SetMode(PlaybackMode.Once);
        timeline.Play();

        AssertResult(timeline.Advance(0.1d), TimelineAdvanceStatus.FrameChanged, 1);
        AssertResult(timeline.Advance(0.1d), TimelineAdvanceStatus.FrameChanged, 2);
        Assert.IsTrue(timeline.IsPlaying);
        AssertResult(timeline.Advance(0.099d), TimelineAdvanceStatus.NoChange, 2);
        AssertResult(timeline.Advance(0.001d), TimelineAdvanceStatus.Completed, 2);
        Assert.IsFalse(timeline.IsPlaying);
        Assert.IsTrue(timeline.IsCompleted);

        Assert.IsTrue(timeline.Play());
        Assert.AreEqual(0, timeline.CurrentFrame);
        Assert.IsFalse(timeline.IsCompleted);
    }

    [TestMethod]
    public void Once_directly_completes_large_stalls_without_frame_by_frame_catch_up()
    {
        var timeline = new AnimationTimeline(8, 1);
        timeline.SetMode(PlaybackMode.Once);
        timeline.Play();

        AssertResult(timeline.Advance(double.MaxValue), TimelineAdvanceStatus.Completed, 7);
        Assert.IsTrue(timeline.IsCompleted);
        Assert.IsFalse(timeline.IsPlaying);
    }

    [TestMethod]
    public void Pause_resume_preserves_partial_interval_without_hidden_backlog()
    {
        var timeline = new AnimationTimeline(3, 10);
        timeline.Play();
        timeline.Advance(0.05d);
        timeline.Pause();

        AssertResult(timeline.Advance(100d), TimelineAdvanceStatus.NoChange, 0);
        Assert.IsTrue(timeline.Play());
        AssertResult(timeline.Advance(0.04d), TimelineAdvanceStatus.NoChange, 0);
        AssertResult(timeline.Advance(0.01d), TimelineAdvanceStatus.FrameChanged, 1);
    }

    [TestMethod]
    public void Stop_seek_and_steps_pause_and_clear_completion_with_expected_bounds()
    {
        var timeline = new AnimationTimeline(3, 10);
        timeline.SetMode(PlaybackMode.Once);
        timeline.Play();
        timeline.Advance(0.3d);
        Assert.IsTrue(timeline.IsCompleted);

        timeline.Stop();
        Assert.AreEqual(0, timeline.CurrentFrame);
        Assert.IsFalse(timeline.IsPlaying);
        Assert.IsFalse(timeline.IsCompleted);

        timeline.Seek(1);
        timeline.Step(100);
        Assert.AreEqual(2, timeline.CurrentFrame);
        timeline.Step(-100);
        Assert.AreEqual(0, timeline.CurrentFrame);
        Assert.IsFalse(timeline.IsPlaying);
        Assert.IsFalse(timeline.IsCompleted);
        Throws<ArgumentOutOfRangeException>(() => timeline.Seek(-1));
        Throws<ArgumentOutOfRangeException>(() => timeline.Seek(3));
    }

    [TestMethod]
    public void Rate_changes_restart_the_interval_and_expose_exact_speed_choices()
    {
        var timeline = new AnimationTimeline(4, 20);
        timeline.Play();
        timeline.Advance(0.025d);
        timeline.SetSpeed(PlaybackSpeed.Half);

        Assert.AreEqual(0.5d, timeline.SpeedMultiplier);
        Assert.AreEqual(10d, timeline.EffectiveFramesPerSecond);
        AssertResult(timeline.Advance(0.05d), TimelineAdvanceStatus.NoChange, 0);
        AssertResult(timeline.Advance(0.05d), TimelineAdvanceStatus.FrameChanged, 1);

        Assert.AreEqual(0.25d, PlaybackSpeed.Quarter.ToMultiplier());
        Assert.AreEqual(1d, PlaybackSpeed.Normal.ToMultiplier());
        Assert.AreEqual(2d, PlaybackSpeed.Double.ToMultiplier());
        Throws<ArgumentOutOfRangeException>(() => timeline.SetSpeed((PlaybackSpeed)999));
    }

    [TestMethod]
    public void Every_supported_speed_advances_at_its_effective_rate()
    {
        foreach (var (speed, effectiveFramesPerSecond) in new[]
        {
            (PlaybackSpeed.Quarter, 2d),
            (PlaybackSpeed.Half, 4d),
            (PlaybackSpeed.Normal, 8d),
            (PlaybackSpeed.Double, 16d),
        })
        {
            var timeline = new AnimationTimeline(2, 8);
            timeline.SetSpeed(speed);
            Assert.AreEqual(effectiveFramesPerSecond, timeline.EffectiveFramesPerSecond);
            Assert.IsTrue(timeline.Play());
            AssertResult(timeline.Advance(1d / effectiveFramesPerSecond), TimelineAdvanceStatus.FrameChanged, 1);
        }
    }

    [TestMethod]
    public void Invalid_source_rate_blocks_playback_until_an_explicit_valid_override_is_set()
    {
        var timeline = new AnimationTimeline(3, 0);

        Assert.IsFalse(timeline.CanPlay);
        Assert.IsNull(timeline.EffectiveFramesPerSecond);
        Assert.IsFalse(timeline.Play());
        AssertResult(timeline.Advance(1d), TimelineAdvanceStatus.NoChange, 0);
        Throws<ArgumentOutOfRangeException>(() => timeline.SetManualFramesPerSecond(121));

        timeline.SetManualFramesPerSecond(12);
        Assert.IsTrue(timeline.CanPlay);
        Assert.AreEqual(12, timeline.ManualFramesPerSecond);
        Assert.AreEqual(12d, timeline.EffectiveFramesPerSecond);
        Assert.IsTrue(timeline.Play());
        AssertResult(timeline.Advance(1d / 12d), TimelineAdvanceStatus.FrameChanged, 1);

        timeline.SetManualFramesPerSecond(null);
        Assert.IsFalse(timeline.CanPlay);
        Assert.IsFalse(timeline.IsPlaying);
        Assert.IsNull(timeline.EffectiveFramesPerSecond);
    }

    [TestMethod]
    public void Invalid_and_zero_deltas_do_not_mutate_state()
    {
        var timeline = new AnimationTimeline(3, 10);
        timeline.Play();
        timeline.Advance(0.05d);

        AssertResult(timeline.Advance(double.NaN), TimelineAdvanceStatus.InvalidDelta, 0);
        AssertResult(timeline.Advance(double.PositiveInfinity), TimelineAdvanceStatus.InvalidDelta, 0);
        AssertResult(timeline.Advance(-1d), TimelineAdvanceStatus.InvalidDelta, 0);
        AssertResult(timeline.Advance(0d), TimelineAdvanceStatus.NoChange, 0);
        AssertResult(timeline.Advance(0.05d), TimelineAdvanceStatus.FrameChanged, 1);
    }

    [TestMethod]
    public void Reconfigure_sequence_clamps_frame_preserves_play_state_and_starts_a_new_interval()
    {
        var timeline = new AnimationTimeline(5, 10);
        timeline.Seek(4);
        timeline.Play();
        timeline.Advance(0.05d);

        timeline.ReconfigureSequence(3);

        Assert.AreEqual(2, timeline.CurrentFrame);
        Assert.IsTrue(timeline.IsPlaying);
        Assert.IsFalse(timeline.IsCompleted);
        AssertResult(timeline.Advance(0.05d), TimelineAdvanceStatus.NoChange, 2);
        AssertResult(timeline.Advance(0.05d), TimelineAdvanceStatus.FrameChanged, 0);
    }

    [TestMethod]
    public void Single_frame_sequences_are_immutable_and_never_start_playback()
    {
        var timeline = new AnimationTimeline(1, 10);

        Assert.IsFalse(timeline.CanPlay);
        Assert.IsFalse(timeline.Play());
        AssertResult(timeline.Advance(10d), TimelineAdvanceStatus.NoChange, 0);
        timeline.Step(1);
        Assert.AreEqual(0, timeline.CurrentFrame);
        timeline.Seek(0);
        Assert.AreEqual(0, timeline.CurrentFrame);
    }

    [TestMethod]
    public void Input_and_control_validation_is_explicit()
    {
        Throws<ArgumentOutOfRangeException>(() => new AnimationTimeline(0, 1));
        var timeline = new AnimationTimeline(2, 1);
        Throws<ArgumentOutOfRangeException>(() => timeline.ResetAsset(-1, 1));
        Throws<ArgumentOutOfRangeException>(() => timeline.ReconfigureSequence(0));
        Throws<ArgumentOutOfRangeException>(() => timeline.SetMode((PlaybackMode)999));
        Throws<ArgumentOutOfRangeException>(() => ((PlaybackSpeed)999).ToMultiplier());
    }

    private static void AssertResult(TimelineAdvanceResult actual, TimelineAdvanceStatus expectedStatus, int expectedFrame)
    {
        Assert.AreEqual(expectedStatus, actual.Status);
        Assert.AreEqual(expectedFrame, actual.CurrentFrame);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception.GetType() == typeof(TException))
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }
}
