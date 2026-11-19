using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.NpcThrowStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-2523 (ledger TW11, 0x4C7160 with no defender, 0x4B39B0): an NPC's throw that names no struck critter (a weapon it wields, a grenade) rolls as the hero's throw at a location does: the hit and
// the critical with only the distance, the line of fire's cost, the light at the tile and the thrower's flags, then on a miss the scatter direction; a hit lands on the location and strikes nothing,
// a miss lands on its scattered tile, a critical miss turns on the thrower. Values are invented (NpcThrowStateTests' thrower, dagger and grenade).
[TestClass]
public sealed class NpcThrowLocationTests
{
    private static readonly CellCoord Tile = new(98, 203);

    private static SessionNpcThrow Dagger_(bool flight = false) => new(Combat(), Dagger, 15, ThrowerCell, Tile) { ThrowerWorn = Wielded(), Flight = flight };
    private static SessionNpcThrow Lob(CellCoord tile) => new(Combat(), Grenade, 5, ThrowerCell, tile);

    private static bool Hit(SessionAttackResult attack) => attack.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;

    private static int ScatterTiles() => ThrowRules.ScatterTiles(Combat().SkillValues(CombatSkill.Throwing, LifeState.Unharmed).Level, ProjectileRules.Distance(ThrowerCell, Tile));

    // A session whose throw came out as wanted (the disposed ones are the seeds that did not).
    private static (GameSessionOwner Owner, SessionNpcThrowResult Result) Thrown(Func<SessionNpcThrow> request, Func<SessionAttackResult, bool> wanted)
    {
        for (ulong seed = 1; seed <= 600; seed++)
        {
            var owner = Owner(seed);
            var result = owner.ThrowByNpc(owner.Version, request());
            if (result.Attack?.Resolution?.Refusal == AttackRefusal.None && wanted(result.Attack)) return (owner, result);
            owner.Dispose();
        }
        throw new AssertFailedException("no seed gave the wanted outcome");
    }

    [TestMethod]
    public void AWeaponThrownAtAnEmptyLocationRollsAndLandsOnTheLocationOnAHitOrTheScatterTileOnAMiss()
    {
        var scatter = ScatterTiles();
        Assert.IsTrue(scatter is > 0 and < 9, $"the scatter is shorter than the distance: {scatter}");
        var hits = 0; var misses = 0;
        for (ulong seed = 1; seed <= 300 && (hits < 2 || misses < 2); seed++)
        {
            using var owner = Owner(seed);
            var draws = owner.QueryRandom().Draws;
            var result = owner.ThrowByNpc(owner.Version, Dagger_());
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
            var attack = result.Attack!;
            Assert.IsTrue(owner.QueryRandom().Draws > draws, "the throw rolls (TW11)");
            Assert.AreEqual(AttackRefusal.None, attack.Resolution!.Refusal);
            Assert.IsNotNull(attack.Resolution.AttackCheck, "the check is the throw's");
            var landed = result.Landed!.Value;
            Assert.AreEqual(landed, Lying(owner, Dagger), "the dagger lies where it landed");
            Assert.AreEqual(0, result.Shots.Count);
            Assert.IsFalse(owner.QueryItemDecays().Any(decay => decay.Entity == Dagger), "a thrown item gets no decay");
            if (Hit(attack))
            {
                hits++;
                Assert.AreEqual(Tile, landed, "a hit lands on the location");
                Assert.IsNull(attack.Life, "a hit with no struck critter carries no damage");
                Assert.IsNull(attack.Scatter);
            }
            else if (attack.Resolution.Outcome == AttackOutcome.CriticalMiss)
                continue;
            else
            {
                misses++;
                Assert.AreEqual(scatter, ProjectileRules.Distance(landed, Tile), $"a miss scatters from the location: {landed}");
                Assert.AreEqual(landed, attack.Scatter);
            }
        }
        Assert.IsTrue(hits >= 2 && misses >= 2, $"both outcomes within the bounded run: {hits} hits, {misses} misses");
    }

    [TestMethod]
    public void TheTargetsLightAndTheLinesCostAreTermsOfTheLocationRoll()
    {
        // A dark tile and a costly line lower the chance: over a bounded run the dark, covered throw hits less often than the lit, open one.
        int Hits(int? light, int cost)
        {
            var hits = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                using var owner = Owner(seed);
                var result = owner.ThrowByNpc(owner.Version, Dagger_() with { TargetLight = light, LineOfFire = new(cost, false) });
                if (result.Attack?.Resolution is { Refusal: AttackRefusal.None } && Hit(result.Attack)) hits++;
            }
            return hits;
        }
        var open = Hits(255, 0);
        Assert.IsTrue(open > 0, "some throws hit");
        Assert.IsTrue(Hits(0, 40) < open, "the dark tile and the line's cost lower the hit chance");
    }

    [TestMethod]
    public void AThrownWeaponThatFliesCarriesItsLocationCheckAndStrikesNothingOnAHit()
    {
        var (owner, result) = Thrown(() => Dagger_(flight: true), attack => Hit(attack));
        using var _ = owner;
        var shot = result.Shots.Single();
        Assert.AreEqual((GameSessionDisposition.Changed, (CellCoord?)null, (CellCoord?)Tile), (result.Disposition, result.Landed, result.Destination), "no landing yet: the flight goes to the location");
        Assert.IsTrue(owner.QueryItemInFlight(Dagger), "the dagger flies, hidden");
        Assert.AreEqual(ThrowerCell, Lying(owner, Dagger), "it lies on the thrower's cell while it flies");
        var facts = owner.QueryNpcShot(shot)!.Value;
        Assert.AreEqual((Thrower, true, SessionNpcShotFacts.LocationTarget), (facts.Shooter, facts.Hit, facts.Target), "the shot carries the decided hit and names no struck critter");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, Tile).Disposition);
        Assert.AreEqual(((CellCoord?)Tile, 0, null), (Lying(owner, Dagger), owner.QueryShotsInFlight().Count, (object?)owner.QueryNpcShot(shot)), "the check ends with the landing");
    }

    [TestMethod]
    public void AThrownWeaponThatMissesFliesToTheScatterTileAndItsStrayStrikeIsAForcedHit()
    {
        var (owner, result) = Thrown(() => Dagger_(flight: true), attack => !Hit(attack) && attack.Resolution!.Outcome != AttackOutcome.CriticalMiss);
        using var _ = owner;
        var shot = result.Shots.Single();
        Assert.AreEqual(ScatterTiles(), ProjectileRules.Distance(result.Destination!.Value, Tile), "the flight goes to the scatter tile");
        Assert.AreEqual(false, owner.QueryNpcShot(shot)!.Value.Hit);
        // The hero standing on the flight's path is a stray target: the strike is a forced hit of the missed shot's weapon (the carried check does not name it).
        var life = owner.QueryHeroLife()!.Record.State.HitPointDamage;
        var strike = owner.LandNpcShot(owner.Version, shot, new(Combat()) { AttackerWorn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>(), AttackerCell = ThrowerCell, Distance = 1 });
        Assert.AreEqual(GameSessionFailure.None, strike.Failure);
        Assert.AreEqual(AttackOutcome.Hit, strike.Resolution!.Outcome, "a stray strike of a missed shot is a hit");
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.HitPointDamage > life || strike.Life is { HitPointDamage: 0 });
    }

    [TestMethod]
    public void ACriticalMissTurnsTheThrowOnTheThrowerAfterTheItemLeftItsHand()
    {
        var (owner, result) = Thrown(() => Dagger_(), attack => attack.Resolution!.Outcome == AttackOutcome.CriticalMiss && attack.Resolution.Damage is not null);
        using var _ = owner;
        var attack = result.Attack!;
        Assert.AreEqual(DamageRecipient.Attacker, attack.Recipient, "the thrower is the victim");
        Assert.IsNotNull(attack.Life, "its damage step ran");
        Assert.IsTrue(owner.QueryLife(Combat().Target).View!.Record.State.HitPointDamage > 0 || attack.Life!.HitPointDamage == 0, "the thrower took the hit");
        Assert.IsNotNull(result.Landed, "the item left the hand and lies on its landing tile");
        Assert.AreEqual(result.Landed, Lying(owner, Dagger));
        Assert.AreEqual(GameSessionFailure.None, attack.StepFailure);
    }

    [TestMethod]
    public void AGrenadeLobbedAtALocationRollsAndFliesToTheLocationOrItsScatterTile()
    {
        var scatter = ScatterTiles();
        var hits = 0; var misses = 0;
        for (ulong seed = 1; seed <= 300 && (hits < 2 || misses < 2); seed++)
        {
            using var owner = Owner(seed);
            var draws = owner.QueryRandom().Draws;
            var result = owner.ThrowByNpc(owner.Version, Lob(Tile));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (CellCoord?)null), (result.Disposition, result.Failure, result.Landed));
            Assert.IsTrue(owner.QueryRandom().Draws > draws, "the lob rolls (TW11)");
            var attack = result.Attack!;
            var shot = result.Shots.Single();
            Assert.AreEqual((true, Hit(attack), SessionNpcShotFacts.LocationTarget), (owner.QueryNpcShot(shot)!.Value.Generic, owner.QueryNpcShot(shot)!.Value.Hit, owner.QueryNpcShot(shot)!.Value.Target), "a generic item's flight carries its check (ARC-COMBAT-2693)");
            Assert.IsTrue(owner.QueryItemInFlight(Grenade));
            Assert.AreEqual(ThrowerCell, Lying(owner, Grenade));
            if (Hit(attack)) { hits++; Assert.AreEqual(Tile, result.Destination, "a hit flies to the location"); }
            else if (attack.Resolution!.Outcome != AttackOutcome.CriticalMiss)
            {
                misses++;
                Assert.AreEqual(scatter, ProjectileRules.Distance(result.Destination!.Value, Tile), "a miss flies to the scatter tile");
                Assert.AreEqual(result.Destination, attack.Scatter);
            }
        }
        Assert.IsTrue(hits >= 2 && misses >= 2, $"both outcomes within the bounded run: {hits} hits, {misses} misses");
    }

    // ARC-COMBAT-2693 (0x4B2870): a critter a missed grenade passes takes a forced hit with the damage of the item's weight, the shot ends with the strike, the thrower's wielded weapon is not worn by it.
    [TestMethod]
    public void AMissedGrenadesStrayStrikeIsAForcedHitOfTheItemAndEndsTheShot()
    {
        for (ulong seed = 1; seed <= 400; seed++)
        {
            using var owner = Owner(seed);
            var result = owner.ThrowByNpc(owner.Version, Lob(Tile));
            if (result.Attack is not { Resolution: { } roll } || Hit(result.Attack) || roll.Outcome == AttackOutcome.CriticalMiss) continue;
            var shot = result.Shots.Single();
            var strike = owner.LandNpcShot(owner.Version, shot, new(Combat()) { AttackerWorn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>(), AttackerCell = ThrowerCell, Distance = 1 });
            Assert.AreEqual(GameSessionFailure.None, strike.Failure);
            Assert.AreEqual(AttackOutcome.Hit, strike.Resolution!.Outcome, "a stray strike of a missed grenade is a hit");
            Assert.IsNull(owner.QueryNpcShot(shot), "the shot ends with the strike");
            return;
        }
        Assert.Fail("no missed lob within the bounded run");
    }

    // ARC-SCRIPT-2724 (0x4B2F60, CP2): the hit event of a grenade's stray strike says the parent item is a generic item, so the host runs no critical-hit point (34) for it; a weapon's stray strike does not say so.
    [TestMethod]
    public void AGrenadesStrayStrikeTellsTheHostItsParentIsAGenericItem()
    {
        for (ulong seed = 1; seed <= 400; seed++)
        {
            using var owner = Owner(seed);
            var result = owner.ThrowByNpc(owner.Version, Lob(Tile));
            if (result.Attack is not { Resolution: { } roll } || Hit(result.Attack) || roll.Outcome == AttackOutcome.CriticalMiss) continue;
            var heard = new List<SessionHitScriptEvent>();
            var strike = owner.LandNpcShot(owner.Version, result.Shots.Single(), new(Combat()) { AttackerWorn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>(), AttackerCell = ThrowerCell, Distance = 1, HitScripts = heard.Add });
            Assert.AreEqual(GameSessionFailure.None, strike.Failure);
            var hit = heard.Single();
            Assert.AreEqual((Grenade, true, true), (hit.Weapon, hit.Projectile, hit.Generic), "the event names the grenade as a projectile's generic parent item");
            Assert.IsFalse(hit.RunsCriticalHitPoint(parentIsWeapon: true), "even a critical flag would run no critical-hit point for it");
            Assert.IsFalse((hit with { Critical = true }).RunsCriticalHitPoint(parentIsWeapon: true), "a critical stray hit of a grenade runs no point 34");
            return;
        }
        Assert.Fail("no missed lob within the bounded run");
    }

    [TestMethod]
    public void TheCriticalHitPointRunsForACriticalHitOfAWeaponItemOnly()
    {
        var swing = new SessionHitScriptEvent("w", true, false);
        Assert.IsTrue(swing.RunsCriticalHitPoint(parentIsWeapon: false), "a swing has no projectile parent: it runs");
        Assert.IsFalse((swing with { Critical = false }).RunsCriticalHitPoint(true), "no critical, no point");
        var shot = new SessionHitScriptEvent("w", true, true);
        Assert.IsTrue(shot.RunsCriticalHitPoint(true));
        Assert.IsFalse(shot.RunsCriticalHitPoint(false), "a projectile of a non-weapon item (CP2)");
        Assert.IsFalse((shot with { Generic = true }).RunsCriticalHitPoint(true), "a generic projectile parent (a grenade)");
    }

    [TestMethod]
    public void ALocationThrowOnAnExhaustedStreamThrowsUnrolledInsteadOfRefusingTheItem()
    {
        using var owner = Owner();
        Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
        var result = owner.ThrowByNpc(owner.Version, Lob(Tile));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (SessionAttackResult?)null, (CellCoord?)Tile), (result.Disposition, result.Failure, result.Attack, result.Destination));
        Assert.AreEqual(1, result.Shots.Count);
    }

    [TestMethod]
    public void ALocationThrowIsRefusedForAnInvalidLightAndTheRefusalsComeBeforeTheRoll()
    {
        using var owner = Owner();
        var draws = owner.QueryRandom().Draws;
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.ThrowByNpc(owner.Version, Dagger_() with { TargetLight = 256 }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.ThrowByNpc(owner.Version, Dagger_() with { TargetLight = -1 }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ThrowByNpc(new(owner.Identity, owner.Version.Revision + 1), Dagger_()).Failure);
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
    }
}
