using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-1397: the pump's per-frame event counts reach the bug report's log summed, not one line per frame.</summary>
[TestClass]
public sealed class SessionEventLogThrottleTests
{
    private static SessionEventLogTally? Quiet(SessionEventLogThrottle throttle, long now, int fired = 1) =>
        throttle.Offer(now, fired, 0, 0, 0, 0, 0, 0, notable: false);

    [TestMethod]
    public void A_quiet_minute_at_sixty_frames_a_second_logs_one_line_a_second()
    {
        var throttle = new SessionEventLogThrottle();
        var lines = 0; var fired = 0; var frames = 0;
        for (var frame = 0; frame < 3600; frame++)
            if (Quiet(throttle, 10_000 + frame * 1000L / 60) is { } tally) { lines++; fired += tally.Fired; frames += tally.Frames; }
        Assert.IsTrue(lines is >= 59 and <= 61, $"lines {lines}");
        Assert.IsTrue(fired <= 3600 && fired >= 3600 - 60, "the sums carry the frames, less the tail still held back");
        Assert.AreEqual(fired, frames);
    }

    [TestMethod]
    public void The_first_frame_logs_at_once()
    {
        var tally = Quiet(new SessionEventLogThrottle(), 5);
        Assert.AreEqual(new SessionEventLogTally(1, 1, 0, 0, 0, 0, 0, 0), tally);
    }

    [TestMethod]
    public void A_notable_frame_logs_at_once_with_the_held_quiet_frames_summed_in()
    {
        var throttle = new SessionEventLogThrottle();
        Assert.IsNotNull(Quiet(throttle, 0));
        Assert.IsNull(Quiet(throttle, 100));
        Assert.IsNull(Quiet(throttle, 200));
        var tally = throttle.Offer(300, 1, 2, 3, 2, 1, 0, 0, notable: true);
        Assert.AreEqual(new SessionEventLogTally(3, 3, 2, 3, 2, 1, 0, 0), tally, "two held frames plus the notable one, with its drop and wait");
        Assert.IsNull(Quiet(throttle, 400), "the interval restarts at the notable line");
        Assert.IsNotNull(Quiet(throttle, 1300));
    }

    [TestMethod]
    public void A_clock_that_moves_backward_logs_and_nothing_is_lost()
    {
        var throttle = new SessionEventLogThrottle();
        Assert.IsNotNull(Quiet(throttle, 50_000));
        Assert.IsNull(Quiet(throttle, 50_100));
        var tally = Quiet(throttle, 10);
        Assert.AreEqual(2, tally?.Frames);
        Assert.AreEqual(2, tally?.Fired);
    }
}
