using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-045 in the AREA viewer: the loyalty step on a companion's AI tick with the follow-goal gate, the wait timer through
/// the event pump with the area's reaction facts, and AL arming the followers. Over the strict owner fixture, whose scenery
/// body at (5,1) stands in for the companion (see <see cref="AreaCompanionTests"/>); the PC stands on the spawn (6,1).
/// </summary>
[TestClass]
public sealed class AreaCompanionLoyaltyTests
{
    private const CompanionLoyalty ReactionMarks = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestReaction;

    private sealed record Fixture(AreaStepSnapshot Steps, AreaCompanionTemplate Template, AreaSnapshot Area);

    private static Fixture Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var steps = AreaOwnerFixture.Steps(area, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        // Its dialog attachment, so the shown host can list it as a carried companion (as AreaVisitorTalkTests does).
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
            [new AreaSparseElement(ScrPoint.Dialog, [0, 0, AreaCompanionRoster.Virgil.ScriptId])], null);
        var row = scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype, EffectiveFields = [.. scenery.EffectiveFields.Where(f => f.Slot != 31), scripts] };
        var speaker = new AreaSpeaker(0, row, AreaCompanionRoster.Virgil.ScriptId, scenery.WorldX!.Value, scenery.WorldY!.Value);
        return new(steps, AreaCompanionTemplate.From([speaker], AreaScenePlan.Create(area), area.Visuals, AreaChart.Exterior, new(1)).Single(), area);
    }

    private static PartyConsentFacts? Facts(AreaCompanion companion) => AreaCompanionRoster.Facts(companion.Facts, 0, 50);

    [TestMethod]
    public void The_ai_tick_delay_grows_with_the_distance_to_the_pc()
    {
        Assert.AreEqual(250, AreaCompanionLoyalty.AiTickDelayMs(0));
        Assert.AreEqual(250 + 4750 * 4 / 30, AreaCompanionLoyalty.AiTickDelayMs(4));
        Assert.AreEqual(5000, AreaCompanionLoyalty.AiTickDelayMs(30));
        Assert.AreEqual(5000, AreaCompanionLoyalty.AiTickDelayMs(31));
        Assert.AreEqual(5000, AreaCompanionLoyalty.AiTickDelayMs(-1));
        Assert.AreEqual(AreaCompanionLoyalty.MaxAiTickDelayMs, AreaCompanionLoyalty.AiTickDelayMs(long.MaxValue));
    }

    [TestMethod]
    public void A_following_companions_ai_tick_runs_the_loyalty_step_unless_its_follow_goal_lives()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create([f.Template], f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), StepDuration.Of(1)) { Occupancy = group.OccupancyForPc };
        var state = new AreaConversationState(session, new(1), [companion.Entity], 50);

        // A free companion has no tick.
        Assert.AreEqual(0, group.Loyalty(session, pc.State, SimulationTime.At(0), Facts).Count);
        Assert.IsNull(companion.NextAiTick);

        Assert.AreEqual(GameSessionDisposition.Changed, session.JoinParty(session.Version, companion.Entity, Facts(companion)!, false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AdjustReaction(session.Version, companion.Entity, state.NpcFacts(), state.PcFacts, -5).Disposition);
        Assert.AreEqual(ReactionMarks, session.QueryCompanion(companion.Entity).Loyalty);
        var first = group.Loyalty(session, pc.State, SimulationTime.At(0), Facts).Single();
        Assert.AreEqual((GameSessionDisposition.Changed, LoyaltyStepKind.Stayed, CompanionLoyalty.None),
            (first.Result.Disposition, first.Result.Step!.Value.Kind, session.QueryCompanion(companion.Entity).Loyalty));
        // One cell from the PC: the next tick is 250 + 4750 / 30 = 408 ms later.
        Assert.AreEqual(SimulationTime.At(408, 1000), companion.NextAiTick);
        Assert.AreEqual(0, group.Loyalty(session, pc.State, SimulationTime.At(407, 1000), Facts).Count, "not due yet");
        Assert.AreEqual(1, group.Loyalty(session, pc.State, SimulationTime.At(408, 1000), Facts).Count);
        // A host clock that went back further than any delay (a fresh actor session) makes the tick due at once.
        Assert.AreEqual(1, group.Loyalty(session, pc.State, SimulationTime.At(100), Facts).Count);
        Assert.AreEqual(0, group.Loyalty(session, pc.State, SimulationTime.At(99_600, 1000), Facts).Count, "0.8 s back: still waits");
        Assert.AreEqual(1, group.Loyalty(session, pc.State, SimulationTime.At(0), Facts).Count);

        // While its follow goal lives the step does not run (decision D5), and the marks stay.
        companion.FollowGoal = true;
        Assert.AreEqual(GameSessionDisposition.Changed, session.AdjustReaction(session.Version, companion.Entity, state.NpcFacts(), state.PcFacts, -5).Disposition);
        var gated = group.Loyalty(session, pc.State, SimulationTime.At(110), Facts).Single();
        Assert.AreEqual((GameSessionDisposition.NoChange, LoyaltyStepKind.GoalRunning), (gated.Result.Disposition, gated.Result.Step!.Value.Kind));
        Assert.AreEqual(ReactionMarks, session.QueryCompanion(companion.Entity).Loyalty);
        // A conversation with it interrupts the goal; the next tick runs the step.
        group.InterruptGoals(companion.Key);
        Assert.IsFalse(companion.FollowGoal);
        Assert.AreEqual(LoyaltyStepKind.Stayed, group.Loyalty(session, pc.State, SimulationTime.At(120), Facts).Single().Result.Step!.Value.Kind);
        Assert.AreEqual(CompanionLoyalty.None, session.QueryCompanion(companion.Entity).Loyalty);
        // No facts: no step.
        Assert.AreEqual(0, group.Loyalty(session, pc.State, SimulationTime.At(130), _ => null).Count);

        // A refused check makes it leave: free, jilted, its refusal lines named.
        var left = group.Loyalty(session, pc.State, SimulationTime.At(140), c => AreaCompanionRoster.Facts(c.Facts, 0, -1000)).Single();
        Assert.AreEqual(LoyaltyStepKind.Left, left.Result.Step!.Value.Kind);
        Assert.AreEqual("left, refusal lines 1800-1899 (Dislike)", AreaCompanionLoyalty.Shown(left.Result.Step.Value));
        StringAssert.Contains(AreaCompanionLoyalty.Log(companion, left.Result), "ARC-WORLD-045 loyalty script=1324 disposition=Changed");
        Assert.AreEqual((CompanionStanding.Free, true), (companion.Standing, session.QueryCompanion(companion.Entity).Jilted));
        Assert.AreEqual(0, group.Loyalty(session, pc.State, SimulationTime.At(150), Facts).Count);
    }

    [TestMethod]
    public void An_approach_starts_the_follow_goal_and_it_lives_after_arrival()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var group = AreaCompanionGroup.Create([f.Template], f.Steps, AreaChart.Exterior, session, new(1), SimulationTime.At(0));
        var companion = group.Companions.Single();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, companion.Entity).Disposition);
        var pc = new AreaGridSession(f.Steps, SimulationTime.At(0), StepDuration.Of(1)) { Occupancy = group.OccupancyForPc };
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
        Frames(4);
        Assert.IsFalse(companion.FollowGoal, "next to the PC it holds");
        Frames(4 * 9 - 1, NeighborDirection.XPlus);
        Frames(1); pc.Advance(SimulationTime.At(++time, 4), [new(SimulationTime.At(time, 4), [MovementInput.Set(time, ActorIntent.Neutral)])]);
        Frames(4 * 14);
        Assert.AreEqual(RouteFollowStatus.Arrived, companion.Follow.Route.Status);
        Assert.IsTrue(companion.FollowGoal, "the original's follow goal does not end by itself");
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, companion.Entity).Disposition);
        Frames(1);
        Assert.IsFalse(companion.FollowGoal, "waiting ends it");
    }

    private sealed class Objects(string entity) : IAreaEventObjects
    {
        public LifeFacts? Facts(string id) => null;
        public bool HasObject(string id) => id == entity;
    }

    [TestMethod]
    public void A_wait_timer_jilts_through_the_pump_with_the_areas_reaction_facts()
    {
        var f = Load();
        using var session = ConversationSessionFixture.Create();
        var entity = f.Template.Entity;
        var host = AreaVisitorTalkTests.Shown(f.Area, session);
        host.AdmitVisitor(f.Template, out _);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        // Virgil's own wait index 6 has no timer; an index of 1 waits 15 minutes.
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, entity, AreaCompanionRoster.WaitFacts(AreaCompanionRoster.Virgil)).Disposition);
        Assert.IsNull(session.QueryWaitTimer(entity));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, entity,
            AreaCompanionRoster.WaitFacts(AreaCompanionRoster.Virgil with { WaitIndex = 1 })).Disposition);
        var deadline = session.QueryWaitTimer(entity)!.Value;

        var world = new AreaEventWorld(() => new Objects(entity), () => host);
        Assert.AreEqual(host.State.NpcFacts(), world.ReactionNpcFacts(new(1), entity));
        Assert.AreEqual(AreaConversationState.DiagnosticPc, world.ReactionPcFacts());
        Assert.IsNull(new AreaEventWorld(() => null, () => null).ReactionPcFacts(), "no conversation host, no facts");
        var pump = new FrameStep(session, world);
        // Host time up to the last whole millisecond before the deadline, then past it (game time runs 8 times host time).
        Assert.AreEqual(0, pump.AdvanceHost((deadline - session.QueryClockSnapshot().GameTimestampMs - 1) / 8).Waits.Count);
        var report = pump.AdvanceHost(10);
        var firing = report.Waits.Single();
        Assert.AreEqual((entity, 8, -24), (firing.Entity, firing.LeaderCharisma, firing.Reaction!.Value.Delta), "2 × (8 − 20) for the diagnostic PC");
        Assert.AreEqual(new SessionCompanion(entity, CompanionStanding.Free, false, false, true, ReactionMarks), session.QueryCompanion(entity));
        Assert.AreEqual(26, host.State.Reaction(0));
        StringAssert.Contains(AreaCompanionLoyalty.Log(firing), $"ARC-WORLD-045 wait expired entity={entity} map=1 charisma=8 reaction=None:-24 failure=None");
    }

    [TestMethod]
    public void An_alignment_change_in_dialog_arms_the_followers()
    {
        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "oid:a").Disposition);
        var state = new AreaConversationState(session, new(1), ["oid:a"], 50);
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new AlignmentCommand(AreaConversationState.PcParticipant, DialogReactionMode.Adjust, 7)));
        Assert.AreEqual((7, CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood), (state.Alignment, session.QueryCompanion("oid:a").Loyalty));
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new AlignmentCommand(AreaConversationState.PcParticipant, DialogReactionMode.Target, -3)));
        Assert.AreEqual(CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood | CompanionLoyalty.TestTooBad, session.QueryCompanion("oid:a").Loyalty);
        Assert.AreEqual(-3, state.Alignment);
    }
}
