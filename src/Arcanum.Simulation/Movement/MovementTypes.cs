namespace Arcanum.Simulation.Movement;

public readonly record struct CellCoord(int X, int Y);
public readonly record struct WorldAnchor(ExactScalar U, ExactScalar V);
public enum NeighborDirection { XMinusYMinus, XMinus, XMinusYPlus, YPlus, XPlusYPlus, XPlus, XPlusYMinus, YMinus }
public enum MovementPhase { Settled, OutboundBeforeEntry, OutboundAfterEntry, Returning }
public enum MovementAction { Idle, Walk }
public enum AssessmentCoverage { Supported, Unknown, Unsupported }
public enum AssessmentDisposition { Open, Blocked }
public enum MovementInputKind { SetActorIntent, RequestReassessment }
public enum MovementEventKind { StepStarted, StepCancelled, CellCommitted, CenterArrived, StepRejected, FacingChanged, ActionChanged }
public enum MovementStatus { Completed, InvalidRequest }
public enum MovementFailureKind
{
    InvalidState, InvalidTarget, InvalidInput, InvalidAssessment, IdentityMismatch,
    WrongReassessmentPhase, InvalidRevision, NumericOverflow, CapacityExceeded,
}

public readonly record struct ActorBinding(
    string ChartId, string ActorId, string ContextId, string CapabilitiesProfileId,
    string CapabilitiesFingerprint, string AssessmentProfileId, string InputFingerprint, long EvidenceEpoch);
public readonly record struct InputKey(SimulationTime Time, long Sequence);
public readonly record struct ActorIntent(bool Active, NeighborDirection? Direction)
{
    public static ActorIntent Neutral => new(false, null);
    public static ActorIntent Toward(NeighborDirection direction) => new(true, direction);
}
public readonly record struct MovementInput(long Sequence, MovementInputKind Kind, ActorIntent Intent, long EvidenceRevision)
{
    public static MovementInput Set(long sequence, ActorIntent intent) => new(sequence, MovementInputKind.SetActorIntent, intent, 0);
    public static MovementInput Reassess(long sequence, long revision) => new(sequence, MovementInputKind.RequestReassessment, ActorIntent.Neutral, revision);
}

/// <summary>Session limits can be lowered, never raised above the policy maxima.</summary>
public sealed class MovementLimits
{
    public MovementLimits(int batches = 4096, int inputs = 4096, int assessments = 1024,
        int opportunities = 1024, int events = 8192)
    {
        if (batches is < 1 or > 4096 || inputs is < 1 or > 4096 || assessments is < 1 or > 1024 ||
            opportunities is < 1 or > 1024 || events is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(batches));
        MaxBatches = batches; MaxInputs = inputs; MaxAssessments = assessments;
        MaxOpportunities = opportunities; MaxEvents = events;
    }
    public int MaxBatches { get; }
    public int MaxInputs { get; }
    public int MaxAssessments { get; }
    public int MaxOpportunities { get; }
    public int MaxEvents { get; }
}

public sealed class InputBatch
{
    public InputBatch(SimulationTime time, ReadOnlySpan<MovementInput> entries)
    {
        if (entries.Length > 4096) throw new ArgumentOutOfRangeException(nameof(entries));
        Time = time;
        Entries = Array.AsReadOnly(entries.ToArray());
    }
    public SimulationTime Time { get; }
    public IReadOnlyList<MovementInput> Entries { get; }
}

public readonly record struct AdmissionKey(ActorBinding Binding, CellCoord From, CellCoord To,
    NeighborDirection Direction, SimulationTime StartTime, long OpportunityId, long ProposedStepId,
    InputKey MaterialIntentKey, long EvidenceRevision);

public sealed class StepAssessmentTiming
{
    public StepAssessmentTiming(AdmissionKey key, AssessmentCoverage coverage, AssessmentDisposition? disposition,
        StepDuration? duration, ReadOnlySpan<string> reasons, ReadOnlySpan<string> provenance)
    {
        CheckCodes(reasons); CheckCodes(provenance);
        Key = key; Coverage = coverage; Disposition = disposition; Duration = duration;
        Reasons = Array.AsReadOnly(reasons.ToArray()); Provenance = Array.AsReadOnly(provenance.ToArray());
    }
    private static void CheckCodes(ReadOnlySpan<string> codes)
    {
        if (codes.Length > 16) throw new ArgumentOutOfRangeException(nameof(codes));
        foreach (var code in codes)
            if (code is { Length: > 256 }) throw new ArgumentOutOfRangeException(nameof(codes));
    }
    public AdmissionKey Key { get; }
    public AssessmentCoverage Coverage { get; }
    public AssessmentDisposition? Disposition { get; }
    public StepDuration? Duration { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<string> Provenance { get; }
}

public sealed class MovementRequest
{
    public MovementRequest(SimulationTime targetTime, ReadOnlySpan<InputBatch> batches,
        ReadOnlySpan<StepAssessmentTiming> assessments)
    {
        if (batches.Length > 4096 || assessments.Length > 1024) throw new ArgumentOutOfRangeException(nameof(batches));
        var entries = 0;
        foreach (var batch in batches)
        {
            ArgumentNullException.ThrowIfNull(batch);
            entries = checked(entries + batch.Entries.Count);
            if (entries > 4096) throw new ArgumentOutOfRangeException(nameof(batches));
        }
        foreach (var record in assessments) ArgumentNullException.ThrowIfNull(record);
        // Nested objects already own bounded defensive copies and have no setters.
        TargetTime = targetTime;
        Batches = Array.AsReadOnly(batches.ToArray());
        Assessments = Array.AsReadOnly(assessments.ToArray());
    }
    public SimulationTime TargetTime { get; }
    public IReadOnlyList<InputBatch> Batches { get; }
    public IReadOnlyList<StepAssessmentTiming> Assessments { get; }
}

public sealed class ActiveSegment
{
    internal ActiveSegment(StepAssessmentTiming assessment, SimulationTime? releaseTime = null)
    {
        Assessment = assessment; ReleaseTime = releaseTime;
    }
    public StepAssessmentTiming Assessment { get; }
    public AdmissionKey Key => Assessment.Key;
    public long StepId => Key.ProposedStepId;
    public CellCoord From => Key.From;
    public CellCoord To => Key.To;
    public NeighborDirection Direction => Key.Direction;
    public SimulationTime StartTime => Key.StartTime;
    public StepDuration Duration => Assessment.Duration!.Value;
    public SimulationTime EntryTime => new(StartTime.Value + Duration.Value / ExactScalar.Integer(2));
    public SimulationTime ArrivalTime => new(StartTime.Value + Duration.Value);
    public SimulationTime? ReleaseTime { get; }
    public SimulationTime EndTime => ReleaseTime is { } r ? new(r.Value + (r.Value - StartTime.Value)) : ArrivalTime;
}

// Only production code and friend tests can create raw descriptors. Every runtime/query entry validates them.
internal sealed record MovementDescriptor(ActorBinding Binding, MovementLimits Limits,
    SimulationTime Time, bool IsClosed, CellCoord CommittedCell, WorldAnchor Anchor,
    MovementPhase Phase, ActorIntent LatestIntent, InputKey? LatestInputKey, InputKey? MaterialIntentKey,
    NeighborDirection Facing, MovementAction Action, ActiveSegment? Segment,
    long EvidenceRevision, long NextOpportunityId, long NextStepId, long NextEventOrdinal,
    AdmissionKey? LastOpportunity, string? LastOpportunityDisposition);

public sealed class MovementState
{
    private MovementState(MovementDescriptor descriptor) { Descriptor = descriptor; }
    internal MovementDescriptor Descriptor { get; }
    internal static MovementState FromDescriptor(MovementDescriptor descriptor) => new(descriptor);
    public static MovementState CreateSettled(ActorBinding binding, CellCoord cell, SimulationTime initialTime,
        NeighborDirection initialFacing, MovementLimits? limits = null)
    {
        var state = new MovementState(new(binding, limits ?? new MovementLimits(), initialTime, false, cell,
            MovementGeometry.Center(cell), MovementPhase.Settled, ActorIntent.Neutral, null, null,
            initialFacing, MovementAction.Idle, null, 0, 1, 1, 1, null, null));
        if (!MovementValidation.IsValidState(state)) throw new ArgumentException("Invalid initial state.");
        return state;
    }
    public ActorBinding Binding => Descriptor.Binding;
    public MovementLimits Limits => Descriptor.Limits;
    public SimulationTime Time => Descriptor.Time;
    public bool IsClosed => Descriptor.IsClosed;
    public CellCoord CommittedCell => Descriptor.CommittedCell;
    public WorldAnchor Anchor => Descriptor.Anchor;
    public MovementPhase Phase => Descriptor.Phase;
    public ActorIntent LatestIntent => Descriptor.LatestIntent;
    public InputKey? LatestInputKey => Descriptor.LatestInputKey;
    public InputKey? MaterialIntentKey => Descriptor.MaterialIntentKey;
    public NeighborDirection Facing => Descriptor.Facing;
    public MovementAction Action => Descriptor.Action;
    public ActiveSegment? Segment => Descriptor.Segment;
    public long EvidenceRevision => Descriptor.EvidenceRevision;
    public long NextOpportunityId => Descriptor.NextOpportunityId;
    public long NextStepId => Descriptor.NextStepId;
    public long NextEventOrdinal => Descriptor.NextEventOrdinal;
    public AdmissionKey? LastOpportunity => Descriptor.LastOpportunity;
    public string? LastOpportunityDisposition => Descriptor.LastOpportunityDisposition;
}

public sealed record MovementSnapshot(ActorBinding Binding, SimulationTime Time, bool IsClosed, CellCoord CommittedCell,
    WorldAnchor Anchor, MovementPhase Phase, ActorIntent LatestIntent, InputKey? LatestInputKey, InputKey? MaterialIntentKey,
    NeighborDirection Facing, MovementAction Action, ActiveSegment? Segment,
    long EvidenceRevision, long NextOpportunityId, long NextStepId, long NextEventOrdinal,
    AdmissionKey? LastOpportunity, string? LastOpportunityDisposition);
public sealed record MovementFailure(MovementFailureKind Kind, int? InputBatchIndex = null,
    int? AssessmentIndex = null, AdmissionKey? Key = null);
public sealed record MovementEvent(SimulationTime Time, long Ordinal, MovementEventKind Kind,
    AdmissionKey Key, string? Reason, StepAssessmentTiming? Assessment,
    NeighborDirection? OldFacing = null, NeighborDirection? NewFacing = null,
    MovementAction? OldAction = null, MovementAction? NewAction = null)
{
    private static readonly IReadOnlyList<string> PolicyProvenance = Array.AsReadOnly(new[] { "PROJECT_POLICY" });
    public IReadOnlyList<string> Provenance => Assessment?.Provenance ?? PolicyProvenance;
}

public sealed class MovementResult
{
    internal MovementResult(MovementStatus status, MovementState? state, MovementFailure? failure,
        ExactScalar? unconsumedTime, MovementEvent[] events, AdmissionKey[] consumed,
        StepAssessmentTiming[] unused, int processedBatches)
    {
        Status = status; State = state; Failure = failure; UnconsumedTime = unconsumedTime;
        Events = Array.AsReadOnly(events); ConsumedAssessmentKeys = Array.AsReadOnly(consumed);
        UnusedAssessments = Array.AsReadOnly(unused); ProcessedBatches = processedBatches;
    }
    public MovementStatus Status { get; }
    public MovementState? State { get; }
    public MovementFailure? Failure { get; }
    public SimulationTime? ProcessedWatermark => State?.Descriptor?.Time;
    public bool? IsWatermarkClosed => State?.Descriptor?.IsClosed;
    public ExactScalar? UnconsumedTime { get; }
    public int ProcessedBatches { get; }
    public IReadOnlyList<MovementEvent> Events { get; }
    public IReadOnlyList<AdmissionKey> ConsumedAssessmentKeys { get; }
    public IReadOnlyList<StepAssessmentTiming> UnusedAssessments { get; }
}

internal static class MovementGeometry
{
    internal static (int X, int Y) Delta(NeighborDirection direction) => direction switch
    {
        NeighborDirection.XMinusYMinus => (-1, -1), NeighborDirection.XMinus => (-1, 0),
        NeighborDirection.XMinusYPlus => (-1, 1), NeighborDirection.YPlus => (0, 1),
        NeighborDirection.XPlusYPlus => (1, 1), NeighborDirection.XPlus => (1, 0),
        NeighborDirection.XPlusYMinus => (1, -1), NeighborDirection.YMinus => (0, -1),
        _ => throw new ArgumentException("Invalid direction."),
    };
    internal static NeighborDirection Reverse(NeighborDirection direction) => (NeighborDirection)(((int)direction + 4) % 8);
    internal static CellCoord Neighbor(CellCoord cell, NeighborDirection direction)
    {
        var d = Delta(direction);
        return new(checked(cell.X + d.X), checked(cell.Y + d.Y));
    }
    internal static WorldAnchor Center(CellCoord cell) => new(ExactScalar.Integer(cell.X), ExactScalar.Integer(cell.Y));
    internal static WorldAnchor AnchorAt(ActiveSegment segment, SimulationTime time)
    {
        var elapsed = segment.ReleaseTime is { } r
            ? (r.Value - segment.StartTime.Value) - (time.Value - r.Value)
            : time.Value - segment.StartTime.Value;
        var progress = elapsed / segment.Duration.Value;
        var d = Delta(segment.Direction);
        return new(ExactScalar.Integer(segment.From.X) + progress * ExactScalar.Integer(d.X),
            ExactScalar.Integer(segment.From.Y) + progress * ExactScalar.Integer(d.Y));
    }
}
