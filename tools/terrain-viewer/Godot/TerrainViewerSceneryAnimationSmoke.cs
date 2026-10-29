using Arcanum.Data.Area;
using Arcanum.Simulation.Animation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-035 native observation in map mode: the animated scenery nearest the PC steps one frame per 1000 / fps ms of the loop's
// clock (ledger AS3-AS5), and the canvas draws each frame it reaches. The stage restarts the loops on its own clock, steps the chosen
// loop four pauses and reads the drawn command of the scenery after each; three distinct drawn frames are required. Skips a map
// without an animated scenery near the PC. Prints counts only.
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
        const long start = 1_000;
        _sceneryNowForSelfTest = start;
        try
        {
            var animation = AreaSceneryAnimation.Create(area.Plan, area.Snapshot.Objects, start, pc);
            _sceneryAnimation = animation; _sceneryStamp = area.Plan; _sceneryBodies = [];
            var loop = animation.Loops.Where(l => SceneryAnimationRules.Near(l.Loop.X, l.Loop.Y, pc))
                .OrderBy(l => Math.Max(Math.Abs(l.Loop.X - pc.X), Math.Abs(l.Loop.Y - pc.Y))).FirstOrDefault();
            if (loop is null)
            {
                GD.Print($"ARC-RENDER-035 native-smoke scenery animation SKIP map={_readyMapName} loops={animation.Loops.Count} (none near the PC)");
                return new { SceneryAnimation = "none" };
            }
            var t0 = area.Plan.Commands.Single(c => c.Identity == loop.Key);
            _camera!.ResetWorld(new GamepadLab.Core.Vec2(t0.Destination.X + t0.Destination.Width / 2d, t0.Destination.Y + t0.Destination.Height / 2d),
                WorldViewport, 1);
            var expected = new List<int>(); var drawn = new List<int>();
            for (var step = 0; step <= 4; step++)
            {
                _sceneryNowForSelfTest = start + step * loop.Loop.PauseMs;
                if (animation.Advance(_sceneryNowForSelfTest.Value, pc) || step == 0) SyncSceneryBodies(animation);
                expected.Add(loop.Loop.Frame);
                var key = AreaFrameKey.From(loop.Visual);
                AreaDrawCommand? visible = null;
                for (var i = 0; i < 4 && visible is null; i++)
                {
                    _canvas!.SetAreaMovingBodies(AreaMovingCommands()); PublishWorldSelection();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    visible = _canvas.AreaVisibleCommands.FirstOrDefault(c => c.Identity == loop.Key && c.Frame == key);
                }
                if (visible is null)
                    throw new InvalidOperationException($"SCENERY_FRAME_NOT_DRAWN step={step} frame={loop.Loop.Frame} drawn="
                        + string.Join(",", _canvas!.AreaVisibleCommands.Where(c => c.Identity == loop.Key).Select(c => c.Frame.Frame)));
                drawn.Add(visible.Frame.Frame);
            }
            var first = expected[0];
            var stepped = expected.Select((frame, step) => frame == (first + step) % loop.Frames.Count).All(ok => ok);
            if (!stepped || drawn.Distinct().Count() < 3)
                throw new InvalidOperationException("SCENERY_FRAMES " + string.Join(",", expected) + " drawn " + string.Join(",", drawn));
            GD.Print("ARC-RENDER-035 native-smoke scenery animation PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = area.Snapshot.CatalogueMap, Loops = animation.Loops.Count, loop.Frames.Count, loop.Loop.PauseMs, Frames = drawn,
                Distance = Math.Max(Math.Abs(loop.Loop.X - pc.X), Math.Abs(loop.Loop.Y - pc.Y)) }));
            return new { SceneryAnimation = drawn.Count };
        }
        finally
        {
            // The loops restart on the world clock at the next frame.
            _sceneryNowForSelfTest = null; _sceneryStamp = null;
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
