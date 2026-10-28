using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-075: outside combat mode a companion's follow walk passes living critters as the PC's does (the approach walks'
/// path step asks for the request's flag 0x10, ledger LR2 and LR10); in combat mode they block it; under the Original rule mode its walk
/// waits before a living critter at a segment's last step and plans again (goal 61 pops to its parent, ledger EX20). Steps of
/// <see cref="AreaCritterPassTests"/>: an NPC record on (18,30).</summary>
[TestClass]
public sealed class AreaCompanionCritterPassTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    private sealed class Walker
    {
        internal readonly AreaGridSession Grid;
        internal readonly CompanionFollower Follow;
        internal readonly List<CellCoord> Live = [];
        internal readonly HashSet<CellCoord> Visited = [];
        internal readonly List<string> Trace = [];
        internal int Time;

        internal Walker(CellCoord from, bool pass, bool segmentEndCheck)
        {
            var steps = AreaCritterPassTests.Steps();
            AreaOccupancy Occupancy() => new([], Live);
            Grid = new AreaGridSession(steps, SimulationTime.At(0), Step, chart: AreaCritterPassTests.Chart, start: from,
                actorId: AreaCompanionGroup.ActorPrefix + "0", capabilities: AreaCompanionGroup.Capabilities) { Occupancy = Occupancy, CrittersPass = () => pass };
            Follow = new CompanionFollower(new AreaRouteSource(steps, AreaCritterPassTests.Chart, Occupancy, crittersPass: () => pass),
                segmentEndCritterCheck: () => segmentEndCheck);
        }

        internal string Describe() => $"{Follow.Route.Status} {Follow.Route.StopReason} at {Grid.State.CommittedCell} {Follow.LastAction} "
            + $"plans {Follow.Route.PlannedSegments} last {Follow.Route.LastPlan?.Outcome} {Follow.Route.LastPlan?.ReasonCode} trace {string.Join(" ", Trace)}";

        // Frames of one step each, the leader standing on its cell.
        internal void Frames(int count, CellCoord leader)
        {
            for (var i = 0; i < count; i++)
            {
                var frontier = SimulationTime.At(++Time);
                Follow.Update(Grid.State, leader, spreadOut: false, following: true, frontier);
                var drive = Follow.Drive(Grid.State, frontier, (until, batches) => Grid.Advance(until, batches.ToArray()).Movement);
                Assert.IsTrue(drive.Completed, drive.FailureCode);
                Visited.Add(Grid.State.CommittedCell);
                Trace.Add($"{Grid.State.CommittedCell.X}:{Follow.Route.PlannedSegments}/{Follow.Route.RemainingSegment.Count}/{Follow.Route.Status}");
            }
        }
    }

    private static readonly CellCoord Leader = new(30, 30);

    [TestMethod]
    public void Outside_combat_a_companion_walks_through_an_NPC_toward_its_leader()
    {
        var walker = new Walker(new(10, 30), pass: true, segmentEndCheck: false);
        walker.Frames(30, Leader);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(28, 30)), (walker.Follow.Route.Status, walker.Grid.State.CommittedCell),
            "a straight line through the NPC's tile, stopping within range 2");
        Assert.IsTrue(walker.Visited.Contains(new(18, 30)), "it stood on the NPC's tile on the way");
    }

    [TestMethod]
    public void In_combat_mode_the_NPC_blocks_and_the_companion_walks_around_it()
    {
        var walker = new Walker(new(10, 30), pass: false, segmentEndCheck: false);
        walker.Frames(40, Leader);
        Assert.AreEqual(RouteFollowStatus.Arrived, walker.Follow.Route.Status, walker.Describe());
        Assert.AreEqual(2, FollowDistanceRules.Distance(walker.Grid.State.CommittedCell, Leader));
        Assert.IsFalse(walker.Visited.Contains(new(18, 30)), "its plan went around the NPC's tile");
    }

    [TestMethod]
    public void Under_Original_the_companion_stops_before_a_critter_at_a_segment_end_and_plans_again()
    {
        // The NPC on (18,30) is the eighth cell east of (10,30), the first segment's last step: the walk stops before it, the next
        // update plans again from (17,30), and the NPC, now on the new plan's first step, is walked through.
        var walker = new Walker(new(10, 30), pass: true, segmentEndCheck: true);
        var frames = 0;
        do walker.Frames(1, Leader); while (walker.Follow.Route.Status == RouteFollowStatus.Following && ++frames < 12);
        Assert.AreEqual((RouteFollowStatus.Stopped, CompanionFollower.SegmentEndCritterReason, new CellCoord(17, 30)),
            (walker.Follow.Route.Status, walker.Follow.Route.StopReason, walker.Grid.State.CommittedCell), walker.Describe());
        walker.Frames(1, Leader);
        Assert.IsNull(walker.Follow.Unreachable, "a stop before a critter is not remembered as unreachable");
        Assert.AreEqual(RouteFollowStatus.Following, walker.Follow.Route.Status, walker.Describe());
        walker.Frames(20, Leader);
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(28, 30)), (walker.Follow.Route.Status, walker.Grid.State.CommittedCell), walker.Describe());
        Assert.IsTrue(walker.Visited.Contains(new(18, 30)));

        // A live critter (another companion, the PC) is passed the same way; with the switch off there is no stop at all.
        var live = new Walker(new(10, 40), pass: true, segmentEndCheck: true);
        live.Live.Add(new(18, 40));
        live.Frames(30, new(30, 40));
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(28, 40)), (live.Follow.Route.Status, live.Grid.State.CommittedCell), live.Describe());
        var through = new Walker(new(10, 40), pass: true, segmentEndCheck: false);
        through.Live.Add(new(18, 40));
        through.Frames(30, new(30, 40));
        Assert.AreEqual((RouteFollowStatus.Arrived, new CellCoord(28, 40)), (through.Follow.Route.Status, through.Grid.State.CommittedCell), through.Describe());
        Assert.IsTrue(through.Visited.Contains(new(18, 40)) && !through.Trace.Any(t => t.EndsWith("Stopped", StringComparison.Ordinal)));
    }
}
