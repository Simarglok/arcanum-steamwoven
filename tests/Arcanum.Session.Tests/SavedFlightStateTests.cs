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
    private static string Fly(GameSessionOwner owner, SessionShot shot, string? target, string? shooter = null, bool critter = true)
    {
        var id = owner.CreateProjectile(From)!;
        var line = ProjectileRules.Launch(From, To)!;
        var (next, crossing) = ProjectileRules.Step(line, _ => true);
        Assert.IsNotNull(next);
        var flight = new SessionProjectileFlight(shot, new(target, critter, false, shooter, null), To, 0x12340000u, line.Start, line.Destination, next!.Next, next.X, next.Y, next.Steps,
            new ProjectileShot(true, false, 10)) { Ticks = 1, Frames = 2 };
        Assert.IsTrue(owner.MoveProjectile(id, next.Tile, 4, -2, flight));
        var started = owner.StartGoal(new GoalStartRequest(id, GoalStarters.Projectile, AiExecutorRules.ProjectileFrame(target, To)), new ProjectileObjectTests.Ports());
        Assert.IsTrue(started.Started);
        return id;
    }

    private static readonly Regex FlightsSection = new(",\"flights\":\\{.*\\}(?=,\"lighting\")");

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
}
