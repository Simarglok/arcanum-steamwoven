using System.Diagnostics.CodeAnalysis;

namespace Arcanum.Simulation.Movement;

internal sealed class MovementRejectedException(MovementFailure failure) : Exception
{
    internal MovementFailure Failure { get; } = failure;
}

internal static class MovementValidation
{
    internal static void Require([DoesNotReturnIf(false)] bool condition, MovementFailureKind kind, int? batch = null,
        int? assessment = null, AdmissionKey? key = null)
    {
        if (!condition) throw new MovementRejectedException(new(kind, batch, assessment, key));
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    internal static bool Binding(ActorBinding b) => b.EvidenceEpoch >= 0 && Text(b.ChartId) && Text(b.ActorId) &&
        Text(b.ContextId) && Text(b.CapabilitiesProfileId) && Text(b.CapabilitiesFingerprint) &&
        Text(b.AssessmentProfileId) && Text(b.InputFingerprint);
    internal static bool Intent(ActorIntent intent) => intent.Active
        ? intent.Direction is { } d && Enum.IsDefined(d) : intent.Direction is null;
    internal static bool ValidKey(InputKey key) => key.Time.IsValid && key.Sequence >= 0;
    internal static int Compare(InputKey a, InputKey b)
    {
        var t = a.Time.Value.CompareTo(b.Time.Value);
        return t != 0 ? t : a.Sequence.CompareTo(b.Sequence);
    }
    internal static bool KeyShape(AdmissionKey k) => Binding(k.Binding) && Enum.IsDefined(k.Direction) &&
        MovementGeometry.Neighbor(k.From, k.Direction) == k.To && k.StartTime.IsValid && k.OpportunityId >= 1 &&
        k.ProposedStepId >= 1 && ValidKey(k.MaterialIntentKey) &&
        k.MaterialIntentKey.Time.Value <= k.StartTime.Value && k.EvidenceRevision >= 0;
    internal static bool AssessmentShape(StepAssessmentTiming a)
    {
        if (!KeyShape(a.Key) || !Enum.IsDefined(a.Coverage) ||
            (a.Disposition is { } disposition && !Enum.IsDefined(disposition)) ||
            a.Reasons.Count is < 1 or > 16 || a.Provenance.Count is < 1 or > 16 ||
            !a.Reasons.All(Text) || !a.Provenance.All(Text) || (a.Duration is { } duration && !duration.IsValid)) return false;
        var canonical = a.Coverage == AssessmentCoverage.Supported
            ? a.Disposition is not null : a.Disposition is null;
        if (!canonical) return false;
        if (a.Disposition == AssessmentDisposition.Open)
        {
            if (a.Duration is not { IsValid: true }) return false;
            var segment = new ActiveSegment(a);
            return segment.StartTime.Value < segment.EntryTime.Value && segment.EntryTime.Value < segment.ArrivalTime.Value;
        }
        return true;
    }

    internal static bool IsValidState(MovementState? state)
    {
        try { ValidateState(state); return true; }
        catch (MovementRejectedException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
        catch (DivideByZeroException) { return false; }
    }
    internal static void ValidateState(MovementState? state)
    {
        Require(state is not null, MovementFailureKind.InvalidState);
        Require(state!.Descriptor is not null, MovementFailureKind.InvalidState);
        var s = state!.Descriptor;
        Require(Binding(s.Binding) && s.Limits is not null && s.Time.IsValid && s.Anchor.U.IsValid && s.Anchor.V.IsValid &&
            Enum.IsDefined(s.Phase) && Enum.IsDefined(s.Facing) && Enum.IsDefined(s.Action) && Intent(s.LatestIntent) &&
            s.EvidenceRevision >= 0 && s.NextOpportunityId >= 1 && s.NextStepId >= 1 && s.NextEventOrdinal >= 1 &&
            s.NextStepId <= s.NextOpportunityId, MovementFailureKind.InvalidState);
        foreach (var key in new[] { s.LatestInputKey, s.MaterialIntentKey })
            Require(key is null || (ValidKey(key.Value) && key.Value.Time.Value <= s.Time.Value), MovementFailureKind.InvalidState);
        Require(s.MaterialIntentKey is null || (s.LatestInputKey is { } latest &&
            Compare(s.MaterialIntentKey.Value, latest) <= 0), MovementFailureKind.InvalidState);
        Require(!s.LatestIntent.Active || s.MaterialIntentKey is not null, MovementFailureKind.InvalidState);
        Require((s.LastOpportunity is null) == (s.LastOpportunityDisposition is null), MovementFailureKind.InvalidState);
        if (s.LastOpportunity is { } last)
            Require(KeyShape(last) && last.Binding == s.Binding && last.OpportunityId == s.NextOpportunityId - 1 &&
                last.ProposedStepId <= s.NextStepId && last.StartTime.Value <= s.Time.Value &&
                last.EvidenceRevision <= s.EvidenceRevision &&
                s.LastOpportunityDisposition is "Admitted" or "MissingAssessment" or "Denied", MovementFailureKind.InvalidState);
        else Require(s.NextOpportunityId == 1 && s.NextStepId == 1, MovementFailureKind.InvalidState);

        if (s.Phase == MovementPhase.Settled)
        {
            Require(s.Segment is null && s.Anchor == MovementGeometry.Center(s.CommittedCell) &&
                s.Action == MovementAction.Idle, MovementFailureKind.InvalidState);
            return;
        }
        Require(s.Segment is not null && s.Action == MovementAction.Walk, MovementFailureKind.InvalidState);
        var segment = s.Segment!;
        Require(segment.Assessment is not null, MovementFailureKind.InvalidState);
        var k = segment.Key;
        Require(AssessmentShape(segment.Assessment) && segment.Assessment.Coverage == AssessmentCoverage.Supported &&
            segment.Assessment.Disposition == AssessmentDisposition.Open && k.Binding == s.Binding &&
            k.ProposedStepId == s.NextStepId - 1 && k.OpportunityId < s.NextOpportunityId &&
            k.EvidenceRevision <= s.EvidenceRevision && segment.StartTime.Value <= s.Time.Value &&
            s.Time.Value < segment.EndTime.Value && s.LastOpportunity == k && s.LastOpportunityDisposition == "Admitted" &&
            s.MaterialIntentKey is { } material && Compare(k.MaterialIntentKey, material) <= 0,
            MovementFailureKind.InvalidState);
        Require(s.Anchor == MovementGeometry.AnchorAt(segment, s.Time), MovementFailureKind.InvalidState);
        if (s.Phase == MovementPhase.Returning)
        {
            Require(segment.ReleaseTime is { } r && r.IsValid && segment.StartTime.Value < r.Value &&
                r.Value < segment.EntryTime.Value && r.Value <= s.Time.Value && s.CommittedCell == segment.From &&
                s.Facing == MovementGeometry.Reverse(segment.Direction), MovementFailureKind.InvalidState);
        }
        else
        {
            Require(segment.ReleaseTime is null && s.Facing == segment.Direction, MovementFailureKind.InvalidState);
            Require(s.Phase == MovementPhase.OutboundBeforeEntry
                ? s.Time.Value < segment.EntryTime.Value && s.CommittedCell == segment.From
                : s.Time.Value >= segment.EntryTime.Value && s.CommittedCell == segment.To,
                MovementFailureKind.InvalidState);
        }
    }

    internal static void ValidateRequest(MovementState state, MovementRequest? request)
    {
        Require(request is not null && request.TargetTime.IsValid && request.TargetTime.Value >= state.Time.Value,
            MovementFailureKind.InvalidTarget);
        var r = request!;
        Require(r.Batches.Count <= state.Limits.MaxBatches && r.Assessments.Count <= state.Limits.MaxAssessments &&
            r.Batches.Sum(b => b.Entries.Count) <= state.Limits.MaxInputs, MovementFailureKind.CapacityExceeded);
        SimulationTime? previous = null;
        for (var i = 0; i < r.Batches.Count; i++)
        {
            var b = r.Batches[i];
            Require(b.Time.IsValid && b.Entries.Count > 0 && b.Time.Value <= r.TargetTime.Value &&
                (state.IsClosed ? b.Time.Value > state.Time.Value : b.Time.Value >= state.Time.Value) &&
                (previous is null || b.Time.Value > previous.Value.Value), MovementFailureKind.InvalidInput, batch: i);
            long sequence = -1;
            foreach (var input in b.Entries)
            {
                Require(input.Sequence > sequence && Enum.IsDefined(input.Kind) && Intent(input.Intent) &&
                    (input.Kind == MovementInputKind.SetActorIntent ? input.EvidenceRevision == 0 :
                        !input.Intent.Active && input.EvidenceRevision >= 0), MovementFailureKind.InvalidInput, batch: i);
                var key = new InputKey(b.Time, input.Sequence);
                Require(state.LatestInputKey is null || Compare(key, state.LatestInputKey.Value) > 0,
                    MovementFailureKind.InvalidInput, batch: i);
                sequence = input.Sequence;
            }
            previous = b.Time;
        }
        var opportunities = new HashSet<long>();
        for (var i = 0; i < r.Assessments.Count; i++)
        {
            var a = r.Assessments[i]; var k = a.Key;
            Require(AssessmentShape(a), MovementFailureKind.InvalidAssessment, assessment: i, key: k);
            Require(k.Binding == state.Binding, MovementFailureKind.IdentityMismatch, assessment: i, key: k);
            Require(k.OpportunityId >= state.NextOpportunityId && k.ProposedStepId >= state.NextStepId &&
                opportunities.Add(k.OpportunityId) && k.StartTime.Value <= r.TargetTime.Value &&
                (state.IsClosed ? k.StartTime.Value > state.Time.Value : k.StartTime.Value >= state.Time.Value),
                MovementFailureKind.InvalidAssessment, assessment: i, key: k);
        }
    }
}
