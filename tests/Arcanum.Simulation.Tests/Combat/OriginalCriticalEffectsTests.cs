using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Combat.CombatRollPurpose;
using Loc = Arcanum.Simulation.Combat.HitLocation;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-COMBAT-009: the original's critical-effect procedures (0x4B5F40, 0x4B6410) with scripted rolls: the order of every
/// draw, each threshold at its boundary, the body types, the NPC-hits-PC rule, the constitution checks and the miss effects.</summary>
[TestClass]
public sealed class OriginalCriticalEffectsTests
{
    private static readonly OriginalCriticalEffects Tables = OriginalCriticalEffects.Instance;

    // A source that plays back scripted values and records each roll's bounds.
    private sealed class Rolls((CombatRollPurpose Purpose, int Value)[] script) : ICombatRollSource
    {
        private readonly Queue<(CombatRollPurpose Purpose, int Value)> _queue = new(script);
        public List<(CombatRollPurpose Purpose, int Min, int Max)> Made { get; } = [];

        public int Roll(CombatRollPurpose purpose, int min, int max)
        {
            if (_queue.Count == 0) Assert.Fail($"Unscripted roll {purpose} ({min}..{max}).");
            var (expected, value) = _queue.Dequeue();
            Assert.AreEqual(expected, purpose, "roll order");
            Made.Add((purpose, min, max));
            return value;
        }

        public void Done() => Assert.AreEqual(0, _queue.Count, "scripted rolls left unused");
    }

    private static Combatant Npc(int body = 0, CombatWornItems worn = CombatWornItems.None) =>
        new() { CriticalBody = body, Constitution = 8, ConstitutionMaximum = 21, Worn = worn };

    private static Combatant Pc(CombatWornItems worn = CombatWornItems.None) => Npc(0, worn) with { IsPc = true };

    private static CriticalHitContext Hit(Combatant recipient, Loc location, CriticalHitChart chart = CriticalHitChart.Cutting,
        int chance = 0, bool npcHitsPc = false, bool selfHit = false) =>
        new(Npc(), recipient, CombatFixtures.Sword(), chart, location, false, chance, npcHitsPc, selfHit);

    private static CriticalHitEffects Roll(CriticalHitContext context, params (CombatRollPurpose, int)[] script)
    {
        var rolls = new Rolls(script);
        var effects = Tables.RollHit(context, rolls);
        rolls.Done();
        return effects;
    }

    [TestMethod]
    public void AStunnedNpcIsKnockedOutOnTheSameThresholdAndABipedKnockedDown()
    {
        // Crushing on the head without a helmet: 5 + 10 + 10 = 25; a failed check needs more than CN 8 − 5 = 3.
        var context = Hit(Npc(), Loc.Head, CriticalHitChart.Crushing);
        Assert.AreEqual(CriticalHitEffects.Stun | CriticalHitEffects.Knockout | CriticalHitEffects.Knockdown,
            Roll(context, (CriticalStun, 25), (CriticalStunCheck, 4), (CriticalKnockout, 25), (CriticalKnockoutCheck, 21), (CriticalKnockdown, 10)),
            "a crushing head hit without a helmet neither blinds nor drops anything; knockdown 5 + 5");
        Assert.AreEqual(CriticalHitEffects.None, Roll(context, (CriticalStun, 26), (CriticalKnockdown, 11)), "26 misses the stun");
        Assert.AreEqual(CriticalHitEffects.None, Roll(context, (CriticalStun, 1), (CriticalStunCheck, 3), (CriticalKnockdown, 11)),
            "a passed check (3 ≤ CN − 5) gives no stun and no knockout roll");
        var rolls = new Rolls([(CriticalStun, 1), (CriticalStunCheck, 21), (CriticalKnockout, 1), (CriticalKnockoutCheck, 1), (CriticalKnockdown, 100)]);
        Tables.RollHit(context, rolls);
        CollectionAssert.AreEqual(new[] { (1, 100), (1, 21), (1, 100), (1, 21), (1, 100) }, rolls.Made.Select(r => (r.Min, r.Max)).ToArray(),
            "a check rolls 1 .. the race's constitution maximum");
    }

    [TestMethod]
    public void APcIsNeverKnockedOutAndAFatigueImmuneNpcNeither()
    {
        var torso = Hit(Pc(), Loc.Torso, CriticalHitChart.Electrical);
        Assert.AreEqual(CriticalHitEffects.Stun | CriticalHitEffects.Scar,
            Roll(torso, (CriticalStun, 15), (CriticalStunCheck, 20), (CriticalScar, 5), (CriticalKnockdown, 6)), "torso: stun 15, scar 5, knockdown 5");
        var immune = Hit(Npc() with { Conditions = CombatantConditions.FatigueImmune }, Loc.Torso);
        Assert.AreEqual(CriticalHitEffects.Stun, Roll(immune, (CriticalStun, 5), (CriticalStunCheck, 20), (CriticalKnockdown, 6)));
    }

    [TestMethod]
    public void TheHitLocationGivesOneInjuryAtMost()
    {
        Assert.AreEqual(CriticalHitEffects.CrippleArm, Roll(Hit(Npc(), Loc.Arm, chance: 10),
            (CriticalStun, 100), (CriticalCrippleArm, 11), (CriticalCrippleArmCheck, 4), (CriticalKnockdown, 100)), "arm: chance + 1, check at −5");
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(Npc(), Loc.Arm, chance: 10),
            (CriticalStun, 100), (CriticalCrippleArm, 12), (CriticalKnockdown, 100)));
        Assert.AreEqual(CriticalHitEffects.CrippleLeg | CriticalHitEffects.Knockdown, Roll(Hit(Npc(), Loc.Leg),
            (CriticalStun, 100), (CriticalCrippleLeg, 1), (CriticalCrippleLegCheck, 4), (CriticalKnockdown, 15)), "a leg adds 10 to the knockdown");
        // Blinding: chance + 1 without a helmet, + 0 with one, a check at 0 (fails above CN 8).
        Assert.AreEqual(CriticalHitEffects.Blind, Roll(Hit(Npc(), Loc.Head, chance: 4),
            (CriticalStun, 100), (CriticalBlind, 5), (CriticalBlindCheck, 9), (CriticalKnockdown, 100)));
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(Npc(worn: CombatWornItems.Helmet), Loc.Head, chance: 4),
            (CriticalStun, 100), (CriticalBlind, 5), (CriticalKnockdown, 100), (CriticalDamageArmor, 100)));
        Assert.AreEqual(CriticalHitEffects.DropHelmet, Roll(Hit(Npc(worn: CombatWornItems.Helmet), Loc.Head, CriticalHitChart.Crushing),
            (CriticalStun, 100), (CriticalDropHelmet, 5), (CriticalKnockdown, 100), (CriticalDamageArmor, 100)), "crushing drops a helmet, no check");
        // An NPC hitting a PC never blinds it.
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(Pc(), Loc.Head, npcHitsPc: true),
            (CriticalStun, 100), (CriticalScar, 100), (CriticalKnockdown, 100)));
    }

    [TestMethod]
    public void TheBodyTypeDecidesWhichSectionsRun()
    {
        var weaponAndBoots = CombatWornItems.Weapon | CombatWornItems.Boots;
        Assert.AreEqual(CriticalHitEffects.DamageWeapon | CriticalHitEffects.DamageArmor,
            Roll(Hit(Npc(2, weaponAndBoots), Loc.Leg), (CriticalDamageWeapon, 10), (CriticalDropWeapon, 11), (CriticalDamageArmor, 10)),
            "body type 2 only takes the item effects");
        Assert.AreEqual(CriticalHitEffects.Knockdown, Roll(Hit(Npc() with { Conditions = CombatantConditions.Undead }, Loc.Arm),
            (CriticalKnockdown, 5)), "the undead skip stun and injuries but fall");
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(Npc(4), Loc.Leg), (CriticalStun, 100)), "body type 4: the head only");
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(Npc(5), Loc.Arm), (CriticalStun, 100)), "body type 5: no arms");
        Assert.AreEqual(CriticalHitEffects.CrippleLeg, Roll(Hit(Npc(5), Loc.Leg),
            (CriticalStun, 100), (CriticalCrippleLeg, 1), (CriticalCrippleLegCheck, 21)), "no knockdown beyond body type 0");
        Assert.AreEqual(CriticalHitEffects.CrippleArm, Roll(Hit(Npc(1), Loc.Arm),
            (CriticalStun, 100), (CriticalCrippleArm, 1), (CriticalCrippleArmCheck, 21)));
    }

    [TestMethod]
    public void TheWeaponRollsAreIndependentAndAnNpcNeverDisarmsAPc()
    {
        Assert.AreEqual(CriticalHitEffects.DropWeapon, Roll(Hit(Npc(2, CombatWornItems.Weapon), Loc.Torso),
            (CriticalDamageWeapon, 11), (CriticalDropWeapon, 10)));
        Assert.AreEqual(CriticalHitEffects.DamageWeapon, Roll(Hit(Pc(CombatWornItems.Weapon), Loc.Torso, npcHitsPc: true),
            (CriticalStun, 100), (CriticalScar, 100), (CriticalKnockdown, 100), (CriticalDamageWeapon, 1)));
    }

    [TestMethod]
    public void AScarredPcThatHitItselfIsNotScarredAgain()
    {
        var scarred = Pc() with { Scarred = true };
        Assert.AreEqual(CriticalHitEffects.None, Roll(Hit(scarred, Loc.Torso, selfHit: true), (CriticalStun, 100), (CriticalKnockdown, 100)));
        Assert.AreEqual(CriticalHitEffects.Scar, Roll(Hit(scarred, Loc.Torso), (CriticalStun, 100), (CriticalScar, 5), (CriticalKnockdown, 100)),
            "another's hit scars a scarred PC again");
        Assert.AreEqual(CriticalHitEffects.Scar, Roll(Hit(Pc(), Loc.Torso, selfHit: true), (CriticalStun, 100), (CriticalScar, 5), (CriticalKnockdown, 100)));
    }

    [TestMethod]
    public void ACriticalMissRollsTheWeaponThenTheAmmunition()
    {
        CriticalMissEffects Miss(CombatWeapon weapon, CriticalMissChart chart, int chance, params (CombatRollPurpose, int)[] script)
        {
            var rolls = new Rolls(script);
            var effects = Tables.RollMiss(new(Npc(), weapon, chart, chance), rolls);
            rolls.Done();
            return effects;
        }
        Assert.AreEqual(CriticalMissEffects.None, Miss(CombatWeapon.Unarmed(), CriticalMissChart.Hands, 0));
        Assert.AreEqual(CriticalMissEffects.DestroyWeapon, Miss(CombatFixtures.Sword(), CriticalMissChart.Bladed, 2, (CriticalDestroyWeapon, 3)));
        var gun = CombatFixtures.Sword() with { UsesAmmunition = true };
        Assert.AreEqual(CriticalMissEffects.ExplodeWeapon | CriticalMissEffects.LoseAmmo, Miss(gun, CriticalMissChart.Explosive, 0,
            (CriticalDestroyWeapon, 2), (CriticalExplodeWeapon, 1), (CriticalDestroyAmmo, 2), (CriticalLoseAmmo, 11)));
        Assert.AreEqual(CriticalMissEffects.DestroyAmmo, Miss(gun, CriticalMissChart.Guns, 0, (CriticalDestroyWeapon, 2), (CriticalDestroyAmmo, 1)),
            "all the ammunition ends the ammunition rolls");
    }

    [TestMethod]
    public void TheResolverRollsTheTablesAfterTheMultipliersAndFallsBackWhenNothingCameOut()
    {
        var request = CombatFixtures.Melee() with { Target = Npc(2) };
        var resolution = CombatFixtures.Run(request,
            CombatFixtures.Concat(CombatFixtures.CriticalHit(), [(CombatFixtures.Normal, 5)], CombatFixtures.NoMultiplier()), Tables);
        Assert.AreEqual((CriticalHitEffects.None, DamageMultiplier.OneAndHalf, true),
            (resolution.Damage!.HitEffects, resolution.Damage.Multiplier, resolution.Damage.MultiplierFromFallback));
        // A biped is knocked down by the same critical: the table rolls after the multipliers, and no fallback follows.
        var biped = CombatFixtures.Run(request with { Target = Npc() }, CombatFixtures.Concat(CombatFixtures.CriticalHit(), [(CombatFixtures.Normal, 5)],
            CombatFixtures.NoMultiplier(), [(CriticalStun, 100), (CriticalKnockdown, 1)]), Tables);
        Assert.AreEqual((CriticalHitEffects.Knockdown, DamageMultiplier.None), (biped.Damage!.HitEffects, biped.Damage.Multiplier));
    }
}
