using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-048 native smoke (map mode on the start map, stage "worldmap", last): W opens the world map; a far area refuses its
// one-waypoint route; the nearest area whose route plans is added; Enter travels at the original's pace; the journey discovers
// the area, jumps the session clock by an hour per sector step, lands and loads the start map around the area, with the
// session carried and the actor at the landing. Prints counts and outcomes only.
public partial class TerrainViewerApp
{
    private async Task RunAreaWorldMapSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-048 native-smoke worldmap FAIL " + why);
            throw new InvalidOperationException("ARC-UI-048 world-map smoke: " + why);
        }
        if (_areaSession is null) { GD.Print("ARC-UI-048 native-smoke worldmap SKIP (no area session)"); return; }
        // Only the start map (runtime map 1) opens it; an interior skips without waiting for the data.
        if (_readyArea!.Snapshot.RuntimeMap != 1) { GD.Print($"ARC-UI-048 native-smoke worldmap SKIP (map {_readyMapName} is not the start map)"); return; }
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
        var data = _worldMapData!.Data!;
        if (_readyArea!.Snapshot.RuntimeMap != data.StartRuntime) { GD.Print($"ARC-UI-048 native-smoke worldmap SKIP (map {_readyMapName} is not the start map)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var session = _areaSession!;
            InventoryKey(Key.W);
            Require(WorldMapOpen && WorldModal && _worldMap is not null, "W opened the world map: " + _worldMapStatus);
            var map = _worldMap!;
            var here = WorldTravelGeometry.Location(map.Position, data.Width);
            var byDistance = Enumerable.Range(1, data.TravelAreas.Count - 1)
                .Where(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here) > data.TravelAreas[a].Radius)
                .OrderBy(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).ToArray();
            // The farthest placed area: its straight route meets mountains or water it cannot pass around.
            var far = byDistance.Last(a => data.TravelAreas[a].Radius > 0 && data.TravelAreas[a].Location != default);
            WorldMapWaypoint(() => map.AddArea(far), $"area {far}");
            Require(map.Waypoints.Count == 0 && _worldMapPanel!.MessageForSelfTest.Contains("601", StringComparison.Ordinal), $"far area {far} refused: {_worldMapPanel!.MessageForSelfTest}");
            var target = 0;
            // An area of negative radius is never discovered by travel (WT3): the target is one that can be.
            // An unknown target: a known one snaps the end onto its location, where the last step may take no hour.
            foreach (var area in byDistance.Where(a => data.TravelAreas[a].Radius > 0 && !session.IsAreaKnown(a)))
            {
                if (map.AddArea(area) != AreaWorldMapRefusal.None) continue;
                target = area; break;
            }
            Require(target > 0, "no area plans from here");
            RenderWorldMap(null);
            var (steps, hours) = map.Estimate();
            var before = session.QueryClockSnapshot().GameTimestampMs;
            var wasKnown = session.IsAreaKnown(target);
            var started = System.Environment.TickCount64;
            InventoryKey(Key.Enter);
            Require(map.Travelling, "Enter started the journey");
            for (var i = 0; i < 36000 && WorldMapOpen; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var elapsed = System.Environment.TickCount64 - started;
            // The clock as the journey left it (the frames of the load let ordinary time flow again).
            var gained = session.QueryClockSnapshot().GameTimestampMs - before;
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, session)); i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!WorldMapOpen && _mapHandoff is null, "the journey ended and the start map loaded");
            Require(ReferenceEquals(_areaSession, session), "the session is carried");
            Require(session.IsAreaKnown(target) && session.QueryLastDiscoveredArea() == 0, "the target is known, the last record cleared");
            Require(gained == hours * WorldJourney.MsPerSector, $"clock gained {gained} ms for {hours} h");
            // Frame deltas accumulate, so the steps of one frame's catch-up may come early.
            Require(elapsed >= (steps - WorldMapStepsPerFrame) * WorldJourney.StepRealMs, $"one step per 62 ms: {elapsed} ms for {steps} steps");
            var landing = data.TravelAreas[target].Location;
            var actor = session.QueryActor();
            Require(actor.Map.RuntimeMap == data.StartRuntime, "on the start map");
            var off = Math.Max(Math.Abs(actor.Cell.X - landing.X), Math.Abs(actor.Cell.Y - landing.Y));
            Require(off <= 64, $"the actor stands {off} tiles from the landing");
            Require(_mapWindowCentre is { } centre && centre.X == landing.X >> 6 && centre.Y == landing.Y >> 6, "the window opened around the landing");
            GD.Print("ARC-UI-048 native-smoke worldmap PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Target = target, Far = far, Steps = steps, Hours = hours, ElapsedMs = elapsed, WasKnown = wasKnown,
                Known = session.QueryKnownAreas().Count, ActorOff = off, Rules = session.Rules.Travel.Name }));
        }
        finally
        {
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
