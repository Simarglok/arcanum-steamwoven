using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.TrapEffectTests;

namespace Arcanum.Session.Tests;

// ARC-TECHNOLOGY-1635 (ledger TT15): a native trap's shot (script 30002) flies as a projectile. The cast host does the dispatcher's part at the trap (the amount, the two d100, the fate branch, the spell) and hands the
// missile to the host (LaunchTrapShot); the strike (the attack event of the shooter object, then the damage) is StrikeTrapShot, called where the missile meets the victim, and a missile that ends without meeting it
// (EndShot) strikes nothing. A shot in flight is saved (schema 100, the `trapShots` list). Values are invented within the original's ranges.
[TestClass]
public sealed class TrapShotFlightTests
{
    private static SessionTrapFiring Shot() => Firing(30002, Counters(90, 5, 8, 2), 0) with { Shooters = [new(Box, 30007, Counters(0, 5, 0, 0))] };

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, List<SessionTrapShotLaunch> Launches, List<(string? Attacker, string Victim, int HitPoints)> Events) Flying()
    {
        var (owner, caster, _) = TrapEffectTests.Session();
        var launches = new List<SessionTrapShotLaunch>();
        var events = new List<(string? Attacker, string Victim, int HitPoints)>();
        caster.LaunchTrapShot = launch => { launches.Add(launch); return true; };
        caster.AiNotice = (attacker, victim) => events.Add((attacker, victim, HeroHitPoints(owner)));
        return (owner, caster, launches, events);
    }

    [TestMethod]
    public void A_shot_flies_from_the_shooters_tile_to_the_victims_and_the_event_and_the_damage_come_at_the_strike()
    {
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        var before = HeroHitPoints(owner);
        var draws = owner.QueryRandom().Draws;
        var result = caster.ApplyTrapEffect(Shot());
        var shot = result.Victims.Single();
        Assert.AreEqual(draws + 3, owner.QueryRandom().Draws, "the amount and the dispatcher's two d100: the strike draws nothing more");
        Assert.AreEqual((SessionTrapHit.InFlight, Box, SpellCastAttemptOutcome.Started), (shot.Hit, shot.Shooter, shot.Spell), "the spell is cast as the dispatcher returns, before the missile arrives");
        Assert.IsNull(shot.Step);
        Assert.IsTrue(shot.Amount is >= 2 and <= 8);
        var launch = launches.Single();
        Assert.AreEqual((Box, Hero, new CellCoord(4, 6), new CellCoord(5, 5)), (launch.Shooter, launch.Victim, launch.From, launch.Destination), "from the shooter's tile to the victim's location");
        Assert.AreEqual(new SessionTrapShotFacts(Box, Hero, shot.Amount), owner.QueryTrapShot(launch.Shot), "the amount rides in the shot (field 0x4F)");
        CollectionAssert.AreEqual(new[] { launch.Shot }, owner.QueryShotsInFlight().ToArray());
        Assert.AreEqual(before, HeroHitPoints(owner), "no damage and no event while it flies");
        Assert.AreEqual(0, events.Count);

        var struck = caster.StrikeTrapShot(launch.Shot, Hero)!;
        Assert.AreEqual((SessionTrapHit.Damaged, Hero, shot.Amount, Box), (struck.Hit, struck.Entity, struck.Amount, struck.Shooter));
        Assert.AreEqual(new DamageByType(shot.Amount, 0, 0, 0, 0), struck.Step!.Rolled, "normal damage only");
        Assert.AreEqual(before - shot.Amount, HeroHitPoints(owner));
        CollectionAssert.AreEqual(new[] { ((string?)Box, Hero, before) }, events, "one event from the shooter object, before the damage");
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count, "the strike ends the shot");
        Assert.IsNull(caster.StrikeTrapShot(launch.Shot, Hero), "a shot strikes once");
        Assert.IsNull(owner.QueryTrapShot(launch.Shot));
        Assert.AreEqual(draws + 3, owner.QueryRandom().Draws);
    }

    [TestMethod]
    public void A_missile_that_ends_without_meeting_its_victim_strikes_nothing()
    {
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        var before = HeroHitPoints(owner);
        caster.ApplyTrapEffect(Shot());
        var launch = launches.Single();
        Assert.IsTrue(owner.EndShot(launch.Shot), "the victim left the line: the missile flew to its range and ended");
        Assert.AreEqual((before, 0, 0), (HeroHitPoints(owner), events.Count, owner.QueryShotsInFlight().Count));
        Assert.IsNull(caster.StrikeTrapShot(launch.Shot, Hero), "an ended shot has no strike");
    }

    [TestMethod]
    public void A_missile_that_meets_another_critter_strikes_it_with_the_shooter_as_the_attacker()
    {
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        var before = HeroHitPoints(owner);
        var amount = caster.ApplyTrapEffect(Shot()).Victims.Single().Amount;
        var struck = caster.StrikeTrapShot(launches.Single().Shot, Guard)!;
        Assert.AreEqual((SessionTrapHit.Damaged, Guard, amount, Box), (struck.Hit, struck.Entity, struck.Amount, struck.Shooter));
        CollectionAssert.AreEqual(new[] { ((string?)Box, Guard, before) }, events, "the stray's event names the shooter object and the critter struck");
        Assert.AreEqual(before, HeroHitPoints(owner), "the hero it was shot at is not hurt");
    }

    [TestMethod]
    public void A_launch_the_host_refuses_leaves_no_shot_and_the_shot_strikes_at_once()
    {
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        caster.LaunchTrapShot = launch => { launches.Add(launch); return false; };
        var before = HeroHitPoints(owner);
        var shot = caster.ApplyTrapEffect(Shot()).Victims.Single();
        Assert.AreEqual(1, launches.Count);
        Assert.AreEqual((SessionTrapHit.Damaged, 0, 1), (shot.Hit, owner.QueryShotsInFlight().Count, events.Count));
        Assert.AreEqual(before - shot.Amount, HeroHitPoints(owner));
        caster.LaunchTrapShot = null;
        var plain = caster.ApplyTrapEffect(Shot()).Victims.Single();
        Assert.AreEqual((SessionTrapHit.Damaged, 1), (plain.Hit, launches.Count), "no launch hook: the strike follows the dispatcher at once, as before the flight was drawn");
    }

    [TestMethod]
    public void A_full_shot_budget_strikes_at_once_and_a_shooterless_shot_flies_nothing()
    {
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        for (var i = 0; i < GameSessionOwner.MaxShotsInFlight; i++) Assert.IsNotNull(owner.BeginTrapShot(Box, Hero, 1));
        Assert.IsNull(owner.BeginTrapShot(Box, Hero, 1), "the budget is full");
        var full = caster.ApplyTrapEffect(Shot()).Victims.Single();
        Assert.AreEqual((SessionTrapHit.Damaged, 0, 1), (full.Hit, launches.Count, events.Count));
        var none = caster.ApplyTrapEffect(Firing(30002, Counters(90, 5, 8, 2), 0) with { Shooters = [new(Box, 30007, Counters(0, 4, 0, 0))] }).Victims.Single();
        Assert.AreEqual((SessionTrapHit.NoShooter, 0), (none.Hit, launches.Count));
        Assert.IsNull(owner.BeginTrapShot("", Hero, 1));
        Assert.IsNull(owner.BeginTrapShot(Box, Hero, -1));
    }

    [TestMethod]
    public void The_heros_armed_critical_miss_fate_turns_the_shots_hit_into_a_miss_that_pops_on_the_shooters_own_tile()
    {
        // 0x4B3170: a PC victim's armed fate kind 3 (0x4F5270, used up) makes the hit a critical miss; 0x4B6410 draws one d100 (no weapon, the shooter no critter: no effect), the shooter becomes the target and the ranged
        // path flies the missile to its own tile, where it pops at once: nothing struck, no event, no damage; the trap's spell is cast as before.
        var (owner, caster, launches, events) = Flying();
        using var __ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GrantHeroFatePoint(owner.Version).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArmHeroFate(owner.Version, FateKind.CriticalMiss).Disposition);
        var before = HeroHitPoints(owner);
        var draws = owner.QueryRandom().Draws;
        var shot = caster.ApplyTrapEffect(Shot()).Victims.Single();
        Assert.AreEqual((SessionTrapHit.Missed, Box, SpellCastAttemptOutcome.Started), (shot.Hit, shot.Shooter, shot.Spell));
        Assert.AreEqual(draws + 4, owner.QueryRandom().Draws, "the amount, the dispatcher's two d100 and the critical-miss procedure's one");
        Assert.AreEqual((before, 0, 0, 0), (HeroHitPoints(owner), launches.Count, events.Count, owner.QueryShotsInFlight().Count));
        Assert.IsFalse(owner.HeroFateArmed(FateKind.CriticalMiss), "the fate is used up");
        var again = caster.ApplyTrapEffect(Shot()).Victims.Single();
        Assert.AreEqual((SessionTrapHit.InFlight, 1), (again.Hit, launches.Count), "the next shot flies: the fate served one");
    }

    [TestMethod]
    public void The_heros_fate_does_not_save_an_npc_victim_and_an_unarmed_hero_is_shot_at()
    {
        var (owner, caster, launches, _) = Flying();
        using var __ = owner;
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.CriticalMiss);
        var square = Firing(30002, Counters(0, 5, 4, 4), TrapRules.SquareTwoFlag) with { Shooters = [new(Box, 30007, Counters(0, 5, 0, 0))] };
        var result = caster.ApplyTrapEffect(square);
        Assert.AreEqual(2, result.Victims.Count);
        Assert.AreEqual(SessionTrapHit.InFlight, result.Victims.Single(v => v.Entity == Guard).Hit, "the fate is the hero's alone");
        Assert.AreEqual(SessionTrapHit.Missed, result.Victims.Single(v => v.Entity == Hero).Hit);
        Assert.AreEqual(Guard, launches.Single().Victim);
    }

    [TestMethod]
    public void A_shot_in_flight_is_saved_and_after_the_load_strikes_with_the_amount_it_carried()
    {
        var (owner, caster, launches, _) = Flying();
        using var __ = owner;
        var amount = caster.ApplyTrapEffect(Shot()).Victims.Single().Amount;
        var shot = launches.Single().Shot;
        SavedFlightStateTests.Fly(owner, shot, Hero, shooter: Box, critter: false);
        var capture = owner.Capture();
        Assert.AreEqual(new SessionSavedTrapShot(shot, Box, Hero, amount), capture.Flights.TrapShots.Single());
        Assert.IsFalse(capture.Flights.IsEmpty);
        var text = GameSessionSnapshotCodec.Encode(capture);
        Assert.AreEqual(capture.Flights.TrapShots.Single(), GameSessionSnapshotCodec.TryDecode(text).Snapshot!.Flights.TrapShots.Single(), "the codec round-trips the shot");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionTrapShotFacts(Box, Hero, amount), restored.QueryTrapShot(shot));
        CollectionAssert.AreEqual(new[] { shot }, restored.QueryShotsInFlight().ToArray());
        Assert.AreEqual(Unsequenced(text), Unsequenced(GameSessionSnapshotCodec.Encode(restored.Capture())), "the restored shot saves as it was loaded");

        var (again, _) = CasterOf(restored);
        var events = new List<(string? Attacker, string Victim)>();
        again.AiNotice = (attacker, victim) => events.Add((attacker, victim));
        var before = HeroHitPoints(restored);
        var struck = again.StrikeTrapShot(shot, Hero)!;
        Assert.AreEqual((SessionTrapHit.Damaged, amount), (struck.Hit, struck.Amount));
        Assert.AreEqual(before - amount, HeroHitPoints(restored));
        CollectionAssert.AreEqual(new[] { ((string?)Box, Hero) }, events);
        Assert.AreEqual(0, restored.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void A_shot_without_its_projectile_is_not_saved_and_a_restore_refuses_a_trap_shot_the_launch_does_not_name()
    {
        var (owner, caster, launches, _) = Flying();
        using var __ = owner;
        caster.ApplyTrapEffect(Shot());
        var shot = launches.Single().Shot;
        Assert.IsTrue(owner.Capture().Flights.IsEmpty, "a shot no projectile carries would dangle");
        SavedFlightStateTests.Fly(owner, shot, Hero, shooter: Box, critter: false);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Refused(string damaged, string why)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(damaged);
            if (decoded.Snapshot is null) return;
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure, why);
        }
        var wrongShooter = text.Replace("\"shooter\":\"oid:box\",\"victim\"", "\"shooter\":\"oid:other\",\"victim\"");
        Assert.AreNotEqual(text, wrongShooter);
        Refused(wrongShooter, "the launch names the shot's shooter");
        var wrongVictim = text.Replace("\"victim\":\"pc\"", "\"victim\":\"oid:guard\"");
        Assert.AreNotEqual(text, wrongVictim);
        Refused(wrongVictim, "the launch's target is the shot's victim");
        var negative = System.Text.RegularExpressions.Regex.Replace(text, "\"amount\":\\d+", "\"amount\":-1");
        Assert.AreNotEqual(text, negative);
        Refused(negative, "an amount is not negative");
        var noProjectile = System.Text.RegularExpressions.Regex.Replace(text, "\"projectiles\":\\[.*?\\](?=,\"nextShot\")", "\"projectiles\":[]");
        Assert.AreNotEqual(text, noProjectile);
        Refused(noProjectile, "a trap's shot needs its projectile");
    }
}
