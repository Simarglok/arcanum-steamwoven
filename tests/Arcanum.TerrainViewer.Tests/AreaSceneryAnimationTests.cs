using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaCompositionFixture;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-035: the scenery animation loop (ledger AS1–AS7) on synthetic scenery.</summary>
[TestClass]
public sealed class AreaSceneryAnimationTests
{
    private static readonly CellCoord Here = new(92928, 82560);

    // The host over a session: the loops' runs are stepped by the frame step on the animation clock, 20 ms at a time.
    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public CellCoord? Player = Here;
        public AreaSceneryAnimation Animation { get; }
        public AreaSceneryHost Host { get; }
        public FrameStep Step { get; }

        public List<CellCoord> Burned { get; } = [];
        public AreaFireBurner Fire { get; }

        public Fixture(AreaScenePlan plan, AreaObjectRecord row, int victims = 0)
        {
            Animation = AreaSceneryAnimation.Create(plan, [row]);
            Fire = new AreaFireBurner(Session, () => null, cell => { Burned.Add(cell); return Enumerable.Range(0, victims).Select(i => $"npc{i}").ToArray(); }, _ => null);
            Host = new AreaSceneryHost(Session, Animation, () => Player, Fire);
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, () => Host) };
        }

        public AreaSceneryLoop Loop => Animation.Loops.Single();
        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void A_near_loop_is_a_run_that_steps_one_frame_per_pause_and_wraps()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, plan.SceneryFrames[row.Key].Select(v => (int)((v.RawArt >> 14) & 31)).ToArray());
        using var f = new Fixture(plan, row);
        Assert.AreEqual(1, f.Host.Start());
        Assert.AreEqual(GoalType.AnimateLoop, f.Host.GoalOf(f.Loop.Entity));
        Assert.AreEqual(1, f.Host.Running);
        f.Run(100);
        Assert.AreEqual(0, f.Loop.Loop.Frame, "the run waits one pause after its begin");
        Assert.IsFalse(f.Host.TakeChanged());
        f.Run(100);
        Assert.AreEqual(1, f.Loop.Loop.Frame);
        Assert.IsTrue(f.Host.TakeChanged());
        Assert.IsFalse(f.Host.TakeChanged(), "taken once");
        f.Run(800);
        Assert.AreEqual(f.Loop.Loop.Steps % 4, f.Loop.Loop.Frame, "frames 2, 3, then 0 again");
        Assert.IsTrue(f.Loop.Loop.Steps is >= 7 and <= 8, $"one frame per 125 ms: {f.Loop.Loop.Steps}");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the loop never ends by itself");
    }

    [TestMethod]
    public void A_far_loop_holds_its_frame_and_measures_again_every_third_check()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Player = new CellCoord(Here.X + 30, Here.Y);
        f.Host.Start();
        // Begin: far. Check 1 (about 130 ms) and check 2 (about 930 ms) keep that answer; check 3 (about 1730 ms) measures again.
        f.Run(1600);
        Assert.AreEqual(0, f.Loop.Loop.Steps, "checks 1 and 2 reuse the begin's answer, even with the player back home");
        f.Player = Here;
        f.Run(100);
        Assert.AreEqual(0, f.Loop.Loop.Steps, "check 3 is not due yet");
        for (var waited = 0; f.Loop.Loop.Steps == 0 && waited < 500; waited += 20) f.Run(20);
        Assert.AreEqual(1, f.Loop.Loop.Steps, "check 3 measures: now near, it steps");
        // Near again: checks 4 and 5 step without measuring, even with the player gone; check 6 measures and holds.
        f.Player = new CellCoord(Here.X + 30, Here.Y);
        f.Run(300);
        Assert.AreEqual(3, f.Loop.Loop.Steps, "two more steps on the old answer");
        f.Run(1500);
        Assert.AreEqual(3, f.Loop.Loop.Steps, "the sixth check measured: far, it holds");
    }

    [TestMethod]
    public void Frame_deltas_move_the_object_offset()
    {
        var (plan, row) = Scenery(frames: 3, fps: 10, delta: (2, -1));
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Run(330);
        var loop = f.Loop;
        Assert.AreEqual((0, 4, -2), (loop.Loop.Frame, loop.Loop.OffsetX, loop.Loop.OffsetY), "frames 1 and 2 move it by their delta, frame 0 has none");
        var t0 = plan.Commands.Single(c => c.Identity == row.Key);
        var visual = plan.SceneryFrames[row.Key][0];
        var moved = AreaScenePlan.SceneryFrame(t0, row, visual, loop.Visual, loop.Loop.OffsetX, loop.Loop.OffsetY);
        Assert.AreEqual(new Arcanum.Data.Terrain.TerrainRect(t0.Destination.X + 4, t0.Destination.Y - 2, t0.Destination.Width, t0.Destination.Height), moved.Destination);
    }

    [TestMethod]
    public void A_burning_scenery_runs_goal_77_and_burns_at_its_begin()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8, sceneryFlags: AreaSceneryAnimation.BurningFlag);
        using var f = new Fixture(plan, row);
        Assert.IsTrue(f.Loop.Burning);
        f.Host.Start();
        Assert.AreEqual(GoalType.AnimateLoopFireDamage, f.Host.GoalOf(f.Loop.Entity));
        f.Run(40);
        Assert.AreEqual(1, f.Host.FireFrames, "the begin burns once at once");
        f.Run(400);
        Assert.IsTrue(f.Host.FireFrames >= 2, "frame 1 burns again");
    }

    [TestMethod]
    public void A_start_leaves_a_scenery_that_has_a_run_and_losing_the_loops_ends_the_runs()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        Assert.AreEqual(1, f.Host.Start());
        Assert.AreEqual(0, f.Host.Start(), "a scenery with a main run is not started twice");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        f.Host.LoseAll();
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        f.Run(500);
        Assert.AreEqual(0, f.Loop.Loop.Steps);
    }

    [TestMethod]
    public void A_run_of_a_scenery_the_view_left_or_with_no_host_ends()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        var run = f.Session.QueryGoals().RunsOf(f.Loop.Entity).Single();
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, () => null);
        Assert.AreEqual(SessionGoalStepAnswer.End, router.StepGoal(run, default));
        Assert.AreEqual(1, router.SceneriesLost);
        var stranger = new GoalRun("sector:99:1", default, Arcanum.Simulation.Ai.AiExecutorRules.SceneryFrame(false));
        Assert.AreEqual(SessionGoalStepAnswer.End, f.Host.StepGoal(stranger, default));
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
        Assert.AreEqual(0, AreaSceneryAnimation.Create(plan, [row]).Loops.Count, "a missing frame");
        (plan, row) = Scenery(frames: 4, fps: 8, sceneryFlags: 0x1);
        Assert.AreEqual(0, AreaSceneryAnimation.Create(plan, [row]).Loops.Count, "scenery flag 0x1");
        (plan, row) = Scenery(frames: 4, fps: 8);
        Assert.AreEqual(1, AreaSceneryAnimation.Create(plan, [row]).Loops.Count);
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

    [TestMethod]
    public void A_burning_scenery_burns_the_critters_on_its_own_tile_and_a_plain_loop_burns_nobody()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8, sceneryFlags: AreaSceneryAnimation.BurningFlag);
        using var f = new Fixture(plan, row, victims: 2);
        f.Host.Start();
        f.Run(40);
        Assert.AreEqual(1, f.Fire.Burns);
        Assert.AreEqual(0, f.Fire.Reached, "two critters stand on the tile, but with no cast host nothing reaches the damage path (and the round gate is not spent)");
        Assert.IsTrue(f.Burned.All(cell => cell.X == f.Loop.Loop.X && cell.Y == f.Loop.Loop.Y), "the scenery's own tile");
        var (plan2, row2) = Scenery(frames: 4, fps: 8);
        using var g = new Fixture(plan2, row2, victims: 2);
        g.Host.Start();
        g.Run(1000);
        Assert.AreEqual(0, g.Fire.Burns);
    }

    [TestMethod]
    public void A_lost_loop_run_stays_still_until_it_is_restarted_by_the_restart_rule()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Host.LoseAll();
        Assert.AreEqual(0, f.Host.Running);
        f.Run(500);
        Assert.AreEqual(0, f.Loop.Loop.Steps, "a table sweep ends the run and nothing restarts it by itself (GT98)");
        Assert.AreEqual(SceneryRestart.Start, f.Host.Restart(f.Loop.Entity, objectFlags: 0, sceneryFlags: 0));
        Assert.AreEqual(1, f.Host.Running);
        Assert.AreEqual(SceneryRestart.Skip, f.Host.Restart("sector:0:0", 0, 0), "not this host's scenery");
        Assert.AreEqual(SceneryRestart.Start, f.Host.Restart(f.Loop.Entity, 0, 0), "a scenery that has a main run keeps it");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(SceneryRestart.Kill, f.Host.Restart(f.Loop.Entity, 0, sceneryFlags: SceneryAnimationRules.NoAutoAnimate));
        Assert.AreEqual(0, f.Host.Running, "a scenery that must not animate any more has its run killed");
        Assert.AreEqual(SceneryRestart.Skip, f.Host.Restart(f.Loop.Entity, objectFlags: 0x2, sceneryFlags: 0));
        Assert.AreEqual(SceneryRestart.Start, f.Host.Restart(f.Loop.Entity, 0, AreaSceneryAnimation.BurningFlag));
        Assert.AreEqual(GoalType.AnimateLoopFireDamage, f.Host.GoalOf(f.Loop.Entity), "the goal follows the object's present flag 0x10");
    }
}
