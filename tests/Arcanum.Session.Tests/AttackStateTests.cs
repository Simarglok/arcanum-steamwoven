using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's attack on an NPC (ARC-COMBAT-005). The NPC fields are invented in the shape of the Crash Site's small animals:
// stored hit-point adjustment −3, WP 3, ST 2, level 1 (10 hit points), animal flag, worth and pool 160.
[TestClass]
public sealed class AttackStateTests
{
    private const string Rat = "oid:rat";
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    internal static int[] Stats(int strength = 2, int dexterity = 4, int constitution = 3, int willpower = 3, int level = 1, int poison = 0,
        int gender = 1, int race = 0) =>
        [strength, dexterity, constitution, 3, 4, 9, willpower, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, level, 0, 0, 0, 5, 0, 0, poison, 20, gender, race];

    internal static CombatFields Npc(int[]? stats = null, uint critterFlags = 0x8, int hpAdjustment = -3, int hpDamage = 0, int ac = 0,
        int[]? resistances = null, int[]? skills = null, uint objectFlags = 0, uint spellFlags = 0, int worth = 160, int pool = 160,
        int type = CombatFieldReader.NpcType, uint flags2 = 0, int material = 4) => new(CombatFieldOrigin.AreaObject,
        new CombatObjectFields(type, 28359, objectFlags, spellFlags, 0, 0, ac, 0, hpAdjustment, hpDamage, material, resistances ?? [0, 0, 0, 0, 0], 0),
        null, null, null, null, null,
        new CombatCritterFields(critterFlags, flags2, stats ?? Stats(), skills ?? new int[12], new int[4], new int[25], 0, 0, 0, 1, 0, 0),
        type == CombatFieldReader.NpcType ? new CombatNpcFields(0, 19, worth, pool, 19, 0, 0, [.. Enumerable.Repeat(new CombatDamageRange(1, 3), 5)]) : null);

    private static SessionCritterCombat Combat(CombatFields? fields = null, string entity = Rat)
    {
        var build = SessionCritterCombat.From(entity, fields ?? Npc());
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner Owner(bool hero = true, bool experience = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with
        {
            Hero = hero ? HalfElf() : null, Experience = experience ? ExperienceStateTests.Rules() : null,
        });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void AnNpcsLifeFactsAndLoadedStateComeFromItsFields()
    {
        var rat = Combat(Npc(hpDamage: 4, critterFlags: 0x8 | 0x80 | 0x400));
        Assert.AreEqual((Rat, true, 1), (rat.Entity, rat.Monstrous, rat.Level));
        Assert.AreEqual(10, LifeRules.MaxHitPoints(rat.Facts, 0), "−3 + WP 3 + 2 × (ST 2 + level 1) + 4");
        Assert.AreEqual(new LifeState(4, 0, 0, LifeConditions.Blinded | LifeConditions.CrippledLegsBoth, 0), rat.Loaded);
        Assert.AreEqual(new SessionNpcExperience(Rat, 160, 160), rat.Experience);
        Assert.AreEqual((Rat, false, rat.Facts, rat.Loaded), (rat.Target.Entity, rat.Target.IsHero, rat.Target.Facts, rat.Target.Loaded));

        var undead = Combat(Npc(critterFlags: 0x4 | 0x20000000 | 0x4000000 | 0x40, objectFlags: 0x400000, spellFlags: 0x10000, flags2: 0x200));
        Assert.AreEqual(LifeTraits.Undead | LifeTraits.Mechanical | LifeTraits.FatigueImmune | LifeTraits.Paralyzed | LifeTraits.Invulnerable |
            LifeTraits.Stoned | LifeTraits.NighInvulnerable, undead.Facts.Traits);
        var human = Combat(Npc(critterFlags: 0));
        Assert.IsFalse(human.Monstrous);
        // ARC-COMBAT-007: the stored material is what the NPC defends as (a melee hit on it wears by it).
        var status = new LifeStatus(10, 10, 10, 10, false, false, true);
        Assert.AreEqual(ObjectMaterial.Flesh, human.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Material);
        Assert.AreEqual(ObjectMaterial.Stone, Combat(Npc(material: 0)).Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Material);
        // A material outside the fourteen wears nothing in the original, as flesh does.
        foreach (var odd in new[] { 14, -1, 255 })
            Assert.AreEqual(ObjectMaterial.Flesh, Combat(Npc(material: odd)).Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Material);
    }

    [TestMethod]
    public void AnNpcDefendsWithItsSkillsArmourClassResistancesAndConditions()
    {
        var status = new LifeStatus(10, 10, 10, 10, false, false, true);
        // DEX 16 caps skills at 19; dodge 5 points = 20, expert training (bits 6..7 = 2).
        var skills = new int[12];
        skills[(int)BasicCombatSkill.Dodge] = 5 | 2 << 6;
        var nimble = Combat(Npc(stats: Stats(dexterity: 16), skills: skills, ac: 4, resistances: [30, 10, 20, 40, 0], critterFlags: 0));
        var combatant = nimble.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual((19, SkillTraining.Expert), (combatant.DodgeLevel, combatant.DodgeTraining));
        Assert.AreEqual(4 + 6, combatant.ArmorClass, "base 4 + AcAdjustment (DEX 16 − 10)");
        Assert.AreEqual(new DamageResistances(30, 40, 20, 10), combatant.Resistances, "stored order normal, fire, electrical, poison");
        Assert.IsFalse(combatant.IsPc);

        var monster = Combat(Npc(resistances: [120, 0, 0, 0, 0], critterFlags: 0x8000));
        Assert.AreEqual(120, monster.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Resistances.Normal,
            "a monstrous NPC's resistance is not clamped");
        var person = Combat(Npc(resistances: [120, 0, 0, 0, 0], critterFlags: 0, stats: Stats(constitution: 10)));
        var defended = person.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).Resistances;
        Assert.AreEqual((95, 30), (defended.Normal, defended.Poison), "clamped to 95; poison gets 5 × (CN 10 − 4)");

        var stunned = new LifeState(0, 0, 0, LifeConditions.Stunned | LifeConditions.CrippledArmOne, 0);
        var asleep = Combat(Npc(critterFlags: 0x8 | 0x1000));
        var conditions = asleep.Combatant(status with { IsUnconscious = true }, stunned, CombatAnomalyProfile.Steamwoven).Conditions;
        Assert.AreEqual(CombatantConditions.Unconscious | CombatantConditions.Stunned | CombatantConditions.CrippledArmOne | CombatantConditions.Sleeping,
            conditions);
    }

    [TestMethod]
    public void FieldsThatAreNotAnNpcGiveATypedFailure()
    {
        Assert.AreEqual(SessionCritterCombatFailure.NotAnNpc, SessionCritterCombat.From(Rat, Npc(type: CombatFieldReader.PcType)).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidEntity, SessionCritterCombat.From("bad id!", Npc()).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidRaceOrGender, SessionCritterCombat.From(Rat, Npc(stats: Stats(race: 11))).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidRaceOrGender, SessionCritterCombat.From(Rat, Npc(stats: Stats(gender: 2))).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidExperience, SessionCritterCombat.From(Rat, Npc(worth: -1)).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidLife, SessionCritterCombat.From(Rat, Npc(hpDamage: -1)).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.None, SessionCritterCombat.From(Rat, Npc(stats: Stats(strength: 30))).Failure,
            "a stat beyond the race's range is clamped, as a monster stores one");
        Assert.AreEqual(SessionCritterCombatFailure.InvalidCombatValues, SessionCritterCombat.From(Rat, Npc(resistances: [2_000_000, 0, 0, 0, 0])).Failure);
        var skills = new int[12]; skills[1] = int.MinValue;
        Assert.AreEqual(SessionCritterCombatFailure.InvalidCombatValues, SessionCritterCombat.From(Rat, Npc(skills: skills)).Failure);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidCombatValues, SessionCritterCombat.From(Rat, Npc(ac: -2_000_000)).Failure);
        // An item in the weapon slot that is no wearable weapon.
        var odd = new CombatFields(CombatFieldOrigin.AreaObject, new CombatObjectFields(14, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, 1004, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, 0), null, null, null,
            new CombatGenericFields(0, 0, 0), null, null);
        Assert.AreEqual(SessionCritterCombatFailure.InvalidWornItem, SessionCritterCombat.From(Rat, Npc(), [odd]).Failure);
        var carried = odd with { Item = odd.Item! with { InventoryLocation = 3 } };
        Assert.AreEqual(SessionCritterCombatFailure.None, SessionCritterCombat.From(Rat, Npc(), [carried]).Failure, "only worn items count");
    }

    [TestMethod]
    public void AnArmedCriticalHitFateMakesTheNextAttackACriticalHitAndIsUsed()
    {
        // ARC-WORLD-054 (FT6): the hero's armed critical-hit fate is read by the resolver and used by the attack.
        using var owner = Owner();
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, Arcanum.Simulation.Character.FateKind.CriticalHit);
        Assert.IsTrue(owner.QueryHeroAttack()!.Combatant.CriticalHitFateArmed);
        var tough = Combat(Npc(hpAdjustment: 5000));
        var before = owner.Version.Revision;
        var fated = owner.AttackCritter(owner.Version, new(tough));
        Assert.AreEqual((FateUse.AttackerCriticalHit, AttackOutcome.CriticalHit), (fated.Resolution!.Fate, fated.Resolution.Outcome));
        Assert.IsFalse(owner.HeroFateArmed(Arcanum.Simulation.Character.FateKind.CriticalHit));
        Assert.AreEqual(new Arcanum.Simulation.Character.FateState(0, 0), owner.QueryHeroFate());
        Assert.IsTrue(owner.Version.Revision >= before + 2, "the used fate, then the damage");
        Assert.AreEqual(FateUse.None, owner.AttackCritter(owner.Version, new(tough)).Resolution!.Fate);
    }

    [TestMethod]
    public void ACriticalSelfHitHurtsTheHeroAndPaysNothing()
    {
        using var owner = Owner();
        // A tough target, so the hero meets a critical miss before it dies.
        var tough = Combat(Npc(hpAdjustment: 5000));
        SessionAttackResult? self = null;
        for (var attempt = 0; attempt < 2000 && self is null; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(tough));
            Assert.AreEqual(GameSessionFailure.None, result.Failure);
            if (result.Recipient == DamageRecipient.Attacker) self = result;
        }
        Assert.IsNotNull(self, "a critical self-hit within the bounded run");
        Assert.AreEqual(AttackOutcome.CriticalMiss, self!.Resolution!.Outcome);
        Assert.IsNull(self.HitAward);
        Assert.IsNull(self.KillAward);
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.HitPointDamage > 0 || self.Life!.HitPointDamage == 0, "the hero took the damage");
    }

    [TestMethod]
    public void TheHeroAttacksBareHandedWithNoSkillUntilTheNpcDiesAndPaysItsExperience()
    {
        using var owner = Owner();
        var rat = Combat();
        var hero = owner.QueryHeroAttack()!;
        Assert.AreEqual((WeaponKind.Unarmed, CombatSkill.Melee, 0, SkillTraining.None, 10, 1, true),
            (hero.Weapon.Kind, hero.Skill, hero.SkillLevel, hero.Training, hero.WeaponSpeed, hero.Range, hero.Supported));
        Assert.IsTrue(hero.Combatant.IsPc);
        Assert.AreEqual(9, hero.Speed, "the half-elf's effective dexterity 9");

        var experience = owner.QueryHeroExperience()!.Experience;
        var hits = 0; SessionAttackResult? last = null;
        for (var attempt = 0; attempt < 400 && !owner.QueryNpcKilled(Rat); attempt++)
        {
            var draws = owner.QueryRandom().Draws;
            last = owner.AttackCritter(owner.Version, new(rat));
            Assert.AreEqual(GameSessionFailure.None, last.Failure);
            Assert.AreEqual(GameSessionFailure.None, last.StepFailure);
            Assert.IsTrue(owner.QueryRandom().Draws > draws, "every resolved attack rolls");
            Assert.IsTrue(last.Resolution!.Rolls.All(roll => Enum.IsDefined(roll.Purpose)));
            if (last.Life is { } life && last.Recipient == DamageRecipient.Target && life.HitPointDamage > 0) hits++;
        }
        Assert.IsTrue(owner.QueryNpcKilled(Rat), "bare hands kill a 10-hit-point animal in a bounded run");
        Assert.IsTrue(hits >= 1);
        Assert.IsTrue(last!.Life!.Events.HasFlag(LifeEvents.Died));
        Assert.IsNotNull(last.KillAward);
        Assert.AreEqual(160 / 5, last.KillAward!.Award, "a fifth of the worth (normal difficulty)");
        var record = owner.QueryLifeRecord(Rat)!.Value;
        Assert.AreEqual(LifeLimits.DeathHitPointDamage, record.State.HitPointDamage);
        Assert.IsNotNull(record.DeathTimeMs);
        var npc = owner.QueryNpcExperience(Rat)!.Value;
        Assert.AreEqual(0, npc.Worth, "a kill pays once");
        Assert.IsTrue(npc.Pool < 160, "hits drained the pool");
        Assert.AreEqual(experience + (160 - npc.Pool) + 32, owner.QueryHeroExperience()!.Experience, "pool shares and the kill went to the hero");

        var corpse = owner.AttackCritter(owner.Version, new(rat));
        Assert.AreEqual((GameSessionDisposition.NoChange, AttackRefusal.TargetDead), (corpse.Disposition, corpse.Resolution!.Refusal));
    }

    [TestMethod]
    public void AnNpcKilledInTheSessionCanBeLooted()
    {
        using var owner = Owner();
        var seed = new SessionMapInventorySeed([new(Rat, SessionHolderKind.Npc, false, false, [], ["tail"])],
            [new("tail", InventoryStateTests.Facts(SessionItemType.Generic, 5700), null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.LootItem(owner.Version, Rat, "tail", Accept).Failure);
        Assert.IsFalse(owner.QueryMapInventory(Exterior)!.Holders.Single().Dead);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Combat().Target).Disposition);
        Assert.IsTrue(owner.QueryNpcKilled(Rat));
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Holders.Single().Dead);
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, Rat, "tail", Accept).Failure);
    }

    [TestMethod]
    public void AnAttackIsRefusedBeforeAnyRollWhenItCannotRun()
    {
        using var plain = Owner(hero: false);
        Assert.AreEqual(GameSessionFailure.AttackUnavailable, plain.AttackCritter(plain.Version, new(Combat())).Failure);
        Assert.IsNull(plain.QueryHeroAttack());

        using var owner = Owner();
        var rat = Combat();
        void Refused(SessionAttack attack, GameSessionFailure failure, GameSessionVersion? version = null)
        {
            var draws = owner.QueryRandom().Draws;
            var result = owner.AttackCritter(version ?? owner.Version, attack);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(draws, owner.QueryRandom().Draws, "a refusal rolls nothing");
            Assert.AreEqual(0, owner.Version.Revision);
        }
        Refused(new(rat) { Aim = HitLocation.Torso }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { Aim = (HitLocation)9 }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { Distance = 2 }, GameSessionFailure.InvalidAttack);
        Refused(new(rat) { Distance = -1 }, GameSessionFailure.InvalidAttack);
        Refused(new(rat), GameSessionFailure.StaleVersion, new GameSessionVersion(owner.Identity, 5));
    }

    [TestMethod]
    public void AnAimedAttackKeepsItsLocationAndARangedWeaponIsNotSupported()
    {
        using var owner = Owner();
        var rat = Combat();
        var aimed = owner.AttackCritter(owner.Version, new(rat) { Aim = HitLocation.Head });
        Assert.AreEqual(GameSessionFailure.None, aimed.Failure);
        Assert.AreEqual(HitLocation.Head, aimed.Resolution!.HitLocation);
        Assert.IsTrue(aimed.Resolution.Aimed || aimed.Resolution.Outcome == AttackOutcome.CriticalMiss);

        var bow = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Bow, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 10, Range = 10 },
        };
        var equipped = owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, bow), (_, _) => true, Accept);
        Assert.AreEqual(GameSessionFailure.None, equipped.Failure);
        Assert.IsFalse(owner.QueryHeroAttack()!.Supported);
        Assert.AreEqual(GameSessionFailure.AttackUnsupported, owner.AttackCritter(owner.Version, new(rat)).Failure);
    }

    [TestMethod]
    public void WithoutExperienceDataTheAttackStillDealsDamage()
    {
        using var owner = Owner(experience: false);
        var rat = Combat();
        for (var attempt = 0; attempt < 400 && !owner.QueryNpcKilled(Rat); attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(rat));
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            Assert.IsNull(result.HitAward);
            Assert.IsNull(result.KillAward);
        }
        Assert.IsTrue(owner.QueryNpcKilled(Rat));
        Assert.IsNull(owner.QueryNpcExperience(Rat));
    }
}
