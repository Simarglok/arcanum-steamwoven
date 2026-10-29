using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaCompositionFixture;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-035: the scenery animation loop (ledger AS1–AS7) on synthetic scenery.</summary>
[TestClass]
public sealed class AreaSceneryAnimationTests
{
    private static readonly CellCoord Here = new(92928, 82560);

    [TestMethod]
    public void A_near_loop_steps_one_frame_per_pause_and_wraps()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, plan.SceneryFrames[row.Key].Select(v => (int)((v.RawArt >> 14) & 31)).ToArray());
        var animation = AreaSceneryAnimation.Create(plan, [row], 1000, Here);
        var loop = animation.Loops.Single();
        Assert.AreEqual(0, loop.Loop.Frame);
        Assert.IsFalse(animation.Advance(1124, Here), "the run waits one pause after its begin");
        Assert.IsTrue(animation.Advance(1125, Here));
        Assert.AreEqual(1, loop.Loop.Frame);
        animation.Advance(1500, Here);
        Assert.AreEqual(0, loop.Loop.Frame, "frames 2, 3, then 0 again");
        Assert.AreEqual(4, loop.Loop.Steps);
    }

    [TestMethod]
    public void A_far_loop_holds_its_frame_and_measures_again_every_third_check()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        var far = new CellCoord(Here.X + 30, Here.Y);
        var animation = AreaSceneryAnimation.Create(plan, [row], 0, far);
        var loop = animation.Loops.Single();
        // Begin: far. Check 1 at 125 and check 2 at 925 keep that answer; check 3 at 1725 measures again.
        Assert.IsFalse(animation.Advance(925, Here), "checks 1 and 2 reuse the begin's answer");
        Assert.AreEqual(0, loop.Loop.Frame);
        Assert.IsTrue(animation.Advance(1725, Here), "check 3 measures: now near, it steps");
        Assert.AreEqual(1, loop.Loop.Frame);
        // Near again: checks 1 and 2 of the next count step without measuring, even with the PC gone.
        Assert.IsTrue(animation.Advance(1975, far));
        Assert.AreEqual(3, loop.Loop.Frame);
        Assert.IsFalse(animation.Advance(2100, far), "check 3 measures: far, it holds");
        Assert.AreEqual(3, loop.Loop.Frame);
    }

    [TestMethod]
    public void Frame_deltas_move_the_object_offset()
    {
        var (plan, row) = Scenery(frames: 3, fps: 10, delta: (2, -1));
        var animation = AreaSceneryAnimation.Create(plan, [row], 0, Here);
        animation.Advance(300, Here);
        var loop = animation.Loops.Single();
        Assert.AreEqual((0, 4, -2), (loop.Loop.Frame, loop.Loop.OffsetX, loop.Loop.OffsetY), "frames 1 and 2 move it by their delta, frame 0 has none");
        var t0 = plan.Commands.Single(c => c.Identity == row.Key);
        var visual = plan.SceneryFrames[row.Key][0];
        var moved = AreaScenePlan.SceneryFrame(t0, row, visual, loop.Visual, loop.Loop.OffsetX, loop.Loop.OffsetY);
        Assert.AreEqual(new Arcanum.Data.Terrain.TerrainRect(t0.Destination.X + 4, t0.Destination.Y - 2, t0.Destination.Width, t0.Destination.Height), moved.Destination);
    }

    [TestMethod]
    public void Another_frame_is_placed_by_its_own_size_and_hot_spot()
    {
        var (plan, row) = Scenery(frames: 2, fps: 5, second: (30, 50, 12, 40));
        var t0 = plan.Commands.Single(c => c.Identity == row.Key);
        var frames = plan.SceneryFrames[row.Key];
        var drawn = AreaScenePlan.SceneryFrame(t0, row, frames[0], frames[1], 0, 0);
        // T0 hot spot (20, 60), frame 1 hot spot (12, 40): the frame's corner moves by the difference.
        Assert.AreEqual(new Arcanum.Data.Terrain.TerrainRect(t0.Destination.X + 8, t0.Destination.Y + 20, 30, 50), drawn.Destination);
        Assert.AreEqual(t0.Visual, drawn.Visual, "the visual key stays the body's");
        Assert.AreEqual(AreaFrameKey.From(frames[1]), drawn.Frame);
        Assert.Throws<ArgumentException>(() => AreaScenePlan.SceneryFrame(t0, row with { Type = 5 }, frames[0], frames[1], 0, 0));
    }

    [TestMethod]
    public void No_loop_without_every_frame_or_with_no_auto_animation()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8, decoded: 3);
        Assert.AreEqual(0, AreaSceneryAnimation.Create(plan, [row], 0, Here).Loops.Count, "a missing frame");
        (plan, row) = Scenery(frames: 4, fps: 8, sceneryFlags: 0x1);
        Assert.AreEqual(0, AreaSceneryAnimation.Create(plan, [row], 0, Here).Loops.Count, "scenery flag 0x1");
        (plan, row) = Scenery(frames: 4, fps: 8);
        Assert.AreEqual(1, AreaSceneryAnimation.Create(plan, [row], 0, Here).Loops.Count);
        Assert.AreEqual(1, plan.Frames.Count(f => f.Frame == 3), "the plan's frames include every loop frame");
    }

    private static (AreaScenePlan Plan, AreaObjectRecord Row) Scenery(int frames, int fps, (int X, int Y) delta = default,
        (int W, int H, int HotX, int HotY)? second = null, int? decoded = null, uint sceneryFlags = 0)
    {
        var row = Body(0, scenery: sceneryFlags);
        var body = Visual(row, 40, 70, 20, 60) with { ArtOffsetX = 0, ArtOffsetY = 0, FrameCount = frames, FramesPerSecond = fps };
        var visuals = new List<AreaVisualRecord> { body };
        for (var frame = 1; frame < (decoded ?? frames); frame++)
        {
            var size = frame == 1 && second is { } s ? s : (40, 70, 20, 60);
            visuals.Add(body with
            {
                Key = body.Key with { Layer = AreaLayerKind.SceneryFrame, SparseIndex = frame },
                RawArt = body.RawArt | ((uint)frame << 14), LookupArt = body.LookupArt | ((uint)frame << 14), Frame = frame,
                Width = size.Item1, Height = size.Item2, HotX = size.Item3, HotY = size.Item4, ArtOffsetX = delta.X, ArtOffsetY = delta.Y,
            });
        }
        // The T0 frame's own delta is not applied at load (the begin does not step).
        return (AreaScenePlan.Create([], [row], visuals), row);
    }
}
