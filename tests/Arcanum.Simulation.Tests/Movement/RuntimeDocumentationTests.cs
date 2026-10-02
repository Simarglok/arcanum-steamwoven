using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

[TestClass]
public sealed class RuntimeDocumentationTests
{
    [TestMethod]
    public void SyntheticUseFromRuntimeDocumentIsExercised()
    {
        var binding = new ActorBinding("SYNTHETIC_CHART", "ACTOR", "FIXTURE_CONTEXT",
            "CAP", "CAP_FP", "PROFILE", "INPUT_FP", 7);
        var state = MovementState.CreateSettled(binding, new CellCoord(0, 0),
            SimulationTime.At(0), NeighborDirection.XPlus);
        var key = new AdmissionKey(binding, new CellCoord(0, 0), new CellCoord(1, 0),
            NeighborDirection.XPlus, SimulationTime.At(0), 1, 1,
            new InputKey(SimulationTime.At(0), 0), 0);
        var assessment = new StepAssessmentTiming(key, AssessmentCoverage.Supported,
            AssessmentDisposition.Open, StepDuration.Of(8),
            ["SYNTHETIC_OPEN"], ["SYNTHETIC_POLICY"]);
        var request = new MovementRequest(SimulationTime.At(3),
            [new InputBatch(SimulationTime.At(0),
                [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))]),
             new InputBatch(SimulationTime.At(2),
                [MovementInput.Set(1, ActorIntent.Neutral)])], [assessment]);
        var result = MovementRuntime.AdvanceTo(state, request);
        var snapshot = MovementRuntime.QuerySnapshot(result.State!);
        Assert.AreEqual(MovementStatus.Completed, result.Status);
        Assert.AreEqual(MovementPhase.Returning, snapshot.Phase);
        Assert.AreEqual(new CellCoord(0, 0), snapshot.CommittedCell);
        Assert.AreEqual(new WorldAnchor(ExactScalar.Create(1, 8), ExactScalar.Integer(0)), snapshot.Anchor);
        Assert.AreEqual(NeighborDirection.XMinus, snapshot.Facing);
        Assert.AreEqual(MovementAction.Walk, snapshot.Action);
        Assert.IsTrue(snapshot.IsClosed);
        Assert.AreEqual(SimulationTime.At(3), snapshot.Time);
        Fixtures.Transcript(result.Events, [assessment], "0:S:1 0:A:1:Idle>Walk 2:X:1 2:F:1:XPlus>XMinus");
    }
}
