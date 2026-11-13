using System.Reflection;
using System.Text.RegularExpressions;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-288 (saved-runs.md §3.5, the research note's SV5, SV6): a projectile in flight is saved with its flight run and the shot it carries, as the original writes every object made
// in play with the projectile's fields; after the load it flies on and strikes with the outcome decided at the shot. Values are invented.
[TestClass]
public sealed class SavedFlightStateTests
{
    private static readonly CellCoord From = new(100, 200), To = new(103, 200);

    // A projectile carrying the shot, its flight written as the host writes it, and its run (goal 36) on the table.
    internal static string Fly(GameSessionOwner owner, SessionShot shot, string? target, string? shooter = null, bool critter = true, string? thrower = null)
    {
        var id = owner.CreateProjectile(From)!;
        var line = ProjectileRules.Launch(From, To)!;
        var (next, crossing) = ProjectileRules.Step(line, _ => true);
        Assert.IsNotNull(next);
        var flight = new SessionProjectileFlight(shot, new(target, critter, false, shooter, thrower), To, 0x12340000u, line.Start, line.Destination, next!.Next, next.X, next.Y, next.Steps,
            new ProjectileShot(true, false, 10)) { Ticks = 1, Frames = 2 };
        Assert.IsTrue(owner.MoveProjectile(id, next.Tile, 4, -2, flight));
        var started = owner.StartGoal(new GoalStartRequest(id, GoalStarters.Projectile, AiExecutorRules.ProjectileFrame(target, To)), new ProjectileObjectTests.Ports());
        Assert.IsTrue(started.Started);
        return id;
    }

    private static readonly Regex FlightsSection = new(",\"flights\":\\{.*\\}(?=,\"barLights\")");

    private static string Flights(string text) => FlightsSection.Match(text) is { Success: true } match ? match.Value : throw new AssertFailedException("no flights section");

    [TestMethod]
    public void A_hero_shot_in_flight_is_saved_with_its_projectile_and_run_and_strikes_the_same_after_the_load()
    {
        using var owner = ProjectileStateTests.Owner();
        var rat = ProjectileStateTests.Rat();
        var shot = ProjectileStateTests.ShootUntil(owner, rat, hit: true).Shot!.Value;
        var id = Fly(owner, shot, rat.Entity);
        var capture = owner.Capture();
        Assert.AreEqual(1, capture.Flights.Projectiles.Count);
        Assert.AreEqual(1, capture.Flights.HeroShots.Count);
        Assert.AreEqual(1, capture.Goals.Runs.Count(run => run.Owner == id), "the flight run is saved with its projectile");
        var text = GameSessionSnapshotCodec.Encode(capture);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryProjectile(id), restored.QueryProjectile(id), "the projectile and its flight come back as saved");
        CollectionAssert.AreEqual(owner.QueryShotsInFlight().ToArray(), restored.QueryShotsInFlight().ToArray());
        Assert.IsTrue(restored.QueryGoals().Runs.Single(run => run.Owner == id) is { Bottom.Goal: GoalType.Projectile } run && run.Bottom.Target == rat.Entity);
        Assert.AreEqual(Flights(text), Flights(GameSessionSnapshotCodec.Encode(restored.Capture())), "the restored flights save as they were loaded");

        var before = ProjectileStateTests.Hp(owner, rat);
        var landed = owner.LandShot(owner.Version, shot, new(rat) { Distance = 99 });
        var again = restored.LandShot(restored.Version, shot, new(rat) { Distance = 99 });
        Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (landed.Failure, again.Failure));
        Assert.AreEqual((landed.Resolution!.Outcome, landed.Resolution.HitLocation, landed.Life!.HitPointDamage),
            (again.Resolution!.Outcome, again.Resolution.HitLocation, again.Life!.HitPointDamage), "the strike lands the hit decided at the shot, with the same damage roll");
        Assert.AreEqual(before - again.Life.HitPointDamage, ProjectileStateTests.Hp(restored, rat));
        Assert.AreEqual(0, restored.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void An_npc_arrow_in_flight_is_saved_with_the_shooters_values_and_strikes_after_the_load()
    {
        using var owner = NpcShotFlightStateTests.Owner();
        var archer = NpcShotFlightStateTests.Combat(NpcShotFlightStateTests.Archer, NpcShotFlightStateTests.BowFields());
        var (result, facts) = NpcShotFlightStateTests.Next(owner, NpcShotFlightStateTests.Shoot(archer), hit: true);
        var shot = result.Shot!.Value;
        Fly(owner, shot, AiSchedulerRules.Pc, NpcShotFlightStateTests.Archer, critter: false);
        var capture = owner.Capture();
        Assert.AreEqual((1, 0), (capture.Flights.NpcArrows.Count, capture.Flights.HeroShots.Count));
        Assert.AreEqual(capture.Flights.NpcArrows[0].Values, Decode(GameSessionSnapshotCodec.Encode(capture)).Flights.NpcArrows[0].Values, "the shooter's values round-trip whole");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(capture)));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, NpcShotFlightStateTests.Seed()).Failure);
        Assert.AreEqual(facts, restored.QueryNpcShot(shot));
        var strike = owner.LandNpcShot(owner.Version, shot, new(archer) { AttackerWorn = NpcShotFlightStateTests.Worn() });
        var again = restored.LandNpcShot(restored.Version, shot, new(archer) { AttackerWorn = NpcShotFlightStateTests.Worn() });
        Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (strike.Failure, again.Failure));
        Assert.AreEqual(strike.Life!.HitPointDamage, again.Life!.HitPointDamage);
        Assert.AreEqual(owner.QueryHeroLife()!.Status.HitPoints, restored.QueryHeroLife()!.Status.HitPoints);
    }

    // ARC-COMBAT-1140: an NPC's arrow decided a hit on a door (the line of fire's blocker) is saved with the door as its target and strikes it after the load.
    [TestMethod]
    public void An_npc_arrow_aimed_at_an_object_is_saved_and_strikes_it_after_the_load()
    {
        using var owner = NpcShotFlightStateTests.Owner();
        var archer = NpcShotFlightStateTests.Combat(NpcShotFlightStateTests.Archer, NpcShotFlightStateTests.BowFields());
        var door = new SessionObjectTarget(Exterior, "sector:1:7", new SessionObjectBaseline
        {
            Kind = DamageableObjectKind.Portal, Points = 30, Material = ObjectMaterial.Cloth,
            Openable = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Portal, 0, 0, 0),
        });
        var aimed = owner.AttackObjectByNpc(owner.Version, new(archer) { Target = door, Distance = 6, AttackerWorn = NpcShotFlightStateTests.Worn(), Flight = true });
        var shot = aimed.Shot!.Value;
        var facts = owner.QueryNpcShot(shot);
        Assert.AreEqual((true, door.Entity), (facts!.Value.Hit, facts.Value.Target));
        Fly(owner, shot, door.Entity, NpcShotFlightStateTests.Archer, critter: false);
        var capture = owner.Capture();
        Assert.AreEqual(door.Entity, capture.Flights.NpcArrows[0].Target);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(capture)));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, NpcShotFlightStateTests.Seed()).Failure);
        Assert.AreEqual(facts, restored.QueryNpcShot(shot));
        var strike = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = NpcShotFlightStateTests.Worn() });
        var again = restored.LandNpcShotOnObject(restored.Version, shot, new(archer) { Target = door, AttackerWorn = NpcShotFlightStateTests.Worn() });
        Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (strike.Failure, again.Failure));
        Assert.AreEqual(strike.Object!.Damage, again.Object!.Damage, "the strike lands the hit decided at the shot, with the same damage roll");
        Assert.AreEqual(strike.Object.Damage, restored.QueryObject(door).Damage);
    }

    [TestMethod]
    public void A_shot_without_its_projectile_and_a_projectile_without_its_run_are_not_saved()
    {
        using var owner = ProjectileStateTests.Owner();
        var rat = ProjectileStateTests.Rat();
        var shot = ProjectileStateTests.ShootUntil(owner, rat, hit: true).Shot!.Value;
        var bare = owner.Capture();
        Assert.IsTrue(bare.Flights.IsEmpty, "a shot no projectile carries would dangle");
        var id = Fly(owner, shot, rat.Entity);
        owner.KillGoals(id);
        Assert.IsTrue(owner.Capture().Flights.IsEmpty, "a projectile whose run ended is not saved");
    }

    [TestMethod]
    public void A_restore_refuses_a_projectile_without_its_run_or_shot_and_a_run_without_its_projectile()
    {
        using var owner = ProjectileStateTests.Owner();
        var rat = ProjectileStateTests.Rat();
        var shot = ProjectileStateTests.ShootUntil(owner, rat, hit: true).Shot!.Value;
        Fly(owner, shot, rat.Entity);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Refused(string damaged, string why)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(damaged);
            if (decoded.Snapshot is null) return;
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure, why);
        }
        var noRun = Regex.Replace(text, ",\"goals\":\\{\"nextRunId\":(\\d+),\"runs\":\\[.*?\\]\\}(?=,\"spells\")", ",\"goals\":{\"nextRunId\":$1,\"runs\":[]}");
        Assert.AreNotEqual(text, noRun);
        Refused(noRun, "a projectile needs its run");
        var noShot = Regex.Replace(text, "\"heroShots\":\\[.*?\\](?=,\"npcThrows\")", "\"heroShots\":[]");
        Assert.AreNotEqual(text, noShot);
        Refused(noShot, "a projectile needs its shot");
        var noProjectile = Regex.Replace(text, "\"projectiles\":\\[.*?\\](?=,\"nextShot\")", "\"projectiles\":[]");
        Assert.AreNotEqual(text, noProjectile);
        Refused(noProjectile, "a flight run needs its projectile (and a shot its projectile)");
        Refused(text.Replace("\"lastProjectile\":1", "\"lastProjectile\":0"), "a projectile numbered above the counter");
    }

    [TestMethod]
    public void The_codec_writes_every_property_of_the_combatant_and_the_weapon()
    {
        static string[] Names(Type type) =>
            [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.Name != "EqualityContract").Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).Order()];
        CollectionAssert.AreEqual(Names(typeof(Combatant)), GameSessionSnapshotCodec.CombatantFields.Order().ToArray(), "a Combatant property is left out of a save");
        CollectionAssert.AreEqual(Names(typeof(CombatWeapon)), GameSessionSnapshotCodec.CombatWeaponFields.Order().ToArray(), "a CombatWeapon property is left out of a save");
    }

    [TestMethod]
    public void A_schema_79_save_upgrades_to_an_empty_flights_section()
    {
        var older = SnapshotUpgradeTests.Schema79(GoldenJson);
        var decoded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        Assert.IsTrue(decoded.Snapshot!.Flights.IsEmpty);
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(decoded.Snapshot));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoFlights + ",\"lighting\":")).Failure,
            "a schema 79 text has no flights section");
    }

    [TestMethod]
    public void A_boomerang_turned_back_in_flight_is_saved_away_and_ends_in_the_hand_after_the_load()
    {
        using var owner = BoomerangStateTests.Owner();
        var rat = BoomerangStateTests.Rat();
        var shot = BoomerangStateTests.ThrowUntil(owner, rat, hit: false).Shot!.Value;
        Assert.IsTrue(owner.ReturnShot(shot), "it turns back at its first end");
        Fly(owner, shot, rat.Entity);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        var saved = restored.Capture().Flights.HeroShots.Single();
        Assert.AreEqual((true, true, false), (saved.Boomerang, saved.Returning, saved.Hit));
        Assert.IsTrue(restored.QueryBoomerangInFlight(), "the boomerang is away after the load: no attack starts");
        Assert.IsFalse(restored.QueryShotReturns(shot), "it turned back already");
        Assert.AreEqual(GameSessionFailure.WeaponInFlight, restored.AttackCritter(restored.Version, new(rat) { Distance = 3, Flight = true }).Failure);
        Assert.IsTrue(restored.EndShot(shot));
        Assert.IsFalse(restored.QueryBoomerangInFlight());
        Assert.IsTrue(BoomerangStateTests.Wields(restored), "it stayed in the hand");
    }

    [TestMethod]
    public void A_thrown_weapon_in_flight_stays_hidden_after_the_load_and_lands_where_the_flight_ends()
    {
        using var owner = ThrowStateTests.Owner();
        var rat = ThrowStateTests.Rat();
        var shot = ThrowStateTests.ThrowUntil(owner, rat, hit: true).Shot!.Value;
        var thrown = owner.QueryThrownShot(shot)!.Value;
        Assert.IsTrue(owner.QueryItemInFlight(thrown.Item));
        Fly(owner, shot, rat.Entity);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(thrown, restored.QueryThrownShot(shot));
        Assert.IsTrue(restored.QueryItemInFlight(thrown.Item), "the item stays hidden while the restored flight goes on");
        Assert.AreEqual(owner.Capture().Flights.HeroShots.Single().WeaponItem, restored.Capture().Flights.HeroShots.Single().WeaponItem);
        Assert.IsTrue(restored.Capture().Flights.HeroShots.Single().Hit, "the restored shot carries the hit decided at the throw");
        var landed = restored.LandShot(restored.Version, shot, new(rat) { Distance = 99 });
        Assert.AreEqual(GameSessionFailure.None, landed.Failure, "the restored hit strikes");
        Assert.IsTrue(restored.QueryItemInFlight(thrown.Item), "a struck throw waits for its item's landing");
        Assert.IsTrue(restored.Capture().Flights.HeroShots.Single().Struck);
    }

    [TestMethod]
    public void An_npc_grenade_in_flight_is_saved_with_its_thrower_and_stays_hidden_after_the_load()
    {
        using var owner = NpcThrowStateTests.Owner();
        var tile = new CellCoord(98, 203);
        var shot = owner.ThrowByNpc(owner.Version, NpcThrowStateTests.Lob(tile)).Shots.Single();
        Fly(owner, shot, NpcThrowStateTests.Thrower, critter: false, thrower: NpcThrowStateTests.Thrower);
        var capture = owner.Capture();
        Assert.AreEqual((1, 0, 0), (capture.Flights.NpcThrows.Count, capture.Flights.HeroShots.Count, capture.Flights.NpcArrows.Count));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(capture)));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, NpcThrowStateTests.Seed()).Failure);
        Assert.AreEqual(owner.QueryThrownShot(shot), restored.QueryThrownShot(shot));
        Assert.IsTrue(restored.QueryItemInFlight(NpcThrowStateTests.Grenade), "the grenade stays hidden while it flies");
        Assert.AreEqual(owner.QueryShotEndItemEvents(shot, tile).Single().Event, restored.QueryShotEndItemEvents(shot, tile).Single().Event, "it still goes off where it ends");
    }

    [TestMethod]
    public void A_restore_refuses_a_launch_of_another_kind_than_its_shot()
    {
        using var owner = ProjectileStateTests.Owner();
        var rat = ProjectileStateTests.Rat();
        var shot = ProjectileStateTests.ShootUntil(owner, rat, hit: true).Shot!.Value;
        Fly(owner, shot, rat.Entity);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var damaged = text.Replace("\"shooter\":null", "\"shooter\":\"oid:archer\"");
        Assert.AreNotEqual(text, damaged);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(damaged), RestoreOptions()).Failure, "a hero's shot names no shooter");
        var both = text.Replace("\"object\":null,\"thrown\"", "\"object\":\"wall\",\"thrown\"");
        Assert.AreNotEqual(text, both);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(both), RestoreOptions()).Failure, "a shot aims a critter or an object, not both");
        var counter = System.Text.RegularExpressions.Regex.Replace(text, "\"lastProjectile\":\\d+", "\"lastProjectile\":2147483647");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(counter), RestoreOptions()).Failure, "the counter keeps room for the next projectile");

        using var npc = NpcShotFlightStateTests.Owner();
        var archer = NpcShotFlightStateTests.Combat(NpcShotFlightStateTests.Archer, NpcShotFlightStateTests.BowFields());
        var arrow = NpcShotFlightStateTests.Next(npc, NpcShotFlightStateTests.Shoot(archer), hit: true).Result.Shot!.Value;
        Fly(npc, arrow, AiSchedulerRules.Pc, shooter: null, critter: false);
        var unnamed = GameSessionSnapshotCodec.Encode(npc.Capture());
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(unnamed), RestoreOptions()).Failure, "an NPC's arrow names its shooter");
    }
}
