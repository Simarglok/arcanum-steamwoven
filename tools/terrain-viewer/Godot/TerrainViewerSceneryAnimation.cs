using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-035: animated scenery (fires, torches and the like) plays its ART's frames as the original's scenery animation loop does
// (ledger AS1-AS7): loops start when the shown map is ready, step on the world clock, and a scenery off its T0 frame is drawn like a
// moved body. Not ready: the loop's ambient sound and goal 77's fire damage, restarting a loop when an object's ART or OFF flag
// changes at runtime (busted scenery keeps its ground body, unanimated).
public partial class TerrainViewerApp
{
    private AreaSceneryAnimation? _sceneryAnimation;
    // The plan the loops were made from (not the load result, which a retired generation must be free to release).
    private AreaScenePlan? _sceneryStamp;
    private Dictionary<AreaObjectKey, AreaDrawCommand> _sceneryBodies = [];
    // The smoke's own clock for the loops (null: the world clock).
    private long? _sceneryNowForSelfTest;

    /// <summary>Per frame: the loops of the shown map step to the world clock; a changed frame redraws the scenery's body.</summary>
    private void ProcessAreaSceneryAnimation()
    {
        if (_readyArea is not { } area || HeroWorldCell() is not { } pc)
        {
            if (_sceneryAnimation is not null || _sceneryBodies.Count > 0) { StopSceneryAnimation(); _canvas?.SetAreaMovingBodies(AreaMovingCommands()); }
            return;
        }
        var now = _sceneryNowForSelfTest ?? WorldMilliseconds();
        var changed = false;
        if (!ReferenceEquals(_sceneryStamp, area.Plan))
        {
            _sceneryStamp = area.Plan;
            _sceneryAnimation = AreaSceneryAnimation.Create(area.Plan, area.Snapshot.Objects, now, pc);
            _sceneryBodies = [];
            GD.Print($"ARC-RENDER-035 scenery-loops map={area.Snapshot.RuntimeMap} loops={_sceneryAnimation.Loops.Count} "
                + $"frames={_sceneryAnimation.Loops.Sum(l => l.Frames.Count)}");
            changed = true;
        }
        if (_sceneryAnimation is not { } animation) return;
        changed |= animation.Advance(now, pc);
        if (changed) SyncSceneryBodies(animation);
    }

    /// <summary>Drops the loops and their bodies (the shown map is cleared).</summary>
    private void StopSceneryAnimation() { _sceneryAnimation = null; _sceneryStamp = null; _sceneryBodies = []; }

    private void SyncSceneryBodies(AreaSceneryAnimation animation)
    {
        AreaFrames();
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        foreach (var loop in animation.Loops)
        {
            if (!overlay.Bodies.TryGetValue(loop.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var body)) continue;
            if (loop.Visual.Key == body.Key && loop.Loop.OffsetX == 0 && loop.Loop.OffsetY == 0) continue;
            bodies[loop.Key] = AreaScenePlan.SceneryFrame(command, loop.Row, body, loop.Visual,
                loop.Loop.OffsetX, loop.Loop.OffsetY);
        }
        if (bodies.Count == 0 && _sceneryBodies.Count == 0) return;
        _sceneryBodies = bodies;
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }
}
