using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-COMBAT-017: the thrown range (0x4622A0, ledger RH4). Strengths and weights are invented.
[TestClass]
public sealed class ThrowRulesTests
{
    [TestMethod]
    public void TheRangeIsFiftyTimesStrengthOverATenthOfTheWeightOverFive()
    {
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 15), "400 / 1 / 5");
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.Apprentice, 15));
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Expert, 15), "expert adds half: 600 / 1 / 5");
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Master, 15));
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 0), "a tenth of the weight is at least 1");
        Assert.AreEqual(5, ThrowRules.Range(8, SkillTraining.None, 150), "400 / 15 = 26, / 5 = 5: divisions toward zero");
        Assert.AreEqual(9, ThrowRules.Range(9, SkillTraining.Expert, 150), "450 + 225 = 675, / 15 = 45, / 5 = 9");
    }

    [TestMethod]
    public void TheRangeIsAtLeastOne()
    {
        Assert.AreEqual(1, ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight));
        Assert.AreEqual(1, ThrowRules.Range(-3, SkillTraining.None, 15), "a negative arm counts as 1");
        Assert.AreEqual(1, ThrowRules.Range(0, SkillTraining.Expert, 15));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, (SkillTraining)9, 15));
    }

    // ARC-COMBAT-018 (0x4B39B0, ledger RH2, PS rows): (20 − level) / 5 + 1 tiles toward zero, at most the distance.
    [TestMethod]
    public void AMissedThrowScattersFewerTilesTheBetterTheThrower()
    {
        Assert.AreEqual(5, ThrowRules.ScatterTiles(0, 10));
        Assert.AreEqual(4, ThrowRules.ScatterTiles(4, 10), "16 / 5 = 3");
        Assert.AreEqual(4, ThrowRules.ScatterTiles(5, 10));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(16, 10));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(20, 10));
        Assert.AreEqual(2, ThrowRules.ScatterTiles(0, 2), "capped at the distance");
        Assert.AreEqual(0, ThrowRules.ScatterTiles(0, 0));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(24, 10), "-4 / 5 = 0 toward zero");
        Assert.AreEqual(0, ThrowRules.ScatterTiles(25, 10));
        Assert.AreEqual(-1, ThrowRules.ScatterTiles(30, 10));
    }

    // ARC-COMBAT-018 (0x4B90D0): the walk takes its steps in one rotation and stops on the last tile a step off the map left it on.
    [TestMethod]
    public void TheScatterWalksItsTilesInOneRotationAndStopsAtTheMapsEdge()
    {
        static CellCoord? Open(CellCoord cell, int rotation) => Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(cell, rotation);
        static CellCoord? Edge(CellCoord cell, int rotation) => Open(cell, rotation) is { X: >= 9 } next ? next : null;
        var from = new CellCoord(10, 10);
        Assert.AreEqual(new CellCoord(13, 10), ThrowRules.Scatter(from, 5, 3, Open));
        Assert.AreEqual(new CellCoord(7, 7), ThrowRules.Scatter(from, 0, 3, Open));
        Assert.AreEqual(new CellCoord(10, 13), ThrowRules.Scatter(from, 3, 3, Open));
        Assert.AreEqual(new CellCoord(9, 9), ThrowRules.Scatter(from, 0, 3, Edge), "the step off the map stops the walk");
        Assert.AreEqual(from, ThrowRules.Scatter(from, 4, 0, Open));
        Assert.AreEqual(from, ThrowRules.Scatter(from, 4, -1, Open), "no tiles: no walk");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Scatter(from, 8, 1, Open));
        Assert.ThrowsExactly<ArgumentNullException>(() => ThrowRules.Scatter(from, 1, 1, null!));
    }

    // ARC-COMBAT-023 (0x462330, ledger WB8): an item that is not a throwable weapon strikes with the damage of its weight.
    [TestMethod]
    public void AThrownItemsDamageIsAHundredthToAFiftiethOfItsWeight()
    {
        Assert.AreEqual(DamageRanges.None, ThrowRules.ItemDamage(0), "99 / 100 and 49 / 50 are 0");
        Assert.AreEqual(new DamageRange(1, 1), ThrowRules.ItemDamage(1).Normal);
        Assert.AreEqual(new DamageRange(1, 1), ThrowRules.ItemDamage(15).Normal, "114 / 100, 64 / 50");
        Assert.AreEqual(new DamageRange(2, 3), ThrowRules.ItemDamage(150).Normal, "249 / 100, 199 / 50");
        Assert.AreEqual(DamageRanges.None with { Normal = new(5, 10) }, ThrowRules.ItemDamage(500), "no other type");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.ItemDamage(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.ItemDamage(ThrowRules.MaxWeight + 1));
    }

    [TestMethod]
    public void OnlyAThrowableWeaponKeepsItsOwnDamage()
    {
        var sword = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, MagickTechComplexity = 50,
            Weapon = new EquipmentWeapon
            {
                Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(4, 12) }, MagicDamage = new(3, 0, 0, 0, 0), ToHit = 7,
                Range = 1, CriticalHitChart = CriticalHitChart.Edged,
            },
        };
        var thrown = ThrowRules.ThrownWeapon(sword, throwable: false, weight: 150, aptitude: 0, range: 5);
        Assert.AreEqual((WeaponKind.Weapon, 5, 7, CriticalHitChart.Edged), (thrown.Kind, thrown.Range, thrown.ToHitBonus, thrown.CriticalHitChart),
            "a weapon keeps its own values but the range");
        Assert.AreEqual(ThrowRules.ItemDamage(150), thrown.Damage, "a weapon without flag 0x10 strikes with its weight's damage");
        Assert.AreEqual(3, thrown.MagicDamageAdjustment.Normal, "and keeps its magic damage adjustment (a weapon's, 0x461590)");
        Assert.AreEqual(sword.Weapon!.Damage, ThrowRules.ThrownWeapon(sword, throwable: true, 150, 0, 5).Damage, "a throwable weapon keeps its own");

        var rock = ThrowRules.ThrownWeapon(null, throwable: true, weight: 500, aptitude: 0, range: 3);
        Assert.AreEqual((WeaponKind.ThrownItem, 3, ThrowRules.ItemDamage(500), false), (rock.Kind, rock.Range, rock.Damage, rock.Uber));
        var armour = new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor(), Uber = true };
        Assert.AreEqual((WeaponKind.ThrownItem, true), (ThrowRules.ThrownWeapon(armour, false, 500, 0, 3) is var plate ? plate.Kind : default, plate.Uber));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.ThrownWeapon(null, false, 500, 0, -1));
    }
}
