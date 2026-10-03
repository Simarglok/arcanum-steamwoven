using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private async Task WorldOriginalReloadReadbackAsync(TerrainLoadResult result)
    {
        if (!_nativeSmoke || result.TraversalSnapshot?.InputIdentity.Kind != Arcanum.Data.Traversal.CardinalTraversalInputKind.AuthenticatedOriginal)
            throw new InvalidOperationException("Original native reload requires guarded input.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("GRID original reload: " + why); }
        _worldActor.Configure(result.TraversalSnapshot!, SimulationTime.At(0));
        _worldGridInput = new(); RetireWorldLifecycle(); _worldAdapter.Clear();
        _worldReducer.SetStartupDevices(Array.Empty<int>()); _worldTestTime = SimulationTime.At(0); _worldFocused = true;
        if (WorldModal) ToggleWorldSettings(); GetViewport().GuiReleaseFocus(); QuarantineWorld();
        var none = new RawInputFrame(-1, default);
        void Frame(int numerator) => WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1, observationTime: SimulationTime.At(numerator,16));
        Frame(1); Frame(2); Frame(3);
        _worldTestTime = SimulationTime.At(1); _Input(new InputEventKey { Keycode = Key.D, Pressed = true });
        WorldFrameForSelfTest(none with {KeyboardActor = new(1,0), KeyboardActorHeld = true}, Array.Empty<RawInputFrame>(), .1, observationTime: SimulationTime.At(17,16));
        var retired = _worldActor.Session!; var epoch = retired.Snapshot.EvidenceEpoch; var previousLoad = _lastLoad;
        _worldTestTime = SimulationTime.At(9,8); _Input(new InputEventKey { Keycode = Key.L, Pressed = true });
        Require(ReferenceEquals(retired,_worldActor.Session) && _worldReloadPending && ReferenceEquals(previousLoad,_lastLoad), "L loaded before settlement");
        Frame(19); Frame(20);
        Require(ReferenceEquals(retired,_worldActor.Session) && retired.State.Phase == MovementPhase.Returning
            && retired.State.Anchor.U == ExactScalar.Create(49,8), "early L did not return");
        Frame(21);
        Require(!ReferenceEquals(previousLoad,_lastLoad) && retired.State.Phase == MovementPhase.Settled
            && retired.State.CommittedCell == new CellCoord(6,1), "L not started after real arrival");
        await (_lastLoad ?? throw new InvalidOperationException("Reload task missing."));
        var successor = _worldActor.Session;
        Require(successor is not null && !ReferenceEquals(retired,successor) && successor.Snapshot.EvidenceEpoch > epoch
            && successor.Snapshot.EvidenceEpoch == _generation && successor.State.LastOpportunity is null
            && successor.State.CommittedCell == new CellCoord(6,1) && _worldLifecycleSession is null
            && !_worldReloadPending && !_worldResetPending, "reload epoch/actor/pending ownership");
        Require(!_coordinator.TryPublish(epoch, () => throw new InvalidOperationException("stale reload result published")), "old publication remained current");
        GD.Print("ARC-WORLD-003 original reload PASS actual L deferred until early-return CenterArrived; fresh guarded snapshot epoch/empty successor/retired publication rejected");
    }

    internal async Task WorldRetirementMatrixForSelfTestAsync(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("Synthetic test bootstrap required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("GRID retirement: " + why); }
        var none = new RawInputFrame(-1, default);
        var held = none with { KeyboardActor = new(1,0), KeyboardActorHeld = true };
        void Frame(int time, RawInputFrame? keys = null) => WorldFrameForSelfTest(keys ?? none, Array.Empty<RawInputFrame>(), .1, observationTime: SimulationTime.At(time));
        void Prepare()
        {
            _worldTestTime = SimulationTime.At(0); _worldObservationTime = null;
            _worldActor.Configure(result.TraversalSnapshot!, SimulationTime.At(0), StepDuration.Of(8));
            _worldGridInput = new(); RetireWorldLifecycle(); _worldAdapter.Clear();
            _worldReducer.SetStartupDevices(Array.Empty<int>()); _worldFocused = true;
            QuarantineWorld(); PublishWorldSelection(); Frame(1); Frame(2); Frame(3);
            _worldTestTime = SimulationTime.At(10); _Input(new InputEventKey { Keycode = Key.D, Pressed = true });
            Frame(11, held); Frame(12, held);
            Require(_worldActor.Session!.State.Phase == MovementPhase.OutboundBeforeEntry, "moving setup");
        }
        foreach (var release in new[] { 12, 14, 16 })
        {
            Prepare(); var retired = _worldActor.Session!; var oldQueue = _worldGridInput;
            _worldTestTime = SimulationTime.At(release); _Input(new InputEventKey { Keycode = Key.R, Pressed = true });
            Require(ReferenceEquals(retired, _worldActor.Session) && _worldResetPending && !_worldGate.IsOpen, "R must defer replacement");
            Frame(release + 1); Frame(release + 2);
            Require(ReferenceEquals(retired, _worldActor.Session) && retired.State.Action == MovementAction.Walk,
                "accepted return/finish must remain old Walk session");
            if (release == 12)
                Require(retired.State.Anchor.U == ExactScalar.Create(49,8) && retired.State.CommittedCell == new CellCoord(6,1), "early13 literal anchor/cell");
            else Require(retired.State.CommittedCell == new CellCoord(7,1), "entry precedes equality/late neutral");
            Frame(20); Frame(21);
            var last = retired.LastAdvance!;
            Require(retired.State.Phase == MovementPhase.Settled && retired.State.CommittedCell == new CellCoord(release == 12 ? 6 : 7,1), "old generation exact center");
            Require(last.Movement.Events.Count(e => e.Kind == MovementEventKind.CenterArrived) == 1
                && last.Movement.Events.Single(e => e.Kind == MovementEventKind.CenterArrived).Time == SimulationTime.At(release == 12 ? 14 : 18), "exact real arrival, no reset teleport event");
            Require(!ReferenceEquals(retired, _worldActor.Session) && !_worldResetPending && !_worldReloadPending && _worldLifecycleSession is null
                && _worldActor.Session!.State.CommittedCell == new CellCoord(6,1) && _worldActor.Session.State.LastOpportunity is null,
                "explicit successor starts clean at diagnostic spawn");
            try { oldQueue.Observe(_worldActor.Session!, SimulationTime.At(22), ActorIntent.Neutral); throw new InvalidOperationException("old queue accepted successor"); }
            catch (InvalidOperationException error) when (error.Message == "GRID_OBSERVATION_RETIRED_SESSION") { }
            Frame(22); Frame(23); Frame(24,held); Frame(25,held);
            Require(_worldActor.Session!.State.LastOpportunity!.Value.MaterialIntentKey.Sequence == 0, "old input sequence leaked into successor");
        }
        // Pending reload without actual arguments must fail, never masquerade as
        // reset. Fault disposal clears the live scene only after old settlement.
        Prepare(); var beforeReload = _worldActor.Session!; _arguments = null;
        _worldTestTime = SimulationTime.At(12); RequestWorldLifecycle(true);
        Frame(13); Frame(14);
        try { Frame(15); throw new InvalidOperationException("missing reload succeeded"); }
        catch (InvalidOperationException error) when (error.Message == "GRID_RELOAD_ARGUMENTS_REQUIRED") { }
        Require(beforeReload.State.Phase == MovementPhase.Settled && _worldActor.Session is null
            && !_worldResetPending && !_worldReloadPending && _worldLifecycleSession is null
            && _hud!.Text.Contains("failure stage=ActorPlan", StringComparison.Ordinal), "missing reload arguments did not fail closed");
        PublishSyntheticForSelfTest(arguments, result);
        // Both catalogue/grid consumers use this production scope. Exercise
        // async success AND failure with an active session and pending reload;
        // reference/state identity proves no pending live inputs were consumed.
        foreach (var fail in new[] { false, true })
        {
            Prepare(); _worldTestTime = SimulationTime.At(12); RequestWorldLifecycle(true);
            var actor = _worldActor; var state = actor.Session!.State; var queue = _worldGridInput;
            var adapter = _worldAdapter; var reducer = _worldReducer; var gate = _worldGate;
            var camera = _camera; var cameraState = camera!.State; var follow = camera.FollowState;
            var plan = _readyDiagnosticPlan; var clock = _worldTestTime; var frontier = queue.CompleteFrontier;
            var pending = queue.CompleteThrough(SimulationTime.At(12)).Length;
            var processing = IsProcessing(); var visible = _canvas!.ActorVisibleForReadback;
            try
            {
                await WithWorldReadbackScopeAsync(result, async () =>
                {
                    Require(!ReferenceEquals(actor,_worldActor) && !ReferenceEquals(queue,_worldGridInput)
                        && !ReferenceEquals(reducer,_worldReducer) && !ReferenceEquals(gate,_worldGate)
                        && _worldLifecycleSession is null && !_worldReloadPending, "readback not isolated");
                    _worldActor.Advance(SimulationTime.At(1), [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0,ActorIntent.Toward(NeighborDirection.XPlus))])]);
                    _canvas.SetActorVisibilityForReadback(false); _canvas.SetStaticVisibilityForReadback(false);
                    _camera!.ResetWorld(WorldAnchor,WorldViewport,3); await NextFrameAsync();
                    if (fail) throw new InvalidOperationException("owned readback failure");
                });
                Require(!fail, "expected failure missing");
            }
            catch (InvalidOperationException error) when (fail && error.Message == "owned readback failure") { }
            Require(ReferenceEquals(actor,_worldActor) && ReferenceEquals(state,_worldActor.Session!.State)
                && ReferenceEquals(queue,_worldGridInput) && ReferenceEquals(adapter,_worldAdapter)
                && ReferenceEquals(reducer,_worldReducer) && ReferenceEquals(gate,_worldGate)
                && ReferenceEquals(camera,_camera) && _camera!.State == cameraState && _camera.FollowState == follow
                && ReferenceEquals(plan,_readyDiagnosticPlan) && _worldTestTime == clock
                && ReferenceEquals(_worldLifecycleSession,actor.Session) && _worldReloadPending && !_worldResetPending
                && queue.CompleteFrontier == frontier && queue.CompleteThrough(SimulationTime.At(12)).Length == pending
                && IsProcessing() == processing && _canvas.ActorVisibleForReadback == visible && _canvas.StaticVisibleForReadback,
                "async success/failure leaked ownership/state/visibility");
        }
        // Failure/new publication clears old pending lifecycle; a successor may
        // not execute an old reset or carry its observation queue/sequence.
        var oldSession = _worldActor.Session!; var generation = _coordinator.BeginGeneration();
        PublishSyntheticFailureForSelfTest(TerrainViewerMode.WorldDiagnostic, new(TerrainViewerErrorCode.RenderFailure,Stage:TerrainViewerFailureStage.Upload));
        Require(_worldLifecycleSession is null && !_worldReloadPending && _worldActor.Session is null, "failure retirement");
        PublishSyntheticForSelfTest(arguments,result);
        Require(!_coordinator.TryPublish(generation-1, () => throw new InvalidOperationException("stale failure published"))
            && _worldLifecycleSession is null && _worldActor.Session!.State.LastOpportunity is null, "stale app publication");
        Prepare(); var aborted = _worldActor.Session!; var abortedState = aborted.State;
        ExitWorld(); ExitWorld();
        Require(_worldActor.Session is null && _worldLifecycleSession is null && !_worldResetPending && !_worldReloadPending
            && ReferenceEquals(aborted.State,abortedState) && aborted.State.Phase == MovementPhase.OutboundBeforeEntry,
            "exit disposal manufactured settlement or retained lifecycle");
        Require(oldSession.State.Phase == MovementPhase.OutboundBeforeEntry, "failure disposal manufactured CenterArrived");
        GD.Print("ARC-WORLD-003 retirement/ownership PASS production R early/equality/late/settled replacement/retired queue/reload missing args/async readback success-failure/stale app publication/exit disposal distinct from CenterArrived");
    }
}
