using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One complete advance's full immutable Data evidence, correlated to runtime-generated keys.</summary>
public sealed record GridStepEvidence(AdmissionKey Key, CardinalTraversalAssessment Assessment);
/// <summary>One complete advance's whole-core Data evidence, correlated to runtime-generated keys.</summary>
public sealed record AreaGridStepEvidence(AdmissionKey Key, AreaStepAssessment Assessment);
public sealed record GridAdvance(MovementResult Movement, IReadOnlyList<GridStepEvidence> Evidence)
{
    public IReadOnlyList<AreaGridStepEvidence> AreaEvidence { get; init; } = Array.Empty<AreaGridStepEvidence>();
}

/// <summary>An authoritative grid session the viewer actor can drive, independent of its Data profile.</summary>
public interface IGridSession
{
    MovementState State { get; }
    StepDuration Duration { get; }
    GridAdvance? LastAdvance { get; }
    long EvidenceEpoch { get; }
    GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches, Action<MovementState, MovementResult>? validatePresentation = null);
}

/// <summary>
/// Bounded viewer admission adapter. Every trial uses the authoritative runtime,
/// the same previous state and the same complete request. No integration math,
/// speculative later permits, I/O or mutable permission cache participates.
/// </summary>
public sealed class DiagnosticGridSession : IGridSession
{
    // Explicit diagnostic speed, unrelated to ART cadence or recovered original speed.
    public static StepDuration LiveDuration => StepDuration.Of(1, 2);
    public CardinalTraversalSnapshot Snapshot { get; }
    public MovementState State { get; private set; }
    public StepDuration Duration { get; }
    public GridAdvance? LastAdvance { get; private set; }
    public long EvidenceEpoch => Snapshot.EvidenceEpoch;

    public DiagnosticGridSession(CardinalTraversalSnapshot snapshot, SimulationTime initialTime,
        StepDuration? duration = null, MovementLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        Duration = duration ?? LiveDuration;
        if (!Duration.IsValid) throw new ArgumentException("GRID_DURATION_INVALID");
        var context = snapshot.Assess(Request(new(6, 1), new(7, 1), NeighborDirection.XPlus)).StepEvidence.ContextFingerprint;
        var binding = new ActorBinding("SECTOR_1452_1290_LOCAL_CENTERS", "HUMAN_FIXED_DIAGNOSTIC",
            context, "ISOLATED_PC_HUMAN_FIXED_V1", "KIND15_ART2_FLAGS0_GROUND_NONPOLYMORPH_REGISTRY0",
            snapshot.ProfileId, snapshot.InputIdentity.SelectedEvidenceSha256, snapshot.EvidenceEpoch);
        State = MovementState.CreateSettled(binding, new(6, 1), initialTime, NeighborDirection.XPlus, limits);
    }

    private CardinalTraversalRequest Request(CellCoord from, CellCoord to, NeighborDirection direction) => new(
        Snapshot.ProfileId, Snapshot.InputIdentity, Snapshot.EvidenceEpoch,
        Snapshot.InputIdentity.Sector.SectorX, Snapshot.InputIdentity.Sector.SectorY,
        new(from.X, from.Y), new(to.X, to.Y), MapDirection(direction), Snapshot.DeclaredContext);

    private static CardinalTraversalDirection MapDirection(NeighborDirection direction) => GridReplay.MapDirection(direction);

    /// <summary>Selection is preflighted before either state or evidence is published.</summary>
    public GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches,
        Action<MovementState, MovementResult>? validatePresentation = null)
    {
        var (result, evidence) = GridReplay.Run(State, target, batches, key =>
        {
            var assessment = Snapshot.Assess(Request(key.From, key.To, key.Direction));
            return (GridReplay.Permit(key, assessment.Coverage, assessment.Disposition, assessment.ReasonCode,
                assessment.StepEvidence.SelectedEvidenceSha256, Duration), new GridStepEvidence(key, assessment));
        }, validatePresentation);
        if (result.Movement.Status != MovementStatus.Completed) return result;
        result = result with { Evidence = evidence };
        State = result.Movement.State!; LastAdvance = result;
        return result;
    }
}

/// <summary>Whole-core T0 admission adapter. Cells are base-sector-relative to the retained chart origin
/// (92928,82560), so negative coordinates address the western/northern core sectors.</summary>
public sealed class AreaGridSession : IGridSession
{
    public const long ChartOriginX = AreaScenePlan.CameraOriginX, ChartOriginY = AreaScenePlan.CameraOriginY;
    /// <summary>Chart-relative cell range of the nine core sectors around the base sector.</summary>
    public const int MinimumChartCell = -64, MaximumChartCell = 127;
    public static CellCoord Spawn => new(6, 1);
    public AreaStepSnapshot Snapshot { get; }
    public MovementState State { get; private set; }
    public StepDuration Duration { get; }
    public GridAdvance? LastAdvance { get; private set; }
    public long EvidenceEpoch => Snapshot.EvidenceEpoch;

    public AreaGridSession(AreaStepSnapshot snapshot, SimulationTime initialTime, StepDuration? duration = null, MovementLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        Duration = duration ?? DiagnosticGridSession.LiveDuration;
        if (!Duration.IsValid) throw new ArgumentException("GRID_DURATION_INVALID");
        if (!snapshot.IsAdmitted(Cell(Spawn))) throw new ArgumentException("GRID_SPAWN_NOT_ADMITTED");
        var context = Snapshot.Assess(Snapshot.Request(Cell(Spawn), Cell(new(7, 1)), CardinalTraversalDirection.XPlus)).ContextFingerprint;
        var binding = new ActorBinding("CRASH_SITE_CORE_BASE_CHART_CENTERS", "HUMAN_FIXED_DIAGNOSTIC",
            context, "ISOLATED_PC_HUMAN_FIXED_V1", "KIND15_ART2_FLAGS0_GROUND_NONPOLYMORPH_REGISTRY0",
            snapshot.ProfileId, snapshot.EvidenceSha256, snapshot.EvidenceEpoch);
        State = MovementState.CreateSettled(binding, Spawn, initialTime, NeighborDirection.XPlus, limits);
    }

    public static AreaStepCell Cell(CellCoord cell) => new(ChartOriginX + cell.X, ChartOriginY + cell.Y);

    public GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches,
        Action<MovementState, MovementResult>? validatePresentation = null)
    {
        var (result, evidence) = GridReplay.Run(State, target, batches, key =>
        {
            var assessment = Snapshot.Assess(Snapshot.Request(Cell(key.From), Cell(key.To), GridReplay.MapDirection(key.Direction)));
            return (GridReplay.Permit(key, assessment.Coverage, assessment.Disposition, assessment.ReasonCode, assessment.EvidenceSha256, Duration),
                new AreaGridStepEvidence(key, assessment));
        }, validatePresentation);
        if (result.Movement.Status != MovementStatus.Completed) return result;
        result = new GridAdvance(result.Movement, Array.Empty<GridStepEvidence>()) { AreaEvidence = evidence };
        State = result.Movement.State!; LastAdvance = result;
        return result;
    }
}

/// <summary>
/// Shared replay: every trial uses the authoritative runtime, the same previous state and the same complete
/// request. No integration math, speculative later permits, I/O or mutable permission cache participates.
/// </summary>
internal static class GridReplay
{
    internal static (GridAdvance Result, IReadOnlyList<T> Evidence) Run<T>(MovementState previous, SimulationTime target,
        ReadOnlySpan<InputBatch> batches, Func<AdmissionKey, (StepAssessmentTiming Permit, T Evidence)> assess,
        Action<MovementState, MovementResult>? validatePresentation)
    {
        var ownedBatches = batches.ToArray();
        var permits = new List<StepAssessmentTiming>();
        var evidence = new List<(AdmissionKey Key, T Evidence)>();
        while (true)
        {
            var trial = MovementRuntime.AdvanceTo(previous, new(target, ownedBatches, permits.ToArray()));
            if (trial.Status != MovementStatus.Completed) return (new(trial, Array.Empty<GridStepEvidence>()), Array.Empty<T>());
            var missing = trial.Events.FirstOrDefault(e => e.Kind == MovementEventKind.StepRejected && e.Reason == "MissingAssessment");
            if (missing is null)
            {
                if (trial.UnusedAssessments.Count != 0 || trial.ConsumedAssessmentKeys.Count != evidence.Count)
                    throw new InvalidOperationException("GRID_REPLAY_PREFIX_INVALID");
                validatePresentation?.Invoke(trial.State!, trial);
                return (new(trial, Array.Empty<GridStepEvidence>()), Array.AsReadOnly(evidence.Select(e => e.Evidence).ToArray()));
            }
            // Leave the entire request atomic on host replay capacity exhaustion.
            // A deliberately over-limit assessment request exercises the runtime's
            // own capacity rejection without committing the missing trial.
            if (permits.Count >= previous.Limits.MaxAssessments || permits.Count >= previous.Limits.MaxOpportunities)
                // The runtime would otherwise stop after one missing opportunity;
                // the adapter must not accept that as a completed bridge advance.
                throw new GridReplayCapacityException(previous.Limits.MaxAssessments, previous.Limits.MaxOpportunities);
            var key = missing.Key;
            if (evidence.Any(e => e.Key == key)) throw new InvalidOperationException("GRID_REPLAY_DUPLICATE_KEY");
            var (permit, record) = assess(key);
            evidence.Add((key, record));
            permits.Add(permit);
        }
    }

    // Full components, ALL records and gaps remain in evidence. Bounded Simulation
    // codes reference that exact-key record, never truncate it.
    internal static StepAssessmentTiming Permit(AdmissionKey key, CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition,
        string reason, string evidenceSha256, StepDuration duration)
    {
        var mappedCoverage = coverage switch
        {
            CardinalTraversalCoverage.Supported => AssessmentCoverage.Supported,
            CardinalTraversalCoverage.Unknown => AssessmentCoverage.Unknown,
            CardinalTraversalCoverage.Unsupported => AssessmentCoverage.Unsupported,
            _ => throw new InvalidOperationException("GRID_COVERAGE_INVALID")
        };
        AssessmentDisposition? mappedDisposition = disposition switch
        {
            CardinalTraversalDisposition.Open => AssessmentDisposition.Open,
            CardinalTraversalDisposition.Blocked => AssessmentDisposition.Blocked,
            null => null,
            _ => throw new InvalidOperationException("GRID_DISPOSITION_INVALID")
        };
        return new(key, mappedCoverage, mappedDisposition, mappedDisposition == AssessmentDisposition.Open ? duration : null,
            [reason], ["GRID_FULL_DATA_RECORD", evidenceSha256]);
    }

    // Deliberate semantic mapping, not an enum cast or compass/ART alias.
    internal static CardinalTraversalDirection MapDirection(NeighborDirection direction) => direction switch
    {
        NeighborDirection.XMinusYMinus => CardinalTraversalDirection.XMinusYMinus,
        NeighborDirection.XMinus => CardinalTraversalDirection.XMinus,
        NeighborDirection.XMinusYPlus => CardinalTraversalDirection.XMinusYPlus,
        NeighborDirection.YPlus => CardinalTraversalDirection.YPlus,
        NeighborDirection.XPlusYPlus => CardinalTraversalDirection.XPlusYPlus,
        NeighborDirection.XPlus => CardinalTraversalDirection.XPlus,
        NeighborDirection.XPlusYMinus => CardinalTraversalDirection.XPlusYMinus,
        NeighborDirection.YMinus => CardinalTraversalDirection.YMinus,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
}

public sealed class GridReplayCapacityException(int assessmentLimit, int opportunityLimit)
    : Exception("GRID_REPLAY_CAPACITY_EXCEEDED")
{
    public int AssessmentLimit { get; } = assessmentLimit;
    public int OpportunityLimit { get; } = opportunityLimit;
}
