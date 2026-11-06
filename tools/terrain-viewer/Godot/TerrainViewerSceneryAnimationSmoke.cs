using Arcanum.Data.Area;
using Arcanum.Simulation.Animation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-035 native observation in map mode: the animated scenery nearest the PC steps one frame per 1000 / fps ms of its goal run (ledger AS3-AS5;
// ARC-DEV-142: the run is goal 1 or 77 on the session's table, stepped by the frame step on the animation clock), and the canvas draws each frame it reaches.
// The stage takes the shown map's loops, waits for the chosen loop to step four more times and reads the drawn command of the scenery after each; three distinct drawn
// frames are required, and the loop must own a run of the table. Skips a map without an animated scenery drawn near the PC. Prints counts only.
public partial class TerrainViewerApp
{
    private Task<object> RunSceneryAnimationSmokeAsync() => WithAreaSmokeCameraHeld(ObserveSceneryAnimationAsync);

    private async Task<object> ObserveSceneryAnimationAsync()
    {
        if (_readyArea is not { } area || HeroWorldCell() is not { } pc)
        {
            GD.Print("ARC-RENDER-035 native-smoke scenery animation SKIP (no map or no hero)");
            return new { SceneryAnimation = "skip" };
        }
        try
        {
            // The loops and their runs of the shown map (the frame loop made them already, this is the same call).
            PortalSmokeClockStart();
            ProcessAreaSceneryAnimation();
            if (_sceneryAnimation is not { } animation || _sceneryHost is not { } host)
            {
                GD.Print("ARC-RENDER-035 native-smoke scenery animation SKIP (no session)");
                return new { SceneryAnimation = "skip" };
            }
            _canvas!.SetAreaMovingBodies(AreaMovingCommands());
            // The nearest loops near the PC whose T0 body the canvas draws (a roof or a fade can hide one).
            AreaSceneryLoop? loop = null;
            foreach (var candidate in animation.Loops.Where(l => SceneryAnimationRules.Near(l.Loop.X, l.Loop.Y, pc) && host.GoalOf(l.Entity) is not null)
                .OrderBy(l => Math.Max(Math.Abs(l.Loop.X - pc.X), Math.Abs(l.Loop.Y - pc.Y))).Take(4))
            {
                var t0 = area.Plan.Commands.Single(c => c.Identity == candidate.Key);
                _camera!.ResetWorld(new GamepadLab.Core.Vec2(t0.Destination.X + t0.Destination.Width / 2d, t0.Destination.Y + t0.Destination.Height / 2d),
                    WorldViewport, 1);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (_canvas.AreaVisibleCommands.Any(c => c.Identity == candidate.Key)) { loop = candidate; break; }
            }
            if (loop is null)
            {
                GD.Print($"ARC-RENDER-035 native-smoke scenery animation SKIP map={_readyMapName} loops={animation.Loops.Count} runs={host.Running} (none drawn near the PC)");
                return new { SceneryAnimation = "none" };
            }
            var drawn = new List<int>();
            for (var step = 0; step <= 4; step++)
            {
                // Ticks the frame clock (literal host stamps, 50 ms each, as the door smokes do) until the loop's run has stepped once more, at most 20 s of animation time; the first sample is the frame shown now.
                var wanted = loop.Loop.Steps + (step == 0 ? 0 : 1);
                for (var ticks = 0; loop.Loop.Steps < wanted; ticks++)
                {
                    if (ticks > 400)
                        throw new InvalidOperationException($"SCENERY_RUN_STALLED step={step} steps={loop.Loop.Steps} goal={host.GoalOf(loop.Entity)} stepped={host.Steps}");
                    TickAreaClock(_portalSmokeHostMs += 50);
                    ProcessAreaSceneryAnimation();
                }
                AreaDrawCommand? visible = null;
                for (var i = 0; i < 4 && visible is null; i++)
                {
                    _canvas!.SetAreaMovingBodies(AreaMovingCommands()); PublishWorldSelection();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    // The drawn frame is one of the loop's frames; the loop may have stepped on again meanwhile, so it is compared with the run's frame within one step.
                    visible = _canvas.AreaVisibleCommands.FirstOrDefault(c => c.Identity == loop.Key && loop.Frames.Any(f => AreaFrameKey.From(f) == c.Frame));
                }
                if (visible is null)
                    throw new InvalidOperationException($"SCENERY_FRAME_NOT_DRAWN step={step} frame={loop.Loop.Frame} drawn="
                        + string.Join(",", _canvas!.AreaVisibleCommands.Where(c => c.Identity == loop.Key).Select(c => c.Frame.Frame)));
                drawn.Add(visible.Frame.Frame);
            }
            if (drawn.Distinct().Count() < 3)
                throw new InvalidOperationException("SCENERY_FRAMES drawn " + string.Join(",", drawn));
            GD.Print("ARC-RENDER-035 native-smoke scenery animation PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = area.Snapshot.CatalogueMap, Loops = animation.Loops.Count, Runs = host.Running, loop.Frames.Count, loop.Loop.PauseMs, Goal = host.GoalOf(loop.Entity)?.ToString(), Frames = drawn,
                Distance = Math.Max(Math.Abs(loop.Loop.X - pc.X), Math.Abs(loop.Loop.Y - pc.Y)) }));
            return new { SceneryAnimation = drawn.Count };
        }
        catch (Exception failure)
        {
            GD.Print("ARC-RENDER-035 native-smoke scenery animation FAIL " + failure.Message);
            throw;
        }
        finally
        {
            PortalSmokeClockEnd();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
