using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-035: animated scenery (fires, torches and the like) plays its ART's frames as the original's scenery animation loop does
// (ledger AS1-AS7): loops start when the shown map is ready and a scenery off its T0 frame is drawn like a moved body.
// ARC-DEV-142 (MIG13 S9a, docs/architecture/goal-table.md §3.5): the loop is the scenery's own goal run (goal 1, or 77 for a burning scenery, the S9 read GT75-GT78) that the frame step hands to the scenery host
// through the router, on the animation clock (so the loops stop with the Esc pause); this file binds the host to the shown map and redraws a scenery whose frame the host moved.
// ARC-DEV-144 (S9c): goal 77 burns the critters on the scenery's tile (TerrainViewerFire.cs).
// Not ready: the loop's ambient sound, restarting a loop when an object's ART or OFF flag changes at runtime (AreaSceneryHost.Restart is the rule; no site calls it) and the items on a burning tile (busted scenery keeps its ground body,
// unanimated). A table-exhaustion sweep ends the loops for good, as in the original (GT98).
public partial class TerrainViewerApp
{
    private AreaSceneryAnimation? _sceneryAnimation;
    private AreaSceneryHost? _sceneryHost;
    // The plan the loops were made from (not the load result, which a retired generation must be free to release).
    private AreaScenePlan? _sceneryStamp;
    private Dictionary<AreaObjectKey, AreaDrawCommand> _sceneryBodies = [];

    /// <summary>The scenery host as far as it belongs to the shown session; null when there is none or when it still belongs to a replaced session (a quick load, a carried session: its session is disposed). The goal-step router goes through this; <see cref="ProcessAreaSceneryAnimation"/> replaces the stale host.</summary>
    private AreaSceneryHost? ShownSceneryHost => _sceneryHost is { } host && ReferenceEquals(host.Session, _areaSession) ? host : null;

    /// <summary>Per frame: a newly shown map (or session) gets its loops and their runs; a loop whose run moved a frame redraws the scenery's body.</summary>
    private void ProcessAreaSceneryAnimation()
    {
        if (_readyArea is not { } area || _areaSession is not { } session || HeroWorldCell() is null)
        {
            if (_sceneryAnimation is not null || _sceneryBodies.Count > 0) { StopSceneryAnimation(); _canvas?.SetAreaMovingBodies(AreaMovingCommands()); }
            return;
        }
        var changed = false;
        if (!ReferenceEquals(_sceneryStamp, area.Plan) || _sceneryHost is null || !ReferenceEquals(_sceneryHost.Session, session))
        {
            // The runs of the loops of the map or window shown before end (a map's close ended them already); the new window's start.
            _sceneryHost?.LoseAll();
            _sceneryStamp = area.Plan;
            _sceneryAnimation = AreaSceneryAnimation.Create(area.Plan, area.Snapshot.Objects);
            _sceneryHost = new(session, _sceneryAnimation, HeroWorldCell, FireBurnerFor(session));
            var started = _sceneryHost.Start();
            // A restamp drops the old map's bodies from the canvas too, even when no loop has stepped yet.
            if (_sceneryBodies.Count > 0) { _sceneryBodies = []; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); }
            GD.Print($"ARC-RENDER-035 scenery-loops map={area.Snapshot.RuntimeMap} loops={_sceneryAnimation.Loops.Count} "
                + $"frames={_sceneryAnimation.Loops.Sum(l => l.Frames.Count)} burning={_sceneryAnimation.Loops.Count(l => l.Burning)} runs={started} refused={_sceneryHost.Refused}");
            changed = true;
        }
        // A start that found the table held by a pass is tried again (the loops already running stay).
        if (_sceneryHost.StartPending) _sceneryHost.Start();
        changed |= _sceneryHost.TakeChanged();
        if (changed && _sceneryAnimation is { } animation) SyncSceneryBodies(animation);
    }

    /// <summary>Drops the loops, their runs and their bodies (the shown map is cleared).</summary>
    private void StopSceneryAnimation() { _sceneryHost?.LoseAll(); _sceneryHost = null; _sceneryAnimation = null; _sceneryStamp = null; _sceneryBodies = []; }

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
