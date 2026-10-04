using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One complete advance's full immutable Data evidence, correlated to runtime-generated keys.</summary>
public sealed record GridStepEvidence(AdmissionKey Key, CardinalTraversalAssessment Assessment);
public sealed record GridAdvance(MovementResult Movement, IReadOnlyList<GridStepEvidence> Evidence);

/// <summary>
/// Bounded viewer admission adapter. Every trial uses the authoritative runtime,
/// the same previous state and the same complete request. No integration math,
/// speculative later permits, I/O or mutable permission cache participates.
/// </summary>
public sealed class DiagnosticGridSession
{
    // Explicit diagnostic speed, unrelated to ART cadence or recovered original speed.
    public static StepDuration LiveDuration => StepDuration.Of(1, 2);
    public CardinalTraversalSnapshot Snapshot { get; }
    public MovementState State { get; private set; }
    public StepDuration Duration { get; }
    public GridAdvance? LastAdvance { get; private set; }

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

    // Deliberate semantic mapping, not an enum cast or compass/ART alias.
    private static CardinalTraversalDirection MapDirection(NeighborDirection direction) => direction switch
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

    /// <summary>Selection is preflighted before either state or evidence is published.</summary>
    public GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches,
        Action<MovementState, MovementResult>? validatePresentation = null)
    {
        var previous = State;
        var ownedBatches = batches.ToArray();
        var permits = new List<StepAssessmentTiming>();
        var evidence = new List<GridStepEvidence>();
        while (true)
        {
            var trial = MovementRuntime.AdvanceTo(previous, new(target, ownedBatches, permits.ToArray()));
            if (trial.Status != MovementStatus.Completed) return new(trial, Array.Empty<GridStepEvidence>());
            var missing = trial.Events.FirstOrDefault(e => e.Kind == MovementEventKind.StepRejected && e.Reason == "MissingAssessment");
            if (missing is null)
            {
                if (trial.UnusedAssessments.Count != 0 || trial.ConsumedAssessmentKeys.Count != evidence.Count)
                    throw new InvalidOperationException("GRID_REPLAY_PREFIX_INVALID");
                validatePresentation?.Invoke(trial.State!, trial);
                var result = new GridAdvance(trial, Array.AsReadOnly(evidence.ToArray()));
                State = trial.State!;
                LastAdvance = result;
                return result;
            }
            // Leave the entire request atomic on host replay capacity exhaustion.
            // A deliberately over-limit assessment request exercises the runtime's
            // own capacity rejection without committing the missing trial.
            if (permits.Count >= previous.Limits.MaxAssessments || permits.Count >= previous.Limits.MaxOpportunities)
            {
                var capped = new MovementLimits(previous.Limits.MaxBatches, previous.Limits.MaxInputs,
                    previous.Limits.MaxAssessments, previous.Limits.MaxOpportunities, previous.Limits.MaxEvents);
                // The runtime would otherwise stop after one missing opportunity;
                // the adapter must not accept that as a completed bridge advance.
                throw new GridReplayCapacityException(capped.MaxAssessments, capped.MaxOpportunities);
            }
            var key = missing.Key;
            if (evidence.Any(e => e.Key == key)) throw new InvalidOperationException("GRID_REPLAY_DUPLICATE_KEY");
            var assessment = Snapshot.Assess(Request(key.From, key.To, key.Direction));
            var coverage = assessment.Coverage switch
            {
                CardinalTraversalCoverage.Supported => AssessmentCoverage.Supported,
                CardinalTraversalCoverage.Unknown => AssessmentCoverage.Unknown,
                CardinalTraversalCoverage.Unsupported => AssessmentCoverage.Unsupported,
                _ => throw new InvalidOperationException("GRID_COVERAGE_INVALID")
            };
            AssessmentDisposition? disposition = assessment.Disposition switch
            {
                CardinalTraversalDisposition.Open => AssessmentDisposition.Open,
                CardinalTraversalDisposition.Blocked => AssessmentDisposition.Blocked,
                null => null,
                _ => throw new InvalidOperationException("GRID_DISPOSITION_INVALID")
            };
            evidence.Add(new(key, assessment));
            // Full components, ALL records and gaps remain in evidence. Bounded
            // Simulation codes reference that exact-key record, never truncate it.
            permits.Add(new(key, coverage, disposition,
                disposition == AssessmentDisposition.Open ? Duration : null,
                [assessment.ReasonCode], ["GRID_FULL_DATA_RECORD", assessment.StepEvidence.SelectedEvidenceSha256]));
        }
    }
}

public sealed class GridReplayCapacityException(int assessmentLimit, int opportunityLimit)
    : Exception("GRID_REPLAY_CAPACITY_EXCEEDED")
{
    public int AssessmentLimit { get; } = assessmentLimit;
    public int OpportunityLimit { get; } = opportunityLimit;
}
