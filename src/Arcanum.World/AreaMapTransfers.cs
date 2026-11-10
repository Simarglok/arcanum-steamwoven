using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Scr;

namespace Arcanum.World;

/// <summary>A map the viewer's map mode can open: its runtime and catalogue numbers and its folder name.</summary>
public sealed record AreaMapDestination(int Runtime, int Catalogue, string Name);

/// <summary>A queued map-mode transfer: the transition site that set it off, the map it leaves, the destination and the
/// authored arrival tile. <see cref="Trigger"/> is the used object (null for a jump point or a tile entry).</summary>
public sealed record AreaMapTransfer(MapTransitionSite Site, int FromMap, AreaMapDestination To, AreaStepCell Arrival, AreaObjectKey? Trigger)
{
    public bool SameMap => To.Runtime == FromMap;

    /// <summary>ARC-DEV-113: the extras of a script's fade-and-teleport (action 102, ledger TR2), or null for a jump point, a plain
    /// teleport (action 44) and an unconditional site, which request with flags 0 (TR1, TR3).</summary>
    public ScrTeleportFade? Fade { get; init; }
}

/// <summary>The answer to one map-mode use of an object or entry of a tile.</summary>
public sealed record AreaMapUseResult(AreaUseOutcome Outcome, MapTransitionSite? Site, int Distance, string Reason);

/// <summary>
/// ARC-WORLD-031: the unconditional transitions of the map shown in map mode (ARC-DATA-025, read from the content
/// stack for any map). A use click on a trigger object follows the ARC-WORLD-009 use rule
/// (<see cref="AreaUseRule"/>); a jump point or an unconditional tile script fires when the player enters its tile
/// (ledger TR3: the jump point first). One transfer is pending at a time (TR4). A destination on the same map needs an
/// admitted arrival; another map must be one the viewer can open (listed, with 1 to 256 sector files), otherwise the
/// transfer is refused with the reason. Since ARC-SCRIPT-015 a guarded use site runs its use script, and since
/// ARC-SCRIPT-017 every tile and sector script the player enters runs (ledger TS2), through the host's runners; a script's
/// teleport of the player is the transfer. Caller-serialized; no I/O.
/// </summary>
public sealed class AreaMapTransfers
{
    private readonly Dictionary<AreaObjectKey, (MapTransitionSite Site, AreaObjectRecord Object)> _uses = new();
    private readonly Dictionary<AreaStepCell, MapTransitionSite> _cells = new();
    // ARC-SCRIPT-015: use sites whose teleport stands behind guards or a computed destination; their script decides.
    private readonly Dictionary<AreaObjectKey, (MapTransitionSite Site, AreaObjectRecord Object)> _scripted = new();
    // ARC-SCRIPT-017: every tile script by its tile and every sector script by its sector, teleport or not.
    private readonly Dictionary<AreaStepCell, MapPlacedScript> _tiles = new();
    private readonly Dictionary<WorldSectorCoordinate, MapPlacedScript> _sectors = new();
    private readonly IReadOnlyDictionary<int, AreaMapDestination> _destinations;
    /// <summary>ARC-SCRIPT-050: the shown map's transitions with its map-list row (get area reads its area).</summary>
    public MapTransitionSet Transitions { get; }
    /// <summary>Every map map mode can open, by runtime number (ARC-WORLD-038: a quick load to another map).</summary>
    public IReadOnlyDictionary<int, AreaMapDestination> Destinations { get; }

    /// <param name="templateScripts">ARC-WORLD-081: the sector scripts of a window's template sectors
    /// (<see cref="MapTransitions.TemplateSectorScripts"/>), which run on entry as an own sector's does.</param>
    public AreaMapTransfers(AreaMapRuntime map, MapTransitionSet transitions, IReadOnlyDictionary<int, AreaMapDestination> destinations,
        IEnumerable<MapPlacedScript>? templateScripts = null)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(transitions); ArgumentNullException.ThrowIfNull(destinations);
        if (transitions.Entry is not { } entry || entry.Runtime != map.RuntimeMap) throw new ArgumentException("AREA_MAP_TRANSFERS_MAP_MISMATCH");
        Map = map; _destinations = destinations;
        Destinations = destinations;
        Transitions = transitions;
        var statics = map.Snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Static && o.Key.Sector is not null)
            .ToDictionary(o => (o.Key.Sector!.Value, o.Key.SourceIndex));
        // A mobile has its own unique id; its record ordinal depends on the source (aggregate or editor files).
        var mobiles = map.Snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).GroupBy(o => o.Key.ObjectIdSha256).ToDictionary(g => g.Key, g => g.First());
        // Per trigger the first site in chain length then line order; jump points before tile scripts, as the original
        // runs them on a tile change (TR3).
        foreach (var site in transitions.Unconditional.OrderBy(s => s.Trigger == MapTransitionTrigger.JumpPoint ? 0 : 1).ThenBy(s => s.Chain.Count).ThenBy(s => s.Line))
        {
            if (site.Trigger is MapTransitionTrigger.JumpPoint or MapTransitionTrigger.TileEntry)
            {
                _cells.TryAdd(new(site.Location.X, site.Location.Y), site);
                continue;
            }
            if (site.Trigger != MapTransitionTrigger.Use || site.Object is not { } owner) continue;
            var row = owner.Sector is { } sector ? statics.GetValueOrDefault((sector.Id, owner.Index)) : mobiles.GetValueOrDefault(owner.OwnIdSha256);
            // The owner's own id digest must agree (sector objects with a null id share one digest, so the sector and ordinal decide).
            if (row is null || row.Key.ObjectIdSha256 != owner.OwnIdSha256 || row.WorldX is null || row.WorldY is null) { Unmatched++; continue; }
            _uses.TryAdd(row.Key, (site, row));
        }
        Exits = transitions.Unconditional.Count();
        foreach (var site in transitions.Sites.Where(s => !s.Unconditional && s.Trigger == MapTransitionTrigger.Use && (s.Guards & ~ScriptDecides) == 0)
            .OrderBy(s => s.Chain.Count).ThenBy(s => s.Line))
        {
            if (site.Object is not { } owner) continue;
            var row = owner.Sector is { } sector ? statics.GetValueOrDefault((sector.Id, owner.Index)) : mobiles.GetValueOrDefault(owner.OwnIdSha256);
            if (row is null || row.Key.ObjectIdSha256 != owner.OwnIdSha256 || row.WorldX is null || row.WorldY is null || _uses.ContainsKey(row.Key)) continue;
            _scripted.TryAdd(row.Key, (site, row));
        }
        foreach (var placed in transitions.Placed.Concat(templateScripts ?? []))
            if (placed.Trigger == MapTransitionTrigger.TileEntry) _tiles.TryAdd(new(placed.Location.X, placed.Location.Y), placed);
            else if (placed.Trigger == MapTransitionTrigger.SectorEntry) _sectors.TryAdd(placed.Sector, placed);
    }

    /// <summary>ARC-SCRIPT-017: runs a tile or sector script of this map for the player and returns what it asked for; set
    /// by the host. The second argument names why a teleport cannot be taken, as for <see cref="ScriptRunner"/>. Without it a
    /// tile fires only its jump point or unconditional tile teleport (ARC-WORLD-031) and a sector nothing.</summary>
    public Func<MapPlacedScript, Func<ScrTeleport, string?>, AreaScriptedUse>? PlacedRunner { get; set; }

    /// <summary>ARC-SCRIPT-046: runs the tile script of this map for an NPC (by its identity: the map whose stored objects hold it and its session
    /// entity, which is another map's for a companion carried here, ARC-DEV-328) that entered the tile, with the NPC as the triggerer, and returns
    /// what it did; set by the host. Without it an NPC's step fires nothing.</summary>
    public Func<MapPlacedScript, ScrObjectRef, AreaScriptedUse>? CritterTileRunner { get; set; }

    /// <summary>
    /// ARC-SCRIPT-046: an NPC (<paramref name="entity"/>) entered <paramref name="cell"/>: the original runs
    /// the tile's script for a PC or an NPC after every move (ledger TR3), the jump point for a PC only, so only the tile's script runs,
    /// through <see cref="CritterTileRunner"/>; the run's teleport is the runner's to refuse. Null when the tile has no script or no runner
    /// is set. <see cref="LastScripted"/> stays the player's. <paramref name="homeMap"/> is the runtime map whose stored objects hold the NPC
    /// (ARC-DEV-328: a companion carried here has its own; null is this map): the original keeps one object wherever it stands, so a visitor
    /// runs the tile's script as any NPC of the map does (ledger TG2, TG9).
    /// </summary>
    public AreaScriptedUse? EnterByCritter(AreaStepCell cell, string entity, int? homeMap = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(entity);
        return CritterTileRunner is { } runner && _tiles.TryGetValue(cell, out var placed) ? runner(placed, new ScrObjectRef(homeMap ?? Map.RuntimeMap, entity)) : null;
    }

    /// <summary>ARC-SCRIPT-046: the script of a tile, or null.</summary>
    public MapPlacedScript? TileScript(AreaStepCell cell) => _tiles.GetValueOrDefault(cell);

    /// <summary>ARC-SCRIPT-017: the tiles and sectors with a script.</summary>
    public IReadOnlyCollection<AreaStepCell> ScriptTiles => _tiles.Keys;
    public IReadOnlyCollection<WorldSectorCoordinate> ScriptSectors => _sectors.Keys;
    /// <summary>ARC-WORLD-081: the script a sector runs on entry (its own file's or its template's), or null.</summary>
    public MapPlacedScript? SectorScript(WorldSectorCoordinate sector) => _sectors.GetValueOrDefault(sector);

    /// <summary>ARC-SCRIPT-015: the guards a scripted use runs its script for (a conditional path, a computed destination);
    /// any other guard (another object moved, a trap, a container's loot screen, an object a click cannot pick) keeps the
    /// site out.</summary>
    public const MapTransitionGuard ScriptDecides = MapTransitionGuard.ConditionalPath | MapTransitionGuard.ComputedDestination;

    /// <summary>ARC-SCRIPT-015: runs a scripted use's script for its object and returns what it asked for; set by the host.
    /// The third argument names why a teleport cannot be taken (<see cref="Refusal"/> of the resolved site), so the runner
    /// applies nothing then. Without a runner a scripted use is refused.</summary>
    public Func<MapTransitionSite, AreaObjectRecord, Func<ScrTeleport, string?>, AreaScriptedUse>? ScriptRunner { get; set; }

    /// <summary>ARC-SCRIPT-015: the objects whose use runs their script to decide the transfer.</summary>
    public IReadOnlyCollection<AreaObjectKey> ScriptedTriggers => _scripted.Keys;
    public MapTransitionSite? ScriptedSite(AreaObjectKey key) => _scripted.TryGetValue(key, out var use) ? use.Site : null;

    /// <summary>The script run of the last <see cref="RequestUse"/>, <see cref="Enter"/> or <see cref="EnterSector"/> (its
    /// printed lines and detail), or null when it ran none; for a tile with a jump point and a script, the tile's run.</summary>
    public AreaScriptedUse? LastScripted { get; private set; }

    public AreaMapRuntime Map { get; }
    public AreaMapTransfer? Pending { get; private set; }
    /// <summary>Unconditional sites of the map (jump points, use and tile sites).</summary>
    public int Exits { get; }
    /// <summary>Use sites whose object the map's snapshot does not hold (not drawn, or outside the publication).</summary>
    public int Unmatched { get; }
    public IReadOnlyCollection<AreaObjectKey> Triggers => _uses.Keys;
    public IReadOnlyCollection<AreaStepCell> TriggerCells => _cells.Keys;
    public MapTransitionSite? Site(AreaObjectKey key) => _uses.TryGetValue(key, out var use) ? use.Site : null;
    public MapTransitionSite? CellSite(AreaStepCell cell) => _cells.GetValueOrDefault(cell);
    public AreaObjectRecord Object(AreaObjectKey key) => _uses.TryGetValue(key, out var use) ? use.Object : _scripted[key].Object;

    public AreaMapUseResult RequestUse(AreaObjectKey trigger, AreaStepCell actor)
    {
        LastScripted = null;
        if (Pending is not null) return new(AreaUseOutcome.TransitionPending, Pending.Site, -1, "TRANSITION_PENDING");
        var scripted = !_uses.TryGetValue(trigger, out var use) && _scripted.TryGetValue(trigger, out use);
        if (use.Site is null) return new(AreaUseOutcome.NotATrigger, null, -1, "NO_TRANSITION_FOR_OBJECT");
        var objectCell = new AreaStepCell(use.Object.WorldX!.Value, use.Object.WorldY!.Value);
        var check = AreaUseRule.Check(Map, trigger, objectCell, actor);
        if (check.Outcome != AreaUseOutcome.Accepted) return new(check.Outcome, use.Site, check.Distance, check.Reason);
        if (!scripted) return Queue(use.Site, trigger, check.Distance);
        // ARC-SCRIPT-015: the script decides; its teleport of the player gives the destination.
        if (ScriptRunner is not { } runner) return new(AreaUseOutcome.ScriptRefused, use.Site, check.Distance, "NO_SCRIPT_RUNNER");
        var site = use.Site;
        var ran = LastScripted = runner(site, use.Object, teleport => Refusal(Teleported(site, teleport)));
        return ran.Outcome switch
        {
            AreaScriptedUseOutcome.Teleport => Queue(Teleported(site, ran.Teleport!), trigger, check.Distance, ran.Teleport!.Fade),
            AreaScriptedUseOutcome.TeleportRefused => new(AreaUseOutcome.DestinationUnavailable, Teleported(site, ran.Teleport!), check.Distance, ran.Detail),
            AreaScriptedUseOutcome.NoTeleport => new(AreaUseOutcome.ScriptEnded, use.Site, check.Distance, ran.Detail),
            _ => new(AreaUseOutcome.ScriptRefused, use.Site, check.Distance, ran.Detail),
        };
    }

    /// <summary>
    /// The player entered <paramref name="cell"/>: its jump point queues its transfer (ledger TR3, first); then, with a
    /// <see cref="PlacedRunner"/>, the tile's script runs and its teleport of the player is the transfer; without a runner,
    /// or when the run was refused before it started (outside the set, no content), an unconditional tile teleport there
    /// queues its transfer. Empty when the tile has none; a refused one returns its
    /// reason. A teleport asked while another transfer is pending is dropped and the run's other effects stay (TR4).
    /// </summary>
    public IReadOnlyList<AreaMapUseResult> Enter(AreaStepCell cell)
    {
        LastScripted = null;
        var results = new List<AreaMapUseResult>(2);
        var site = _cells.GetValueOrDefault(cell);
        if (site is { Trigger: MapTransitionTrigger.JumpPoint }) results.Add(Pending is not null ? new(AreaUseOutcome.TransitionPending, site, 0, "TRANSITION_PENDING") : Queue(site, null, 0));
        var ran = PlacedRunner is not null && _tiles.TryGetValue(cell, out var placed) ? RunPlaced(placed) : null;
        // A run that did not happen (outside the set, no content, no session) leaves the tile's unconditional teleport
        // (ARC-WORLD-031) to fire as before.
        if (ran is not null && LastScripted?.Outcome is not (AreaScriptedUseOutcome.Refused or AreaScriptedUseOutcome.ContentUnavailable)) results.Add(ran);
        else if (site is { Trigger: MapTransitionTrigger.TileEntry })
            results.Add(Pending is not null ? new(AreaUseOutcome.TransitionPending, site, 0, "TRANSITION_PENDING") : Queue(site, null, 0));
        else if (ran is not null) results.Add(ran);
        return results;
    }

    /// <summary>ARC-SCRIPT-017: the player stepped from <paramref name="from"/> into <paramref name="to"/>; when that enters
    /// another sector whose script exists, the script runs (with a <see cref="PlacedRunner"/>), as the original runs a
    /// sector script when the player enters its sector. Null otherwise.</summary>
    public AreaMapUseResult? EnterSector(AreaStepCell from, AreaStepCell to)
    {
        var sector = WorldSectorCoordinate.Containing(new(to.X, to.Y));
        if (PlacedRunner is null || sector == WorldSectorCoordinate.Containing(new(from.X, from.Y)) || !_sectors.TryGetValue(sector, out var placed)) return null;
        LastScripted = null;
        return RunPlaced(placed);
    }

    private AreaMapUseResult RunPlaced(MapPlacedScript placed)
    {
        var site = new MapTransitionSite(placed.Trigger, placed.Location, null, placed.Script, [placed.Script], 0, false, false, Map.Snapshot.CatalogueMap,
            Map.RuntimeMap, placed.Location, MapTransitionGuard.None);
        // A pending transfer refuses the teleport only: the run applies and its teleport is dropped (TR4).
        var ran = LastScripted = PlacedRunner!(placed, teleport => Pending is not null ? null : Refusal(Teleported(site, teleport)));
        return ran.Outcome switch
        {
            AreaScriptedUseOutcome.Teleport when Pending is not null => new(AreaUseOutcome.TransitionPending, Teleported(site, ran.Teleport!), 0, "TRANSITION_PENDING"),
            AreaScriptedUseOutcome.Teleport => Queue(Teleported(site, ran.Teleport!), null, 0, ran.Teleport!.Fade),
            AreaScriptedUseOutcome.TeleportRefused => new(AreaUseOutcome.DestinationUnavailable, Teleported(site, ran.Teleport!), 0, ran.Detail),
            AreaScriptedUseOutcome.NoTeleport => new(AreaUseOutcome.ScriptEnded, site, 0, ran.Detail),
            _ => new(AreaUseOutcome.ScriptRefused, site, 0, ran.Detail),
        };
    }

    /// <summary>The site as the script's teleport resolved it: its destination and arrival (a runtime −1 keeps this map, as a
    /// pending map of −1 does, ledger TR4).</summary>
    public MapTransitionSite Teleported(MapTransitionSite site, ScrTeleport teleport)
    {
        ArgumentNullException.ThrowIfNull(site); ArgumentNullException.ThrowIfNull(teleport);
        var here = teleport.RuntimeMap == -1;
        var runtime = here ? Map.RuntimeMap : teleport.RuntimeMap;
        var catalogue = here ? Map.Snapshot.CatalogueMap : teleport.CatalogueMap;
        return site with { DestinationCatalogue = catalogue, DestinationRuntime = runtime, Arrival = new(teleport.X, teleport.Y) };
    }

    /// <summary>Why a site's transfer cannot be queued (its destination is not opened in map mode, or a same-map arrival is
    /// not admitted), or null when it can.</summary>
    public string? Refusal(MapTransitionSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (!_destinations.TryGetValue(site.DestinationRuntime, out var destination)) return $"DESTINATION_NOT_OPENED_IN_MAP_MODE {site.DestinationCatalogue}";
        var arrival = new AreaStepCell(site.Arrival.X, site.Arrival.Y);
        if (destination.Runtime == Map.RuntimeMap && (!Map.Steps.IsAdmitted(arrival) || !Map.Chart.Contains(Map.Chart.ToChart(arrival)))) return "ARRIVAL_NOT_ADMITTED";
        return null;
    }

    private AreaMapUseResult Queue(MapTransitionSite site, AreaObjectKey? trigger, int distance, ScrTeleportFade? fade = null)
    {
        if (Refusal(site) is { } refusal) return new(AreaUseOutcome.DestinationUnavailable, site, distance, refusal);
        Pending = new(site, Map.RuntimeMap, _destinations[site.DestinationRuntime], new(site.Arrival.X, site.Arrival.Y), trigger) { Fade = fade };
        return new(AreaUseOutcome.Accepted, site, distance, "QUEUED");
    }

    /// <summary>Drops the pending transfer (applied, refused or cancelled); returns it, or null when none was pending.</summary>
    public AreaMapTransfer? Clear()
    {
        var pending = Pending; Pending = null;
        return pending;
    }
}
