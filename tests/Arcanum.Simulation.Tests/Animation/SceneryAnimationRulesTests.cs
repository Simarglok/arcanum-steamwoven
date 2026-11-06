using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Animation;

/// <summary>ARC-RENDER-035: the scenery animation rules (ledger AS1–AS6).</summary>
[TestClass]
public sealed class SceneryAnimationRulesTests
{
    [TestMethod]
    public void The_start_needs_a_visible_auto_animated_unstoned_scenery_of_several_frames()
    {
        Assert.IsTrue(SceneryAnimationRules.Starts(3, 0, 0x10, 0, 2), "a fire (scenery flag 0x10) loops too (goal 77)");
        Assert.IsFalse(SceneryAnimationRules.Starts(3, 0, 0, 0, 1), "one frame: nothing to draw");
        Assert.IsFalse(SceneryAnimationRules.Starts(3, 0, 0x1, 0, 8), "no auto-animation");
        Assert.IsFalse(SceneryAnimationRules.Starts(3, 0x1, 0, 0, 8), "destroyed");
        Assert.IsFalse(SceneryAnimationRules.Starts(3, 0x2, 0, 0, 8), "off");
        Assert.IsFalse(SceneryAnimationRules.Starts(3, 0, 0, 0x10000, 8), "stoned");
        Assert.IsFalse(SceneryAnimationRules.Starts(5, 0, 0, 0, 8), "not scenery");
    }

    [TestMethod]
    public void The_pause_is_1000_over_the_rate_and_the_frame_wraps_to_zero()
    {
        Assert.AreEqual(125, SceneryAnimationRules.PauseMs(8));
        Assert.AreEqual(333, SceneryAnimationRules.PauseMs(3), "unsigned division truncates");
        Assert.AreEqual(100, SceneryAnimationRules.PauseMs(0), "an unread rate takes the unread ART's pause");
        Assert.AreEqual(1, SceneryAnimationRules.PauseMs(2000), "a rate above 1000 waits 1 ms, not 0");
        Assert.AreEqual(1, SceneryAnimationRules.NextFrame(0, 4));
        Assert.AreEqual(0, SceneryAnimationRules.NextFrame(3, 4));
        Assert.AreEqual(0, SceneryAnimationRules.NextFrame(9, 4), "a frame past the count goes to 0");
        Assert.IsTrue(SceneryAnimationRules.Near(100, 100, new(129, 71)), "29 tiles (Chebyshev)");
        Assert.IsFalse(SceneryAnimationRules.Near(100, 100, new(130, 100)), "30 tiles");
    }

    [TestMethod]
    public void A_loop_steps_and_applies_each_new_frames_delta()
    {
        var loop = new SceneryLoop(0, 0, [(0, 0), (1, 2), (0, 0)], 1, 100);
        Assert.AreEqual(2, loop.Step());
        Assert.AreEqual((2, 0, 0), (loop.Frame, loop.OffsetX, loop.OffsetY), "frame 2 has no delta");
        Assert.AreEqual(0, loop.Step(), "the frame after the last is 0");
        Assert.AreEqual(1, loop.Step());
        Assert.AreEqual((1, 1, 2, 3L), (loop.Frame, loop.OffsetX, loop.OffsetY, loop.Steps), "frames 0 and 1: only 1 moves it");
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneryLoop(0, 0, [(0, 0)], 0, 100));
    }
}
