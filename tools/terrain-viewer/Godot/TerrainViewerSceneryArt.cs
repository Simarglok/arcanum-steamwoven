using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-1781 (ledger AN6, AN10-AN13): a scenery whose art id a script's change art number wrote (the session's object art field, ARC-SCRIPT-1735) is drawn from the new art's frames and its animation loop runs over them:
// the original's loop step reads the object's current art every time (frame count from its header, next frame, frame delta), so a new art of several frames animates and one of a single frame is still.
// The viewer loads every frame of the new art (AreaExtraArt, one art id per frame), hands them to the scenery host (AreaSceneryHost.Reframe: the restart rule of the art writer), and draws the host's frame in SyncSceneryBodies.
// ARC-SCRIPT-2670: a busted scenery's destroyed art goes through the same path (the break's art write, ledger BS3c); the restart is decided at the next art scan, after the BUST script (the original writes the art before it: a run the BUST script starts on the scenery is killed by a one-frame busted art here, a kept difference).
// Not ready: the hover and pick shape of the changed object stay the T0 body's (the hover ring and index read the map data's art), and the loop sound of a changed art is the begin node's (the sound follows the object, not the art).
public partial class TerrainViewerApp
{
    /// <summary>A scenery that has another art than its map data gives: the art id (frame 0) and the new art's frames in frame order (their pixels are in the map's frame overlay).</summary>
    private sealed record SceneryArt(uint Art, AreaObjectRecord Row, IReadOnlyList<AreaVisualRecord> Frames);

    private Dictionary<string, SceneryArt> _sceneryArt = [];
    // ARC-DEV-1961: the stamp names the map and session the last scan read by weak references: a strong field held the retired map's AreaLoadResult
    // until the next scan of the new map (the resources smoke stage's RETIRED_GENERATION_ALIVE after a map stage), and a stamp must never keep a generation alive.
    private (WeakReference? Area, WeakReference? Session, long Revision) _sceneryArtStamp;
    private bool SceneryArtStampIs(AreaLoadResult area, GameSessionOwner session, long revision)
        => _sceneryArtStamp.Area is { } a && ReferenceEquals(a.Target, area) && _sceneryArtStamp.Session is { } s && ReferenceEquals(s.Target, session) && _sceneryArtStamp.Revision == revision;
    private void SceneryArtStampSet(AreaLoadResult area, GameSessionOwner session, long revision) => _sceneryArtStamp = (new WeakReference(area), new WeakReference(session), revision);
    /// <summary>A cleared scene forgets the scenery arts drawn for its map and the stamp of its last scan.</summary>
    private void RetireSceneryArt() { _bustableSceneryFor = null; _bustableSceneries = []; _sceneryArt = []; _sceneryArtStamp = default; _sceneryArtScanDue = true; }
    private readonly HashSet<string> _sceneryArtReported = [];
    // The scan of the session's art fields copies and sorts the map's changes: at most one per this many wall milliseconds (a change shows within it), and at once when a stage asks (ScanSceneryArtNow).
    private const long SceneryArtScanMs = 150;
    private long _sceneryArtScanAt;
    private bool _sceneryArtScanDue = true;
    private void ScanSceneryArtNow() => _sceneryArtScanDue = true;
    /// <summary>The art frames a scenery's art field may hold at most (frame bits 14-18 of the id).</summary>
    private const int SceneryArtMaxFrames = 32;

    /// <summary>The id of frame <paramref name="frame"/> of the scenery art <paramref name="art"/> (every class but the interface, misc, facade, light and eye candy keeps the frame in bits 14-18, AN3).</summary>
    internal static uint SceneryArtFrameId(uint art, int frame) => (art & ~(31u << 14)) | ((uint)frame << 14);

    /// <summary>
    /// The art fields of the shown map against the arts drawn so far: a scenery with a new art id gets its frames loaded and its loop reframed, one whose field went (the id of the map data) gets its own frames back. True when something changed
    /// (the bodies are drawn again). <paramref name="newHost"/>: the host is a fresh one (its loops are the map data's), so every field is applied again before the loops start.
    /// </summary>
    private bool SyncSceneryArt(AreaSceneryHost host, bool newHost)
    {
        if (_readyArea is not { } area || _areaSession is not { } session) return false;
        var revision = session.Version.Revision;
        if (newHost) _sceneryArt = [];
        else if (SceneryArtStampIs(area, session, revision) || !_sceneryArtScanDue && System.Environment.TickCount64 - _sceneryArtScanAt < SceneryArtScanMs) return false;
        _sceneryArtScanAt = System.Environment.TickCount64; _sceneryArtScanDue = false;
        var map = new SessionMapId(area.Snapshot.RuntimeMap);
        var wanted = WantedSceneryArts(area, session, map);
        if (wanted.Count == 0 && _sceneryArt.Count == 0) { SceneryArtStampSet(area, session, revision); return false; }
        // Without the module's content the art cannot be read: tried again on the next frame.
        if (ExtraArt() is not { } art) return false;
        SceneryArtStampSet(area, session, revision);
        AreaFrames();
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var changed = false;
        var rows = new Dictionary<string, AreaObjectRecord>(StringComparer.Ordinal);
        foreach (var row in area.Snapshot.Objects.Where(o => o.Type == SceneryType)) rows.TryAdd(AreaUseScriptHost.EntityId(row), row);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entity, aid) in wanted)
        {
            if (!rows.TryGetValue(entity, out var row) || !overlay.Bodies.TryGetValue(row.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual) || aid == visual.RawArt) continue;
            if (_sceneryArt.TryGetValue(entity, out var known) && known.Art == aid) { seen.Add(entity); continue; }
            // An art that cannot be read leaves the object on its map data's art (an earlier changed art of it is given back below).
            if (LoadSceneryArt(art, aid, entity) is not { } loaded) continue;
            seen.Add(entity);
            _sceneryArt[entity] = new(aid, row, loaded.Frames);
            host.Reframe(row, loaded.Frames, loaded.FramesPerSecond);
            changed = true;
            // A busted body is drawn from the loop's frames now, not from the static ground body.
            if (IsBustedSceneryArt(row, aid)) _groundStamp = null;
        }
        foreach (var entity in _sceneryArt.Keys.Where(entity => !seen.Contains(entity)).ToArray())
        {
            // The field went: the object has the art of the map data again (its frames from the plan, or its body alone).
            var row = _sceneryArt[entity].Row;
            if (IsBustedSceneryArt(row, _sceneryArt[entity].Art)) _groundStamp = null;
            _sceneryArt.Remove(entity);
            // ARC-SCRIPT-2670: a busted scenery the next hit destroyed has no art to give back: its runs ended at the destroy and nothing restarts for it.
            if (AreaUseScriptHost.Destroyed(session, area.Snapshot.RuntimeMap, entity) || session.QueryObjectCondition(map, entity, Arcanum.Simulation.Combat.DamageableObjectKind.Scenery) == SessionObjectCondition.Destroyed)
            {
                changed = true;
                continue;
            }
            if (overlay.Bodies.TryGetValue(row.Key, out var command) && overlay.Visuals.TryGetValue(command.Visual, out var visual))
            {
                var own = AreaSceneryAnimation.BaseFrames(area.Plan, row);
                host.Reframe(row, own?.Frames ?? [visual], (own?.Body ?? visual).FramesPerSecond);
            }
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// ARC-SCRIPT-2670 (ledger OD7, BS3c, 0x43EB30 → 0x43F710): the arts the shown map's sceneries have apart from the map data. A script's art number comes from the session; a scenery the session holds busted has its destroyed ART (field 24 of the row) too: the break wrote it
    /// into the object through the art writer, so the loop is the restart's decision on that art (several frames: the loop runs on them; one frame: the object's runs end). The busted art wins over a script's art, as the ground body of a busted scenery did.
    /// </summary>
    private IReadOnlyList<(string Entity, uint Art)> WantedSceneryArts(AreaLoadResult area, GameSessionOwner session, SessionMapId map)
    {
        var wanted = session.QueryObjectArts(map).ToList();
        // The sceneries with a destroyed art are listed once per map data (a scan runs on the session's revisions); the session says which of them are busted, with no dependency on the attack host.
        if (!ReferenceEquals(_bustableSceneryFor, area.Snapshot))
        {
            _bustableSceneryFor = area.Snapshot;
            _bustableSceneries = area.Snapshot.Objects.Where(r => r.Type == SceneryType && BustedArt(r) is not null).Select(r => (AreaUseScriptHost.EntityId(r), r)).ToList();
        }
        foreach (var (entity, row) in _bustableSceneries)
        {
            if (session.QueryObjectCondition(map, entity, Arcanum.Simulation.Combat.DamageableObjectKind.Scenery) != SessionObjectCondition.Busted) continue;
            wanted.RemoveAll(w => w.Entity == entity);
            wanted.Add((entity, BustedArt(row)!.Value));
        }
        return wanted;
    }

    private object? _bustableSceneryFor;
    private List<(string Entity, AreaObjectRecord Row)> _bustableSceneries = [];

    /// <summary>The destroyed ART of a scenery row (field 24), null when it has none.</summary>
    private uint? BustedArt(AreaObjectRecord row) => Number(row, DestroyedArtSlot) is { } art && !AreaDataOwner.NamesNoArt(art) ? art : null;

    private bool IsBustedSceneryArt(AreaObjectRecord row, uint art) => BustedArt(row) == art;

    /// <summary>Every frame of the art id (frame 0 first), each loaded into the frame overlay; null (said once) when a frame cannot be read: the object is then drawn from its map data as before.</summary>
    private (IReadOnlyList<AreaVisualRecord> Frames, int FramesPerSecond)? LoadSceneryArt(AreaExtraArt art, uint aid, string entity)
    {
        // Only a scenery art (class 4) is read frame by frame (bits 14-18); another class keeps the id as the script wrote it, one frame (the frame bits differ by class, AN3).
        var frameOf = aid >> 28 == 4 ? (Func<int, uint>)(index => SceneryArtFrameId(aid, index)) : _ => aid;
        var first = art.Load(frameOf(0));
        if (first.Frame is not { } head)
        {
            if (_sceneryArtReported.Add($"{entity}:{aid}")) GD.Print($"ARC-RENDER-1781 scenery-art entity={entity} art=0x{aid:X8} not drawn: status={first.Status} detail={first.Detail}");
            return null;
        }
        var count = aid >> 28 == 4 ? Math.Clamp(head.FramesPerGroup, 1, SceneryArtMaxFrames) : 1;
        var visuals = new List<AreaVisualRecord>(count);
        var frames = (AreaFrameOverlay)AreaFrames();
        for (var index = 0; index < count; index++)
        {
            var load = index == 0 ? first : art.Load(frameOf(index));
            if (load.Frame is not { } frame)
            {
                if (_sceneryArtReported.Add($"{entity}:{aid}:{index}")) GD.Print($"ARC-RENDER-1781 scenery-art entity={entity} art=0x{aid:X8} frame {index} unreadable: status={load.Status} detail={load.Detail}");
                return null;
            }
            frames.Add(frame.Key, frame.Frame); _extraVisuals[frame.Visual.Key] = frame.Visual;
            visuals.Add(frame.Visual);
        }
        return (visuals, head.FramesPerSecond);
    }

    /// <summary>The draw command of a scenery with a changed art: the frame its loop shows (frame 0 when it has none) of the new art, placed where the T0 body stood, moved by the offset the loop's frame deltas gave the object (AS6).</summary>
    private AreaDrawCommand? SceneryArtCommand(string entity, SceneryArt art, AreaSceneryHost? host,
        Dictionary<AreaObjectKey, AreaDrawCommand> drawn, Dictionary<AreaVisualKey, AreaVisualRecord> visuals)
    {
        if (!drawn.TryGetValue(art.Row.Key, out var command) || !visuals.TryGetValue(command.Visual, out var body)) return null;
        // The loop's frame counts only when it is the loop of this art (a skipped restart leaves the old loop on the old art's frames).
        var pose = host is not null && host.LoopOf(entity) is { } own && !ReferenceEquals(own.Frames, art.Frames) ? (0, 0, 0) : host?.Pose(entity) ?? (0, 0, 0);
        var frame = art.Frames[Math.Min(pose.Item1, art.Frames.Count - 1)];
        return AreaScenePlan.Repose(command, art.Row, body, AreaFrameKey.From(frame), frame.Width, frame.Height, frame.HotX, frame.HotY, pose.Item2, pose.Item3);
    }
}
