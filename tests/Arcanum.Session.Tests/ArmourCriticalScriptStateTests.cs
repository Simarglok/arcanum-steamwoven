using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2828 (ledger TQ23; 0x4B4390, the loop at 0x4B47F8): a critical damage step on a living critter tells the host's armour scripts (point 34), after the victim's taking-damage script
// and before the hit points are written. The NPC fields are AttackStateTests' invented small animal, made tough so a bounded run meets the critical it waits for.
[TestClass]
public sealed class ArmourCriticalScriptStateTests
{
    private const string Victim = "oid:victim", Striker = "oid:striker";

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
        // Both NPCs hold their pieces in the map's inventory seed, as the wear step tracks the items it is told about.
        var owner = result.Owner!;
        var holders = new[] { (Victim, "victim"), (Striker, "striker") };
        var seed = new SessionMapInventorySeed(
            [.. holders.Select(npc => new SessionSeedHolder(npc.Item1, SessionHolderKind.Npc, false, false, [], [.. WornBy(npc.Item2).Values.Select(item => item.Entity)]))],
            [.. holders.SelectMany(npc => WornBy(npc.Item2)).Select(pair => new SessionSeedItem(pair.Value.Entity, InventoryStateTests.Facts(SessionItemType.Armor, 2900, (int)pair.Key), null))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    // Three worn pieces, listed against their slot order (boots before the helmet in the dictionary's own order).
    private static Dictionary<EquipmentSlot, SessionWearItem> WornBy(string prefix) => new()
    {
        [EquipmentSlot.Boots] = new($"{prefix}-boots", new(1000, 0, 0, false)),
        [EquipmentSlot.Armor] = new($"{prefix}-armour", new(1000, 0, 0, false)),
        [EquipmentSlot.Helmet] = new($"{prefix}-helm", new(1000, 0, 0, false)),
    };

    private static readonly string[] InSlotOrder = ["-helm", "-armour", "-boots"];

    [TestMethod]
    public void AHerosCriticalHitTellsTheTargetsWornPiecesInSlotOrderAfterItsTakingDamageScriptAndBeforeTheHitPointsAreWritten()
    {
        using var owner = Owner();
        var victim = Tough(Victim);
        var worn = WornBy("victim");
        var log = new List<string>();
        var heard = new List<(SessionArmourCriticalEvent Event, int HitPoints)>();
        int criticalHits = 0, plainHits = 0;
        for (var attempt = 0; attempt < 3000 && criticalHits < 3; attempt++)
        {
            log.Clear(); heard.Clear();
            var before = owner.QueryLife(victim.Target).View!.Status.HitPoints;
            var result = owner.AttackCritter(owner.Version, new SessionAttack(victim)
            {
                TargetWorn = worn,
                TakingDamage = _ => { log.Add("taking-damage"); return true; },
                ArmourScripts = armour => { log.Add("armour"); heard.Add((armour, owner.QueryLife(victim.Target).View!.Status.HitPoints)); },
            });
            if (result.Resolution?.Refusal != AttackRefusal.None) continue;
            var critical = result.Resolution!.Outcome == AttackOutcome.CriticalHit && result.Resolution.Damage is { Prevented: DamagePrevention.None };
            var selfCritical = result.Resolution.Outcome == AttackOutcome.CriticalMiss && result.Resolution.CriticalSelfHit;
            if (critical)
            {
                criticalHits++;
                Assert.AreEqual(1, heard.Count, "one event for a critical hit");
                Assert.AreEqual(DamageRecipient.Target, heard[0].Event.Recipient);
                CollectionAssert.AreEqual(InSlotOrder.Select(suffix => "victim" + suffix).ToArray(), heard[0].Event.Worn.ToArray());
                Assert.AreEqual(before, heard[0].HitPoints, "the hit points are written after the scripts");
                CollectionAssert.AreEqual(new[] { "taking-damage", "armour" }, log, "after the victim's taking-damage script");
            }
            else
            {
                if (result.Resolution.Outcome == AttackOutcome.Hit) plainHits++;
                // The hero's self-hit has no worn pieces in this call (HeroWorn null): nothing to tell.
                Assert.AreEqual(0, heard.Count, selfCritical ? "the hero passes no worn pieces" : "no critical damage step, no armour script");
            }
        }
        Assert.IsTrue(criticalHits >= 3 && plainHits >= 1, $"critical hits {criticalHits}, plain hits {plainHits}");
    }

    [TestMethod]
    public void ASkippedDefaultOfTheTakingDamageScriptStopsTheArmourScripts()
    {
        using var owner = Owner();
        var victim = Tough(Victim);
        var told = 0; var vetoedCriticals = 0;
        for (var attempt = 0; attempt < 3000 && vetoedCriticals < 2; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new SessionAttack(victim)
            {
                TargetWorn = WornBy("victim"), TakingDamage = _ => false, ArmourScripts = _ => told++,
            });
            if (result.Vetoed && result.Resolution?.Outcome == AttackOutcome.CriticalHit) vetoedCriticals++;
        }
        Assert.IsTrue(vetoedCriticals >= 2, "critical hits met within the bounded run");
        Assert.AreEqual(0, told, "a vetoed damage step never reaches the armour loop");
    }

    [TestMethod]
    public void AnNpcsCriticalHitTellsTheVictimsPiecesAndItsCriticalSelfHitTellsItsOwn()
    {
        using var owner = Owner();
        var striker = Tough(Striker);
        var victim = Tough(Victim);
        var victimWorn = WornBy("victim");
        var strikerWorn = WornBy("striker");
        int hits = 0, selfHits = 0, others = 0;
        for (var attempt = 0; attempt < 6000 && (hits < 2 || selfHits < 2); attempt++)
        {
            var heard = new List<SessionArmourCriticalEvent>();
            var result = owner.AttackByNpc(owner.Version, new SessionNpcAttack(striker)
            {
                Target = victim, AttackerWorn = strikerWorn, TargetWorn = victimWorn, TakingDamage = _ => true, ArmourScripts = heard.Add,
            });
            if (result.Resolution?.Refusal != AttackRefusal.None) continue;
            var resolution = result.Resolution!;
            if (resolution.Outcome == AttackOutcome.CriticalHit && resolution.Damage is { Prevented: DamagePrevention.None })
            {
                hits++;
                Assert.AreEqual(1, heard.Count);
                Assert.AreEqual(DamageRecipient.Target, heard[0].Recipient);
                CollectionAssert.AreEqual(InSlotOrder.Select(suffix => "victim" + suffix).ToArray(), heard[0].Worn.ToArray());
            }
            else if (resolution.Outcome == AttackOutcome.CriticalMiss && resolution.CriticalSelfHit && resolution.Damage is { Prevented: DamagePrevention.None })
            {
                selfHits++;
                Assert.AreEqual(1, heard.Count, "the critical self-hit is a critical damage step on the attacker");
                Assert.AreEqual(DamageRecipient.Attacker, heard[0].Recipient);
                CollectionAssert.AreEqual(InSlotOrder.Select(suffix => "striker" + suffix).ToArray(), heard[0].Worn.ToArray());
            }
            else
            {
                others++;
                Assert.AreEqual(0, heard.Count, "a plain hit, a miss and a self-hit that is no critical tell nothing");
            }
        }
        Assert.IsTrue(hits >= 2 && selfHits >= 2 && others >= 1, $"hits {hits}, critical self-hits {selfHits}, others {others}");
    }

    [TestMethod]
    public void NoHostAndNoWornPiecesRunNothing()
    {
        using var owner = Owner();
        var victim = Tough(Victim);
        for (var attempt = 0; attempt < 400; attempt++)
        {
            Assert.AreEqual(GameSessionFailure.None, owner.AttackCritter(owner.Version, new SessionAttack(victim) { TargetWorn = WornBy("victim") }).Failure, "no host: the damage goes on");
            var told = 0;
            owner.AttackCritter(owner.Version, new SessionAttack(victim) { ArmourScripts = _ => told++ });
            Assert.AreEqual(0, told, "a target that wears nothing has no pieces");
        }
    }
}
