using Arcanum.Data.Area;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-035: animated scenery (fires, torches and the like) plays its ART's frames as the original's scenery animation loop does
// (ledger AS1-AS7): loops start when the shown map is ready and a scenery off its T0 frame is drawn like a moved body.
// ARC-DEV-142 (MIG13 S9a, docs/architecture/goal-table.md §3.5): the loop is the scenery's own goal run (goal 1, or 77 for a burning scenery, the S9 read GT75-GT78) that the frame step hands to the scenery host
// through the router, on the animation clock (so the loops stop with the Esc pause); this file binds the host to the shown map and redraws a scenery whose frame the host moved.
// ARC-DEV-144 (S9c): goal 77 burns the critters on the scenery's tile (TerrainViewerFire.cs).
// ARC-RENDER-1126: the begin node's looping sound (TerrainViewerSoundCallers.cs). ARC-RENDER-1781: an object's ART changed at run time reframes its loop (TerrainViewerSceneryArt.cs). Not ready: the ambient sound of a one-frame ART and of a created scenery, restarting a loop when an object's OFF flag changes at runtime (AreaSceneryHost.Restart is the rule; no site calls it) and the items on a burning tile (busted scenery keeps its ground body,
// unanimated). A table-exhaustion sweep ends the loops for good, as in the original (GT98).
public partial class TerrainViewerApp
{
    private AreaSceneryAnimation? _sceneryAnimation;
    private AreaSceneryHost? _sceneryHost;
    // The plan the loops were made from (not the load result, which a retired generation must be free to release).
    private AreaScenePlan? _sceneryStamp;
    private Dictionary<AreaObjectKey, AreaDrawCommand> _sceneryBodies = [];
    // The occupancy revision the beds were last drawn at (ARC-WORLD-1162).
    private long _bedRevisionDrawn = -1;

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
            StopSceneryVoices();
            _sceneryStamp = area.Plan;
            _sceneryAnimation = AreaSceneryAnimation.Create(area.Plan, area.Snapshot.Objects);
            _sceneryHost = new(session, _sceneryAnimation, HeroWorldCell, FireBurnerFor(session));
            // ARC-RENDER-1781: the sceneries with a changed art get their loops before the loops start (the original starts a loop with the art the object has).
            SyncSceneryArt(_sceneryHost, newHost: true);
            DropDestroyedSceneryLoops(session, _sceneryHost, area.Snapshot.RuntimeMap);
            var started = _sceneryHost.Start();
            // A restamp drops the old map's bodies from the canvas too, even when no loop has stepped yet.
            if (_sceneryBodies.Count > 0) { _sceneryBodies = []; _canvas?.SetAreaMovingBodies(AreaMovingCommands()); }
            GD.Print($"ARC-RENDER-035 scenery-loops map={area.Snapshot.RuntimeMap} loops={_sceneryAnimation.Loops.Count} "
                + $"frames={_sceneryAnimation.Loops.Sum(l => l.Frames.Count)} burning={_sceneryAnimation.Loops.Count(l => l.Burning)} runs={started} refused={_sceneryHost.Refused}");
            changed = true;
        }
        // ARC-RENDER-1781: a scenery whose art field came, changed or went at run time gets the loop of its art (AN10).
        changed |= SyncSceneryArt(_sceneryHost, newHost: false);
        // A start that found the table held by a pass is tried again (the loops already running stay).
        if (_sceneryHost.StartPending) _sceneryHost.Start();
        // ARC-SCRIPT-2646 (0x43F5F0 → 0x43D280 → 0x43F710): a scenery a break switched off and the respawn switched on gets its loop back.
        var shownMap = new SessionMapId(area.Snapshot.RuntimeMap);
        _sceneryHost.RestartBroken(entity => session.QueryObjectCondition(shownMap, entity, Arcanum.Simulation.Combat.DamageableObjectKind.Scenery) == SessionObjectCondition.Whole);
        RetryPendingRunEnds();
        _areaDoors?.RetryPending();
        // ARC-DEV-352: the created sceneries (a wall of fire's pieces) get or lose their loops.
        SyncCreatedSceneryLoops(session, _sceneryHost);
        changed |= _sceneryHost.TakeChanged();
        // ARC-RENDER-1126: the loops whose begin node ran decide their looping sound (0x42DA50).
        TickSceneryLoopSounds(_sceneryHost);
        // ARC-WORLD-1162: a bed that took or lost a sleeper is drawn at its next frame or its own.
        if ((changed || _bedRevisionDrawn != _bedOccupancy.Revision) && _sceneryAnimation is { } animation) SyncSceneryBodies(animation);
        // A created scenery is a ground body drawn at its loop's frame: the ground is drawn again only when a created loop came, went or moved a frame.
        if (_sceneryHost.TakeCreatedChanged()) { _groundStamp = null; SyncAreaInventory(); }
    }

    /// <summary>
    /// ARC-SCRIPT-2514 (ledger BS3c, 0x43CCA0 → 0x423FF0, 0x459740): a script destroyed a scenery or container of the shown map. Every goal run of the object ends (its loop, the eye-candy overlays and the hover: the scenery host does it with the loop,
    /// the session's goal table alone for an object with no loop) and the spell runs aimed at or cast from it hear of the destroy in the cast host. An object of another map has no run in the shown session's table.
    /// </summary>
    private void EndDestroyedObjectRuns(ScrObjectRef obj)
    {
        if (_areaSession is not { } session || obj.Map != CurrentAreaMap) return;
        var spells = SpellCasterNow();
        var castId = spells?.CastIdOf(obj.Code);
        bool ended;
        if (ShownSceneryHost is { } host) ended = host.ObjectDestroyed(obj.Code, castId);
        else
        {
            ended = false;
            foreach (var owner in new[] { obj.Code, castId }.Where(o => o is not null).Distinct())
                if (session.QueryGoals().RunsOf(owner!).Any(r => !r.Done)) ended |= EndRunsOfOwner(session, owner!, kill: true);
        }
        spells?.ObjectGone(castId!);
        GD.Print($"ARC-SCRIPT-2514 object-destroyed object={obj.Code} runs-ended={ended} spell-runs-told={spells is not null}");
    }

    /// <summary>
    /// ARC-SCRIPT-2646 (ledger OD7, 0x43F1C0 → 0x424070 at level 5): a break of a container, scenery, trap, door or window of the shown map interrupts the object's goal runs before anything else of the break: a scenery's loop ends (the scenery host does it, and stops the voice),
    /// its overlays stay (no-interrupt goals). An object of another map has no run in the shown session's table.
    /// </summary>
    private void InterruptBrokenObjectRuns(AreaObjectRecord row)
    {
        if (_areaSession is not { } session || ShownObjectRef(row) is not { } obj || obj.Map != CurrentAreaMap) return;
        var castId = SpellCasterNow()?.CastIdOf(obj.Code);
        bool ended;
        // ARC-SCRIPT-2669: a door or window's runs (70, 71, 72) are the door host's: it interrupts them and runs the end nodes (a cut closing snaps the door shut).
        if (_areaDoors is { } doors && ReferenceEquals(doors.Session, session) && doors.Owns(obj.Code)) ended = doors.ObjectBroken(obj.Code);
        else if (ShownSceneryHost is { } host) ended = host.ObjectBroken(obj.Code, castId);
        else
        {
            ended = false;
            foreach (var owner in new[] { obj.Code, castId }.Where(o => o is not null).Distinct())
                ended |= EndRunsOfOwner(session, owner!, kill: false);
        }
        GD.Print($"ARC-SCRIPT-2646 object-broken object={obj.Code} loop-ended={ended}");
    }

    // The scenery host's own retry is not there when the host belongs to a replaced session: a refusal for a pass in progress is tried again on the next frames.
    private readonly List<(GameSessionOwner Session, string Owner, bool Kill)> _pendingRunEnds = [];

    private bool EndRunsOfOwner(GameSessionOwner session, string owner, bool kill)
    {
        var change = kill ? session.KillGoals(owner) : session.InterruptGoals(owner, AreaSceneryHost.BreakInterruptLevel);
        if (change.Failure == SessionGoalFailure.PassInProgress) { _pendingRunEnds.Add((session, owner, kill)); return false; }
        return kill ? change.Succeeded : change.Ended.Any();
    }

    private void RetryPendingRunEnds()
    {
        if (_pendingRunEnds.Count == 0) return;
        var due = _pendingRunEnds.ToArray();
        _pendingRunEnds.Clear();
        foreach (var (session, owner, kill) in due)
            if (ReferenceEquals(session, _areaSession)) EndRunsOfOwner(session, owner, kill);
    }

    /// <summary>ARC-SCRIPT-2514: a scenery the session holds destroyed gets no loop on a host built for a window or map shown again (the original's lists do not hold a destroyed object, so no loop starts for it).</summary>
    private void DropDestroyedSceneryLoops(GameSessionOwner session, AreaSceneryHost host, int map)
    {
        foreach (var loop in host.Animation.Loops.ToArray())
            if (AreaUseScriptHost.Destroyed(session, map, loop.Entity)
                || session.QueryObjectCondition(new SessionMapId(map), loop.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Scenery) == SessionObjectCondition.Destroyed)
                host.ObjectDestroyed(loop.Entity);
    }

    /// <summary>Drops the loops, their runs and their bodies (the shown map is cleared).</summary>
    private void StopSceneryAnimation() { _sceneryHost?.LoseAll(); StopSceneryVoices(); _sceneryHost = null; _sceneryAnimation = null; _sceneryStamp = null; _sceneryBodies = []; }

    private void SyncSceneryBodies(AreaSceneryAnimation animation)
    {
        AreaFrames();
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        foreach (var loop in animation.Loops)
        {
            if (_sceneryArt.ContainsKey(loop.Entity)) continue;
            if (!overlay.Bodies.TryGetValue(loop.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var body)) continue;
            if (loop.Visual.Key == body.Key && loop.Loop.OffsetX == 0 && loop.Loop.OffsetY == 0) continue;
            bodies[loop.Key] = AreaScenePlan.SceneryFrame(command, loop.Row, body, loop.Visual,
                loop.Loop.OffsetX, loop.Loop.OffsetY);
        }
        // ARC-RENDER-1781: a scenery with a changed art, on its loop's frame of that art or on frame 0 (a busted one's ground body wins in the draw order).
        foreach (var (entity, art) in _sceneryArt)
            if (SceneryArtCommand(entity, art, _sceneryHost, overlay.Bodies, overlay.Visuals) is { } drawn) bodies[art.Row.Key] = drawn;
        AddOccupiedBeds(bodies, overlay.Bodies, overlay.Visuals);
        _bedRevisionDrawn = _bedOccupancy.Revision;
        if (bodies.Count == 0 && _sceneryBodies.Count == 0) return;
        _sceneryBodies = bodies;
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>ARC-WORLD-1162 (ledger SWB15, 0x43F7F0): a bed that holds a sleeper is drawn at the next frame of its ART (the sleeper itself is not drawn, object flag 0x100). The frames come from the plan's extra frames of the bed (<see cref="AreaBedArt"/>).</summary>
    private void AddOccupiedBeds(Dictionary<AreaObjectKey, AreaDrawCommand> bodies, Dictionary<AreaObjectKey, AreaDrawCommand> drawn, Dictionary<AreaVisualKey, AreaVisualRecord> visuals)
    {
        if (_mapTransfers is not { } transfers || _readyArea is not { } shown) return;
        foreach (var key in transfers.Beds)
        {
            if (_bedOccupancy.Occupant(key) is null || bodies.ContainsKey(key)) continue;
            if (!shown.Plan.SceneryFrames.TryGetValue(key, out var frames) || !drawn.TryGetValue(key, out var command) || !visuals.TryGetValue(command.Visual, out var body)) continue;
            var next = AreaBeds.OccupiedFrame((int)((body.RawArt >> 14) & 31), body.FrameCount);
            if (frames.FirstOrDefault(f => (int)((f.RawArt >> 14) & 31) == next) is not { } frame) continue;
            bodies[key] = AreaScenePlan.SceneryFrame(command, transfers.Object(key), body, frame, 0, 0);
        }
    }
}
