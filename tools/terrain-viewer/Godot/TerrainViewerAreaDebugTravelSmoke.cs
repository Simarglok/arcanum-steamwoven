using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-050 native smoke (map mode with --debug-mode, stage "debug-travel" only when named): W opens the world map on this map
// (the start map or any other) with the area list and Teleport; Teleport on the farthest placed area (one the original's travel
// cannot route to in one waypoint) loads the start map around it with the session carried, no area discovered and no travel time;
// a save location is marked; Ctrl+Shift+F10 hides and shows the diagnostic panel. Prints counts and outcomes only.
public partial class TerrainViewerApp
{
    private async Task RunAreaDebugTravelSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-050 native-smoke debug-travel FAIL " + why);
            throw new InvalidOperationException("ARC-DEV-050 debug-travel smoke: " + why);
        }
        Require(DebugMode, "the stage needs --debug-mode");
        if (_areaSession is null) { GD.Print("ARC-DEV-050 native-smoke debug-travel SKIP (no area session)"); return; }
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "data " + _worldMapData?.Status + " " + _worldMapData?.Detail);
        var data = _worldMapData!.Data!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var session = _areaSession!;
            var fromMap = _readyArea!.Snapshot.RuntimeMap;
            Require(SaveLocation().StartsWith(ViewerDebugMode.SaveMark, StringComparison.Ordinal), "a save location is marked: " + SaveLocation());
            // Ctrl+Shift+F10 hides the diagnostic panel and shows it again (pressed through the HUD's own input handler).
            void Chord(bool pressed) => _hud!._Input(new InputEventKey { Keycode = Key.F10, Pressed = pressed, CtrlPressed = true, ShiftPressed = true });
            var shown = _hud!.PanelShown;
            Chord(true); Chord(false);
            Require(_hud.PanelShown != shown, "Ctrl+Shift+F10 toggled the diagnostic panel");
            Chord(true); Chord(false);
            Require(_hud.PanelShown == shown, "Ctrl+Shift+F10 toggled it back");
            InventoryKey(Key.W);
            Require(WorldMapOpen && _worldMap is not null, "W opened the world map: " + _worldMapStatus);
            Require(_worldMapPanel!.DebugControlsShownForSelfTest, "the area list and Teleport are shown");
            var map = _worldMap!;
            Require(map.OnStartMap == (fromMap == data.StartRuntime), $"the view knows the start map: {map.OnStartMap} on map {fromMap}");
            // The farthest placed area from the actor's sector on the start map, or from the map's first area elsewhere.
            var here = map.OnStartMap ? WorldTravelGeometry.Location(map.Position, data.Width) : data.TravelAreas[1].Location;
            var target = Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Location != default)
                .OrderBy(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)).Last();
            var known = session.QueryKnownAreas().Count; var wasKnown = session.IsAreaKnown(target);
            var before = session.QueryClockSnapshot().GameTimestampMs;
            _worldMapPanel.TeleportForSelfTest(target);
            Require(!WorldMapOpen && _mapHandoff is not null, "Teleport closed the panel and started the load: " + _worldMapPanel.MessageForSelfTest);
            for (var i = 0; i < 36000 && (_mapHandoff is not null || !ReferenceEquals(_areaSession, session)); i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mapHandoff is null && ReferenceEquals(_areaSession, session), "the start map loaded with the session carried");
            var landing = data.TravelAreas[target].Location;
            var actor = session.QueryActor();
            Require(actor.Map.RuntimeMap == data.StartRuntime, $"on the start map (map {actor.Map.RuntimeMap})");
            var off = Math.Max(Math.Abs(actor.Cell.X - landing.X), Math.Abs(actor.Cell.Y - landing.Y));
            Require(off <= 64, $"the actor stands {off} tiles from area {target}");
            Require(session.QueryKnownAreas().Count == known && session.IsAreaKnown(target) == wasKnown, "the teleport discovered nothing");
            // Ordinary time flows during the load's frames; a journey there would have taken many game hours.
            var gained = session.QueryClockSnapshot().GameTimestampMs - before;
            Require(gained < WorldJourney.MsPerSector, $"no travel time ({gained} game ms)");
            GD.Print("ARC-DEV-050 native-smoke debug-travel PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                From = fromMap, Target = target, Name = data.Areas.Areas[target].Name, ActorOff = off, GainedMs = gained, Known = known, WasKnown = wasKnown }));
        }
        finally
        {
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
