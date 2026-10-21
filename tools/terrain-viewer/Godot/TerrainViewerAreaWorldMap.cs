using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-048: the world map in map mode on the start map (runtime map 1, the map the world map is keyed to). The original key W
// (logical key down) opens the diagnostic panel under the in-game key handler's guards like C, L and T; it is modal (gate
// closed, clock paused for the frames, a walk stopped). Waypoints follow the rules of ARC-WORLD-056 (ledger WT5–WT8) with the
// session's rule setting; Travel runs one travel step per 62 real ms (WT10): the step's game time jumps the session clock
// through the event pump, a discovered area becomes known. The journey ends by its landing (WT12), or by W, Escape or cancel,
// which lands where it stands; the panel closes and the start map loads around the landing as a transfer does, the session and
// following companions carried. The panel refuses on another map, in combat mode, with a transfer pending and without the data.
// When the landing load fails, the start map reloads where the PC stood, with the journey's hours and discoveries kept. Not
// modelled: travel music, hiding the party, encounters, town maps (whose sectors the original keeps off the walkable world map).
public partial class TerrainViewerApp
{
    private AreaWorldMapPanel? _worldMapPanel;
    private AreaWorldMap? _worldMap;
    private string? _worldMapStatus;
    private string? _worldMapMessage;
    private Task<AreaWorldMapDataLoad>? _worldMapDataTask;
    private string? _worldMapDataModule;
    private AreaWorldMapDataLoad? _worldMapData;
    private double _worldMapStepMs;
    // Travel steps per frame at most, so a long frame catches up without a burst (project policy).
    private const int WorldMapStepsPerFrame = 4;
    private bool WorldMapOpen => _worldMapPanel?.IsOpen == true;
    private bool WorldMapActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea && _readyMapName is not null;

    /// <summary>Starts loading the world-map data for this module once; a reload or a map transfer reuses them.</summary>
    private void BindAreaWorldMap(TerrainViewerArguments arguments)
    {
        if (arguments.AreaMapName is null || _worldMapDataModule == arguments.ModulePath) return;
        _worldMapDataModule = arguments.ModulePath; _worldMapData = null;
        var module = arguments.ModulePath;
        _worldMapDataTask = Task.Run(() => AreaWorldMap.LoadData(module));
    }

    private void AdoptWorldMapData()
    {
        if (_worldMapData is not null || _worldMapDataTask is not { IsCompleted: true } task) return;
        _worldMapData = task.IsCompletedSuccessfully ? task.Result
            : new(AreaWorldMapDataStatus.StackFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _worldMapDataTask = null;
        GD.Print($"ARC-UI-048 world-map data status={_worldMapData.Status} detail={_worldMapData.Detail}");
    }

    private void OpenWorldMap()
    {
        if (!WorldMapActive || WorldMapOpen) return;
        AdoptWorldMapData();
        string? refused = _worldMapData is not { Data: { } data } ? $"world-map data {(_worldMapData is null ? "loading" : _worldMapData.Status.ToString())}"
            : _readyArea!.Snapshot.RuntimeMap != data.StartRuntime ? $"only on the start map ({data.StartMap})"
            : HeroInCombatMode ? "leave combat mode first"
            : AreaTransferPending ? "a transfer is pending"
            : _worldActor.Session is not AreaGridSession { State.Phase: Arcanum.Simulation.Movement.MovementPhase.Settled } ? "the actor is moving"
            : null;
        if (refused is not null)
        {
            _worldMapStatus = refused;
            GD.Print("ARC-UI-048 world map refused " + refused);
            RefreshAreaStatus();
            return;
        }
        var session = (AreaGridSession)_worldActor.Session!;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        _worldMap = new AreaWorldMap(_worldMapData!.Data!, _areaSession!, new(cell.X, cell.Y));
        if (_worldMapPanel is null)
        {
            _worldMapPanel = new AreaWorldMapPanel(); AddChild(_worldMapPanel);
            _worldMapPanel.WaypointRequested += pixel => WorldMapWaypoint(() => _worldMap!.AddWaypoint(pixel), $"pixel ({pixel.X},{pixel.Y})");
            _worldMapPanel.AreaRequested += area => WorldMapWaypoint(() => _worldMap!.AddArea(area), $"area {area}");
            _worldMapPanel.TravelRequested += StartWorldMapTravel;
            _worldMapPanel.ClearRequested += () => { if (_worldMap?.Clear() == true) RenderWorldMap(null); };
            _worldMapPanel.CloseRequested += CloseWorldMapByPlayer;
        }
        StopWorldRoute("ROUTE_STOPPED_BY_WORLD_MAP");
        QuarantineWorld(); _quickKeys.Reset();
        var names = _worldMapData.Data!.Areas.Areas;
        _worldMapPanel.Open(_worldMap, _areaSession!.IsAreaKnown, area => names[area].Name);
        _worldMapStatus = null; _worldMapMessage = null; _worldMapStepMs = 0;
        GD.Print($"ARC-UI-048 world map open position=({_worldMap.Position.X},{_worldMap.Position.Y}) known={_areaSession.QueryKnownAreas().Count} rules={_areaSession.Rules.Travel.Name}");
        RefreshReadyStatus();
    }

    private void WorldMapWaypoint(Func<AreaWorldMapRefusal> add, string what)
    {
        if (!WorldMapOpen || _worldMap is null) return;
        var refusal = add();
        var message = refusal switch
        {
            AreaWorldMapRefusal.None => null,
            AreaWorldMapRefusal.NoRoute => "no route there (the original's message 601): add waypoints around mountains or water",
            AreaWorldMapRefusal.TooMany => "at most 30 waypoints",
            AreaWorldMapRefusal.Travelling => "travelling: waypoints are set before the journey",
            _ => "off the map",
        };
        GD.Print($"ARC-UI-048 waypoint {what} refusal={refusal} waypoints={_worldMap.Waypoints.Count}");
        RenderWorldMap(message);
    }

    private void StartWorldMapTravel()
    {
        if (!WorldMapOpen || _worldMap is null) return;
        var refusal = _worldMap.Start();
        var (steps, hours) = _worldMap.Estimate();
        GD.Print($"ARC-UI-048 travel start refusal={refusal} steps={steps} hours={hours}");
        _worldMapStepMs = 0;
        RenderWorldMap(refusal == AreaWorldMapRefusal.None ? null : "nothing to travel to: add a waypoint");
    }

    private void RenderWorldMap(string? message)
    {
        _worldMapMessage = message;
        _worldMapPanel?.Render(message);
        RefreshAreaStatus();
    }

    /// <summary>Per frame: while the journey runs, one travel step per <see cref="WorldJourney.StepRealMs"/> of real time; a
    /// replaced session or another map closes the panel.</summary>
    private void SyncAreaWorldMap(double delta)
    {
        if (!WorldMapOpen) { AdoptWorldMapData(); return; }
        if (!WorldMapActive || _worldMap is null || !ReferenceEquals(_worldMap.Session, _areaSession)) { EndWorldMap(); return; }
        if (!_worldMap.Travelling) return;
        _worldMapStepMs += delta * 1000;
        for (var i = 0; i < WorldMapStepsPerFrame && _worldMap.Travelling && _worldMapStepMs >= WorldJourney.StepRealMs; i++)
        {
            _worldMapStepMs -= WorldJourney.StepRealMs;
            var step = _worldMap.Step(AdvanceWorldMapClock);
            if (step.Discovered > 0) GD.Print($"ARC-UI-048 discovered area={step.Discovered} step={_worldMap.Steps}");
        }
        // After a stall the journey goes on at its pace instead of catching up at four steps a frame.
        _worldMapStepMs = Math.Min(_worldMapStepMs, WorldJourney.StepRealMs);
        if (_worldMap.Travelling) { _worldMapPanel!.Render(_worldMapMessage); return; }
        if (_worldMap.Landing is { } landing) LandWorldMap(landing, "arrived");
    }

    // The step's game time: through the lighting driver's event pump when the map is lit, else the session clock alone.
    private void AdvanceWorldMapClock(long gameMs)
    {
        if (_areaAmbient is { } driver) { driver.AdvanceMilliseconds(gameMs, AmbientSector()); LogAreaEvents(driver); PushAreaAmbient(); }
        else
        {
            var result = _areaSession!.AdvanceGameTime(_areaSession.QueryClockSnapshot().Token, gameMs, Arcanum.Simulation.Clock.ClockAdvanceReason.ExplicitWait);
            if (result.Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Rejected) GD.Print("ARC-UI-048 clock refused " + result.Failure);
        }
    }

    /// <summary>The panel closes and the start map loads around the landing location, as a transfer to it does.</summary>
    private void LandWorldMap(TravelLocation landing, string why)
    {
        var steps = _worldMap?.Steps ?? 0; var hours = (_worldMap?.GameMs ?? 0) / WorldJourney.MsPerSector;
        EndWorldMap();
        if (_worldActor.Session is not AreaGridSession session || _arguments is null || _readyMapName is null) return;
        LeaveCombatModeForMapChange();
        var origin = _arguments;
        _mapHandoff = new(_areaSession, CurrentAreaMap, _readyMapName, session.State.Facing, origin, session.Chart.ToWorld(session.State.CommittedCell),
            _readyMapName, Companions: LeavingCompanions());
        var next = origin with { AreaMapArrival = new(landing.X, landing.Y), AreaMapSector = null };
        _worldMapStatus = $"{why} after {steps} step(s), {hours} h: landing at ({landing.X},{landing.Y})";
        GD.Print("ARC-UI-048 landing " + System.Text.Json.JsonSerializer.Serialize(new { Why = why, landing.X, landing.Y, Steps = steps, Hours = hours,
            Known = _areaSession?.QueryKnownAreas().ToArray() }));
        RefreshAreaStatus();
        _lastLoad = LoadTransferAsync(next);
    }

    // W, Escape, cancel or the button: before the journey the panel closes; during it the PC lands where it stands (WT12).
    private void CloseWorldMapByPlayer()
    {
        if (!WorldMapOpen || _worldMap is null) return;
        if (_worldMap.Travelling) { LandWorldMap(_worldMap.Cancel(), "cancelled"); return; }
        EndWorldMap();
    }

    private void EndWorldMap()
    {
        if (_worldMapPanel is null || !_worldMapPanel.IsOpen) { _worldMap = null; return; }
        GD.Print("ARC-UI-048 world map closed");
        _worldMapPanel.CloseView(); _worldMap = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>The original World map key (W) acts on its logical key down while a map-mode session exists. Returns true when
    /// the key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteWorldMapKey(WorldKeyDecision decision)
    {
        if (!WorldMapActive || decision.Key != WorldKey.W) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _worldMapStatus = "W ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenWorldMap();
        return true;
    }

    /// <summary>While the panel is open it owns every key: W and Escape close it (or end the journey), Enter travels.</summary>
    private void HandleWorldMapKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed is Key.W or Key.Escape) CloseWorldMapByPlayer();
        else if (routed is Key.Enter or Key.KpEnter) _worldMapPanel!.Confirm();
    }

    private void HandleWorldMapPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseWorldMapByPlayer();
        else if (intent.Confirm) _worldMapPanel!.Confirm();
    }

    private string AreaWorldMapStatus() => !WorldMapActive ? "" : WorldMapOpen ? " | world map open: W or Escape closes"
        : " | W world map" + (_worldMapStatus is null ? "" : ": " + _worldMapStatus);
}
