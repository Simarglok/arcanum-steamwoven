using Arcanum.Input;
using Arcanum.Input.Godot;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

using Input = global::Godot.Input;

public partial class TerrainViewerApp
{
    private GodotInputAdapter _worldAdapter = new() { WorldRoute = true };
    private InputStateReducer _worldReducer = new();
    private DiagnosticInputSafetyGate _worldGate = new();
    private DiagnosticGridActor _worldActor = new();
    private GridInputQueue _worldGridInput = new();
    // ARC-WORLD-007: LMB click-to-move, bound only to the whole-core AREA session. The
    // follower belongs to exactly one session; a reset/reload session gets a fresh one.
    private RouteFollower? _worldRoute;
    private IGridSession? _worldRouteSession;
    private string _worldRouteDiagnostic = "no click yet";
    private string _worldRouteClick = "none";
    // ARC-WORLD-016: a route step denied by a live companion is planned again a few times for the same goal.
    private bool _worldActorDenied;
    private (CellCoord? Goal, int Count) _worldActorRetries;
    private const int ActorDenialRetries = 8;
    private bool _worldResetPending;
    private bool _worldReloadPending;
    private IGridSession? _worldLifecycleSession;
    private string _worldGridDiagnostic = "No step assessment yet";
    private string? _worldLastStepAttempt;
    private SimulationTime? _worldTestTime;
    private SimulationTime? _worldObservationTime;
    // ARC-DEV-125 (MIG12 S6): the world time. A test's literal time first (automated runs drive their own); in play the session's frame
    // clocks (the world milliseconds the frame's participant carried, so the walk, the attack and cast, projectiles, missiles, eye candy,
    // the dying animations, scenery and pools stop with the Esc pause and never run ahead of the clock the events see); the host's own
    // time only where no frame runs (no lighting driver, a WORLD diagnostic scene, before the first frame).
    private SimulationTime WorldTime => _worldObservationTime ?? _worldTestTime ?? LiveWorldTime();
    private readonly WorldTimeline _worldTimeline = new();
    // The frame participant of the bound ambient driver (null while none is bound).
    private FrameClockTap? _frameTap;
    // A smoke that drives the frame clock by literal host stamps (TickAreaClock) and reads the world time from it.
    private bool _frameClockForSelfTest;
    private bool LiveFrameClock => _frameTap is not null && (!(SelfTestBootstrap || _nativeSmoke) || _frameClockForSelfTest);
    private SimulationTime LiveWorldTime() => SimulationTime.At(_worldTimeline.Read(LiveFrameClock ? _frameTap : null, (long)Time.GetTicksUsec()), 1_000_000);
    // ARC-WORLD-025: a smoke stage drives the actor on test time, which can run ahead of the wall clock. The time it
    // restores must not be older than the actor's session or the stage's last observation, or the next observation (a
    // quarantine on a pad change, the next frame) is refused as late or non-monotonic (GRID_OBSERVATION_LATE_OR_INVALID,
    // GRID_OBSERVATION_NONMONOTONIC): the clock then stays at the later of the two, possibly for the rest of the smoke.
    // Call it after the stage's last observation (its pad release), never before.
    private void RestoreWorldTestTime(SimulationTime? saved)
    {
        var floor = _worldTestTime;
        if (_worldActor.Session?.State.Time is { } session && (floor is null || floor.Value.Value < session.Value)) floor = session;
        var restored = saved ?? SimulationTime.At(Time.GetTicksUsec(), 1_000_000);
        _worldTestTime = floor is { } latest && restored.Value < latest.Value ? latest : saved;
    }
    private void WorldObservation(Action action)
    {
        if (_worldObservationTime.HasValue) { action(); return; }
        _worldObservationTime = WorldTime;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or GridReplayCapacityException or OverflowException or TerrainViewerException or ArgumentException)
        {
            // Fault disposal is distinct from motion settlement. Do not retry
            // quarantine on an invalid/closed clock or publish trial events.
            _worldGate.Close(); _worldReducer.Quarantine();
            GD.Print("ARC-WORLD-003 lifecycle=observation-fault-disposal (not CenterArrived)");
            // Only our closed numeric input/runtime category is safe here;
            // arbitrary exception text can contain protected input paths.
            if (error.Message.StartsWith("GRID_", StringComparison.Ordinal)
                && error.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                GD.Print("ARC-WORLD-003 observation-failure=" + error.Message);
            // ARC-DEV-065: the exception's type and its methods (no message, no file paths) name the failing code in a player's log.
            GD.Print("ARC-DEV-065 observation-fault " + ObservationFaultTrace(error));
            PublishFailure(_arguments?.Mode ?? _readyMode, new(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan));
            if (SelfTestBootstrap || _nativeSmoke) throw;
        }
        finally { _worldObservationTime = null; }
    }
    internal static string ObservationFaultTrace(Exception error)
    {
        var type = $"type={error.GetType().FullName}";
        // The trace must never replace the fault it reports.
        try
        {
            var frames = new System.Diagnostics.StackTrace(error, false).GetFrames()
                .Select(f => f.GetMethod() is { } m ? $"{m.DeclaringType?.FullName}.{m.Name}" : "?").Take(24);
            return type + " at " + string.Join(" < ", frames);
        }
        catch (Exception) { return type; }
    }
    private void RetireWorldLifecycle()
    {
        _worldResetPending = false; _worldReloadPending = false; _worldLifecycleSession = null; _worldPaletteReloadRequested = false;
    }
    private readonly DiagnosticInputSettingsStore _worldStore = new();
    private readonly DiagnosticZoomSettingsStore _worldZoomStore = new();
    private double _worldPreferredZoom = 2;
    private bool _worldFitView;
    private bool _worldZoomPending;
    private bool _worldZoomSaveFailed;
    private double _worldZoomSettled;
    private DiagnosticInputSettingsPanel? _worldPanel;
    private InputSettings _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings;
    // Diagnostic chord ownership comes before every other keyboard mapping.
    private readonly DiagnosticPaletteChord _worldChord = new();
    private string? _worldLastReserved;
    private bool _worldFocused = true;
    private Window? _worldFocusWindow;
    // One initialization owns the panel and every subscription; ExitWorld
    // consumes it once and a later InitializeWorld acquires a fresh set.
    private bool _worldInitialized;
    private Action<string>? _worldInputTraceForSelfTest;
    private void TraceWorldInput(string seam)
    {
        if (_worldInputTraceForSelfTest is null) return;
        var focus = GetViewport().GuiGetFocusOwner();
        _worldInputTraceForSelfTest(System.Text.Json.JsonSerializer.Serialize(new {
            Seam = seam, Host = GetInstanceId(), Viewport = GetViewport().GetInstanceId(),
            Panel = _worldPanel?.GetInstanceId(), Focus = focus?.GetInstanceId(),
            FocusClass = focus?.GetClass().ToString(), FocusName = focus?.Name.ToString(),
            Modal = WorldModal, Initialized = _worldInitialized,
            Focused = _worldFocused, WindowFocused = GetWindow().HasFocus(),
            SimulatedFocus = _worldFocusSimulationForSelfTest, TestClock = _worldTestTime.HasValue,
            Processing = IsProcessing(), ActiveDevice = _worldReducer.ActiveDevice,
            Settings = _worldSettings, PanelSettings = _worldPanel?.Settings,
            Gate = _worldGate.IsOpen, Actor = _worldActor.State.Position, Camera = _camera?.State
        }));
    }

    private bool _worldUiFocused;
    private int _worldMenuAnalogDirection;
    private bool IsWorld => (_arguments is not null && IsWorldMode(_arguments.Mode)) || IsWorldMode(_readyMode);
    private bool PaletteOpen => _worldPanel?.IsOpen == true;
    // ARC-DEV-109 (MIG16 S2): the windows that are modes (conversation, inventory, sheet, logbook, schematics, barter, reading, world map)
    // are read from the interface mode stack; the panels outside the stack keep their own flags.
    // This is the hero's INPUT gate and the cursor's, not the clock's: since ARC-DEV-126 (MIG12 S7, GQ3 A) a window, the palette or the bug report no longer stops the world.
    private bool WorldModal => PaletteOpen || AnyWindowOpen || SaveListOpen || MainMenuOpen || HeroDeathCovering || BugReportOpen;
    // ARC-DEV-126: the one raiser of the pause is the Esc menu and a save or load list (TL23); the options screen the menu leads to is under it. `_mainMenu` covers the frames in which the menu's panel is hidden while it waits for the save list to open.
    private bool WorldMenuPause => MainMenuOpen || SaveListOpen || _mainMenu is not null;
    // ARC-DEV-126: the death cover and its slide show are frames in which the original does not ping (TL25): no stamp is observed and no clock moves.
    private bool WorldFrozenFrame => HeroDeathCovering;
    private bool WorldUiFocus => !WorldModal && GetViewport().GuiGetFocusOwner() is not null;
    private Vec2 WorldAnchor => _worldActor.FootAnchor;
    private Vector2 WorldViewport => GetViewport().GetVisibleRect().Size;

    private void InitializeWorld()
    {
        if (_worldInitialized) return;
        _worldInitialized = true;
        // WORLD uses actual viewport pixels for bounds/HUD; a fixed virtual
        // 1280x800 stretch viewport would conceal native resize observations.
        // Do not change the host window for the isolated SubViewport harness.
        if (GetViewport() == GetWindow()) GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        _worldSettings = _worldStore.Load();
        _worldPreferredZoom = _worldZoomStore.Load();
        // ARC-WORLD-016: the PC's AREA steps are admitted against the shown map's companions.
        _worldActor.AreaOccupancy = AreaPcOccupancy;
        // ARC-WORLD-074: outside combat mode the PC steps through living NPCs and companions (the owner's decision 2026-10-21).
        _worldActor.AreaCrittersPass = () => !HeroInCombatMode;
        // ARC-COMBAT-020: in a turn-based round each step the PC starts charges its action points; ARC-WORLD-110: the walk's trap check
        // of the next tile comes first.
        _worldActor.AreaStepCharge = HeroStepGate;
        ConfigureGait();
        _worldPanel = new DiagnosticInputSettingsPanel(); AddChild(_worldPanel);
        _worldPanel.Changed += SaveWorldSettings;
        _worldPanel.CloseRequested += ToggleWorldSettings;
        _worldPanel.ResetRequested += ResetWorldSettings;
        _worldPanel.ZoomRequested += SetPreferredWorldZoom;
        _worldPanel.FitRequested += FitWorldView;
        _worldPanel.ResetSceneRequested += PaletteResetScene;
        _worldPanel.ReloadRequested += PaletteReload;
        _worldPanel.GameHourRequested += PaletteGameHour;
        _worldPanel.LightingToggleRequested += PaletteLightingToggle;
        _worldPanel.VisualToggleRequested += PaletteVisualToggle;
        _worldPanel.AlwaysRunToggleRequested += PaletteAlwaysRunToggle;
        _worldPanel.ProwlToggleRequested += PaletteProwlToggle;
        _worldPanel.PickPocketRequested += ArmPickPocket;
        _worldPanel.SoundTestRequested += PaletteSoundTest;
        _worldPanel.SaveGameRequested += PaletteSaveGame;
        _worldPanel.LoadGameRequested += PaletteLoadGame;
        _worldPanel.VolumeChanged += PaletteVolumeChanged;
        _worldPanel.OverlayToggled += SetPassabilityOverlay; _overlayPanelState = null;
        _hud!.SettingsRequested += ToggleWorldSettings;
        Input.JoyConnectionChanged += WorldConnectionChanged;
        _worldFocusWindow = GetWindow();
        _worldFocusWindow.FocusEntered += WorldWindowFocusEntered;
        _worldFocusWindow.FocusExited += WorldWindowFocusExited;
        _worldAdapter.Clear(); _worldAdapter.WorldRoute = true; _worldAdapter.PaletteOpen = false; _worldChord.Reset();
        _worldReducer.SetStartupDevices(Input.GetConnectedJoypads().Select(id => (int)id));
        _worldFocused = GetWindow().HasFocus();
        _worldUiFocused = WorldUiFocus;
        QuarantineWorld();
    }
    private InputContext WorldContext(double delta = 0, bool advance = false) => new(
        CameraDeadzone: _worldSettings.CameraDeadzone, ActorDeadzone: _worldSettings.ActorDeadzone,
        CameraSensitivity: _worldSettings.CameraSensitivity, ActorSensitivity: _worldSettings.ActorSensitivity,
        Delta: delta, Zoom: _camera?.State.Zoom ?? 1, InvertCameraX: _worldSettings.InvertCameraX,
        InvertCameraY: _worldSettings.InvertCameraY, Modal: WorldModal || WorldUiFocus, AdvanceTime: advance)
    { WorldZoomEnabled = true, KeyboardCameraFixedSign = true, ScrollUnderModal = WorldModal && WorldScrollsGameView };

    private void QuarantineWorld() => WorldObservation(QuarantineWorldObserved);
    private void QuarantineWorldObserved()
    {
        TraceWorldInput("Quarantine");
        _gameMenuEscapeArmed = false; // ARC-INPUT-1124: a held Escape whose release another owner may take opens nothing later.
        _worldGate.Close(); _worldReducer.Quarantine();
        // Palette, focus loss, device change, reset/reload and faults all pass here:
        // the route stops, and the step already started still ends at its cell centre.
        StopWorldRoute("ROUTE_STOPPED_QUARANTINE");
        if (_worldActor.Session is { } session && !_exiting)
            _worldGridInput.Observe(session, WorldTime, ActorIntent.Neutral);
        _worldMenuAnalogDirection = 0;
        _dragging = false; _camera?.SafetyResetWorld();
        if (IsWorldMode(_readyMode) && _readyActorPlan is not null) PublishWorldSelection();
    }
    private void ResetWorldScene() => WorldObservation(() => RequestWorldLifecycle(false));
    private void RequestWorldLifecycle(bool reload)
    {
        QuarantineWorld();
        if (_worldActor.Session is null) return;
        _worldLifecycleSession = _worldActor.Session;
        _worldReloadPending |= reload; _worldResetPending = !_worldReloadPending;
        FlushWorldZoom(); _worldFitView = false;
        RefreshReadyStatus();
    }
    private void ToggleWorldSettings()
    {
        if (!_worldInitialized || !IsWorld || _worldPanel is null || ConversationOpen || InventoryOpen || SheetOpen || LogbookOpen || SchematicsOpen || BarterOpen || ReadingOpen || WorldMapOpen || SaveListOpen || MainMenuOpen) return;
        QuarantineWorld();
        if (_worldPanel.IsOpen) { _worldPanel.Hide(); GetViewport().GuiReleaseFocus(); _visualKeyNotice = null; }
        else { _worldPanel.Open(_worldSettings); BindWorldZoom(); BindWorldPaletteSummary(); BindAreaAmbientPalette(); _overlayPanelState = null; SyncPassabilityOverlay(); }
        _worldAdapter.PaletteOpen = _worldPanel.IsOpen;
        RefreshReadyStatus();
    }
    private void SaveWorldSettings(InputSettings settings)
    {
        TraceWorldInput("Save:before");
        _worldSettings = settings.Validated(DiagnosticWorldPolicy.DefaultInputSettings); QuarantineWorld();
        try { _worldStore.Save(_worldSettings); _worldPanel?.SetSaveStatus(true); }
        catch (InvalidOperationException) { _worldPanel?.SetSaveStatus(false); }
        TraceWorldInput("Save:after");
    }
    // Preferences only: the scene, actor and Follow are not reset (Reset scene is separate).
    private void ResetWorldSettings()
    {
        SaveWorldSettings(DiagnosticWorldPolicy.DefaultInputSettings); SetPreferredWorldZoom(2);
        _worldPanel?.Bind(_worldSettings);
        _worldPanel?.SetActionStatus("Preferences reset to defaults (input false/false, zoom 2.00); scene unchanged.");
    }
    private void PaletteResetScene()
    {
        if (_worldActor.Session is null) { _worldPanel?.SetActionStatus("Reset scene unavailable: no ready scene."); return; }
        ResetWorldScene();
        _worldPanel?.SetActionStatus("Reset scene requested: the accepted step settles, then actor/Follow reset; preferences kept.");
    }
    // The palette reports how its Reset scene / Reload ended, not only the request.
    // A palette Reload is reported only for the load generation it started; a
    // retired request or an unrelated later publish never reads as "Reload done".
    private bool _worldPaletteReloadRequested;
    private long? _worldPaletteReloadGeneration;
    private void PaletteScenePublished()
    {
        _overlayPanelState = null; SyncPassabilityOverlay();
        if (_worldPaletteReloadGeneration != _generation) return;
        _worldPaletteReloadGeneration = null;
        _worldPanel?.SetActionStatus($"Reload done: generation {_generation} published; actor at spawn.");
        BindWorldPaletteSummary();
    }
    private void PaletteSceneFailed(TerrainViewerError error)
    {
        _worldPaletteReloadGeneration = null;
        if (_worldPanel is { } panel && GodotObject.IsInstanceValid(panel))
            panel.SetActionStatus($"Scene FAILED: stage={error.Stage} code={error.Code}; the old scene is not republished.");
    }
    private void PaletteReload()
    {
        if (_arguments is null || _worldActor.Session is null) { _worldPanel?.SetActionStatus("Reload unavailable: no loaded source."); return; }
        WorldObservation(() => RequestWorldLifecycle(true));
        _worldPaletteReloadRequested = _worldReloadPending;
        _worldPanel?.SetActionStatus("Reload requested: the accepted step settles, then the current source reloads; failures are shown, the old scene is not republished.");
    }
    private void BindWorldPaletteSummary()
    {
        if (_worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel)) return;
        var runtime = _worldActor.Session?.State;
        _worldPanel.SetSummary($"{(_worldActor.Session is AreaGridSession ? "Whole Crash Site core at T0" : "Bounded WORLD test area")}; cell {runtime?.CommittedCell} {runtime?.Phase}; camera {_camera?.FollowState.Mode}; last assessment: {_worldGridDiagnostic}");
        BindVisualPalette(); BindGaitPalette(); BindAudioPalette();
    }
    private void BindWorldZoom()
    {
        // Parent _ExitTree may run after the panel has left/freed its children.
        if (_exiting || _worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel) || !_worldPanel.IsInsideTree()) return;
        _worldPanel.BindZoom(_camera?.State.Zoom ?? _worldPreferredZoom, _worldPreferredZoom, _worldFitView, _worldZoomPending, _worldZoomSaveFailed);
    }
    private void SetPreferredWorldZoom(double value) => ApplyPreferredWorldZoom(value, false);
    private void ApplyPreferredWorldZoom(double value, bool continuous)
    {
        if (!double.IsFinite(value) || _camera is null) return;
        var shown = _camera.State.Zoom;
        _camera.SetWorldZoom(value, WorldAnchor, WorldViewport); _worldFitView = false;
        // ARC-RENDER-032: a window's floor limits the shown zoom, not the preference other maps open at; a held trigger at the
        // floor (the shown zoom unchanged) leaves the preference as it is.
        if (continuous && _camera.WindowFloored && _camera.State.Zoom == shown) return;
        _worldPreferredZoom = _camera.WindowFloored ? WorldZoomPolicy.Absolute(_worldPreferredZoom, value) : _camera.State.Zoom;
        _worldZoomPending = true; _worldZoomSaveFailed = false; _worldZoomSettled = 0;
        if (!continuous) FlushWorldZoom();
        BindWorldZoom(); RefreshReadyStatus();
    }
    private void StepPreferredWorldZoom(double factor) => SetPreferredWorldZoom(WorldZoomPolicy.Step(_camera!.State.Zoom, factor));
    private void FitWorldView()
    {
        QuarantineWorld(); _camera?.Fit(WorldViewport); _worldFitView = true;
        BindWorldZoom(); RefreshReadyStatus();
    }
    private void FlushWorldZoom()
    {
        if (!_worldZoomPending) return;
        try { _worldZoomStore.Save(_worldPreferredZoom); _worldZoomPending = false; _worldZoomSaveFailed = false; }
        catch (InvalidOperationException) { _worldZoomSaveFailed = true; }
        BindWorldZoom();
    }
    private void SettleWorldZoom(double delta, bool driving)
    {
        if (!_worldZoomPending || _worldZoomSaveFailed || driving) return;
        _worldZoomSettled = Math.Min(.25, _worldZoomSettled + Math.Min(delta, .25));
        if (_worldZoomSettled >= .25) FlushWorldZoom();
    }
    private void WorldConnectionChanged(long id, bool connected)
    {
        if (_worldInitialized) WorldObservation(() => WorldConnectionObserved(id, connected));
    }
    private void WorldConnectionObserved(long id, bool connected)
    {
        var device = checked((int)id);
        if (!connected)
        {
            var active = _worldReducer.ActiveDevice == device;
            _worldAdapter.ForgetDevice(device); _worldReducer.Disconnect(device);
            if (active) QuarantineWorld();
        }
        // A notification is never a complete neutral observation. Active reconnect
        // and meaningful acquisition close the gate on the next frame below.
    }
    public override void _Notification(int what)
    {
        // ARC-UI-005: the pointer leaving the window hides the hover tooltip and action cursor.
        // An automated run's pointer is the one its test places (HoverAtForSelfTest, WorldPointerForSelfTest): the real pointer entering or
        // leaving the window must not flip it (the border scroll of the inv-ui smoke failed whenever the real pointer was outside the window).
        if (!AutomatedVisualRun)
        {
            if (what == NotificationWMMouseExit) _pointerInWindow = false;
            else if (what == NotificationWMMouseEnter) _pointerInWindow = true;
        }
        if (!IsWorld || !_worldInitialized) return;
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationFocusIn)
            ObserveWorldFocus(what == NotificationApplicationFocusIn && GetWindow().HasFocus());
    }
    private void WorldWindowFocusEntered() => ObserveWorldFocus(true);
    // Raw focus-exit signals of the world window, also under simulated focus: the native smoke checks that opening the follower
    // order menu does not take the window's focus, as an embedded popup did (ARC-WORLD-046).
    private int _worldFocusExitSignals;
    private void WorldWindowFocusExited() { _worldFocusExitSignals++; ObserveWorldFocus(false); }
    private void ObserveWorldFocus(bool focused)
    {
        if (_exiting || !IsWorld || !_worldInitialized) return;
        // ARC-UI-099: a window that lost focus forgets the pointer; the edge scroll waits for the next pointer event.
        if (!focused) _worldPointer = null;
        // Native window focus can change while the application remains active
        // (minimize or another window). Application notifications alone miss it.
        // Only explicitly simulated navigation owns focus during its GUI tests.
        if (_worldFocusSimulationForSelfTest && (SelfTestBootstrap || _nativeSmoke)) return;
        if (_worldFocused == focused) return;
        WorldObservation(() =>
        {
            _worldFocused = focused;
            if (!focused) { CancelPendingOrder("the window lost focus"); _orderKeyArmed = null; _gameMenuEscapeArmed = false; CancelArmedLockpicks("the window lost focus"); }
            _worldReducer.FocusChanged(focused); QuarantineWorld(); RefreshReadyStatus();
            TraceWorldInput("NativeFocus:transition");
        });
    }
    private void HandleWorldPulse(IntentFrame intent, bool ownsMenuAnalog)
    {
        if (_worldInputTraceForSelfTest is not null) TraceWorldInput($"Pulse accepted={intent.Accepted} confirm={intent.Confirm}");
        if (!intent.Accepted) return;
        if (HeroDeathCovering) { HandleHeroDeathPulse(intent); return; }
        // ARC-DEV-111: the save list and the menu are on top of a window under them.
        if (SaveListOpen) { HandleSaveListPulse(intent); return; }
        if (MainMenuOpen) { HandleMainMenuPulse(intent); return; }
        if (ConversationOpen) { HandleConversationPulse(intent); return; }
        if (InventoryOpen) { HandleInventoryPulse(intent); return; }
        if (SheetOpen) { HandleSheetPulse(intent); return; }
        if (LogbookOpen) { HandleLogbookPulse(intent); return; }
        if (SchematicsOpen) { HandleSchematicsPulse(intent); return; }
        if (BarterOpen) { HandleBarterPulse(intent); return; }
        if (ReadingOpen) { HandleReadingPulse(intent); return; }
        if (WorldMapOpen) { HandleWorldMapPulse(intent); return; }
        if (intent.Settings || (WorldModal && intent.Cancel)) { ToggleWorldSettings(); return; }
        if (!WorldModal) return;
        if (intent.MenuPressedDirection != 0) _worldPanel?.Navigate(intent.MenuPressedDirection);
        // Only the selected device can release its analog menu latch. A neutral
        // keyboard reduction in the same frame is not controller-neutral proof.
        if (ownsMenuAnalog)
        {
            var analogDirection = Math.Abs(intent.MenuAnalog) >= .6 ? Math.Sign(intent.MenuAnalog) : 0;
            if (analogDirection != 0 && analogDirection != _worldMenuAnalogDirection) _worldPanel?.Navigate(analogDirection);
            _worldMenuAnalogDirection = analogDirection;
        }
        if (intent.Previous) _worldPanel?.Adjust(-1);
        if (intent.Next) _worldPanel?.Adjust(1);
        if (intent.Confirm) _worldPanel?.Confirm();
    }
    private void ProcessWorld(double delta)
    {
        if (_exiting || !_worldInitialized) return;
        if (_worldTestTime.HasValue && (SelfTestBootstrap || _nativeSmoke)) return;
        WorldObservation(() => ProcessWorldObserved(delta));
    }
    private void ProcessWorldObserved(double delta)
    {
        _worldAdapter.PaletteOpen = PaletteOpen;
        if (!double.IsFinite(delta) || delta < 0) { QuarantineWorld(); AdvanceWorldGrid(default); return; }
        if (WorldUiFocus != _worldUiFocused) { _worldUiFocused = WorldUiFocus; QuarantineWorld(); }
        var complete = _worldFocused;
        var controllers = new List<RawInputFrame>();
        if (complete)
        {
            _worldAdapter.RefreshKeyboardLevels(Input.IsKeyPressed);
            foreach (var id in Input.GetConnectedJoypads())
            {
                var device = (int)id;
                if (!_worldAdapter.RefreshControllerLevels(device, axis => Input.GetJoyAxis(device, axis), button => Input.IsJoyButtonPressed(device, button), worldZoom: true))
                { complete = false; break; }
                controllers.Add(_worldAdapter.SampleController(device, true)!);
            }
        }
        ApplyWorldObservedFrame(delta, complete, _worldAdapter.SampleKeyboard(_worldFocused), controllers, "LIVE FRAME LEVELS (not physical usability proof)");
    }
    /// <summary>Production aggregation seam. Tests provide complete simulated levels,
    /// not cached neutral; ordinary input always comes from the pinned OS APIs above.</summary>
    private void ApplyWorldObservedFrame(double delta, bool complete, RawInputFrame keyboard, IReadOnlyList<RawInputFrame> controllers, string provenance)
    {
        // Admit completeness before timing or UI reduction. Cached/partial input
        // must not leave Walk, drag, or Manual-return eligibility latched.
        var observedActive = _worldReducer.ActiveDevice;
        static bool Valid(RawInputFrame raw) => raw.Focused && raw.Connected
            && double.IsFinite(raw.LeftStick.X) && double.IsFinite(raw.LeftStick.Y)
            && double.IsFinite(raw.RightStick.X) && double.IsFinite(raw.RightStick.Y)
            && double.IsFinite(raw.KeyboardActor.X) && double.IsFinite(raw.KeyboardActor.Y)
            && double.IsFinite(raw.KeyboardCamera.X) && double.IsFinite(raw.KeyboardCamera.Y)
            && InputStateReducer.ValidTrigger(raw.LeftTrigger) && InputStateReducer.ValidTrigger(raw.RightTrigger);
        if (!complete || !_worldFocused || !double.IsFinite(delta) || delta < 0 || !Valid(keyboard)
            || controllers.Any(raw => !Valid(raw))
            || (observedActive is int device && !controllers.Any(raw => raw.DeviceId == device)))
        { QuarantineWorld(); AdvanceWorldGrid(default); RefreshReadyStatusPerFrame(delta); return; }
        keyboard = WorldKeyboardRouting(keyboard);
        var context = WorldContext(delta, true);
        var priorActive = _worldReducer.ActiveDevice;
        // Timing belongs to exactly one selected sample per frame, never every
        // connected device plus the keyboard.
        var keyboardIntent = WithOriginalScroll(_worldReducer.Reduce(keyboard, context with { AdvanceTime = priorActive is null }, provenance), keyboard, context);
        var intents = new List<IntentFrame> { keyboardIntent };
        foreach (var raw in controllers) intents.Add(_worldReducer.Reduce(raw, context with { AdvanceTime = raw.DeviceId == priorActive }, provenance));
        _worldReducer.EndFrame();
        if (priorActive != _worldReducer.ActiveDevice) QuarantineWorld();
        for (var i = 0; i < intents.Count; i++)
            HandleWorldPulse(intents[i], i == 0 ? _worldReducer.ActiveDevice is null : controllers[i - 1].DeviceId == _worldReducer.ActiveDevice);
        var active = _worldReducer.ActiveDevice;
        complete &= active is null || controllers.Any(raw => raw.DeviceId == active);
        var neutral = !InputStateReducer.HasAnyLevel(keyboard, context)
            && controllers.Where(raw => raw.DeviceId == active).All(raw => !InputStateReducer.HasAnyLevel(raw, context)) && !_dragging;
        var wasOpen = _worldGate.IsOpen;
        _worldGate.ObserveCompleteFrame(_worldFocused, WorldModal || WorldUiFocus || _worldLifecycleSession is not null, complete, neutral);
        if (!wasOpen || !_worldGate.IsOpen || _readyActorPlan is null || !IsWorldMode(_readyMode))
        {
            AdvanceWorldGrid(default);
            // ARC-DEV-200 (SA44): the view scrolls in the dialog's mode, which the hero's input gate is closed for. Only a modal window closes the
            // gate here for this purpose: a gate waiting for a neutral frame, a lifecycle session or a transfer keeps the camera still.
            if (context.ScrollUnderModal && _worldLifecycleSession is null && !AreaTransferPending && _readyActorPlan is not null && IsWorldMode(_readyMode)) ScrollGameViewUnderWindow(intents);
            SettleWorldZoom(delta, false); RefreshReadyStatusPerFrame(delta); return;
        }
        var actor = intents.Aggregate(default(Vec2), (sum, intent) => sum + intent.Movement);
        if (actor.Length > _worldSettings.ActorSensitivity) actor = actor.Normalized() * _worldSettings.ActorSensitivity;
        var pan = intents.Aggregate(default(Vec2), (sum, intent) => sum + intent.CameraPan);
        var actorActive = intents.Any(intent => intent.ActorActive);
        var cameraActive = intents.Any(intent => intent.CameraActive);
        if (!double.IsFinite(pan.X) || !double.IsFinite(pan.Y)) { QuarantineWorld(); return; }
        try
        {
            AdvanceWorldGrid(actor);
            var triggerHeld = controllers.Where(raw => raw.DeviceId == active)
                .Any(raw => InputStateReducer.EffectiveTrigger(raw.LeftTrigger) > 0 || InputStateReducer.EffectiveTrigger(raw.RightTrigger) > 0);
            // Zoom-only is neither pan nor a neutral opportunity to arm Manual
            // return. Actor/pan behavior otherwise keeps the existing contract.
            if (actorActive || cameraActive || !triggerHeld)
                _camera!.UpdateWorld(WorldAnchor, actor, pan, actorActive, cameraActive, WorldViewport);
            var drive = intents.Sum(intent => intent.ZoomDrive);
            var zoom = WorldZoomPolicy.Advance(_camera!.State.Zoom, drive, delta);
            if (zoom != _camera.State.Zoom) ApplyPreferredWorldZoom(zoom, true);
            SettleWorldZoom(delta, drive != 0);
            RefreshReadyStatusPerFrame(delta);
        }
        catch (TerrainViewerException exception) { PublishFailure(_arguments?.Mode ?? _readyMode, exception.Error); }
        catch (OverflowException) { PublishFailure(_arguments?.Mode ?? _readyMode, new(TerrainViewerErrorCode.ActorPlan, Stage: TerrainViewerFailureStage.ActorPlan)); }
    }
    /// <summary>ARC-DEV-200: the camera pan of a frame whose hero gate is closed (a dialog is up). The pan is the reducer's camera vector
    /// (the arrow keys and the right stick); the hero takes no part in it.</summary>
    private void ScrollGameViewUnderWindow(IReadOnlyList<IntentFrame> intents)
    {
        var pan = intents.Aggregate(default(Vec2), (sum, intent) => sum + intent.CameraPan);
        var cameraActive = intents.Any(intent => intent.CameraActive);
        if (!cameraActive || !double.IsFinite(pan.X) || !double.IsFinite(pan.Y) || _camera is null) return;
        try { _camera.UpdateWorld(WorldAnchor, default, pan, false, true, WorldViewport); }
        catch (TerrainViewerException exception) { PublishFailure(_arguments?.Mode ?? _readyMode, exception.Error); }
    }
    // ARC-DEV-380: set by a scene publication; the next PublishWorldSelection is timed.
    private bool _perfFirstSelection;
    private void PublishWorldSelection()
    {
        // Child canvases have already left the tree when the parent exits.
        // Exit quarantine disposes input/runtime, never republishes a scene.
        if (_exiting) return;
        if (_readyActorPlan is null || (_readyArea is null && _readyDiagnosticPlan?.IsWorldDiagnostic != true)) return;
        if (_worldActor.Session is null) return;
        var input = _worldActor.Placement();
        var actor = _readyActorPlan.Select(input);
        if (_readyArea is not null)
        {
            _readyActorInput = input; _readyActorPlan = actor; _readyActor = actor.Current;
            // ARC-DEV-380: the first selection after a scene publication is timed (steps print as selection.NAME).
            using var lap = _perfFirstSelection && PerfProbe.Enabled ? PerfProbe.Lap("selection") : null;
            _perfFirstSelection = false;
            lap?.Step("ambient");
            FollowAreaAmbient(); lap?.Step("walls"); FollowAreaWalls(); lap?.Step("roofs"); FollowAreaRoofs();
            // ARC-COMBAT-005: fallen bodies move like companions, from the frame repository with their poses.
            lap?.Step("combat");
            SyncAreaCombatBodies();
            // ARC-RENDER-033: destroyed ART and items lying away from a T0 body.
            lap?.Step("ground");
            SyncAreaGroundBodies();
            // ARC-WORLD-051: portals off their T0 frame are drawn like moved bodies.
            lap?.Step("portals");
            FollowAreaPortals();
            lap?.Step("moving");
            SyncTraitBodies();
            SyncAreaBodyStyles();
            _canvas!.SetAreaMovingBodies(AreaMovingCommands()); _canvas.SetAreaAbsent(_areaCompanions?.Absent);
            lap?.Step("canvas");
            _canvas.PublishArea(_readyArea.Plan, AreaFrames(), actor, input, _cache); return;
        }
        var plan = _readyDiagnosticPlan!.WithWorldActor(actor);
        _readyActorInput = input; _readyActorPlan = actor; _readyActor = actor.Current; _readyDiagnosticPlan = plan;
        _canvas!.Publish(plan, _cache);
    }
    private void AdvanceWorldGrid(Vec2 actor)
    {
        if (_worldActor.Session is not { } session || _readyActorPlan is null || !IsWorldMode(_readyMode)) return;
        if (ProcessAreaTransition()) return;
        // GR19 (ARC-DEV-078): a loaded round's load rule before anything this frame can charge or end a subturn.
        ResumeLoadedRound();
        ProcessTalkAfterRoute();
        ProcessUsePending();
        ProcessPickPocketPending();
        ProcessAreaAttack();
        ProcessHeroThrow();
        // ARC-WORLD-092: the NPCs' goal runs step after the hero's attack.
        ProcessAreaAi();
        // ARC-COMBAT-016: projectiles fly and strike after the attack's frame.
        ProcessAreaProjectiles();
        ProcessAreaSpells();
        // ARC-MAGIC-018: spell missiles fly after the casting goal's release frame.
        ProcessSpellMissiles();
        ProcessAreaEyeCandy();
        // ARC-DEV-352: the eye candies' callbacks call their spell runs back.
        ProcessEyeCandyCallbacks();
        // ARC-RENDER-040: the NPCs' dying and fall animations step their frames.
        ProcessLyingAnimations();
        // ARC-RENDER-035: animated scenery steps its frames.
        ProcessAreaSceneryAnimation();
        // ARC-COMBAT-020: the sector entries and the request for the turn-based event. MIG12 S8: the NPCs' subturns pass and the hero's completion
        // check run when that real-time event falls due, at the head of the next frame (the frame step), so before the hero moves as well.
        ProcessTurnBasedRound();
        // ARC-COMBAT-021: the hero falls, lies or gets up before its input is read.
        ProcessHeroKnockdown();
        // ARC-DEV-145: the hero's rotate goal (53) steps on the world clock.
        ProcessHeroTurn();
        var time = WorldTime;
        // A queued map transfer holds the actor until it applies (ARC-WORLD-009): the stick starts no step meanwhile.
        // A dialog opened just above (walk-to-talk arrival) is modal: this frame's stick starts no step either.
        // ARC-COMBAT-021: a hero lying down (falling, unconscious, getting up) or dead does not walk.
        var direction = _worldResetPending || _worldReloadPending || AreaTransferPending || WorldModal || _spellGoal is not null || HeroLyingDown ? null
            : DiagnosticGridActor.Resolve(actor, _worldSettings.InvertActorX, _worldSettings.InvertActorY);
        // ARC-DEV-147: the hero's stick is a held walk of the goal table (ApplyStickWalk), whose steps the route follower takes; the grid actor takes no stick intent, only the route's.
        if (StickWalksOnTable && session is AreaGridSession stickGrid) { ApplyStickWalk(stickGrid, direction, time); direction = null; }
        else
        {
            // The stick takes over at once; the click route never fights it.
            if (direction is null) _stickHeld = false;
            if (direction is not null) { SetStickGait(); _stickHeld = true; StopWorldRoute("ROUTE_STOPPED_BY_STICK"); CancelAreaUseApproach("the stick took over"); CancelTalkApproach("the stick took over"); CancelInventoryApproach("the stick took over"); CancelAreaAttack("the stick took over"); }
        }
        _worldGridInput.Observe(session, time, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral);
        if (_worldGridInput.CompleteFrontier is not { } frontier) return;
        var batches = _worldGridInput.CompleteThrough(frontier);
        var route = WorldRoute(session);
        GridAdvance Run(SimulationTime until, ReadOnlySpan<InputBatch> input)
        {
            var advance = _worldActor.Advance(until, input, selection => _readyActorPlan.Select(selection));
            if (advance.Movement.Status != MovementStatus.Completed)
                throw new InvalidOperationException($"GRID_RUNTIME_{advance.Movement.Status}_{advance.Movement.Failure?.Kind}");
            PublishWorldAdvance(session, advance);
            return advance;
        }
        // A following route also waits while a stick receipt is still open past the frontier
        // (a release observed after a click): deciding at an earlier step end would start a
        // step that receipt then cancels.
        // ARC-WORLD-016: only a step the route itself drove, in a route that was following, is retried after a denial.
        var routeDrove = false;
        _worldActorDenied = false;
        if (route is null || batches.Length > 0 || (route.Status == RouteFollowStatus.Following && _worldGridInput.HasPending))
        {
            // Observed stick/neutral receipts close first; the follower only watches them.
            var advance = Run(frontier, batches);
            route?.Observe(advance.Movement);
        }
        else
        {
            // No pending receipt: the route drives to the same frontier at exact step ends.
            routeDrove = route.Status == RouteFollowStatus.Following;
            var drive = route.Drive(session.State, frontier, (until, input) => Run(until, input.ToArray()).Movement);
            if (!drive.Completed) throw new InvalidOperationException("GRID_" + drive.FailureCode);
        }
        _worldGridInput.PublishedThrough(frontier);
        RetryActorDenial(routeDrove ? route : null, frontier);
        if (route is not null) _worldRouteDiagnostic = DescribeWorldRoute(route);
        DriveAreaCompanions(session, frontier);
        PublishWorldSelection();
        if (ReferenceEquals(_worldLifecycleSession, session) && session.State.Phase == MovementPhase.Settled)
        {
            if (_worldReloadPending && _arguments is not null)
            {
                var paletteReload = _worldPaletteReloadRequested;
                RetireWorldLifecycle();
                _lastLoad = StartLoadAsync(_arguments, false);
                _worldPaletteReloadGeneration = paletteReload ? _generation : null;
            }
            else
            {
                if (_worldReloadPending) throw new InvalidOperationException("GRID_RELOAD_ARGUMENTS_REQUIRED");
                RetireWorldLifecycle();
                if (session is AreaGridSession area) { _worldActor.Configure(area.Snapshot, time, chart: area.Chart); BindAreaCompanions(); ResetAreaRoofs(); }
                else _worldActor.Configure(((DiagnosticGridSession)session).Snapshot, time);
                _worldGridInput = new(); _worldGate.Close();
                WriteHeroLocation(_worldActor.Session!);
                PublishWorldSelection(); _camera?.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
                _worldPanel?.SetActionStatus("Reset scene done: actor at spawn, Follow; preferences kept.");
                BindWorldPaletteSummary();
                GD.Print("ARC-WORLD-003 lifecycle=explicit-settled-reset");
            }
        }
    }
    /// <summary>ARC-WORLD-016: the planner sees a companion where it stood when the route was planned; when it then steps
    /// into the route, the denied goal is planned again around it (at most <see cref="ActorDenialRetries"/> times per goal).
    /// Since ARC-WORLD-074 a companion refuses the PC's step only in combat mode.
    /// <paramref name="route"/> is null unless this frame's advance was driven by a following route, so a stick bump into
    /// a companion never revives an earlier, finished route.</summary>
    private void RetryActorDenial(RouteFollower? route, SimulationTime frontier)
    {
        var denied = _worldActorDenied; _worldActorDenied = false;
        if (!denied || route is not { Status: RouteFollowStatus.Stopped, StopReason: "ROUTE_STEP_DENIED", Target: { } goal }) return;
        var count = _worldActorRetries.Goal == goal ? _worldActorRetries.Count : 0;
        if (count >= ActorDenialRetries) return;
        _worldActorRetries = (goal, count + 1);
        route.SetTarget(goal, frontier, route.Range);
        GD.Print($"ARC-WORLD-016 route replanned around a companion target={goal} retry={count + 1}");
    }
    private Action<GridAdvance>? _worldAdvanceObserverForSelfTest;
    private void PublishWorldAdvance(IGridSession session, GridAdvance advance)
    {
        _worldAdvanceObserverForSelfTest?.Invoke(advance);
        // ARC-DEV-150: the committed cell is the hero's location before anything looks at the cells it entered.
        WriteHeroLocation(session);
        // ARC-DEV-287: the view stays put after a knockback until the hero walks again (a step started in this advance).
        if (_camera is { Held: true } && advance.Movement.Events.Any(e => e.Kind == MovementEventKind.StepStarted)) _camera.ReleaseWorld();
        ObserveCombatRunSteps(advance);
        NoteMapEntries(advance);
        NoteTrapSteps(session, advance);
        NoteMapPortals(advance);
        foreach (var evidence in advance.AreaEvidence)
        {
            _worldActorDenied |= evidence.Live?.ReasonCode is AreaOccupancyRules.DestinationActor or AreaOccupancyRules.ShoulderActor;
            var assessment = evidence.Assessment;
            _worldLastStepAttempt = $"({evidence.Key.From.X},{evidence.Key.From.Y})->({evidence.Key.To.X},{evidence.Key.To.Y}) {evidence.Key.Direction} {assessment.Coverage}/{assessment.Disposition} {assessment.ReasonCode} epoch={session.EvidenceEpoch}";
            _worldGridDiagnostic = $"{assessment.Coverage}/{assessment.Disposition} {assessment.ReasonCode}{(evidence.Live is { } live ? $" live {live.Coverage}/{live.Disposition} {live.ReasonCode}" : "")} objects={assessment.Objects.Count} gaps={assessment.Gaps.Count} epoch={session.EvidenceEpoch}";
            // Numeric, path-free production diagnostic evidence; no archive bytes/pixels.
            GD.Print("ARC-WORLD-003 evidence=" + System.Text.Json.JsonSerializer.Serialize(new {
                evidence.Key, coverage=assessment.Coverage.ToString(), disposition=assessment.Disposition?.ToString(),
                assessment.ReasonCode, objects=assessment.Objects.Count, gaps=assessment.Gaps.Count,
                terrain=assessment.DestinationTerrain?.ReasonCode, evidenceSha256=assessment.EvidenceSha256
            }));
        }
        foreach (var evidence in advance.Evidence)
        {
            var assessment = evidence.Assessment;
            _worldLastStepAttempt = $"({evidence.Key.From.X},{evidence.Key.From.Y})->({evidence.Key.To.X},{evidence.Key.To.Y}) {evidence.Key.Direction} {assessment.Coverage}/{assessment.Disposition} {assessment.ReasonCode} epoch={session.EvidenceEpoch}";
            _worldGridDiagnostic = $"{assessment.Coverage}/{assessment.Disposition} {assessment.ReasonCode} records={assessment.RecordAssessments.Count} gaps={assessment.Gaps.Count} epoch={session.EvidenceEpoch}";
            // Numeric, path-free production diagnostic evidence; no archive bytes/pixels.
            GD.Print("ARC-WORLD-003 evidence=" + System.Text.Json.JsonSerializer.Serialize(new {
                evidence.Key, coverage=assessment.Coverage.ToString(), disposition=assessment.Disposition?.ToString(),
                assessment.ReasonCode, records=assessment.RecordAssessments.Count, gaps=assessment.Gaps.Count,
                selectedEvidenceSha256=assessment.StepEvidence.SelectedEvidenceSha256
            }));
        }
        foreach (var item in advance.Movement.Events)
            GD.Print("ARC-WORLD-003 event=" + System.Text.Json.JsonSerializer.Serialize(item));
    }
    private RouteFollower? WorldRoute(IGridSession session)
    {
        if (session is not AreaGridSession area || _readyMode != TerrainViewerMode.Area) return null;
        if (!ReferenceEquals(_worldRouteSession, session))
        {
            // ARC-WORLD-042: the player's click route follows a corridor to a goal beyond the local planner's reach; ARC-WORLD-072:
            // not under the Original rule mode, read from the session's rules when a click sets the goal (the PC does not move).
            // ARC-WORLD-073: the original's walk (its request, budget, segments and the PC's straight-line retry); outside combat mode its
            // plan passes living critters in both rule modes (the owner's decision 2026-10-21), and under Original the walk ends before
            // a segment's last step onto one (LR5). The PC's steps pass them too outside combat mode (ARC-WORLD-074); a step refused
            // for another reason (in combat, a door that closed) plans around its cell.
            _worldRoute = new RouteFollower(RouteSourceFor(area),
                corridorLimits: new RouteCorridorLimits(), corridorAllowed: () => _areaSession?.Rules.Navigation.FarClickDoesNotMove != true, straightRetry: true,
                segmentEndCritterEndsWalk: () => _areaSession?.Rules.Navigation.SegmentEndCritterEndsWalk == true, replanOnDenial: true)
            {
                // ARC-DEV-302 (GT148, D8): a held hero's route (a far goal the follower walks itself) asks for no step while the hold lasts.
                Held = HeroStepsHeld,
            };
            _worldRouteSession = session; _worldRouteDiagnostic = "no click yet"; _worldRouteClick = "none";
        }
        return _worldRoute;
    }

    /// <summary>The same live occupancy, portals, critter rule and death state for route planning and object approach selection.</summary>
    private AreaRouteSource RouteSourceFor(AreaGridSession area) => new(area.Snapshot, area.Chart, area.Occupancy,
        () => area.Portals?.Invoke(), () => !HeroInCombatMode, AreaCritterDeadNow);

    // ARC-DEV-323: set while the steal window's close quarantines the input gate: the route stops, the hero's goal runs (a walking theft) are not interrupted.
    private bool _quarantineKeepsRuns;

    /// <summary>
    /// Stops the hero's walk: the route and, ARC-DEV-149, the walk's run on the table (the level-3 interrupt: a route stop is "another action replaced the walk", and the table's walk would otherwise
    /// go on stepping the hero). The raw route stop (<see cref="StopWorldRouteOnly"/>) is the table's own.
    /// </summary>
    private void StopWorldRoute(string reason)
    {
        // ARC-DEV-189: not while the hero's knock-down or get-up plays: a level-3 interrupt would end that run (priority 3), and the hero lying takes no ordinary action that replaces it.
        if (!_exiting && !_quarantineKeepsRuns && ShownHeroRunsHost?.DownRun is not { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.KnockDown or Arcanum.Simulation.Goals.GoalType.GetUp }) HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, reason);
        StopWorldRouteOnly(reason);
    }

    private void StopWorldRouteOnly(string reason)
    {
        if (_exiting || _worldRoute is not { Status: RouteFollowStatus.Following } route
            || !ReferenceEquals(_worldRouteSession, _worldActor.Session)) return;
        route.Stop(WorldTime, reason);
        _worldRouteDiagnostic = DescribeWorldRoute(route);
        GD.Print($"ARC-WORLD-007 route stop reason={reason} target={route.Target}");
    }
    private static string DescribeWorldRoute(RouteFollower route) =>
        $"{route.Status} target={route.Target?.ToString() ?? "none"}{(route.StopReason is { } reason ? " " + reason : "")}"
        + $" plan={route.LastPlan?.ReasonCode ?? "none"} segments={route.PlannedSegments}";
    /// <summary>ARC-WORLD-007: an unhandled LMB press on the ground sets the route goal (KM-03). Native
    /// GUI (HUD, palette) handles its own clicks first; a left release has no meaning, a right release is ARC-COMBAT-022's.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsWorld || !_worldInitialized || _exiting || BugReportOpen) return;
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            WorldObservation(() => WorldRouteClick(click.Position, click.CtrlPressed, click.AltPressed, click.ShiftPressed));
        // ARC-WORLD-020: macOS reports a physical Ctrl+left click as a right button press with Ctrl held; the world's right
        // press has no action of its own, so it is the original's Ctrl+LMB (its release is taken with it).
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true, CtrlPressed: true } ctrlClick)
        {
            _rightPressTaken = true;
            WorldObservation(() => WorldRouteClick(ctrlClick.Position, true));
        }
        // ARC-MAGIC-015: a right press closes the spell's targeting cursor (0x57BFF0).
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && (_spellTargeting is not null || _itemAiming is not null || DisarmArmed))
        {
            _rightPressTaken = true;
            // ARC-DEV-365: the same right press cancels the item cursor (IA5, IA6).
            WorldObservation(() => { CancelSpellTargeting("right click"); CancelArmedLockpicks("right click"); CancelDisarm("right click"); RefreshAreaStatus(); });
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) _rightPressTaken = false;
        // ARC-COMBAT-022: the right release leaves combat mode or concealment, unless its press was taken above.
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } rightRelease)
        {
            if (_rightPressTaken || _rightPressPoppedWindow) { _rightPressTaken = false; _rightPressPoppedWindow = false; }
            // ARC-DEV-280 (0x56AC47): a mouse release that no menu button takes closes the follower order menu (the original's left or right release).
            else if (_followerPortraits is { MenuShown: true } openMenu) openMenu.CloseMenu();
            else WorldObservation(() => { WorldRightRelease(rightRelease.Position); GameCursorRightRelease(); });
        }
    }
    /// <summary>The guards a world left press passes (ARC-UI-005 hover shows the same answer).</summary>
    private bool WorldClickInputOpen => _worldFocused && GameViewModeOpen && !WorldUiFocus && _readyActorPlan is not null
        && _worldGate.IsOpen && _worldLifecycleSession is null;
    private void WorldRouteClick(Vector2 screen, bool ctrl = false, bool alt = false, bool shift = false)
    {
        if (_worldActor.Session is not { } session || WorldRoute(session) is not { } route || _canvas is null) return;
        if (!WorldClickInputOpen)
        {
            _worldRouteClick = "ignored (input quarantined/modal/lifecycle)";
            RefreshReadyStatus(); return;
        }
        if (AreaTransferPending)
        {
            _worldRouteClick = "ignored (map transfer pending)";
            RefreshReadyStatus(); return;
        }
        // ARC-WORLD-046: a press outside the open follower order menu only closes it; after a portrait's walk order the press picks
        // its tile (ARC-WORLD-117: after its attack order, the object under it).
        if (_followerPortraits is { MenuShown: true } portraits)
        {
            portraits.CloseMenu();
            _worldRouteClick = "closed the follower order menu";
            RefreshReadyStatus(); return;
        }
        // ARC-COMBAT-021: the original's world press handler (0x54ED30) does nothing for a PC that is not active (0x45D790); a hero
        // lying down (falling, unconscious, getting up) or dead takes no press.
        if (HeroLyingDown)
        {
            // ARC-UI-063: a held item goes back when the PC is not active (0x575770, TT2).
            PutBackGameCursorItem("the hero cannot act");
            _worldRouteClick = "ignored (the hero is down)";
            RefreshReadyStatus(); return;
        }
        // ARC-COMBAT-020: in a turn-based round the world takes the hero's commands only on its own subturn (the original's world
        // handler ignores them during another's).
        if (_areaSession?.QueryCombatRound() is { } round && CombatModeActive && !round.Actor.IsHero)
        {
            _worldRouteClick = "ignored (another participant's turn)"; _turnBasedNotice = "the press waits for the hero's turn";
            RefreshReadyStatus(); return;
        }
        // ARC-UI-063: an item on the game cursor takes the press: it is thrown, or goes back for a hero that cannot act (TT2, TT6).
        if (_gameCursorItem is not null) { _worldRouteClick = "throw"; GameCursorPress(screen); return; }
        // ARC-MAGIC-015: the open targeting cursor takes every left press (the original's interface mode 1).
        if (_spellTargeting is not null) { SpellTargetClick(screen); return; }
        // ARC-MAGIC-015: the casting goal holds the hero until it ends (the original queues other goals under it, not ready here).
        if (_spellGoal is not null) { _spellNotice = "casting: the press waits for the cast to end (ignored)"; RefreshReadyStatus(); return; }
        if (TryPendingOrder(screen)) return;
        // ARC-WORLD-051: any click replaces a walk to a door (a press on a door sets a new one).
        CancelPortalApproach("another click replaced it", keepArmed: true);
        // ARC-DEV-365: the open item cursor takes the press (the original's interface mode 16, IA6).
        if (_itemAiming is not null) { CancelAreaAttack("the item cursor took the press"); ItemAimClick(screen, ctrl, shift); return; }
        // ARC-SKILLS-006: armed lockpicks take this press: a door or window has its lock picked, anything else cancels the pick.
        if (_lockpickArmed is not null)
        {
            CancelAreaAttack("armed lockpicks took the press");
            if (TryAreaPortalUse(screen)) SetObjectApproachGait();
            else
            {
                CancelTalkApproach("a press cancelled the pick"); CancelAreaUseApproach("a press cancelled the pick"); CancelInventoryApproach("a press cancelled the pick");
                RefreshReadyStatus();
            }
            return;
        }
        // ARC-SKILLS-010: an armed pick pocket takes this press: an NPC has its pocket picked, anything else cancels the skill.
        if (PickPocketArmed)
        {
            CancelAreaAttack("an armed pick pocket took the press");
            PickPocketPress(screen);
            return;
        }
        // ARC-SKILLS-1156: an armed disarm traps skill takes this press: a known trap runs the skill, anything else cancels it.
        if (DisarmArmed)
        {
            CancelAreaAttack("an armed disarm traps skill took the press");
            DisarmPress(screen);
            return;
        }
        // ARC-COMBAT-005: in combat mode a press on an NPC attacks it (or walks within reach first) instead of talking.
        if (TryAreaAttack(screen, alt, shift)) return;
        CancelAreaAttack("another click replaced it");
        // ARC-SCRIPT-005: a press on a dialog NPC talks (or walks within reach first), under the same guards.
        if (TryAreaTalk(screen)) { SetObjectApproachGait(); return; }
        // ARC-WORLD-009: a press on a cave trigger sprite is a use action, under the same guards as a ground click.
        if (TryAreaUse(screen)) { SetObjectApproachGait(); return; }
        // ARC-WORLD-051: a press on a door or window uses it (walking within use range first).
        if (TryAreaPortalUse(screen, alt)) { SetObjectApproachGait(); return; }
        // ARC-UI-004: a press on an item on the ground picks it up, on a dead NPC loots it (walking within reach first).
        if (TryAreaInventoryClick(screen, alt)) { SetObjectApproachGait(); return; }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return;
        // ARC-DEV-145: the click is a WalkRequest; the host decides the gait (0x4304C0) and the port replaces the approach intents and the route.
        var accepted = HeroRequests.Apply(new WalkRequest(cell, ctrl)).Outcome == HeroRequestOutcome.Done;
        _worldRouteDiagnostic = DescribeWorldRoute(route);
        _worldRouteClick = $"{cell.X},{cell.Y} {(accepted ? "goal set" : "same goal kept")}";
        GD.Print($"ARC-WORLD-007 route click cell={cell} accepted={accepted}");
        RefreshReadyStatus();
    }
    private static WorldKeyModifiers KeyModifiers(InputEventKey key)
        => (key.CtrlPressed ? WorldKeyModifiers.Ctrl : 0) | (key.ShiftPressed ? WorldKeyModifiers.Shift : 0)
            | (key.AltPressed ? WorldKeyModifiers.Alt : 0) | (key.MetaPressed ? WorldKeyModifiers.Meta : 0);

    /// <summary>ARC-INPUT-004: an open window's own letter (L, C, I, T, W) does not close it under Ctrl, Alt or Command.</summary>
    private static bool CharacterKeySilenced(InputEventKey key, Key routed)
        => OriginalWorldKeyboard.CharacterKeySilenced(GodotInputAdapter.ToWorldKey(routed), KeyModifiers(key));

    private void InputWorld(InputEvent input)
    {
        if (_worldInitialized && !_exiting) WorldObservation(() => InputWorldObserved(input));
    }
    private void InputWorldObserved(InputEvent input)
    {
        NoteWorldPointer(input);
        if (_worldInputTraceForSelfTest is not null) TraceWorldInput($"Callback:entry type={input.GetClass()}" + (input is InputEventKey tracedKey ? $" key={tracedKey.Keycode} pressed={tracedKey.Pressed} echo={tracedKey.Echo}" : ""));
        // ARC-DEV-051: the bug report comes first (Ctrl+B over any window, the death screen included; the open report owns every event).
        if (RouteBugReportInput(input)) return;
        // ARC-UI-026: the death screen owns every key and mouse button while it covers the game.
        if (HandleHeroDeathInput(input)) { GetViewport().SetInputAsHandled(); return; }
        // ARC-DEV-201: a right press on a bar closes the window the bar handler pops; the release that follows is part of that press.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rightPress) _rightPressPoppedWindow = HandleRightPressOverWindow(rightPress.Position, rightPress.CtrlPressed);
        if (input is InputEventKey modifierKey) { TrackAttackModifiers(modifierKey); TrackSpellModifiers(modifierKey); }
        // ARC-INPUT-003: the reserved-key notice in the sticky HUD header lasts until the next key press.
        if (input is InputEventKey { Pressed: true, Echo: false } pressedKey)
        {
            _hud?.SetNotice(null);
            // A bare modifier is not a key press that cancels a pending walk order (ARC-WORLD-046).
            static bool Modifier(Key key) => key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.Capslock;
            // ARC-DEV-111 (SA51): Escape is no target mode's key (it opens the menu, which leaves the mode as it is).
            if (!Modifier(pressedKey.Keycode) && !Modifier(pressedKey.PhysicalKeycode) && pressedKey.Keycode != Key.Escape && pressedKey.PhysicalKeycode != Key.Escape
                && !MainMenuOpen && !SaveListOpen)
            {
                CancelPendingOrder("a key was pressed");
                CancelArmedLockpicks("a key was pressed");
                _followerPortraits?.CloseMenu();
                // Disarms an F1–F6 whose release was lost (a modifier held, a window that took it); a bare F1–F6 in an open world
                // re-arms in RouteOrderKey below.
                _orderKeyArmed = null;
            }
        }
        // ARC-DEV-111 (MIG16 S4): the menu and the save list sit on top of any window under them (the in-play menu opens over a window), so they
        // take the keys first.
        // ARC-UI-011: the open main menu owns every key (a letter chooses its button, Escape goes back).
        if (MainMenuOpen && input is InputEventKey mainMenuKey)
        {
            HandleMainMenuKey(mainMenuKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-010: the open save window owns every key except the typing of a description into its field.
        if (SaveListOpen && input is InputEventKey saveListKey)
        {
            if (HandleSaveListKey(saveListKey)) GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-106: an open note text entry of the world map takes Enter and Escape before the global keys (Escape would open the menu over the entry); the typed keys
        // reach its field.
        if (WorldMapOpen && _worldMapPanel is { EditingNote: true } && input is InputEventKey entryKey)
        {
            if (HandleWorldMapKey(entryKey)) GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-DEV-111 (SA45, SA40, SA44): Escape, the other windows' keys and the number row are global keys of the original's main loop and
        // bar handler, which no open window handles itself.
        if (input is InputEventKey interfaceKey && HandleGlobalKeyOverInterface(interfaceKey))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (ConversationOpen && input is InputEventKey conversationKey && HandleConversationKey(conversationKey))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-004: the open inventory window owns every key (I and Escape close it).
        if (InventoryOpen && input is InputEventKey inventoryKey)
        {
            HandleInventoryKey(inventoryKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-007: the open character sheet owns every key (C and Escape close it).
        if (SheetOpen && input is InputEventKey sheetKey)
        {
            HandleSheetKey(sheetKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-008: the open logbook owns every key (L and Escape close it, Tab switches the tab).
        if (LogbookOpen && input is InputEventKey logbookKey)
        {
            HandleLogbookKey(logbookKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-TECHNOLOGY-005: the open schematic window owns every key (T and Escape close it, Tab assembles).
        if (SchematicsOpen && input is InputEventKey schematicsKey)
        {
            HandleSchematicsKey(schematicsKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-050: the open barter window owns every key (Escape closes it).
        if (BarterOpen && input is InputEventKey barterKey)
        {
            HandleBarterKey(barterKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-049: the open reading window owns every key (Escape closes it, Left/Right turn a book's pages).
        if (ReadingOpen && input is InputEventKey readingKey)
        {
            HandleReadingKey(readingKey);
            GetViewport().SetInputAsHandled();
            return;
        }
        // ARC-UI-048: the open world map owns every key (W closes it or ends the journey, Enter travels; Escape is the menu's).
        if (WorldMapOpen && input is InputEventKey worldMapKey)
        {
            if (HandleWorldMapKey(worldMapKey)) GetViewport().SetInputAsHandled();
            return;
        }
        var wasModal = WorldModal;
        _worldAdapter.PaletteOpen = PaletteOpen;
        if (input is InputEventKey chordKey)
        {
            // Diagnostics first: a consumed chord key never reaches the shared
            // mapping, native GUI or an original-key sink (down, echo or release).
            var routedKey = chordKey.Keycode != Key.None ? chordKey.Keycode : chordKey.PhysicalKeycode;
            var modifiers = KeyModifiers(chordKey);
            var decision = _worldChord.Route(GodotInputAdapter.ToWorldKey(routedKey), chordKey.Pressed, chordKey.Echo, modifiers,
                WorldModal, _worldFocused && !WorldUiFocus);
            if (decision.Consumed)
            {
                GetViewport().SetInputAsHandled();
                if (decision.TogglePalette) ToggleWorldSettings();
                return;
            }
            // ARC-INPUT-004: Ctrl, Alt or Command with a character letter (A C F I L R S T W) is no key in the original:
            // no window opens and nothing is reported as reserved.
            // ARC-WORLD-011: F7/F8 are quick save/load while an AREA game session exists.
            if (!OriginalWorldKeyboard.CharacterKeySilenced(decision.Key, modifiers) && !RouteQuickKey(decision) && !RouteGameMenuKey(decision) && !RouteOrderKey(decision, modifiers) && !RouteInventoryKey(decision) && !RouteSheetKey(decision) && !RouteLogbookKey(decision) && !RouteSpellKey(decision) && !RouteHotkeyKey(decision, modifiers) && !RouteSchematicsKey(decision) && !RouteWorldMapKey(decision) && !RouteVisualKey(decision, modifiers) && !RouteNumLockKey(decision) && !RouteTurnBasedKey(decision) && !RouteCombatModeKey(decision) && decision.Role == WorldKeyRole.OriginalReserved && decision.Pressed && !decision.Echo && !WorldModal && !WorldUiFocus)
            {
                _worldLastReserved = $"{routedKey} = {decision.OriginalAction} (original; NOT IMPLEMENTED)";
                _hud?.SetNotice($"Reserved: {routedKey} = {decision.OriginalAction} (NOT IMPLEMENTED)");
            }
        }
        var raw = _worldAdapter.Map(input, _worldFocused);
        if (input is InputEventMouseButton)
        {
            // The shared adapter's mouse Confirm/Cancel is historical lab
            // behavior. WORLD mouse controls belong to native GUI dispatch;
            // retain real keyboard levels/provenance, never counterfeit a
            // semantic activation on the previously focused control.
            var keyboard = _worldAdapter.SampleKeyboard(_worldFocused);
            raw = raw with { Confirm = keyboard.Confirm, Cancel = keyboard.Cancel };
        }
        if (raw.DeviceId < 0) raw = WorldKeyboardRouting(raw);
        var intent = _worldReducer.Reduce(raw, WorldContext());
        if (_worldInputTraceForSelfTest is not null) TraceWorldInput($"Callback:reduced rawConfirm={raw.Confirm} focused={raw.Focused} connected={raw.Connected} accepted={intent.Accepted} confirm={intent.Confirm}");
        if (input is not InputEventMouse) HandleWorldPulse(intent, _worldReducer.ActiveDevice is null || raw.DeviceId == _worldReducer.ActiveDevice);
        CaptureWorldCallbackActor(raw, intent);
        // Semantic keyboard/controller modal routing is owned above; do not
        // also let focused native controls toggle/navigate the same event.
        var semanticKey = input is InputEventKey keyEvent &&
            (keyEvent.Keycode is Key.Escape or Key.Up or Key.Down
            || (GetViewport().GuiGetFocusOwner() is not LineEdit && keyEvent.Keycode is Key.Enter or Key.Space or Key.Backspace));
        if ((wasModal || WorldModal) && (semanticKey || input is InputEventJoypadButton or InputEventJoypadMotion))
            GetViewport().SetInputAsHandled();
        if (_worldInputTraceForSelfTest is not null) TraceWorldInput($"Callback:handled semanticKey={semanticKey} handled={GetViewport().IsInputHandled()}");
        if (!_worldFocused || WorldModal || WorldUiFocus || _readyActorPlan is null) return;
        // The world mouse route is free for original controls: LMB neither drags
        // the camera nor confirms (in crash-site-area an unhandled LMB press sets the
        // click-to-move goal, see _UnhandledInput), and wheel/+/- do not zoom. Exact zoom, Fit, Reset scene and Reload live in the palette.
        // F11 is a host window control (not an M/Q game key), not a diagnostic.
        if (input is InputEventKey key && key.Pressed && !key.Echo && GetViewport().GuiGetFocusOwner() is null && key.Keycode == Key.F11)
            GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
        RefreshReadyStatus();
    }
    private void CaptureWorldCallbackActor(RawInputFrame raw, IntentFrame reduced)
    {
        if (_worldActor.Session is not { } session) return;
        var actor = default(Vec2);
        if (_worldGate.IsOpen && _worldFocused && !WorldModal && !WorldUiFocus && _worldLifecycleSession is null)
        {
            // Reduce the other domain too; a mouse/keyboard event must not
            // release the active left stick. Timing/pulses remain callback-only.
            actor = raw.DeviceId < 0 ? reduced.Movement
                : _worldReducer.Reduce(WorldKeyboardRouting(_worldAdapter.SampleKeyboard(_worldFocused)), WorldContext()).Movement;
            var selected = _worldAdapter.SampleController(_worldReducer.ActiveDevice, _worldFocused);
            if (selected is not null)
                actor += raw.DeviceId == _worldReducer.ActiveDevice ? reduced.Movement
                    : _worldReducer.Reduce(selected, WorldContext()).Movement;
            if (actor.Length > _worldSettings.ActorSensitivity) actor = actor.Normalized() * _worldSettings.ActorSensitivity;
        }
        var direction = DiagnosticGridActor.Resolve(actor, _worldSettings.InvertActorX, _worldSettings.InvertActorY);
        // ARC-DEV-147: the hero's stick is the table's held walk, asked for once a frame (ApplyStickWalk); the grid actor takes no stick intent.
        if (StickWalksOnTable) direction = null;
        _worldGridInput.Observe(session, WorldTime, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral);
    }
    internal void WorldFrameForSelfTest(RawInputFrame keyboard, IReadOnlyList<RawInputFrame> controllers, double delta, bool complete = true, SimulationTime? observationTime = null)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        // The script clock is independent of camera delta, including zero,
        // invalid and huge zoom deltas. Exact motion tests supply literal times.
        _worldTestTime = observationTime ?? new SimulationTime((_worldTestTime ?? _worldActor.Session?.State.Time ?? WorldTime).Value + ExactScalar.Create(1, 10));
        WorldObservation(() => ApplyWorldObservedFrame(delta, complete, keyboard, controllers, "SIMULATION complete-level frame (not physical evidence)"));
    }
    private RawInputFrame WorldKeyboardRouting(RawInputFrame raw)
    {
        // Host-side enforcement of the WORLD keyboard route, independent of the
        // adapter: no keyboard actor vector, no lab Inspect/Context/Q/E/F1 pulses,
        // and semantic Confirm/Cancel/menu pulses only inside the open palette.
        raw = raw with { KeyboardActor = default, Inspect = false, Context = false, Previous = false, Next = false, Settings = false, KeyboardActorHeld = false };
        // The conversation panel is modal too, but keyboard keys are not dialog controls (ARC-SCRIPT-005).
        if (!PaletteOpen) raw = raw with { Confirm = false, Cancel = false, MenuUp = false, MenuDown = false };
        // The native numeric editor owns text, Enter, Backspace and Tab. Only
        // explicit Escape cancels here; do not reinterpret Backspace as Cancel.
        if (WorldModal && GetViewport().GuiGetFocusOwner() is LineEdit)
            return raw with { Confirm = false, Cancel = _worldAdapter.IsKeyHeld(Key.Escape), Previous = false, Next = false };
        return raw;
    }
    internal DiagnosticActorState WorldStateForSelfTest => _worldActor.State;
    internal SimulationTime WorldTimeForSelfTest => _worldTestTime ?? WorldTime;
    internal void WorldSettleForSelfTest(params RawInputFrame[] controllers)
    {
        // Bounded independent receipt observations, not camera-delta integration.
        // Live D=1/2 means eight 1/10-second receipts suffice for a fresh neutral
        // to seal, return/finish and close its settlement frontier.
        for (var i = 0; i < 8; i++) WorldFrameForSelfTest(new(-1, default), controllers, .1);
        if (_worldActor.Session?.State.Phase != MovementPhase.Settled)
            throw new InvalidOperationException("GRID_TEST_SETTLEMENT_REQUIRED");
    }
    internal void WorldZoomForSelfTest(double zoom)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        SetPreferredWorldZoom(zoom);
    }
    internal void WorldFitFollowForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        _camera!.Fit(WorldViewport);
        _camera.ResetWorld(WorldAnchor, WorldViewport, _camera.State.Zoom);
    }
    internal CameraFollowState WorldFollowForSelfTest => _camera!.FollowState;
    internal void WorldFocusForSelfTest(bool focused)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Simulation requires an explicit test launch.");
        _worldFocused = focused; QuarantineWorld();
    }
    private string WorldStatus()
    {
        var s = _worldActor.State;
        var zoomStatus = $" | World zoom {_camera!.State.Zoom:0.#####} ({(_worldFitView ? "Fit session view" : "camera ratio")}); preferred {_worldPreferredZoom:0.#####}; "
            + (_worldZoomSaveFailed ? "zoom save FAILED, session only" : _worldZoomPending ? "zoom save pending" : "zoom preference current")
            + $" | wanted center ({_camera.WantedCenter.X:0.###},{_camera.WantedCenter.Y:0.###}) bounded ({_camera.State.X:0.###},{_camera.State.Y:0.###}) bound limited {_camera.BoundLimited} | LT out / RT in";
        var profile = _worldActor.Profile;
        var binding = profile.Facing(s.Action, s.Facing);
        var runtime = _worldActor.Session?.State;
        return zoomStatus + $" | Grid committed={runtime?.CommittedCell} continuous anchor=({runtime?.Anchor.U},{runtime?.Anchor.V}) phase={runtime?.Phase} time={runtime?.Time.Value} step={runtime?.Segment?.Key.ProposedStepId} opportunity={runtime?.LastOpportunity?.OpportunityId} | {(_worldActor.Session is AreaGridSession ? "whole Crash Site core (9 sectors) at new-game T0, core edge refused" : "small diagnostic test area x4..8/y0..3 sector1452,1290")} spawn(6,1); movement may stop without visible obstacles; {(_worldActor.Session is AreaGridSession { Timing: not null } ? "step time from the hero body's ART stride by gait (ARC-WORLD-020, ARC-UI-039)" : "D=1/2s diagnostic NOT original speed")} | {_worldGridDiagnostic} | lifecycle={(_worldReloadPending ? "reload pending settled" : _worldResetPending ? "reset pending settled" : "active")} | Actor: {profile.Id} {s.Action} Frame: {_worldActor.CurrentFrame} | ART clock Idle{profile.Idle.RawFramesPerSecond}/Walk{_worldActor.AreaGaitTiming?.ArtRate(MovementGait.Walk) ?? profile.Walk.RawFramesPerSecond}{(_worldActor.Session is AreaGridSession ? $"/Run{_worldActor.AreaGaitTiming?.ArtRate(MovementGait.Run) ?? profile.Run!.RawFramesPerSecond}/Vault{profile.Vault!.RawFramesPerSecond}" : "")} | Facing: octant{s.Facing} source{binding.SourceFacing} group{binding.PhysicalGroup} flip{binding.FlipX} | Camera: {_camera!.FollowState.Mode} | neutral gate {(_worldGate.IsOpen ? "open" : "quarantined")} | Diagnostic palette: Ctrl+Shift+F9 / on-screen button / gamepad Start (Fit, Reset scene, Reload, exact zoom) | Gamepad: left stick eight screen sectors, right stick pan, LT/RT zoom | Keyboard/mouse (original route): arrows pan (fixed direction); original keys reserved, NOT IMPLEMENTED except Num Lock (always run), Ctrl+LMB (run once) and F1–F6 (follower orders while the session party is wired, ARC-WORLD-046) in crash-site-area; {(_worldActor.Session is AreaGridSession && _readyMode == TerrainViewerMode.Area ? "LMB click-to-move route " + _worldRouteDiagnostic + " (last click " + _worldRouteClick + ")" : "LMB click-to-move only in crash-site-area")}; no WASD/debug keys except H (visual Legacy/HD){(_worldLastReserved is null ? "" : " | last reserved key: " + _worldLastReserved)} | {PassabilityOverlayStatus()} | {_canvas!.HdStatusText()} | {VisualHudStatus()} |{(_worldActor.Session is AreaGridSession ? " " + GaitHudStatus() + " |" : "")} original motion/spawn/occlusion parity NOT_VERIFIED";
    }
    private void ExitWorld()
    {
        if (!_worldInitialized) return;
        // Consume this initialization before any callbacks. Retirement and tree
        // exit may both arrive; neither may disconnect another generation's
        // subscriptions or dispose the same session twice.
        _worldInitialized = false;
        try
        {
            FlushWorldZoom();
            _worldChord.Reset(); _worldAdapter.PaletteOpen = false; _worldPaletteReloadGeneration = null;
            RetirePassabilityOverlay(); _overlayPanelState = null;
            QuarantineWorld();
            GD.Print($"ARC-WORLD-003 lifecycle=disposed phase={_worldActor.Session?.State.Phase} time={_worldActor.Session?.State.Time.Value} (not CenterArrived)");
        }
        finally
        {
            _worldActor.DisposeSession(); _worldRoute = null; _worldRouteSession = null;
            RetireWorldLifecycle(); _worldGridInput = new();
            Input.JoyConnectionChanged -= WorldConnectionChanged;
            if (_worldFocusWindow is { } window)
            {
                _worldFocusWindow = null;
                if (GodotObject.IsInstanceValid(window))
                {
                    window.FocusEntered -= WorldWindowFocusEntered;
                    window.FocusExited -= WorldWindowFocusExited;
                }
            }
            if (_hud is not null) _hud.SettingsRequested -= ToggleWorldSettings;
            if (_worldPanel is { } panel)
            {
                _worldPanel = null;
                if (GodotObject.IsInstanceValid(panel))
                {
                    panel.Changed -= SaveWorldSettings; panel.CloseRequested -= ToggleWorldSettings; panel.ResetRequested -= ResetWorldSettings;
                    panel.ZoomRequested -= SetPreferredWorldZoom; panel.FitRequested -= FitWorldView;
                    panel.ResetSceneRequested -= PaletteResetScene; panel.ReloadRequested -= PaletteReload; panel.OverlayToggled -= SetPassabilityOverlay;
                    panel.VisualToggleRequested -= PaletteVisualToggle;
                    panel.AlwaysRunToggleRequested -= PaletteAlwaysRunToggle;
                    panel.SoundTestRequested -= PaletteSoundTest;
                    panel.SaveGameRequested -= PaletteSaveGame; panel.LoadGameRequested -= PaletteLoadGame;
                    panel.VolumeChanged -= PaletteVolumeChanged;
                    panel.Hide(); RemoveChild(panel); panel.QueueFree();
                }
            }
        }
    }
}
