using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-1778 (ledger AN10, AN11, LK10, LK13, LW2): the art of the shown map's doors, windows and containers as a script's change art number (59) and the open frame leave it. A door or window or a container holds its open state in
// the frame of its art id (the session's frame), and the action writes the new art id with frame 0, so the drawn art is the art id the session holds (the new one, else the map's own) with the session's frame: a portal is given
// the new art's frames (AreaPortals.Retarget, which the open steps, the passage rule and a script's toggle read), a container is drawn from its frame (its closed and open body, also on the map's own art: the lid was not drawn before).
public partial class TerrainViewerApp
{
    // The containers of the shown map with their lock identity, T0 baseline and T0 art, read once for the map's data.
    private (AreaLoadResult Area, IReadOnlyList<(AreaObjectRecord Row, SessionOpenable Openable, SessionOpenableBaseline Baseline, uint Art)> Containers)? _containerFacts;
    // The portals given another art, and the state they were last synced for.
    private readonly HashSet<AreaObjectKey> _portalsRetargeted = [];
    private (AreaPortals Portals, GameSessionOwner Session, long Revision)? _portalArtStamp;

    private IReadOnlyList<(AreaObjectRecord Row, SessionOpenable Openable, SessionOpenableBaseline Baseline, uint Art)> ContainerFacts(AreaLoadResult area)
    {
        if (_containerFacts is { } known && ReferenceEquals(known.Area, area)) return known.Containers;
        var map = new SessionMapId(area.Snapshot.RuntimeMap);
        var facts = new List<(AreaObjectRecord, SessionOpenable, SessionOpenableBaseline, uint)>();
        foreach (var row in area.Snapshot.Objects)
            if (row.Type == ContainerType && Number(row, CurrentArtSlot) is { } art && CombatFieldReader.ReadBreakable(row).Fields is { } fields)
                facts.Add((row, new(map, AreaUseScriptHost.EntityId(row)), AreaObjectTargets.ContainerBaseline(row, fields), art));
        _containerFacts = (area, facts);
        return facts;
    }

    // The door and window rows of the shown map by key, read once per map (a portal with a script's art is drawn at every frame).
    private (AreaLoadResult Area, Dictionary<AreaObjectKey, AreaObjectRecord> Rows)? _portalRows;

    private Dictionary<AreaObjectKey, AreaObjectRecord> PortalRows(AreaLoadResult area)
    {
        if (_portalRows is { } known && ReferenceEquals(known.Area, area)) return known.Rows;
        var rows = area.Snapshot.Objects.Where(row => row.Type == CombatFieldReader.PortalType).ToDictionary(row => row.Key);
        _portalRows = (area, rows);
        return rows;
    }

    /// <summary>The art id a script wrote for the object of the shown map (AN8, AN10), null while it has the art its data gives.</summary>
    private uint? WrittenArt(string entity) => _areaSession is { } session && _readyArea is { } area ? session.QueryObjectArt(new SessionMapId(area.Snapshot.RuntimeMap), entity) : null;

    /// <summary>
    /// The containers off their T0 draw: the art id (the session's written one, else the map's own) with the session's frame, when it differs from the id the T0 body draws, as a body placed where the T0 body stood.
    /// An art id that does not read leaves the T0 body (said once).
    /// </summary>
    private IEnumerable<KeyValuePair<AreaObjectKey, AreaDrawCommand>> ContainerArtBodies(AreaLoadResult area, GameSessionOwner session, Dictionary<AreaObjectKey, AreaDrawCommand> commands,
        Dictionary<AreaVisualKey, AreaVisualRecord> visuals, Func<uint, string, AreaExtraFrame?> frameOf)
    {
        var written = session.QueryObjectArts(new SessionMapId(area.Snapshot.RuntimeMap)).ToDictionary(item => item.Entity, item => item.Art, StringComparer.Ordinal);
        foreach (var (row, openable, baseline, baseArt) in ContainerFacts(area))
        {
            var art = written.TryGetValue(openable.Entity, out var changed) ? changed : baseArt;
            var frame = session.QueryOpenable(openable, baseline).Frame;
            var drawn = (art & ~(31U << 14)) | ((uint)frame << 14);
            if (drawn == baseArt || !commands.TryGetValue(row.Key, out var command) || !visuals.TryGetValue(command.Visual, out var visual)) continue;
            if (frameOf(drawn, "container-art") is not { } found) continue;
            yield return new(row.Key, AreaScenePlan.Repose(command, row, visual, found.Key, found.Frame.Width, found.Frame.Height, found.Visual.HotX, found.Visual.HotY));
        }
    }

    /// <summary>The frame count of the art a container of the shown map has now (its written art's, else its body's): what the open step reads (ledger LW2, an ART of one frame keeps frame 0).</summary>
    private int ContainerFrameCount(AreaObjectRecord row)
    {
        if (_readyArea is not { } area) return 0;
        var map = new SessionMapId(area.Snapshot.RuntimeMap);
        if (_areaSession is { } session && session.QueryObjectArt(map, AreaUseScriptHost.EntityId(row)) is { } art && ExtraArtFrame(art & ~(31U << 14), "container-art") is { } first) return first.FramesPerGroup;
        return area.Snapshot.Visuals.FirstOrDefault(v => v.Key.Layer == AreaLayerKind.Body && v.Key.Object == row.Key)?.FrameCount ?? 0;
    }

    /// <summary>
    /// Gives the shown map's portals the art the session holds for them (a script's change art number, saved with the object, or the map's own art again): the art's facts and every frame of its group are read from the module and
    /// handed to <see cref="AreaPortals.Retarget"/>. Runs when the session or the portals changed; a portal whose new art does not read keeps its own (said once).
    /// </summary>
    private void SyncPortalArt()
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session || _readyArea is not { } area) { _portalsRetargeted.Clear(); _portalArtStamp = null; return; }
        var stamp = (portals, session, session.Version.Revision);
        if (Equals(stamp, _portalArtStamp)) return;
        if (_portalArtStamp is { } before && !ReferenceEquals(before.Portals, portals)) _portalsRetargeted.Clear();
        _portalArtStamp = stamp;
        var written = session.QueryObjectArts(new SessionMapId(area.Snapshot.RuntimeMap)).Where(item => portals.ByEntity(item.Entity) is not null).ToList();
        if (written.Count == 0 && _portalsRetargeted.Count == 0) return;
        var wanted = new Dictionary<AreaObjectKey, uint>();
        foreach (var (entity, art) in written)
        {
            var portal = portals.ByEntity(entity)!;
            // The data's own art with its frame cleared is the same art: the portal keeps the frames its map gave it.
            if (area.Snapshot.Objects.FirstOrDefault(row => row.Key == portal.Key) is { } row && Number(row, CurrentArtSlot) is { } own
                && ObjectArtNumber.WithFrameZero(own) == ObjectArtNumber.WithFrameZero(art)) continue;
            wanted[portal.Key] = art & ~(31U << 14);
        }
        foreach (var key in _portalsRetargeted.Where(key => !wanted.ContainsKey(key)).ToList())
        {
            portals.Retarget(key, null, new(false, 0, 1), 0, []);
            _portalsRetargeted.Remove(key);
        }
        foreach (var (key, art) in wanted)
        {
            if (portals.TryGet(key, out var current) && current.ArtId == art) continue;
            if (!PortalArtFacts(art, out var facts, out var frames, out var rate)) continue;
            if (portals.Retarget(key, art, facts, rate, frames)) _portalsRetargeted.Add(key);
        }
    }

    // Every frame of a door or window art's group read from the module: the art's facts (door or window, rotation, frame count), its frame rate and the frames in frame order. False when any does not read.
    private bool PortalArtFacts(uint art, out PortalArt facts, out List<AreaVisualRecord> frames, out int rate)
    {
        facts = default; frames = []; rate = 0;
        if (ExtraArtFrame(art, "portal-art") is not { } first) return false;
        var count = Math.Clamp(first.FramesPerGroup, 1, 32);
        for (var index = 0; index < count; index++)
        {
            if ((index == 0 ? first : ExtraArtFrame(art | ((uint)index << 14), "portal-art")) is not { } frame) return false;
            frames.Add(frame.Visual);
        }
        facts = new(((art >> 10) & 1) != 0, (int)((art >> 11) & 7), count);
        rate = first.FramesPerSecond;
        return true;
    }
}
