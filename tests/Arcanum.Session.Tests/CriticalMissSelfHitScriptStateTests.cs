using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2841 (ledger TQ22; 0x4B3170 → 0x4B6410 → 0x4B3770): a critical miss turns the attack into a hit on the attacker, and the hit step runs the weapon's hit (7) and critical-hit (34, when the self-hit is critical) scripts and the
// attacker's critter-hits script (26) before the damage apply. The session tells the host (SessionHitScriptEvent.SelfHit) before the self-hit's damage step. The NPC fields are AttackStateTests' invented small animal, made tough.
[TestClass]
public sealed class CriticalMissSelfHitScriptStateTests
{
    private static SessionCritterCombat Tough(string entity)
    {
        var build = SessionCritterCombat.From(entity, Npc(hpAdjustment: 5000));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void AHerosCriticalMissTellsTheHostItsSelfHitBeforeTheDamageWithTheWieldedWeaponAndOnlyACriticalSelfHitIsCritical()
    {
        using var owner = Owner();
        var blade = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(4, 9) }, SpeedFactor = 15, DamagesArmor = true, CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, blade), (_, _) => true, _ => SessionInsertVerdict.Accepted).Failure);
        var heroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", new(1000, 0, 0, false)) };
        var victim = Tough("oid:victim");
        int critical = 0, plain = 0;
        for (var attempt = 0; attempt < 6000 && (critical < 1 || plain < 1); attempt++)
        {
            var heard = new List<(SessionHitScriptEvent Event, int HitPoints)>();
            var before = owner.QueryLife(SessionLifeTarget.Hero).View!.Status.HitPoints;
            var result = owner.AttackCritter(owner.Version, new SessionAttack(victim)
            {
                HeroWorn = heroWorn, HitScripts = hit => heard.Add((hit, owner.QueryLife(SessionLifeTarget.Hero).View!.Status.HitPoints)),
            });
            if (result.Resolution?.Refusal != AttackRefusal.None || result.Resolution!.Outcome != AttackOutcome.CriticalMiss) continue;
            Assert.AreEqual(1, heard.Count, "one event for the self-hit");
            Assert.AreEqual((true, result.Resolution.CriticalSelfHit, "kit:1", false, false), (heard[0].Event.SelfHit, heard[0].Event.Critical, heard[0].Event.Weapon, heard[0].Event.Projectile, heard[0].Event.Thrown));
            Assert.AreEqual(before, heard[0].HitPoints, "before the hit points are written");
            if (result.Resolution.CriticalSelfHit) critical++; else plain++;
        }
        Assert.IsTrue(critical >= 1 && plain >= 1, $"critical self-hits {critical}, plain {plain}");
    }

    [TestMethod]
    public void AnNpcsCriticalMissTellsTheHostItsSelfHitAndAPlainMissTellsNothing()
    {
        using var owner = Owner();
        var striker = Tough("oid:striker");
        var victim = Tough("oid:victim");
        int selfHits = 0, misses = 0;
        for (var attempt = 0; attempt < 6000 && (selfHits < 3 || misses < 3); attempt++)
        {
            var heard = new List<(SessionHitScriptEvent Event, int HitPoints)>();
            var before = owner.QueryLife(striker.Target).View!.Status.HitPoints;
            var result = owner.AttackByNpc(owner.Version, new SessionNpcAttack(striker)
            {
                Target = victim, HitScripts = hit => heard.Add((hit, owner.QueryLife(striker.Target).View!.Status.HitPoints)),
            });
            if (result.Resolution?.Refusal != AttackRefusal.None) continue;
            var resolution = result.Resolution!;
            if (resolution.Outcome == AttackOutcome.CriticalMiss)
            {
                selfHits++;
                Assert.AreEqual(1, heard.Count);
                Assert.AreEqual((true, resolution.CriticalSelfHit, before), (heard[0].Event.SelfHit, heard[0].Event.Critical, heard[0].HitPoints), "the attacker is its own victim; the hit points are written after the scripts");
            }
            else if (resolution.Outcome == AttackOutcome.Miss)
            {
                misses++;
                Assert.AreEqual(0, heard.Count, "a plain miss tells nothing");
            }
        }
        Assert.IsTrue(selfHits >= 3 && misses >= 3, $"self-hits {selfHits}, misses {misses}");
    }

    [TestMethod]
    public void AFlyingShotsOrAThrowsCriticalMissTellsNothingForTheOriginalsProjectileEndsAtOnceOnTheAttackersTile()
    {
        // ARC-SCRIPT-2841 (exe read of 0x4B3170, 0x4B39B0, 0x42CAC0): a projectile that flies has no hit step on the self-hit, so no script runs for it.
        using var owner = Owner();
        var victim = Tough("oid:victim");
        var seen = 0;
        for (var attempt = 0; attempt < 6000 && seen < 3; attempt++)
        {
            var heard = new List<SessionHitScriptEvent>();
            var result = owner.AttackCritter(owner.Version, new SessionAttack(victim) { Flight = true, Distance = 1, HitScripts = heard.Add });
            if (result.Resolution?.Refusal != AttackRefusal.None) continue;
            if (result.Resolution!.Outcome == AttackOutcome.CriticalMiss) { seen++; Assert.AreEqual(0, heard.Count, "a flying shot's self-hit runs no hit scripts"); }
            foreach (var shot in result.Shots) owner.EndShot(shot);
        }
        Assert.IsTrue(seen >= 1, $"critical misses {seen}");
    }
}
