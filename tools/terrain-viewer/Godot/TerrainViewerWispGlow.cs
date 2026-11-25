using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-2064 (FR-LIGHT-011, FR-LIGHT-013): the soft glow of the wisps (the glowing-orb creatures), a presentation difference of our own that shows only in the relit look of the HD
// graphics option (AreaRelight.Active); the Legacy lattice never gets it. The glow is keyed by the creature prototype (AreaWispGlows.Table), so every wisp of a kind on every map has it,
// and it stands where its creature stands: at the row's T0 cell, or at the pose of the AI host's mover or the companion group (the same poses the carried lights use, LT10, LT12), and it
// ends with a dead or gone critter. Nothing is written to the saved object data. Not ready: a wisp made at run time (a summoned critter) has no map row and no glow.
public partial class TerrainViewerApp
{
    private AreaLoadResult? _wispRowsFor;
    private IReadOnlyList<(AreaObjectRecord Row, WispGlow Glow)> _wispRows = [];
    private Func<IReadOnlyList<AreaRelight.Light>>? _wispSource;
    private readonly List<AreaRelight.Light> _wispLights = [];

    /// <summary>The wisps' glow in the relit look: on (the default); <c>ARC_RELIGHT_WISPS=off</c> takes it away for a comparison (the "before" of a capture) and <see cref="SetWispGlow"/> does the same for a smoke.</summary>
    internal static bool WispGlowOn { get; private set; } = !string.Equals(OS.GetEnvironment("ARC_RELIGHT_WISPS"), "off", StringComparison.OrdinalIgnoreCase);
    /// <summary>The owner's tuning knob for the glow's strength: a factor on every kind's own strength (<c>ARC_RELIGHT_WISP_STRENGTH</c>, 1 by default).</summary>
    internal static float WispStrengthFactor { get; } = float.TryParse(OS.GetEnvironment("ARC_RELIGHT_WISP_STRENGTH"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor >= 0 ? factor : 1f;
    internal void SetWispGlow(bool on) { WispGlowOn = on; _canvas?.RefreshRelight(); }

    /// <summary>The wisps' glows for the relit look as lights in world pixels (what the canvas appends to the carried lights at each submission); none while no map is shown. ARC-RENDER-3127: the list is reused by the next call (the canvas copies it at once).</summary>
    private IReadOnlyList<AreaRelight.Light> WispRelitLights()
    {
        if (!WispGlowOn || _readyArea is not { } area || area.Lighting?.Local is not { } local) return [];
        if (!ReferenceEquals(_wispRowsFor, area)) { _wispRows = AreaWispGlows.Rows(area.Snapshot.Objects); _wispRowsFor = area; }
        if (_wispRows.Count == 0) return [];
        var poses = CritterPoses();
        var absent = _areaCompanions?.Absent;
        var host = _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) ? ai : null;
        var entities = host is null ? null : WispEntities(host);
        var lights = _wispLights; lights.Clear();
        foreach (var (row, glow) in _wispRows)
        {
            if (absent?.Contains(row.Key) == true) continue;
            poses.TryGetValue(row.Key, out var pose);
            var stood = AreaWispGlows.Place(row, glow, poses.ContainsKey(row.Key) ? pose : null);
            var tile = TerrainGeometry.Project(checked((int)(stood.CellX - local.Chart.OriginX)), checked((int)(stood.CellY - local.Chart.OriginY)));
            var light = new AreaRelight.Light(tile.X + stood.OffsetX + 40f, tile.Y + stood.OffsetY + 20f - glow.Lift, glow.Radius,
                new Color(glow.Red / 255f * glow.Strength * WispStrengthFactor, glow.Green / 255f * glow.Strength * WispStrengthFactor, glow.Blue / 255f * glow.Strength * WispStrengthFactor));
            // The cheap test first: a wisp whose glow cannot reach the view costs no life query.
            if (!AreaRelight.ReachesView(light)) continue;
            if (entities is not null && entities.TryGetValue(row.Key, out var entity) && (host!.Gone(entity) || host.DeadCritter(entity))) continue;
            lights.Add(light);
        }
        return lights;
    }

    // The AI host's entity of each wisp row (built again when the host's scene changes; a duplicate key keeps the first).
    private AreaAiScene? _wispEntitiesScene;
    private Dictionary<AreaObjectKey, string> _wispEntities = [];
    private Dictionary<AreaObjectKey, string> WispEntities(AreaAiHost host)
    {
        if (ReferenceEquals(_wispEntitiesScene, host.Scene)) return _wispEntities;
        var map = new Dictionary<AreaObjectKey, string>();
        foreach (var npc in host.Scene.Npcs) map.TryAdd(npc.Row.Key, npc.Entity);
        _wispEntitiesScene = host.Scene; return _wispEntities = map;
    }

    /// <summary>Per frame: gives the canvas the wisps' glow source (a no-op once it has it).</summary>
    private void TickWispGlow() => _canvas?.SetAreaRelitExtra(_wispSource ??= WispRelitLights);
}
