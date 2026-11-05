using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using Step = Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceStep;

namespace Arcanum.Session.Tests;

// ARC-DEV-114 (MIG11 slice S3): the change of place of a mover that is not the hero, and the teleport caller's emptied sector cache with the
// pre-cache that follows it (research CP10 to CP12 of docs/architecture/change-of-place.md). Ids, cells and maps are invented.
[TestClass]
public sealed class ChangeOfPlaceMoverTests
{
    private const string Wolf = "oid:wolf", Dog = "oid:dog", Rat = "oid:rat";
    private static readonly CellCoord There = new(10, 20), Far = new(700, 700);
    private static readonly SectorMapExtent Extent = new(16, 16);
    private static readonly SessionDangerReaction Reaction = new(new(60, CharacterRace.Human, false, false), new(CharacterRace.HalfElf, 0, 0, [], [], [], false), 30);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };

    private sealed class Host : IChangeOfPlaceHost
    {
        public List<string> Calls { get; } = [];
        public SessionTransferFacts Facts { get; init; } = new();
        public IReadOnlyList<MapChangeEdit> Edits { get; init; } = [];
        public SessionMoveList? MoveList { get; private set; }

        public bool DestinationListed(int map) => true;
        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) { Calls.Add("facts"); return Facts; }
        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            Calls.Add("ready " + destination.RuntimeMap);
            return ChangeOfPlaceReadiness.Ready;
        }

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            Calls.Add("arrive " + destination.RuntimeMap);
            MoveList = moveList;
            return new(request.Destination, NeighborDirection.YPlus, Edits);
        }

        public bool Run(Step step, ChangeOfPlaceRequest request) { Calls.Add(step.ToString()); return true; }
    }

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven) =>
        GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules }).Owner!;

    private static GameSessionOwner WithSectors(RuleMode rules = RuleMode.Steamwoven)
    {
        var owner = Owner(rules);
        SessionResidentRow[] rows = [new(Wolf, SessionResidentKind.Npc, new(10, 20)), new(Dog, SessionResidentKind.Npc, new(700, 700))];
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, rows).Status);
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent));
        return owner;
    }

    private static ChangeOfPlaceSubmission Npc(GameSessionOwner owner, string mover, int map, CellCoord cell, ChangeOfPlaceFlags flags = ChangeOfPlaceFlags.None,
        ChangeOfPlaceCaller caller = ChangeOfPlaceCaller.NpcWaypoint) =>
        owner.RequestChangeOfPlace(mover, caller, map, cell, flags, 0, _ => true);

    private static ChangeOfPlaceSubmission Hero(GameSessionOwner owner, int map, CellCoord cell, ChangeOfPlaceFlags flags = ChangeOfPlaceFlags.None,
        ChangeOfPlaceCaller caller = ChangeOfPlaceCaller.JumpPoint) =>
        owner.RequestHeroChangeOfPlace(caller, map, cell, flags, 0, _ => true);

    private static void Schedule(GameSessionOwner owner, string entity) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleAiTick(owner.Version, new() { Entity = entity, Start = SessionAiTickStart.Delayed, DistanceToPc = 5 }).Disposition);

    private static void Hurt(GameSessionOwner owner, string entity) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Critter(entity, new LifeFacts(), default),
            new LifeDamage { HitPoints = 10 }).Disposition);

    private static void Touch(GameSessionOwner owner, params CellCoord[] cells) =>
        Assert.AreEqual(GameSessionFailure.None, owner.TouchSectors(owner.Version, Exterior, cells).Failure);

    [TestMethod]
    public void A_non_player_request_takes_the_slot_with_no_effects_and_is_overwritten_not_refused()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        var first = Npc(owner, Wolf, Cave.RuntimeMap, There, ChangeOfPlaceFlags.FadeOut | ChangeOfPlaceFlags.BlankScreen);
        Assert.AreEqual((ChangeOfPlaceSubmitStatus.Stored, ChangeOfPlaceRequestEffects.None), (first.Status, first.Effects), "CP1: only the local player's request fades or blanks");
        Assert.AreEqual((Wolf, false, false), (owner.QueryChangeOfPlace()!.Entity, owner.QueryChangeOfPlace()!.IsPlayerCharacter, owner.QueryChangeOfPlace()!.HasLocalPlayerFlag));
        Assert.AreEqual(revision, owner.Version.Revision, "a request writes the slot only");
        var second = Npc(owner, Dog, Exterior.RuntimeMap, There);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.Overwrote, second.Status);
        Assert.AreEqual(Wolf, second.Replaced!.Entity, "the first is lost (RL21)");
        Assert.AreEqual(Dog, owner.QueryChangeOfPlace()!.Entity);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.Overwrote, Hero(owner, Exterior.RuntimeMap, There).Status, "the hero's request overwrites a pending non-player's too");
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, Npc(owner, Wolf, Cave.RuntimeMap, There).Status, "only the hero's pending request refuses");
        Assert.AreEqual(SessionEventKinds.PlayerEntity, owner.QueryChangeOfPlace()!.Entity);
    }

    [TestMethod]
    public void The_mover_must_be_a_map_npc()
    {
        using var owner = Owner();
        foreach (var bad in new[] { SessionEventKinds.PlayerEntity, SessionEventKinds.AbsentRole, SessionMadeItem.Prefix + "1", "bad id", "" })
            Assert.ThrowsExactly<ArgumentException>(() => Npc(owner, bad, Cave.RuntimeMap, There), bad);
        Assert.IsNull(owner.QueryChangeOfPlace());
        Assert.IsFalse(Npc(owner, Wolf, Cave.RuntimeMap, new(-1, 0)).Accepted, "the project's validation refuses a negative cell");
        Assert.IsFalse(owner.RequestChangeOfPlace(Wolf, ChangeOfPlaceCaller.NpcWaypoint, Cave.RuntimeMap, There, ChangeOfPlaceFlags.None, 0, _ => false).Accepted);
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void A_non_player_change_on_the_open_map_applies_the_hosts_edits_clears_the_links_and_keeps_the_hero_and_the_tick()
    {
        using var owner = Owner();
        Schedule(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version,
            new() { Entity = Wolf, State = DangerState.CombatFocus, Target = SessionCombatant.Hero, Reaction = Reaction }).Disposition);
        var flags = owner.QueryDanger(Wolf).Flags;
        Assert.IsNotNull(owner.QueryDanger(Wolf).Focus);
        var tick = owner.QueryAiTick(Wolf);
        Assert.IsTrue(Npc(owner, Wolf, Exterior.RuntimeMap, There).Accepted);
        var revision = owner.Version.Revision;
        var host = new Host { Edits = [new(Exterior, SessionNpcPlacement.Key(Wolf), SessionNpcPlacement.Encode(There, NeighborDirection.YPlus))] };
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Completed, GameSessionFailure.None, false), (run.Status, run.Failure, run.RestoreDraw));
        CollectionAssert.AreEqual(new[] { Step.WaitOrders, Step.BuildMoveList, Step.RelocateObjects }, run.Ran.ToArray(),
            "no player character is in the list: no wall reset, no event carry, no pre-cache");
        CollectionAssert.AreEqual(new[] { "WaitOrders", "facts", "BuildMoveList", "arrive 79", "RelocateObjects" }, host.Calls);
        CollectionAssert.AreEqual(new[] { Wolf }, host.MoveList!.Travelling.ToArray());
        Assert.AreEqual(Start, owner.QueryActor(), "the hero stays");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the placement and the cleared links are one revision");
        Assert.AreEqual((There, NeighborDirection.YPlus), (owner.QueryNpcPlace(Wolf)!.Value.World, owner.QueryNpcPlace(Wolf)!.Value.Facing));
        Assert.IsNull(owner.QueryDanger(Wolf).Focus, "FM6: the combat focus is cleared in both cases");
        Assert.AreEqual(flags, owner.QueryDanger(Wolf).Flags, "the state bits stay");
        Assert.AreEqual(tick, owner.QueryAiTick(Wolf), "a move on the open map leaves the AI events alone (FM12)");
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void A_non_player_change_to_another_map_opens_nothing_and_takes_the_npc_off_the_ai_and_combat_and_keeps_its_timers_in_steamwoven()
    {
        using var owner = Owner();
        Schedule(owner, Wolf);
        Schedule(owner, Dog);
        Hurt(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, SessionCombatant.Critter(Wolf), true, Living).Disposition);
        Assert.IsTrue(owner.QueryCombatMode(SessionCombatant.Critter(Wolf)));
        Assert.IsTrue(Npc(owner, Wolf, Cave.RuntimeMap, There, ChangeOfPlaceFlags.NoWaitOrders).Accepted);
        var host = new Host
        {
            Edits = [new(Cave, MapChangeKey.ForEntity(Wolf, "npc.cell"), SessionNpcPlacement.Encode(There, NeighborDirection.YPlus)), new(Exterior, MapChangeKey.ForEntity(Wolf, "npc.map"), Cave.RuntimeMap)],
        };
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status);
        CollectionAssert.AreEqual(new[] { Step.BuildMoveList, Step.WriteTravellers }, run.Ran.ToArray(), "CP6: no flush, close or open, no view move, no pre-cache, no fade-in without the flag");
        CollectionAssert.AreEqual(new[] { "facts", "BuildMoveList", "arrive 80", "WriteTravellers" }, host.Calls, "the destination is not readied");
        Assert.AreEqual(Start, owner.QueryActor(), "the hero stays on its map");
        Assert.IsNull(owner.QueryAiTick(Wolf), "FM6: the AI events go with the departure");
        Assert.IsNotNull(owner.QueryAiTick(Dog), "another NPC's tick stays");
        Assert.IsFalse(owner.QueryCombatMode(SessionCombatant.Critter(Wolf)), "FM6: combat mode is left");
        var resting = owner.QueryEvents().Single(e => e.Kind == SessionEventKinds.NpcResting);
        Assert.AreEqual((Wolf, Cave.RuntimeMap), (resting.Payload.Entities[0].Code, resting.Payload.Entities[0].Map), "Steamwoven: the NPC's own timer follows it and parks there");
        Assert.AreEqual(Cave.RuntimeMap, resting.ScopeMap);
    }

    [TestMethod]
    public void The_original_setting_drops_the_npcs_own_timers_with_the_old_copy()
    {
        using var owner = Owner(RuleMode.Original);
        Hurt(owner, Wolf);
        Hurt(owner, Rat);
        Schedule(owner, Wolf);
        Assert.AreEqual(2, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.NpcResting));
        Assert.IsTrue(Npc(owner, Wolf, Cave.RuntimeMap, There).Accepted);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(new Host { Edits = [new(Cave, MapChangeKey.ForEntity(Wolf, "npc.cell"), SessionNpcPlacement.Encode(There, NeighborDirection.YPlus))] }).Status);
        Assert.IsNull(owner.QueryAiTick(Wolf));
        var left = owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.NpcResting).ToArray();
        Assert.AreEqual(Rat, left.Single().Payload.Entities[0].Code, "RL15, RL22: the timer that named the destroyed copy dies; the other NPC's stays");
        Assert.IsTrue(GameRules.Original.Lifecycle.NonPlayerMoveDropsEvents);
        Assert.IsFalse(GameRules.Steamwoven.Lifecycle.NonPlayerMoveDropsEvents);
    }

    [TestMethod]
    public void A_host_that_knows_followers_of_the_non_player_mover_stops_the_change_before_anything_moves()
    {
        using var owner = Owner();
        Schedule(owner, Wolf);
        Assert.IsTrue(Npc(owner, Wolf, Cave.RuntimeMap, There).Accepted);
        var revision = owner.Version.Revision;
        var run = owner.ProcessChangeOfPlace(new Host { Facts = new() { FollowersOf = entity => entity == Wolf ? [Dog] : [] } });
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.BuildMoveList, GameSessionFailure.ChangeOfPlaceStopped), (run.Status, run.StoppedAt, run.Failure),
            "the session models no NPC leader (TM10): a group cannot be carried");
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsNotNull(owner.QueryAiTick(Wolf));
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void A_hosts_edits_the_session_refuses_stop_a_non_player_change_with_nothing_written()
    {
        using var owner = Owner();
        Schedule(owner, Wolf);
        Assert.IsTrue(Npc(owner, Wolf, Cave.RuntimeMap, There).Accepted);
        var revision = owner.Version.Revision;
        var key = MapChangeKey.ForEntity(Wolf, "npc.cell");
        var run = owner.ProcessChangeOfPlace(new Host { Edits = [new(Cave, key, 1), new(Cave, key, 2)] });
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.WriteTravellers, GameSessionFailure.InvalidChangeKey), (run.Status, run.StoppedAt, run.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsNotNull(owner.QueryAiTick(Wolf), "a refused payload takes nothing off the NPC");
    }

    [TestMethod]
    public void A_teleport_spell_and_a_landing_empty_the_sector_cache_before_their_request_and_the_others_do_not()
    {
        using var owner = WithSectors();
        Touch(owner, There, Far);
        Assert.AreEqual(2, owner.QuerySectors(Exterior)!.Slots.Length);
        Assert.IsTrue(Hero(owner, Exterior.RuntimeMap, There, caller: ChangeOfPlaceCaller.Script).Accepted);
        Assert.AreEqual(2, owner.QuerySectors(Exterior)!.Slots.Length, "a script's change keeps the sectors (RL24)");
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(new Host()).Status);
        var held = owner.QuerySectors(Exterior)!.Slots.Length;
        Assert.IsTrue(held >= 2, "the sectors stay, the pre-cache adds the arrival's");
        // A request the project's validation refuses never reaches the original's slot, so it empties nothing.
        Assert.IsFalse(Hero(owner, Exterior.RuntimeMap, new(-1, 0), ChangeOfPlaceRules.TeleportSpellFlags, ChangeOfPlaceCaller.TeleportSpell).Accepted);
        Assert.AreEqual(held, owner.QuerySectors(Exterior)!.Slots.Length);
        var revision = owner.Version.Revision;
        var spell = Hero(owner, Exterior.RuntimeMap, There, ChangeOfPlaceRules.TeleportSpellFlags, ChangeOfPlaceCaller.TeleportSpell);
        Assert.AreEqual((ChangeOfPlaceSubmitStatus.Stored, ChangeOfPlaceRequestEffects.BlankScreen), (spell.Status, spell.Effects), "flags 0x120: blank, no fade (CP10)");
        Assert.AreEqual(0, owner.QuerySectors(Exterior)!.Slots.Length, "the helper empties the cache before it requests (0x452650)");
        Assert.AreEqual(held, owner.QuerySectors(Exterior)!.History.Length, "each evicted sector entered the eviction history");
        Assert.AreEqual(revision, owner.Version.Revision, "derived data: no revision");
    }

    [TestMethod]
    public void A_non_player_teleport_spell_empties_the_cache_even_when_the_heros_request_refuses_it()
    {
        using var owner = WithSectors();
        Touch(owner, There);
        Assert.IsTrue(Hero(owner, Exterior.RuntimeMap, Far).Accepted);
        var refused = Npc(owner, Wolf, Cave.RuntimeMap, There, ChangeOfPlaceRules.TeleportSpellFlags, ChangeOfPlaceCaller.TeleportSpell);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, refused.Status);
        Assert.AreEqual(0, owner.QuerySectors(Exterior)!.Slots.Length, "the helper empties first, then requests; the slot's refusal does not undo it");
    }

    [TestMethod]
    public void The_hero_arrives_at_a_cache_that_holds_only_the_three_by_three_sectors_around_the_destination()
    {
        using var owner = WithSectors();
        Touch(owner, There, new(0, 500), new(900, 900));
        Assert.IsTrue(Hero(owner, Exterior.RuntimeMap, Far, ChangeOfPlaceRules.TeleportSpellFlags, ChangeOfPlaceCaller.TeleportSpell).Accepted);
        var run = owner.ProcessChangeOfPlace(new Host());
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status);
        Assert.IsTrue(run.Ran.Contains(Step.PreCache));
        var loaded = owner.QuerySectors(Exterior)!.Slots.Select(slot => slot.Sector).Order().ToArray();
        var expected = new[] { 9, 10, 11 }.SelectMany(y => new[] { 9, 10, 11 }.Select(x => SectorCacheRules.SectorId(x, y))).Order().ToArray();
        CollectionAssert.AreEqual(expected, loaded, "CP6, RL24: the emptied cache holds what the pre-cache loaded");
        Assert.AreEqual(Far, owner.QueryActor().Cell);
        // Sector (1,3), where the hero stood, was evicted into the history and is not loaded.
        Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(1, 3))!.Value);
    }

    [TestMethod]
    public void The_pre_cache_is_left_to_the_host_without_a_sector_cache_and_on_another_map()
    {
        using var plain = Owner();
        Assert.IsTrue(Hero(plain, Exterior.RuntimeMap, Far).Accepted);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, plain.ProcessChangeOfPlace(new Host()).Status, "no cache: nothing to touch");
        using var owner = WithSectors();
        Touch(owner, There);
        Assert.IsTrue(Hero(owner, Cave.RuntimeMap, There, ChangeOfPlaceRules.TeleportSpellFlags, ChangeOfPlaceCaller.TeleportSpell).Accepted);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(new Host()).Status);
        Assert.IsNull(owner.QuerySectors(Cave), "the map change dropped the cache; the host opens the destination's own");
    }

    [TestMethod]
    public void The_cache_can_be_emptied_on_its_own_and_a_later_load_of_a_sector_folds_by_the_history()
    {
        using var owner = WithSectors();
        Assert.IsNull(owner.EmptySectorCache(Cave), "another map has no cache here");
        Assert.AreEqual(0, owner.EmptySectorCache(Exterior)!.Count, "nothing loaded, nothing evicted");
        Touch(owner, There, Far);
        var evicted = owner.EmptySectorCache(Exterior)!;
        CollectionAssert.AreEqual(new[] { new ResidentSector(0, 0), new ResidentSector(10, 10) }, evicted.ToArray());
        // The wolf's sector had a record from the emptying a moment ago: under 3 s later it folds without the wake-up marker.
        var soon = owner.TouchSectors(owner.Version, Exterior, [There]);
        Assert.IsFalse(soon.Folds.Single().Marked, "RL8: away less than 3 s");
        Assert.AreEqual(1, soon.Folds.Single().Npcs.Count);
        Assert.AreEqual(0, new[] { owner.QuerySectorLoaded(Exterior, new(10, 10)) }.Count(l => l == true), "the other sector is not loaded again");
    }
}
