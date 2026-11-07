using Arcanum.Data.Area;
using Arcanum.Simulation.Animation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-203 (B46, SM19) native observation in map mode: the fire of an animated scenery (a fireplace, a brazier, a torch) is drawn over the sprites
// behind it. The stage takes the animated scenery nearest the hero that the canvas draws, hides the roofs, centres the camera on it and reads the viewport back
// twice on four frames of its loop, once as drawn and once with that scenery left out (the canvas's AreaSmokeOmitted seam): the pixels the scenery changes inside its
// sprite's rectangle must number at least FireOrderMinimumPixels each time (a brick or wall sprite drawn over the flame leaves it nothing to change). Prints the counts;
// skips a map without animated scenery near the hero.
public partial class TerrainViewerApp
{
    private const int FireOrderMinimumPixels = 80, FireOrderLoops = 4, FireOrderFrames = 4;

    private Task RunFireOrderSmokeAsync() => WithAreaSmokeCameraHeld(ObserveFireOrderAsync);

    private async Task<object> ObserveFireOrderAsync()
    {
        if (_readyArea is not { } area || HeroWorldCell() is not { } pc)
        {
            GD.Print("ARC-DEV-203 native-smoke fire-order SKIP (no map or hero)");
            return new { FireOrder = "skip" };
        }
        try
        {
            PortalSmokeClockStart();
            ProcessAreaSceneryAnimation();
            if (_sceneryAnimation is not { } animation || _sceneryHost is not { } host)
            {
                GD.Print("ARC-DEV-203 native-smoke fire-order SKIP (no session)");
                return new { FireOrder = "skip" };
            }
            var loops = animation.Loops.Where(l => SceneryAnimationRules.Near(l.Loop.X, l.Loop.Y, pc) && host.GoalOf(l.Entity) is not null)
                .OrderBy(l => Math.Max(Math.Abs(l.Loop.X - pc.X), Math.Abs(l.Loop.Y - pc.Y))).Take(FireOrderLoops).ToArray();
            if (loops.Length == 0)
            {
                GD.Print($"ARC-DEV-203 native-smoke fire-order SKIP map={_readyMapName} loops={animation.Loops.Count} (none near the hero)");
                return new { FireOrder = "none" };
            }
            var counts = new List<int[]>();
            foreach (var loop in loops)
            {
                var t0 = area.Plan.Commands.Single(c => c.Identity == loop.Key);
                var row = new int[FireOrderFrames];
                for (var frame = 0; frame < FireOrderFrames; frame++)
                {
                    // The sample is the frame shown now; the later ones wait for the loop's run to step once more (50 ms host stamps, at most 20 s).
                    var wanted = loop.Loop.Steps + (frame == 0 ? 0 : 1);
                    for (var ticks = 0; loop.Loop.Steps < wanted; ticks++)
                    {
                        if (ticks > 400) throw new InvalidOperationException($"FIRE_RUN_STALLED frame={frame} steps={loop.Loop.Steps}");
                        TickAreaClock(_portalSmokeHostMs += 50);
                        ProcessAreaSceneryAnimation();
                    }
                    _canvas!.SetAreaMovingBodies(AreaMovingCommands());
                    _camera!.ResetWorld(new GamepadLab.Core.Vec2(t0.Destination.X + t0.Destination.Width / 2d, t0.Destination.Y + t0.Destination.Height / 2d), WorldViewport, 2);
                    PublishWorldSelection();
                    // A roof over the building would hide the room; the observation asks only whether the sprites behind the fire cover it.
                    _canvas.SetAreaRoofs(null, null, null);
                    for (var k = 0; k < 3; k++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var with = GetViewport().GetTexture().GetImage();
                    // The same view with the scenery left out.
                    _canvas.AreaSmokeOmitted = loop.Key;
                    PublishWorldSelection(); _canvas.SetAreaRoofs(null, null, null);
                    for (var k = 0; k < 3; k++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var without = GetViewport().GetTexture().GetImage();
                    _canvas.AreaSmokeOmitted = null;
                    row[frame] = ChangedPixels(with, without, with.GetWidth() / 2, with.GetHeight() / 2, Math.Max(t0.Destination.Width, t0.Destination.Height));
                    if (System.Environment.GetEnvironmentVariable("ARC_DEV203_CAPTURE") is { Length: > 0 } capture) with.SavePng($"{capture}-si{loop.Key.SourceIndex}-f{frame}.png");
                }
                counts.Add(row);
                if (row.Min() < FireOrderMinimumPixels)
                    throw new InvalidOperationException($"FIRE_HIDDEN si={loop.Key.SourceIndex} cell=({loop.Loop.X},{loop.Loop.Y}) changed pixels {string.Join(",", row)} below {FireOrderMinimumPixels}");
            }
            GD.Print("ARC-DEV-203 native-smoke fire-order PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.CatalogueMap, Loops = loops.Length, Changed = counts }));
            return new { FireOrder = loops.Length };
        }
        catch (Exception failure)
        {
            GD.Print("ARC-DEV-203 native-smoke fire-order FAIL " + failure.Message);
            throw;
        }
        finally
        {
            PortalSmokeClockEnd();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // The pixels that differ by more than 60 (summed over the colour channels) between two views, in the square of half-width radius around (cx, cy).
    private static int ChangedPixels(Image a, Image b, int cx, int cy, int radius)
    {
        var count = 0;
        for (var y = Math.Max(0, cy - radius); y < Math.Min(a.GetHeight(), cy + radius); y++)
            for (var x = Math.Max(0, cx - radius); x < Math.Min(a.GetWidth(), cx + radius); x++)
            {
                var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                if (Math.Abs(p.R8 - q.R8) + Math.Abs(p.G8 - q.G8) + Math.Abs(p.B8 - q.B8) > 60) count++;
            }
        return count;
    }
}
