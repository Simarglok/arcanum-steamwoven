using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

// ARC-UI-1267 (ledger IW25, IW41–IW43): what the hero's own inventory window prints under its doll and whether its weapon fills its slot, read from the session's hero.
// The hero is HeroStateTests' invented half-elf (strength 8 and the rest of its stats 8); every item is invented.
[TestClass]
public sealed class HeroDollRatingsTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static EquipmentItem Sword(int minimumStrength = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(10, 20) }, SpeedFactor = 10, Range = 1, MinimumStrength = minimumStrength },
    };

    private static EquipmentItem Armour(int complexity, int ac, int magicAc, EquipmentResistances? magic = null) => new()
    {
        Kind = EquipmentItemKind.Armor, MagickTechComplexity = complexity, HitPoints = 20, MaxHitPoints = 20,
        Armor = new EquipmentArmor
        {
            Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Small | ArmorSizes.Medium | ArmorSizes.Large, AcAdjustment = ac, MagicAcAdjustment = magicAc,
            MagicResistances = magic ?? EquipmentResistances.None,
        },
    };

    private static void Wear(GameSessionOwner owner, string entity, EquipmentSlot slot, SessionItemKind kind, EquipmentItem item) =>
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, entity, slot, new(kind, [], false, item), (_, _) => true, Accept).Failure);

    [TestMethod]
    public void ABareHandedHeroRatesItsFistsAndItsBareArmourAndNothingIsTooHeavy()
    {
        using var owner = CreateWith(HalfElf());
        var attack = owner.QueryHeroAttack()!;
        var values = owner.QueryHeroDollRatings(_ => true)!;
        var effectiveness = AttackRules.AttackEffectiveness(attack.Skill, attack.SkillLevel, attack.Combatant.ExtraordinaryIntelligence, true, GameDifficulty.Normal, false);
        Assert.AreEqual(DollRatings.Damage(attack.Weapon, attack.Combatant.DamageBonus, attack.Skill, effectiveness), values.Damage);
        Assert.IsTrue(values.Damage > 0);
        Assert.IsFalse(values.WeaponTooHeavy);
    }

    [TestMethod]
    public void WithoutAHeroThereAreNoRatings()
    {
        var result = GameSessionOwner.Create(SessionFixtures.Descriptor());
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        using var owner = result.Owner!;
        Assert.IsNull(owner.QueryHeroDollRatings(_ => true));
    }

    [TestMethod]
    public void TheDamageRatingReadsTheSkillEffectivenessUnderTheDifficulty()
    {
        using var owner = CreateWith(HalfElf());
        var normal = owner.QueryHeroDollRatings(_ => true)!.Damage;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectDifficulty(GameDifficulty.Easy));
        var easy = owner.QueryHeroDollRatings(_ => true)!.Damage;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectDifficulty(GameDifficulty.Hard));
        var hard = owner.QueryHeroDollRatings(_ => true)!.Damage;
        var attack = owner.QueryHeroAttack()!;
        foreach (var (difficulty, rating) in new[] { (GameDifficulty.Easy, easy), (GameDifficulty.Normal, normal), (GameDifficulty.Hard, hard) })
            Assert.AreEqual(DollRatings.Damage(attack.Weapon, attack.Combatant.DamageBonus, attack.Skill,
                AttackRules.AttackEffectiveness(attack.Skill, attack.SkillLevel, attack.Combatant.ExtraordinaryIntelligence, true, difficulty, false)), rating, difficulty.ToString());
        Assert.IsTrue(easy > hard, $"an easier game rates {easy} over a harder one {hard}");
    }

    [TestMethod]
    public void TheWieldedWeaponChangesTheDamageRating()
    {
        using var owner = CreateWith(HalfElf());
        var bare = owner.QueryHeroDollRatings(_ => true)!.Damage;
        Wear(owner, "kit:1", EquipmentSlot.Weapon, Kit[0].Kind, Sword());
        Assert.IsTrue(owner.QueryHeroDollRatings(_ => true)!.Damage > bare, "a 10..20 weapon rates over bare hands");
    }

    [TestMethod]
    public void AnUnidentifiedMagickalArmourShowsNoMagicAdjustmentsButAnIdentifiedOneDoes()
    {
        using var owner = CreateWith(HalfElf());
        var bare = owner.QueryHeroDollRatings(_ => true)!.Armour;
        var magic = new EquipmentResistances(10, 10, 10, 10, 10);
        Wear(owner, "kit:2", EquipmentSlot.Armor, Kit[1].Kind, Armour(complexity: 100, ac: 30, magicAc: 40, magic));
        var known = owner.QueryHeroDollRatings(_ => true)!.Armour;
        var unknown = owner.QueryHeroDollRatings(_ => false)!.Armour;
        Assert.IsTrue(known > unknown, $"identified {known} over unidentified {unknown}");
        Assert.IsTrue(unknown > bare, "its stored adjustment (armour class 30) still counts");
        // The identification is asked by wear slot: another slot's mark does not help.
        Assert.AreEqual(unknown, owner.QueryHeroDollRatings(slot => slot != EquipmentSlot.Armor)!.Armour);
        // A neutral item (complexity 0) shows what it has whether identified or not.
        using var neutral = CreateWith(HalfElf());
        Wear(neutral, "kit:2", EquipmentSlot.Armor, Kit[1].Kind, Armour(complexity: 0, ac: 30, magicAc: 40, magic));
        Assert.AreEqual(neutral.QueryHeroDollRatings(_ => true)!.Armour, neutral.QueryHeroDollRatings(_ => false)!.Armour);
    }

    [TestMethod]
    public void AWeaponNeedingMoreStrengthThanTheHeroHasFillsItsSlot()
    {
        // The hero's strength is 8 (HeroStateTests' stats): a one-handed weapon without a shield needs its stored value less 2 (0x465E90): 20 → 18 is too heavy, 9 → 7 is not.
        using var heavy = CreateWith(HalfElf());
        Wear(heavy, "kit:1", EquipmentSlot.Weapon, Kit[0].Kind, Sword(minimumStrength: 20));
        Assert.IsTrue(heavy.QueryHeroDollRatings(_ => true)!.WeaponTooHeavy);
        using var light = CreateWith(HalfElf());
        Wear(light, "kit:1", EquipmentSlot.Weapon, Kit[0].Kind, Sword(minimumStrength: 9));
        Assert.IsFalse(light.QueryHeroDollRatings(_ => true)!.WeaponTooHeavy);
        // No weapon in the slot: nothing to fill.
        using var none = CreateWith(HalfElf());
        Assert.IsFalse(none.QueryHeroDollRatings(_ => true)!.WeaponTooHeavy);
    }
}
