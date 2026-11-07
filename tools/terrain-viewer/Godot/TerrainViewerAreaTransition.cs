using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-009: the Crash Site cave and the way back in crash-site-area. A left click on the visible
// entrance or exit object is the original use action (research T3): it reaches here through the ground-click
// path (_UnhandledInput, so never through the HUD or palette) under the same focus, modal, gate and lifecycle
// guards; it is accepted within Chebyshev distance 2, queued, and applied once when the actor is settled.
// A reset or reload takes precedence over a queued transfer. The area scene, its step evidence and chart are
// swapped as a whole; the session records the actor on the destination map at the authored arrival.
// ARC-WORLD-017: the companions that follow the PC go with it onto the arrival cell in the same session step.
// Every other click is left to its existing handling.
public partial class TerrainViewerApp
{
    private AreaTransitionCoordinator? _areaTransitions;
    private GameSessionOwner? _areaSession;

    /// <summary>ARC-DEV-080 (GI6): the gameplay generator's seed for a session the player starts, taken from the clock as the original
    /// does at its start-up (<see cref="AreaRandomSeed"/>); an explicit test or smoke launch keeps seed 0, which its checks rely on.</summary>
    private ulong NewSessionSeed() => AreaRandomSeed.ForNewSession(SelfTestBootstrap || _nativeSmoke, DateTimeOffset.UtcNow);
    private AreaLoadResult? _exteriorArea;
    private AreaCaveLoad? _caveLoad;
    private Arcanum.Data.Traversal.AreaStepSnapshot? _exteriorSteps;
    private string _areaUseStatus = "cave: not loaded";
    // A use clicked from too far: the click-to-move route walks within range first (the source use goal's
    // move-near step), then the same use is requested once on arrival.
    // ARC-DEV-174: the use goal 38's last node asked for the use of this object; applied at the frame's next safe point (ProcessUsePending).
    private AreaObjectKey? _useOpenPending;

    private bool InCave => _readyArea is not null && _caveLoad is not null && ReferenceEquals(_readyArea, _caveLoad.Area);

    // ARC-SCRIPT-006: the area's game session exists with or without the cave; dialogs keep their consequences in it.
    // Its clock profile comes from the area lighting source, or for a map drawn unlit from the module's start clock alone
    // (ARC-WORLD-057; the module id is the snapshot's module input, as the lighting source's). A synthetic self-test
    // publication has no lighting and gets an invented synthetic profile instead (never used for original data).
    private void StartAreaTransitions(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        StopAreaTransitions();
        _exteriorArea = result.Area; _exteriorSteps = result.AreaStepSnapshot;
        if (result.Area is not { } area) return;
        // ARC-RENDER-017: a whole interior (--area-map) has its own chart and none of the Crash Site roster.
        var mapMode = arguments.AreaMapName is not null;
        // ARC-WORLD-017: the roster companions are exterior NPCs; their T0 bodies can stand on any loaded map.
        _companionTemplates = mapMode ? MapCompanionTemplates(arguments, area) : AreaCompanionTemplate.From(AreaConversationHost.FindSpeakers(area.Snapshot), area.Plan, area.Snapshot.Visuals,
            AreaChart.Exterior, new(area.Snapshot.RuntimeMap));
        var moduleSha = area.Lighting?.Source.ModuleSha256 ?? area.Snapshot.Inputs.Single(input => input.Key.Role == AreaSourceRole.Module).Sha256;
        // ARC-WORLD-057: an unlit map's session starts from the module's clock alone.
        ProfileBinding? clock = area.Lighting is { } lighting
            ? AreaAmbientDriver.ClockProfile(lighting.Source)
            : result.UnlitSessionStartup is { } startup ? AreaAmbientDriver.ClockProfile(moduleSha, startup)
            : SelfTestBootstrap ? new(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision, moduleSha, moduleSha, 1885, 15) : null;
        // ARC-WORLD-031: a map reached by a transfer keeps the game session it carried.
        var carried = mapMode ? AdoptHandoffSession(arguments) : null;
        if (carried is null && clock is null)
        {
            if (mapMode) StartMapTransfers(arguments, result);
            _areaUseStatus = mapMode ? $"map {arguments.AreaMapName}: drawn unlit ({area.UnlitReason}), no session clock ({result.UnlitSessionStartupFailure}); " + _areaUseStatus
                : "cave: not loaded (no session clock)";
            return;
        }
        // A map the map list does not name has no runtime number, hence no session (and no dialogs, transfers or saves); ARC-RENDER-027
        // found the session refusing it (AREA_SESSION_InvalidActor) since unlit maps get a clock (ARC-WORLD-057).
        if (carried is null && mapMode && area.Snapshot.RuntimeMap < 1)
        {
            StartMapTransfers(arguments, result);
            _areaUseStatus = $"map {arguments.AreaMapName}: not in the map list (no runtime number), no session; " + _areaUseStatus;
            return;
        }
        // ARC-UI-036: a carried new character's kit tables come from the session's own kit (the load read the generic start again).
        var heroTables = carried is null ? result.Hero : SessionKitTables(result.Hero, carried);
        _areaEffectRules = heroTables?.Effects;
        _areaKitHealing = heroTables?.KitHealing;
        _areaKitLockpicks = heroTables?.KitLockpicks;
        _areaKitDurability = heroTables?.KitDurability;
        _areaKitWear = heroTables?.KitWear;
        _areaKitGridMove = heroTables?.KitGridMove;
        _areaKitArt = heroTables?.KitArt;
        _areaKitMissileArt = heroTables?.KitMissileArt;
        _areaKitThrowWeight = heroTables?.KitThrowWeight;
        _areaKitItemSpells = heroTables?.KitItemSpells;
        _areaKitUse = heroTables?.KitUse;
        if (carried is not null) { _areaSession = carried; AttachAreaEncumbranceEffect(); SyncTurnBasedSetting(carried); }
        else
        {
            // The game-generated object file carries an empty module GUID here, so the session's module id is
            // project-defined: the first 16 bytes of the authenticated module SHA-256. The world id is this session's.
            var header = Convert.FromHexString(moduleSha)[..16];
            // ARC-WORLD-044: the module's reputation table, read through the game folder's content stack.
            var reputations = AreaReputations.Load(arguments.ModulePath);
            GD.Print($"ARC-WORLD-044 reputations status={reputations.Status} detail={reputations.Detail}");
            var created = AreaTransitionCoordinator.CreateSession(clock!.Value, Guid.NewGuid(), new Guid(header), area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn), area.Snapshot.RuntimeMap,
                result.Hero, reputations.Catalog, NewSessionSeed());
            _areaSession = created.Owner ?? throw new InvalidOperationException("AREA_SESSION_" + created.Failure);
            AttachAreaEncumbranceEffect();
            ApplySessionSettings(_areaSession);
            // ARC-UI-011: the main menu's new game makes its session the same way, with the hero the player picks.
            _newGameRecipe = new(clock!.Value, new Guid(header), area.Plan.Chart.ToWorld(area.Plan.Chart.Spawn), area.Snapshot.RuntimeMap, reputations.Catalog);
        }
        // ARC-DEV-099: a session built here, or carried from the map before, answers the prototypes' script arrays for the items it creates.
        AttachAreaPrototypeScripts(_areaSession!);
        BindAreaInventory(arguments, result, keepLeft: carried is not null && _mapHandoff is { Kind: not MapHandoffKind.QuickLoad });
        // ARC-UI-055: the inventory art sizes read the seeds' rows, so they are attached once the seeds are.
        AttachAreaItemArtSizes();
        // ARC-UI-041: the map opens: its default music and ambient pair plays. ARC-WORLD-038: a session a quick load (or its
        // fallback) brings keeps the sound state it holds, as a same-map quick load does.
        if (_mapHandoff is { Kind: MapHandoffKind.QuickLoad or MapHandoffKind.Stream } && ReferenceEquals(carried, _areaSession)) RebindAreaSound();
        else EnterAreaMapSound(area.Snapshot.CatalogueMap);
        if (mapMode)
        {
            StartMapTransfers(arguments, result);
            if (area.UnlitLightsReason is { } lights) _areaUseStatus += $"; stationary lights not drawn ({lights})";
            return;
        }
        if (result.Cave is not { } cave) { _areaUseStatus = "cave: not loaded (no --area-cave-mobile)"; return; }
        // The cave draws a visiting companion from its own frame repository: the companions' T0 frames are added once.
        _caveLoad = _companionTemplates.Count == 0 ? cave
            : cave with { Area = cave.Area with { Frames = AreaCompanionTemplate.Frames(cave.Area.Frames, _companionTemplates, area.Frames) } };
        _areaTransitions = new([new(result.Area.Snapshot, result.AreaStepSnapshot!, AreaChart.Exterior), new(_caveLoad.Area.Snapshot, cave.Steps, cave.Chart)],
            area.Snapshot.RuntimeMap, _areaSession);
        _areaUseStatus = "cave: click the entrance within 2 cells";
        GD.Print("ARC-WORLD-009 transition-ready " + System.Text.Json.JsonSerializer.Serialize(new {
            Maps = _areaTransitions.Maps, Endpoints = _areaTransitions.Triggers().Select(t => t.Endpoint).ToArray(), CaveWalls = cave.Steps.WallEdges }));
    }

    private void StopAreaTransitions()
    {
        EndSheet(); _sheetStatus = null;
        EndLogbook(); _logbookStatus = null;
        EndSchematics(); _schematicsStatus = null; _schematicScriptOpen = null;
        EndBarter(); _barterStatus = null; _barterPending = null; _npcSheetPending = null;
        EndReading(); _readingStatus = null; _readingScriptOpen = null;
        EndWorldMap(); _worldMapStatus = null;
        EndMainMenuForTeardown();
        EndSaveList();
        UnbindAreaInventory();
        UnbindAreaCompanions();
        // A session a map transfer carries outlives the scene it leaves (ARC-WORLD-031).
        if (!ReferenceEquals(_areaSession, _mapHandoff?.Session) && !ReferenceEquals(_areaSession, _mapHandoff?.Previous))
        {
            // ARC-UI-046: a session that ends takes its level-up messages with it.
            EndLevelMessages(_areaSession);
            _areaSession?.Dispose();
        }
        _newGameRecipe = null;
        _areaTransitions = null; _areaSession = null; _useOpenPending = null; _useTargets.Clear(); _mapTransfers = null; _mapEntered.Clear(); _useScriptHost = null;
        _exteriorArea = null; _exteriorSteps = null; _caveLoad = null; _areaUseStatus = "cave: not loaded"; _companionTemplates = [];
        _audio?.StopAll(); _schemePlayer = null; _schemeVoices.Clear(); _combatStinger = _combatLoop = null; _soundSector = null;
        StopPositionalSound(); _tileArt = null; // the scene's terrain must not outlive it (reload retention check)
    }

    /// <summary>A guarded left press (see <c>WorldRouteClick</c>) on a trigger sprite: the hero's use goal 38 (ARC-DEV-174, <see cref="StartUseClick"/>).
    /// Returns true when the click was a use.</summary>
    private bool TryAreaUse(Vector2 screen)
    {
        if (_mapTransfers is not null) return TryAreaMapUse(screen);
        if (_areaTransitions is null || _readyArea is null || _worldActor.Session is not AreaGridSession) return false;
        var hit = AreaTriggerAt(screen);
        if (hit is null) return false;
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it");
        return StartUseClick(hit.Value);
    }

    /// <summary>The trigger object that is the frontmost pickable object under the screen point, if any (ARC-UI-005 pick).</summary>
    private AreaObjectKey? AreaTriggerAt(Vector2 screen)
    {
        if ((_areaTransitions is null && _mapTransfers is null) || AreaObjectAtScreen(screen) is not { } hit) return null;
        return IsAreaTrigger(hit) ? hit : null;
    }

    /// <summary>A standable cell within use range of the trigger, nearest the actor: admitted, and entered Open from
    /// some neighbour over the same step evidence the route and the session use.</summary>
    private CellCoord? UseApproach(AreaGridSession session, AreaObjectKey trigger)
    {
        var target = _areaTransitions!.Current.Snapshot.Objects.Single(o => o.Key == trigger);
        return ApproachCell(session, target.WorldX!.Value, target.WorldY!.Value, AreaTransitionCoordinator.UseRange);
    }

    /// <summary>A reachable, minimum-cost cell within <paramref name="range"/> of a world cell, shared by walk-to-use,
    /// pickup/loot and other object approaches.</summary>
    private CellCoord? ApproachCell(AreaGridSession session, long worldX, long worldY, int range, Func<CellCoord, bool>? accept = null)
    {
        var steps = session.Snapshot; var chart = session.Chart; var actor = session.State.CommittedCell;
        var center = chart.ToChart(new(worldX, worldY));
        var candidates = AreaRouteApproachPlanner.Candidates(center, actor, range)
            .Where(cell => chart.Contains(cell) && steps.IsAdmitted(chart.ToWorld(cell)) && accept?.Invoke(cell) != false);
        return AreaRouteApproachPlanner.Select(RouteSourceFor(session), actor, candidates,
            allowCorridor: _areaSession?.Rules.Navigation.FarClickDoesNotMove != true)?.Cell;
    }

    /// <summary>The hero's use goal ends (a quick load, a downed hero, the stick, a reset, a round's start): the run is killed and its walk stops; a use still asked for is dropped.</summary>
    private void CancelAreaUseApproach(string why)
    {
        var ended = ShownHeroRunsHost?.EndUse(why) == true;
        if (_useOpenPending is null && !ended) return;
        _useOpenPending = null; _areaUseStatus = "walk to use cancelled: " + why;
    }

    /// <summary>Applies a pending transfer once the actor is settled; a walk-to-use asks for its use on arrival.
    /// A reset or reload in progress drops both first. Returns true when the map changed.</summary>
    private bool ProcessAreaTransition()
    {
        if (_mapTransfers is not null || _mapHandoff is not null) return ProcessAreaMapTransfer();
        if (_areaTransitions is null) return false;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending)
        {
            CancelAreaUseApproach("reset/reload"); CancelTalkApproach("reset/reload"); CancelInventoryApproach("reset/reload"); CancelAreaAttack("reset/reload");
            if (_areaTransitions.Cancel() is { } dropped)
            {
                _areaUseStatus = $"transfer to map {dropped.ToMap} cancelled by reset/reload";
                GD.Print("ARC-WORLD-009 transition cancelled by lifecycle to=" + dropped.ToMap);
                RefreshAreaStatus();
            }
            return false;
        }
        // A queued transfer waits while the palette is open.
        if (_areaTransitions.Pending is null || WorldModal || _worldActor.Session is not AreaGridSession session || session.State.Phase != MovementPhase.Settled) return false;
        // ARC-WORLD-017 (research C15, V4): every following companion goes with the PC onto the arrival cell, every other
        // one stays where it stands; one atomic session step with the PC's placement. ARC-DEV-113 (MIG11 S2): the step is the payload
        // of the session's change of place, requested here (an endpoint's use is its script's teleport, flags 0, ledger TR1) and run by
        // this frame's ping.
        var pending = _areaTransitions.Pending!;
        var owner = _areaSession!;
        var submitted = owner.RequestHeroChangeOfPlace(Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceCaller.Script, pending.ToMap,
            new(checked((int)pending.Arrival.X), checked((int)pending.Arrival.Y)), Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceFlags.None, 0,
            map => _areaTransitions.Maps.Contains(map));
        var host = new CaveChangeHost(this, session);
        var run = submitted.Accepted ? owner.ProcessChangeOfPlace(host) : null;
        if (run is not null) LogChangeOfPlace(run);
        if (run?.Status != ChangeOfPlaceRunStatus.Completed)
        {
            // The session refused the step (nothing applied): the PC stays on this map and may use the endpoint again.
            _areaTransitions.Cancel();
            var why = run is null ? $"{submitted.Status} {submitted.Failure}" : run.Failure.ToString();
            _areaUseStatus = $"transfer to map {pending.ToMap} refused by the session ({why})";
            GD.Print("ARC-WORLD-017 transfer refused " + why);
            RefreshAreaStatus();
            return false;
        }
        var transition = pending;
        // ARC-COMBAT-004: the source's teleport leaves combat mode.
        LeaveCombatModeForMapChange();
        LeaveFollowersCombatMode(host.MoveList);
        if (host.Carried is { } carried)
            GD.Print("ARC-WORLD-017 companions carried " + System.Text.Json.JsonSerializer.Serialize(new {
                transition.FromMap, transition.ToMap, transition.Arrival, Carried = carried.Select(c => c.Speaker.ScriptId).ToArray(),
                Stayed = _areaCompanions!.Companions.Except(carried).Select(c => c.Speaker.ScriptId).ToArray() }));
        var target = _areaTransitions.Current;
        var area = transition.ToMap == _caveLoad!.Area.Snapshot.RuntimeMap ? _caveLoad.Area : _exteriorArea!;
        _readyArea = area;
        ConfigureAreaActor(target, transition.ArrivalChartCell, session.State.Facing);
        _camera!.SetAreaPlan(area.Plan, WorldViewport, window: false); // the pinned Crash Site and cave (not map mode)
        RebindAreaMapAmbient();
        EnterAreaMapSound(area.Snapshot.CatalogueMap);
        // The camera centres on the arrival first, then the scene is published at that view.
        _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        PublishWorldSelection();
        _areaUseStatus = InCave ? "in the cave: click the exit within 2 cells to return" : "back outside at the authored return cell";
        var placed = _areaSession!.QueryActor();
        GD.Print("ARC-WORLD-009 transition " + System.Text.Json.JsonSerializer.Serialize(new {
            transition.FromMap, transition.ToMap, transition.Arrival, Chart = transition.ArrivalChartCell,
            Session = new { Map = placed.Map.RuntimeMap, placed.Cell, Revision = _areaSession.Version.Revision } }));
        RefreshAreaStatus();
        return true;
    }

    // A new authoritative session on the map's chart; input observation restarts with it (a queue never
    // observes a retired session) and the safety gate waits for a neutral frame.
    private void ConfigureAreaActor(AreaMapRuntime map, CellCoord start, NeighborDirection facing)
    {
        _worldActor.Configure(map.Steps, WorldTime, chart: map.Chart, start: start, facing: facing);
        _worldGridInput = new(); _worldGate.Close();
        BindAreaCompanions();
        // ARC-DEV-150: a new grid actor stands somewhere at once; the session learns it now, not with the next advance.
        WriteHeroLocation(_worldActor.Session!);
    }

    private string AreaTransitionStatus()
    {
        if (_areaTransitions is null)
            return _areaSession?.QueryActor() is { } actor && _readyMapName is not null
                ? $" | session actor map {actor.Map.RuntimeMap} cell ({actor.Cell.X},{actor.Cell.Y}) rev {_areaSession.Version.Revision} | {_areaUseStatus}"
                : " | " + _areaUseStatus;
        var placed = _areaSession!.QueryActor();
        return $" | map {(InCave ? "Crash Site cave (5078/79)" : "Crash Site exterior (5000/1)")} | session actor map {placed.Map.RuntimeMap} cell ({placed.Cell.X},{placed.Cell.Y}) rev {_areaSession.Version.Revision} | {_areaUseStatus}";
    }

    // Native observation (ARC-WORLD-009): stand within use range of each trigger, click an opaque pixel of its
    // sprite through the production ground-click seam (_UnhandledInput and its guards), let the frames apply the
    // transfer, and observe the other map drawn. Frames run on simulated focus and test time, as the route matrix does.
    private int _areaSmokeTime;
    private async Task RunAreaTransitionSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var report = new List<object>();
            report.Add(await UseNextToTriggerAsync(79));
            // ARC-RENDER-012: the cave's own ambient and stationary lights, observed after the transfer.
            if (_areaAmbient is not null) report.Add(await RunCaveLightingSmokeAsync());
            report.Add(await UseNextToTriggerAsync(1));
            report.Add(await RunAreaWalkToUseSmokeAsync());
            report.Add(await RunAreaResetDropsTransferSmokeAsync());
            // Leave the exterior published for the later resource stages.
            report.Add(await UseNextToTriggerAsync(1));
            GD.Print("ARC-WORLD-009 native-smoke transition PASS " + System.Text.Json.JsonSerializer.Serialize(report));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // In the cave: an accepted use, then a reset before the next frame. The reset drops the queued transfer, the
    // map stays the cave and the session is the same; the actor is back on the cave arrival cell, in the grid and in the session.
    private async Task<object> RunAreaResetDropsTransferSmokeAsync()
    {
        var (_, trigger) = _areaTransitions!.Triggers().Single();
        var map = _areaTransitions.Current;
        var session = _areaSession!; var revision = session.Version.Revision;
        AreaSmokePlace(map, UseCell(map, new(trigger.WorldX!.Value, trigger.WorldY!.Value)));
        var command = _readyArea!.Plan.Commands.Single(c => c.Identity == trigger.Key);
        AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
        for (var wait = 0; wait < 40 && _areaTransitions.Pending is null; wait++) AreaSmokeFrame(); // ARC-DEV-174: the click starts the use goal; its last node asks for the use within a few table steps
        if (_areaTransitions.Pending is null) throw new InvalidOperationException("AREA_RESET_CASE_USE_NOT_ACCEPTED " + _areaUseStatus);
        ResetWorldScene();
        for (var i = 0; i < 4 && (_worldResetPending || _worldLifecycleSession is not null); i++) AreaSmokeFrame();
        // ARC-DEV-150: the hero's moves (the place, the reset's return to the spawn) are written to the session, so its revision moves; what must hold is that the
        // session is the same one, still on the cave, with the hero's location on the spawn cell.
        var spawn = map.Chart.ToWorld(map.Chart.Spawn); var held = session.QueryActor();
        if (_areaTransitions.Pending is not null || _areaTransitions.CurrentMap != map.RuntimeMap || !ReferenceEquals(_areaSession, session)
            || held.Map.RuntimeMap != map.RuntimeMap || held.Cell.X != spawn.X || held.Cell.Y != spawn.Y
            || _worldResetPending || ((AreaGridSession)_worldActor.Session!).State.CommittedCell != map.Chart.Spawn)
            throw new InvalidOperationException("AREA_RESET_DID_NOT_DROP_TRANSFER " + _areaUseStatus);
        return new { ResetDropsTransfer = true, Map = map.RuntimeMap, Session = revision };
    }

    /// <summary>ARC-DEV-181: a smoke that waits on the hero's attack ticks the area clock for it by <paramref name="ms"/> (a frame of the smokes' 1/20 s time); the swing's hit step and the repeat are the goal's.</summary>
    private void SmokeTickHeroRuns(int ms = 50)
    {
        if (ShownHeroRunsHost is not { } runs || runs.AttackRun is null && runs.ThrowRun is null && runs.CastRun is null && runs.SkillRun is null) return;
        // One monotonic host clock for the smokes that tick it by hand (a clock that runs back resets the frame's time observation): this counter and the cast smoke's.
        _talkSmokeHostMs = Math.Max(_talkSmokeHostMs, _spellSmokeHostMs) + ms;
        if (_spellSmokeHostMs != 0) _spellSmokeHostMs = _talkSmokeHostMs;
        TickAreaClock(_talkSmokeHostMs);
    }

    private void AreaSmokeFrame()
    {
        // ARC-DEV-169/174/175: a smoke does not tick the area clock itself (a native smoke's viewer does not); the hero's talk, use and pickup runs need it to step, so the frame ticks it while one is on the table.
        if (ShownHeroRunsHost is { } runs && (runs.TalkRun is not null || runs.UseRun is not null || runs.PickupRun is not null || runs.AttackRun is not null || runs.ThrowRun is not null || runs.CastRun is not null || runs.SkillRun is not null)) TickAreaClock(_talkSmokeHostMs += 100);
        _areaSmokeTime++;
        // ARC-DEV-204: a smoke's frame is a tenth of a second of the change-of-place screen too (a fade-out runs in 20 of them).
        TickTransitionScreen(100);
        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime));
    }

    // A fresh session at the cell, then neutral frames until the safety gate opens.
    private void AreaSmokePlace(AreaMapRuntime map, CellCoord cell)
    {
        _areaSmokeTime = 0; _worldTestTime = SimulationTime.At(0);
        ConfigureAreaActor(map, cell, NeighborDirection.XPlus);
        for (var i = 0; i < 3; i++) AreaSmokeFrame();
        if (_worldGate.IsOpen) return;
        // The frontier line prints only a bare code, so the cause goes on a line of its own.
        GD.Print("ARC-DEV-133 smoke placement gate closed: " + AreaSmokeGateWhy());
        throw new InvalidOperationException("AREA_SMOKE_GATE_CLOSED");
    }

    // What keeps the safety gate closed (ARC-DEV-133): a failed placement names its cause.
    private string AreaSmokeGateWhy() =>
        $"focused={_worldFocused} palette={PaletteOpen} window={AnyWindowOpen} saveList={SaveListOpen} mainMenu={MainMenuOpen} death={HeroDeathCovering} "
        + $"bugReport={BugReportOpen} uiFocus={WorldUiFocus} lifecycle={_worldLifecycleSession is not null} dragging={_dragging} "
        + $"activeDevice={_worldReducer.ActiveDevice?.ToString() ?? "-"} simulatedFocus={_worldFocusSimulationForSelfTest} frameClock={_frameClockForSelfTest}";

    // Frames the trigger, then returns an opaque pixel of its sprite outside the HUD.
    private async Task<Vector2> AreaSmokeTriggerPointAsync(AreaDrawCommand command)
    {
        PublishWorldSelection();
        _camera!.ResetWorld(new GamepadLab.Core.Vec2(command.Destination.X + command.Destination.Width / 2d,
            command.Destination.Y + command.Destination.Height / 2d), WorldViewport, 1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return OpaqueScreenPoint(command) ?? throw new InvalidOperationException("AREA_TRIGGER_NOT_VISIBLE");
    }

    // ARC-DEV-169: a click on a dialog NPC starts the hero's talk goal on the table; the table steps while the area clock is ticked (a smoke ticks it itself, 0.1 s a frame, as the walks' smokes do), and
    // the goal's last node asks for the dialog at its first steps: a few frames, not the click itself.
    private long _talkSmokeHostMs = 80_000_000L;

    private void AreaSmokeTalkFrame() => AreaSmokeFrame();

    private void AreaSmokeFramesUntilConversation(int frames = 40)
    {
        for (var i = 0; i < frames && !ConversationOpen; i++) AreaSmokeTalkFrame();
    }

    private void AreaSmokeClick(Vector2 point)
    {
        _worldTestTime = SimulationTime.At(_areaSmokeTime);
        _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
    }

    private async Task<object> UseNextToTriggerAsync(int expected)
    {
        var (endpoint, trigger) = _areaTransitions!.Triggers().Single();
        var map = _areaTransitions.Current;
        var near = UseCell(map, new(trigger.WorldX!.Value, trigger.WorldY!.Value));
        AreaSmokePlace(map, near);
        var command = _readyArea!.Plan.Commands.Single(c => c.Identity == trigger.Key);
        AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
        for (var wait = 0; wait < 40 && _areaTransitions.Pending is null; wait++) AreaSmokeFrame(); // ARC-DEV-174: as above
        if (_areaTransitions.Pending is null) throw new InvalidOperationException("AREA_USE_NOT_ACCEPTED " + _areaUseStatus);
        AreaSmokeFrame();
        if (_areaTransitions.CurrentMap != expected || _areaTransitions.Pending is not null) throw new InvalidOperationException("AREA_TRANSITION_NOT_APPLIED");
        var arrival = _areaTransitions.Current.Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
        if (arrival.X != endpoint.ArrivalX || arrival.Y != endpoint.ArrivalY || _areaSession!.QueryActor().Map.RuntimeMap != expected)
            throw new InvalidOperationException("AREA_ARRIVAL_MISMATCH");
        for (var frame = 0; frame < 5; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_TRANSITION_CAPTURE");
        if (_areaWallCheck is { WallCount: > 0 }) await CheckAreaWallFadeAsync(string.IsNullOrWhiteSpace(capture) ? null : $"{capture}-map{expected}");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng($"{capture}-map{expected}.png") != Error.Ok) throw new InvalidOperationException("AREA_TRANSITION_CAPTURE_FAILED");
            // A wider view of the arrival surroundings, then the actor-framed view again.
            _camera!.ResetWorld(WorldAnchor, WorldViewport, .5);
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            using var wide = GetViewport().GetTexture().GetImage();
            if (wide.SavePng($"{capture}-map{expected}-wide.png") != Error.Ok) throw new InvalidOperationException("AREA_TRANSITION_CAPTURE_FAILED");
            _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        }
        return new { From = map.RuntimeMap, To = expected, UsedFrom = map.Chart.ToWorld(near), Arrival = arrival, Session = _areaSession.Version.Revision,
            Drawn = _readyArea.Plan.Commands.Count, Walls = _readyArea.Snapshot.Objects.Count(o => o.Type == 0) };
    }

    // From the exterior return cell (three cells from the entrance), one left click on the entrance sets the
    // click-to-move goal within use range; the frames drive the authoritative session there, and the same use
    // is requested on arrival and applied once settled. A ground click first cancels a pending walk-to-use.
    private async Task<object> RunAreaWalkToUseSmokeAsync()
    {
        var (_, trigger) = _areaTransitions!.Triggers().Single();
        var command = _readyArea!.Plan.Commands.Single(c => c.Identity == trigger.Key);
        var start = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
        AreaSmokePlace(_areaTransitions.Current, start);
        var point = await AreaSmokeTriggerPointAsync(command);
        AreaSmokeClick(point);
        if (ShownHeroRunsHost?.UseRun is null) throw new InvalidOperationException("AREA_WALK_NOT_STARTED " + _areaUseStatus);
        // A ground click on the actor's own cell (or the nearest cell whose centre is plain ground) replaces the
        // walk-to-use; no use follows.
        var ground = Enumerable.Range(0, 7).SelectMany(r => Enumerable.Range(-r, 2 * r + 1).Select(d => new CellCoord(start.X + d, start.Y - r)))
            .Select(cell => { var c = AreaCellPicking.Center(cell); return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)c.X, (float)c.Y); })
            .First(p => !_hud!.Rect.HasPoint(p) && GetViewport().GetVisibleRect().HasPoint(p) && AreaTriggerAt(p) is null);
        AreaSmokeClick(ground);
        if (ShownHeroRunsHost?.UseRun is not null) throw new InvalidOperationException("AREA_WALK_NOT_CANCELLED_BY_GROUND_CLICK");
        for (var i = 0; i < 40; i++) AreaSmokeFrame();
        if (_areaTransitions.CurrentMap != 1 || _areaTransitions.Pending is not null) throw new InvalidOperationException("AREA_CANCELLED_WALK_USED");
        // The camera followed those frames: frame the trigger again before the second click.
        AreaSmokePlace(_areaTransitions.Current, start);
        AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
        if (ShownHeroRunsHost?.UseRun is null) throw new InvalidOperationException("AREA_WALK_NOT_RESTARTED " + _areaUseStatus);
        for (var i = 0; i < 400 && _areaTransitions.CurrentMap == 1; i++) AreaSmokeFrame();
        if (_areaTransitions.CurrentMap != 79 || _areaSession!.QueryActor().Map.RuntimeMap != 79)
            throw new InvalidOperationException("AREA_WALK_TO_USE_NOT_APPLIED " + _areaUseStatus);
        return new { WalkToUse = true, GroundClickCancels = true, From = start, Frames = _areaSmokeTime, Session = _areaSession.Version.Revision };
    }

    // A cell next to the trigger reachable over the map's step evidence from the chart's start.
    private static CellCoord UseCell(AreaMapRuntime map, Arcanum.Data.Traversal.AreaStepCell trigger)
    {
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        var start = map.Chart.Spawn; var goal = map.Chart.ToChart(trigger);
        var seen = new HashSet<CellCoord> { start }; var queue = new Queue<CellCoord>([start]);
        while (queue.TryDequeue(out var cell))
        {
            if (Math.Max(Math.Abs(cell.X - goal.X), Math.Abs(cell.Y - goal.Y)) is > 0 and <= 1) return cell;
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cell, direction);
                if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(cell, direction) != Arcanum.Simulation.Navigation.RouteStepVerdict.Open) continue;
                seen.Add(next); queue.Enqueue(next);
            }
        }
        throw new InvalidOperationException("AREA_TRIGGER_UNREACHABLE");
    }

    private Vector2? OpaqueScreenPoint(AreaDrawCommand command)
    {
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size;
        var hud = _hud!.Rect;
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
            if (!hud.HasPoint(new(x + .5f, y + .5f)) && AreaAlphaAt(command, _readyArea!.Frames, transform, x, y) != 0 && AreaTriggerAt(new(x + .5f, y + .5f)) == command.Identity)
                return new Vector2(x + .5f, y + .5f);
        return null;
    }
}
