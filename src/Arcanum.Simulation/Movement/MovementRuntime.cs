namespace Arcanum.Simulation.Movement;

/// <summary>Pure, bounded, complete-request movement. No hidden services or frame clock.</summary>
public static class MovementRuntime
{
    public static MovementSnapshot QuerySnapshot(MovementState state)
    {
        if (!MovementValidation.IsValidState(state)) throw new ArgumentException("Invalid state.", nameof(state));
        return new(state.Binding, state.Time, state.IsClosed, state.CommittedCell, state.Anchor, state.Phase,
            state.LatestIntent, state.LatestInputKey, state.MaterialIntentKey, state.Facing, state.Action, state.Segment,
            state.EvidenceRevision, state.NextOpportunityId, state.NextStepId, state.NextEventOrdinal,
            state.LastOpportunity, state.LastOpportunityDisposition);
    }

    public static MovementResult AdvanceTo(MovementState? previousState, MovementRequest? request)
    {
        if (!MovementValidation.IsValidState(previousState))
            return Invalid(previousState, request, new(MovementFailureKind.InvalidState));
        try
        {
            MovementValidation.ValidateRequest(previousState!, request);
            return new Candidate(previousState!, request!).Run();
        }
        catch (MovementRejectedException e) { return Invalid(previousState, request, e.Failure); }
        catch (OverflowException) { return Invalid(previousState, request, new(MovementFailureKind.NumericOverflow)); }
        catch (ArgumentException) { return Invalid(previousState, request, new(MovementFailureKind.InvalidAssessment)); }
        catch (DivideByZeroException) { return Invalid(previousState, request, new(MovementFailureKind.InvalidAssessment)); }
    }

    private static MovementResult Invalid(MovementState? previous, MovementRequest? request, MovementFailure failure)
    {
        ExactScalar? residual = null;
        if (previous?.Descriptor is { Time.IsValid: true } descriptor && request?.TargetTime.IsValid == true &&
            request.TargetTime.Value >= descriptor.Time.Value)
        {
            try { residual = request.TargetTime.Value - descriptor.Time.Value; }
            catch (OverflowException) { /* Explicitly unavailable, never a fabricated remainder. */ }
        }
        return new(MovementStatus.InvalidRequest, previous, failure, residual, [], [],
            request?.Assessments.ToArray() ?? [], 0);
    }

    private sealed class Candidate
    {
        private MovementDescriptor _s;
        private readonly MovementRequest _request;
        private readonly Dictionary<long, StepAssessmentTiming> _byOpportunity;
        private readonly Dictionary<SimulationTime, StepAssessmentTiming> _byTime;
        private readonly List<MovementEvent> _events = [];
        private readonly List<AdmissionKey> _consumed = [];
        private int _opportunities;
        private int _batchIndex;
        // Counts entry/arrival work for one carried segment plus at most one segment per supplied assessment.
        private int _segmentWork;
        private AdmissionKey? _eventContext;
        private StepAssessmentTiming? _eventAssessment;

        internal Candidate(MovementState previous, MovementRequest request)
        {
            _s = previous.Descriptor;
            _request = request;
            _byOpportunity = request.Assessments.ToDictionary(a => a.Key.OpportunityId);
            _byTime = new();
            foreach (var a in request.Assessments)
                MovementValidation.Require(_byTime.TryAdd(a.Key.StartTime, a), MovementFailureKind.InvalidAssessment, key: a.Key);
        }

        internal MovementResult Run()
        {
            while (true)
            {
                var time = _request.TargetTime;
                if (_batchIndex < _request.Batches.Count && _request.Batches[_batchIndex].Time.Value < time.Value)
                    time = _request.Batches[_batchIndex].Time;
                if (_s.Segment is { } segment)
                {
                    var boundary = _s.Phase == MovementPhase.OutboundBeforeEntry ? segment.EntryTime : segment.EndTime;
                    if (boundary.Value < time.Value) time = boundary;
                }
                _s = _s with { Time = time };
                var arrived = AdvanceMotion();
                var materialAtSettlement = false;
                var reassessment = false;
                if (_batchIndex < _request.Batches.Count && _request.Batches[_batchIndex].Time == time)
                {
                    foreach (var input in _request.Batches[_batchIndex].Entries)
                    {
                        var key = new InputKey(time, input.Sequence);
                        if (input.Kind == MovementInputKind.RequestReassessment)
                        {
                            MovementValidation.Require(_s.Phase == MovementPhase.Settled,
                                MovementFailureKind.WrongReassessmentPhase, batch: _batchIndex);
                            MovementValidation.Require(input.EvidenceRevision > _s.EvidenceRevision,
                                MovementFailureKind.InvalidRevision, batch: _batchIndex);
                            _s = _s with { EvidenceRevision = input.EvidenceRevision, LatestInputKey = key };
                            reassessment = true;
                            continue;
                        }
                        var changed = input.Intent != _s.LatestIntent;
                        materialAtSettlement |= changed && _s.Phase == MovementPhase.Settled;
                        _s = _s with { LatestIntent = input.Intent, LatestInputKey = key,
                            MaterialIntentKey = changed ? key : _s.MaterialIntentKey };
                        if (!input.Intent.Active && _s.Phase == MovementPhase.OutboundBeforeEntry)
                            arrived |= Cancel();
                    }
                    _batchIndex++;
                }
                if (_s.Phase == MovementPhase.Settled && _s.LatestIntent.Active &&
                    (arrived || materialAtSettlement || reassessment)) StartOpportunity();
                ChangeAction(_s.Phase == MovementPhase.Settled ? MovementAction.Idle : MovementAction.Walk);
                if (time == _request.TargetTime) break;
            }
            _s = _s with { IsClosed = true };
            var state = MovementState.FromDescriptor(_s);
            MovementValidation.Require(MovementValidation.IsValidState(state), MovementFailureKind.InvalidState);
            var consumed = _consumed.ToHashSet();
            return new(MovementStatus.Completed, state, null, ExactScalar.Zero, _events.ToArray(),
                _consumed.ToArray(), _request.Assessments.Where(a => !consumed.Contains(a.Key)).ToArray(), _batchIndex);
        }

        private void Work()
        {
            MovementValidation.Require(++_segmentWork <= 2 * (_request.Assessments.Count + 1), MovementFailureKind.CapacityExceeded);
        }
        private bool AdvanceMotion()
        {
            if (_s.Segment is not { } segment) return false;
            _eventContext = segment.Key; _eventAssessment = segment.Assessment;
            if (_s.Phase == MovementPhase.OutboundBeforeEntry && _s.Time == segment.EntryTime)
            {
                Work();
                _s = _s with { Phase = MovementPhase.OutboundAfterEntry, CommittedCell = segment.To };
                Emit(MovementEventKind.CellCommitted, segment.Key, assessment: segment.Assessment);
            }
            if (_s.Time == segment.EndTime)
            {
                Work();
                Settle(segment);
                return true;
            }
            _s = _s with { Anchor = MovementGeometry.AnchorAt(segment, _s.Time) };
            return false;
        }
        private void Settle(ActiveSegment segment)
        {
            _s = _s with { Phase = MovementPhase.Settled, Segment = null,
                Anchor = MovementGeometry.Center(_s.CommittedCell) };
            Emit(MovementEventKind.CenterArrived, segment.Key, assessment: segment.Assessment);
        }
        private bool Cancel()
        {
            var segment = _s.Segment!;
            var returning = new ActiveSegment(segment.Assessment, _s.Time);
            // Compute both time and observed anchor now: late numeric failure is transactional too.
            var end = returning.EndTime;
            Emit(MovementEventKind.StepCancelled, segment.Key, assessment: segment.Assessment);
            _s = _s with { Phase = MovementPhase.Returning, Segment = returning };
            if (end == _s.Time)
            {
                Work(); Settle(returning); return true;
            }
            ChangeFacing(MovementGeometry.Reverse(segment.Direction), segment.Key, segment.Assessment);
            return false;
        }
        private void StartOpportunity()
        {
            MovementValidation.Require(++_opportunities <= _s.Limits.MaxOpportunities, MovementFailureKind.CapacityExceeded);
            var direction = _s.LatestIntent.Direction!.Value;
            var key = new AdmissionKey(_s.Binding, _s.CommittedCell, MovementGeometry.Neighbor(_s.CommittedCell, direction),
                direction, _s.Time, _s.NextOpportunityId, _s.NextStepId, _s.MaterialIntentKey!.Value, _s.EvidenceRevision);
            var nextOpportunity = checked(_s.NextOpportunityId + 1);
            _eventContext = key;
            _byOpportunity.TryGetValue(key.OpportunityId, out var assessment);
            if (assessment is null) _byTime.TryGetValue(key.StartTime, out assessment);
            _eventAssessment = assessment;
            if (assessment is not null)
            {
                MovementValidation.Require(assessment.Key == key && !_consumed.Contains(key),
                    MovementFailureKind.IdentityMismatch, key: key);
                _consumed.Add(key);
            }
            var admitted = assessment is { Coverage: AssessmentCoverage.Supported, Disposition: AssessmentDisposition.Open };
            _s = _s with { NextOpportunityId = nextOpportunity, LastOpportunity = key,
                LastOpportunityDisposition = admitted ? "Admitted" : assessment is null ? "MissingAssessment" : "Denied" };
            if (!admitted)
            {
                Emit(MovementEventKind.StepRejected, key, assessment is null ? "MissingAssessment" : "CoverageDenied", assessment);
                return;
            }
            var segment = new ActiveSegment(assessment!);
            // Freeze and validate representability even if target equals start.
            _ = segment.EntryTime; _ = segment.ArrivalTime;
            var nextStep = checked(_s.NextStepId + 1);
            _s = _s with { Segment = segment, Phase = MovementPhase.OutboundBeforeEntry, NextStepId = nextStep };
            Emit(MovementEventKind.StepStarted, key, assessment: assessment);
            ChangeFacing(direction, key, assessment);
        }
        private void ChangeFacing(NeighborDirection facing, AdmissionKey key, StepAssessmentTiming? assessment)
        {
            if (facing == _s.Facing) return;
            var old = _s.Facing;
            _s = _s with { Facing = facing };
            Emit(MovementEventKind.FacingChanged, key, assessment: assessment, oldFacing: old, newFacing: facing);
        }
        private void ChangeAction(MovementAction action)
        {
            if (action == _s.Action) return;
            var old = _s.Action;
            _s = _s with { Action = action };
            Emit(MovementEventKind.ActionChanged, _eventContext!.Value, assessment: _eventAssessment,
                oldAction: old, newAction: action);
        }
        private void Emit(MovementEventKind kind, AdmissionKey key, string? reason = null,
            StepAssessmentTiming? assessment = null, NeighborDirection? oldFacing = null, NeighborDirection? newFacing = null,
            MovementAction? oldAction = null, MovementAction? newAction = null)
        {
            MovementValidation.Require(_events.Count < _s.Limits.MaxEvents, MovementFailureKind.CapacityExceeded, key: key);
            var nextOrdinal = checked(_s.NextEventOrdinal + 1);
            _events.Add(new(_s.Time, _s.NextEventOrdinal, kind, key, reason, assessment,
                oldFacing, newFacing, oldAction, newAction));
            _s = _s with { NextEventOrdinal = nextOrdinal };
        }
    }
}
