using Arcanum.Data.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-092: an NPC's melee attack (the AI's hit step) on the hero or another NPC. The NPC fields are AttackStateTests'
// invented small animal (10 hit points, animal flag, a 1..3 monster table, worth and pool 160).
[TestClass]
public sealed class NpcAttackStateTests
{
    private const string Rat = "oid:rat", Other = "oid:other";
    private static readonly LifeStatus Healthy = new(10, 10, 10, 10, false, false, true);

    private static SessionCritterCombat Combat(CombatFields? fields = null, string entity = Rat, IEnumerable<CombatFields>? carried = null)
    {
        var build = SessionCritterCombat.From(entity, fields ?? Npc(), carried);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HalfElf() : null, Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    // A worn item in its own wear slot: a weapon, or armour whose inventory art names its coverage (bits 14..16 of an item art).
    private static CombatFields Worn(int type, EquipmentSlot slot, CombatWeaponFields? weapon = null, CombatArmorFields? armor = null, int coverage = 0) =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(type, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, (int)slot, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28 | (uint)coverage << 14, 0),
            weapon, null, armor, null, null, null);

    private static CombatWeaponFields Weapon(CombatWeaponSkill skill, int range = 1) => new(0, skill, 0, 0,
        [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, range, 0, 0, 0, 10000, 0, 0, 0, 0, 0, 0, 0);

    [TestMethod]
    public void AMonstrousNpcWithoutAWeaponBitesWithItsOwnTableAndAnyOtherFightsBareHanded()
    {
        // 0x465F70: no weapon and the monster test 0x45F730 → the NPC's damage table (field 0x12F); melee 20 for a monstrous critter.
        var rat = Combat().AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual((WeaponKind.Unarmed, CombatSkill.Melee, 1, true), (rat.Weapon.Kind, rat.Skill, rat.Range, rat.Supported));
        Assert.AreEqual(new DamageRange(1, 3), rat.Weapon.Damage.Normal);
        Assert.AreEqual(new DamageRange(1, 3), rat.Weapon.Damage.Fire, "every type of the table");
        Assert.AreEqual(Math.Min(SkillRules.MonstrousMeleeLevel, SkillRules.KeyStatCap(4)), rat.SkillLevel, "melee 20, capped by DEX 4");
        Assert.IsFalse(rat.Combatant.IsPc);
        Assert.AreEqual((TurnBasedRules.UnarmedWeaponSpeed, TurnBasedRules.AttackCost(TurnBasedRules.UnarmedWeaponSpeed)), (rat.WeaponSpeed, rat.AttackCost));

        // A person: bare hands, normal and fatigue 1..5 without gauntlets; its stored melee points give its level.
        var skills = new int[12];
        skills[(int)BasicCombatSkill.Melee] = 2;
        var person = Combat(Npc(critterFlags: 0, skills: skills)).AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(CombatWeapon.Unarmed(), person.Weapon);
        Assert.AreEqual(SkillRules.Level(Skill.Melee, 2, 4, false), person.SkillLevel);

        // The gauntlets' unarmed bonus counts for a person; a monster's table wins over them.
        var gauntlets = Worn(7, EquipmentSlot.Gauntlets, armor: new CombatArmorFields(CombatArmorFlags.SizeSmall | CombatArmorFlags.SizeMedium | CombatArmorFlags.SizeLarge, 0, 0, [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], 0, 0, 30),
            coverage: (int)ArmorCoverage.Gauntlets);
        Assert.AreEqual(CombatWeapon.Unarmed(30), Combat(Npc(critterFlags: 0), carried: [gauntlets])
            .AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Weapon);
        Assert.AreEqual(new DamageRange(1, 3), Combat(carried: [gauntlets])
            .AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Weapon.Damage.Normal);

        // A wielded melee weapon is used, by a monster too; a bow is the ranged slice's.
        var dagger = Combat(carried: [Worn(5, EquipmentSlot.Weapon, Weapon(CombatWeaponSkill.Melee))])
            .AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual((WeaponKind.Weapon, new DamageRange(2, 6), true), (dagger.Weapon.Kind, dagger.Weapon.Damage.Normal, dagger.Supported));
        var bow = Combat(carried: [Worn(5, EquipmentSlot.Weapon, Weapon(CombatWeaponSkill.Bow, range: 12))])
            .AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual((CombatSkill.Bow, 12, false), (bow.Skill, bow.Range, bow.Supported));

        // A weapon the NPC no longer wears (a critical dropped or destroyed it) does not strike: the monster's table again.
        var dropped = Combat(carried: [Worn(5, EquipmentSlot.Weapon, Weapon(CombatWeaponSkill.Melee))])
            .AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven, []);
        Assert.AreEqual((WeaponKind.Unarmed, new DamageRange(1, 3)), (dropped.Weapon.Kind, dropped.Weapon.Damage.Normal));
    }

    [TestMethod]
    public void AnNpcHurtsTheHeroAndPaysNothing()
    {
        using var owner = Owner();
        var rat = Combat();
        var experience = owner.QueryHeroExperience()!.Experience;
        SessionAttackResult? hit = null;
        for (var attempt = 0; attempt < 400 && hit is null; attempt++)
        {
            var draws = owner.QueryRandom().Draws;
            var result = owner.AttackByNpc(owner.Version, new(rat));
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            Assert.IsTrue(owner.QueryRandom().Draws > draws, "every resolved attack rolls");
            Assert.IsNull(result.HitAward);
            Assert.IsNull(result.KillAward);
            if (result.Recipient == DamageRecipient.Target && result.Life is { HitPointDamage: > 0 }) hit = result;
        }
        Assert.IsNotNull(hit, "a bite lands within the bounded run");
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.HitPointDamage > 0, "the hero took the damage");
        Assert.AreEqual(experience, owner.QueryHeroExperience()!.Experience);
        Assert.AreEqual(new SessionNpcExperience(Rat, 160, 160), owner.QueryNpcExperience(Rat) ?? rat.Experience, "the attacker's pool is untouched");
    }

    [TestMethod]
    public void AnNpcVictimsPoolDrainsForNothingAndOnlyAPcLedKillerPaysTheKill()
    {
        // XP10: the pool loses the share whoever attacked, only a PC attacker gets it; XP9: the killer's PC leader gets the award,
        // else nothing and the worth stays.
        foreach (var led in new[] { false, true })
        {
            using var owner = Owner();
            if (led) Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Rat, PartyStateTests.Facts(maxFollowers: 5), false).Disposition);
            var rat = Combat();
            var victim = Combat(entity: Other);
            var experience = owner.QueryHeroExperience()!.Experience;
            SessionAttackResult? last = null;
            for (var attempt = 0; attempt < 600 && !owner.QueryNpcKilled(Other); attempt++)
            {
                last = owner.AttackByNpc(owner.Version, new(rat) { Target = victim });
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (last.Failure, last.StepFailure));
                Assert.IsNull(last.HitAward);
            }
            Assert.IsTrue(owner.QueryNpcKilled(Other), "the bites kill the victim within the bounded run");
            var after = owner.QueryNpcExperience(Other)!.Value;
            Assert.IsTrue(after.Pool < 160, "the hits drained the pool");
            if (led)
            {
                Assert.AreEqual(160 / 5, last!.KillAward!.Award);
                Assert.AreEqual(0, after.Worth);
                Assert.AreEqual(experience + 32, owner.QueryHeroExperience()!.Experience, "the kill, not the pool, went to the hero");
            }
            else
            {
                Assert.IsNull(last!.KillAward);
                Assert.AreEqual(160, after.Worth, "no PC recipient: the worth stays");
                Assert.AreEqual(experience, owner.QueryHeroExperience()!.Experience);
            }
        }
    }

    [TestMethod]
    public void TheHerosArmedCriticalMissFateTurnsAnNpcsAttackIntoACriticalMissAndIsUsed()
    {
        using var owner = Owner();
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.CriticalMiss);
        var before = owner.Version.Revision;
        var fated = owner.AttackByNpc(owner.Version, new(Combat()));
        Assert.AreEqual((FateUse.TargetCriticalMiss, AttackOutcome.CriticalMiss), (fated.Resolution!.Fate, fated.Resolution.Outcome));
        Assert.AreEqual(GameSessionDisposition.Changed, fated.Disposition);
        Assert.IsFalse(owner.HeroFateArmed(FateKind.CriticalMiss));
        Assert.IsTrue(owner.Version.Revision > before);
        Assert.AreEqual(FateUse.None, owner.AttackByNpc(owner.Version, new(Combat())).Resolution!.Fate);
    }

    [TestMethod]
    public void AnNpcAttackIsRefusedBeforeAnyRollWhenItCannotRun()
    {
        using var plain = Owner(hero: false);
        Assert.AreEqual(GameSessionFailure.AttackUnavailable, plain.AttackByNpc(plain.Version, new(Combat())).Failure);

        using var owner = Owner();
        var rat = Combat();
        void Refused(SessionNpcAttack attack, GameSessionFailure failure, GameSessionVersion? version = null)
        {
            var draws = owner.QueryRandom().Draws;
            var result = owner.AttackByNpc(version ?? owner.Version, attack);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(draws, owner.QueryRandom().Draws, "a refusal rolls nothing");
            Assert.AreEqual(0, owner.Version.Revision);
        }
        Refused(new(rat) { Target = rat }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { Distance = 2 }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { Distance = -1 }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { TargetCell = new(1 << 24, 0) }, GameSessionFailure.InvalidAttack);
        Refused(new(Combat(carried: [Worn(5, EquipmentSlot.Weapon, Weapon(CombatWeaponSkill.Bow, range: 12))])), GameSessionFailure.AttackUnsupported);
        Refused(new(rat), GameSessionFailure.StaleVersion, new GameSessionVersion(owner.Identity, 5));

        // A dead attacker is the resolver's refusal: no change, no damage.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, rat.Target).Disposition);
        var dead = owner.AttackByNpc(owner.Version, new(rat));
        Assert.AreEqual(GameSessionDisposition.NoChange, dead.Disposition);
        Assert.AreNotEqual(AttackRefusal.None, dead.Resolution!.Refusal);
    }

    [TestMethod]
    public void AnNpcsFirearmsLevelComesFromItsStoredTechSkillAsTheHerosDoes()
    {
        // ARC-WORLD-106: 0x4C65E0 reads the tech skill of a PC or an NPC alike: 4 per point, 0..20, capped by perception (stats[5] 9 → 11).
        var fields = Npc(critterFlags: 0);
        var stored = 2 | (int)SkillTraining.Expert << 6;
        var gunner = fields with { Critter = fields.Critter! with { TechSkills = [0, stored, 0, 0] } };
        var gun = Worn(5, EquipmentSlot.Weapon, Weapon(CombatWeaponSkill.Firearms, range: 10));
        var values = Combat(gunner, carried: [gun]).AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual((CombatSkill.Firearms, 8, SkillTraining.Expert, false), (values.Skill, values.SkillLevel, values.Training, values.Supported),
            "two points, expert; NPC firearms attacks are the ranged slice's");
        Assert.AreEqual(8, Combat(gunner).Chooser(LifeState.Unharmed).FirearmsLevel, "the best-items choice scores a firearm by it");
        var sharp = gunner with { Critter = gunner.Critter! with { TechSkills = [0, 5, 0, 0] } };
        Assert.AreEqual(SkillRules.KeyStatCap(9), Combat(sharp).Chooser(LifeState.Unharmed).FirearmsLevel, "20 capped by perception 9");
        Assert.AreEqual(0, Combat().Chooser(LifeState.Unharmed).FirearmsLevel, "no stored points");
        var shortList = fields with { Critter = fields.Critter! with { TechSkills = [7] } };
        Assert.AreEqual(0, Combat(shortList).Chooser(LifeState.Unharmed).FirearmsLevel, "a missing tech entry is 0");
        var unbounded = fields with { Critter = fields.Critter! with { TechSkills = [0, 2_000_000, 0, 0] } };
        Assert.AreEqual(SessionCritterCombatFailure.InvalidCombatValues, SessionCritterCombat.From(Rat, unbounded).Failure);
    }

    [TestMethod]
    public void AnNpcBuiltFromItsWornItemsWearsEachInTheSlotTheCallerNames()
    {
        // ARC-WORLD-106: the session's slot wins over the stored location (a made item's prototype stores none).
        var stored = Worn(5, EquipmentSlot.Helmet, Weapon(CombatWeaponSkill.Melee));
        var build = SessionCritterCombat.FromWorn(Rat, Npc(critterFlags: 0), [KeyValuePair.Create(EquipmentSlot.Weapon, stored)]);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        Assert.AreEqual(new DamageRange(2, 6), build.Combat!.AttackValues(Healthy, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Weapon.Damage.Normal);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidWornItem,
            SessionCritterCombat.FromWorn(Rat, Npc(), [KeyValuePair.Create(EquipmentSlot.Helmet, stored)]).Failure, "a weapon in the helmet slot");
        Assert.AreEqual(SessionCritterCombatFailure.InvalidWornItem, SessionCritterCombat.FromWorn(Rat, Npc(),
            [KeyValuePair.Create(EquipmentSlot.Weapon, stored), KeyValuePair.Create(EquipmentSlot.Weapon, stored)]).Failure, "two in one slot");
        Assert.AreEqual(SessionCritterCombatFailure.None, SessionCritterCombat.FromWorn(Rat, Npc(), []).Failure, "nothing worn");
    }
}
