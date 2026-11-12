using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-073 (MIG10): the party side of the PC's change of place in the session (research FM1, FM2, FM6, RL22, RL23, RL28).
// Entities are invented; the session has no hero (the PC is the actor).
[TestClass]
public sealed class TransferParityStateTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg", Rat = "oid:rat", Stranger = "oid:stranger";
    private static readonly SessionCombatant HeroHandle = SessionCombatant.Hero, RatHandle = SessionCombatant.Critter(Rat),
        StrangerHandle = SessionCombatant.Critter(Stranger);
    private static readonly ActorPlacement Inside = new(Cave, new(10, 20), NeighborDirection.XPlus);

    private static GameSessionOwner Party(params string[] followers)
    {
        var owner = Create();
        foreach (var entity in followers) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, entity).Disposition);
        return owner;
    }

    private static void Link(GameSessionOwner owner, string entity)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddDangerEnemy(owner.Version, entity, RatHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, entity, RatHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, new()
        {
            Entity = entity, State = DangerState.Flee, Target = RatHandle,
        }).Disposition);
    }

    private static void Tick(GameSessionOwner owner, params string[] entities)
    {
        foreach (var entity in entities)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = entity, Start = SessionAiTickStart.Delayed }).Disposition);
    }

    [TestMethod]
    public void The_move_list_of_a_plain_party_carries_every_follower_and_writes_nothing()
    {
        using var owner = Party(Virgil, Sogg);
        var before = owner.Version;
        var list = owner.QueryTransferMoveList(new());
        Assert.AreEqual(0, list.Waiting.Count);
        CollectionAssert.AreEqual(new[] { Virgil, Sogg }, list.Travelling.ToArray());
        Assert.AreEqual(before, owner.Version);
    }

    [TestMethod]
    public void A_move_to_another_map_clears_FM6_links_of_the_travellers_and_keeps_the_danger_word()
    {
        using var owner = Party(Virgil);
        Link(owner, Virgil);
        Link(owner, Stranger);
        var danger = owner.QueryDanger(Virgil);
        Assert.IsNotNull(danger.FleeingFrom);
        Assert.IsNotNull(danger.HitBy);
        Assert.AreEqual(1, danger.Enemies.Count);
        var list = owner.QueryTransferMoveList(new());
        var revision = owner.Version.Revision;
        var result = owner.ApplyChanges(owner.Version, Inside, [], list);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the whole step");
        var cleared = owner.QueryDanger(Virgil);
        Assert.IsNull(cleared.Focus);
        Assert.IsNull(cleared.FleeingFrom);
        Assert.IsNull(cleared.HitBy);
        Assert.AreEqual(0, cleared.Enemies.Count);
        Assert.AreEqual(danger.Flags, cleared.Flags, "the state bits are the danger word, not links");
        Assert.IsNotNull(owner.QueryDanger(Stranger).HitBy, "an NPC that does not travel keeps its links");
        Assert.AreEqual(Cave, owner.QueryActor().Map);
    }

    [TestMethod]
    public void A_move_on_the_same_map_clears_the_links_too()
    {
        using var owner = Party(Virgil);
        Link(owner, Virgil);
        var list = owner.QueryTransferMoveList(new());
        var result = owner.ApplyChanges(owner.Version, new ActorPlacement(Exterior, new(5, 5), NeighborDirection.XPlus), [], list);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.IsNull(owner.QueryDanger(Virgil).HitBy);
        Assert.AreEqual(0, owner.QueryDanger(Virgil).Enemies.Count);
    }

    [TestMethod]
    public void Only_the_parity_writes_make_a_step_that_changes_nothing_else_a_change()
    {
        using var owner = Party(Virgil);
        Link(owner, Virgil);
        var list = owner.QueryTransferMoveList(new());
        var result = owner.ApplyChanges(owner.Version, owner.QueryActor(), [], list);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, "the cleared links are a change");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyChanges(owner.Version, owner.QueryActor(), [], owner.QueryTransferMoveList(new())).Disposition);
    }

    [TestMethod]
    public void A_wait_order_of_a_map_change_takes_the_tempus_fugit_flag_off_the_waiter_ARC_MAGIC_1190()
    {
        const int tempusFugit = 0x4000000;
        using var owner = Party(Virgil, Sogg);
        foreach (var who in new[] { SessionSpellCaster.Hero, Virgil, Sogg })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, who, SessionObjectFlagField.Spell, tempusFugit, true).Disposition);
        var timer = new PartyWaitFacts(3, PersuasionTraining.None, 8, false);
        var list = owner.QueryTransferMoveList(new() { NoFatigue = entity => entity == Sogg, WaitFacts = _ => timer });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], list).Disposition);
        Assert.AreEqual(0, owner.QueryObjectFlags(Sogg).Spell(0) & tempusFugit, "the waiter left its leader (0x4AA7A0 -> 0x45DFC0): its flag is cleared");
        Assert.AreEqual(tempusFugit, owner.QueryObjectFlags(Virgil).Spell(0) & tempusFugit, "the traveller keeps it");
    }

    [TestMethod]
    public void An_unconscious_follower_is_ordered_to_wait_where_it_stands_and_does_not_travel()
    {
        using var owner = Party(Virgil, Sogg);
        Link(owner, Sogg);
        Tick(owner, Virgil, Sogg);
        var timer = new PartyWaitFacts(3, PersuasionTraining.None, 8, false);
        var list = owner.QueryTransferMoveList(new() { NoFatigue = entity => entity == Sogg, WaitFacts = _ => timer });
        Assert.AreEqual(Sogg, list.Waiting.Single().Entity);
        CollectionAssert.AreEqual(new[] { Virgil }, list.Travelling.ToArray());
        var result = owner.ApplyChanges(owner.Version, Inside, [], list);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Virgil).Standing);
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion(Sogg).Standing, "it keeps the PC as leader but leaves the follower array");
        CollectionAssert.AreEqual(new[] { Virgil }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray());
        var wait = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.PartyWait);
        Assert.AreEqual((Exterior.RuntimeMap, Sogg), (wait.ScopeMap, wait.Payload.Entities[0].Code), "the wait timer stays on the map the PC left");
        Assert.IsNotNull(owner.QueryDanger(Sogg).HitBy, "a follower left behind is not moved, so FM6 does not touch it");
        Assert.IsNull(owner.QueryAiTick(Sogg), "the map left drops the ticks of whoever stays");
    }

    [TestMethod]
    public void The_no_wait_orders_flag_takes_an_unconscious_follower_along()
    {
        using var owner = Party(Virgil);
        var list = owner.QueryTransferMoveList(new() { NoFatigue = _ => true, NoWaitOrders = true });
        Assert.AreEqual(0, list.Waiting.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], list).Disposition);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion(Virgil).Standing);
    }

    [TestMethod]
    public void A_travelling_followers_own_AI_tick_is_carried_to_the_destination_and_the_rest_are_dropped()
    {
        using var owner = Party(Virgil);
        Tick(owner, Virgil, Stranger);
        var deadline = owner.QueryAiTick(Virgil)!.Value;
        var list = owner.QueryTransferMoveList(new());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], list).Disposition);
        Assert.AreEqual(deadline, owner.QueryAiTick(Virgil), "carried with its deadline, one tick");
        Assert.AreEqual(Cave.RuntimeMap, owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.AiTick).ScopeMap);
        Assert.IsNull(owner.QueryAiTick(Stranger), "the map left drops its NPCs' ticks");
    }

    [TestMethod]
    public void A_move_without_a_list_still_drops_every_tick_as_before()
    {
        using var owner = Party(Virgil);
        Tick(owner, Virgil);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, []).Disposition);
        Assert.IsNull(owner.QueryAiTick(Virgil));
    }

    [TestMethod]
    public void A_follower_of_a_follower_in_the_list_travels_with_its_events()
    {
        using var owner = Party(Virgil);
        Tick(owner, Virgil, Rat);
        var list = owner.QueryTransferMoveList(new() { FollowersOf = npc => npc == Virgil ? [Rat] : [] });
        CollectionAssert.AreEqual(new[] { Rat, Virgil }, list.Travelling.ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], list).Disposition);
        Assert.IsNotNull(owner.QueryAiTick(Rat));
        Assert.AreEqual(2, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.AiTick && e.ScopeMap == Cave.RuntimeMap));
    }

    [TestMethod]
    public void A_list_that_does_not_match_the_party_is_refused_and_nothing_is_applied()
    {
        using var owner = Party(Virgil, Sogg);
        Link(owner, Virgil);
        var revision = owner.Version.Revision;
        SessionMoveList[] bad =
        [
            new([], [Virgil]),                                              // Sogg is neither waiting nor travelling
            new([new(Virgil, null)], [Virgil, Sogg]),                       // both waiting and travelling
            new([new(Stranger, null)], [Virgil, Sogg]),                     // waits though it does not follow
            new([new(Sogg, null), new(Sogg, null)], [Virgil]),              // listed twice
            new([], [Virgil, Sogg, Sogg]),                                  // travelling twice
            new([], [Virgil, Sogg, "bad id"]),
            new([], [Virgil, Sogg, SessionEventKinds.PlayerEntity]),        // the PC is the mover, not a traveller
            new([new(Sogg, new PartyWaitFacts(3, (PersuasionTraining)99, 8, false))], [Virgil]),
        ];
        foreach (var list in bad)
        {
            var result = owner.ApplyChanges(owner.Version, Inside, [], list);
            Assert.AreEqual(GameSessionDisposition.Rejected, result.Disposition);
            Assert.AreEqual(revision, owner.Version.Revision);
            Assert.AreEqual(Exterior, owner.QueryActor().Map);
            Assert.IsNotNull(owner.QueryDanger(Virgil).HitBy);
        }
    }

    [TestMethod]
    public void A_traveller_whose_record_held_only_links_leaves_no_danger_record_behind()
    {
        using var owner = Party(Virgil);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddDangerEnemy(owner.Version, Virgil, RatHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerHitBy(owner.Version, Virgil, RatHandle).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], owner.QueryTransferMoveList(new())).Disposition);
        Assert.AreEqual(0, owner.QueryDangerRecords().Records.Count, "a record with nothing left is not kept");
    }

    [TestMethod]
    public void A_stale_version_is_refused_before_the_list_is_read()
    {
        using var owner = Party(Virgil);
        var stale = owner.Version;
        Link(owner, Virgil);
        var result = owner.ApplyChanges(stale, Inside, [], new([], [Virgil]));
        Assert.AreEqual(GameSessionFailure.StaleVersion, result.Failure);
    }

    [TestMethod]
    public void The_result_survives_a_save_and_load()
    {
        using var owner = Party(Virgil, Sogg);
        Link(owner, Virgil);
        Tick(owner, Virgil);
        var list = owner.QueryTransferMoveList(new() { NoFatigue = entity => entity == Sogg });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Inside, [], list).Disposition);
        using var restored = Restore(owner.Capture());
        Assert.AreEqual(CompanionStanding.Waiting, restored.QueryCompanion(Sogg).Standing);
        Assert.IsNull(restored.QueryDanger(Virgil).HitBy);
        Assert.AreEqual(Cave, restored.QueryActor().Map);
    }
}
