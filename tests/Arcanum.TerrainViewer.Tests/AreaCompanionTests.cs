using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-016 over the strict owner fixture (core sectors 1452..1453 x 1290..1291, blocking scenery at (5,1), an
/// OF_NO_BLOCK canopy at (6,1), spawn (6,1)). The fixture has no critter, so the scenery body at (5,1) stands in for
/// the companion: re-labelled with Virgil's prototype and script, it is the moving T0 record.
/// </summary>
[TestClass]
public sealed class AreaCompanionTests
{
    private static readonly StepDuration Step = StepDuration.Of(1);

    private sealed record Fixture(AreaSnapshot Area, AreaStepSnapshot Steps, AreaSpeaker Speaker, AreaObjectKey Scenery);

    private static Fixture Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(area, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        var speaker = new AreaSpeaker(0, scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype }, AreaCompanionRoster.Virgil.ScriptId,
            scenery.WorldX!.Value, scenery.WorldY!.Value);
        return new(area, steps, speaker, scenery.Key);
    }

    private static IReadOnlyList<AreaCompanionTemplate> Templates(Fixture f, AreaSpeaker? speaker = null) =>
        AreaCompanionTemplate.From([speaker ?? f.Speaker], AreaScenePlan.Create(f.Area), f.Area.Visuals, AreaChart.Exterior, new(1));

    private static AreaStepAssessment Assess(AreaStepSnapshot steps, CellCoord from, NeighborDirection direction) =>
        steps.Assess(steps.Request(AreaGridSession.Cell(from), AreaGridSession.Cell(RoutePlanner.Neighbor(from, direction)), GridReplayDirection(direction)));

    private static CardinalTraversalDirection GridReplayDirection(NeighborDirection direction) => (CardinalTraversalDirection)Enum.Parse(typeof(CardinalTraversalDirection), direction.ToString());

    [TestMethod]
    public void TheLiveOverlayMovesT0RecordsAndBlocksActorsLikeCritters()
    {
        var f = Load();
        var into = Assess(f.Steps, new(6, 1), NeighborDirection.XMinus);
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", into.ReasonCode);
        var raw = AreaOccupancyRules.Apply(into, new(6, 1), NeighborDirection.XMinus, AreaOccupancy.None);
        Assert.AreEqual((CardinalTraversalDisposition.Blocked, "DESTINATION_OBJECT_BLOCKER", false), (raw.Disposition!.Value, raw.ReasonCode, raw.Overlaid));

        // The moved record no longer blocks its T0 cell.
        var moved = new AreaOccupancy([f.Scenery], []);
        var open = AreaOccupancyRules.Apply(into, new(6, 1), NeighborDirection.XMinus, moved);
        Assert.AreEqual((CardinalTraversalCoverage.Supported, (CardinalTraversalDisposition?)CardinalTraversalDisposition.Open, "COMPLETE_NONBLOCKING_EVIDENCE", true),
            (open.Coverage, open.Disposition, open.ReasonCode, open.Overlaid));

        // A live actor blocks the destination, and a diagonal step's shoulders.
        var east = Assess(f.Steps, new(6, 1), NeighborDirection.XPlus);
        Assert.AreEqual(AreaOccupancyRules.DestinationActor, AreaOccupancyRules.Apply(east, new(6, 1), NeighborDirection.XPlus, new([], [new(7, 1)])).ReasonCode);
        var diagonal = Assess(f.Steps, new(8, 3), NeighborDirection.XPlusYPlus);
        Assert.AreEqual(CardinalTraversalDisposition.Open, diagonal.Disposition);
        Assert.AreEqual(AreaOccupancyRules.ShoulderActor, AreaOccupancyRules.Apply(diagonal, new(8, 3), NeighborDirection.XPlusYPlus, new([], [new(9, 3)])).ReasonCode);
        Assert.AreEqual(AreaOccupancyRules.ShoulderActor, AreaOccupancyRules.Apply(diagonal, new(8, 3), NeighborDirection.XPlusYPlus, new([], [new(8, 4)])).ReasonCode);
        Assert.AreEqual("COMPLETE_NONBLOCKING_EVIDENCE", AreaOccupancyRules.Apply(diagonal, new(8, 3), NeighborDirection.XPlusYPlus, new([], [new(7, 3)])).ReasonCode);
        // A T0 blocker still wins over a live actor, and the core edge is never changed.
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", AreaOccupancyRules.Apply(into, new(6, 1), NeighborDirection.XMinus, new([], [new(5, 1)])).ReasonCode);
        var edge = Assess(f.Steps, new(6, 0), NeighborDirection.YMinus);
        Assert.AreEqual(CardinalTraversalCoverage.Unsupported, AreaOccupancyRules.Apply(edge, new(6, 0), NeighborDirection.YMinus, new([f.Scenery], [new(6, -1)])).Coverage);

        // The route source and the session admit with the same overlay.
        var routes = new AreaRouteSource(f.Steps, occupancy: () => moved);
        Assert.AreEqual(RouteStepVerdict.Open, routes.Assess(new(6, 1), NeighborDirection.XMinus));
        var session = new AreaGridSession(f.Steps, SimulationTime.At(0), Step) { Occupancy = () => moved };
        var advance = session.Advance(SimulationTime.At(1), [new(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XMinus))])]);
        Assert.AreEqual(new CellCoord(5, 1), session.State.CommittedCell);
        var evidence = advance.AreaEvidence.First();
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", evidence.Assessment.ReasonCode, "the T0 assessment is kept as evidence");
        Assert.AreEqual("COMPLETE_NONBLOCKING_EVIDENCE", evidence.Live!.Value.ReasonCode, "with the verdict it was admitted with");
    }

    [TestMethod]
    public void PlacementPacksOneCellAndFacingPerChange()
    {
        var value = AreaCompanionPlacement.Encode(new(92933, 82561), NeighborDirection.YMinus);
        Assert.AreEqual((new CellCoord(92933, 82561), NeighborDirection.YMinus), AreaCompanionPlacement.Decode(value));
        Assert.AreEqual((new CellCoord(0, 0), NeighborDirection.XMinusYMinus), AreaCompanionPlacement.Decode(0));
        Assert.IsNull(AreaCompanionPlacement.Decode(-1));
        Assert.IsNull(AreaCompanionPlacement.Decode(1L << 61));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaCompanionPlacement.Encode(new(-1, 0), NeighborDirection.XPlus));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaCompanionPlacement.Encode(new(0, 1 << 28), NeighborDirection.XPlus));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaCompanionPlacement.Encode(new(0, 0), (NeighborDirection)8));
        Assert.AreEqual(MapChangeKey.ForEntity("oid:x", "npc.cell"), AreaCompanionPlacement.Key("oid:x"));
    }

    [TestMethod]
    public void RemovedObjectsAreMovedInEveryOverlay()
    {
        // ARC-WORLD-065: the objects the session destroyed or switched off are moved for the PC and for each companion.
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var removedKey = f.Area.Objects.First(o => o.Key != f.Scenery).Key;
        IReadOnlySet<AreaObjectKey> removed = new HashSet<AreaObjectKey> { removedKey };
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        Assert.IsFalse(group.OccupancyForPc().Moved.Contains(removedKey));
        group.Removed = () => removed;
        Assert.IsTrue(group.OccupancyForPc().Moved.IsSupersetOf([removedKey, f.Scenery]));
        Assert.IsTrue(group.Companions.Single().Grid.Occupancy!.Invoke()!.Moved.Contains(removedKey));
        var empty = AreaCompanionGroup.Create([], f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        Assert.AreSame(AreaOccupancy.None, empty.OccupancyForPc());
        empty.Removed = () => removed;
        CollectionAssert.AreEquivalent(new[] { removedKey }, empty.OccupancyForPc().Moved.ToArray());
    }

    // ARC-DEV-105 (MIG9 S5): the AI host's catch-up move of a companion.
    [TestMethod]
    public void TheAiHostsJumpStandsTheCompanionOnTheCellAndEndsItsOrderedGoal()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        Assert.IsTrue(group.StepTo(companion.Entity, new(6, 1), false), "an ordered step is under way");
        Assert.IsTrue(companion.Follow.Ordered);
        var facing = companion.Grid.State.Facing;
        Assert.IsTrue(group.JumpTo(companion.Entity, new(10, 1)));
        Assert.AreEqual((new CellCoord(10, 1), facing, 1), (companion.Grid.State.CommittedCell, companion.Grid.State.Facing, companion.Jumps));
        Assert.IsFalse(companion.Follow.Ordered, "the interrupt ended the ordered goal");
        Assert.AreEqual(RouteFollowStatus.Stopped, companion.Follow.Route.Status);
        Assert.IsFalse(group.JumpTo(companion.Entity, new(9999, 9999)), "a cell outside the step evidence is refused");
        Assert.IsFalse(group.JumpTo("oid:nobody", new(10, 1)));
        Assert.AreEqual(1, companion.Jumps);
    }

    [TestMethod]
    public void AFollowingCompanionWalksAfterThePcStopsWithinTwoAndIsDrawnWhereItStands()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        Assert.AreEqual((new CellCoord(5, 1), CompanionStanding.Free), (companion.Grid.State.CommittedCell, companion.Standing));
        Assert.AreEqual("oid:" + f.Scenery.ObjectIdSha256, companion.Entity);

        // The PC walks 9 cells east; a free NPC does not follow.
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), Step) { Occupancy = group.OccupancyForPc };
        var time = 0;
        void Frames(int count, NeighborDirection? walk = null)
        {
            for (var i = 0; i < count; i++)
            {
                var frontier = SimulationTime.At(++time, 4);
                var input = walk is { } d && i == 0 ? new[] { new InputBatch(SimulationTime.At(time, 4), [MovementInput.Set(time, ActorIntent.Toward(d))]) } : [];
                pc.Advance(frontier, input);
                group.Drive(session, pc.State, frontier);
            }
        }
        Frames(4 * 9 - 1, NeighborDirection.XPlus);
        Frames(1); pc.Advance(SimulationTime.At(++time, 4), [new(SimulationTime.At(time, 4), [MovementInput.Set(time, ActorIntent.Neutral)])]);
        Frames(8);
        Assert.AreEqual(MovementPhase.Settled, pc.State.Phase);
        var pcCell = pc.State.CommittedCell;
        Assert.IsTrue(pcCell.X >= 14, $"the PC walked east: {pcCell}");
        Assert.AreEqual(new CellCoord(5, 1), companion.Grid.State.CommittedCell, "a free NPC stays");

        // Joined: it follows and stops within 2 of the PC.
        Assert.AreEqual(GameSessionDisposition.Changed, session.JoinParty(session.Version, companion.Entity,
            AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, 0, 50), false).Disposition);
        Frames(4 * 14);
        Assert.AreEqual(CompanionStanding.Following, companion.Standing);
        var cell = companion.Grid.State.CommittedCell;
        Assert.AreEqual(MovementPhase.Settled, companion.Grid.State.Phase);
        Assert.AreEqual(2, FollowDistanceRules.Distance(cell, pcCell), $"stops at range 2: {cell} vs PC {pcCell}");
        Assert.AreEqual(RouteFollowStatus.Arrived, companion.Follow.Route.Status);
        Assert.AreEqual((pcCell.X - 2, 1), (cell.X, cell.Y));

        // Its body is drawn on its cell: the T0 command moved by the projected offset and ordered at the new cell.
        var plan = AreaScenePlan.Create(f.Area);
        var t0 = plan.Commands.Single(c => c.Identity == f.Scenery);
        var drawn = group.Commands().Single();
        Assert.AreEqual((t0.Destination.X + 40 * -(cell.X - 5), t0.Destination.Y + 20 * (cell.X - 5)), (drawn.Destination.X, drawn.Destination.Y));
        Assert.AreEqual(((int)AreaGridSession.ChartOriginX + cell.X, (int)AreaGridSession.ChartOriginY + 1), (drawn.MapX, drawn.MapY));
        Assert.AreEqual((t0.Frame, t0.Visual, t0.Destination.Width), (drawn.Frame, drawn.Visual, drawn.Destination.Width));
        Assert.AreEqual((f.Speaker.WorldX + cell.X - 5, f.Speaker.WorldY), group.WorldCell(f.Speaker));
        // The hover/click pick sees it where it is drawn, not on its T0 cell.
        var hover = new AreaHoverIndex(f.Area.Objects, plan.Commands, null);
        bool Under(AreaDrawCommand c, AreaDrawCommand at) => c.Destination == at.Destination;
        Assert.AreEqual(f.Scenery, hover.Pick(c => Under(c, drawn), group.Commands()));
        Assert.IsNull(hover.Pick(c => Under(c, t0), group.Commands()), "nothing is picked on the empty T0 cell");
        Assert.AreEqual(f.Scenery, hover.Pick(c => Under(c, t0)), "without the moved bodies the T0 pick is unchanged");

        // The PC cannot walk into it; its T0 cell no longer blocks the PC.
        Assert.AreEqual(RouteStepVerdict.Blocked, new AreaRouteSource(f.Steps, occupancy: group.OccupancyForPc).Assess(new(cell.X + 1, 1), NeighborDirection.XMinus));
        Assert.AreEqual(RouteStepVerdict.Open, new AreaRouteSource(f.Steps, occupancy: group.OccupancyForPc).Assess(new(6, 1), NeighborDirection.XMinus));

        // Waiting: the PC walks away and the companion stays.
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, companion.Entity).Disposition);
        Frames(4 * 6, NeighborDirection.XPlus);
        Assert.AreEqual(cell, companion.Grid.State.CommittedCell, "a waiting companion does not follow");
    }

    [TestMethod]
    public void AFarCompanionRunsAndANearOneWalksWithGaitTiming()
    {
        // ARC-WORLD-020: beyond range + 2 the follow decision is a run; with gait timing its steps take the run duration.
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0), GaitTiming.HumanFixed);
        var companion = group.Companions.Single();
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), Step) { Occupancy = group.OccupancyForPc };
        pc.Advance(SimulationTime.At(9), [new(SimulationTime.At(0), [MovementInput.Set(0, ActorIntent.Toward(NeighborDirection.XPlus))])]);
        pc.Advance(SimulationTime.At(11), [new(SimulationTime.At(19, 2), [MovementInput.Set(1, ActorIntent.Neutral)])]);
        Assert.AreEqual(MovementPhase.Settled, pc.State.Phase);
        Assert.AreEqual(GameSessionDisposition.Changed, session.JoinParty(session.Version, companion.Entity,
            AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, 0, 50), false).Disposition);
        var permits = new List<StepAssessmentTiming>();
        // Frames of 1/10 s: the follow decision is re-made while the route runs.
        for (var i = 1; i <= 100; i++)
            foreach (var (_, advance) in group.Drive(session, pc.State, SimulationTime.At(110 + i, 10)))
                permits.AddRange(advance.Movement.Events.Where(e => e.Kind == MovementEventKind.StepStarted).Select(e => e.Assessment!));
        Assert.AreEqual(FollowAction.Run, FollowDistanceRules.Decide(new(5, 1), pc.State.CommittedCell, false, false));
        var first = permits.First();
        CollectionAssert.Contains(first.Provenance.ToArray(), GaitTiming.RunProvenance);
        Assert.AreEqual(GaitTiming.HumanFixed.For(MovementGait.Run, first.Key.Direction), first.Duration);
        // The last steps, within range + 2 of the PC, walk; it stops within 2.
        var last = permits.Last();
        CollectionAssert.Contains(last.Provenance.ToArray(), GaitTiming.WalkProvenance);
        Assert.AreEqual(GaitTiming.HumanFixed.For(MovementGait.Walk, last.Key.Direction), last.Duration);
        Assert.AreEqual(2, FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc.State.CommittedCell));
    }

    [TestMethod]
    public void ThePlacementIsSessionStateThatSurvivesASaveAndRestoresTheCompanion()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        Assert.AreEqual(GameSessionFailure.None, group.Record(session));
        Assert.IsNull(session.QueryMapChange(new(1), AreaCompanionPlacement.Key(group.Companions[0].Entity)), "on its T0 cell: no change");

        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, group.Companions[0].Entity).Disposition);
        // The PC far away: beyond 30 cells the companion is stood on the PC's cell, then steps off it.
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), Step, start: new(40, 20)) { Occupancy = group.OccupancyForPc };
        for (var i = 1; i <= 8; i++) { pc.Advance(SimulationTime.At(i, 4), []); group.Drive(session, pc.State, SimulationTime.At(i, 4)); }
        var companion = group.Companions[0];
        Assert.AreEqual(1, companion.Jumps);
        Assert.AreEqual(1, FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, new(40, 20)), "it left the PC's cell for a neighbour");
        Assert.AreEqual(MovementPhase.Settled, companion.Grid.State.Phase);

        Assert.AreEqual(GameSessionFailure.None, group.Record(session));
        var world = AreaChart.Exterior.ToWorld(companion.Grid.State.CommittedCell);
        Assert.AreEqual(AreaCompanionPlacement.Encode(new((int)world.X, (int)world.Y), companion.Grid.State.Facing),
            session.QueryMapChange(new(1), AreaCompanionPlacement.Key(companion.Entity)));
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var again = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, loaded, new(1), SimulationTime.At(9));
        Assert.AreEqual((companion.Grid.State.CommittedCell, companion.Grid.State.Facing, CompanionStanding.Following),
            (again.Companions[0].Grid.State.CommittedCell, again.Companions[0].Grid.State.Facing, again.Companions[0].Standing));

        // A placement the chart cannot stand is skipped, not guessed.
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.SetMapChange(loaded.Version, new(1), AreaCompanionPlacement.Key(companion.Entity),
            AreaCompanionPlacement.Encode(new(10, 10), NeighborDirection.XPlus)).Disposition);
        var skipped = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, loaded, new(1), SimulationTime.At(9));
        Assert.AreEqual(0, skipped.Companions.Count);
        Assert.AreEqual(1, skipped.Skipped.Count);
        // Other speakers are not companions.
        var other = f.Speaker with { ScriptId = 1325 };
        Assert.AreEqual(0, AreaCompanionGroup.Create(Templates(f, other), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0)).Companions.Count);
    }
    [TestMethod]
    public void A_companion_passes_critters_while_the_session_has_it_out_of_combat_mode()
    {
        // ARC-WORLD-075: each drive reads the companion's combat mode; its grid (after a jump too) and its plan follow it.
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create(Templates(f), f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions[0];
        Assert.IsFalse(companion.PassesCritters, "not read before the first drive");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, companion.Entity).Disposition);
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), Step, start: new(40, 20)) { Occupancy = group.OccupancyForPc };
        var time = 0;
        void Frames(int count) { for (var i = 0; i < count; i++) { pc.Advance(SimulationTime.At(++time, 4), []); group.Drive(session, pc.State, SimulationTime.At(time, 4)); } }
        Frames(8);
        Assert.AreEqual(1, companion.Jumps);
        Assert.IsTrue(companion.PassesCritters && companion.Grid.CrittersPass?.Invoke() == true, "the jump's new grid keeps the switch");

        var entered = session.SetCombatMode(session.Version, SessionCombatant.Critter(companion.Entity), true,
            new() { Facts = new() { Speed = 7, Life = new Arcanum.Simulation.Combat.LifeFacts() } });
        Assert.IsTrue(entered.InCombatMode, entered.Failure.ToString());
        Frames(1);
        Assert.IsFalse(companion.PassesCritters || companion.Grid.CrittersPass?.Invoke() == true, "in combat mode critters block it");
    }

    [TestMethod]
    public void A_following_companion_walks_through_an_NPC_record_and_Original_stops_before_it_once()
    {
        // ARC-WORLD-075 through the group: Virgil stands on (10,30) of the open steps of AreaCritterPassTests, an NPC record on (18,30),
        // the eighth cell of his first segment toward a PC on (30,30).
        var f = Load();
        var steps = AreaCritterPassTests.Steps();
        var chart = AreaCritterPassTests.Chart;
        var speaker = f.Speaker with { WorldX = 10, WorldY = 30 };
        var templates = AreaCompanionTemplate.From([speaker], AreaScenePlan.Create(f.Area), f.Area.Visuals, chart, new(1));
        (HashSet<CellCoord> Visited, List<string?> Stops) Walk(RuleMode mode, bool npcDead)
        {
            using var session = ConversationSessionFixture.Create();
            Assert.AreNotEqual(GameSessionDisposition.Rejected, session.SelectRules(mode));
            var group = AreaCompanionGroup.Create(templates, steps, chart, session, new(1), SimulationTime.At(0));
            if (npcDead) group.Dead = key => key == AreaCritterPassTests.FarNpc;
            var companion = group.Companions.Single();
            Assert.AreEqual(new CellCoord(10, 30), companion.Grid.State.CommittedCell);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, companion.Entity).Disposition);
            var pc = new AreaGridSession(steps, SimulationTime.At(0), Step, chart: chart, start: new(30, 30)) { Occupancy = group.OccupancyForPc };
            var visited = new HashSet<CellCoord>(); var stops = new List<string?>();
            for (var i = 1; i <= 120; i++)
            {
                pc.Advance(SimulationTime.At(i, 4), []);
                group.Drive(session, pc.State, SimulationTime.At(i, 4));
                visited.Add(companion.Grid.State.CommittedCell);
                if (companion.Follow.Route.Status == RouteFollowStatus.Stopped && stops.LastOrDefault() != companion.Follow.Route.StopReason)
                    stops.Add(companion.Follow.Route.StopReason);
            }
            Assert.AreEqual(new CellCoord(28, 30), companion.Grid.State.CommittedCell, string.Join(",", stops));
            return (visited, stops);
        }
        var steamwoven = Walk(RuleMode.Steamwoven, npcDead: false);
        Assert.IsTrue(steamwoven.Visited.Contains(new(18, 30)) && steamwoven.Stops.Count == 0, "Steamwoven walks through the NPC");
        var original = Walk(RuleMode.Original, npcDead: false);
        Assert.IsTrue(original.Visited.Contains(new(18, 30)));
        CollectionAssert.AreEqual(new[] { CompanionFollower.SegmentEndCritterReason }, original.Stops, "Original stops before it once, then plans again");
        Assert.AreEqual(0, Walk(RuleMode.Original, npcDead: true).Stops.Count, "a dead NPC is not a living critter at the segment end");
    }

    // ARC-WORLD-017: the fixture stands for both maps of a transfer, home map 1 and a second map 2 on the same chart.
    [TestMethod]
    public void AFollowingCompanionGoesWithThePcToTheArrivalCellAndBackAndAWaitingOneStays()
    {
        var f = Load();
        var templates = Templates(f);
        using var session = ConversationSessionFixture.Create();
        SessionMapId home = new(1), away = new(2);
        var entity = templates[0].Entity;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        var group = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, home, SimulationTime.At(0));
        Assert.AreEqual(0, group.Absent.Count);
        Assert.IsTrue(group.Companions.Single().Template.Home == home);
        Assert.ThrowsExactly<ArgumentException>(() => group.Carry(session, home, new(0, 0)));

        // The PC leaves map 1 for map 2 at (12,3): the follower goes onto the same arrival cell, at any distance, in one revision.
        var arrival = new CellCoord(12, 3);
        var arrivalWorld = AreaChart.Exterior.ToWorld(arrival);
        var world = new CellCoord((int)arrivalWorld.X, (int)arrivalWorld.Y);
        var carry = group.Carry(session, away, world);
        Assert.AreEqual(1, carry.Carried.Count);
        var revision = session.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, new ActorPlacement(away, world, NeighborDirection.XPlus), carry.Changes).Disposition);
        Assert.AreEqual(revision + 1, session.Version.Revision, "the PC and its followers move in one session step");
        Assert.AreEqual(2L, session.QueryMapChange(home, AreaCompanionPlacement.MapKey(entity)));
        Assert.AreEqual(AreaCompanionPlacement.Encode(world, NeighborDirection.XPlus), session.QueryMapChange(away, AreaCompanionPlacement.Key(entity)));
        Assert.IsNull(session.QueryMapChange(home, AreaCompanionPlacement.Key(entity)));
        Assert.AreEqual(new AreaCompanionWhere(away, (world, NeighborDirection.XPlus), null), AreaCompanionPlacement.Where(session, home, entity));

        // On map 1 it is absent: not a companion there, and its T0 record no longer blocks the PC.
        var left = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, home, SimulationTime.At(0));
        Assert.AreEqual((0, true), (left.Companions.Count, left.Absent.Contains(f.Scenery)));
        Assert.AreEqual(RouteStepVerdict.Open, new AreaRouteSource(f.Steps, occupancy: left.OccupancyForPc).Assess(new(6, 1), NeighborDirection.XMinus));
        Assert.AreEqual(RouteStepVerdict.Blocked, new AreaRouteSource(f.Steps).Assess(new(6, 1), NeighborDirection.XMinus), "without the group the T0 record blocks");
        Assert.AreEqual(0, left.Commands().Count);

        // On map 2 it is a visitor on the arrival cell; with the PC on the same cell it steps off to a neighbour.
        var visit = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, away, SimulationTime.At(0));
        var visitor = visit.Companions.Single();
        Assert.AreEqual((arrival, CompanionStanding.Following, 0), (visitor.Grid.State.CommittedCell, visitor.Standing, visit.Absent.Count));
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), Step, start: arrival) { Occupancy = visit.OccupancyForPc };
        for (var i = 1; i <= 12; i++) { pc.Advance(SimulationTime.At(i, 4), []); visit.Drive(session, pc.State, SimulationTime.At(i, 4)); }
        var cell = visitor.Grid.State.CommittedCell;
        Assert.AreEqual((1, MovementPhase.Settled), (FollowDistanceRules.Distance(cell, arrival), visitor.Grid.State.Phase), visit.Status());
        // Drawn there with its T0 body (moved by the projected offset from its home T0 cell) and lit where it stands.
        var t0 = AreaScenePlan.Create(f.Area).Commands.Single(c => c.Identity == f.Scenery);
        var drawn = visit.Commands().Single();
        int du = cell.X - 5, dv = cell.Y - 1;
        Assert.AreEqual((t0.Destination.X + 40 * (dv - du), t0.Destination.Y + 20 * (du + dv), t0.Frame), (drawn.Destination.X, drawn.Destination.Y, drawn.Frame));
        var pair = new AmbientPair(new(200, 100, 50), new(10, 20, 30));
        var channel = AreaAmbientPolicy.Channel(templates[0].Visuals[drawn.Visual].RawArt);
        var ambient = AreaLocalLighting.Ambient(channel, pair);
        Assert.AreEqual(new AreaLitColors(ambient, ambient), visit.Light(drawn, pair, null));
        Assert.IsNull(visit.Light(t0 with { Identity = null }, pair, null), "only the group's companion bodies");

        // Its cell on map 2 survives a save and load.
        Assert.AreEqual(GameSessionFailure.None, visit.Record(session));
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var restored = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, away, SimulationTime.At(0));
        Assert.AreEqual(cell, restored.Companions.Single().Grid.State.CommittedCell);
        Assert.IsTrue(AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, home, SimulationTime.At(0)).Absent.Contains(f.Scenery));

        // Waiting on map 2: the PC goes home alone; it stays where it stands.
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.WaitParty(loaded.Version, entity).Disposition);
        var back = restored.Carry(loaded, home, new(checked((int)f.Speaker.WorldX + 3), checked((int)f.Speaker.WorldY)));
        Assert.AreEqual(0, back.Carried.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.ApplyChanges(loaded.Version, new ActorPlacement(home, world, NeighborDirection.XPlus), back.Changes).Disposition);
        Assert.AreEqual(away, AreaCompanionPlacement.Where(loaded, home, entity).Map);
        Assert.IsTrue(AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, home, SimulationTime.At(0)).Absent.Contains(f.Scenery));

        // Following again, it comes home with the PC: the away map is forgotten and its map 2 cell cleared.
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.UnwaitParty(loaded.Version, entity,
            AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, 0, 50), true).Disposition);
        var homeArrival = new CellCoord(checked((int)f.Speaker.WorldX + 3), checked((int)f.Speaker.WorldY));
        var returning = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, away, SimulationTime.At(0)).Carry(loaded, home, homeArrival);
        Assert.AreEqual(1, returning.Carried.Count);
        var before = loaded.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, loaded.ApplyChanges(loaded.Version, new ActorPlacement(home, homeArrival, NeighborDirection.XPlus), returning.Changes).Disposition);
        Assert.AreEqual(before + 1, loaded.Version.Revision, "the way back is one session step too");
        Assert.IsNull(loaded.QueryMapChange(home, AreaCompanionPlacement.MapKey(entity)));
        Assert.IsNull(loaded.QueryMapChange(away, AreaCompanionPlacement.Key(entity)));
        Assert.AreEqual(0, loaded.QueryMapChanges(away).Count);
        var home2 = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, home, SimulationTime.At(0));
        Assert.AreEqual((AreaChart.Exterior.ToChart(new(homeArrival.X, homeArrival.Y)), 0), (home2.Companions.Single().Grid.State.CommittedCell, home2.Absent.Count));
        Assert.AreEqual(0, AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, loaded, away, SimulationTime.At(0)).Companions.Count);
    }

    // ARC-DEV-073 (MIG10, research FM2): an unconscious follower is ordered to wait by the change of place and stays on the map the PC
    // leaves; the request's "no wait orders" flag takes it along; the session applies the order with the placement in one revision.
    [TestMethod]
    public void AnUnconsciousFollowerIsOrderedToWaitByTheMapChangeAndStaysWhereItStands()
    {
        var f = Load();
        var templates = Templates(f);
        SessionMapId home = new(1), away = new(2);
        var entity = templates[0].Entity;
        var arrival = AreaChart.Exterior.ToWorld(new CellCoord(12, 3));
        var world = new CellCoord((int)arrival.X, (int)arrival.Y);

        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        var group = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, home, SimulationTime.At(0));
        var facts = new SessionTransferFacts { NoFatigue = who => who == entity, WaitFacts = _ => AreaCompanionRoster.WaitFacts(AreaCompanionRoster.Virgil) };
        var leaving = group.Leaving(session, facts);
        Assert.AreEqual(0, leaving.Companions.Count, "it does not leave with the PC");
        Assert.AreEqual(entity, leaving.MoveList.Waiting.Single().Entity);
        var carry = group.Carry(session, away, world, facts);
        Assert.AreEqual(0, carry.Carried.Count);
        var revision = session.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed,
            session.ApplyChanges(session.Version, new ActorPlacement(away, world, NeighborDirection.XPlus), carry.Changes, carry.MoveList).Disposition);
        Assert.AreEqual(revision + 1, session.Version.Revision);
        Assert.AreEqual(CompanionStanding.Waiting, session.QueryCompanion(entity).Standing);
        Assert.AreEqual(home, AreaCompanionPlacement.Where(session, home, entity).Map, "it never left its map");
        Assert.AreEqual(1, AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, home, SimulationTime.At(0)).Companions.Count);

        using var flagged = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionDisposition.Changed, flagged.ForceJoinParty(flagged.Version, entity).Disposition);
        var along = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, flagged, home, SimulationTime.At(0))
            .Carry(flagged, away, world, new SessionTransferFacts { NoFatigue = _ => true, NoWaitOrders = true });
        Assert.AreEqual(1, along.Carried.Count, "no wait orders: the unconscious follower travels");
        Assert.AreEqual(0, along.MoveList.Waiting.Count);
    }

    [TestMethod]
    public void AnUnreadableAwayMapOrAMissingAwayPlacementIsReportedNotGuessed()
    {
        var f = Load();
        var templates = Templates(f);
        var entity = templates[0].Entity;
        using var session = ConversationSessionFixture.Create();
        // The away map names the home map itself: unreadable, skipped on the home map.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(1), AreaCompanionPlacement.MapKey(entity), 1).Disposition);
        Assert.AreEqual("map unreadable", AreaCompanionPlacement.Where(session, new(1), entity).Error);
        var home = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        Assert.AreEqual((0, 1, 1), (home.Companions.Count, home.Skipped.Count, home.Absent.Count), "reported, and its T0 body does not stand in");
        Assert.IsTrue(new AreaRouteSource(f.Steps, occupancy: home.OccupancyForPc).Assess(new(6, 1), NeighborDirection.XMinus) == RouteStepVerdict.Open);
        // Away on map 2 without a cell there: absent at home, skipped on map 2.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(1), AreaCompanionPlacement.MapKey(entity), 2).Disposition);
        Assert.AreEqual(1, AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0)).Absent.Count);
        var away = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, new(2), SimulationTime.At(0));
        Assert.AreEqual((0, "placement missing on map 2"), (away.Companions.Count, AreaCompanionPlacement.Where(session, new(1), entity).Error));
        Assert.AreEqual(1, away.Skipped.Count);
    }

    [TestMethod]
    public void TemplatesCarryTheT0BodyAndAddItsFramesToAnotherMap()
    {
        var f = Load();
        var plan = AreaScenePlan.Create(f.Area);
        var template = Templates(f).Single();
        Assert.AreEqual((new CellCoord(5, 1), new SessionMapId(1)), (template.HomeOrigin, template.Home));
        Assert.IsTrue(template.Commands.Count > 0 && template.Commands.All(c => c.Identity == f.Scenery && template.Visuals.ContainsKey(c.Visual)));
        var frames = template.Commands.Select(c => c.Frame).Distinct().ToDictionary(k => k, _ => new DecodedAreaFrame(1, 1, [1, 2, 3, 4]));
        var merged = AreaCompanionTemplate.Frames(new Dictionary<AreaFrameKey, DecodedAreaFrame>(), [template], frames);
        Assert.AreEqual(frames.Count, merged.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaCompanionTemplate.Frames(new Dictionary<AreaFrameKey, DecodedAreaFrame>(), [template], new Dictionary<AreaFrameKey, DecodedAreaFrame>()));
        var other = frames.ToDictionary(p => p.Key, _ => new DecodedAreaFrame(1, 1, [9, 9, 9, 9]));
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaCompanionTemplate.Frames(other, [template], frames), "a conflicting frame is refused");
        Assert.AreEqual(0, AreaCompanionTemplate.From([f.Speaker with { ScriptId = 1325 }], plan, f.Area.Visuals, AreaChart.Exterior, new(1)).Count);
    }
    // A visitor on a map with another chart origin is drawn where that map's plan would draw a body on its cell.
    [TestMethod]
    public void AVisitorOnAShiftedChartIsDrawnByThatChartsProjection()
    {
        var f = Load();
        var templates = Templates(f);
        var entity = templates[0].Entity;
        var shifted = new AreaChart(AreaChart.Exterior.OriginX + 3, AreaChart.Exterior.OriginY - 2, new(6, 3), "TEST_SHIFTED_CHART");
        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        var world = AreaChart.Exterior.ToWorld(new(14, 4));
        var arrival = new CellCoord((int)world.X, (int)world.Y);
        var carry = AreaCompanionGroup.Create(templates, f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0)).Carry(session, new(2), arrival);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, new ActorPlacement(new(2), arrival, NeighborDirection.XPlus), carry.Changes).Disposition);
        var visit = AreaCompanionGroup.Create(templates, f.Steps, shifted, session, new(2), SimulationTime.At(0));
        Assert.AreEqual(shifted.ToChart(new(world.X, world.Y)), visit.Companions.Single().Grid.State.CommittedCell);
        var t0 = templates[0].Commands.Single();
        var drawn = visit.Commands().Single();
        var home = TerrainGeometry.Project(checked((int)(f.Speaker.WorldX - AreaChart.Exterior.OriginX)), checked((int)(f.Speaker.WorldY - AreaChart.Exterior.OriginY)));
        var here = TerrainGeometry.Project(checked((int)(world.X - shifted.OriginX)), checked((int)(world.Y - shifted.OriginY)));
        Assert.AreEqual((t0.Destination.X - home.X + here.X, t0.Destination.Y - home.Y + here.Y), (drawn.Destination.X, drawn.Destination.Y));
        Assert.AreEqual(((int)world.X, (int)world.Y), (drawn.MapX, drawn.MapY));
    }

    [TestMethod]
    public void A_following_companion_arrives_with_the_pc_from_any_map_and_comes_home()
    {
        // ARC-WORLD-037: map 1 (home) → 2 → 3 → 1, each arrival written in the PC's session step; on the same map only the cell.
        var f = Load();
        var template = Templates(f).Single();
        using var session = ConversationSessionFixture.Create();
        SessionMapId home = new(1), second = new(2), third = new(3);
        var entity = template.Entity;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        CellCoord At(int x) => new(checked((int)f.Speaker.WorldX + x), checked((int)f.Speaker.WorldY));
        void Go(SessionMapId from, SessionMapId to, CellCoord cell) => Assert.AreNotEqual(GameSessionDisposition.Rejected,
            session.ApplyChanges(session.Version, new ActorPlacement(to, cell, NeighborDirection.XPlus),
                AreaCompanionGroup.Arrive(template, from, to, cell, NeighborDirection.YPlus)).Disposition);
        Go(home, second, At(2));
        Go(second, third, At(3));
        Assert.AreEqual(new AreaCompanionWhere(third, (At(3), NeighborDirection.YPlus), null), AreaCompanionPlacement.Where(session, home, entity));
        Assert.IsNull(session.QueryMapChange(second, AreaCompanionPlacement.Key(entity)), "the map it left forgets it");
        Assert.AreEqual(1, AreaCompanionGroup.Create([template], f.Steps, AreaChart.Exterior, session, third, SimulationTime.At(0)).Companions.Count);
        Assert.AreEqual(0, AreaCompanionGroup.Create([template], f.Steps, AreaChart.Exterior, session, second, SimulationTime.At(0)).Companions.Count);
        Assert.AreEqual(2, AreaCompanionGroup.Arrive(template, third, third, At(4), NeighborDirection.XPlus).Count, "on the same map only the cell (and its home map's stored cell, ARC-DEV-079)");
        // ARC-DEV-079 (MIG4): the home map keeps the cell its stored object lies on for as long as the companion is not on it.
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, home, entity));
        Go(third, home, At(1));
        Assert.IsNull(session.QueryMapChange(home, AreaCompanionPlacement.MapKey(entity)), "home again: no away map");
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, home, entity), "off its stored cell: still recorded");
        Assert.AreEqual(new AreaCompanionWhere(home, (At(1), NeighborDirection.YPlus), null), AreaCompanionPlacement.Where(session, home, entity));
    }

    // ARC-DEV-116 (MIG11 S5): the host of a former follower's change of place: the companion's own edits, as the viewer's homecoming host writes them.
    private sealed class HomecomingHost(AreaCompanionTemplate template, SessionMapId from, NeighborDirection facing) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => true;
        public SessionTransferFacts TransferFacts(Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceRequest request) => new();
        public ChangeOfPlaceReadiness ReadyDestination(Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) => ChangeOfPlaceReadiness.Ready;
        public ChangeOfPlaceArrival Arrive(Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) =>
            new(request.Destination, facing, AreaCompanionGroup.Arrive(template, from, destination, request.Destination, facing).ToArray());
        public bool Run(Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceStep step, Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceRequest request) => true;
    }

    [TestMethod]
    public void A_former_follower_left_on_another_map_goes_home_through_its_teleport_map_and_the_hero_stays()
    {
        // ARC-DEV-116: the companion is carried 1 → 2 (FM6 gives it map 1 as its teleport map), released there, and its home request puts it on map 1.
        var f = Load();
        var template = Templates(f).Single();
        using var session = ConversationSessionFixture.Create();
        SessionMapId home = new(1), cave = new(2);
        var entity = template.Entity;
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        CellCoord At(int x) => new(checked((int)f.Speaker.WorldX + x), checked((int)f.Speaker.WorldY));
        Assert.IsNull(session.QueryTeleportMap(entity));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, new ActorPlacement(cave, At(2), NeighborDirection.XPlus),
            AreaCompanionGroup.Arrive(template, home, cave, At(2), NeighborDirection.YPlus), session.QueryTransferMoveList(new())).Disposition);
        Assert.AreEqual(home.RuntimeMap, session.QueryTeleportMap(entity), "the hero's change prepared the follower with the map it left");
        Assert.AreEqual(GameSessionDisposition.Changed, session.LeaveParty(session.Version, entity, new PartyLeave(Force: true)).Disposition);
        Assert.AreEqual(new AreaCompanionWhere(cave, (At(2), NeighborDirection.YPlus), null), AreaCompanionPlacement.Where(session, home, entity));

        // The forced pass on the cave: its teleport map is another map, so the request goes to it (the cell is the home map's standpoint).
        var request = session.RequestChangeOfPlace(entity, Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceCaller.NpcWaypoint, home.RuntimeMap, At(1),
            Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceFlags.None, 0, map => map == home.RuntimeMap);
        Assert.IsTrue(request.Accepted, request.Status.ToString());
        var run = session.ProcessChangeOfPlace(new HomecomingHost(template, cave, NeighborDirection.XMinus));
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status, run.Failure.ToString());
        Assert.AreEqual(new AreaCompanionWhere(home, (At(1), NeighborDirection.XMinus), null), AreaCompanionPlacement.Where(session, home, entity), "home again, on the cell asked for");
        Assert.IsNull(session.QueryMapChange(cave, AreaCompanionPlacement.Key(entity)), "the cave forgets it");
        Assert.IsNull(session.QueryMapChange(home, AreaCompanionPlacement.MapKey(entity)), "no away map: it stands at home");
        Assert.AreEqual(new ActorPlacement(cave, At(2), NeighborDirection.XPlus), session.QueryActor(), "the hero stays where it is (CP11)");
        Assert.AreEqual(home.RuntimeMap, session.QueryTeleportMap(entity), "the tie stays");
        Assert.AreEqual(0, AreaCompanionGroup.Create([template], f.Steps, AreaChart.Exterior, session, cave, SimulationTime.At(0)).Companions.Count, "the cave's group no longer holds it");
        Assert.AreEqual(1, AreaCompanionGroup.Create([template], f.Steps, AreaChart.Exterior, session, home, SimulationTime.At(0)).Companions.Count);
    }
}
