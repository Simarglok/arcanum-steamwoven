using Arcanum.Data.Traversal;
using Arcanum.Presentation.Animation;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Transactional presentation of one owned authoritative grid session.</summary>
public sealed class DiagnosticGridActor
{
    private AnimationTimeline _timeline = Timeline(ActorDiagnosticProfile.HumanFixedArea, ActorDiagnosticAction.Idle);
    public IGridSession? Session { get; private set; }
    /// <summary>The bounded single-sector WORLD session, when that profile is configured.</summary>
    public DiagnosticGridSession? CardinalSession => Session as DiagnosticGridSession;
    // ARC-WORLD-020: the human with its run resource (the WORLD diagnostic never selects Run; facing tables are shared).
    // ARC-UI-039: the AREA hero's own body replaces it (<see cref="UseProfile"/>).
    public ActorDiagnosticProfile Profile { get; private set; } = ActorDiagnosticProfile.HumanFixedArea;
    /// <summary>ARC-UI-039: draws the actor with another profile of the same actions (the hero's body) and steps it with that body's
    /// gait timing, in the AREA session now and in every one this actor configures. The current action's clock restarts from frame 0
    /// on the new ART, whose frame count may differ.</summary>
    public void UseProfile(ActorDiagnosticProfile profile, GaitTiming timing)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(timing);
        if (profile.Run is null || profile.Vault is null || profile.Attack is null) throw new ArgumentException("ACTOR_PROFILE_ACTIONS", nameof(profile));
        AreaGaitTiming = timing;
        if (Session is AreaGridSession { Timing: not null } area) area.Timing = timing;
        if (ReferenceEquals(profile, Profile)) return;
        Profile = profile; AttackPose = null; DownPose = null;
        _timeline = Timeline(Session is { } session ? RenderState(session.State).Action : ActorDiagnosticAction.Idle);
    }
    public int CurrentFrame => Pose is { } pose ? pose.Frame : _timeline.CurrentFrame;
    public bool IsPlaying => _timeline.IsPlaying;
    public DiagnosticActorState State => Session is { } session ? Render(session.State)
        : new(new(6, 1), 5, ActorDiagnosticAction.Idle);
    /// <summary>ARC-COMBAT-005: the attack pose the host draws while the settled actor attacks in place (the profile's
    /// <see cref="ActorDiagnosticAction.Attack"/>, and since ARC-RENDER-039 its <see cref="ActorDiagnosticAction.LowAttack"/> or
    /// <see cref="ActorDiagnosticAction.Throw"/>, facing octant and frame); null otherwise. A moving actor is drawn by its movement
    /// whatever the pose says.</summary>
    public (ActorDiagnosticAction Action, int Octant, int Frame)? AttackPose { get; set; }
    /// <summary>ARC-COMBAT-021: the pose the host draws while the settled hero falls, lies unconscious or gets up (the profile's
    /// <see cref="ActorDiagnosticAction.FallDown"/> or <see cref="ActorDiagnosticAction.GetUp"/>, facing octant and frame); it wins over
    /// <see cref="AttackPose"/>. Null otherwise.</summary>
    public (ActorDiagnosticAction Action, int Octant, int Frame)? DownPose { get; set; }
    private (ActorDiagnosticAction Action, int Octant, int Frame)? Pose => Session?.State.Phase != MovementPhase.Settled ? null
        : DownPose ?? AttackPose;
    /// <summary>ARC-MAGIC-018: the octant a settled actor was turned to in place (the casting goal's facing turn, 0x42C850) and the
    /// movement facing it was turned from; drawn while the actor stays settled with that facing, dropped when it moves. Null otherwise.</summary>
    public (int Octant, NeighborDirection From)? TurnedFacing { get; set; }
    private DiagnosticActorState Render(MovementState state) => Pose is { } pose && state.Phase == MovementPhase.Settled
        ? RenderState(state) with { Facing = pose.Octant, Action = pose.Action }
        : TurnedFacing is { } turned && state.Phase == MovementPhase.Settled && state.Facing == turned.From
            ? RenderState(state) with { Facing = turned.Octant } : RenderState(state);
    public void Configure(CardinalTraversalSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null)
    {
        var session = new DiagnosticGridSession(snapshot, time, duration, limits);
        Session = session; AttackPose = null; DownPose = null; TurnedFacing = null; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    /// <summary>ARC-WORLD-016: the live actor overlay every AREA session this actor configures admits its steps against.</summary>
    public Func<AreaOccupancy?>? AreaOccupancy { get; set; }
    /// <summary>ARC-WORLD-074: whether every AREA session this actor configures passes living critters (<see cref="AreaGridSession.CrittersPass"/>).</summary>
    public Func<bool>? AreaCrittersPass { get; set; }
    /// <summary>ARC-WORLD-020: the gait timing and the current gait every AREA session this actor configures uses
    /// when no explicit step duration is given; null timing, or an explicit duration, keeps the fixed duration.</summary>
    public GaitTiming? AreaGaitTiming { get; set; }
    public Func<MovementGait>? AreaGait { get; set; }
    /// <summary>ARC-COMBAT-020: the step charge every AREA session this actor configures asks (<see cref="AreaGridSession.StepCharge"/>).</summary>
    public Func<AdmissionKey, MovementGait, string?>? AreaStepCharge { get; set; }
    public void Configure(AreaStepSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null,
        AreaChart? chart = null, CellCoord? start = null, NeighborDirection facing = NeighborDirection.XPlus)
    {
        var session = new AreaGridSession(snapshot, time, duration, limits, chart, start, facing) { Occupancy = AreaOccupancy, CrittersPass = AreaCrittersPass, Timing = duration is null ? AreaGaitTiming : null, Gait = AreaGait, StepCharge = AreaStepCharge };
        Session = session; AttackPose = null; DownPose = null; TurnedFacing = null; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    public void DisposeSession() { Session = null; AttackPose = null; DownPose = null; TurnedFacing = null; _timeline = Timeline(ActorDiagnosticAction.Idle); }

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
        // ARC-MAGIC-018: a turn in place lasts until the actor moves.
        if (session.State.Phase != MovementPhase.Settled || session.State.CommittedCell != previous.CommittedCell) TurnedFacing = null;
        return result;
    }
    public ActorDiagnosticPlacementInput Placement() => Session is AreaGridSession area
        ? DiagnosticActorMotion.ToPlacement(Render(area.State), CurrentFrame, area.Chart.MinimumCell, area.Chart.MaximumCell, (area.State.CommittedCell.X, area.State.CommittedCell.Y))
            with { CameraOriginX = AreaGridSession.LocationOriginX, CameraOriginY = AreaGridSession.LocationOriginY }
        : Placement(Session!, Session!.State, CurrentFrame);
    /// <summary>The continuous foot point the camera follows: the projected position plus the drawn anchor offset,
    /// <c>(40,20)</c> on the AREA chart (the diamond centre, ARC-RENDER-011) and <c>(0,20)</c> in WORLD.</summary>
    public Vec2 FootAnchor => DiagnosticActorMotion.Project(State.Position)
        + (Session is AreaGridSession ? new Vec2(AreaGridSession.LocationOriginX, 20) : new Vec2(0, 20));
    // The area profile places the actor anywhere in the admitted core chart, from the chart's location origin;
    // WORLD keeps one sector and its retained origin.
    private static ActorDiagnosticPlacementInput Placement(IGridSession session, MovementState state, int frame) => session is AreaGridSession area
        ? DiagnosticActorMotion.ToPlacement(RenderState(state), frame, area.Chart.MinimumCell, area.Chart.MaximumCell, (state.CommittedCell.X, state.CommittedCell.Y))
            with { CameraOriginX = AreaGridSession.LocationOriginX, CameraOriginY = AreaGridSession.LocationOriginY }
        : Placement(state, frame);
    public static double Seconds(ExactScalar value) => GridActorMath.Seconds(value);
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
    public static int Octant(NeighborDirection facing) => GridActorMath.Octant(facing);
    public static ActorDiagnosticPlacementInput Placement(MovementState state, int frame) =>
        DiagnosticActorMotion.ToPlacement(RenderState(state), frame); // pure geometry ONLY
    // ARC-UI-039: a walk or run plays at the gait's rate (research L6, equal to the human's ART rate at Speed 8).
    private AnimationTimeline Timeline(ActorDiagnosticAction action) => Timeline(Profile, action, action switch
    {
        ActorDiagnosticAction.Walk => AreaGaitTiming?.ArtRate(MovementGait.Walk),
        ActorDiagnosticAction.Run => AreaGaitTiming?.ArtRate(MovementGait.Run),
        _ => null,
    });
    private static AnimationTimeline Timeline(ActorDiagnosticProfile profile, ActorDiagnosticAction action, int? rate = null)
    {
        var resource = profile.Resource(action);
        var timeline = new AnimationTimeline(resource.FramesPerGroup, rate ?? resource.RawFramesPerSecond);
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
