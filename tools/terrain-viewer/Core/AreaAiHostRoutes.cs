using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, RL17, ledger WS1–WS12): the fold's waypoint and standpoint steps of the AI host. The tick rules call them on the wake-up
/// marker of a leaderless NPC outside combat mode (<see cref="AiSchedulerRules.Tick"/>); the rules of the step are
/// <see cref="NpcRouteRules.ForcedWaypoints"/> and <see cref="NpcRouteRules.ForcedStandpoint"/>, over the facts of the NPC's own object row
/// (<see cref="AreaNpcRouteReader"/>), the session clock's hour and the open map. The host applies the outcome: a relocation interrupts the NPC's
/// run and stands it on the target cell as a restored place (the session's <c>npc.pos</c>, so a load or a rebuilt host keeps it).
/// Left out (NOT READY, each named in the ledger): a teleport request to another map (MIG11), the bed lookup, the sleep and the wander of
/// the standpoint step, a sleeper's bed re-seat, the original's stored NPC flag 0x200 and the teleport map the step writes, and the saved waypoint
/// index: the saved schema holds none (S6 may add it), so the index lives here for the host's life and a rebuilt host recovers it from the saved place
/// when the NPC stands exactly on one of its waypoints (<see cref="RouteIndexOf"/>).
/// </summary>
public sealed partial class AreaAiHost
{
    // The route of each NPC row, read once (null: the row's route fields are not readable, so the NPC has no route step).
    private readonly Dictionary<string, AreaNpcRoute?> _routes = new(StringComparer.Ordinal);
    // The waypoint index each NPC's steps stored (field 0x120 of the original); absent: the row's own.
    private readonly Dictionary<string, int> _routeIndex = new(StringComparer.Ordinal);

    // How many route steps each outcome had, waypoint and standpoint together (the smoke prints them).
    private readonly Dictionary<NpcRouteOutcome, int> _routeOutcomes = [];

    /// <summary>How many fold steps (waypoint and standpoint) ended in each outcome since the host was built.</summary>
    public IReadOnlyDictionary<NpcRouteOutcome, int> RouteOutcomes => _routeOutcomes;

    /// <summary>The waypoint index the NPC's steps work from, or null when the NPC has no route: the one a step stored in this host; else, for an NPC the
    /// session left on a cell that is exactly one of its waypoints (a relocation of an earlier host, restored from the saved place), that waypoint's index;
    /// else the row's own. The original keeps the index in field 0x120, saved with the object; here it is recovered from the saved place, so a rebuilt
    /// host goes on from the waypoint the NPC stands on (an NPC that has left its waypoint since falls back to the row's index).</summary>
    public int? RouteIndexOf(string npc) => RouteOf(npc) is { } route ? RouteIndex(npc, route) : null;

    private int RouteIndex(string npc, AreaNpcRoute route)
    {
        if (_routeIndex.TryGetValue(npc, out var held)) return held;
        if (!Movers.IsMoved(npc) || Tile(npc) is not { } cell || cell.X < 0 || cell.Y < 0) return route.WaypointIndex;
        var here = new AreaRouteLocation((uint)cell.X, (uint)cell.Y);
        if (route.WaypointIndex >= 0 && route.WaypointIndex < route.Waypoints.Count && route.Waypoints[route.WaypointIndex] == here) return route.WaypointIndex;
        var found = route.Waypoints.ToList().IndexOf(here);
        return found >= 0 ? found : route.WaypointIndex;
    }

    private bool RunRouteStep(string npc, Func<NpcRouteFacts, NpcRouteStep> rule, bool waypoints)
    {
        if (RouteFacts(npc) is not { } facts) return false;
        var step = rule(facts);
        _routeOutcomes[step.Outcome] = _routeOutcomes.GetValueOrDefault(step.Outcome) + 1;
        if (step.NewWaypointIndex is { } index) _routeIndex[npc] = index;
        var what = waypoints ? "waypoint" : "standpoint";
        switch (step.Outcome)
        {
            case NpcRouteOutcome.Relocate:
                Trace(npc).Route = $"{what} {step.Outcome}: {Relocate(npc, step.Target!.Value, facts)}";
                break;
            case NpcRouteOutcome.RequestTeleport:
                // The change-of-place request of MIG11 (RL21) is not here: the NPC stays where it stands.
                Trace(npc).Route = $"{what} {step.Outcome} to map {step.TeleportMap} ({step.Target!.Value.X},{step.Target.Value.Y}): not run";
                break;
            default:
                Trace(npc).Route = $"{what} {step.Outcome}";
                break;
        }
        return step.Answer;
    }

    private AreaNpcRoute? RouteOf(string npc)
    {
        if (_routes.TryGetValue(npc, out var held)) return held;
        var route = _npcs.TryGetValue(npc, out var row) && AreaNpcRouteReader.Read(row.Row) is { Status: AreaNpcRouteStatus.Ready, Route: { } read } ? read : null;
        _routes[npc] = route;
        return route;
    }

    // The facts of one step; null when the host holds no NPC row of this id (a companion of another map's host, an unreadable route).
    private NpcRouteFacts? RouteFacts(string npc)
    {
        if (RouteOf(npc) is not { } route || Tile(npc) is not { } cell || cell.X < 0 || cell.Y < 0) return null;
        return new(Sleeping(npc), Hidden(npc), Leader(npc) is not null, _session.QueryClockSnapshot().Hour, route.NpcFlags, route.TeleportMap, _scene.RuntimeMap,
            new((uint)cell.X, (uint)cell.Y), [.. route.Waypoints.Select(w => new RouteLocation(w.X, w.Y))], RouteIndex(npc, route),
            new(route.StandpointDay.X, route.StandpointDay.Y), new(route.StandpointNight.X, route.StandpointNight.Y));
    }

    // The original's relocation (0x424070 at priority 4, then 0x43E770 with no offset): the run ends and the NPC stands on the target. A mover that
    // exists keeps its facing; an NPC that never moved is given the mover's default (+x), as after any first step or turn of the host (the original
    // keeps the object's own rotation: NOT READY, the rows' rotation is not read here). A target the shown map cannot hold (outside the chart, not admitted, the hero's own cell) leaves the NPC where it stands.
    private string Relocate(string npc, RouteLocation target, NpcRouteFacts facts)
    {
        DropRun(npc);
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (Tile(npc) == cell) return $"already on {cell.X},{cell.Y}";
        if (!PlaceHeld(cell)) return $"target {cell.X},{cell.Y} not held by the shown map";
        var row = _npcs[npc];
        Movers.Restore(npc, row.Row.Key, row.Origin, cell, Movers.FacingOf(npc, NeighborDirection.XPlus));
        RecordPlace(npc);
        return $"{facts.Location.X},{facts.Location.Y} -> {cell.X},{cell.Y}";
    }

    // Whether the shown map holds a resident on this world cell: it lies on the chart, its tile is admitted and the hero is not on it.
    private bool PlaceHeld(CellCoord world) =>
        _scene.Chart.Contains(ToChart(world)) && _scene.Steps.IsAdmitted(new AreaStepCell(world.X, world.Y)) && _scene.HeroCell() != world;
}
