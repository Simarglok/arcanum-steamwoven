using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The critical effects of the hero's attack (ARC-COMBAT-009). The hero wields an invented blade whose magic critical values are 100,
// so every hit is critical and every effect roll passes its threshold (the constitution checks still roll); a biped golem (body
// type 0) wears a helmet, a vest and a sword from its map seed. Values are invented.
[TestClass]
public sealed class CriticalEffectsStateTests
{
    private const string Golem = "golem";
    private static readonly CellCoord GolemCell = new(101, 200);
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static EquipmentItem Blade(int missChance = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            MagicCriticalHitChance = 100, MagicCriticalHitEffect = 100, MagicCriticalMissChance = missChance,
        },
    };

    private static readonly Dictionary<EquipmentSlot, SessionWearItem> GolemItems = new()
    {
        [EquipmentSlot.Helmet] = new("golem-helmet", Sturdy),
        [EquipmentSlot.Armor] = new("golem-vest", Sturdy),
        [EquipmentSlot.Weapon] = new("golem-sword", Sturdy),
    };

    private static SessionCritterCombat GolemCombat()
    {
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0);
        return SessionCritterCombat.From(Golem, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }).Combat!;
    }

    private static SessionMapInventorySeed Seed() => new(
        [new(Golem, SessionHolderKind.Npc, false, false, [], [.. GolemItems.Values.Select(item => item.Entity)])],
        [.. GolemItems.Select(pair => new SessionSeedItem(pair.Value.Entity,
            InventoryStateTests.Facts(pair.Key == EquipmentSlot.Weapon ? SessionItemType.Weapon : SessionItemType.Armor,
                pair.Key == EquipmentSlot.Weapon ? SwordName : 2900, (int)pair.Key), null))]);

    private const int SwordName = 2901;

    private static GameSessionOwner Owner(int missChance = 0)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade(missChance)),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> HeroWorn() => new() { [EquipmentSlot.Weapon] = new("kit:1", Sturdy) };

    private static Dictionary<EquipmentSlot, SessionWearItem> Worn(GameSessionOwner owner) =>
        GolemItems.Where(pair => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Golem).Items.Any(i => i.Entity == pair.Value.Entity))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    [TestMethod]
    public void ACriticalHitDisarmsTheGolemAndItsSwordLiesAtItsCell()
    {
        using var owner = Owner();
        var golem = GolemCombat();
        SessionAttackResult? critical = null;
        for (var attempt = 0; attempt < 200 && critical is null; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Resolution!.Outcome == AttackOutcome.CriticalHit) critical = result;
        }
        Assert.IsNotNull(critical, "a critical hit within the bounded run");
        var effects = critical!.Resolution!.Damage!.HitEffects;
        Assert.IsTrue(effects.HasFlag(CriticalHitEffects.DamageWeapon | CriticalHitEffects.DropWeapon), $"every roll passes: {effects}");
        Assert.IsTrue(critical.Resolution.Rolls.Any(roll => roll.Purpose == CombatRollPurpose.CriticalStunCheck), "the table rolled in the session");
        // The sword is damaged by the wear, then dropped at the golem's cell.
        Assert.AreEqual(SessionWearStepKind.WeaponDamage, critical.Wear!.Steps.Single(step => step.Entity == "golem-sword").Kind);
        var drop = critical.Criticals.Single(step => step.Kind == SessionCriticalStepKind.DropWeapon);
        Assert.AreEqual(("golem-sword", SessionCriticalStepOutcome.Dropped, (CellCoord?)GolemCell), (drop.Entity, drop.Outcome, drop.Cell));
        var view = owner.QueryMapInventory(Exterior)!;
        Assert.IsFalse(view.Holders.Single(h => h.Entity == Golem).Items.Any(i => i.Entity == "golem-sword"), "it left the golem's list");
        Assert.AreEqual(GolemCell, view.Ground.Single(g => g.Item.Entity == "golem-sword").Cell);

        // The drop is a map change: a restore keeps it.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GolemCell, restored.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == "golem-sword").Cell);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "golem-sword", Accept).Disposition, "picked up from the ground");
        Assert.IsFalse(restored.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == "golem-sword"));

        // The next attack sees the golem without a weapon: no weapon rolls.
        var next = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
        Assert.IsFalse(next.Resolution!.Rolls.Any(roll => roll.Purpose is CombatRollPurpose.CriticalDamageWeapon or CombatRollPurpose.CriticalDropWeapon));

        // Refused before any roll: item data that still lists the dropped sword, a cell the drop cannot hold.
        var draws = owner.QueryRandom().Draws;
        Assert.AreEqual(GameSessionFailure.InvalidWear,
            owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = GolemItems, TargetCell = GolemCell }).Failure);
        foreach (var cell in new CellCoord[] { new(-1, 0), new(0, -1), new(1 << 24, 0), new(0, 1 << 24) })
            Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(golem) { TargetCell = cell }).Failure);
        Assert.AreEqual(draws, owner.QueryRandom().Draws);

        // No other reader of the golem's list sees the sword: negative IN, the NPC inventory store, and loot once it is dead.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.GiveItemToPc(owner.Version, Golem, SwordName, Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RemoveItemFromNpc(owner.Version, Golem, "golem-sword").Failure);
        Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.Kill(owner.Version, golem.Target).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, Golem, "golem-sword", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.AttackCritter(owner.Version, new(golem) { TargetCell = new((1 << 24) - 1, (1 << 24) - 1) }).Failure,
            "the highest cell the drop holds is admitted (the dead golem then refuses in the resolver)");
    }

    [TestMethod]
    public void WithoutItsCellOrItemDataAnNpcKeepsItsItems()
    {
        using var owner = Owner();
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner) });
            if (result.Resolution!.Outcome != AttackOutcome.CriticalHit) continue;
            Assert.AreEqual(SessionCriticalStepOutcome.Unknown, result.Criticals.Single(step => step.Kind == SessionCriticalStepKind.DropWeapon).Outcome);
            Assert.AreEqual(3, Worn(owner).Count);
            return;
        }
        Assert.Fail("no critical hit within the bounded run");
    }

    [TestMethod]
    public void TheHerosDroppedWeaponLiesAtItsCellUntilItIsPickedUp()
    {
        // ARC-WORLD-059. A blade that fails critically whenever it misses: half of those turn on the hero critically, and its own
        // table disarms it.
        using var owner = Owner(missChance: 100);
        var golem = GolemCombat();
        var kit = owner.QueryHeroKit().Select(item => item.Entity).ToArray();
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Recipient != DamageRecipient.Attacker || !result.Resolution!.CriticalSelfHit) continue;
            if (owner.IsItemDestroyed("kit:1")) Assert.Inconclusive("the critical miss destroyed the blade first");
            var drop = result.Criticals.Single(step => step.Kind == SessionCriticalStepKind.DropWeapon);
            Assert.AreEqual((DamageRecipient.Attacker, "kit:1", SessionCriticalStepOutcome.Dropped, (CellCoord?)Start.Cell),
                (drop.Whose, drop.Entity, drop.Outcome, drop.Cell), "at the hero's cell");
            Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "no longer wielded");
            Assert.IsFalse(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"));
            var placed = owner.QueryPlacedItems().Single();
            Assert.AreEqual(("kit:1", SessionPlacedSource.Kit, Exterior, Start.Cell), (placed.Entity, placed.Source, placed.Map, placed.Cell));
            Assert.AreEqual(Start.Cell, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == "kit:1").Cell);

            // Saved and restored with the session; picked up, it is back in the kit at its number's place.
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
            Assert.AreEqual(placed, restored.QueryPlacedItems().Single());
            Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "kit:1", Accept).Disposition);
            CollectionAssert.AreEqual(kit, restored.QueryHeroKit().Select(item => item.Entity).ToArray());
            Assert.AreEqual(0, restored.QueryPlacedItems().Count);
            return;
        }
        Assert.Fail("no critical self-hit within the bounded run");
    }

    [TestMethod]
    public void ACrippledArmDropsWhatItCanNoLongerHold()
    {
        // A torch in the shield slot (a generic item with the torch flag): one crippled arm refuses it, the one-handed sword stays.
        var torch = new CombatFields(CombatFieldOrigin.AreaObject, new CombatObjectFields(14, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, 1005, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, 0), null, null, null,
            new CombatGenericFields(0x1, 0, 0), null, null);
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0);
        var build = SessionCritterCombat.From(Golem, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }, [torch]);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure, $"torch {SessionEquipmentFacts.FromCombatFields(torch)}");
        var golem = build.Combat!;
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = Seed();
        seed = new([new(Golem, SessionHolderKind.Npc, false, false, [], [.. seed.Holders[0].Items, "golem-torch"])],
            [.. seed.Items, new SessionSeedItem("golem-torch", InventoryStateTests.Facts(SessionItemType.Generic, 2900, 1005), null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade()),
            (_, _) => true, Accept).Failure);
        var worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = GolemItems[EquipmentSlot.Weapon], [EquipmentSlot.Shield] = new("golem-torch", Sturdy) };
        // An arm is a tenth of the rolled locations (an aimed attack at the arm misses too often to be worth it here).
        for (var attempt = 0; attempt < 4000; attempt++)
        {
            // A critical self-hit may stun or cripple the hero: it recovers between attacks.
            owner.ClearStun(owner.Version, SessionLifeTarget.Hero);
            owner.Heal(owner.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
            var result = owner.AttackCritter(owner.Version, new(golem)
            {
                HeroWorn = owner.QueryEquipment().Worn.ToDictionary(item => item.Slot, item => new SessionWearItem(item.Entity, Sturdy)),
                TargetWorn = worn.Where(pair => !owner.IsItemDestroyed(pair.Value.Entity) && !owner.QueryMapInventory(Exterior)!.Ground
                    .Any(g => g.Item.Entity == pair.Value.Entity)).ToDictionary(pair => pair.Key, pair => pair.Value),
                TargetCell = GolemCell,
            });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Recipient != DamageRecipient.Target || result.Life?.Events.HasFlag(LifeEvents.ArmHitDropsItems) != true) continue;
            var steps = result.Criticals.Where(step => step.Kind is SessionCriticalStepKind.ArmHitWeapon or SessionCriticalStepKind.ArmHitShield).ToArray();
            // The sword's wear values are not in the golem's map data (no carried fields for it): its check cannot run.
            Assert.AreEqual((SessionCriticalStepOutcome.Unknown, SessionCriticalStepOutcome.Dropped), (steps[0].Outcome, steps[1].Outcome),
                string.Join("; ", result.Criticals) + " attempt " + attempt);
            Assert.AreEqual(GolemCell, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == "golem-torch").Cell);
            return;
        }
        Assert.Fail("no crippling arm hit within the bounded run");
    }
}
