using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// Native validation owns a detached actor, input frontier and input reducer
    /// across awaits. Neither success nor a failed pixel control may consume the
    /// live session's pending input/lifecycle or leave a hidden canvas behind.
    /// This seam is never used by ordinary launches.
    /// </summary>
    private async Task WithWorldReadbackScopeAsync(TerrainLoadResult result, Func<Task> readback)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        var actor = _worldActor; var queue = _worldGridInput; var settings = _worldSettings;
        var adapter = _worldAdapter; var reducer = _worldReducer; var gate = _worldGate;
        var clock = _worldTestTime; var observation = _worldObservationTime;
        var lifecycle = _worldLifecycleSession; var reset = _worldResetPending; var reload = _worldReloadPending;
        var scene = _readyDiagnosticPlan; var plan = _readyActorPlan; var input = _readyActorInput; var selected = _readyActor;
        var diagnostic = _worldGridDiagnostic; var focused = _worldFocused; var uiFocused = _worldUiFocused;
        var dragging = _dragging; var menuDirection = _worldMenuAnalogDirection;
        var camera = _camera!; var cameraPosition = camera.Camera.Position; var cameraZoom = camera.Camera.Zoom;
        var size = new Vector2I((int)WorldViewport.X, (int)WorldViewport.Y);
        var actorVisible = _canvas!.ActorVisibleForReadback; var staticVisible = _canvas.StaticVisibleForReadback;
        var processing = IsProcessing(); var processingInput = IsProcessingInput(); var simulatedFocus = _worldFocusSimulationForSelfTest;
        var focusOwner = GetViewport().GuiGetFocusOwner(); var modal = WorldModal;
        // Render-only catalogue checks replace detached sessions across awaits.
        // OS callbacks must not bind their queue to a previous detached session.
        // Explicit test _Input calls still exercise the real routing directly.
        SetProcess(false); SetProcessInput(false); _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldActor = new(); _worldGridInput = new(); _worldAdapter = new() { WorldRoute = true }; _worldReducer = new(); _worldGate = new();
            _worldTestTime = Arcanum.Simulation.Movement.SimulationTime.At(0); _worldObservationTime = null;
            RetireWorldLifecycle(); _worldFocused = true; _worldUiFocused = false;
            _dragging = false; _worldMenuAnalogDirection = 0;
            _worldPanel?.Hide(); GetViewport().GuiReleaseFocus();
            _worldActor.Configure(result.TraversalSnapshot!, _worldTestTime.Value);
            _camera = new TerrainCameraController(camera.Camera);
            _camera.SetPlan(result.DiagnosticPlan!, WorldViewport);
            await readback();
        }
        finally
        {
            _worldActor.DisposeSession();
            if (GetViewport() is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
            // Settle resize while the detached controller/focus domain still owns
            // notifications. Restore the original controller without recomputing
            // Follow eligibility, center or a pending live lifecycle.
            await NextFrameAsync();
            _worldActor = actor; _worldGridInput = queue; _worldAdapter = adapter; _worldReducer = reducer; _worldGate = gate; _worldSettings = settings;
            _worldTestTime = clock; _worldObservationTime = observation;
            _worldLifecycleSession = lifecycle; _worldResetPending = reset; _worldReloadPending = reload;
            _readyDiagnosticPlan = scene; _readyActorPlan = plan; _readyActorInput = input; _readyActor = selected;
            _worldGridDiagnostic = diagnostic; _worldFocused = focused; _worldUiFocused = uiFocused;
            _dragging = dragging; _worldMenuAnalogDirection = menuDirection;
            _camera = camera; camera.Camera.Position = cameraPosition; camera.Camera.Zoom = cameraZoom;
            _canvas.SetActorVisibilityForReadback(actorVisible); _canvas.SetStaticVisibilityForReadback(staticVisible);
            _canvas.Publish(scene!, _cache);
            if (modal) _worldPanel?.Show(); else _worldPanel?.Hide();
            if (GodotObject.IsInstanceValid(focusOwner)) focusOwner!.GrabFocus(); else GetViewport().GuiReleaseFocus();
            _worldFocusSimulationForSelfTest = simulatedFocus; SetProcess(processing); SetProcessInput(processingInput); RefreshReadyStatus();
        }
    }
}
