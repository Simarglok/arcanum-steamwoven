using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A map the viewer's map mode can open: its runtime and catalogue numbers and its folder name.</summary>
public sealed record AreaMapDestination(int Runtime, int Catalogue, string Name);

/// <summary>A queued map-mode transfer: the transition site that set it off, the map it leaves, the destination and the
/// authored arrival tile. <see cref="Trigger"/> is the used object (null for a jump point or a tile entry).</summary>
public sealed record AreaMapTransfer(MapTransitionSite Site, int FromMap, AreaMapDestination To, AreaStepCell Arrival, AreaObjectKey? Trigger)
{
    public bool SameMap => To.Runtime == FromMap;
}

/// <summary>The answer to one map-mode use of an object or entry of a tile.</summary>
public sealed record AreaMapUseResult(AreaUseOutcome Outcome, MapTransitionSite? Site, int Distance, string Reason);

/// <summary>
/// ARC-WORLD-031: the unconditional transitions of the map shown in map mode (ARC-DATA-025, read from the content
/// stack for any map). A use click on a trigger object follows the ARC-WORLD-009 use rule
/// (<see cref="AreaUseRule"/>); a jump point or an unconditional tile script fires when the player enters its tile
/// (ledger TR3: the jump point first). One transfer is pending at a time (TR4). A destination on the same map needs an
/// admitted arrival; another map must be one the viewer can open (listed, with 1 to 256 sector files), otherwise the
/// transfer is refused with the reason. Caller-serialized; no I/O.
/// </summary>
public sealed class AreaMapTransfers
{
    private readonly Dictionary<AreaObjectKey, (MapTransitionSite Site, AreaObjectRecord Object)> _uses = new();
    private readonly Dictionary<AreaStepCell, MapTransitionSite> _cells = new();
    private readonly IReadOnlyDictionary<int, AreaMapDestination> _destinations;
    /// <summary>Every map map mode can open, by runtime number (ARC-WORLD-038: a quick load to another map).</summary>
    public IReadOnlyDictionary<int, AreaMapDestination> Destinations { get; }

    public AreaMapTransfers(AreaMapRuntime map, MapTransitionSet transitions, IReadOnlyDictionary<int, AreaMapDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(transitions); ArgumentNullException.ThrowIfNull(destinations);
        if (transitions.Entry is not { } entry || entry.Runtime != map.RuntimeMap) throw new ArgumentException("AREA_MAP_TRANSFERS_MAP_MISMATCH");
        Map = map; _destinations = destinations;
        Destinations = destinations;
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
    }

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
    public AreaObjectRecord Object(AreaObjectKey key) => _uses[key].Object;

    public AreaMapUseResult RequestUse(AreaObjectKey trigger, AreaStepCell actor)
    {
        if (Pending is not null) return new(AreaUseOutcome.TransitionPending, Pending.Site, -1, "TRANSITION_PENDING");
        if (!_uses.TryGetValue(trigger, out var use)) return new(AreaUseOutcome.NotATrigger, null, -1, "NO_TRANSITION_FOR_OBJECT");
        var objectCell = new AreaStepCell(use.Object.WorldX!.Value, use.Object.WorldY!.Value);
        var check = AreaUseRule.Check(Map, trigger, objectCell, actor);
        if (check.Outcome != AreaUseOutcome.Accepted) return new(check.Outcome, use.Site, check.Distance, check.Reason);
        return Queue(use.Site, trigger, check.Distance);
    }

    /// <summary>The player entered <paramref name="cell"/>: a jump point or an unconditional tile script there queues its
    /// transfer. Null when the tile has none; a refused one returns its reason.</summary>
    public AreaMapUseResult? Enter(AreaStepCell cell)
    {
        if (!_cells.TryGetValue(cell, out var site)) return null;
        if (Pending is not null) return new(AreaUseOutcome.TransitionPending, site, 0, "TRANSITION_PENDING");
        return Queue(site, null, 0);
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

    private AreaMapUseResult Queue(MapTransitionSite site, AreaObjectKey? trigger, int distance)
    {
        if (Refusal(site) is { } refusal) return new(AreaUseOutcome.DestinationUnavailable, site, distance, refusal);
        Pending = new(site, Map.RuntimeMap, _destinations[site.DestinationRuntime], new(site.Arrival.X, site.Arrival.Y), trigger);
        return new(AreaUseOutcome.Accepted, site, distance, "QUEUED");
    }

    /// <summary>Drops the pending transfer (applied, refused or cancelled); returns it, or null when none was pending.</summary>
    public AreaMapTransfer? Clear()
    {
        var pending = Pending; Pending = null;
        return pending;
    }
}
