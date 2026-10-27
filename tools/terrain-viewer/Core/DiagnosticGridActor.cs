using Arcanum.Data.Traversal;
using Arcanum.Presentation.Animation;
using Arcanum.Simulation.Movement;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Transactional presentation of one owned authoritative grid session.</summary>
public sealed class DiagnosticGridActor
{
    private AnimationTimeline _timeline = Timeline(ActorDiagnosticAction.Idle);
    public IGridSession? Session { get; private set; }
    /// <summary>The bounded single-sector WORLD session, when that profile is configured.</summary>
    public DiagnosticGridSession? CardinalSession => Session as DiagnosticGridSession;
    // ARC-WORLD-020: the human with its run resource (the WORLD diagnostic never selects Run; facing tables are shared).
    public ActorDiagnosticProfile Profile => ActorDiagnosticProfile.HumanFixedArea;
    public int CurrentFrame => Pose is { } pose ? pose.Frame : _timeline.CurrentFrame;
    public bool IsPlaying => _timeline.IsPlaying;
    public DiagnosticActorState State => Session is { } session ? Render(session.State)
        : new(new(6, 1), 5, ActorDiagnosticAction.Idle);
    /// <summary>ARC-COMBAT-005: the attack pose the host draws while the settled actor attacks in place (facing octant and
    /// attack frame); null otherwise. A moving actor is drawn by its movement whatever the pose says.</summary>
    public (int Octant, int Frame)? AttackPose { get; set; }
    private (int Octant, int Frame)? Pose => AttackPose is { } pose && Session?.State.Phase == MovementPhase.Settled ? pose : null;
    private DiagnosticActorState Render(MovementState state) => Pose is { } pose && state.Phase == MovementPhase.Settled
        ? RenderState(state) with { Facing = pose.Octant, Action = ActorDiagnosticAction.Attack } : RenderState(state);
    public void Configure(CardinalTraversalSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null)
    {
        var session = new DiagnosticGridSession(snapshot, time, duration, limits);
        Session = session; AttackPose = null; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    /// <summary>ARC-WORLD-016: the live actor overlay every AREA session this actor configures admits its steps against.</summary>
    public Func<AreaOccupancy?>? AreaOccupancy { get; set; }
    /// <summary>ARC-WORLD-074: whether every AREA session this actor configures passes living critters (<see cref="AreaGridSession.CrittersPass"/>).</summary>
    public Func<bool>? AreaCrittersPass { get; set; }
    /// <summary>ARC-WORLD-020: the gait timing and the current gait every AREA session this actor configures uses
    /// when no explicit step duration is given; null timing, or an explicit duration, keeps the fixed duration.</summary>
    public GaitTiming? AreaGaitTiming { get; set; }
    public Func<MovementGait>? AreaGait { get; set; }
    public void Configure(AreaStepSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null,
        AreaChart? chart = null, CellCoord? start = null, NeighborDirection facing = NeighborDirection.XPlus)
    {
        var session = new AreaGridSession(snapshot, time, duration, limits, chart, start, facing) { Occupancy = AreaOccupancy, CrittersPass = AreaCrittersPass, Timing = duration is null ? AreaGaitTiming : null, Gait = AreaGait };
        Session = session; AttackPose = null; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    public void DisposeSession() { Session = null; AttackPose = null; _timeline = Timeline(ActorDiagnosticAction.Idle); }

    public GridAdvance Advance(SimulationTime time, ReadOnlySpan<InputBatch> batches,
        Action<ActorDiagnosticPlacementInput>? validateSelection = null)
    {
        var session = Session ?? throw new InvalidOperationException("GRID_SESSION_UNAVAILABLE");
        var trial = _timeline.Snapshot();
        var previous = session.State;
        var result = session.Advance(time, batches, (state, movement) =>
        {
            var cursor = previous.Time;
            var moving = previous.Action;
            var stepping = StepAction(previous.Segment?.Assessment);
            var action = Action(moving, stepping);
            foreach (var item in movement.Events)
            {
                trial.Advance(Seconds(item.Time.Value - cursor.Value));
                cursor = item.Time;
                if (item.Kind == MovementEventKind.ActionChanged) moving = item.NewAction!.Value;
                // ARC-WORLD-020: each step's permit names its gait; a run step draws the run ART.
                if (item.Kind == MovementEventKind.StepStarted) stepping = StepAction(item.Assessment);
                var next = Action(moving, stepping);
                // A vault plays once per window step, so a vault right after a vault restarts it too.
                if (next != action || (item.Kind == MovementEventKind.StepStarted && next == ActorDiagnosticAction.Vault)) { action = next; trial = Timeline(action); }
                // Facing changes select another physical row at the same frame.
                // The action clock stays continuous for Walk->Walk, Run->Run and turns.
            }
            trial.Advance(Seconds(state.Time.Value - cursor.Value));
            validateSelection?.Invoke(Placement(session, state, trial.CurrentFrame));
        });
        if (result.Movement.Status == MovementStatus.Completed) _timeline = trial;
        return result;
    }
    public ActorDiagnosticPlacementInput Placement() => Session is AreaGridSession area
        ? DiagnosticActorMotion.ToPlacement(Render(area.State), CurrentFrame, area.Chart.MinimumCell, area.Chart.MaximumCell)
            with { CameraOriginX = AreaGridSession.LocationOriginX, CameraOriginY = AreaGridSession.LocationOriginY }
        : Placement(Session!, Session!.State, CurrentFrame);
    /// <summary>The continuous foot point the camera follows: the projected position plus the drawn anchor offset,
    /// <c>(40,20)</c> on the AREA chart (the diamond centre, ARC-RENDER-011) and <c>(0,20)</c> in WORLD.</summary>
    public Vec2 FootAnchor => DiagnosticActorMotion.Project(State.Position)
        + (Session is AreaGridSession ? new Vec2(AreaGridSession.LocationOriginX, 20) : new Vec2(0, 20));
    // The area profile places the actor anywhere in the admitted core chart, from the chart's location origin;
    // WORLD keeps one sector and its retained origin.
    private static ActorDiagnosticPlacementInput Placement(IGridSession session, MovementState state, int frame) => session is AreaGridSession area
        ? DiagnosticActorMotion.ToPlacement(RenderState(state), frame, area.Chart.MinimumCell, area.Chart.MaximumCell)
            with { CameraOriginX = AreaGridSession.LocationOriginX, CameraOriginY = AreaGridSession.LocationOriginY }
        : Placement(state, frame);
    public static double Seconds(ExactScalar value) => (double)value.Numerator / (double)value.Denominator;
    public static DiagnosticActorState RenderState(MovementState state) => new(
        new(Seconds(state.Anchor.U), Seconds(state.Anchor.V)), Octant(state.Facing), Action(state.Action, StepAction(state.Segment?.Assessment)));
    // The drawn action of a moving step from its permit's gait code (ARC-WORLD-020 run, ARC-WORLD-021 vault).
    private static ActorDiagnosticAction StepAction(StepAssessmentTiming? permit) =>
        permit?.Provenance.Contains(GaitTiming.VaultProvenance) == true ? ActorDiagnosticAction.Vault
        : permit?.Provenance.Contains(GaitTiming.RunProvenance) == true ? ActorDiagnosticAction.Run : ActorDiagnosticAction.Walk;
    private static ActorDiagnosticAction Action(MovementAction action, ActorDiagnosticAction stepping) => action switch
    {
        MovementAction.Idle => ActorDiagnosticAction.Idle,
        MovementAction.Walk => stepping,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    public static int Octant(NeighborDirection facing) => facing switch
    {
        NeighborDirection.XMinusYMinus => 0, NeighborDirection.XMinus => 1,
        NeighborDirection.XMinusYPlus => 2, NeighborDirection.YPlus => 3,
        NeighborDirection.XPlusYPlus => 4, NeighborDirection.XPlus => 5,
        NeighborDirection.XPlusYMinus => 6, NeighborDirection.YMinus => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(facing))
    };
    public static ActorDiagnosticPlacementInput Placement(MovementState state, int frame) =>
        DiagnosticActorMotion.ToPlacement(RenderState(state), frame); // pure geometry ONLY
    private static AnimationTimeline Timeline(ActorDiagnosticAction action)
    {
        var resource = ActorDiagnosticProfile.HumanFixedArea.Resource(action);
        var timeline = new AnimationTimeline(resource.FramesPerGroup, resource.RawFramesPerSecond);
        // The vault plays once over its step (it stops on the last frame); walk and run cycles loop.
        timeline.SetMode(action == ActorDiagnosticAction.Vault ? PlaybackMode.Once : PlaybackMode.Loop);
        if (action is ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run or ActorDiagnosticAction.Vault) timeline.Play();
        return timeline;
    }

    /// <summary>
    /// WORLD policy: quantize the aggregate in screen space, after the reducer's
    /// deadzone and once-only actor inversion. Retain components at >= half the
    /// largest magnitude, inclusive ties. Translate all eight screen sectors to
    /// projected adjacent map centers; never reroute a denied Data request.
    /// Sensitivity affects magnitude, not duration. Opposing keys net neutral;
    /// their separate activity bit still governs safety and camera Follow.
    /// </summary>
    public static NeighborDirection? Resolve(Vec2 reduced, bool invertActorX = false, bool invertActorY = false)
    {
        if (!double.IsFinite(reduced.X) || !double.IsFinite(reduced.Y)) throw new ArgumentException("GRID_INPUT_NONFINITE");
        reduced = new(invertActorX ? -reduced.X : reduced.X, invertActorY ? -reduced.Y : reduced.Y);
        var largest = Math.Max(Math.Abs(reduced.X), Math.Abs(reduced.Y));
        if (largest == 0) return null;
        var x = Math.Abs(reduced.X) >= largest * .5 ? Math.Sign(reduced.X) : 0;
        var y = Math.Abs(reduced.Y) >= largest * .5 ? Math.Sign(reduced.Y) : 0;
        return (x, y) switch
        {
            (0,-1) => NeighborDirection.XMinusYMinus, (1,-1) => NeighborDirection.XMinus,
            (1,0) => NeighborDirection.XMinusYPlus, (1,1) => NeighborDirection.YPlus,
            (0,1) => NeighborDirection.XPlusYPlus, (-1,1) => NeighborDirection.XPlus,
            (-1,0) => NeighborDirection.XPlusYMinus, (-1,-1) => NeighborDirection.YMinus,
            _ => throw new InvalidOperationException("GRID_INPUT_RESOLUTION_INVALID")
        };
    }
}
