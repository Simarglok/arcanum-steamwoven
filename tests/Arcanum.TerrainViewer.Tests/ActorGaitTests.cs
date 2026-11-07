using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        // ARC-WORLD-021: the vault is 9 frames at 125 ms (8 fps, no Speed scaling).
        Assert.AreEqual(StepDuration.Of(9, 8), timing.Vault);
        Assert.IsNull(new GaitTiming(timing.Walk, timing.Run, 8).Vault);
        Assert.Throws<ArgumentException>(() => new GaitTiming(timing.Walk, timing.Run, 8, 9, 0));
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
        var run = ActorDiagnosticProfile.HumanFixedArea;
        Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed, run.Id);
        Assert.AreEqual((0x20000180u, 2671, 10, 20), (run.Run!.Selector, run.Run.Ordinal, run.Run.FramesPerGroup, run.Run.RawFramesPerSecond));
        Assert.AreEqual(ActorDiagnosticAction.Run, run.ActionFor(0x20000180));
        Assert.AreEqual((195, 1705144L), (run.DistinctKeys, run.OriginalRgbaBytes!.Value));
        Assert.AreEqual((0x20000340u, 2677, 9, 8), (run.Vault!.Selector, run.Vault.Ordinal, run.Vault.FramesPerGroup, run.Vault.RawFramesPerSecond));
        Assert.AreEqual(ActorDiagnosticAction.Vault, run.ActionFor(0x20000340));
        Assert.IsNull(ActorDiagnosticProfile.HumanFixed.Vault);
        CollectionAssert.AreEqual(new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run, ActorDiagnosticAction.Vault,
            ActorDiagnosticAction.Attack }, run.Actions.ToArray());
        // ARC-COMBAT-005: the bare-handed attack, animation 20 with weapon slot 1 (resource1 ordinal 2683), 9 frames at 15 fps, action frame 6.
        Assert.AreEqual((0x20000501u, 2683, 9, 15, 6), (run.Attack!.Selector, run.Attack.Ordinal, run.Attack.FramesPerGroup, run.Attack.RawFramesPerSecond,
            run.Attack.ActionFrame));
        Assert.AreEqual(ActorDiagnosticAction.Attack, run.ActionFor(0x20000501));
        Assert.IsNull(ActorDiagnosticProfile.HumanFixed.Attack);
        Assert.Throws<TerrainViewerException>(() => ActorDiagnosticProfile.MonsterRegression.Resource(ActorDiagnosticAction.Run));
    }

    [TestMethod]
    public void AHeroBodySwapChangesTheProfileTheSessionTimingAndThePlaybackRate()
    {
        // ARC-UI-039: a dwarf or halfling body steps and plays its walk at 17 + 4 frames per second.
        var human = ActorDiagnosticProfile.HumanFixedArea;
        var fast = new GaitTiming(human.Walk.Stride!, human.Run!.Stride!, GaitTiming.BaseSpeed, human.Vault!.FramesPerGroup, human.Vault.RawFramesPerSecond, 4);
        var actor = new DiagnosticGridActor { AreaGaitTiming = GaitTiming.HumanFixed, AreaGait = () => MovementGait.Walk };
        actor.Configure(Steps(), SimulationTime.At(0));
        var session = (AreaGridSession)actor.Session!;
        // The human's resources as a hero body profile (another profile object, as an elf man's would be).
        var body = ActorDiagnosticProfile.ForHeroBody(human.Idle, human.Walk, human.Run, human.Vault, human.Attack!, human.DistinctKeys);
        actor.AttackPose = (ActorDiagnosticAction.Attack, 5, 3);
        actor.UseProfile(body, fast);
        Assert.AreSame(body, actor.Profile);
        Assert.AreSame(fast, actor.AreaGaitTiming);
        Assert.AreSame(fast, session.Timing);
        Assert.IsNull(actor.AttackPose);
        Assert.AreEqual((ActorDiagnosticAction.Idle, 0), (actor.State.Action, actor.CurrentFrame));
        // A walk step east, sampled at 0.1 s: the step takes the fast duration and the walk clock runs at 21 fps (frame 2, not 1).
        var step = fast.For(MovementGait.Walk, NeighborDirection.XPlus);
        Assert.IsTrue(step.Value < GaitTiming.HumanFixed.For(MovementGait.Walk, NeighborDirection.XPlus).Value);
        var started = actor.Advance(SimulationTime.At(1, 10), [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        Assert.AreEqual(step, started.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!.Duration);
        Assert.AreEqual((ActorDiagnosticAction.Walk, 2), (actor.State.Action, actor.CurrentFrame));
        // A profile without the AREA actions is refused, and the actor keeps its own.
        Assert.Throws<ArgumentException>(() => actor.UseProfile(ActorDiagnosticProfile.MonsterRegression, fast));
        Assert.AreSame(body, actor.Profile);
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
        var profile = ActorDiagnosticProfile.HumanFixedArea;
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

    // A whole-map snapshot over sector (0,0) with window wall pieces on the +X edge of the given cells (default (10,10)),
    // and optionally an Unknown object record on (11,10).
    private static readonly AreaObjectKey Window = new("s", 1, AreaSourceKind.Static, 0, 0, 0, "window", 1);
    private static readonly AreaObjectKey Unread = new("s", 1, AreaSourceKind.Mobile, 0, 0, 1, "npc", 1);
    private static AreaStepSnapshot WindowSteps(bool unknownRecord = false, params (int X, int Y)[] windows)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var walls = (windows.Length == 0 ? [(10, 10)] : windows).ToDictionary(w => new AreaStepCell(w.X, w.Y),
            w => new[] { new AreaStepWallEdge(Window, new(w.X, w.Y), 5, true, AreaStepEdgeRule.WindowJump, "WINDOW_PIECE_JUMP") });
        var objects = new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>();
        if (unknownRecord) objects[new(11, 10)] = [new(Unread, new(11, 10), 15, CardinalTraversalCoverage.Unknown, CardinalTraversalApplicability.Unknown, null, "TEST_UNREAD")];
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain, objects, walls);
    }

    [TestMethod]
    public void TheCompanionOverlayKeepsTheWindowJump()
    {
        var chart = new AreaChart(0, 0, new(10, 10), "TEST_WINDOW_CHART");
        // An occupied map (a moved companion record elsewhere) keeps the reason of an Open window step.
        var steps = WindowSteps();
        var occupied = new AreaOccupancy([Unread], [new CellCoord(30, 30)]);
        var window = steps.Assess(steps.Request(new(10, 10), new(11, 10), CardinalTraversalDirection.XPlus));
        var live = AreaOccupancyRules.Apply(window, new(10, 10), NeighborDirection.XPlus, occupied);
        Assert.AreEqual(("WINDOW_JUMP", false), (live.ReasonCode, live.Overlaid));
        // A window step whose only gap is a moved record opens through the overlay and stays a jump: vault cost, no straight line.
        var gapped = WindowSteps(unknownRecord: true);
        var raw = gapped.Assess(gapped.Request(new(10, 10), new(11, 10), CardinalTraversalDirection.XPlus));
        Assert.AreEqual((CardinalTraversalCoverage.Unknown, (AreaObjectKey?)Window), (raw.Coverage, raw.WindowJump));
        Assert.AreEqual("WINDOW_JUMP", AreaOccupancyRules.Apply(raw, new(10, 10), NeighborDirection.XPlus, occupied).ReasonCode);
        var routes = new AreaRouteSource(gapped, chart, () => occupied);
        Assert.AreEqual((RouteStepVerdict.Open, AreaRouteSource.WindowJumpCost), (routes.Assess(new(10, 10), NeighborDirection.XPlus), routes.ExtraCost(new(10, 10), NeighborDirection.XPlus)));
        Assert.AreEqual(RouteMethod.LocalSearch, RoutePlanner.Plan(routes, new(8, 10), new(13, 10)).Method);
        var session = new AreaGridSession(gapped, SimulationTime.At(0), chart: chart) { Occupancy = () => occupied, Timing = GaitTiming.HumanFixed };
        var advance = session.Advance(SimulationTime.At(1), [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        Assert.AreEqual(StepDuration.Of(9, 8), advance.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!.Duration);
    }

    [TestMethod]
    public void AVaultRightAfterAVaultPlaysFromItsFirstFrame()
    {
        var actor = new DiagnosticGridActor { AreaGaitTiming = GaitTiming.HumanFixed, AreaGait = () => MovementGait.Walk };
        actor.Configure(WindowSteps(false, (10, 10), (11, 10)), SimulationTime.At(0), chart: new AreaChart(0, 0, new(10, 10), "TEST_WINDOW_CHART"));
        actor.Advance(new SimulationTime(ExactScalar.Create(9, 16)), [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        Assert.AreEqual(ActorDiagnosticAction.Vault, actor.State.Action);
        // A quarter into the second vault: the clip restarted (frame 2 of 9 at 8 fps), not frozen on its last frame.
        actor.Advance(new SimulationTime(ExactScalar.Create(9, 8) + ExactScalar.Create(9, 32)), []);
        Assert.AreEqual((ActorDiagnosticAction.Vault, 2, true), (actor.State.Action, actor.CurrentFrame, actor.IsPlaying));
    }

    [TestMethod]
    public void AWindowStepIsAVaultWithItsOwnTimeArtAndRouteCost()
    {
        var steps = WindowSteps();
        var chart = new AreaChart(0, 0, new(10, 10), "TEST_WINDOW_CHART");
        var gait = MovementGait.Run;
        var actor = new DiagnosticGridActor { AreaGaitTiming = GaitTiming.HumanFixed, AreaGait = () => gait };
        actor.Configure(steps, SimulationTime.At(0), chart: chart);
        // The jump east: Open, the vault time and code whatever the gait, drawn with the vault ART.
        var half = new SimulationTime(ExactScalar.Create(9, 16));
        var advance = actor.Advance(half, [new InputBatch(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        var evidence = advance.AreaEvidence.Single();
        Assert.AreEqual(("WINDOW_JUMP", true), (evidence.Assessment.ReasonCode, evidence.Assessment.WindowJump is not null));
        var permit = advance.Movement.Events.Single(e => e.Kind == MovementEventKind.StepStarted).Assessment!;
        Assert.AreEqual(StepDuration.Of(9, 8), permit.Duration);
        CollectionAssert.Contains(permit.Provenance.ToArray(), GaitTiming.VaultProvenance);
        Assert.AreEqual(ActorDiagnosticAction.Vault, actor.State.Action);
        // The next ordinary step runs again, then the actor settles on the far side.
        var next = new SimulationTime(ExactScalar.Create(9, 8) + GaitTiming.HumanFixed.For(MovementGait.Run, NeighborDirection.XPlus).Value / ExactScalar.Integer(2));
        actor.Advance(next, []);
        Assert.AreEqual(ActorDiagnosticAction.Run, actor.State.Action);
        var release = new SimulationTime(next.Value + ExactScalar.Create(1, 1000));
        actor.Advance(new SimulationTime(next.Value + ExactScalar.Integer(1)), [new InputBatch(release, [MovementInput.Set(1, ActorIntent.Neutral)])]);
        Assert.AreEqual((ActorDiagnosticAction.Idle, new CellCoord(12, 10)), (actor.State.Action, actor.Session!.State.CommittedCell));
        // Route evidence: the window step is Open with extra cost 50; the plain step beside it costs nothing extra.
        var routes = new AreaRouteSource(steps, chart);
        Assert.AreEqual((RouteStepVerdict.Open, AreaRouteSource.WindowJumpCost), (routes.Assess(new(10, 10), NeighborDirection.XPlus), routes.ExtraCost(new(10, 10), NeighborDirection.XPlus)));
        Assert.AreEqual(AreaRouteSource.WindowJumpCost, routes.ExtraCost(new(11, 10), NeighborDirection.XMinus));
        Assert.AreEqual(0, routes.ExtraCost(new(10, 11), NeighborDirection.XPlus));
        Assert.AreEqual(RouteStepVerdict.Blocked, routes.Assess(new(10, 10), NeighborDirection.XPlusYPlus), "a diagonal step across a window is blocked");
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
