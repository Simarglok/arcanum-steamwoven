using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Combat.CombatFixtures;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-COMBAT-007: armour and weapon wear in combat (ledger "Combat wear").</summary>
[TestClass]
public sealed class CombatWearRulesTests
{
    [TestMethod]
    public void ArmorSlotByHitLocation()
    {
        Assert.AreEqual(EquipmentSlot.Helmet, CombatWearRules.ArmorSlot(HitLocation.Head));
        Assert.AreEqual(EquipmentSlot.Gauntlets, CombatWearRules.ArmorSlot(HitLocation.Arm));
        Assert.AreEqual(EquipmentSlot.Boots, CombatWearRules.ArmorSlot(HitLocation.Leg));
        Assert.AreEqual(EquipmentSlot.Armor, CombatWearRules.ArmorSlot(HitLocation.Torso));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatWearRules.ArmorSlot((HitLocation)4));
    }

    [TestMethod]
    public void WearByMaterialForAWeaponAndForBareHands()
    {
        var expected = new Dictionary<ObjectMaterial, (int Weapon, int Hands)>
        {
            [ObjectMaterial.Stone] = (4, 4), [ObjectMaterial.Brick] = (3, 3), [ObjectMaterial.Wood] = (2, 2), [ObjectMaterial.Plant] = (1, 1),
            [ObjectMaterial.Flesh] = (0, 0), [ObjectMaterial.Metal] = (5, 5), [ObjectMaterial.Glass] = (0, 5), [ObjectMaterial.Cloth] = (0, 0),
            [ObjectMaterial.Liquid] = (0, 0), [ObjectMaterial.Paper] = (0, 0), [ObjectMaterial.Gas] = (0, 0), [ObjectMaterial.Force] = (0, 0),
            [ObjectMaterial.Fire] = (5, 5), [ObjectMaterial.Powder] = (0, 0),
        };
        Assert.AreEqual(Enum.GetValues<ObjectMaterial>().Length, expected.Count, "every material");
        foreach (var (material, (weapon, hands)) in expected)
        {
            Assert.AreEqual(weapon, CombatWearRules.WeaponWear(material, Sword()), $"{material} weapon");
            Assert.AreEqual(hands, CombatWearRules.WeaponWear(material, CombatWeapon.Unarmed(30)), $"{material} hands");
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatWearRules.WeaponWear((ObjectMaterial)14, Sword()));
        Assert.ThrowsExactly<ArgumentException>(() => CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { Kind = WeaponKind.ThrownItem }));
    }

    [TestMethod]
    public void ComplexityLowersAWeaponsWearInBothDirections()
    {
        // wear − trunc(wear × min(|c|, 100) / 100): the original divides by −100 and adds.
        foreach (var (complexity, wear) in new[] { (0, 4), (24, 4), (25, 3), (-25, 3), (49, 3), (50, 2), (-50, 2), (74, 2), (75, 1), (99, 1),
                     (100, 0), (-100, 0), (1000, 0), (-100_000, 0) })
            Assert.AreEqual(wear, CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { MagickTechComplexity = complexity }), $"c {complexity}");
        Assert.AreEqual(4, CombatWearRules.WeaponWear(ObjectMaterial.Metal, Sword() with { MagickTechComplexity = 20 }));
        Assert.AreEqual(1, CombatWearRules.WeaponWear(ObjectMaterial.Plant, Sword() with { MagickTechComplexity = 99 }));
        // Bare hands ignore complexity and subtype.
        Assert.AreEqual(4, CombatWearRules.WeaponWear(ObjectMaterial.Stone, CombatWeapon.Unarmed() with { MagickTechComplexity = 100, ArtSubtype = 4 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { MagickTechComplexity = 100_001 }));
    }

    [TestMethod]
    public void ArtSubtypesFourFiveAndThirteenWearLess()
    {
        for (var subtype = 0; subtype <= CombatWearRules.MaxArtSubtype; subtype++)
        {
            var expected = subtype switch { 4 => 3, 5 or 13 => 4, _ => 5 };
            Assert.AreEqual(expected, CombatWearRules.WeaponWear(ObjectMaterial.Metal, Sword() with { ArtSubtype = subtype }), $"subtype {subtype}");
        }
        Assert.AreEqual(0, CombatWearRules.WeaponWear(ObjectMaterial.Plant, Sword() with { ArtSubtype = 4 }), "never below 0");
        Assert.AreEqual(0, CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { ArtSubtype = 4, MagickTechComplexity = 50 }), "4 − 2 − 2");
        Assert.AreEqual(1, CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { ArtSubtype = 13, MagickTechComplexity = -50 }), "4 − 2 − 1");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { ArtSubtype = 16 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatWearRules.WeaponWear(ObjectMaterial.Stone, Sword() with { ArtSubtype = -1 }));
    }

    [TestMethod]
    public void BareHandsNeverLoseTheLastHitPoint()
    {
        foreach (var (wear, current, expected) in new[] { (4, 10, 4), (4, 5, 4), (4, 4, 3), (5, 2, 1), (4, 1, 0), (4, 0, 0), (4, -7, 0), (0, 10, 0) })
            Assert.AreEqual(expected, CombatWearRules.HandsWear(wear, current), $"{wear} at {current}");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CombatWearRules.HandsWear(-1, 10));

        var facts = new LifeFacts { HitPointPoints = 10 };
        var max = LifeRules.Evaluate(LifeState.Unharmed, facts).MaxHitPoints;
        var hurt = CombatWearRules.WearHands(LifeState.Unharmed with { HitPointDamage = max - 3 }, facts, 4);
        Assert.AreEqual(2, hurt.HitPointDamage, "3 hit points left: 2 lost");
        Assert.AreEqual(max - 1, hurt.After.HitPointDamage);
        Assert.AreEqual(LifeEvents.None, hurt.Events);
        Assert.IsFalse(LifeRules.Evaluate(hurt.After, facts).IsDead);
        var last = CombatWearRules.WearHands(hurt.After, facts, 4);
        Assert.IsFalse(last.Changed, "the last hit point stays");
        Assert.AreEqual(0, last.HitPointDamage);
        var full = CombatWearRules.WearHands(LifeState.Unharmed, facts, 5);
        Assert.AreEqual(5, full.After.HitPointDamage);
    }

    [TestMethod]
    public void WearOfADamagePacket()
    {
        var hit = Packet();
        var request = Melee() with { Target = Npc() with { Material = ObjectMaterial.Stone } };

        var plain = CombatWearRules.Of(request, hit, HitLocation.Leg, ObjectMaterial.Stone, recipientIsCritter: true)!;
        Assert.AreEqual(new AttackWear(DamageRecipient.Target, true, EquipmentSlot.Boots, 0, 0, false, false, AttackerWearKind.Weapon, 4, false), plain);
        Assert.IsTrue(plain.Any);

        // Armour: fire after resistance or a damage-armour weapon (the resolver's flag) or the critical, once.
        var armor = CombatWearRules.Of(request, hit with { DamagesArmor = true, HitEffects = CriticalHitEffects.DamageArmor }, HitLocation.Head,
            ObjectMaterial.Flesh, true)!;
        Assert.AreEqual(CombatWearRules.ArmorWear, armor.ArmorDamage);
        Assert.AreEqual(EquipmentSlot.Helmet, armor.ArmorSlot);
        Assert.AreEqual(AttackerWearKind.None, armor.AttackerWear, "flesh wears nothing");
        Assert.AreEqual(10, CombatWearRules.Of(request, hit with { HitEffects = CriticalHitEffects.DamageArmor }, HitLocation.Arm, ObjectMaterial.Flesh, true)!.ArmorDamage);

        // The critical weapon effects.
        var weapon = CombatWearRules.Of(request, hit with { HitEffects = CriticalHitEffects.DamageWeapon }, HitLocation.Torso, ObjectMaterial.Flesh, true)!;
        Assert.AreEqual(CombatWearRules.CriticalWeaponDamage, weapon.WeaponDamage);
        Assert.IsFalse(weapon.DestroyWeapon);
        var destroy = CombatWearRules.Of(request, hit with { MissEffects = CriticalMissEffects.DestroyWeapon }, HitLocation.Torso, ObjectMaterial.Flesh, true)!;
        Assert.IsTrue(destroy.DestroyWeapon);
        Assert.IsFalse(destroy.ExplodeWeapon);
        var explode = CombatWearRules.Of(request, hit with { MissEffects = CriticalMissEffects.ExplodeWeapon }, HitLocation.Torso, ObjectMaterial.Flesh, true)!;
        Assert.IsTrue(explode.DestroyWeapon && explode.ExplodeWeapon);
        Assert.IsFalse(CombatWearRules.Of(request, hit with { MissEffects = CriticalMissEffects.LoseAmmo | CriticalMissEffects.DestroyAmmo },
            HitLocation.Torso, ObjectMaterial.Flesh, true)!.Any, "ammunition is not wear");

        // An object: no armour or critical item effects, the attacker's wear only while it stays whole.
        var door = CombatWearRules.Of(request, hit with { DamagesArmor = true, HitEffects = CriticalHitEffects.DamageWeapon,
            MissEffects = CriticalMissEffects.DestroyWeapon }, HitLocation.Torso, ObjectMaterial.Wood, recipientIsCritter: false)!;
        Assert.AreEqual(new AttackWear(DamageRecipient.Target, false, EquipmentSlot.Armor, 0, 0, false, false, AttackerWearKind.Weapon, 2, true), door);

        // Only damage dealt wears the attacker's side: hit points above 0, or fatigue for a critter.
        Assert.AreEqual(AttackerWearKind.None, CombatWearRules.Of(request, hit with { HitPointDamage = 0 }, HitLocation.Torso, ObjectMaterial.Stone, true)!.AttackerWear);
        Assert.AreEqual(AttackerWearKind.None, CombatWearRules.Of(request, hit with { HitPointDamage = -3 }, HitLocation.Torso, ObjectMaterial.Stone, true)!.AttackerWear);
        Assert.AreEqual(4, CombatWearRules.Of(request, hit with { HitPointDamage = 0, FatigueDamage = 2 }, HitLocation.Torso, ObjectMaterial.Stone, true)!.AttackerWearAmount);
        Assert.AreEqual(AttackerWearKind.None,
            CombatWearRules.Of(request, hit with { HitPointDamage = 0, FatigueDamage = 2 }, HitLocation.Torso, ObjectMaterial.Stone, false)!.AttackerWear);

        // Melee only: a bow, a throw or a firearm never wears.
        foreach (var skill in new[] { CombatSkill.Bow, CombatSkill.Throwing, CombatSkill.Firearms })
            Assert.AreEqual(AttackerWearKind.None, CombatWearRules.Of(request with { Skill = skill }, hit, HitLocation.Torso, ObjectMaterial.Stone, true)!.AttackerWear);
        var thrown = request with { Skill = CombatSkill.Throwing, Weapon = Sword() with { Kind = WeaponKind.ThrownItem } };
        Assert.AreEqual(AttackerWearKind.None, CombatWearRules.Of(thrown, hit, HitLocation.Torso, ObjectMaterial.Stone, true)!.AttackerWear);

        // Bare hands hurt the attacker: glass only bare-handed.
        var hands = CombatWearRules.Of(request with { Weapon = CombatWeapon.Unarmed() }, hit, HitLocation.Torso, ObjectMaterial.Glass, true)!;
        Assert.AreEqual(AttackerWearKind.Hands, hands.AttackerWear);
        Assert.AreEqual(5, hands.AttackerWearAmount);

        // A prevented packet wears nothing at all.
        foreach (var prevention in new[] { DamagePrevention.Invulnerable, DamagePrevention.NighInvulnerable, DamagePrevention.Wall })
            Assert.IsNull(CombatWearRules.Of(request, hit with { Prevented = prevention }, HitLocation.Torso, ObjectMaterial.Stone, true));
    }

    [TestMethod]
    public void TheResolverReportsTheWearOfAHitAndOfASelfHit()
    {
        // A plain hit on a stone critter wears the attacker's sword by 4 and nothing else.
        var golem = Melee() with { Target = Npc() with { Material = ObjectMaterial.Stone } };
        var hit = Run(golem, Concat(PlainHit(), new[] { (Normal, 7) }));
        Assert.AreEqual(new AttackWear(DamageRecipient.Target, true, EquipmentSlot.Armor, 0, 0, false, false, AttackerWearKind.Weapon, 4, false), hit.Wear);

        // Fire damage wears the armour at the rolled location; the location roll is the attack's own (a leg).
        var fiery = golem with { Weapon = Sword() with { Damage = DamageRanges.None with { Normal = new(5, 10), Fire = new(2, 2) } } };
        var burned = Run(fiery, Concat(Opening(80), new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100),
            (Normal, 7) }));
        Assert.AreEqual(EquipmentSlot.Boots, burned.Wear!.ArmorSlot);
        Assert.AreEqual(10, burned.Wear.ArmorDamage);
        // Fire fully resisted does not wear the armour.
        var resisted = Run(fiery with { Target = Npc() with { Resistances = new(0, 0, 0, 100) } }, Concat(PlainHit(), new[] { (Normal, 7) }));
        Assert.AreEqual(0, resisted.Damage!.Final.Fire);
        Assert.AreEqual(0, resisted.Wear!.ArmorDamage);
        // A damage-armour weapon wears it however the damage came out.
        Assert.AreEqual(10, Run(golem with { Weapon = Sword() with { DamagesArmor = true } }, Concat(PlainHit(), new[] { (Normal, 7) })).Wear!.ArmorDamage);

        // A miss wears nothing; an invulnerable target neither.
        Assert.IsNull(Run(golem, Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) })).Wear);
        Assert.IsNull(Run(golem with { Target = Npc() with { Conditions = CombatantConditions.Invulnerable, Material = ObjectMaterial.Stone } },
            Concat(PlainHit(), new[] { (Normal, 7) })).Wear);

        // A critical self-hit: the attacker took the damage, so its own armour and weapon take the critical effects, and its
        // own material decides its weapon's wear (an iron attacker: 5).
        var effects = new FixedCriticalEffects { Hit = CriticalHitEffects.DamageWeapon | CriticalHitEffects.DamageArmor, Miss = CriticalMissEffects.DestroyWeapon };
        var clumsy = golem with { Attacker = Npc() with { Material = ObjectMaterial.Metal }, AimedLocation = HitLocation.Arm };
        var self = Run(clumsy, new[]
        {
            (TechnologyFailure, 100), (MagickCancel, 100), (AttackCheck, 100), (AttackCritical, 1), (CriticalSelfHit, 50), (Normal, 5),
            (Triple, 100), (DoubleRoll, 100), (OneAndHalf, 100),
        }, effects);
        Assert.AreEqual(AttackOutcome.CriticalMiss, self.Outcome);
        Assert.AreEqual(new AttackWear(DamageRecipient.Attacker, true, EquipmentSlot.Gauntlets, 10, 10, true, false, AttackerWearKind.Weapon, 5, false),
            self.Wear);

        // An object struck with a melee weapon wears it only while it stays whole; a portal's material decides.
        var door = Run(Melee() with { TargetKind = AttackTargetKind.Portal, Target = Npc() with { Material = ObjectMaterial.Wood } },
            Concat(Opening(), new[] { (Normal, 8) }));
        Assert.AreEqual(new AttackWear(DamageRecipient.Target, false, EquipmentSlot.Armor, 0, 0, false, false, AttackerWearKind.Weapon, 2, true), door.Wear);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AttackResolver.Resolve(Melee() with { Weapon = Sword() with { ArtSubtype = 16 } }, new SeededRolls(1)));
    }

    private static AttackDamage Packet() => new(DamageRecipient.Target, DamageByType.Zero with { Normal = 7 }, DamageByType.Zero with { Normal = 7 },
        7, 0, 0, DamageMultiplier.None, false, CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None);
}
