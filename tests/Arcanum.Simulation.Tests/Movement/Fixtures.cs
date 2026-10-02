using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

internal static class Fixtures
{
    internal static ActorBinding Binding => new("SYNTHETIC_CHART", "ACTOR", "FIXTURE_CONTEXT", "CAP", "CAP_FP", "PROFILE", "INPUT_FP", 7);
    internal static SimulationTime T(long n, long d = 1) => SimulationTime.At(n, d);
    internal static ExactScalar Q(long n, long d = 1) => ExactScalar.Create(n, d);
    internal static MovementState Initial(CellCoord cell = default, NeighborDirection facing = NeighborDirection.XPlus, MovementLimits? limits = null) =>
        MovementState.CreateSettled(Binding, cell, T(0), facing, limits);
    internal static MovementInput Press(long sequence, NeighborDirection direction = NeighborDirection.XPlus) => MovementInput.Set(sequence, ActorIntent.Toward(direction));
    internal static MovementInput Neutral(long sequence) => MovementInput.Set(sequence, ActorIntent.Neutral);
    internal static InputBatch Batch(long time, params MovementInput[] inputs) => new(T(time), inputs);
    internal static AdmissionKey Key(long opportunity = 1, long step = 1, CellCoord from = default, CellCoord? to = null,
        NeighborDirection direction = NeighborDirection.XPlus, long start = 0, long materialTime = 0, long materialSequence = 0, long revision = 0) =>
        new(Binding, from, to ?? new CellCoord(1, 0), direction, T(start), opportunity, step, new(T(materialTime), materialSequence), revision);
    internal static StepAssessmentTiming Open(AdmissionKey key, StepDuration? duration = null) =>
        new(key, AssessmentCoverage.Supported, AssessmentDisposition.Open, duration ?? StepDuration.Of(8), ["SYNTHETIC_OPEN"], ["SYNTHETIC_POLICY"]);
    internal static MovementRequest Request(long target, InputBatch[]? batches = null, StepAssessmentTiming[]? assessments = null) =>
        new(T(target), batches ?? [], assessments ?? []);
    internal static MovementResult Complete(MovementState state, MovementRequest request)
    {
        var r = MovementRuntime.AdvanceTo(state, request);
        Assert.AreEqual(MovementStatus.Completed, r.Status, r.Failure?.ToString());
        Assert.IsNotNull(r.State);
        Assert.AreEqual(request.TargetTime, r.ProcessedWatermark);
        Assert.IsTrue(r.IsWatermarkClosed);
        Assert.AreEqual(ExactScalar.Zero, r.UnconsumedTime);
        Assert.AreEqual(request.Batches.Count, r.ProcessedBatches);
        return r;
    }
    internal static void Invalid(MovementState? previous, MovementRequest request, MovementFailureKind? kind = null)
    {
        var r = MovementRuntime.AdvanceTo(previous, request);
        Assert.AreEqual(MovementStatus.InvalidRequest, r.Status);
        Assert.AreSame(previous, r.State);
        Assert.AreEqual(0, r.Events.Count);
        Assert.AreEqual(0, r.ConsumedAssessmentKeys.Count);
        Assert.AreEqual(0, r.ProcessedBatches);
        Assert.AreEqual(request.Assessments.Count, r.UnusedAssessments.Count);
        Assert.AreEqual(previous?.Descriptor?.Time, r.ProcessedWatermark);
        Assert.AreEqual(previous?.Descriptor?.IsClosed, r.IsWatermarkClosed);
        if (kind is { } expected) Assert.AreEqual(expected, r.Failure!.Kind);
    }
    // Literal transcript tokens: timestamp:kind:assessmentIndex[:old>new].
    // This only materializes the expected records; it computes no motion/admission/boundaries.
    internal static void Transcript(IReadOnlyList<MovementEvent> events, StepAssessmentTiming[] records, string literal, long firstOrdinal = 1)
    {
        var tokens = literal.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(tokens.Length, events.Count, string.Join(" ", events.Select(e => $"{e.Time.Value}:{e.Kind}")));
        for (var i = 0; i < tokens.Length; i++)
        {
            var parts = tokens[i].Split(':');
            var a = records[int.Parse(parts[2]) - 1];
            var kind = parts[1] switch
            {
                "S" => MovementEventKind.StepStarted, "C" => MovementEventKind.CellCommitted,
                "R" => MovementEventKind.CenterArrived, "X" => MovementEventKind.StepCancelled,
                "F" => MovementEventKind.FacingChanged, "A" => MovementEventKind.ActionChanged,
                "D" => MovementEventKind.StepRejected, _ => throw new AssertFailedException("Unknown literal token"),
            };
            NeighborDirection? oldF = null, newF = null;
            MovementAction? oldA = null, newA = null;
            if (kind == MovementEventKind.FacingChanged)
            {
                var change = parts[3].Split('>');
                oldF = Enum.Parse<NeighborDirection>(change[0]); newF = Enum.Parse<NeighborDirection>(change[1]);
            }
            if (kind == MovementEventKind.ActionChanged)
            {
                var change = parts[3].Split('>');
                oldA = Enum.Parse<MovementAction>(change[0]); newA = Enum.Parse<MovementAction>(change[1]);
            }
            var expected = new MovementEvent(T(long.Parse(parts[0])), firstOrdinal + i, kind, a.Key,
                kind == MovementEventKind.StepRejected ? "CoverageDenied" : null, a, oldF, newF, oldA, newA);
            Assert.AreEqual(expected, events[i], $"token {tokens[i]}");
            CollectionAssert.AreEqual(a.Provenance.ToArray(), events[i].Provenance.ToArray());
        }
    }
}
