using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Ai;
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

    // ARC-RENDER-1781 (AN6, AN10-AN13): the frames a changed art has (their deltas and the frame count are what the loop reads).
    private static IReadOnlyList<AreaVisualRecord> NewArt(AreaObjectRecord row, int frames, (int X, int Y) delta = default)
    {
        var body = Visual(row, 24, 30, 12, 28) with { ArtOffsetX = 0, ArtOffsetY = 0, FrameCount = frames };
        return Enumerable.Range(0, frames).Select(i => body with { Frame = i, RawArt = body.RawArt | ((uint)i << 14), ArtOffsetX = i == 0 ? 0 : delta.X, ArtOffsetY = i == 0 ? 0 : delta.Y }).ToArray();
    }

    [TestMethod]
    public void A_still_scenery_that_takes_an_art_of_several_frames_gets_the_loop_of_that_art()
    {
        var (plan, row) = Scenery(frames: 1, fps: 8);
        using var f = new Fixture(plan, row);
        Assert.AreEqual(0, f.Animation.Loops.Count, "a one-frame art starts no loop");
        Assert.AreEqual(SceneryRestart.Start, f.Host.Reframe(row, NewArt(row, 4), framesPerSecond: 8));
        var loop = f.Animation.Loops.Single();
        Assert.AreEqual(4, loop.Frames.Count);
        Assert.AreEqual(125, loop.Loop.PauseMs, "a run started now reads the new art's rate");
        Assert.AreEqual(GoalType.AnimateLoop, f.Host.GoalOf(loop.Entity));
        Assert.AreEqual((0, 0, 0), f.Host.Pose(loop.Entity), "the action set frame 0");
        f.Run(1000);
        Assert.IsTrue(loop.Loop.Steps is >= 7 and <= 8, $"one frame per 125 ms: {loop.Loop.Steps}");
        Assert.AreEqual(loop.Loop.Steps % 4, loop.Loop.Frame, "it wraps over the new art's four frames");
        Assert.AreEqual((loop.Loop.Frame, 0, 0), f.Host.Pose(loop.Entity));
        Assert.AreEqual(0, f.Host.Start(), "the start leaves a scenery that has its run");
    }

    [TestMethod]
    public void A_running_loop_follows_the_new_art_with_its_run_and_the_delay_it_began_with()
    {
        var (plan, row) = Scenery(frames: 2, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Run(500);
        var handle = f.Session.QueryGoals().MainRunOf(f.Loop.Entity)!.Handle;
        Assert.AreEqual(SceneryRestart.Start, f.Host.Reframe(row, NewArt(row, 5), framesPerSecond: 20));
        var loop = f.Animation.Loops.Single();
        Assert.AreEqual(5, loop.Frames.Count);
        Assert.AreEqual(0, loop.Loop.Frame, "the frame is 0 after the change");
        Assert.AreEqual(125, loop.Loop.PauseMs, "the begin node stored the old art's delay; the starter does not run again while the run exists (AS2)");
        Assert.AreEqual(handle, f.Session.QueryGoals().MainRunOf(loop.Entity)!.Handle, "the same run");
        f.Run(1000);
        Assert.AreEqual(loop.Loop.Steps % 5, loop.Loop.Frame, "the next frame wraps at the new art's frame count (0x502C40)");
        Assert.IsTrue(loop.Loop.Frame != 0 || loop.Loop.Steps % 5 == 0);
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void An_art_of_one_frame_kills_the_loop_and_leaves_the_object_where_the_loop_moved_it()
    {
        var (plan, row) = Scenery(frames: 3, fps: 10, delta: (2, -1));
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Run(130);
        var entity = f.Loop.Entity;
        var moved = f.Host.Pose(entity)!.Value;
        Assert.AreEqual((1, 2, -1), moved);
        Assert.AreEqual(SceneryRestart.Kill, f.Host.Reframe(row, NewArt(row, 1), framesPerSecond: 8));
        Assert.AreEqual(0, f.Animation.Loops.Count, "the old art's loop stops");
        Assert.AreEqual(0, f.Host.Running);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual((0, 2, -1), f.Host.Pose(entity), "frame 0 of the one-frame art, the object offset kept (field 3 and 4 of the object)");
        f.Run(500);
        Assert.AreEqual((0, 2, -1), f.Host.Pose(entity), "still");
        // A later art of several frames starts from that offset.
        Assert.AreEqual(SceneryRestart.Start, f.Host.Reframe(row, NewArt(row, 3, (1, 1)), framesPerSecond: 10));
        Assert.AreEqual((2, -1), (f.Animation.Loops.Single().Loop.OffsetX, f.Animation.Loops.Single().Loop.OffsetY));
        Assert.AreEqual(100, f.Animation.Loops.Single().Loop.PauseMs);
    }

    [TestMethod]
    public void The_art_writers_restart_rule_decides_flags_not_the_art()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8, sceneryFlags: SceneryAnimationRules.NoAutoAnimate);
        using var f = new Fixture(plan, row);
        Assert.AreEqual(SceneryRestart.Kill, f.Host.Reframe(row, NewArt(row, 4), 8), "scenery flag 0x1: no auto-animation");
        Assert.AreEqual(0, f.Animation.Loops.Count);
        Assert.AreEqual(0, f.Host.Running);
        var (plan2, row2) = Scenery(frames: 4, fps: 8);
        using var g = new Fixture(plan2, row2);
        g.Host.Start();
        var destroyed = Body(0, flags: 0x2);
        Assert.AreEqual(SceneryRestart.Skip, g.Host.Reframe(destroyed, NewArt(destroyed, 1), 8), "an object that is off is left as it is");
        Assert.AreEqual(1, g.Animation.Loops.Count);
        Assert.AreEqual(SceneryRestart.Skip, g.Host.Reframe(row2, [], 8), "no frames: nothing to follow");
    }

    [TestMethod]
    public void A_stoned_scenery_gets_no_new_loop_and_a_killed_loop_is_reported_for_its_voice()
    {
        var (plan, row) = Scenery(frames: 1, fps: 8);
        using var f = new Fixture(plan, row);
        var stoned = Body(0, scenery: 0) with { EffectiveFields = [.. row.EffectiveFields, Field(19, SceneryAnimationRules.Stoned)] };
        Assert.AreEqual(SceneryRestart.Start, f.Host.Reframe(stoned, NewArt(stoned, 4), 8));
        Assert.AreEqual(0, f.Animation.Loops.Count, "the begin node refuses a stoned object (AS3)");
        Assert.AreEqual(0, f.Host.Running);
        var (plan2, row2) = Scenery(frames: 4, fps: 8);
        using var g = new Fixture(plan2, row2);
        g.Host.Start();
        Assert.AreEqual(0, g.Host.TakeKilledByArt().Count);
        g.Host.Reframe(row2, NewArt(row2, 1), 8);
        CollectionAssert.AreEqual(new[] { AreaUseScriptHost.EntityId(row2) }, g.Host.TakeKilledByArt().ToArray());
        Assert.AreEqual(0, g.Host.TakeKilledByArt().Count, "taken once");
    }

    [TestMethod]
    public void The_begin_node_of_each_loop_is_reported_once_for_its_looping_sound()
    {
        // ARC-RENDER-1126 (0x42DA50): the begin node is where the original starts the scenery's looping sound; the host reports the begin and the viewer decides.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        Assert.AreEqual(0, f.Host.TakeSoundBegins().Count, "nothing began before the start");
        f.Host.Start();
        f.Run(40);
        CollectionAssert.AreEqual(new[] { f.Loop.Entity }, f.Host.TakeSoundBegins().ToArray());
        Assert.AreEqual(0, f.Host.TakeSoundBegins().Count, "taken once");
        f.Run(1000);
        Assert.AreEqual(0, f.Host.TakeSoundBegins().Count, "the run's frames and distance checks are no begin");
    }
    [TestMethod]
    public void A_destroyed_scenery_loses_its_loop_and_every_other_run_of_the_object()
    {
        // ARC-SCRIPT-2514 (BS3c, 0x43CCA0 -> 0x423FF0): the destroy kills every goal run of the object, a loop and an overlay alike; the loop never restarts.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        var eye = new AreaEyeCandyHost(f.Session, _ => null, (art, parts) => new EyeCandyFacts(4, 2, 100, 0, true, true), (low, high) => low);
        f.Host.Start();
        f.Run(300);
        var entity = f.Loop.Entity;
        Assert.IsTrue(eye.StartHover(entity), "the hover overlay runs beside the loop");
        Assert.AreEqual(2, f.Session.QueryGoals().RunsOf(entity).Count(r => !r.Done));
        Assert.IsTrue(f.Host.ObjectDestroyed(entity));
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(entity).Count(r => !r.Done), "the loop and the hover are both gone");
        Assert.AreEqual(0, f.Animation.Loops.Count);
        Assert.AreEqual(0, f.Host.Running);
        Assert.IsTrue(f.Host.TakeChanged(), "the body is drawn again");
        CollectionAssert.AreEqual(new[] { entity }, f.Host.TakeKilledByArt().ToArray(), "the viewer stops the looping voice");
    }

    [TestMethod]
    public void A_destroyed_scenery_does_not_start_again_and_an_unknown_object_is_left_alone()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        var entity = f.Loop.Entity;
        f.Host.Start();
        f.Run(200);
        Assert.IsTrue(f.Host.ObjectDestroyed(entity));
        Assert.AreEqual(0, f.Host.Start(), "a sweep's start finds no loop for the destroyed object");
        f.Run(500);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "nothing steps for it");
        Assert.IsFalse(f.Host.ObjectDestroyed(entity), "a second destroy has nothing to end");
        Assert.IsFalse(f.Host.ObjectDestroyed("not-in-this-host"));
    }
    [TestMethod]
    public void A_destroy_also_ends_the_overlay_runs_owned_by_the_objects_cast_id()
    {
        // ARC-SCRIPT-2514: the overlays of a script or a spell are owned by the object's id on the spell side, which differs from the entity for a mobile row.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        var eye = new AreaEyeCandyHost(f.Session, _ => null, (art, parts) => new EyeCandyFacts(4, 2, 100, 0, true, true), (low, high) => low);
        f.Host.Start();
        const string cast = "row:Mobile:7";
        var entity = f.Loop.Entity;
        Assert.IsTrue(eye.StartHover(cast));
        Assert.IsTrue(f.Host.ObjectDestroyed(entity, cast));
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(cast).Count(r => !r.Done), "the overlay owned by the cast id ended");
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(entity).Count(r => !r.Done));
    }

    [TestMethod]
    public void A_break_interrupts_the_loop_at_level_five_and_leaves_the_overlay_running()
    {
        // ARC-SCRIPT-2646 (OD7, 0x43F1C0 -> 0x424070 at level 5): goal 1 is priority 1 and ends, the hover (a no-interrupt goal) stays; the loop is no run until the object is started again.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        var eye = new AreaEyeCandyHost(f.Session, _ => null, (art, parts) => new EyeCandyFacts(4, 2, 100, 0, true, true), (low, high) => low);
        f.Host.Start();
        f.Run(300);
        var entity = f.Loop.Entity;
        Assert.IsTrue(eye.StartHover(entity));
        Assert.IsTrue(f.Host.ObjectBroken(entity));
        var left = f.Session.QueryGoals().RunsOf(entity).Where(r => !r.Done).ToArray();
        Assert.AreEqual(1, left.Length, "only the overlay is left");
        Assert.AreEqual(GoalType.Floating, left[0].Top.Goal);
        Assert.AreEqual(1, f.Animation.Loops.Count, "the scenery keeps its loop and the frame it shows");
        CollectionAssert.AreEqual(new[] { entity }, f.Host.TakeKilledByArt().ToArray(), "the end node stops the looping voice");
        Assert.IsTrue(f.Host.TakeChanged());
        Assert.IsFalse(f.Host.ObjectBroken(entity), "a second break has no loop to end");
    }

    [TestMethod]
    public void A_broken_loop_waits_for_the_object_to_come_back_and_a_sweep_leaves_it()
    {
        // ARC-SCRIPT-2646 (0x43F5F0 -> 0x43D280 -> 0x43F710): a busted scenery stays still (a sweep's start skips it); a respawned one gets its loop back.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Run(200);
        var entity = f.Loop.Entity;
        Assert.IsTrue(f.Host.ObjectBroken(entity));
        Assert.AreEqual(0, f.Host.Start(), "the sweep starts nothing for the broken loop");
        Assert.AreEqual(0, f.Host.RestartBroken(_ => false), "still busted or off: it waits");
        Assert.AreEqual(0, f.Host.Running);
        Assert.AreEqual(1, f.Host.RestartBroken(e => e == entity), "switched on again: the loop starts");
        Assert.AreEqual(1, f.Host.Running);
        Assert.AreEqual(0, f.Host.RestartBroken(_ => true), "the mark is gone");
    }

    [TestMethod]
    public void A_destroy_after_the_break_leaves_nothing_to_restart()
    {
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        var entity = f.Loop.Entity;
        Assert.IsTrue(f.Host.ObjectBroken(entity));
        Assert.IsTrue(f.Host.ObjectDestroyed(entity), "the destroy that follows gives the loop up");
        Assert.AreEqual(0, f.Host.RestartBroken(_ => true), "a destroyed object has no loop to start");
        Assert.AreEqual(0, f.Host.Running);
    }
    [TestMethod]
    public void A_busted_art_of_several_frames_starts_the_interrupted_loop_again_at_once()
    {
        // ARC-SCRIPT-2670 (0x43EB30 -> 0x43F710 -> 0x433580): the break interrupts the loop, then the busted art is written and its restart starts the loop on the busted frames.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        f.Host.Start();
        f.Run(200);
        var entity = f.Loop.Entity;
        Assert.IsTrue(f.Host.ObjectBroken(entity));
        Assert.AreEqual(0, f.Host.Running, "the break ended the loop's run");
        Assert.AreEqual(SceneryRestart.Start, f.Host.Reframe(row, NewArt(row, 3), framesPerSecond: 10));
        Assert.AreEqual(1, f.Host.Running, "the busted art's restart runs the loop again");
        Assert.AreEqual(GoalType.AnimateLoop, f.Host.GoalOf(entity));
        Assert.AreEqual(3, f.Host.LoopOf(entity)!.Frames.Count);
        Assert.AreEqual(100, f.Host.LoopOf(entity)!.Loop.PauseMs, "a run started now reads the busted art's rate");
        Assert.AreEqual(0, f.Host.RestartBroken(_ => true), "the break's mark is gone: no second start");
        f.Run(500);
        Assert.AreEqual(f.Host.LoopOf(entity)!.Loop.Steps % 3, f.Host.LoopOf(entity)!.Loop.Frame, "the busted frames wrap at three");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void A_busted_art_of_one_frame_kills_every_run_of_the_object_including_the_overlay()
    {
        // ARC-SCRIPT-2670: a one-frame busted art (no ambient sound) takes the restart's kill branch: 0x423FF0 ends every run of the object, the no-interrupt overlays too.
        var (plan, row) = Scenery(frames: 4, fps: 8);
        using var f = new Fixture(plan, row);
        var eye = new AreaEyeCandyHost(f.Session, _ => null, (art, parts) => new EyeCandyFacts(4, 2, 100, 0, true, true), (low, high) => low);
        f.Host.Start();
        f.Run(300);
        var entity = f.Loop.Entity;
        Assert.IsTrue(eye.StartHover(entity));
        Assert.IsTrue(f.Host.ObjectBroken(entity));
        Assert.AreEqual(1, f.Session.QueryGoals().RunsOf(entity).Count(r => !r.Done), "the break leaves the overlay");
        Assert.AreEqual(SceneryRestart.Kill, f.Host.Reframe(row, NewArt(row, 1), framesPerSecond: 8));
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(entity).Count(r => !r.Done), "the busted art's one frame ends the overlay too");
        Assert.AreEqual(0, f.Host.Running);
        Assert.AreEqual(0, f.Host.Start(), "the loop is given up");
        Assert.AreEqual(0, f.Host.RestartBroken(_ => true));
    }
}
