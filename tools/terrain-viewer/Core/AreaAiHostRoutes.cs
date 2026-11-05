using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, RL17, ledger WS1–WS12): the fold's waypoint and standpoint steps of the AI host. The tick rules call them on the wake-up
/// marker of a leaderless NPC outside combat mode (<see cref="AiSchedulerRules.Tick"/>); the rules of the step are
/// <see cref="NpcRouteRules.ForcedWaypoints"/> and <see cref="NpcRouteRules.ForcedStandpoint"/>, over the facts of the NPC's own object row
/// (<see cref="AreaNpcRouteReader"/>), the session clock's hour and the open map. The host applies the outcome: a relocation interrupts the NPC's
/// run and stands it on the target cell as a restored place (the session's <c>npc.pos</c>, so a load or a rebuilt host keeps it).
/// ARC-DEV-116 (MIG11 S5, FM10, GR15, ledger WS3, WS6): the teleport map is the session's (<see cref="GameSessionOwner.QueryTeleportMap"/>, written by
/// every change of place and by the first step of a leaderless NPC); a companion of the shown map is a route NPC too (its row is its home map's), and a
/// former follower whose teleport map is another map than the shown one is sent home: the step's request goes through the session's change of place
/// (<see cref="SendHome"/>, caller <see cref="ChangeOfPlaceCaller.NpcWaypoint"/>, flags 0), which the viewer pings once per frame.
/// Left out (NOT READY, each named in the ledger): the bed lookup, the sleep and the wander of the standpoint step, a sleeper's bed re-seat,
/// the original's stored NPC flag 0x200, and the saved waypoint
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
        // 0x45F6D0 (WS3): the teleport map is written with the open map while it is 0, also when the step then ends.
        var written = step.TeleportMapSet is { } open ? WriteTeleportMap(npc, open) : null;
        var what = waypoints ? "waypoint" : "standpoint";
        switch (step.Outcome)
        {
            case NpcRouteOutcome.Relocate:
                Trace(npc).Route = $"{what} {step.Outcome}: {(_followers.ContainsKey(npc) ? RelocateFollower(npc, step.Target!.Value, facts) : Relocate(npc, step.Target!.Value, facts))}";
                break;
            case NpcRouteOutcome.RequestTeleport:
                // WS6: a forced pass of a leaderless NPC whose teleport map is another map asks for the NPC's change of place to its target there.
                Trace(npc).Route = $"{what} {step.Outcome} to map {step.TeleportMap} ({step.Target!.Value.X},{step.Target.Value.Y}): {SendHome(npc, step.TeleportMap, step.Target.Value)}";
                break;
            default:
                Trace(npc).Route = $"{what} {step.Outcome}";
                break;
        }
        if (written is not null) Trace(npc).Route += $" [teleport map {written}]";
        return step.Answer;
    }

    // The getter's write (a refusal leaves the field 0: the next step writes it again).
    private string WriteTeleportMap(string npc, int open)
    {
        var result = _session.EnsureTeleportMap(_session.Version, npc, new SessionMapId(open));
        return result.Failure == GameSessionFailure.None ? open.ToString(System.Globalization.CultureInfo.InvariantCulture) : "not written: " + result.Failure;
    }

    // ---- ARC-DEV-116 (MIG11 S5): a former follower goes home ----

    // The requests of NPCs the session's one slot could not take yet (Steamwoven); the Original setting makes each at once and the slot keeps the last.
    private List<(string Npc, int Map, CellCoord Cell)> Homecomings => _scene.Homecomings ?? _ownHomecomings;
    private readonly List<(string Npc, int Map, CellCoord Cell)> _ownHomecomings = [];

    /// <summary>The NPCs waiting to ask for their change of place (Steamwoven: one request is made per frame, <see cref="FlushHomecomings"/>).</summary>
    public IReadOnlyList<string> PendingHomecomings => [.. Homecomings.Select(h => h.Npc)];

    private string SendHome(string npc, int map, RouteLocation target)
    {
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (_session.Rules.Lifecycle.NonPlayerRequestsOverwriteEachOther) return Submit(npc, map, cell).Text;
        // Steamwoven: a request waits for the slot; the NPC's own earlier one is replaced (one request per NPC).
        Homecomings.RemoveAll(h => h.Npc == npc);
        Homecomings.Add((npc, map, cell));
        return "queued";
    }

    private (string Text, bool Accepted) Submit(string npc, int map, CellCoord cell)
    {
        try
        {
            var submitted = _session.RequestChangeOfPlace(npc, ChangeOfPlaceCaller.NpcWaypoint, map, cell, ChangeOfPlaceFlags.None, 0, _scene.MapListed ?? (m => m == _scene.RuntimeMap));
            return (submitted.Status + (submitted.Replaced is { } lost ? $" (lost {lost.Entity})" : ""), submitted.Accepted);
        }
        catch (ArgumentException)
        {
            return ("not a map NPC", false);
        }
    }

    /// <summary>
    /// ARC-DEV-116: makes the next waiting request when the slot is free (nothing pending or in progress); the viewer calls it once per frame before it pings
    /// (<see cref="GameSessionOwner.ProcessChangeOfPlace"/>). A request the project refuses (a map the viewer does not list, a pending hero request) is
    /// dropped with its reason in the NPC's trace. True when a request was made.
    /// </summary>
    public bool FlushHomecomings()
    {
        if (Homecomings.Count == 0 || _session.QueryChangeOfPlace() is not null || _session.ChangeOfPlaceInProgress) return false;
        var (npc, map, cell) = Homecomings[0];
        Homecomings.RemoveAt(0);
        var (text, accepted) = Submit(npc, map, cell);
        Trace(npc).Route += $" -> {text}";
        return accepted;
    }

    // A companion of the shown map is a route NPC: its row is its home map's, its teleport map the session's. Its relocation is its walk's jump.
    private string RelocateFollower(string npc, RouteLocation target, NpcRouteFacts facts)
    {
        DropRun(npc);
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (Tile(npc) == cell) return $"already on {cell.X},{cell.Y}";
        if (!PlaceHeld(cell)) return $"target {cell.X},{cell.Y} not held by the shown map";
        return _scene.Followers is { } followers && followers.Jump(npc, cell) ? $"{facts.Location.X},{facts.Location.Y} -> {cell.X},{cell.Y}" : "the companion's walk refused the jump";
    }

    private AreaNpcRoute? RouteOf(string npc)
    {
        if (_routes.TryGetValue(npc, out var held)) return held;
        var route = TryNpc(npc, out var row) && AreaNpcRouteReader.Read(row.Row) is { Status: AreaNpcRouteStatus.Ready, Route: { } read } ? read : null;
        _routes[npc] = route;
        return route;
    }

    // The facts of one step; null when the host holds no NPC row of this id (a companion of another map's host, an unreadable route).
    private NpcRouteFacts? RouteFacts(string npc)
    {
        if (RouteOf(npc) is not { } route || Tile(npc) is not { } cell || cell.X < 0 || cell.Y < 0) return null;
        // ARC-DEV-116 (FM10): the teleport map is the session's; the row's own (field 243 of the stored object, 0 in every row of the installed maps) is the baseline.
        return new(Sleeping(npc), Hidden(npc), Leader(npc) is not null, _session.QueryClockSnapshot().Hour, route.NpcFlags, _session.QueryTeleportMap(npc) ?? route.TeleportMap, _scene.RuntimeMap,
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
