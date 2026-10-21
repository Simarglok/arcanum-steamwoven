using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-046 in the AREA viewer: the order facts of the shown map's companions, the session order and the goals it starts (walk
/// here, the spread-out step). Over the strict owner fixture, whose scenery body at (5,1) stands in for the companion (see
/// <see cref="AreaCompanionTests"/>); the PC stands on the spawn (6,1).
/// </summary>
[TestClass]
public sealed class AreaCompanionOrderTests
{
    private static (AreaStepSnapshot Steps, AreaCompanionTemplate Template) Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(area, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        var speaker = new AreaSpeaker(0, scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype }, AreaCompanionRoster.Virgil.ScriptId,
            scenery.WorldX!.Value, scenery.WorldY!.Value);
        return (steps, AreaCompanionTemplate.From([speaker], AreaScenePlan.Create(area), area.Visuals, AreaChart.Exterior, new(1)).Single());
    }

    [TestMethod]
    public void The_facts_name_the_following_and_waiting_companions_with_their_distance()
    {
        var (steps, template) = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create([template], steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        var state = new AreaConversationState(session, new(1), [companion.Entity], 50);
        Assert.AreEqual(0, AreaCompanionOrders.Facts(session, group, new(6, 1), state, _ => "Virgil").Count, "a free NPC is no follower");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, companion.Entity).Disposition);
        var facts = AreaCompanionOrders.Facts(session, group, new(9, 5), state, _ => "Virgil").Single();
        Assert.AreEqual((companion.Entity, "Virgil", 4L, false), (facts.Entity, facts.KnownName, facts.Distance, facts.MindControlled));
        Assert.AreEqual(AreaCompanionRoster.WaitFacts(AreaCompanionRoster.Virgil), facts.Wait);
        Assert.AreEqual(50, facts.Consent.Reaction);
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, companion.Entity).Disposition);
        Assert.AreEqual(1, AreaCompanionOrders.Facts(session, group, new(6, 1), state, _ => "Virgil").Count, "a waiting follower has facts too (D1)");
    }

    [TestMethod]
    public void Walk_here_and_spread_out_start_ordered_goals_and_wait_makes_it_wait()
    {
        var (steps, template) = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create([template], steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        var state = new AreaConversationState(session, new(1), [companion.Entity], 50);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, companion.Entity).Disposition);
        var pc = new CellCoord(6, 1);
        CompanionOrderRequest Order(CompanionOrderType type, CellCoord? tile = null) =>
            new(type, CompanionOrderAddressing.All, AreaCompanionOrders.Facts(session, group, pc, state, _ => "Virgil"), Tile: tile);

        var (walk, walkNoGoal) = AreaCompanionOrders.Issue(session, group, Order(CompanionOrderType.WalkHere, new(12, 1)), pc, SimulationTime.At(1));
        Assert.AreEqual(0, walkNoGoal.Count);
        var (_, offMap) = AreaCompanionOrders.Issue(session, group, Order(CompanionOrderType.WalkHere, new(5000, 1)), pc, SimulationTime.At(1));
        CollectionAssert.AreEqual(new[] { companion.Entity }, offMap.ToArray(), "a tile off the map's steps starts no goal");
        Assert.AreEqual(CompanionOrderEffect.WalkRequested, walk.Outcomes.Single().Effect);
        Assert.IsTrue(companion.Follow.Ordered);
        Assert.AreEqual(new CellCoord(12, 1), companion.Follow.Route.Target);

        var (spread, _) = AreaCompanionOrders.Issue(session, group, Order(CompanionOrderType.SpreadOut), pc, SimulationTime.At(2));
        Assert.IsTrue(spread.Outcomes.Single().StartSpreadGoal);
        Assert.IsTrue(session.QueryCompanion(companion.Entity).SpreadOut);
        Assert.AreEqual(AreaCompanionOrders.Away(new(5, 1), pc), companion.Follow.Route.Target);
        StringAssert.Contains(AreaCompanionOrders.Log("F4", CompanionOrderType.SpreadOut, spread),
            $"ARC-WORLD-046 order source=F4 type=SpreadOut disposition=Changed failure=None reached=1 | {companion.Entity} SpreadOut -> SpreadSet lines 2100-2199");

        var (wait, _) = AreaCompanionOrders.Issue(session, group, Order(CompanionOrderType.Wait), pc, SimulationTime.At(3));
        Assert.AreEqual(CompanionOrderEffect.Waited, wait.Outcomes.Single().Effect);
        Assert.AreEqual(CompanionStanding.Waiting, companion.Standing, "the group reads the standing again");
        Assert.IsNull(session.QueryWaitTimer(companion.Entity), "Virgil's index 6: no timer");
    }

    [TestMethod]
    public void The_spread_step_moves_straight_away_from_the_pc()
    {
        Assert.AreEqual(new CellCoord(8, 1), AreaCompanionOrders.Away(new(5, 1), new(4, 1)));
        Assert.AreEqual(new CellCoord(2, 7), AreaCompanionOrders.Away(new(5, 4), new(6, 3)));
        Assert.AreEqual(new CellCoord(8, 4), AreaCompanionOrders.Away(new(5, 4), new(5, 4)), "on the PC's cell: toward +X");
        Assert.AreEqual(new CellCoord(int.MaxValue, 4), AreaCompanionOrders.Away(new(int.MaxValue - 1, 4), new(0, 4)), "clamped, no overflow");
        Assert.AreEqual("wait", AreaCompanionOrders.Label(FollowerMenuItem.Wait));
    }
}
