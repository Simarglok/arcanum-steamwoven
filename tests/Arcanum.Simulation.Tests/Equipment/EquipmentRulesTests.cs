using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Equipment;

// The wear and worn-item rules (ARC-WORLD-028). Every item value is invented.
[TestClass]
public sealed class EquipmentRulesTests
{
    internal static EquipmentItem Sword(bool twoHanded = false, bool fixedHands = false, int complexity = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        MagickTechComplexity = complexity,
        HitPoints = 40,
        MaxHitPoints = 50,
        Weapon = new EquipmentWeapon
        {
            TwoHanded = twoHanded, HandCountFixed = fixedHands, Skill = CombatSkill.Melee,
            Damage = DamageRanges.None with { Normal = new(3, 9) }, MagicDamage = DamageByType.Zero with { Fire = 4 },
            ToHit = 5, MagicToHit = 10, SpeedFactor = 12, MagicSpeed = 6, Range = 1, MagicRange = 2, MinimumStrength = 7,
            MagicMinimumStrength = -2, CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            MagicCriticalHitChance = 8, MagicCriticalHitEffect = 6, MagicCriticalMissChance = -4, MagicCriticalMissEffect = 2,
        },
    };

    internal static EquipmentItem Armor(ArmorCoverage coverage = ArmorCoverage.Torso, int ac = 10, int complexity = 0,
        ArmorSizes sizes = ArmorSizes.Medium, bool maleOnly = false, bool femaleOnly = false, int normal = 0) => new()
    {
        Kind = EquipmentItemKind.Armor,
        MagickTechComplexity = complexity,
        Armor = new EquipmentArmor
        {
            Coverage = coverage, Sizes = sizes, MaleOnly = maleOnly, FemaleOnly = femaleOnly, AcAdjustment = ac, MagicAcAdjustment = 20,
            Resistances = new(normal, 5, 0, 0, 0), MagicResistances = new(10, 0, 0, 0, 30), UnarmedBonusDamage = 12,
        },
    };

    private static readonly EquipmentItem Torch = new() { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true, Light = EquipmentLight.Medium };
    private static readonly EquipmentWearer Human = new() { Race = CharacterRace.Human };
    private static readonly Dictionary<EquipmentSlot, EquipmentItem> Nothing = [];

    [TestMethod]
    public void ItemsBelongInTheSlotOfTheirTypeAndCoverage()
    {
        Assert.AreEqual(EquipmentSlot.Weapon, EquipmentRules.WearSlot(Sword()));
        var expected = new Dictionary<ArmorCoverage, EquipmentSlot>
        {
            [ArmorCoverage.Torso] = EquipmentSlot.Armor, [ArmorCoverage.Shield] = EquipmentSlot.Shield,
            [ArmorCoverage.Helmet] = EquipmentSlot.Helmet, [ArmorCoverage.Gauntlets] = EquipmentSlot.Gauntlets,
            [ArmorCoverage.Boots] = EquipmentSlot.Boots, [ArmorCoverage.Ring] = EquipmentSlot.Ring1,
            [ArmorCoverage.Medallion] = EquipmentSlot.Medallion,
        };
        foreach (var (coverage, slot) in expected) Assert.AreEqual(slot, EquipmentRules.WearSlot(Armor(coverage)), coverage.ToString());
        Assert.AreEqual(EquipmentSlot.Shield, EquipmentRules.WearSlot(Torch));
        Assert.IsNull(EquipmentRules.WearSlot(Torch with { UsesShieldLocation = false }), "an ordinary generic item is not worn");
    }

    [TestMethod]
    public void TheRaceTableGivesOneArmourSize()
    {
        var small = new[] { CharacterRace.Dwarf, CharacterRace.Gnome, CharacterRace.Halfling };
        var large = new[] { CharacterRace.HalfOgre, CharacterRace.Ogre };
        foreach (var race in Enum.GetValues<CharacterRace>())
            Assert.AreEqual(small.Contains(race) ? ArmorSizes.Small : large.Contains(race) ? ArmorSizes.Large : ArmorSizes.Medium,
                EquipmentRules.ArmorSize(race), race.ToString());
    }

    [TestMethod]
    public void TheWearCheckRefusesInTheSourceOrder()
    {
        // Transformed first, even for a broken magickal item in the wrong slot.
        var everything = Sword(complexity: 50) with { Broken = true };
        var allergic = Human with { MagickAllergy = true };
        Assert.AreEqual(EquipmentRefusal.NotWieldable, EquipmentRules.CheckWear(everything, EquipmentSlot.Helmet, allergic with { Transformed = true }, Nothing));
        Assert.AreEqual(EquipmentRefusal.WieldMagicItems, EquipmentRules.CheckWear(everything, EquipmentSlot.Helmet, allergic, Nothing));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Sword(complexity: -50), EquipmentSlot.Weapon, allergic, Nothing),
            "the allergy refuses magickal items only");
        Assert.AreEqual(EquipmentRefusal.Broken, EquipmentRules.CheckWear(everything, EquipmentSlot.Helmet, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.WrongType, EquipmentRules.CheckWear(Sword(), EquipmentSlot.Helmet, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Sword(), EquipmentSlot.Weapon, Human, Nothing));
    }

    [TestMethod]
    public void EitherRingSlotTakesARing()
    {
        var ring = Armor(ArmorCoverage.Ring);
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(ring, EquipmentSlot.Ring1, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(ring, EquipmentSlot.Ring2, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.WrongType, EquipmentRules.CheckWear(ring, EquipmentSlot.Medallion, Human, Nothing));
    }

    [TestMethod]
    public void AFixedTwoHandedWeaponNeedsBothHands()
    {
        var shield = new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Shield] = Armor(ArmorCoverage.Shield) };
        var oneArm = Human with { CrippledArmOne = true };
        var bothArms = Human with { CrippledArmsBoth = true };
        var great = Sword(twoHanded: true, fixedHands: true);
        Assert.AreEqual(EquipmentRefusal.NoFreeHand, EquipmentRules.CheckWear(great, EquipmentSlot.Weapon, oneArm, shield));
        Assert.AreEqual(EquipmentRefusal.Crippled, EquipmentRules.CheckWear(great, EquipmentSlot.Weapon, oneArm, Nothing));
        Assert.AreEqual(EquipmentRefusal.Crippled, EquipmentRules.CheckWear(great, EquipmentSlot.Weapon, bothArms, Nothing));
        // Without the fixed hand count a two-handed weapon is like any other: only both crippled arms refuse it.
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Sword(twoHanded: true), EquipmentSlot.Weapon, oneArm, shield));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Sword(), EquipmentSlot.Weapon, oneArm, Nothing));
        Assert.AreEqual(EquipmentRefusal.Crippled, EquipmentRules.CheckWear(Sword(), EquipmentSlot.Weapon, bothArms, Nothing));
    }

    [TestMethod]
    public void AShieldNeedsAFreeAndHealthyArm()
    {
        var great = new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword(twoHanded: true, fixedHands: true) };
        var shield = Armor(ArmorCoverage.Shield);
        Assert.AreEqual(EquipmentRefusal.NoFreeHand, EquipmentRules.CheckWear(shield, EquipmentSlot.Shield, Human, great));
        Assert.AreEqual(EquipmentRefusal.NoFreeHand, EquipmentRules.CheckWear(Torch, EquipmentSlot.Shield, Human, great));
        // The original refuses a shield with one crippled arm too (CE reads only "both arms").
        Assert.AreEqual(EquipmentRefusal.Crippled, EquipmentRules.CheckWear(shield, EquipmentSlot.Shield, Human with { CrippledArmOne = true }, Nothing));
        Assert.AreEqual(EquipmentRefusal.Crippled, EquipmentRules.CheckWear(Torch, EquipmentSlot.Shield, Human with { CrippledArmsBoth = true }, Nothing));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(shield, EquipmentSlot.Shield, Human,
            new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword(twoHanded: true) }));
    }

    [TestMethod]
    public void BodyArmourChecksSizeGenderAndUniqueArt()
    {
        var dwarf = new EquipmentWearer { Race = CharacterRace.Dwarf };
        Assert.AreEqual(EquipmentRefusal.WrongSize, EquipmentRules.CheckWear(Armor(), EquipmentSlot.Armor, dwarf, Nothing));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Armor(sizes: ArmorSizes.Small | ArmorSizes.Medium), EquipmentSlot.Armor, dwarf, Nothing));
        var woman = Human with { Gender = CharacterGender.Female };
        Assert.AreEqual(EquipmentRefusal.WrongGender, EquipmentRules.CheckWear(Armor(maleOnly: true), EquipmentSlot.Armor, woman, Nothing));
        Assert.AreEqual(EquipmentRefusal.WrongGender, EquipmentRules.CheckWear(Armor(femaleOnly: true), EquipmentSlot.Armor, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.WrongGender, EquipmentRules.CheckWear(Armor(maleOnly: true, femaleOnly: true), EquipmentSlot.Armor, woman, Nothing),
            "male-only is read first");
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Armor(maleOnly: true, femaleOnly: true), EquipmentSlot.Armor, Human, Nothing));
        Assert.AreEqual(EquipmentRefusal.NotWieldable, EquipmentRules.CheckWear(Armor(), EquipmentSlot.Armor, Human with { UniqueNpcArt = true }, Nothing));
        Assert.AreEqual(EquipmentRefusal.WrongSize, EquipmentRules.CheckWear(Armor(), EquipmentSlot.Armor, dwarf with { UniqueNpcArt = true }, Nothing));
        // Helmets, boots and other armour are not sized.
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckWear(Armor(ArmorCoverage.Helmet, sizes: ArmorSizes.None), EquipmentSlot.Helmet, dwarf, Nothing));
    }

    [TestMethod]
    public void TheArtLookupAndTheNoDropCheck()
    {
        Assert.IsTrue(EquipmentRules.NeedsArtCheck(Sword(), EquipmentSlot.Weapon));
        Assert.IsTrue(EquipmentRules.NeedsArtCheck(Armor(), EquipmentSlot.Armor));
        Assert.IsTrue(EquipmentRules.NeedsArtCheck(Armor(ArmorCoverage.Shield), EquipmentSlot.Shield));
        Assert.IsFalse(EquipmentRules.NeedsArtCheck(Torch, EquipmentSlot.Shield));
        Assert.IsFalse(EquipmentRules.NeedsArtCheck(Armor(ArmorCoverage.Helmet), EquipmentSlot.Helmet));
        Assert.AreEqual(EquipmentRefusal.None, EquipmentRules.CheckRemove(Sword()));
        Assert.AreEqual(EquipmentRefusal.NotDroppable, EquipmentRules.CheckRemove(Sword() with { NoDrop = true }));
    }

    [TestMethod]
    public void EffectivePowerIsClampedOnTheItemsSide()
    {
        Assert.AreEqual(40, EquipmentRules.EffectivePower(50, 30));
        Assert.AreEqual(50, EquipmentRules.EffectivePower(50, 100));
        Assert.AreEqual(0, EquipmentRules.EffectivePower(50, -60), "a technologist draws nothing from magick");
        Assert.AreEqual(-40, EquipmentRules.EffectivePower(-50, -30));
        Assert.AreEqual(-50, EquipmentRules.EffectivePower(-50, -100));
        Assert.AreEqual(0, EquipmentRules.EffectivePower(-50, 60), "a mage draws nothing from technology");
        Assert.AreEqual(0, EquipmentRules.EffectivePower(0, 80));
        Assert.AreEqual(-1, EquipmentRules.EffectivePower(-1, -2), "(-1 - 2) / 2 truncates toward zero");
        Assert.AreEqual(1, EquipmentRules.EffectivePower(3, 0));

        Assert.AreEqual(100, EquipmentRules.EffectivePowerRatio(0, -100));
        Assert.AreEqual(80, EquipmentRules.EffectivePowerRatio(50, 30));
        Assert.AreEqual(80, EquipmentRules.EffectivePowerRatio(-50, -30));

        Assert.AreEqual(16, EquipmentRules.AdjustMagic(20, 50, 30), "magickal: value × power / complexity");
        Assert.AreEqual(4, EquipmentRules.AdjustMagic(20, -50, -30), "technological: value − value × power / complexity");
        Assert.AreEqual(0, EquipmentRules.AdjustMagic(20, -50, -100));
        Assert.AreEqual(20, EquipmentRules.AdjustMagic(20, -50, 100));
        Assert.AreEqual(-7, EquipmentRules.AdjustMagic(-7, 0, 40), "neutral: unchanged");
        Assert.AreEqual(-1, EquipmentRules.AdjustMagic(-5, 3, 0), "power 1: −5 × 1 / 3 truncates toward zero");
    }

    [TestMethod]
    public void WornArmourAddsArmourClassAndResistances()
    {
        var worn = new[] { Armor(ac: 10, complexity: 50, normal: 7), Armor(ArmorCoverage.Helmet, ac: 3), Sword() };
        // 10 + 20 × 40/50 = 26, 3 + 20 = 23; the sword adds nothing.
        Assert.AreEqual(49, EquipmentRules.ArmorClassAdjustment(worn, 30));
        var resistances = EquipmentRules.ResistanceAdjustments(worn, 30);
        Assert.AreEqual(new EquipmentResistances(7 + 8 + 10, 10, 0, 0, 24 + 30), resistances);
        Assert.AreEqual(0, EquipmentRules.ArmorClassAdjustment([], 0));
    }

    [TestMethod]
    public void LightShrinksWithEffectivePowerAndTheLargestWins()
    {
        var lamp = new EquipmentItem { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true, Light = EquipmentLight.ExtraLarge, MagickTechComplexity = -40 };
        Assert.AreEqual(LightEmission.ExtraLarge, EquipmentRules.Light([lamp], -100));
        Assert.AreEqual(LightEmission.Large, EquipmentRules.Light([lamp], -20), "power -30, 75 %");
        Assert.AreEqual(LightEmission.Small, EquipmentRules.Light([lamp], 20), "power -10, 25 %: three sizes smaller");
        Assert.AreEqual(LightEmission.None, EquipmentRules.Light([lamp with { Light = EquipmentLight.Small }], 60), "a small light goes out");
        Assert.AreEqual(LightEmission.Medium, EquipmentRules.Light([Torch, lamp with { Light = EquipmentLight.Small }], -100));
        Assert.AreEqual(LightEmission.None, EquipmentRules.Light([Sword()], 0));
    }

    [TestMethod]
    public void MinimumStrengthFollowsTheShieldRule()
    {
        // 7 − 2 magic adjustment (neutral item).
        Assert.AreEqual(3, EquipmentRules.MinimumStrength(Sword(), 0, false), "one-handed without a shield: −2");
        Assert.AreEqual(5, EquipmentRules.MinimumStrength(Sword(), 0, true));
        Assert.AreEqual(9, EquipmentRules.MinimumStrength(Sword(twoHanded: true), 0, true), "two-handed next to a shield: +4");
        Assert.AreEqual(5, EquipmentRules.MinimumStrength(Sword(twoHanded: true), 0, false));
        Assert.AreEqual(5, EquipmentRules.MinimumStrength(Sword(fixedHands: true), 0, false), "a fixed hand count ignores the rule");
    }

    [TestMethod]
    public void WeaponSpeedAndAttackCost()
    {
        Assert.AreEqual(10, EquipmentRules.WeaponSpeed(null, 0, SkillTraining.Master), "bare hands");
        Assert.AreEqual(18, EquipmentRules.WeaponSpeed(Sword(), 0, SkillTraining.None));
        Assert.AreEqual(23, EquipmentRules.WeaponSpeed(Sword(), 0, SkillTraining.Apprentice));
        Assert.AreEqual(14, EquipmentRules.WeaponSpeed(Sword(complexity: 50), -10, SkillTraining.None), "12 + magic speed 6 × 20/50 = 2");
        var combat = EquipmentRules.Evaluate(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword() }, 0, SkillTraining.Apprentice);
        Assert.AreEqual((23, 2), (combat.WeaponSpeed, combat.AttackCost));
        Assert.AreEqual((10, 5), (EquipmentRules.Evaluate(Nothing, 0, SkillTraining.None).WeaponSpeed, EquipmentRules.Evaluate(Nothing, 0, SkillTraining.None).AttackCost));
    }

    [TestMethod]
    public void BareHandsTakeTheGauntletBonus()
    {
        Assert.AreEqual(CombatWeapon.Unarmed(), EquipmentRules.Weapon(Nothing, 0));
        var gauntlets = new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Gauntlets] = Armor(ArmorCoverage.Gauntlets) };
        Assert.AreEqual(CombatWeapon.Unarmed(12), EquipmentRules.Weapon(gauntlets, 0));
        var combat = EquipmentRules.Evaluate(gauntlets, 0, SkillTraining.None);
        Assert.AreEqual((CombatSkill.Melee, new DamageRange(1, 17)), (combat.Skill, combat.Weapon.Damage.Normal));
    }

    [TestMethod]
    public void AWieldedWeaponHasItsValuesForThisWielder()
    {
        var worn = new Dictionary<EquipmentSlot, EquipmentItem>
        {
            [EquipmentSlot.Weapon] = Sword(complexity: 50),
            [EquipmentSlot.Shield] = Armor(ArmorCoverage.Shield),
            [EquipmentSlot.Gauntlets] = Armor(ArmorCoverage.Gauntlets),
        };
        var weapon = EquipmentRules.Weapon(worn, 30);
        Assert.AreEqual(WeaponKind.Weapon, weapon.Kind);
        Assert.AreEqual(new DamageRange(3, 9), weapon.Damage.Normal);
        Assert.AreEqual(DamageByType.Zero with { Fire = 3 }, weapon.MagicDamageAdjustment, "4 × 40/50");
        Assert.AreEqual((5, 8), (weapon.ToHitBonus, weapon.MagicToHitAdjustment));
        Assert.AreEqual(2, weapon.Range, "1 + 2 × 40/50 = 1 + 1");
        var short_ = new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword() with { Weapon = Sword().Weapon! with { MagicRange = -5 } } };
        Assert.AreEqual(0, EquipmentRules.Weapon(short_, 0).Range, "1 − 5 reaches nothing: 0");
        Assert.AreEqual(6, weapon.MinimumStrength, "7 + (−2 × 40/50 = −1), one-handed with a shield");
        Assert.AreEqual((6, -3, 4, 1), (weapon.MagicCriticalHitChance, weapon.MagicCriticalMissChance, weapon.MagicCriticalEffectChance,
            weapon.MagicCriticalMissEffectChance));
        Assert.AreEqual((CriticalHitChart.Cutting, CriticalMissChart.Bladed, 50), (weapon.CriticalHitChart, weapon.CriticalMissChart, weapon.MagickTechComplexity));
        Assert.AreEqual((40, 50), (weapon.Hp, weapon.MaxHp));
        Assert.IsFalse(weapon.Uber);
        Assert.IsTrue(EquipmentRules.Weapon(new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword() with { Uber = true } }, 0).Uber);

        var combat = EquipmentRules.Evaluate(worn, 30, SkillTraining.None);
        Assert.AreEqual(weapon, combat.Weapon);
        Assert.AreEqual(10 + 20 + 10 + 20, combat.ArmorClassAdjustment, "the two armour items, neutral");
    }

    [TestMethod]
    public void ItemsAreValidatedForShapeAndBounds()
    {
        Assert.IsTrue(EquipmentRules.IsValid(Sword()));
        Assert.IsTrue(EquipmentRules.IsValid(Armor()));
        Assert.IsTrue(EquipmentRules.IsValid(Torch));
        Assert.IsFalse(EquipmentRules.IsValid(null));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { Weapon = null }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { Armor = Armor().Armor }));
        Assert.IsFalse(EquipmentRules.IsValid(Armor() with { UsesShieldLocation = true }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { HitPoints = 51 }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { MagickTechComplexity = 1001 }));
        Assert.IsFalse(EquipmentRules.IsValid(Torch with { Light = (EquipmentLight)16 }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { Weapon = Sword().Weapon! with { SpeedFactor = 496 } }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { Weapon = Sword().Weapon! with { AmmoType = -1 } }));
        Assert.IsFalse(EquipmentRules.IsValid(Armor() with { Armor = Armor().Armor! with { Sizes = (ArmorSizes)8 } }));
        // The attack resolver's bounds: no negative damage or unarmed bonus, a condition up to 100 000.
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { Weapon = Sword().Weapon! with { Damage = DamageRanges.None with { Fire = new(-1, 2) } } }));
        Assert.IsFalse(EquipmentRules.IsValid(Armor() with { Armor = Armor().Armor! with { UnarmedBonusDamage = -1 } }));
        Assert.IsTrue(EquipmentRules.IsValid(Sword() with { HitPoints = 100_000, MaxHitPoints = 100_000 }));
        Assert.IsFalse(EquipmentRules.IsValid(Sword() with { MaxHitPoints = 100_001 }));
        Assert.ThrowsExactly<ArgumentException>(() => EquipmentRules.Evaluate(
            new Dictionary<EquipmentSlot, EquipmentItem> { [EquipmentSlot.Weapon] = Sword() with { Weapon = null } }, 0, SkillTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EquipmentRules.Evaluate(Nothing, 101, SkillTraining.None));
    }
}
