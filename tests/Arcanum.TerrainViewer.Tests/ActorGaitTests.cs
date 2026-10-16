using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ActorGaitTests
{
    private static InputBatch Input(int time, long sequence, NeighborDirection? direction) => new(SimulationTime.At(time),
        [MovementInput.Set(sequence, direction is { } d ? ActorIntent.Toward(d) : ActorIntent.Neutral)]);

    private static AreaStepSnapshot Steps()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        return AreaOwnerFixture.Steps(source.Owner.LoadStrict(), source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
    }

    [TestMethod]
    public void FrameRateFollowsSpeedWithTheOriginalMiddleValueAtEight()
    {
        Assert.AreEqual(6, GaitTiming.FramesPerSecond(MovementGait.Walk, 0));
        Assert.AreEqual(15, GaitTiming.FramesPerSecond(MovementGait.Walk, 7));
        Assert.AreEqual(17, GaitTiming.FramesPerSecond(MovementGait.Walk, 8));
        Assert.AreEqual(20, GaitTiming.FramesPerSecond(MovementGait.Walk, 9));
        Assert.AreEqual(29, GaitTiming.FramesPerSecond(MovementGait.Walk, 29));
        Assert.AreEqual(30, GaitTiming.FramesPerSecond(MovementGait.Walk, 30));
        Assert.AreEqual(18, GaitTiming.FramesPerSecond(MovementGait.Run, 7));
        Assert.AreEqual(20, GaitTiming.FramesPerSecond(MovementGait.Run, 8));
        Assert.AreEqual(23, GaitTiming.FramesPerSecond(MovementGait.Run, 9));
        Assert.AreEqual(30, GaitTiming.FramesPerSecond(MovementGait.Run, 100));
        Assert.AreEqual(58, GaitTiming.FrameMilliseconds(17));
        Assert.AreEqual(30, GaitTiming.FrameMilliseconds(40));
        Assert.AreEqual(800, GaitTiming.FrameMilliseconds(1));
    }

    [TestMethod]
    public void HumanStepDurationsAreTheResearchAverages()
    {
        // Research ARC-RES-047 L8: walk 547 / 773 ms, run 168 / 235 ms per axis / diagonal step at Speed 8.
        var timing = GaitTiming.HumanFixed;
        Assert.AreEqual(StepDuration.Of(29, 53), timing.For(MovementGait.Walk, NeighborDirection.XPlus));
        Assert.AreEqual(StepDuration.Of(58, 75), timing.For(MovementGait.Walk, NeighborDirection.XPlusYPlus));
        Assert.AreEqual(StepDuration.Of(25, 149), timing.For(MovementGait.Run, NeighborDirection.YMinus));
        Assert.AreEqual(StepDuration.Of(4, 17), timing.For(MovementGait.Run, NeighborDirection.XMinusYPlus));
        foreach (var direction in Enum.GetValues<NeighborDirection>())
            Assert.IsTrue(timing.For(MovementGait.Run, direction).Value < timing.For(MovementGait.Walk, direction).Value);
        // Faster frames at a higher Speed shorten every step; the stride stays the ART's.
        var quick = new GaitTiming(timing.Walk, timing.Run, 9);
        Assert.AreEqual(StepDuration.Of(500 * 2000, 1000 * 2120), quick.For(MovementGait.Walk, NeighborDirection.XPlus));
        Assert.Throws<ArgumentException>(() => new GaitTiming(timing.Walk with { AxisDx = 0, AxisDy = 0 }, timing.Run, 8));
    }

    [TestMethod]
    public void HumanRunResourceIsTheAreaProfileOnly()
    {
        Assert.IsNull(ActorDiagnosticProfile.HumanFixed.Run);
        Assert.AreEqual(55, ActorDiagnosticProfile.HumanFixed.DistinctKeys);
        var run = ActorDiagnosticProfile.HumanFixedRun;
        Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed, run.Id);
        Assert.AreEqual((0x20000180u, 2671, 10, 20), (run.Run!.Selector, run.Run.Ordinal, run.Run.FramesPerGroup, run.Run.RawFramesPerSecond));
        Assert.AreEqual(ActorDiagnosticAction.Run, run.ActionFor(0x20000180));
        Assert.AreEqual((105, 889656L), (run.DistinctKeys, run.OriginalRgbaBytes!.Value));
        CollectionAssert.AreEqual(new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run }, run.Actions.ToArray());
        Assert.Throws<TerrainViewerException>(() => ActorDiagnosticProfile.MonsterRegression.Resource(ActorDiagnosticAction.Run));
    }

    [TestMethod]
    public void RunGaitPermitsShorterStepsAndTheActorDrawsTheRunArt()
    {
        var gait = MovementGait.Run;
        var actor = new DiagnosticGridActor { AreaGaitTiming = GaitTiming.HumanFixed, AreaGait = () => gait };
        actor.Configure(Steps(), SimulationTime.At(0));
        var session = (AreaGridSession)actor.Session!;
        var step = GaitTiming.HumanFixed.For(MovementGait.Run, NeighborDirection.XPlus);
        // One run step east, sampled half-way: the permit carries the run duration and its gait code.
        var half = new SimulationTime(step.Value / ExactScalar.Integer(2));
        var started = actor.Advance(half, [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        var permit = started.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!;
        Assert.AreEqual(step, permit.Duration);
        CollectionAssert.Contains(permit.Provenance.ToArray(), GaitTiming.RunProvenance);
        Assert.AreEqual(ActorDiagnosticAction.Run, actor.State.Action);
        Assert.IsTrue(actor.IsPlaying);
        // The next step walks: the drawn action follows the new permit, and settling is Idle.
        gait = MovementGait.Walk;
        var walkStep = GaitTiming.HumanFixed.For(MovementGait.Walk, NeighborDirection.XPlus);
        var next = new SimulationTime(step.Value + walkStep.Value / ExactScalar.Integer(2));
        var walked = actor.Advance(next, []);
        var second = walked.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!;
        Assert.AreEqual(walkStep, second.Duration);
        CollectionAssert.Contains(second.Provenance.ToArray(), GaitTiming.WalkProvenance);
        Assert.AreEqual(ActorDiagnosticAction.Walk, actor.State.Action);
        var release = new SimulationTime(next.Value + ExactScalar.Create(1, 1000));
        actor.Advance(new SimulationTime(next.Value + ExactScalar.Integer(1)), [new InputBatch(release, [MovementInput.Set(1, ActorIntent.Neutral)])]);
        Assert.AreEqual(ActorDiagnosticAction.Idle, actor.State.Action);
        Assert.AreEqual(new CellCoord(8, 1), session.State.CommittedCell);
    }

    [TestMethod, TestCategory("RealData")]
    public void StridesAreTheFrameOffsetSumsOfTheInstalledArt()
    {
        // The stride constants restate per-cycle frame offset sums of the walk (2668) and run (2671) ART, group 1 an
        // axis rotation and group 2 a diagonal one (research ARC-RES-047 L7). Opt-in: ARC_AREA_ORIGINAL=1 and ARC_AREA_RESOURCE1.
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1") { Assert.Inconclusive("ARC_AREA_ORIGINAL=1 runs the original stride check."); return; }
        var path = Environment.GetEnvironmentVariable("ARC_AREA_RESOURCE1") ?? throw new AssertFailedException("ARC_AREA_RESOURCE1 is required");
        using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var profile = ActorDiagnosticProfile.HumanFixedRun;
        foreach (var resource in new[] { profile.Walk, profile.Run! })
        {
            var metadata = ArtDecoder.Inspect(archive.ReadEntry(archive.Entries.Single(e => e.Ordinal == resource.Ordinal)), new ArtDecoderOptions { MaxInputBytes = 1048576 });
            (int X, int Y) Sum(int group) => (Enumerable.Range(0, metadata.Header.FramesPerGroup).Sum(f => metadata.GetFrame(group, f).OffsetX),
                Enumerable.Range(0, metadata.Header.FramesPerGroup).Sum(f => metadata.GetFrame(group, f).OffsetY));
            var stride = resource.Stride!;
            Assert.AreEqual((stride.FramesPerCycle, stride.ArtFramesPerSecond), (metadata.Header.FramesPerGroup, metadata.Header.FramesPerSecond));
            Assert.AreEqual((stride.AxisDx, -stride.AxisDy), Sum(1), $"axis stride of {resource.Ordinal}");
            Assert.AreEqual((stride.DiagonalPixels, 0), Sum(2), $"diagonal stride of {resource.Ordinal}");
        }
    }

    [TestMethod]
    public void WithoutTimingEveryStepKeepsTheFixedDuration()
    {
        var session = new AreaGridSession(Steps(), SimulationTime.At(0), StepDuration.Of(8)) { Gait = () => MovementGait.Run };
        var advance = session.Advance(SimulationTime.At(1), [Input(0, 0, NeighborDirection.XPlus)]);
        var permit = advance.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!;
        Assert.AreEqual(StepDuration.Of(8), permit.Duration);
        CollectionAssert.DoesNotContain(permit.Provenance.ToArray(), GaitTiming.RunProvenance);
    }
}
