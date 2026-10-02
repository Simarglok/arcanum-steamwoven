using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Fixtures;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class StateConsistencyTests
{
    [TestMethod]
    [DataRow("NeutralBeforeEntry")]
    [DataRow("RevisionBeforeEntry")]
    [DataRow("AdmittedWithoutStep")]
    [DataRow("DeniedWithStep")]
    [DataRow("RevisionWithoutInput")]
    [DataRow("RevisionAfterEntry")]
    [DataRow("RevisionReturning")]
    [DataRow("MissingWithStep")]
    [DataRow("DeniedProposedStepBehind")]
    [DataRow("AdmittedProposedStepBehind")]
    [DataRow("ReceiptMaterialAhead")]
    [DataRow("UnchangedMaterialTurn")]
    [DataRow("UnchangedMaterialNeutralAfterEntry")]
    [DataRow("NeutralBeforeEntryRecordedAfterEntry")]
    [DataRow("DeniedProposedStepBeyondOpportunity")]
    [DataRow("ReturnMaterialBeforeRelease")]
    [DataRow("DeniedWrongCenter")]
    [DataRow("MissingWrongCenter")]
    [DataRow("AdmittedWrongCenter")]
    public void M5_R1_ContradictoryDescriptorsAreAtomicallyRefused(string mutation)
    {
        // These witnesses come from real production transitions. Expected refusal is
        // literal policy, independent of the validator's implementation/constants.
        var initial = Initial();
        var a = Open(Key());
        var before = Complete(initial, Request(1, [Batch(0, Press(0))], [a])).State!.Descriptor;
        var after = Complete(initial, Request(4, [Batch(0, Press(0)), Batch(4, Neutral(1))], [a])).State!.Descriptor;
        var returning = Complete(initial, Request(3, [Batch(0, Press(0)), Batch(2, Neutral(1))], [a])).State!.Descriptor;
        var denial = new StepAssessmentTiming(Key(), AssessmentCoverage.Unknown, null, null,
            ["UNKNOWN"], ["SYNTHETIC_POLICY"]);
        var denied = Complete(initial, Request(0, [Batch(0, Press(0))], [denial])).State!.Descriptor;
        var missing = Complete(initial, Request(0, [Batch(0, Press(0))])).State!.Descriptor;
        var settled = Complete(initial, Request(8, [Batch(0, Press(0)), Batch(8, Neutral(1))], [a])).State!.Descriptor;
        var two = Complete(initial, Request(16, [Batch(0, Press(0)), Batch(16, Neutral(1))],
            [a, Open(Key(2, 2, new(1, 0), new(2, 0), start: 8))])).State!.Descriptor;
        var deniedAfterAdmission = Complete(initial, Request(8, [Batch(0, Press(0))],
            [a, new StepAssessmentTiming(Key(2, 2, new(1, 0), new(2, 0), start: 8),
                AssessmentCoverage.Unknown, null, null, ["UNKNOWN"], ["SYNTHETIC_POLICY"])])).State!.Descriptor;
        var descriptor = mutation switch
        {
            "NeutralBeforeEntry" => before with { LatestIntent = ActorIntent.Neutral },
            "RevisionBeforeEntry" => before with { EvidenceRevision = 1 },
            "AdmittedWithoutStep" => denied with { LastOpportunityDisposition = "Admitted" },
            "DeniedWithStep" => denied with { NextStepId = 2 },
            "RevisionWithoutInput" => initial.Descriptor with { EvidenceRevision = 1 },
            "RevisionAfterEntry" => after with { EvidenceRevision = 1 },
            "RevisionReturning" => returning with { EvidenceRevision = 1 },
            "MissingWithStep" => missing with { NextStepId = 2 },
            "DeniedProposedStepBehind" => deniedAfterAdmission with { LastOpportunity = deniedAfterAdmission.LastOpportunity!.Value with { ProposedStepId = 1 } },
            "AdmittedProposedStepBehind" => two with { LastOpportunity = two.LastOpportunity!.Value with { ProposedStepId = 1 } },
            "ReceiptMaterialAhead" => denied with { LatestInputKey = new(T(0), 2), LastOpportunity = Key(materialSequence: 1) },
            "UnchangedMaterialTurn" => before with { LatestIntent = ActorIntent.Toward(NeighborDirection.YPlus) },
            "UnchangedMaterialNeutralAfterEntry" => after with { MaterialIntentKey = new(T(0), 0) },
            "NeutralBeforeEntryRecordedAfterEntry" => after with { MaterialIntentKey = new(T(2), 1) },
            "DeniedProposedStepBeyondOpportunity" => denied with { NextStepId = 2, LastOpportunity = Key(step: 2) },
            "ReturnMaterialBeforeRelease" => returning with { MaterialIntentKey = new(T(1), 1) },
            "DeniedWrongCenter" => denied with { CommittedCell = new(1, 0), Anchor = new(Q(1), Q(0)) },
            "MissingWrongCenter" => missing with { CommittedCell = new(1, 0), Anchor = new(Q(1), Q(0)) },
            "AdmittedWrongCenter" => settled with { CommittedCell = new(2, 0), Anchor = new(Q(2), Q(0)) },
            _ => throw new AssertFailedException("Unknown literal mutation")
        };
        var bad = MovementState.FromDescriptor(descriptor);
        Assert.IsFalse(MovementValidation.IsValidState(bad), mutation);
        // Includes assessments and a batch: invalid state must not consume either.
        Invalid(bad, Request(24, [Batch(20, Neutral(4))], [Open(Key(3, 3, start: 20))]), MovementFailureKind.InvalidState);
        Assert.ThrowsExactly<ArgumentException>(() => MovementRuntime.QuerySnapshot(bad));
    }

    [TestMethod]
    public void M5_R1_LegitimatePhaseRevisionAndReceiptProgressionRemainValid()
    {
        var initial = Initial();
        var a = Open(Key());
        var after = Complete(initial, Request(4, [Batch(0, Press(0)), Batch(4, Neutral(1))], [a])).State!;
        Assert.AreEqual(MovementPhase.OutboundAfterEntry, after.Phase);
        Assert.AreEqual(ActorIntent.Neutral, after.LatestIntent);
        Assert.AreEqual(new InputKey(T(4), 1), after.MaterialIntentKey);
        var returnedActive = Complete(initial, Request(3, [Batch(0, Press(0)), Batch(2, Neutral(1)),
            Batch(3, Press(2, NeighborDirection.YPlus))], [a])).State!;
        Assert.AreEqual(MovementPhase.Returning, returnedActive.Phase);
        Assert.AreEqual(ActorIntent.Toward(NeighborDirection.YPlus), returnedActive.LatestIntent);
        Assert.AreEqual(new InputKey(T(3), 2), returnedActive.MaterialIntentKey);
        var neutralRevision = Complete(initial, Request(1, [Batch(1, MovementInput.Reassess(0, 1))])).State!;
        Assert.AreEqual(1L, neutralRevision.EvidenceRevision);
        Assert.IsNull(neutralRevision.MaterialIntentKey);
        Assert.AreEqual(new InputKey(T(1), 0), neutralRevision.LatestInputKey);
        var denied = Complete(initial, Request(0, [Batch(0, Press(0))],
            [new StepAssessmentTiming(Key(), AssessmentCoverage.Unknown, null, null, ["UNKNOWN"], ["SYNTHETIC_POLICY"])])).State!;
        var admitted = Complete(denied, Request(1, [Batch(1, MovementInput.Reassess(1, 1))],
            [Open(Key(2, 1, start: 1, revision: 1))])).State!;
        Assert.AreEqual(3L, admitted.NextOpportunityId);
        Assert.AreEqual(2L, admitted.NextStepId);
        Assert.AreEqual("Admitted", admitted.LastOpportunityDisposition);
        Assert.AreEqual(1L, admitted.LastOpportunity!.Value.ProposedStepId);
        var settled = Complete(admitted, Request(9, [Batch(9, Neutral(2))])).State!;
        var revisedSettled = Complete(settled, Request(10, [Batch(10, MovementInput.Reassess(3, 2))])).State!;
        Assert.AreEqual(2L, revisedSettled.EvidenceRevision);
        Assert.AreEqual(1L, revisedSettled.LastOpportunity!.Value.EvidenceRevision);
        Assert.AreEqual("Admitted", revisedSettled.LastOpportunityDisposition);
        var deniedAfterAdmission = Complete(admitted, Request(9)).State!;
        Assert.AreEqual("MissingAssessment", deniedAfterAdmission.LastOpportunityDisposition);
        Assert.AreEqual(2L, deniedAfterAdmission.NextStepId);
        Assert.AreEqual(4L, deniedAfterAdmission.NextOpportunityId);
        Assert.AreEqual(2L, deniedAfterAdmission.LastOpportunity!.Value.ProposedStepId);
        var neutralAfterDenial = Complete(denied, Request(1, [Batch(1, Neutral(1))])).State!;
        var revisedDenied = Complete(neutralAfterDenial, Request(2, [Batch(2, MovementInput.Reassess(2, 1))])).State!;
        Assert.AreEqual("Denied", revisedDenied.LastOpportunityDisposition);
        Assert.AreEqual(1L, revisedDenied.EvidenceRevision);
        Assert.AreEqual(0L, revisedDenied.LastOpportunity!.Value.EvidenceRevision);
        foreach (var state in new[] { after, returnedActive, neutralRevision, denied, admitted, settled,
            revisedSettled, deniedAfterAdmission, neutralAfterDenial, revisedDenied })
        {
            Assert.IsTrue(MovementValidation.IsValidState(state));
            var snapshot = MovementRuntime.QuerySnapshot(state);
            Assert.AreEqual(state.LatestIntent, snapshot.LatestIntent);
            Assert.AreEqual(state.EvidenceRevision, snapshot.EvidenceRevision);
            Assert.AreEqual(state.LastOpportunity, snapshot.LastOpportunity);
            var unchanged = Complete(state, new(state.Time, [], []));
            Assert.AreEqual(state.Descriptor, unchanged.State!.Descriptor);
            Assert.AreEqual(0, unchanged.Events.Count);
        }
        // Validated open zero-progress cancellation/re-input is covered separately by
        // M2_ValidatedOpenZeroProgressFixtureUsesProductionValidator and full transcripts.
    }
}
