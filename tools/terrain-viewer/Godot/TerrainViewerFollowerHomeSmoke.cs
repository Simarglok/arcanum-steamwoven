using Arcanum.Session;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
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
            GD.Print("ARC-DEV-116 native-smoke follower-home PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Visitor = companion.Speaker.ScriptId, From = shown, Home = home.RuntimeMap, At = where.Placement is { } placed ? $"{placed.World.X},{placed.World.Y}" : "its stored cell",
                Route = route }));
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
    }
}
