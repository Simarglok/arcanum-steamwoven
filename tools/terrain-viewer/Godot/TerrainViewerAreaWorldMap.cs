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
// closed and a walk stopped; the clock runs behind it, ARC-DEV-126). Waypoints follow the rules of ARC-WORLD-056 (ledger WT5–WT8) with the
// session's rule setting; Travel runs one travel step per 62 real ms (WT10) as the session's real-time events (MIG12 S8: the first
// 50 ms after the start, each next 62 ms after it, run by the frame step even behind the pause): the step's game time jumps the
// session clock, a discovered area becomes known. The journey ends by its landing (WT12), or by W or a controller cancel
// (Escape opens the in-play menu over the map instead, ARC-DEV-111, and the steps go on behind its pause), which lands where it stands; the panel closes and the start map loads around the landing as a transfer does, the session and
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
    // ARC-UI-073: the original look's art and tiles, read once a module beside the data (null without a game folder or when they did not read).
    private Task<AreaWorldMapArtContent>? _worldMapArtTask;
    private AreaWorldMapArtLook? _worldMapArtLook;
    private bool _worldMapArtAdopted;
    private bool WorldMapOpen => WindowOpen(InterfaceMode.WorldMap);
    private bool WorldMapActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area && _readyMapName is not null;

    /// <summary>Starts loading the world-map data for this module once; a reload or a map transfer reuses them.</summary>
    private void BindAreaWorldMap(TerrainViewerArguments arguments)
    {
        if (arguments.AreaMapName is null || _worldMapDataModule == arguments.ModulePath) return;
        _worldMapDataModule = arguments.ModulePath; _worldMapData = null;
        var module = arguments.ModulePath;
        _worldMapDataTask = Task.Run(() => AreaWorldMap.LoadData(module));
        _worldMapArtTask = SelfTestBootstrap || module is null ? null : Task.Run(() => AreaWorldMapArtContent.Load(module));
        _worldMapArtLook = null; _worldMapArtAdopted = false;
    }

    /// <summary>Builds the window's textures once the background read is done; the window keeps the diagnostic look until then and when the art did not read.</summary>
    private AreaWorldMapArtLook? AdoptWorldMapArt()
    {
        if (_worldMapArtAdopted || _worldMapArtTask is not { IsCompleted: true } task) return _worldMapArtLook;
        _worldMapArtAdopted = true;
        if (!task.IsCompletedSuccessfully) { GD.Print("ARC-UI-073 world-map art load threw " + task.Exception?.GetBaseException().GetType().Name); return null; }
        var content = task.Result;
        if (!content.Ready) { GD.Print($"ARC-UI-073 world-map art not ready: {content.Detail}"); return null; }
        // The font's glyph frames stay as they are for the art text draw, not as textures.
        var art = content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0 && load.Number != AreaWorldMapArtLayout.FontArt)
            .ToDictionary(load => load.Number, load => load.Frames.Select(frame => (Texture2D)HotkeyTexture(frame)).ToArray());
        var tiles = new List<Texture2D>();
        foreach (var bytes in content.Tiles)
        {
            var image = new Image();
            if (image.LoadBmpFromBuffer(bytes!) != Error.Ok) { GD.Print("ARC-UI-073 world-map tile did not decode"); return null; }
            image.Convert(Image.Format.Rgba8);
            tiles.Add(ImageTexture.CreateFromImage(image));
        }
        var zoomed = new Image();
        if (zoomed.LoadBmpFromBuffer(content.Zoomed!) != Error.Ok) { GD.Print("ARC-UI-083 world-map continent picture did not decode"); return null; }
        zoomed.Convert(Image.Format.Rgba8);
        _worldMapArtLook = new(art, tiles, ImageTexture.CreateFromImage(zoomed), new AreaArtFontFace(content.Art[AreaWorldMapArtLayout.FontArt].Frames), content);
        GD.Print($"ARC-UI-073 world-map art ready {content.Detail}");
        return _worldMapArtLook;
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
            _worldMapPanel.SoundRequested += id => PlayInterfaceSound(id, "world map button");
            _worldMapPanel.PollGate = () => !MainMenuOpen;
            // ARC-UI-083: a marker's description is written into the message window at once (WM60), a button's hint after the quiet time (WM61).
            _worldMapPanel.DescriptionRequested += text =>
            {
                if (text is null) _messageLog.ClearHover(); else _messageLog.ShowTransient(AreaMessageEntry.Text(AreaMessageKind.Hover, text));
                _gameBarPanel?.MessageLog.Render();
            };
            _worldMapPanel.HintRequested += text =>
            {
                if (text is null) { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
                else PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, text), "hover");
            };
        }
        _worldMapPanel.SetArtLook(AdoptWorldMapArt());
        StopWorldRoute("ROUTE_STOPPED_BY_WORLD_MAP");
        QuarantineWorld(); _quickKeys.Reset();
        var names = _worldMapData.Data!.Areas.Areas;
        if (!OpenWindowMode(InterfaceMode.WorldMap, () => _worldMapPanel!.Open(worldMap, _areaSession!.IsAreaKnown, area => names[area].Name, DebugMode), () => _worldMapPanel!.CloseView()))
        {
            _worldMap = null;
            return;
        }
        _worldMapStatus = null; _worldMapMessage = null;
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
        // MIG12 S8: the journey is a chain of real-time events of the session, which only a frame step moves (a lit map's, or an unlit map's frame driver, ARC-DEV-124).
        if (!HasFrameSource)
        {
            GD.Print("ARC-UI-048 travel start refused: no game clock on this map");
            RenderWorldMap("no game clock on this map: the journey cannot run");
            return;
        }
        var refusal = _worldMap.Start();
        var (steps, hours) = _worldMap.Estimate();
        GD.Print($"ARC-UI-048 travel start refusal={refusal} steps={steps} hours={hours}");
        // The first step falls due 50 ms later (TL28); each step asks for the next one 62 ms on (RunWorldMapStep).
        if (refusal == AreaWorldMapRefusal.None && _areaSession!.ScheduleRealStep(SessionRealStep.WorldMapStep, WorldJourney.FirstStepRealMs) == GameSessionDisposition.Rejected)
        {
            GD.Print("ARC-UI-048 travel step refused by the session");
            CloseWorldMapByPlayer();
            return;
        }
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

    /// <summary>Per frame: a replaced session or another map closes the panel, and a journey that ended lands. MIG12 S8 (ARC-DEV-127): the travel steps
    /// themselves are the session's real-time events (<see cref="RunWorldMapStep"/>, run by the frame step: 50 ms after the start, then every
    /// <see cref="WorldJourney.StepRealMs"/> of real time, behind the Esc menu), not a delta loop of this frame.</summary>
    private void SyncAreaWorldMap()
    {
        if (!WorldMapOpen) { AdoptWorldMapData(); return; }
        if (!WorldMapActive || _worldMap is null || !ReferenceEquals(_worldMap.Session, _areaSession)) { EndWorldMap(); return; }
        if (_worldMap.Travelling)
        {
            // The chain's safety net: a repeat the session refused (a full queue) must not freeze the journey; it is asked for again here.
            if (_areaSession!.QueryRealStep(SessionRealStep.WorldMapStep) is null)
                _areaSession.ScheduleRealStep(SessionRealStep.WorldMapStep, WorldJourney.StepRealMs);
            return;
        }
        if (_worldMap.Landing is { } landing) LandWorldMap(landing, "arrived");
    }

    /// <summary>The WorldMap event's host (TL28): one travel step with its time jump of game and animation time together (TL26), a discovered area made
    /// known; returns the real time to the next step while the route continues, null when the journey ended (the frame lands it) or is gone.</summary>
    private long? RunWorldMapStep()
    {
        if (!WorldMapOpen || !WorldMapActive || _worldMap is not { Travelling: true } map || !ReferenceEquals(map.Session, _areaSession)) return null;
        var step = map.Step(JumpWorldMapClock);
        if (step.Discovered > 0) GD.Print($"ARC-UI-048 discovered area={step.Discovered} step={map.Steps}");
        if (!map.Travelling) return null;
        _worldMapPanel!.Render(_worldMapMessage);
        return WorldJourney.StepRealMs;
    }

    // The step's game time: a jump of game and animation time (TL26), through the session alone; the events it made due fire in the same frame step
    // (the pass that follows the step), and the lighting follows the hour changed it reports (TickAreaClock).
    private void JumpWorldMapClock(long gameMs)
    {
        var result = _areaSession!.JumpClocks(_areaSession.QueryClockSnapshot().Token, gameMs);
        if (result.Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Rejected) GD.Print("ARC-UI-048 clock refused " + result.Failure);
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
        // A refused request (the slot already holds a local-player request: unlikely while the modal map was open) leaves the panel closed and
        // the hero out of combat mode; the old path could not refuse.
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

    // W, a controller cancel or the button (Escape is the menu's key, not this window's): before the journey the panel closes; during it the PC lands where it stands (WT12).
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
        // MIG12 S8: a journey that ended any way (landing, cancel, the panel closed) leaves no travel step behind.
        _areaSession?.CancelRealStep(SessionRealStep.WorldMapStep);
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

    /// <summary>While the panel is open it owns every key: W closes it (or ends the journey), Enter travels; Escape is the menu's (it opens over the map).</summary>
    private void HandleWorldMapKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.W && !CharacterKeySilenced(key, routed)) CloseWorldMapByPlayer();
        else if (routed is Key.Enter or Key.KpEnter) _worldMapPanel!.Confirm();
        else if (routed == Key.Home) _worldMapPanel!.CentreOnParty();
        else if (routed == Key.Z && !CharacterKeySilenced(key, routed)) _worldMapPanel!.ToggleKind();
        else if (routed == Key.Delete) _worldMapPanel!.DropWaypoints();
    }

    private void HandleWorldMapPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseWorldMapByPlayer();
        else if (intent.Confirm) _worldMapPanel!.Confirm();
    }

    private string AreaWorldMapStatus() => !WorldMapActive ? "" : WorldMapOpen ? " | world map open: W closes"
        : " | W world map" + (DebugMode ? " (DEBUG MODE: teleport)" : "") + (_worldMapStatus is null ? "" : ": " + _worldMapStatus);
}
