using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Equipment.EquipmentRulesTests;

namespace Arcanum.Simulation.Tests.Equipment;

// The best-item choice (ARC-WORLD-034). Every item value is invented.
[TestClass]
public sealed class EquipmentChoiceTests
{
    private static readonly EquipmentChooser Plain = new() { MeleeLevel = 0 };

    private static EquipmentCandidate Weapon(int min, int max, CombatSkill skill = CombatSkill.Melee, int? ammo = null, int consumption = 0,
        DamageType type = DamageType.Normal, int range = 1) => new(new EquipmentItem
    {
        Kind = EquipmentItemKind.Weapon,
        Weapon = new EquipmentWeapon
        {
            Skill = skill, Damage = DamageRanges.None with { } is var none ? type switch
            {
                DamageType.Normal => none with { Normal = new(min, max) },
                DamageType.Fatigue => none with { Fatigue = new(min, max) },
                _ => none with { Fire = new(min, max) },
            } : none,
            AmmoType = ammo, AmmoConsumption = consumption, Range = range,
        },
    });

    private static EquipmentCandidate Piece(int worth, EquipmentSlot? worn = null, int complexity = 0, bool identified = false) =>
        new(Armor(ArmorCoverage.Helmet, complexity: complexity)) { Worth = worth, WornIn = worn, IdentifiedFlag = identified };

    [TestMethod]
    public void WorthIsStoredWorthAtLeastTwoOrThreeHundredUnidentified()
    {
        Assert.AreEqual(2, EquipmentChoice.Worth(Piece(0)));
        Assert.AreEqual(55, EquipmentChoice.Worth(Piece(55)));
        Assert.AreEqual(300, EquipmentChoice.Worth(Piece(55, complexity: 20)), "an unidentified magickal item");
        Assert.AreEqual(55, EquipmentChoice.Worth(Piece(55, complexity: 20, identified: true)));
        Assert.AreEqual(55, EquipmentChoice.Worth(Piece(55, complexity: -20)), "technological items count as identified");
        Assert.IsTrue(EquipmentChoice.Identified(Sword(complexity: 0), false));
        Assert.IsFalse(EquipmentChoice.Identified(Sword(complexity: 1), false));
    }

    [TestMethod]
    public void ThrownDamageComesFromTheWeaponOrTheWeight()
    {
        var boomerang = Weapon(2, 6, CombatSkill.Throwing);
        Assert.AreEqual(new DamageRange(2, 6), EquipmentChoice.ThrownDamage(boomerang with { Throwable = true }).Normal);
        Assert.AreEqual(new DamageRange(3, 5), EquipmentChoice.ThrownDamage(boomerang with { Weight = 250 }).Normal, "(250+99)/100, (250+49)/50");
        Assert.AreEqual(DamageRange.Zero, EquipmentChoice.ThrownDamage(boomerang with { Weight = 250 }).Fire);
        Assert.AreEqual(new DamageRange(0, 0), EquipmentChoice.ThrownDamage(Piece(1)).Normal);
    }

    [TestMethod]
    public void AWeaponScoresItsAverageDamageOfEveryTypeTimesEffectiveness()
    {
        // Melee level 0: effectiveness 25. Normal 2..8 averages 5, fire 4..6 averages 5 (the original adds every type).
        var candidate = new EquipmentCandidate(Sword() with
        {
            Weapon = Sword().Weapon! with { Damage = DamageRanges.None with { Normal = new(2, 8), Fire = new(4, 6) }, MagicDamage = DamageByType.Zero },
        });
        Assert.AreEqual(10 * 25 / 100, EquipmentChoice.WeaponScore(candidate, Plain));
        Assert.AreEqual(10 * 35 / 100, EquipmentChoice.WeaponScore(candidate, Plain with { MeleeLevel = 2 }));
        // DamageBonus +2 raises normal to 4..10 (avg 7) in melee.
        Assert.AreEqual(12 * 25 / 100, EquipmentChoice.WeaponScore(candidate, Plain with { DamageBonus = 2 }));
        Assert.AreEqual(10 * 37 / 100, EquipmentChoice.WeaponScore(candidate, Plain with { IsPc = true, Difficulty = GameDifficulty.Easy }), "25 + 12");
    }

    [TestMethod]
    public void UntrainedRangedSkillsAndZeroEffectivenessCountMinusOne()
    {
        var bow = Weapon(300, 500, CombatSkill.Bow);
        Assert.AreEqual(-4, EquipmentChoice.WeaponScore(bow, Plain), "level 0: 400 × −1 / 100");
        Assert.AreEqual(400 * 30 / 100, EquipmentChoice.WeaponScore(bow, Plain with { BowLevel = 1 }));
        Assert.AreEqual(-4, EquipmentChoice.WeaponScore(Weapon(300, 500, CombatSkill.Firearms), Plain));
        // A PC on hard difficulty with 1 throwing level: 32 − 8 = 24, still above 0.
        Assert.AreEqual(4 * 24 / 100, EquipmentChoice.WeaponScore(Weapon(3, 5, CombatSkill.Throwing) with { Throwable = true },
            Plain with { ThrowingLevel = 1, IsPc = true, Difficulty = GameDifficulty.Hard }));
    }

    [TestMethod]
    public void AWeaponWithoutAmmunitionIsSkipped()
    {
        var gun = Weapon(5, 9, CombatSkill.Firearms, ammo: 1, consumption: 1);
        Assert.IsNull(EquipmentChoice.WeaponScore(gun, Plain));
        Assert.IsNotNull(EquipmentChoice.WeaponScore(gun, Plain with { Ammunition = new Dictionary<int, int> { [1] = 1 } }));
        Assert.IsNotNull(EquipmentChoice.WeaponScore(gun with { Item = gun.Item with { Weapon = gun.Item.Weapon! with { AmmoConsumption = 0 } } }, Plain));
    }

    [TestMethod]
    public void TheBestWeaponWinsAndATieGoesToTheLaterOne()
    {
        EquipmentCandidate[] weapons = [Weapon(1, 3), Weapon(4, 8), Weapon(4, 8), Weapon(9, 20, CombatSkill.Bow)];
        Assert.AreEqual(2, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, _ => true, null, Plain), "1 and 2 tie: the later one");
        Assert.AreEqual(1, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, i => i != 2, null, Plain));
        Assert.AreEqual(0, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, [weapons[3]], _ => true, null, Plain), "the first scored wins over nothing, even negative");
        Assert.IsNull(EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, _ => false, null, Plain));
    }

    [TestMethod]
    public void WithATargetTheBestWeaponThatReachesItWins()
    {
        // Scores 37, 5, 7, 0 (effectiveness 25).
        EquipmentCandidate[] weapons = [Weapon(100, 200), Weapon(10, 30, range: 5), Weapon(20, 40, range: 6), Weapon(1, 1, range: 7)];
        // The original stores the in-range score (the reconstruction keeps the last in-range weapon).
        Assert.AreEqual(2, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, _ => true, null, Plain, targetDistance: 4));
        Assert.AreEqual(0, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, _ => true, null, Plain, targetDistance: 1));
        Assert.AreEqual(0, EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, weapons, _ => true, null, Plain, targetDistance: 9), "nothing reaches: the best overall");
    }

    [TestMethod]
    public void OtherSlotsTakeTheMostValuableItemNotWorn()
    {
        EquipmentCandidate[] pieces = [Piece(10), Piece(30), Piece(30), Piece(90, EquipmentSlot.Helmet), Piece(25)];
        Assert.AreEqual(1, EquipmentChoice.ChooseBest(EquipmentSlot.Helmet, pieces, _ => true, null, Plain), "strictly greater: the first of the 30s; worn items never");
        Assert.IsNull(EquipmentChoice.ChooseBest(EquipmentSlot.Helmet, pieces, _ => true, 30, Plain), "the kept item's 30 is not beaten");
        Assert.AreEqual(1, EquipmentChoice.ChooseBest(EquipmentSlot.Helmet, pieces, _ => true, 29, Plain));
        Assert.ThrowsExactly<ArgumentException>(() => EquipmentChoice.ChooseBest(EquipmentSlot.Weapon, pieces, _ => true, 5, Plain));
    }
}
