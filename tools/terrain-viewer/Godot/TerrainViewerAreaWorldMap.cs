using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
// ARC-DEV-050: in debug mode (ViewerDebugMode) W opens on any map; the panel lists every area and Teleport lands the PC at the
// chosen area's location on the start map at once (a load as the journey's landing, from another map a transfer to the start
// map), with no game time and no discovered area. Without the debug mode there is no area list and no teleport.
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
    private bool WorldMapOpen => WindowOpen(InterfaceMode.WorldMap);
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
        if (!WorldMapActive || !WindowCanOpen(InterfaceMode.WorldMap)) return;
        AdoptWorldMapData();
        string? refused = _worldMapData is not { Data: { } data } ? $"world-map data {(_worldMapData is null ? "loading" : _worldMapData.Status.ToString())}"
            : _readyArea!.Snapshot.RuntimeMap != data.StartRuntime && !DebugMode ? $"only on the start map ({data.StartMap})"
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
        // ARC-DEV-050: off the start map (debug mode only) the view has no position and only teleports.
        var onStart = _readyArea!.Snapshot.RuntimeMap == _worldMapData!.Data!.StartRuntime;
        var worldMap = _worldMap = new AreaWorldMap(_worldMapData.Data, _areaSession!, onStart ? new(cell.X, cell.Y) : null);
        if (_worldMapPanel is null)
        {
            _worldMapPanel = new AreaWorldMapPanel(); AddChild(_worldMapPanel);
            _worldMapPanel.WaypointRequested += pixel => WorldMapWaypoint(() => _worldMap!.AddWaypoint(pixel), $"pixel ({pixel.X},{pixel.Y})");
            _worldMapPanel.AreaRequested += area => WorldMapWaypoint(() => _worldMap!.AddArea(area), $"area {area}");
            _worldMapPanel.TravelRequested += StartWorldMapTravel;
            _worldMapPanel.ClearRequested += () => { if (_worldMap?.Clear() == true) RenderWorldMap(null); };
            _worldMapPanel.CloseRequested += CloseWorldMapByPlayer;
            _worldMapPanel.TeleportRequested += TeleportWorldMap;
        }
        StopWorldRoute("ROUTE_STOPPED_BY_WORLD_MAP");
        QuarantineWorld(); _quickKeys.Reset();
        var names = _worldMapData.Data!.Areas.Areas;
        if (!OpenWindowMode(InterfaceMode.WorldMap, () => _worldMapPanel!.Open(worldMap, _areaSession!.IsAreaKnown, area => names[area].Name, DebugMode), () => _worldMapPanel!.CloseView()))
        {
            _worldMap = null;
            return;
        }
        _worldMapStatus = null; _worldMapMessage = null; _worldMapStepMs = 0;
        GD.Print($"ARC-UI-048 world map open position=({worldMap.Position.X},{worldMap.Position.Y}) onStartMap={onStart} debugMode={DebugMode} "
                 + $"known={_areaSession!.QueryKnownAreas().Count} rules={_areaSession.Rules.Travel.Name}");
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
            AreaWorldMapRefusal.NotOnStartMap => "not on the start map: no journey from here (teleport only)",
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
        RenderWorldMap(refusal switch
        {
            AreaWorldMapRefusal.None => null,
            AreaWorldMapRefusal.NotOnStartMap => "not on the start map: no journey from here (teleport only)",
            _ => "nothing to travel to: add a waypoint",
        });
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

    /// <summary>ARC-DEV-050, debug mode only: Teleport on the selected area lands the PC at its location at once.</summary>
    private void TeleportWorldMap(int area)
    {
        if (!DebugMode || !WorldMapOpen || _worldMap is not { } map) return;
        var refusal = map.Teleport(area, out var landing);
        GD.Print($"ARC-DEV-050 debug teleport area={area} refusal={refusal} landing=({landing.X},{landing.Y}) from map={CurrentAreaMap}");
        if (refusal != AreaWorldMapRefusal.None)
        {
            RenderWorldMap(refusal == AreaWorldMapRefusal.Travelling ? "travelling: teleport before the journey" : $"area {area} has no location on the map");
            return;
        }
        LandWorldMap(landing, $"debug teleport to area {area}", teleport: true);
    }

    /// <summary>The panel closes and the start map loads around the landing location, as a transfer to it does. ARC-DEV-050: a
    /// debug teleport may start on another map (a transfer to the start map), and it opens at the landing's sector without an
    /// arrival tile, so the PC stands on the walkable tile nearest to the sector's middle (an area's location tile may be blocked,
    /// and an arrival is taken whenever its sector is loaded, walkable or not). ARC-DEV-115 (MIG11 S4, WT12, RL24, RL29): the landing is
    /// the session's change of place (caller <see cref="ChangeOfPlaceCaller.WorldMapLanding"/>, blank-screen flag alone), requested for
    /// the start map and pinged in the same frame (<see cref="TerrainViewerApp.RequestWorldMapLanding"/>).</summary>
    private void LandWorldMap(TravelLocation landing, string why, bool teleport = false)
    {
        var steps = _worldMap?.Steps ?? 0; var hours = (_worldMap?.GameMs ?? 0) / WorldJourney.MsPerSector;
        var onStart = _worldMap?.OnStartMap != false;
        var startMap = _worldMapData?.Data?.StartMap;
        var startRuntime = _worldMapData?.Data?.StartRuntime;
        EndWorldMap();
        if (_worldActor.Session is not AreaGridSession session || _arguments is null || _readyMapName is null || _areaSession is not { } owner) return;
        var target = onStart ? _readyMapName : startMap;
        if (target is null || startRuntime is not { } startRuntimeMap) return;
        LeaveCombatModeForMapChange();
        var origin = _arguments;
        var next = teleport
            ? origin with { AreaMapName = target, AreaMapArrival = null, AreaMapSector = new Arcanum.Data.Area.AreaSectorCoordinate(landing.X >> 6, landing.Y >> 6) }
            : origin with { AreaMapName = target, AreaMapArrival = new(landing.X, landing.Y), AreaMapSector = null };
        _worldMapStatus = $"{why} after {steps} step(s), {hours} h: landing at ({landing.X},{landing.Y})";
        GD.Print("ARC-UI-048 landing " + System.Text.Json.JsonSerializer.Serialize(new { Why = why, landing.X, landing.Y, Steps = steps, Hours = hours,
            Known = _areaSession?.QueryKnownAreas().ToArray() }));
        RefreshAreaStatus();
        RequestWorldMapLanding(owner, session, origin, next, target, startRuntimeMap, new(checked((int)landing.X), checked((int)landing.Y)), sameMap: onStart);
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
        if (_worldMapPanel is null || !WorldMapOpen) { _worldMap = null; return; }
        GD.Print("ARC-UI-048 world map closed");
        CloseWindowMode(InterfaceMode.WorldMap); _worldMap = null;
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
        if (routed == Key.Escape || (routed == Key.W && !CharacterKeySilenced(key, routed))) CloseWorldMapByPlayer();
        else if (routed is Key.Enter or Key.KpEnter) _worldMapPanel!.Confirm();
    }

    private void HandleWorldMapPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseWorldMapByPlayer();
        else if (intent.Confirm) _worldMapPanel!.Confirm();
    }

    private string AreaWorldMapStatus() => !WorldMapActive ? "" : WorldMapOpen ? " | world map open: W or Escape closes"
        : " | W world map" + (DebugMode ? " (DEBUG MODE: teleport)" : "") + (_worldMapStatus is null ? "" : ": " + _worldMapStatus);
}
