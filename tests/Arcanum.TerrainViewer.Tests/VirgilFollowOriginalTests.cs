using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-016 on the authenticated exterior: the viewer's conversation host with the session party joins Virgil
/// through his own dialog, and his companion actor then follows the PC over the whole-core T0 step evidence. Opt-in
/// like the other AREA gates (ARC_AREA_ORIGINAL=1 and the ARC_AREA_* roles). Structural identifiers and cells only.
/// </summary>
[TestClass]
public sealed class VirgilFollowOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public async Task OriginalVirgilJoinsThroughTheViewerHostAndFollowsThePc()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1")
            Assert.Inconclusive("Opt-in authenticated AREA follow gate; not a synthetic PASS.");
        string Required(string role) => Environment.GetEnvironmentVariable(role) is { Length: > 0 } value ? value
            : throw new AssertFailedException("Requested original AREA gate is missing an input role: " + role);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Required("ARC_AREA_MODULE"),
            "--resource", Required("ARC_AREA_RESOURCE2"), "--actor-resource", Required("ARC_AREA_RESOURCE1"),
            "--area-mobile", Required("ARC_AREA_MOBILE"), "--prototypes", Required("ARC_AREA_PROTOTYPES"),
            "--area-effects", Required("ARC_AREA_EFFECTS"), "--area-patch", Required("ARC_AREA_PATCH"),
            "--source-id", "ORIGINAL_AREA_FOLLOW"]);
        using var coordinator = new TerrainLoadCoordinator();
        var result = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments).WaitAsync(TimeSpan.FromMinutes(8));
        var steps = result.AreaStepSnapshot ?? throw new AssertFailedException("No whole-core step evidence.");
        var area = result.Area!.Snapshot;

        using var session = ConversationSessionFixture.Create(area.RuntimeMap);
        using var scripts = AreaContentRoot.OpenScripts(Required("ARC_AREA_MODULE")).Content ?? throw new InvalidOperationException("SCRIPT_CONTENT");
        var host = new AreaConversationHost(area, scripts, session, party: true);
        var speaker = host.Speakers.Single();
        Assert.AreSame(AreaCompanionRoster.Virgil, AreaCompanionRoster.For(speaker), "the only speaker is Virgil (prototype 17102, script 1324)");
        // ARC-WORLD-037: the facts read from the game's data (AI row, level and alignment with effects) are the measured ones.
        var data = AreaCompanionData.Open(Required("ARC_AREA_MODULE"));
        Assert.AreEqual(AreaCompanionDataStatus.Ready, data.Status, data.Detail);
        Assert.AreEqual(AreaCompanionRoster.Virgil, data.Data!.Facts(speaker, out var refusal), refusal);
        var templates = AreaCompanionTemplate.From(host.Speakers, result.Area.Plan, area.Visuals, AreaChart.Exterior, new(area.RuntimeMap));
        var group = AreaCompanionGroup.Create(templates, steps, AreaChart.Exterior, session, new(area.RuntimeMap), SimulationTime.At(0));
        var virgil = group.Companions.Single();
        var origin = virgil.Grid.State.CommittedCell;
        Console.WriteLine($"Virgil T0 chart cell {origin.X},{origin.Y}");

        // The first talk joins him through the session party (JO at node 210 -> 74), not the diagnostic key.
        var start = host.Begin(host.Nearest(speaker.WorldX, speaker.WorldY));
        Assert.AreEqual("script 1324 SCR line 4 -> DLG node 1", start.Detail);
        Assert.IsNotNull(start.Party);
        var conversation = start.Conversation!;
        while (conversation.Phase == DialogConversationPhase.AwaitingSelection)
            conversation.Select((conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.Direct)
                ?? conversation.Options.First(o => o.Block is null)).Token);
        Assert.AreEqual(DialogConversationPhase.Ended, conversation.Phase);
        Assert.AreEqual(CompanionStanding.Following, session.QueryCompanion(virgil.Entity).Standing);
        Assert.IsNull(session.QueryMapChange(new(area.RuntimeMap), MapChangeKey.ForEntity(virgil.Entity, AreaConversationState.FollowProperty)));
        Assert.IsTrue(host.State.Follows(speaker.Index));
        // Talking again: the follower entry (node 385) now passes its WA conditions instead of stopping at a frontier.
        var again = host.Begin(speaker);
        Assert.AreEqual("script 1324 SCR line 6 -> DLG node 385", again.Detail);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, again.Conversation!.Phase);
        Assert.IsTrue(again.Conversation.Close());

        // The PC 12 cells east of him (the nearest standable cell): he follows to within 2 and stands settled.
        var pc = Standable(steps, origin with { X = origin.X + 12 });
        var pcGrid = new AreaGridSession(steps, SimulationTime.At(0), StepDuration.Of(1, 2), start: pc) { Occupancy = group.OccupancyForPc };
        var frame = 0;
        for (; frame < 400 && (virgil.Grid.State.Phase != MovementPhase.Settled || FollowDistanceRules.Distance(virgil.Grid.State.CommittedCell, pc) > 2); frame++)
        {
            var frontier = SimulationTime.At(frame + 1, 10);
            pcGrid.Advance(frontier, []);
            group.Drive(session, pcGrid.State, frontier);
        }
        var cell = virgil.Grid.State.CommittedCell;
        Console.WriteLine($"PC {pc.X},{pc.Y}; Virgil {cell.X},{cell.Y} after {frame} frames, {virgil.Follow.Route.PlannedSegments} plans, last {virgil.Follow.Route.LastPlan?.ReasonCode}");
        Assert.IsTrue(FollowDistanceRules.Distance(cell, pc) <= 2, group.Status());
        Assert.AreEqual(MovementPhase.Settled, virgil.Grid.State.Phase);
        Assert.AreNotEqual(origin, cell);
        // His upright T0 body command is drawn at his cell: moved by the projected offset, ordered at the new cell.
        var t0 = result.Area.Plan.Commands.Single(c => c.Identity == virgil.Key);
        var moved = group.Commands().Single();
        Assert.AreEqual((4, t0.Destination.X - 40 * (cell.X - origin.X) + 40 * (cell.Y - origin.Y), t0.Destination.Y + 20 * (cell.X - origin.X + cell.Y - origin.Y)),
            (moved.Phase, moved.Destination.X, moved.Destination.Y));

        // Saved and loaded: he stands where he stopped, still following.
        Assert.AreEqual(GameSessionFailure.None, group.Record(session));
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var restored = AreaCompanionGroup.Create(AreaCompanionTemplate.From(host.Rebind(loaded).Speakers, result.Area.Plan, area.Visuals, AreaChart.Exterior, new(area.RuntimeMap)), steps, AreaChart.Exterior, loaded, new(area.RuntimeMap), SimulationTime.At(0));
        Assert.AreEqual((cell, CompanionStanding.Following), (restored.Companions.Single().Grid.State.CommittedCell, restored.Companions.Single().Standing));
    }

    private static CellCoord Standable(Arcanum.Data.Traversal.AreaStepSnapshot steps, CellCoord near)
    {
        for (var r = 0; r <= 6; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            var cell = new CellCoord(near.X + dx, near.Y + dy);
            var routes = new AreaRouteSource(steps);
            if (!routes.Contains(cell)) continue;
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var from = new CellCoord(cell.X - Delta(direction).X, cell.Y - Delta(direction).Y);
                if (routes.Contains(from) && routes.Assess(from, direction) == Arcanum.Simulation.Navigation.RouteStepVerdict.Open) return cell;
            }
        }
        throw new AssertFailedException("no standable cell near " + near);
    }

    private static (int X, int Y) Delta(NeighborDirection direction)
    {
        var next = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(new(0, 0), direction);
        return (next.X, next.Y);
    }
}
