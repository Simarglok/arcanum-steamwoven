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
    public ActorDiagnosticProfile Profile => ActorDiagnosticProfile.HumanFixed;
    public int CurrentFrame => _timeline.CurrentFrame;
    public bool IsPlaying => _timeline.IsPlaying;
    public DiagnosticActorState State => Session is { } session ? RenderState(session.State)
        : new(new(6, 1), 5, ActorDiagnosticAction.Idle);
    public void Configure(CardinalTraversalSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null)
    {
        var session = new DiagnosticGridSession(snapshot, time, duration, limits);
        Session = session; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    public void Configure(AreaStepSnapshot snapshot, SimulationTime time, StepDuration? duration = null, MovementLimits? limits = null)
    {
        var session = new AreaGridSession(snapshot, time, duration, limits);
        Session = session; _timeline = Timeline(ActorDiagnosticAction.Idle);
    }
    public void DisposeSession() { Session = null; _timeline = Timeline(ActorDiagnosticAction.Idle); }

    public GridAdvance Advance(SimulationTime time, ReadOnlySpan<InputBatch> batches,
        Action<ActorDiagnosticPlacementInput>? validateSelection = null)
    {
        var session = Session ?? throw new InvalidOperationException("GRID_SESSION_UNAVAILABLE");
        var trial = _timeline.Snapshot();
        var previous = session.State;
        var result = session.Advance(time, batches, (state, movement) =>
        {
            var cursor = previous.Time;
            var action = Action(previous.Action);
            foreach (var item in movement.Events)
            {
                trial.Advance(Seconds(item.Time.Value - cursor.Value));
                cursor = item.Time;
                if (item.Kind == MovementEventKind.ActionChanged)
                {
                    var next = Action(item.NewAction!.Value);
                    if (next != action) { action = next; trial = Timeline(action); }
                }
                // Facing changes select another physical row at the same frame.
                // The action clock stays continuous for Walk->Walk and turns.
            }
            trial.Advance(Seconds(state.Time.Value - cursor.Value));
            validateSelection?.Invoke(Placement(state, trial.CurrentFrame));
        });
        if (result.Movement.Status == MovementStatus.Completed) _timeline = trial;
        return result;
    }
    public ActorDiagnosticPlacementInput Placement() => Placement(Session!.State, CurrentFrame);
    public static double Seconds(ExactScalar value) => (double)value.Numerator / (double)value.Denominator;
    public static DiagnosticActorState RenderState(MovementState state) => new(
        new(Seconds(state.Anchor.U), Seconds(state.Anchor.V)), Octant(state.Facing), Action(state.Action));
    private static ActorDiagnosticAction Action(MovementAction action) => action switch
    {
        MovementAction.Idle => ActorDiagnosticAction.Idle,
        MovementAction.Walk => ActorDiagnosticAction.Walk,
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
        var resource = ActorDiagnosticProfile.HumanFixed.Resource(action);
        var timeline = new AnimationTimeline(resource.FramesPerGroup, resource.RawFramesPerSecond);
        timeline.SetMode(PlaybackMode.Loop);
        if (action == ActorDiagnosticAction.Walk) timeline.Play();
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
