using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-COMBAT-020: the AREA viewer's turn-based round host: the scan order, the world's answers and facts, the frame step and the
// hero's charges (invented critters and values).
[TestClass]
public sealed class AreaCombatRoundTests
{
    private const int Map = 1;
    private static readonly SessionCombatant Wolf = SessionCombatant.Critter("oid:wolf");

    private static AreaObjectKey Key(int index, AreaSourceKind kind = AreaSourceKind.Mobile, long sector = 0) =>
        new("src", Map, kind, sector, index, index * 16L, "id" + index, 16);

    private static GameSessionOwner Owner()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), Map, load).Owner!;
    }

    private static CellCoord HeroCell(GameSessionOwner owner) => owner.QueryActor().Cell;

    // The hero and a wolf two cells away, the wolf first in scan order (the party goes last anyway).
    private static AreaCombatWorld Fight(GameSessionOwner owner, int speed = 7, Func<string, SessionCombatantFacts?>? facts = null)
    {
        var hero = HeroCell(owner);
        return new(Map, () => AreaCombatScan.Order([new(SessionCombatant.Hero, hero, 0, null), new(Wolf, new(hero.X + 2, hero.Y), 0, Key(1))]),
            facts ?? (entity => entity == Wolf.Entity ? new SessionCombatantFacts { Speed = speed, Life = new LifeFacts() } : null));
    }

    [TestMethod]
    public void The_scan_orders_by_sector_rows_then_columns_and_the_last_attached_first_in_a_sector()
    {
        AreaScanCritter At(string name, int x, int y, long entered = 0, AreaObjectKey? key = null) =>
            new(SessionCombatant.Critter(name), new(x, y), entered, key ?? Key(0));
        var order = AreaCombatScan.Order([
            At("lower-left", 10, 70, key: Key(1)),
            At("upper-right", 70, 10, key: Key(2)),
            At("upper-left-first-loaded", 5, 5, key: Key(3)),
            At("upper-left-last-loaded", 6, 6, key: Key(9)),
            At("upper-left-static", 7, 7, key: Key(20, AreaSourceKind.Static)),
            At("upper-left-walked-in", 8, 8, entered: 4, key: Key(4)),
            At("upper-left-walked-in-later", 9, 9, entered: 7, key: Key(5)),
        ]).Select(c => c.Combatant.Entity).ToArray();
        CollectionAssert.AreEqual(new[] { "upper-left-walked-in-later", "upper-left-walked-in", "upper-left-last-loaded", "upper-left-first-loaded",
            "upper-left-static", "upper-right", "lower-left" }, order);
    }

    [TestMethod]
    public void Sector_entries_number_each_new_sector_and_keep_a_sector_already_entered()
    {
        var entries = new AreaSectorEntries();
        Assert.AreEqual(0, entries.Of(null));
        entries.Observe(null, new(1, 1));
        entries.Observe("oid:virgil", new(2, 2));
        entries.Observe(null, new(63, 63));
        Assert.AreEqual((1L, 2L), (entries.Of(null), entries.Of("oid:virgil")), "a step inside the sector is no new entry");
        entries.Observe(null, new(64, 63));
        Assert.AreEqual(3, entries.Of(null));
        entries.Clear();
        Assert.AreEqual(0, entries.Of("oid:virgil"));
    }

    [TestMethod]
    public void The_critters_list_the_hero_once_and_leave_out_npcs_not_on_the_map()
    {
        var entries = new AreaSectorEntries();
        entries.Observe("oid:virgil", new(5, 5));
        var critters = AreaCombatScan.Critters([("oid:wolf", Key(1)), ("oid:away", Key(2)), ("oid:virgil", Key(3))], new(4, 4),
            entity => entity == "oid:away" ? null : entity == "oid:virgil" ? new CellCoord(5, 5) : new CellCoord(3, 3), entries);
        CollectionAssert.AreEqual(new[] { "oid:virgil", null, "oid:wolf" }, critters.Select(c => c.Combatant.Entity).ToArray(),
            "the companion entered its sector last, then the hero (entry 0, no row), then the map's NPC");
        Assert.AreEqual(1, critters.Count(c => c.Combatant.IsHero));
    }

    [TestMethod]
    public void The_world_answers_only_for_its_map_and_reads_facts_from_the_fields()
    {
        using var owner = Owner();
        var world = Fight(owner);
        Assert.AreEqual(2, world.ScanCombatants(new(Map)).Count);
        Assert.AreEqual(0, world.ScanCombatants(new(Map + 1)).Count);
        Assert.IsNull(world.CombatantFacts(new(Map + 1), Wolf.Entity!));
        Assert.AreEqual(7, world.CombatantFacts(new(Map), Wolf.Entity!)!.Speed);

        var stats = new int[28];
        for (var i = 0; i < 8; i++) stats[i] = 8;
        stats[17] = 1; stats[25] = 20;
        CombatFields Fields(uint objectFlags, uint critterFlags) => new(CombatFieldOrigin.AreaObject,
            new CombatObjectFields(16, 17102, objectFlags, 0, 0, 0, 0, 1, 2, 0, 4, [0, 0, 0, 0, 0], 0), null, null, null, null, null,
            new CombatCritterFields(critterFlags, 0, stats, new int[12], new int[4], new int[25], 0, 0, 0, 1, 0, 0),
            new CombatNpcFields(0, 0, 0, 0, 0, 0, 0, [new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)]));
        var plain = Fields(0, 0);
        var combat = SessionCritterCombat.From("oid:wolf", plain).Combat!;
        var facts = AreaCombatWorld.Facts(combat, plain, combat.Loaded);
        Assert.AreEqual((combat.EffectiveStat(CharacterStat.Speed, combat.Loaded), false, false), (facts.Speed, facts.Hidden, facts.Sleeping));
        Assert.AreSame(combat.Facts, facts.Life);
        var hidden = AreaCombatWorld.Facts(combat, Fields(0x100, 0x1000), combat.Loaded);
        Assert.AreEqual((true, true), (hidden.Hidden, hidden.Sleeping), "object flag 0x100 hides it from drawing; critter flag 0x1000 is sleep");
    }

    [TestMethod]
    public void A_frame_passes_the_npcs_subturn_and_stops_at_the_heros_held_subturn()
    {
        using var owner = Owner();
        var world = Fight(owner);
        Assert.AreEqual(AreaTurnBasedStep.None.Calls, AreaTurnBased.Step(owner, world, true).Calls, "no round, no call");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, world).Disposition);
        Assert.AreEqual(Wolf, owner.QueryCombatRound()!.Actor);

        var step = AreaTurnBased.Step(owner, world, heroIdle: false);
        Assert.AreEqual(1, step.Calls, "the NPC's subturn ends whether or not the hero is idle");
        CollectionAssert.AreEqual(new[] { SessionCombatEventKind.SubturnEnded, SessionCombatEventKind.SubturnStarted }, step.Events.Select(e => e.Kind).ToArray());
        var round = owner.QueryCombatRound()!;
        Assert.IsTrue(round.Actor.IsHero);
        Assert.AreEqual(TurnBasedRules.ActionPointGrant(8), round.ActionPoints);

        Assert.AreEqual(0, AreaTurnBased.Step(owner, world, heroIdle: false).Calls, "a busy hero holds its subturn");
        var revision = owner.Version.Revision;
        var idle = AreaTurnBased.Step(owner, world, heroIdle: true);
        Assert.AreEqual((1, 0, revision), (idle.Calls, idle.Events.Count, owner.Version.Revision), "an idle hero with action points keeps its subturn");
    }

    [TestMethod]
    public void The_heros_charges_pay_overexert_and_then_refuse_passing_the_turn_on()
    {
        using var owner = Owner();
        var world = Fight(owner);
        Assert.AreEqual(AreaHeroCharge.Free, AreaTurnBased.ChargeHero(owner, 2, world), "no round: free");
        owner.StartCombatRound(owner.Version, world);
        Assert.AreEqual(AreaHeroCharge.NotTheirTurn, AreaTurnBased.ChargeHero(owner, 2, world));
        Assert.AreEqual(Wolf, owner.QueryCombatRound()!.Actor, "a command on another's subturn changes nothing");
        AreaTurnBased.Step(owner, world, true);

        var points = owner.QueryCombatRound()!.ActionPoints;
        var walk = AreaTurnBased.StepCost(MovementGait.Walk, owner.QueryHeroLife()!.Status.Fatigue);
        Assert.AreEqual((2, 1), (walk, AreaTurnBased.StepCost(MovementGait.Run, 5)));
        Assert.AreEqual(2, AreaTurnBased.StepCost(MovementGait.Run, 0), "a runner without fatigue pays a walk");
        Assert.AreEqual(AreaHeroCharge.Paid, AreaTurnBased.ChargeHero(owner, walk, world));
        Assert.AreEqual(points - walk, owner.QueryCombatRound()!.ActionPoints);
        Assert.AreEqual(AreaHeroCharge.Paid, AreaTurnBased.ChargeHero(owner, 1, world), "a pickup's point leaves an odd number");
        while (owner.QueryCombatRound()!.ActionPoints >= walk) Assert.AreEqual(AreaHeroCharge.Paid, AreaTurnBased.ChargeHero(owner, walk, world));

        var fatigue = owner.QueryHeroLife()!.Status.Fatigue;
        Assert.AreEqual(1, owner.QueryCombatRound()!.ActionPoints);
        Assert.AreEqual(AreaHeroCharge.Overexerted, AreaTurnBased.ChargeHero(owner, walk, world));
        Assert.AreEqual(fatigue - TurnBasedRules.OverexertionFatigue, owner.QueryHeroLife()!.Status.Fatigue, "the overexertion's fatigue damage");
        Assert.IsTrue(AreaTurnBased.Proceeds(AreaHeroCharge.Overexerted));
        var round = owner.QueryCombatRound()!;
        Assert.IsTrue(round.Actor.IsHero && round.ActionPoints == 0, "the hero holds its subturn with nothing left until a refusal or a completion");

        var clock = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(AreaHeroCharge.Refused, AreaTurnBased.ChargeHero(owner, walk, world));
        Assert.IsFalse(AreaTurnBased.Proceeds(AreaHeroCharge.Refused));
        round = owner.QueryCombatRound()!;
        Assert.AreEqual((2, Wolf), (round.Turn, round.Actor), "the refusal passed the turn on: the round ended and the next began");
        Assert.AreEqual(clock + TurnBasedRules.RoundGameTimeMs, owner.QueryClockSnapshot().GameTimestampMs, "one game second per round");
    }

    [TestMethod]
    public void A_frame_stops_after_a_new_round_began_so_the_event_pump_runs_between_rounds()
    {
        using var owner = Owner();
        var world = Fight(owner);
        owner.StartCombatRound(owner.Version, world);
        AreaTurnBased.Step(owner, world, true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndCombatTurn(owner.Version, SessionCombatant.Hero, world).Disposition, "E");
        Assert.AreEqual((2, Wolf), (owner.QueryCombatRound()!.Turn, owner.QueryCombatRound()!.Actor));

        // The hero spends everything, then the idle check passes the turn, the round ends and the next begins: the frame stops there.
        AreaTurnBased.Step(owner, world, true);
        while (owner.QueryCombatRound()!.ActionPoints > 0) AreaTurnBased.ChargeHero(owner, 1, world);
        var step = AreaTurnBased.Step(owner, world, true);
        Assert.AreEqual(1, step.Calls);
        Assert.IsTrue(step.Events.Any(e => e.Kind == SessionCombatEventKind.RoundBegan));
        Assert.AreEqual((3, Wolf), (owner.QueryCombatRound()!.Turn, owner.QueryCombatRound()!.Actor), "the wolf's new subturn waits for the next frame");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaTurnBased.Step(owner, world, true, 0));
        Assert.AreEqual(1, AreaTurnBased.Step(owner, world, false, maxSubturns: 1).Calls, "the frame's bound");
    }

    [TestMethod]
    public void The_status_names_the_round_the_actor_and_the_points()
    {
        using var owner = Owner();
        var world = Fight(owner);
        Assert.AreEqual("turn-based: off", AreaTurnBased.Status(false, null, c => "x"));
        Assert.AreEqual("turn-based: on, no round", AreaTurnBased.Status(true, null, c => "x"));
        owner.StartCombatRound(owner.Version, world);
        Assert.AreEqual("turn-based: round 1, wolf's turn, AP 7 (1/2)", AreaTurnBased.Status(true, owner.QueryCombatRound(), c => "wolf"));
        Assert.AreEqual("", AreaTurnBased.Refusal(AreaHeroCharge.Paid));
        Assert.AreEqual("not the hero's turn", AreaTurnBased.Refusal(AreaHeroCharge.NotTheirTurn));
    }
}
