using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;

namespace Arcanum.Session.Tests;

// ARC-UI-1121 (ledger IW25, IW29, IW41–IW43): what the loot window's doll view prints for an NPC and whether its weapon fills its slot. The NPC fields are AttackStateTests'
// invented person (strength 2, dexterity 4, no armour class of its own); every item is invented too.
[TestClass]
public sealed class DollValuesTests
{
    private static readonly LifeStatus Healthy = new(10, 10, 10, 10, false, false, true);
    private static readonly CombatArmorFlags AllSizes = CombatArmorFlags.SizeSmall | CombatArmorFlags.SizeMedium | CombatArmorFlags.SizeLarge;

    private static SessionCritterCombat Combat(IEnumerable<CombatFields>? carried = null, int[]? skills = null, int[]? resistances = null)
    {
        var build = SessionCritterCombat.From("oid:person", Npc(critterFlags: 0, skills: skills, resistances: resistances), carried);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static SessionDollRatings Values(SessionCritterCombat combat, int storedMagic = 0, Func<EquipmentSlot, bool>? identified = null) =>
        combat.DollValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven, storedMagic, identified ?? (_ => true));

    private static CombatFields Item(int type, EquipmentSlot slot, int complexity = 0, CombatWeaponFields? weapon = null, CombatArmorFields? armor = null, int coverage = 0) =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(type, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, complexity, 0, (int)slot, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28 | (uint)coverage << 14, 0),
            weapon, null, armor, null, null, null);

    private static CombatWeaponFields Weapon(int minimumStrength = 0) => new(0, CombatWeaponSkill.Melee, 0, 0,
        [new(10, 20), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 1, 0, minimumStrength, 0, 10000, 0, 0, 0, 0, 0, 0, 0);

    private static CombatFields Helmet(int ac, int magicAc, int[] resistances, int[] magicResistances, int complexity = 0) =>
        Item(7, EquipmentSlot.Helmet, complexity, armor: new CombatArmorFields(AllSizes, ac, magicAc, resistances, magicResistances, 0, 0, 0), coverage: (int)ArmorCoverage.Helmet);

    [TestMethod]
    public void ABarehandedPersonRatesItsSkillAndItsFistsAndItsBareArmour()
    {
        var combat = Combat();
        var values = Values(combat);
        // Damage: its melee effectiveness (stored points 0: level 0 under melee 5L + 25 = 25) and bare hands, 1..5 normal and fatigue (average 3).
        var attack = combat.AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(CombatSkill.Melee, attack.Skill);
        Assert.AreEqual(DollRatings.Damage(CombatWeapon.Unarmed(), attack.Combatant.DamageBonus, CombatSkill.Melee,
            AttackRules.AttackEffectiveness(CombatSkill.Melee, attack.SkillLevel, attack.Combatant.ExtraordinaryIntelligence, false, GameDifficulty.Normal, false)), values.Damage);
        // Armour: no armour class, resistances 0 except the poison bonus of constitution 3 (none: 5 × (3 − 4) is negative): a rating of 0, and nothing is too heavy.
        Assert.AreEqual(0, values.Armour);
        Assert.IsFalse(values.WeaponTooHeavy);
    }

    [TestMethod]
    public void TheMagicResistanceAndTheStoredResistancesAndTheArmourClassCountInTheArmourRating()
    {
        // Base armour class 16 (stored; the effective stat of dexterity 4 takes 6 off: 10), resistances normal 20, fire 0, electrical 0, poison 5 (stored; constitution 3 adds none), magic 10 (stored): the hand-worked 16.
        var build = SessionCritterCombat.From("oid:person", Npc(critterFlags: 0, ac: 16, resistances: [20, 0, 0, 5, 0]), null);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        Assert.AreEqual(16, Values(build.Combat!, storedMagic: 10).Armour);
        Assert.AreEqual(DollRatings.Armour(10, 20, 0, 0, 5, 0), Values(build.Combat!, storedMagic: 0).Armour);
    }

    [TestMethod]
    public void AnUnidentifiedMagickalItemShowsNoMagicAdjustmentsButAnIdentifiedOneDoes()
    {
        int[] none = [0, 0, 0, 0, 0], magic = [10, 10, 10, 10, 10];
        var helmet = Helmet(ac: 30, magicAc: 30, resistances: none, magicResistances: magic, complexity: 50);
        var plain = Helmet(ac: 30, magicAc: 0, resistances: none, magicResistances: none);
        var known = Values(Combat([helmet]), identified: _ => true).Armour;
        var unknown = Values(Combat([helmet]), identified: _ => false).Armour;
        Assert.IsTrue(known > unknown, $"identified {known} over unidentified {unknown}");
        Assert.AreEqual(Values(Combat([plain])).Armour, unknown, "an unidentified magickal item rates as one without its magic part");
        Assert.IsTrue(unknown > 0, "its stored adjustment (armour class 30) still counts");
        // A technological item (complexity below 0) always shows its adjustments, identified or not.
        var tech = Helmet(ac: 30, magicAc: 30, resistances: none, magicResistances: magic, complexity: -50);
        Assert.AreEqual(Values(Combat([tech]), identified: _ => true).Armour, Values(Combat([tech]), identified: _ => false).Armour);
    }

    [TestMethod]
    public void AWeaponNeedingMoreStrengthThanTheNpcHasFillsItsSlot()
    {
        // Strength 2: a one-handed weapon without a shield needs its stored value less 2 (0x465E90): 9 → 7 is too heavy, 4 → 2 is not.
        Assert.IsTrue(Values(Combat([Item(5, EquipmentSlot.Weapon, weapon: Weapon(9))])).WeaponTooHeavy);
        Assert.IsFalse(Values(Combat([Item(5, EquipmentSlot.Weapon, weapon: Weapon(4))])).WeaponTooHeavy);
        // No weapon in the slot: nothing to fill.
        Assert.IsFalse(Values(Combat()).WeaponTooHeavy);
    }

    [TestMethod]
    public void TheWeaponInTheSlotChangesTheDamageRatingAndAnItemTheNpcNoLongerWearsDoesNot()
    {
        var sword = Item(5, EquipmentSlot.Weapon, weapon: Weapon());
        var bare = Values(Combat()).Damage;
        var armed = Values(Combat([sword])).Damage;
        Assert.IsTrue(armed > bare, $"a 10..20 weapon rates {armed} over bare hands {bare}");
        Assert.AreEqual(bare, Combat([sword]).DollValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven, 0, _ => true, []).Damage, "a dropped weapon: bare hands again");
        Assert.IsFalse(Combat([Item(5, EquipmentSlot.Weapon, weapon: Weapon(9))]).DollValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven, 0, _ => true, []).WeaponTooHeavy,
            "and it fills nothing");
    }
}
