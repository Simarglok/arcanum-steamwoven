using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-351: the objects the opens of maps notified through their teleported marks (entity, map, cell), for the follower-home smoke; and the former follower it sent home.
    private readonly List<(string Entity, int Map, AreaStepCell Cell)> _teleportedNoticesForSmoke = [];
    private (string Entity, SessionMapId Home)? _formerFollowerMarkForSmoke;

    // ARC-DEV-116 (MIG11 S5, B09, smoke plan SM29) native observation (only when named, inside the transfer stage's carry, with --hero): the companion the
    // follow smoke carried to the destination is a visitor there; the hero releases it, the destination's forced waypoint or standpoint pass (the fold's
    // first tick, driven here by the host's own steps) finds a leaderless NPC whose teleport map (the map the hero carried it from, FM6) is another map and
    // asks for its change of place there, and the frame's ping carries it out: it stands on its home map again, no longer on the destination, which
    // the hero never left.
    private void SendAreaMapFormerFollowerHome()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-116 follower-home smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_FOLLOWER_HOME_FAILED");
        }
        if (_smokeCompanion is not { } entity || _areaSession is not { } session || session.QueryHero() is null
            || _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == entity) is not { } companion || companion.Template.Home.RuntimeMap == CurrentAreaMap)
        { GD.Print($"ARC-DEV-116 native-smoke follower-home NONE (no visitor here: map {_readyMapName}, hero {_areaSession?.QueryHero() is not null})"); return; }
        var home = companion.Template.Home;
        // ARC-DEV-351 (TG9, RL27): the carry gave it the original's teleported mark on this map; the open of this map took it off and notified it on the arrival cell.
        Require(session.QueryTeleportedMarks(new(CurrentAreaMap)).All(mark => mark.Entity != entity), "the open of the destination took the carried follower's mark off");
        Require(_teleportedNoticesForSmoke.Any(n => n.Entity == entity && n.Map == CurrentAreaMap), "the open notified the carried follower: " + string.Join(";", _teleportedNoticesForSmoke));
        Require(session.QueryTeleportMap(entity) == home.RuntimeMap,
            $"the carry gave it the map it left as its teleport map: {session.QueryTeleportMap(entity)?.ToString() ?? "none"} not {home.RuntimeMap}");
        var shown = CurrentAreaMap;
        _aiForSmoke = true;
        try
        {
            Require(session.LeaveParty(session.Version, entity, new PartyLeave(Force: true)).Disposition != GameSessionDisposition.Rejected, "the hero released the companion");
            BindAreaCompanions();
            Require(_areaCompanions?.Companions.Any(c => c.Entity == entity) == true, "the released companion still stands here: " + _areaCompanions?.Status());
            var host = AreaAiHostNow();
            Require(host is not null && host.RunCompanions.Contains(entity), "the AI host runs the free companion");
            // The tick's forced pass: the standpoint step only when the waypoint step did nothing (RL17).
            if (!host!.RunWaypoints(entity)) host.RunStandpoints(entity);
            var route = host.TraceOf(entity)?.Route;
            Require(route is not null && route.Contains("RequestTeleport", StringComparison.Ordinal) && route.Contains($"to map {home.RuntimeMap}", StringComparison.Ordinal),
                "the forced pass asked for the change of place to its teleport map: " + route);
            // The frame: the request waiting in the host is made and pinged.
            ProcessNpcChangeOfPlace(host);
            var where = AreaCompanionPlacement.Where(session, home, entity);
            Require(where.Error is null && where.Map == home, $"the companion's place is its home map again: map {where.Map.RuntimeMap} {where.Error} (route {host.TraceOf(entity)?.Route})");
            Require(_areaCompanions?.Companions.Any(c => c.Entity == entity) != true, "the destination's group no longer holds it");
            Require(session.QueryActor().Map.RuntimeMap == shown && CurrentAreaMap == shown, "the hero stays on the destination");
            Require(session.QueryChangeOfPlace() is null && !session.ChangeOfPlaceInProgress, "the slot is empty again");
            Require(session.QueryTeleportMap(entity) == home.RuntimeMap, "the tie to its map stays");
            // ARC-DEV-351 (TG9): sent home alone while its map is closed, it left the original's teleported mark there, on the cell it was written on; the open of that map notifies it.
            var mark = session.QueryTeleportedMarks(home).SingleOrDefault(m => m.Entity == entity);
            Require(mark.Entity == entity && (where.Placement is not { } placement || placement.World == mark.Cell), $"the homecoming left its teleported mark on the home map: {string.Join(";", session.QueryTeleportedMarks(home))}");
            _formerFollowerMarkForSmoke = (entity, home);
            GD.Print("ARC-DEV-116 native-smoke follower-home PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Visitor = companion.Speaker.ScriptId, From = shown, Home = home.RuntimeMap, At = where.Placement is { } placed ? $"{placed.World.X},{placed.World.Y}" : "its stored cell",
                Route = route }));
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
    }

    // ARC-DEV-351 (TG9, RL27): after the homecoming above, the hero goes back to the former follower's home map through an exit of this map that leads there. The open of the home map
    // takes the former follower's teleported mark off and notifies it on the cell it was written on (its tile's script runs when the cell has one).
    private async Task<bool> RunFormerFollowerReturnSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-351 follower-return smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_FOLLOWER_RETURN_FAILED");
        }
        if (_formerFollowerMarkForSmoke is not var (entity, home) || _mapTransfers is not { } transfers || _areaSession is not { } session)
        { GD.Print("ARC-DEV-351 native-smoke follower-return NONE (no former follower sent home)"); return false; }
        _formerFollowerMarkForSmoke = null;
        var exit = transfers.Triggers.Where(k => transfers.Site(k)!.DestinationRuntime == home.RuntimeMap && transfers.Refusal(transfers.Site(k)!) is null)
            .Select(k => (Key: k, Cell: UsableCell(transfers.Map, k))).FirstOrDefault(p => p.Cell is not null);
        if (exit.Cell is not { } near) { GD.Print($"ARC-DEV-351 native-smoke follower-return NONE (no exit of map {_readyMapName} leads to map {home.RuntimeMap})"); return false; }
        AreaSmokePlace(transfers.Map, near);
        var noticed = _teleportedNoticesForSmoke.Count;
        AreaSmokeClick(await AreaSmokeTriggerPointAsync(_readyArea!.Plan.Commands.Single(c => c.Identity == exit.Key)));
        for (var wait = 0; wait < 40 && transfers.Pending is null; wait++) AreaSmokeFrame();
        Require(transfers.Pending is { } pending && pending.To.Runtime == home.RuntimeMap, "the exit's use did not queue a transfer home: " + _areaUseStatus);
        AreaSmokeFrame();
        if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
        Require(CurrentAreaMap == home.RuntimeMap && ReferenceEquals(_areaSession, session), $"the hero stands on the home map in the same session: map {CurrentAreaMap}");
        for (var frame = 0; frame < 8; frame++) AreaSmokeFrame();
        Require(session.QueryTeleportedMarks(home).All(mark => mark.Entity != entity), "the open took the former follower's mark off");
        var notice = _teleportedNoticesForSmoke.Skip(noticed).FirstOrDefault(n => n.Entity == entity && n.Map == home.RuntimeMap);
        Require(notice.Entity == entity, "the open notified the former follower: " + string.Join(";", _teleportedNoticesForSmoke.Skip(noticed)));
        var run = _npcTileRuns.LastOrDefault(r => r.Npc == entity && r.Cell == notice.Cell);
        GD.Print("ARC-DEV-351 native-smoke follower-return PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Entity = entity, Home = home.RuntimeMap, Cell = $"{notice.Cell.X},{notice.Cell.Y}", TileScript = run.Npc is null ? "none on that tile (or refused)" : run.Outcome.ToString() }));
        return true;
    }
}
