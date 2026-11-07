using Arcanum.Data.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-UI-097 (ledger LB21): the attack steps feed the PC's kill and injury log. Description numbers, alignment and levels are invented; the
/// NPC is AttackStateTests' small animal with a description number 5001 (known) and 5002 (not yet met).
/// </summary>
[TestClass]
public sealed class KillLogFeedStateTests
{
    private const string Rat = "oid:rat", Other = "oid:other";
    private const int Known = 5001, Unknown = 5002;

    private static CombatFields Named(int alignment = 0, int level = 1, int known = Known, int unknown = Unknown, int hp = -3)
    {
        var stats = Stats(level: level);
        stats[19] = alignment;
        var fields = Npc(stats, hpAdjustment: hp);
        return fields with { Object = fields.Object with { Description = known }, Npc = fields.Npc! with { UnknownDescription = unknown } };
    }

    private static SessionCritterCombat Combat(CombatFields fields, string entity = Rat)
    {
        var build = SessionCritterCombat.From(entity, fields);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner Owner(bool experience = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = experience ? ExperienceStateTests.Rules() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Meet(GameSessionOwner owner, string entity) => Assert.AreEqual(GameSessionDisposition.Changed, owner.AdjustReaction(owner.Version, entity,
        new ReactionNpcFacts(50, CharacterRace.Human, false, false), owner.QueryHeroReactionFacts()!, 5).Disposition);

    [TestMethod]
    public void AnNpcsKillFactsComeFromItsFields()
    {
        var facts = Combat(Named(alignment: -40, level: 7)).KillFacts;
        Assert.AreEqual(new SessionKillFacts(Known, Unknown, -40, 0), facts, "level 7 is the Level property; aptitude 0 from no magick or tech points");
        Assert.AreEqual(1000, Combat(Named(alignment: 5000)).KillFacts.Alignment, "clamped to the stat's range");
        Assert.AreEqual(-1000, Combat(Named(alignment: -5000)).KillFacts.Alignment);
        Assert.AreEqual(0, Combat(Npc()).KillFacts.UnknownDescription, "a record without the field names nobody");
    }

    [TestMethod]
    public void TheHeroKillsAnNpcAndTheLogNamesItByTheDescriptionTheHeroKnows()
    {
        foreach (var met in new[] { false, true })
            foreach (var experience in new[] { true, false })
            {
                using var owner = Owner(experience);
                if (met) Meet(owner, Rat);
                var rat = Combat(Named(alignment: -40, level: 1));
                Assert.IsTrue(owner.QueryHeroKillLog()!.IsEmpty);
                SessionAttackResult? last = null;
                for (var attempt = 0; attempt < 400 && !owner.QueryNpcKilled(Rat); attempt++)
                {
                    last = owner.AttackCritter(owner.Version, new(rat));
                    Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (last.Failure, last.StepFailure));
                }
                Assert.IsTrue(owner.QueryNpcKilled(Rat));
                var name = met ? Known : Unknown;
                var log = owner.QueryHeroKillLog()!;
                Assert.AreEqual(1, log.Total, $"met {met}, experience {experience}");
                Assert.AreEqual(new SessionKillRecord(name, 1), log.HighestLevel);
                Assert.AreEqual(new SessionKillRecord(name, 1), log.LowestLevel);
                Assert.AreEqual(default, log.MostPositiveAlignment, "a negative alignment counts for the negative slot only");
                Assert.AreEqual(new SessionKillRecord(name, -40), log.MostNegativeAlignment);
                Assert.AreEqual((default(SessionKillRecord), default(SessionKillRecord)), (log.HighestAptitude, log.LowestAptitude), "aptitude 0 counts for neither");
                Assert.AreEqual(0, log.Injuries.Count);

                // A corpse is not killed twice: another attack records nothing.
                Assert.AreEqual(GameSessionDisposition.NoChange, owner.AttackCritter(owner.Version, new(rat)).Disposition);
                Assert.AreEqual(1, owner.QueryHeroKillLog()!.Total);
            }
    }

    [TestMethod]
    public void AFollowersKillIsTheHerosAndAStrangersIsNot()
    {
        foreach (var led in new[] { false, true })
        {
            using var owner = Owner();
            if (led) Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Rat, PartyStateTests.Facts(maxFollowers: 5), false).Disposition);
            var rat = Combat(Named());
            var victim = Combat(Named(alignment: 25, level: 3, known: 6001, unknown: 6002), Other);
            for (var attempt = 0; attempt < 600 && !owner.QueryNpcKilled(Other); attempt++)
            {
                var result = owner.AttackByNpc(owner.Version, new(rat) { Target = victim });
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            }
            Assert.IsTrue(owner.QueryNpcKilled(Other));
            var log = owner.QueryHeroKillLog()!;
            if (led)
            {
                Assert.AreEqual(1, log.Total);
                Assert.AreEqual(new SessionKillRecord(6002, 3), log.HighestLevel, "the victim has not met the hero: its not-yet-met description");
                Assert.AreEqual(new SessionKillRecord(6002, 25), log.MostPositiveAlignment);
            }
            else Assert.IsTrue(log.IsEmpty, "no PC leader: no kill record");
        }
    }

    [TestMethod]
    public void AnInjuryIsRecordedForEachNewConditionInTheOriginalsOrder()
    {
        using var owner = Owner();
        const int Cause = 7001;
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.None, owner.RecordHeroInjuries(LifeEvents.None, Cause));
        Assert.AreEqual(revision, owner.Version.Revision, "nothing happened");
        Assert.AreEqual(GameSessionFailure.None, owner.RecordHeroInjuries(
            LifeEvents.CrippledLegs | LifeEvents.Blinded | LifeEvents.Scarred | LifeEvents.CrippledBothArms | LifeEvents.Stunned, Cause));
        CollectionAssert.AreEqual(new SessionInjury[] { new(Cause, 3), new(Cause, 0), new(Cause, 1), new(Cause, 2) }, owner.QueryHeroKillLog()!.Injuries.ToArray(),
            "scar, blinding, arm, legs");
        // A scar the session's own limit refused is recorded all the same (the original has no limit there).
        Assert.AreEqual(GameSessionFailure.None, owner.RecordHeroInjuries(LifeEvents.ScarLimitReached | LifeEvents.CrippledArm, 7002));
        CollectionAssert.AreEqual(new SessionInjury[] { new(7002, 3), new(7002, 1) }, owner.QueryHeroKillLog()!.Injuries.Skip(4).ToArray());
        // A cause below 1 would cut the original's list: nothing is recorded.
        Assert.AreEqual(GameSessionFailure.None, owner.RecordHeroInjuries(LifeEvents.Blinded, 0));
        Assert.AreEqual(6, owner.QueryHeroKillLog()!.Injuries.Count);
    }

    [TestMethod]
    public void AFullInjuryLogDropsTheRecordWithoutFailingTheAttack()
    {
        using var owner = Owner();
        for (var i = 0; i < SessionStoryLimits.MaxInjuries; i++) Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordHeroInjury(owner.Version, 9, 2).Disposition);
        Assert.AreEqual(GameSessionFailure.None, owner.RecordHeroInjuries(LifeEvents.Blinded, 9));
        Assert.AreEqual(SessionStoryLimits.MaxInjuries, owner.QueryHeroKillLog()!.Injuries.Count);
    }

    // An invented blade whose magic critical values are 100 (as CriticalEffectsStateTests' hero blade): the wielder's aptitude scales them, so
    // the NPC stores magick points.
    private static CombatFields CriticalBlade() => new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
        new CombatItemFields(0, 1, 1, 0, 1, 0, (int)Arcanum.Simulation.Equipment.EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, 0),
        new CombatWeaponFields(0, CombatWeaponSkill.Melee, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 1, 0, 0, 0, 10000, 0, 0, 100, 100, 0, 0, 0),
        null, null, null, null, null);

    [TestMethod]
    public void AnNpcsCriticalHitsOnTheHeroLeaveInjuriesCausedByItsDescription()
    {
        var injuries = 0;
        foreach (var met in new[] { false, true })
        {
            using var owner = Owner();
            if (met) Meet(owner, Rat);
            var cause = met ? Known : Unknown;
            var stats = Stats(); stats[22] = 100;
            var fields = Named(); fields = fields with { Critter = fields.Critter! with { BaseStats = stats, Flags = 0 } };
            var biter = Combat(fields);
            var build = SessionCritterCombat.From(Rat, fields, [CriticalBlade()]);
            Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
            biter = build.Combat!;
            for (var attempt = 0; attempt < 600 && owner.QueryHeroLife() is { } life && !life.Status.IsDead; attempt++)
            {
                var before = owner.QueryHeroKillLog()!.Injuries.Count;
                var result = owner.AttackByNpc(owner.Version, new(biter));
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
                var events = result.Recipient == DamageRecipient.Target && result.Life is { } outcome ? outcome.Events : LifeEvents.None;
                var expected = (events.HasFlag(LifeEvents.Scarred) || events.HasFlag(LifeEvents.ScarLimitReached) ? 1 : 0) + (events.HasFlag(LifeEvents.Blinded) ? 1 : 0)
                    + (events.HasFlag(LifeEvents.CrippledArm) || events.HasFlag(LifeEvents.CrippledBothArms) ? 1 : 0) + (events.HasFlag(LifeEvents.CrippledLegs) ? 1 : 0);
                var after = owner.QueryHeroKillLog()!.Injuries;
                Assert.AreEqual(before + expected, after.Count);
                Assert.IsTrue(after.Skip(before).All(injury => injury.NameId == cause), "the attacker as the hero knows it");
                injuries += expected;
            }
        }
        Assert.IsTrue(injuries > 0, $"injuries within the bounded runs: {injuries}");
    }

    [TestMethod]
    public void TheHerosCriticalSelfHitIsRecordedAsSelfInflicted()
    {
        // 0x4F5690: the attacker is the victim, so the cause is the original's 30286. A bare-handed critical miss turns on the hero (the record
        // follows the life events of each such attack exactly; whether one scars or cripples is the table's roll).
        using var owner = Owner();
        var tough = Combat(Named(hp: 5000));
        var selfHits = 0;
        for (var attempt = 0; attempt < 800 && owner.QueryHeroLife() is { } life && !life.Status.IsDead; attempt++)
        {
            var before = owner.QueryHeroKillLog()!.Injuries.Count;
            var result = owner.AttackCritter(owner.Version, new(tough));
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            var after = owner.QueryHeroKillLog()!.Injuries;
            if (result.Recipient != DamageRecipient.Attacker)
            {
                Assert.AreEqual(before, after.Count);
                continue;
            }
            selfHits++;
            var events = result.Life!.Events;
            var expected = (events.HasFlag(LifeEvents.Scarred) || events.HasFlag(LifeEvents.ScarLimitReached) ? 1 : 0) + (events.HasFlag(LifeEvents.Blinded) ? 1 : 0)
                + (events.HasFlag(LifeEvents.CrippledArm) || events.HasFlag(LifeEvents.CrippledBothArms) ? 1 : 0) + (events.HasFlag(LifeEvents.CrippledLegs) ? 1 : 0);
            Assert.AreEqual(before + expected, after.Count);
            Assert.IsTrue(after.Skip(before).All(injury => injury.NameId == 30286));
        }
        Assert.IsTrue(selfHits > 0, "a critical self-hit within the bounded run");
    }
}
