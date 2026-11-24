using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.NpcShotFlightStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1140 (RA4, RH5, PF8, OD1-OD5): an NPC's ranged attack on the door, container, scenery or wall that stops its line of fire (decided like the hero's object attack, its arrow carries the hit), and an arrow
// that meets one past its destination (a forced hit): the object's damage and break, the shooter's weapon wear while it stays whole, no witnesses. Objects, materials and hit points are invented.
[TestClass]
public sealed class NpcShotObjectStrikeTests
{
    private static SessionObjectTarget Door(string entity = "sector:1:7", int points = 6, bool invulnerable = false) => new(Exterior, entity, new SessionObjectBaseline
    {
        Kind = DamageableObjectKind.Portal, Points = points, Material = ObjectMaterial.Cloth, Invulnerable = invulnerable,
        Openable = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Portal, 0, 0, 0),
    });

    private static SessionObjectTarget Scenery(string entity = "sector:1:9", int points = 6) => new(Exterior, entity, new SessionObjectBaseline
    {
        Kind = DamageableObjectKind.Scenery, Points = points, Material = ObjectMaterial.Cloth,
    });

    // A shot decided as wanted, in flight.
    private static SessionShot Shot(GameSessionOwner owner, SessionCritterCombat archer, bool hit) => Next(owner, Shoot(archer), hit).Result.Shot!.Value;

    [TestMethod]
    public void AShotThatHitsItsAimedCritterStillStrikesAnObjectOnItsWayWithAForcedHit()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var shot = Shot(owner, archer, hit: true);
        var landed = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn() });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, GameSessionFailure.None), (landed.Disposition, landed.Failure, landed.StepFailure));
        Assert.AreEqual((AttackOutcome.Hit, HitLocation.Torso), (landed.Resolution!.Outcome, landed.Resolution.HitLocation), "a forced hit at the torso");
        Assert.IsTrue(landed.Object!.Damage > 0);
        Assert.AreEqual(landed.Object.Damage, owner.QueryObject(door).Damage, "the door's damage is the session's now");
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count, "the shot ends with its strike");
        Assert.IsNull(owner.QueryNpcShot(shot));
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn() }).Failure, "a second strike is refused");
    }

    [TestMethod]
    public void AMissedShotStrikesAnObjectToo()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var shot = Shot(owner, archer, hit: false);
        var landed = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn() });
        Assert.AreEqual(GameSessionFailure.None, landed.Failure);
        Assert.IsTrue(landed.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        Assert.IsTrue(owner.QueryObject(door).Damage > 0);
    }

    [TestMethod]
    public void ArrowsBreakADoorAndItsHostStepsHaveNoWitnesses()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door(points: 4);
        SessionObjectAttackResult? last = null;
        for (var arrow = 0; arrow < 50 && last?.Object?.Broken != true; arrow++)
        {
            var arrowShot = Shot(owner, archer, hit: true);
            last = owner.LandNpcShotOnObject(owner.Version, arrowShot, new(archer) { Target = door, AttackerWorn = Worn() });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (last.Failure, last.StepFailure));
            Assert.AreNotEqual(SessionObjectHostSteps.None, last.Host & (SessionObjectHostSteps.WitnessesHearAttack | SessionObjectHostSteps.WitnessesHearBreak), "the witness step stays (the AI host's rule admits only a PC-led shooter, ARC-AI-2084)");
        }
        Assert.IsTrue(last!.Object!.Broken, "the door breaks within a bounded run");
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(door).Condition);
        var late = Shot(owner, archer, hit: true);
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.LandNpcShotOnObject(owner.Version, late, new(archer) { Target = door, AttackerWorn = Worn() }).Failure,
            "a destroyed door is gone");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count, "a refused strike leaves the shot in flight");
    }

    [TestMethod]
    public void ABustedSceneryNamesItsBustScriptAndBecomesInvulnerable()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var scenery = Scenery(points: 2);
        SessionObjectAttackResult? last = null;
        for (var arrow = 0; arrow < 50 && last?.Object?.Broken != true; arrow++)
        {
            var arrowShot = Shot(owner, archer, hit: true);
            last = owner.LandNpcShotOnObject(owner.Version, arrowShot, new(archer) { Target = scenery, AttackerWorn = Worn() });
        }
        Assert.IsTrue(last!.Object!.Broken);
        Assert.IsTrue(last.Host.HasFlag(SessionObjectHostSteps.BustScript), "the host runs the BUST script");
        Assert.AreEqual(SessionObjectCondition.Busted, owner.QueryObject(scenery).Condition);
    }

    [TestMethod]
    public void AnInvulnerableObjectTakesNothingButTheShotIsSpent()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door(invulnerable: true);
        var asked = 0;
        var shot = Shot(owner, archer, hit: true);
        var landed = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn(), TakingDamage = () => { asked++; return true; } });
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (landed.Disposition, landed.Failure));
        Assert.AreEqual(1, asked, "the object's taking-damage script is asked even for an invulnerable object (GT198)");
        Assert.IsNull(landed.Object);
        Assert.AreEqual(0, owner.QueryObject(door).Damage);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void ATakingDamageScriptThatSkipsTheDefaultCancelsTheDamage()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var shot = Shot(owner, archer, hit: true);
        var landed = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn(), TakingDamage = () => false });
        Assert.IsTrue(landed.Vetoed);
        Assert.AreEqual(0, owner.QueryObject(door).Damage);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count, "the shot is spent");
    }

    [TestMethod]
    public void AWallTakesNothingAndTheShotEnds()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var shot = Shot(owner, archer, hit: false);
        var landed = owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Wall = true, AttackerWorn = Worn() });
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (landed.Disposition, landed.Failure));
        Assert.AreEqual(DamagePrevention.Wall, landed.Resolution!.Damage!.Prevented);
        Assert.IsNull(landed.Object);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AnNpcsHitOnAWallOffersTheWeaponsHitScriptsAndTheWallsTakingDamageScript()
    {
        // ARC-COMBAT-2538 (ledger RH5): as the hero's (ARC-COMBAT-1973), the hit step offers the weapon's hit script and the damage step the wall's taking-damage script before it leaves for a wall; the answer changes nothing.
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var heard = new List<SessionHitScriptEvent>();
        var asked = 0;
        // An attack that lands at once offers both once.
        var now = owner.AttackObjectByNpc(owner.Version, Aim(archer, null, flight: false, wall: true) with { HitScripts = heard.Add, TakingDamage = () => { asked++; return false; } });
        Assert.AreEqual(DamagePrevention.Wall, now.Resolution!.Damage!.Prevented);
        Assert.AreEqual((1, 1), (heard.Count, asked), "both scripts are offered once");
        Assert.IsFalse(heard[0].Critical || heard[0].Projectile);
        Assert.IsFalse(now.Vetoed, "a wall takes nothing whatever the script says");
        // An arrow that flies asks neither at the shot, both at its strike (a projectile hit).
        var flying = owner.AttackObjectByNpc(owner.Version, Aim(archer, null, wall: true) with { HitScripts = heard.Add, TakingDamage = () => { asked++; return true; } });
        Assert.AreEqual((1, 1, 1), (flying.Shots.Count, heard.Count, asked), "nothing is asked at the shot");
        var landed = owner.LandNpcShotOnObject(owner.Version, flying.Shot!.Value, Aim(archer, null, wall: true, distance: 1) with { HitScripts = heard.Add, TakingDamage = () => { asked++; return true; } });
        Assert.AreEqual(GameSessionFailure.None, landed.Failure);
        Assert.AreEqual((2, 2), (heard.Count, asked));
        Assert.IsTrue(heard[1].Projectile);
    }

    [TestMethod]
    public void AShotThatIsNoNpcArrowOrAnotherShootersOrBadTargetsAreRefused()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var shot = Shot(owner, archer, hit: true);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShotOnObject(owner.Version, shot, new(Combat(Other, BowFields())) { Target = door, AttackerWorn = Worn() }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShotOnObject(owner.Version, new SessionShot(999), new(archer) { Target = door, AttackerWorn = Worn() }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { AttackerWorn = Worn() }).Failure, "no object named");
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, Wall = true, AttackerWorn = Worn() }).Failure, "two named");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count, "every refusal leaves the shot in flight");
        Assert.AreEqual(0, owner.QueryObject(door).Damage);
    }

    [TestMethod]
    public void AStaleVersionIsRefusedAndNothingIsSpent()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var shot = Shot(owner, archer, hit: true);
        var stale = owner.Version;
        var door = Door();
        Assert.AreEqual(GameSessionFailure.None, owner.LandNpcShotOnObject(owner.Version, shot, new(archer) { Target = door, AttackerWorn = Worn() }).Failure);
        var next = Shot(owner, archer, hit: true);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.LandNpcShotOnObject(stale, next, new(archer) { Target = door, AttackerWorn = Worn() }).Failure);
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count);
    }

    private static SessionNpcObjectAttack Aim(SessionCritterCombat archer, SessionObjectTarget? target, bool flight = true, int distance = 6, bool wall = false) =>
        new(archer) { Target = target, Wall = wall, Distance = distance, AttackerWorn = Worn(), Flight = flight };

    [TestMethod]
    public void ABlockedLineStrikesTheDoorInsteadWithAShotThatCarriesAHitWithoutASkillRoll()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var aimed = owner.AttackObjectByNpc(owner.Version, Aim(archer, door));
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (aimed.Disposition, aimed.Failure));
        Assert.AreEqual(AttackOutcome.Hit, aimed.Resolution!.Outcome, "an object is hit without the skill roll (RH5)");
        Assert.IsNull(aimed.Resolution.Damage, "the damage waits for the strike");
        Assert.AreEqual(1, aimed.Shots.Count);
        Assert.AreEqual(0, owner.QueryObject(door).Damage);
        var facts = owner.QueryNpcShot(aimed.Shot!.Value)!.Value;
        Assert.AreEqual((Archer, true, door.Entity), (facts.Shooter, facts.Hit, facts.Target), "the shot carries the door as its target");
        var landed = owner.LandNpcShotOnObject(owner.Version, aimed.Shot.Value, Aim(archer, door, distance: 1));
        Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (landed.Failure, landed.StepFailure));
        Assert.IsTrue(landed.Object!.Damage > 0);
        Assert.AreEqual(landed.Object.Damage, owner.QueryObject(door).Damage);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void WithoutAFlyingHostTheAttackOnTheDoorLandsAtOnceAndAWallTakesNothing()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var now = owner.AttackObjectByNpc(owner.Version, Aim(archer, door, flight: false));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 0), (now.Disposition, now.Failure, now.Shots.Count));
        Assert.IsTrue(now.Object!.Damage > 0);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
        var wall = owner.AttackObjectByNpc(owner.Version, Aim(archer, null, flight: false, wall: true));
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, DamagePrevention.Wall), (wall.Disposition, wall.Failure, wall.Resolution!.Damage!.Prevented));
        Assert.IsNull(wall.Object);
        var flyingWall = owner.AttackObjectByNpc(owner.Version, Aim(archer, null, wall: true));
        Assert.AreEqual(1, flyingWall.Shots.Count);
        Assert.AreEqual(GameSessionFailure.None, owner.LandNpcShotOnObject(owner.Version, flyingWall.Shot!.Value, Aim(archer, null, wall: true, distance: 1)).Failure);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AnObjectBeyondTheWeaponsReachIsRefusedBeforeAnyRoll()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var draws = owner.Capture();
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackObjectByNpc(owner.Version, Aim(archer, Door(), distance: 13)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackObjectByNpc(owner.Version, Aim(archer, Door(), distance: -1)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.AttackObjectByNpc(owner.Version, Aim(archer, null)).Failure);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
        Assert.AreEqual(draws.Revision, owner.Capture().Revision, "nothing committed");
    }

    [TestMethod]
    public void AShotAimedAtADoorThatStrikesAnotherObjectIsAForcedHit()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var door = Door();
        var other = Scenery();
        var aimed = owner.AttackObjectByNpc(owner.Version, Aim(archer, door));
        var landed = owner.LandNpcShotOnObject(owner.Version, aimed.Shot!.Value, Aim(archer, other, distance: 1));
        Assert.AreEqual(GameSessionFailure.None, landed.Failure);
        Assert.AreEqual(HitLocation.Torso, landed.Resolution!.HitLocation, "a stray strike is a forced hit at the torso");
        Assert.IsTrue(owner.QueryObject(other).Damage > 0);
        Assert.AreEqual(0, owner.QueryObject(door).Damage);
    }
}
