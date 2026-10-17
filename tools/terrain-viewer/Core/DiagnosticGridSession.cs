using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One complete advance's full immutable Data evidence, correlated to runtime-generated keys.</summary>
public sealed record GridStepEvidence(AdmissionKey Key, CardinalTraversalAssessment Assessment);
/// <summary>One complete advance's whole-core Data evidence, correlated to runtime-generated keys. <see cref="Live"/> is the
/// verdict the step was admitted with when the live actor overlay changed the T0 assessment (ARC-WORLD-016).</summary>
public sealed record AreaGridStepEvidence(AdmissionKey Key, AreaStepAssessment Assessment)
{
    public AreaStepVerdict? Live { get; init; }
    /// <summary>ARC-COMBAT-004: the gait the step was admitted with (a window jump vaults whatever this says).</summary>
    public MovementGait Gait { get; init; }
}
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

    /// <summary>The exact production assessment movement admission uses for this directed step (pure, no state).</summary>
    public CardinalTraversalAssessment Assess(CellCoord from, CellCoord to, NeighborDirection direction) => Snapshot.Assess(Request(from, to, direction));

    /// <summary>Selection is preflighted before either state or evidence is published.</summary>
    public GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches,
        Action<MovementState, MovementResult>? validatePresentation = null)
    {
        var (result, evidence) = GridReplay.Run(State, target, batches, key =>
        {
            var assessment = Assess(key.From, key.To, key.Direction);
            return (GridReplay.Permit(key, assessment.Coverage, assessment.Disposition, assessment.ReasonCode,
                assessment.StepEvidence.SelectedEvidenceSha256, Duration), new GridStepEvidence(key, assessment));
        }, validatePresentation);
        if (result.Movement.Status != MovementStatus.Completed) return result;
        result = result with { Evidence = evidence };
        State = result.Movement.State!; LastAdvance = result;
        return result;
    }
}

/// <summary>One map's chart: the world cell at chart cell (0,0), the default start cell and the actor binding
/// name. The exterior keeps the retained base-sector origin (92928,82560); another map uses the origin of its
/// first core sector, so chart pixels stay small and chart cells stay inside the placement range.</summary>
public sealed record AreaChart(long OriginX, long OriginY, CellCoord Spawn, string BindingName)
{
    public static AreaChart Exterior { get; } = new(AreaScenePlan.CameraOriginX, AreaScenePlan.CameraOriginY, new(6, 1), "CRASH_SITE_CORE_BASE_CHART_CENTERS");
    /// <summary>The chart cells (both axes) the actor may stand on: the nine core sectors around the base sector
    /// by default; a whole map (ARC-RENDER-017) charts all of its sectors from 0.</summary>
    public int MinimumCell { get; init; } = AreaGridSession.MinimumChartCell;
    public int MaximumCell { get; init; } = AreaGridSession.MaximumChartCell;
    public bool Contains(CellCoord cell) => cell.X >= MinimumCell && cell.X <= MaximumCell && cell.Y >= MinimumCell && cell.Y <= MaximumCell;
    public const string ExteriorProfile = "arc-data-009-complete-file-effective-source-v1";

    /// <summary>The exterior chart for the exterior profile; otherwise the first core sector's origin with the given start.</summary>
    public static AreaChart For(Arcanum.Data.Area.AreaSnapshot snapshot, CellCoord? spawn = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Profile == ExteriorProfile) return Exterior;
        var core = snapshot.Sectors.Where(s => s.Scope == Arcanum.Data.Area.AreaScope.Core && s.Present).OrderBy(s => s.Coordinate.Y).ThenBy(s => s.Coordinate.X).FirstOrDefault()
            ?? throw new InvalidOperationException("AREA_CHART_CORE_REQUIRED");
        return new(core.Coordinate.OriginX, core.Coordinate.OriginY, spawn ?? new(0, 0), $"MAP_{snapshot.RuntimeMap}_CORE_CHART_CENTERS");
    }
    public AreaStepCell ToWorld(CellCoord cell) => new(checked(OriginX + cell.X), checked(OriginY + cell.Y));
    public CellCoord ToChart(AreaStepCell cell) => new(checked((int)(cell.X - OriginX)), checked((int)(cell.Y - OriginY)));
}

/// <summary>Whole-core T0 admission adapter. Cells are relative to the map's chart origin (the exterior keeps
/// (92928,82560), so negative coordinates address the western/northern core sectors).</summary>
public sealed class AreaGridSession : IGridSession
{
    public const long ChartOriginX = AreaScenePlan.CameraOriginX, ChartOriginY = AreaScenePlan.CameraOriginY;
    /// <summary>The CE <c>location_xy</c> origin of the AREA chart (ARC-RENDER-011). CE places cell (x,y) at
    /// <c>40(y-x-1)</c> and an object at that point plus its offset and <c>+40,+20</c>; the AREA plan projects terrain
    /// and bodies from <c>40(y-x)</c>, 40 px to the right. The actor placement, which restates <c>location_xy</c>,
    /// takes this origin so the actor stands on the same diamond centre as every AREA body.</summary>
    public const long LocationOriginX = 40, LocationOriginY = 0;
    /// <summary>Chart-relative cell range of the nine core sectors around the base sector (the default <see cref="AreaChart"/> range).</summary>
    public const int MinimumChartCell = -64, MaximumChartCell = 127;
    public static CellCoord Spawn => new(6, 1);
    public AreaStepSnapshot Snapshot { get; }
    public MovementState State { get; private set; }
    public StepDuration Duration { get; }
    public GridAdvance? LastAdvance { get; private set; }
    public long EvidenceEpoch => Snapshot.EvidenceEpoch;
    public AreaChart Chart { get; }
    /// <summary>ARC-WORLD-016: the live actors this session's steps are admitted against (read at every assessment); null is T0 alone.</summary>
    public Func<AreaOccupancy?>? Occupancy { get; set; }
    /// <summary>ARC-WORLD-020: per-gait step durations. Null keeps the fixed <see cref="Duration"/> for every step.</summary>
    public GaitTiming? Timing { get; init; }
    /// <summary>ARC-WORLD-020: the gait a step starts with, read when it is admitted; null walks.</summary>
    public Func<MovementGait>? Gait { get; set; }
    public const string PcActorId = "HUMAN_FIXED_DIAGNOSTIC", PcCapabilities = "ISOLATED_PC_HUMAN_FIXED_V1";

    public AreaGridSession(AreaStepSnapshot snapshot, SimulationTime initialTime, StepDuration? duration = null, MovementLimits? limits = null,
        AreaChart? chart = null, CellCoord? start = null, NeighborDirection facing = NeighborDirection.XPlus,
        string actorId = PcActorId, string capabilities = PcCapabilities)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot; Chart = chart ?? AreaChart.Exterior;
        Duration = duration ?? DiagnosticGridSession.LiveDuration;
        if (!Duration.IsValid) throw new ArgumentException("GRID_DURATION_INVALID");
        var origin = start ?? Chart.Spawn;
        if (!Chart.Contains(origin) || !snapshot.IsAdmitted(Chart.ToWorld(origin)))
            throw new ArgumentException("GRID_SPAWN_NOT_ADMITTED");
        // The context fingerprint is the declared context, the same for every request of this snapshot.
        var probe = new CellCoord(origin.X + 1, origin.Y);
        var context = Snapshot.Assess(Snapshot.Request(Chart.ToWorld(origin), Chart.ToWorld(probe), CardinalTraversalDirection.XPlus)).ContextFingerprint;
        var binding = new ActorBinding(Chart.BindingName, actorId,
            context, capabilities, "KIND15_ART2_FLAGS0_GROUND_NONPOLYMORPH_REGISTRY0",
            snapshot.ProfileId, snapshot.EvidenceSha256, snapshot.EvidenceEpoch);
        State = MovementState.CreateSettled(binding, origin, initialTime, facing, limits);
    }

    /// <summary>The exterior chart's world cell. Use <see cref="AreaChart.ToWorld"/> for another map.</summary>
    public static AreaStepCell Cell(CellCoord cell) => new(ChartOriginX + cell.X, ChartOriginY + cell.Y);

    /// <summary>The exact production assessment movement admission uses for this directed step (pure, no state).</summary>
    public AreaStepAssessment Assess(CellCoord from, CellCoord to, NeighborDirection direction)
        => Snapshot.Assess(Snapshot.Request(Chart.ToWorld(from), Chart.ToWorld(to), GridReplay.MapDirection(direction)));

    public GridAdvance Advance(SimulationTime target, ReadOnlySpan<InputBatch> batches,
        Action<MovementState, MovementResult>? validatePresentation = null)
    {
        var occupancy = Occupancy?.Invoke();
        var (result, evidence) = GridReplay.Run(State, target, batches, key =>
        {
            var assessment = Assess(key.From, key.To, key.Direction);
            var live = AreaOccupancyRules.Apply(assessment, key.From, key.Direction, occupancy);
            var gait = Gait?.Invoke() ?? MovementGait.Walk;
            // ARC-WORLD-021: an Open window jump is the vault (its own time and ART), whatever the gait.
            var vault = Timing?.Vault is { } vaultTime && assessment.WindowJump is not null ? vaultTime : (StepDuration?)null;
            var duration = vault ?? Timing?.For(gait, key.Direction) ?? Duration;
            return (GridReplay.Permit(key, live.Coverage, live.Disposition, live.ReasonCode, assessment.EvidenceSha256, duration,
                    Timing is null ? null : vault is not null ? GaitTiming.VaultProvenance : GaitTiming.Provenance(gait)),
                new AreaGridStepEvidence(key, assessment) { Live = live.Overlaid ? live : null, Gait = gait });
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
        string reason, string evidenceSha256, StepDuration duration, string? gait = null)
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
            [reason], gait is null ? ["GRID_FULL_DATA_RECORD", evidenceSha256] : ["GRID_FULL_DATA_RECORD", evidenceSha256, gait]);
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
