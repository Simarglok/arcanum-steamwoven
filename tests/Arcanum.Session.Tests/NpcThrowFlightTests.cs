using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.NpcThrowStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1717 (PF1, PF6, PF7, PF10; 0x4B3C00 → 0x4B3170): an NPC's thrown weapon flies when the host can fly it. The check and a miss's scatter are decided at the throw, the weapon lies hidden
// on the thrower's cell, the hit's damage waits for the strike (LandNpcShot) and the item lands where the flight ends (LandThrownItem). Values are invented (NpcThrowStateTests' thrower and dagger).
[TestClass]
public sealed class NpcThrowFlightTests
{
    private static bool Hit(SessionNpcThrowResult result) => result.Attack!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;

    // A session whose flying throw at the hero came out a hit (or a miss); the disposed ones are the seeds that did not.
    private static (GameSessionOwner Owner, SessionNpcThrowResult Result) FlyingThrow(bool hit)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var owner = Owner(seed);
            var result = owner.ThrowByNpc(owner.Version, AtHero() with { Flight = true });
            if (result.Attack?.Resolution?.Refusal == AttackRefusal.None && Hit(result) == hit) return (owner, result);
            owner.Dispose();
        }
        throw new AssertFailedException($"no seed gave a {(hit ? "hit" : "miss")}");
    }

    private static SessionNpcAttack Strike(Dictionary<EquipmentSlot, SessionWearItem>? worn = null) =>
        new(Combat()) { AttackerWorn = worn ?? new Dictionary<EquipmentSlot, SessionWearItem>(), AttackerCell = ThrowerCell, Distance = 1 };

    [TestMethod]
    public void AThrownWeaponThatHitsFliesToTheStruckCritterAndItsDamageWaitsForTheStrike()
    {
        var (owner, result) = FlyingThrow(hit: true);
        using var _ = owner;
        var life = owner.QueryHeroLife()!.Record.State.HitPointDamage;
        var shot = result.Shots.Single();
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (CellCoord?)null, (CellCoord?)HeroCell), (result.Disposition, result.Failure, result.Landed, result.Destination),
            "no landing yet: the flight goes to the struck critter's cell");
        Assert.IsNull(result.Attack!.Life, "the hit's damage waits for the strike (DeferHit)");
        Assert.AreEqual(life, owner.QueryHeroLife()!.Record.State.HitPointDamage);
        Assert.IsTrue(owner.QueryItemInFlight(Dagger), "the dagger flies, hidden");
        Assert.AreEqual(ThrowerCell, Lying(owner, Dagger), "it lies on the thrower's cell while it flies (0x4415C0)");
        Assert.IsFalse(owner.QueryItemDecays().Any(decay => decay.Entity == Dagger), "no decay (TW6)");
        Assert.AreEqual(new SessionThrownShot(Dagger, owner.QueryThrownShot(shot)!.Value.Range), owner.QueryThrownShot(shot));
        Assert.AreEqual(new SessionNpcShotFacts(Thrower, true, owner.QueryThrownShot(shot)!.Value.Range, SessionNpcShotFacts.HeroTarget), owner.QueryNpcShot(shot), "the shot carries the decided hit and the thrown range");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count, "the thrown weapon is one shot in both stores");
        Assert.IsTrue(owner.QueryShotEndItemEvents(shot, HeroCell).Single() is { Item: Dagger }, "its item event is asked at the end (PF10)");

        // The strike lands the hit decided at the throw; the dagger stays in flight for its landing (the weapon is no longer the thrower's).
        var struck = owner.LandNpcShot(owner.Version, shot, Strike());
        Assert.AreEqual((GameSessionFailure.None, true), (struck.Failure, struck.Life is not null), "the strike applies the damage");
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.HitPointDamage > life || struck.Life!.HitPointDamage == 0);
        Assert.IsTrue(owner.QueryItemInFlight(Dagger), "a struck throw waits for its item's landing");
        Assert.IsNull(owner.QueryNpcShot(shot), "the check ends with the strike");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, shot, Strike()).Failure, "a second strike is refused");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, HeroCell).Disposition);
        Assert.AreEqual((false, 0, (CellCoord?)HeroCell), (owner.QueryItemInFlight(Dagger), owner.QueryShotsInFlight().Count, Lying(owner, Dagger)), "the dagger lies where the flight ended");
    }

    [TestMethod]
    public void AThrownWeaponThatMissesFliesToTheScatterTileAndStrikesNothingOfItsOwnTarget()
    {
        var (owner, result) = FlyingThrow(hit: false);
        using var _ = owner;
        var shot = result.Shots.Single();
        var level = Combat().SkillValues(CombatSkill.Throwing, LifeState.Unharmed).Level;
        Assert.AreEqual(ThrowRules.ScatterTiles(level, ProjectileRules.Distance(ThrowerCell, HeroCell)), ProjectileRules.Distance(result.Destination!.Value, HeroCell), "the flight goes to the scatter tile");
        Assert.AreEqual((GameSessionDisposition.Changed, (CellCoord?)null), (result.Disposition, result.Landed));
        Assert.IsTrue(owner.QueryItemInFlight(Dagger));
        Assert.AreEqual(false, owner.QueryNpcShot(shot)!.Value.Hit);
        // A miss that meets its own target is refused (the hero is no stray); the flight ends at its tile and lands the item there.
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, shot, Strike()).Failure);
        var end = result.Destination.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, end).Disposition);
        Assert.AreEqual(((CellCoord?)end, 0, false), (Lying(owner, Dagger), owner.QueryShotsInFlight().Count, owner.QueryItemInFlight(Dagger)));
    }

    [TestMethod]
    public void AThrowAtAnEmptyLocationFliesUncheckedAndLandsWhereTheFlightEnds()
    {
        using var owner = Owner();
        var draws = owner.QueryRandom().Draws;
        var tile = new CellCoord(98, 203);
        var result = owner.ThrowByNpc(owner.Version, new(Combat(), Dagger, 15, ThrowerCell, tile) { ThrowerWorn = Wielded(), Flight = true });
        Assert.AreEqual((GameSessionDisposition.Changed, (SessionAttackResult?)null, (CellCoord?)null, (CellCoord?)tile), (result.Disposition, result.Attack, result.Landed, result.Destination));
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "nothing is rolled without a struck critter");
        var shot = result.Shots.Single();
        Assert.IsTrue(owner.QueryItemInFlight(Dagger));
        Assert.IsNull(owner.QueryNpcShot(shot), "an unchecked flight carries no check");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, tile).Disposition);
        Assert.AreEqual((CellCoord?)tile, Lying(owner, Dagger));
    }

    [TestMethod]
    public void AFlightTheHostGivesUpEndsBothStoresAndLeavesTheItemOnTheThrowersCell()
    {
        var (owner, result) = FlyingThrow(hit: true);
        using var _ = owner;
        var shot = result.Shots.Single();
        Assert.IsTrue(owner.EndShot(shot));
        Assert.AreEqual((0, false, null, null), (owner.QueryShotsInFlight().Count, owner.QueryItemInFlight(Dagger), (object?)owner.QueryNpcShot(shot), (object?)owner.QueryThrownShot(shot)));
        Assert.AreEqual(ThrowerCell, Lying(owner, Dagger));
    }

    [TestMethod]
    public void WithoutAHostThatFliesTheThrowLandsAtOnceAsBefore()
    {
        using var owner = Owner(5);
        var result = owner.ThrowByNpc(owner.Version, AtHero());
        Assert.AreEqual(0, result.Shots.Count);
        Assert.IsNotNull(result.Landed);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }
}
