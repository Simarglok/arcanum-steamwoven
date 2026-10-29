using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-050 native smoke (map mode with --debug-mode, stage "debug-travel" only when named): W opens the world map on this map
// (the start map or any other) with the area list and Teleport; Teleport on the farthest placed area (one the original's travel
// cannot route to in one waypoint) loads the start map around it with the session carried, no area discovered and no travel time;
// a save location is marked; Ctrl+Shift+F10 hides and shows the diagnostic panel. ARC-WORLD-120 (D1): a save made before the teleport
// (into a temporary save folder) lies beyond the window the teleport opened; its load moves the window back around the saved cell and
// stands the actor there, instead of refusing the cell as not admitted. Prints counts and outcomes only.
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
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-world-120-").FullName;
        _saveLibraryForSelfTest = new(saves);
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
            var savedCell = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            var saved = map.OnStartMap ? SaveGame(null, "before the teleport", null, "smoke ") : null;
            Require(!map.OnStartMap || saved is not null, "saved before the teleport: " + _quickSaveStatus);
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
            // ARC-WORLD-120 (D1): the save made on this map before the teleport lies beyond the shown window; its load opens the window
            // around the saved cell with the loaded session and the actor on that cell.
            object? farLoad = null;
            if (saved is not null)
            {
                var window = _readyArea!.Snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).Select(s => s.Coordinate).ToArray();
                var cell = new Arcanum.Simulation.Movement.CellCoord(checked((int)savedCell.X), checked((int)savedCell.Y));
                Require(_mapWindowCentre is not null && !AreaQuickSave.InShownSectors(window, cell), $"the saved cell {cell} lies beyond the window around {_mapWindowCentre}");
                Require(LoadGame(SaveLibrary.Slot(saved), "smoke F8 "), "the far save's load began: " + _quickSaveStatus);
                Require(_mapHandoff is not null, "the load moves the window: " + _quickSaveStatus);
                for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var loaded = _areaSession!;
                var at = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                Require(_mapHandoff is null && !ReferenceEquals(loaded, session) && _readyArea!.Snapshot.RuntimeMap == data.StartRuntime
                    && loaded.QueryActor().Cell == cell && at == savedCell && _mapWindowCentre == new Arcanum.Data.Area.AreaSectorCoordinate(savedCell.X >> 6, savedCell.Y >> 6),
                    $"the window opened around the saved cell with the actor there (at {at}, window {_mapWindowCentre}): {_quickSaveStatus}");
                farLoad = new { Saved = savedCell, Window = new { _mapWindowCentre!.Value.X, _mapWindowCentre.Value.Y } };
            }
            GD.Print("ARC-DEV-050 native-smoke debug-travel PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                From = fromMap, Target = target, Name = data.Areas.Areas[target].Name, ActorOff = off, GainedMs = gained, Known = known, WasKnown = wasKnown, FarLoad = farLoad }));
            if (farLoad is not null) GD.Print("ARC-WORLD-120 native-smoke far load PASS " + System.Text.Json.JsonSerializer.Serialize(farLoad));
        }
        finally
        {
            _saveLibraryForSelfTest = null;
            try { System.IO.Directory.Delete(saves, recursive: true); }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-WORLD-120 debug-travel smoke: temporary saves not removed: " + exception.GetType().Name); }
            if (WorldMapOpen) EndWorldMap();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
