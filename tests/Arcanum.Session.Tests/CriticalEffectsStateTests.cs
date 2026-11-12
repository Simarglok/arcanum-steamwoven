using Arcanum.Data.Combat;
using Arcanum.Simulation.Ai;
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

    private static EquipmentItem Blade(int missChance = 0, int? ammoType = null, int ammoConsumption = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            MagicCriticalHitChance = 100, MagicCriticalHitEffect = 100, MagicCriticalMissChance = missChance,
            AmmoType = ammoType, AmmoConsumption = ammoConsumption, MagicCriticalMissEffect = ammoType is null ? 0 : 30,
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

    private static SessionMapInventorySeed Seed(SessionItemType? swordType = null) => new(
        [new(Golem, SessionHolderKind.Npc, false, false, [], [.. GolemItems.Values.Select(item => item.Entity)])],
        [.. GolemItems.Select(pair => new SessionSeedItem(pair.Value.Entity,
            InventoryStateTests.Facts(pair.Key == EquipmentSlot.Weapon ? swordType ?? SessionItemType.Weapon : SessionItemType.Armor,
                pair.Key == EquipmentSlot.Weapon ? SwordName : 2900, (int)pair.Key), null))]);

    private const int SwordName = 2901;

    private static GameSessionOwner Owner(int missChance = 0, SessionItemType? swordType = null)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed(swordType)).Failure);
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
        // ARC-WORLD-062: the dropped sword decays two game days on.
        Assert.AreEqual(("golem-sword", Exterior), owner.QueryItemDecays().Select(d => (d.Entity, d.Map)).Single());

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
            Assert.AreEqual("kit:1", owner.QueryItemDecays().Single().Entity, "the hero's dropped blade decays (ARC-WORLD-062)");

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
    public void TheHeroKeepsANoDropWeaponAndCannotDropOneItsOwnHitBroke()
    {
        foreach (var (worn, expected) in new[]
        {
            (new SessionWearItem("kit:1", Sturdy) { NoDrop = true }, SessionCriticalStepOutcome.Refused),
            // "Damage weapon" (10) breaks a weapon of 10 hit points first: the first break takes it off, so its slot is empty.
            (new SessionWearItem("kit:1", new ItemCondition(10, 0, 0, false)), SessionCriticalStepOutcome.NoItem),
        })
        {
            using var owner = Owner(missChance: 100);
            var golem = GolemCombat();
            var found = false;
            for (var attempt = 0; attempt < 600 && !found; attempt++)
            {
                if (owner.QueryEquipment().Worn.Count == 0) Assert.Inconclusive("the blade left before a self-hit with both effects");
                var result = owner.AttackCritter(owner.Version, new(golem)
                {
                    HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = worn }, TargetWorn = Worn(owner), TargetCell = GolemCell,
                });
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
                var effects = result.Resolution!.Damage?.HitEffects ?? CriticalHitEffects.None;
                if (result.Recipient != DamageRecipient.Attacker || !effects.HasFlag(CriticalHitEffects.DamageWeapon | CriticalHitEffects.DropWeapon)) continue;
                if (owner.IsItemDestroyed("kit:1")) Assert.Inconclusive("the critical miss destroyed the blade first");
                Assert.AreEqual(expected, result.Criticals.Single(step => step.Kind == SessionCriticalStepKind.DropWeapon).Outcome);
                Assert.AreEqual(0, owner.QueryPlacedItems().Count, "nothing lies on the ground");
                found = true;
            }
            Assert.IsTrue(found, "a critical self-hit with both weapon effects within the bounded run");
        }
    }

    // ARC-WORLD-1288 (ledger VP6, VPG3; the item drop 0x461AB0): an NPC that dropped a weapon gets LOOK_FOR_WEAPON, one that dropped armour LOOK_FOR_ARMOR, another item
    // type no bit, and the bit is saved. Every NPC drop of the session goes through the one Drop step: the attack's critical drop and the arm-hit drops (an attack on the
    // golem), a spell's weapon block (SpellWeaponDrop).
    private static DangerFlags LookFor(GameSessionOwner owner) => owner.QueryDanger(Golem).Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor);

    [TestMethod]
    public void AnNpcThatDropsItsWeaponThroughACriticalLooksForAWeapon()
    {
        using var owner = Owner();
        var golem = GolemCombat();
        Assert.AreEqual(DangerFlags.None, LookFor(owner), "no bit before a drop");
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            var dropped = result.Criticals.Where(step => step.Outcome == SessionCriticalStepOutcome.Dropped && step.Whose == DamageRecipient.Target).ToArray();
            if (dropped.Length == 0) { Assert.AreEqual(DangerFlags.None, LookFor(owner), "a hit with no drop sets no bit"); continue; }
            Assert.IsTrue(dropped.Any(step => step.Entity == "golem-sword"));
            var armour = dropped.Any(step => step.Entity != "golem-sword");
            Assert.AreEqual(armour ? DangerFlags.LookForWeapon | DangerFlags.LookForArmor : DangerFlags.LookForWeapon, LookFor(owner));
            // The bits are saved as today: a restore keeps them.
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(LookFor(owner), restored.QueryDanger(Golem).Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor));
            // The hero's drops set no bit: the hero has no danger record entry of its own, and no other NPC gained one.
            Assert.IsFalse(owner.QueryDangerRecords().Records.Any(record => record.Entity != Golem && (record.Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor)) != 0));
            return;
        }
        Assert.Fail("no critical drop within the bounded run");
    }

    [TestMethod]
    public void ASpellsWeaponDropSetsTheLookForBitOfTheDroppedItemType()
    {
        foreach (var (type, expected) in new[]
        {
            (SessionItemType.Weapon, DangerFlags.LookForWeapon), (SessionItemType.Armor, DangerFlags.LookForArmor),
            (SessionItemType.Generic, DangerFlags.None), (SessionItemType.Food, DangerFlags.None),
        })
        {
            // The weapon slot holds an item of the tested type (the slot decides only which item is dropped; the item's own type decides the bit).
            using var owner = Owner(swordType: type);
            var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(owner), GolemCell, null, out var failure);
            Assert.AreEqual(GameSessionFailure.None, failure);
            Assert.AreEqual(SessionCriticalStepOutcome.Dropped, steps.Single().Outcome, type.ToString());
            Assert.AreEqual(expected, LookFor(owner), type.ToString());
            // Saved as today.
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
            Assert.AreEqual(expected, restored.QueryDanger(Golem).Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor), type.ToString());
        }
    }

    [TestMethod]
    public void ARefusedOrMissingDropSetsNoLookForBitAndTheHeroSetsNone()
    {
        // No item data: the drop is not run, nothing is set.
        using var owner = Owner();
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, null, GolemCell, null, out _);
        Assert.AreEqual(SessionCriticalStepOutcome.Unknown, steps.Single().Outcome);
        Assert.AreEqual(DangerFlags.None, LookFor(owner));
        // A no-drop item is refused: no bit.
        var noDrop = Worn(owner).ToDictionary(pair => pair.Key, pair => pair.Value with { NoDrop = true });
        steps = owner.SpellWeaponDrop(GolemCombat().Target, noDrop, GolemCell, null, out _);
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, steps.Single().Outcome);
        Assert.AreEqual(DangerFlags.None, LookFor(owner));
        // The hero's dropped weapon sets no bit on any record.
        using var heroOwner = Owner(missChance: 100);
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 600; attempt++)
        {
            heroOwner.ClearStun(heroOwner.Version, SessionLifeTarget.Hero);
            heroOwner.Heal(heroOwner.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
            if (heroOwner.IsItemDestroyed("kit:1") || heroOwner.QueryEquipment().Worn.Count == 0) break;
            var result = heroOwner.AttackCritter(heroOwner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(heroOwner), TargetCell = GolemCell });
            if (!result.Criticals.Any(step => step.Whose == DamageRecipient.Attacker && step.Outcome == SessionCriticalStepOutcome.Dropped)) continue;
            Assert.IsFalse(heroOwner.QueryDangerRecords().Records.Any(record => (record.Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor)) != 0));
            return;
        }
        Assert.Inconclusive("no critical self-hit with a drop within the bounded run");
    }

    // ARC-COMBAT-010: an open map without bounds; a path is the straight line of DropPlacementRules' target, critters stand on the
    // listed cells.
    private sealed class OpenMap(params CellCoord[] critters) : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public List<(CellCoord From, CellCoord To, DropOwner Who)> Asked { get; } = [];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            Asked.Add((from, to, who));
            var rotations = new List<int>();
            for (var cell = from; cell != to; cell = Step(cell, rotations[^1])!.Value)
                rotations.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - cell.X), Math.Sign(to.Y - cell.Y))));
            return new(rotations.Count, 0, rotations);
        }
        public bool HasCritter(CellCoord cell) => critters.Contains(cell);
    }

    [TestMethod]
    public void WithTheHostsMapAnItemLandsWhereTheOriginalsTriesLead()
    {
        // The golem's sword: its draws give a direction; the first tile off the golem's has no critter, so the walk stops there.
        using var owner = Owner(missChance: 100);
        var golem = GolemCombat();
        var map = new OpenMap(GolemCell, Start.Cell);
        bool npcDropped = false, heroDropped = false;
        for (var attempt = 0; attempt < 600 && !(npcDropped && heroDropped); attempt++)
        {
            owner.ClearStun(owner.Version, SessionLifeTarget.Hero);
            owner.Heal(owner.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
            if (owner.IsItemDestroyed("kit:1") || owner.QueryEquipment().Worn.Count == 0) break;
            map.Asked.Clear();
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell, DropMap = map });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            foreach (var drop in result.Criticals.Where(step => step.Outcome == SessionCriticalStepOutcome.Dropped))
            {
                var hero = drop.Whose == DamageRecipient.Attacker;
                var from = hero ? Start.Cell : GolemCell;
                var asked = map.Asked.Where(a => a.From == from).ToArray();
                Assert.IsTrue(asked.Length >= 1 && asked.All(a => a.Who == (hero ? DropOwner.PcSide : DropOwner.Npc)), "the owner's kind goes to the path");
                // Each try's target gives one landing: the walk stops on the first tile without a critter (the hero stands next to the
                // golem), else at the path's end; the owner's own tile when the target is that tile (direction 8). Two drops of one
                // owner share the asked list, so the landing is one of its targets'.
                var landings = asked.Select(a =>
                {
                    var cell = from;
                    while (cell != a.To && map.HasCritter(cell)) cell = map.Step(cell, AreaRotation(cell, a.To))!.Value;
                    return cell;
                }).ToHashSet();
                Assert.IsTrue(landings.Contains(drop.Cell!.Value), $"{hero} {from} {drop.Cell}");
                if (hero)
                {
                    Assert.AreEqual(drop.Cell, owner.QueryPlacedItems().Single().Cell);
                    heroDropped = true;
                    Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:1", Accept).Disposition);
                    Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon,
                        new(Kit[0].Kind, [], false, Blade(100)), (_, _) => true, Accept).Failure);
                }
                else
                {
                    Assert.AreEqual(drop.Cell, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == drop.Entity).Cell);
                    npcDropped |= drop.Entity == "golem-sword";
                }
            }
        }
        if (!heroDropped || !npcDropped) Assert.Inconclusive($"drops seen: hero {heroDropped}, golem {npcDropped}");

        // A no-drop item is refused before any try (after the distance draw, PI1).
        using var kept = Owner(missChance: 100);
        var noDrop = new OpenMap();
        for (var attempt = 0; attempt < 600; attempt++)
        {
            kept.ClearStun(kept.Version, SessionLifeTarget.Hero);
            var result = kept.AttackCritter(kept.Version, new(golem)
            {
                HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", Sturdy) { NoDrop = true } },
                TargetWorn = Worn(kept), TargetCell = GolemCell, DropMap = noDrop,
            });
            if (result.Criticals.FirstOrDefault(step => step.Whose == DamageRecipient.Attacker && step.Kind == SessionCriticalStepKind.DropWeapon) is not { } refused)
                continue;
            Assert.AreEqual(SessionCriticalStepOutcome.Refused, refused.Outcome);
            Assert.IsFalse(noDrop.Asked.Any(a => a.From == Start.Cell), "no try for the hero's item");
            return;
        }
        Assert.Inconclusive("no critical self-hit with a drop within the bounded run");
    }

    private static int AreaRotation(CellCoord from, CellCoord to) => (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y)) switch
    {
        (-1, -1) => 0, (-1, 0) => 1, (-1, 1) => 2, (0, 1) => 3, (1, 1) => 4, (1, 0) => 5, (1, -1) => 6, _ => 7,
    };

    [TestMethod]
    public void ACriticalMissSpendsTheHerosAmmunitionOfItsWeaponsType()
    {
        // ARC-COMBAT-013 (CR15, AM4): a blade that uses bullets (type 1), three a shot, with a magic critical-miss effect of 30 (a
        // critical self-hit takes all at d100 ≤ 31, else five shots at a second d100 ≤ 41), and 25 bullets the hero picked up. "Lose
        // ammunition" takes 15 and is refused with fewer left, the stack unchanged; "destroy ammunition" takes the whole stack, which is
        // then deleted. Each attack first spends its shot (3, ARC-COMBAT-012); with fewer than 3 left the attack is refused before any
        // critical, so an empty stack never meets one. Sessions with several seeds run until every outcome was seen.
        var seen = new HashSet<(SessionCriticalStepKind, SessionCriticalStepOutcome)>();
        var refusedWithUnits = false;
        bool Done() => seen.Contains((SessionCriticalStepKind.DestroyAmmo, SessionCriticalStepOutcome.AmmunitionSpent))
            && seen.Contains((SessionCriticalStepKind.LoseAmmo, SessionCriticalStepOutcome.AmmunitionSpent)) && refusedWithUnits;
        for (ulong randomSeed = 0; randomSeed < 64 && !Done(); randomSeed++)
        {
            using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), RandomSeed = randomSeed }).Owner!;
            var seed = Seed();
            seed = new(seed.Holders, [.. seed.Items, new SessionSeedItem("bullets", new(new(SessionItemType.Ammo, 7059, 100, 200), 1, 0, 0, 25, []) { AmmoType = 1 },
                new CellCoord(105, 200))]);
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "bullets", Accept).Disposition);
            Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon,
                new(Kit[0].Kind, [], false, Blade(missChance: 100, ammoType: 1, ammoConsumption: 3)), (_, _) => true, Accept).Failure);
            var golem = GolemCombat();
            for (var attempt = 0; attempt < 400 && !owner.IsItemDestroyed("bullets"); attempt++)
            {
                owner.ClearStun(owner.Version, SessionLifeTarget.Hero);
                owner.Heal(owner.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
                if (owner.IsItemDestroyed("kit:1") || owner.QueryEquipment().Worn.Count == 0) break;
                var before = owner.QueryHeroAmmo().SingleOrDefault().Units;
                // An invulnerable blade is never destroyed (CW3), so the ammunition steps run; a no-drop one stays wielded.
                var result = owner.AttackCritter(owner.Version, new(golem)
                {
                    HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", Sturdy) { Invulnerable = true, NoDrop = true } },
                    TargetWorn = Worn(owner), TargetCell = GolemCell,
                });
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
                if (result.Resolution!.Refusal == AttackRefusal.OutOfAmmunition) { Assert.IsTrue(before < 3); break; }
                // The shot's 3 go first (ARC-COMBAT-012).
                Assert.AreEqual((GameSessionDisposition.Changed, before - 3), (result.Ammunition!.Value.Disposition, result.Ammunition.Value.UnitsAfter));
                var spent = 3;
                foreach (var step in result.Criticals.Where(step => step.Kind is SessionCriticalStepKind.DestroyAmmo or SessionCriticalStepKind.LoseAmmo))
                {
                    seen.Add((step.Kind, step.Outcome));
                    Assert.AreEqual(DamageRecipient.Attacker, step.Whose);
                    switch (step.Outcome)
                    {
                        case SessionCriticalStepOutcome.AmmunitionSpent:
                            Assert.AreEqual((step.Kind == SessionCriticalStepKind.LoseAmmo ? 15 : before - spent, "bullets"), (step.Units, step.Entity));
                            spent += step.Units;
                            break;
                        case SessionCriticalStepOutcome.NoAmmunition when step.Kind == SessionCriticalStepKind.LoseAmmo:
                            Assert.IsTrue(before - spent < 15, $"lose with {before - spent}");
                            refusedWithUnits |= before - spent > 0 && step.Entity == "bullets";
                            break;
                        case SessionCriticalStepOutcome.NoAmmunition:
                            Assert.AreEqual(0, before - spent, "destroy refused only with no stack");
                            break;
                        default: Assert.Fail($"{step.Kind} {step.Outcome}"); break;
                    }
                }
                Assert.AreEqual(before - spent, owner.QueryHeroAmmo().SingleOrDefault().Units, "the stack lost exactly the spent units");
            }
            if (owner.QueryHeroAmmo().Count == 0) Assert.IsTrue(owner.IsItemDestroyed("bullets"), "a stack spent to 0 is deleted");
        }
        Assert.IsTrue(Done(), $"{string.Join(",", seen)} refused with units {refusedWithUnits}");
    }

    [TestMethod]
    public void AShotSpendsItsAmmunitionHitOrMissAndTooFewRefusesAfterTheHitLocation()
    {
        // ARC-COMBAT-012 (A): a blade that uses bullets, two a shot, and 41 bullets: every attack the resolver runs takes 2, a hit or a
        // miss; with 1 left the attack is refused after the hit location's roll and nothing is spent.
        using var owner = AmmoOwner(41);
        var golem = GolemCombat();
        var outcomes = new HashSet<AttackOutcome>();
        for (var left = 39; left >= 1; left -= 2)
        {
            var result = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
            Assert.AreEqual((GameSessionFailure.None, AttackRefusal.None), (result.Failure, result.Resolution!.Refusal));
            Assert.AreEqual((GameSessionDisposition.Changed, "bullets", left), (result.Ammunition!.Value.Disposition, result.Ammunition.Value.Stack, result.Ammunition.Value.UnitsAfter));
            Assert.AreEqual(left, owner.QueryHeroAmmo().Single().Units);
            outcomes.Add(result.Resolution.Outcome);
        }
        Assert.IsTrue(outcomes.Overlaps([AttackOutcome.Hit, AttackOutcome.CriticalHit]) && outcomes.Contains(AttackOutcome.Miss), "hits and misses spent alike: " + string.Join(",", outcomes));
        var draws = owner.QueryRandom().Draws;
        var refused = owner.AttackCritter(owner.Version, new(golem) { HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, 1, (SessionAmmoUse?)null), (refused.Resolution!.Refusal, refused.Resolution.Rolls.Count, refused.Ammunition));
        Assert.AreEqual(draws + 1, owner.QueryRandom().Draws, "the hit location's roll was made");
        Assert.AreEqual(1, owner.QueryHeroAmmo().Single().Units, "nothing spent");
        // An aimed shot rolls nothing before the check.
        var aimed = owner.AttackCritter(owner.Version, new(golem) { Aim = HitLocation.Head, HeroWorn = HeroWorn(), TargetWorn = Worn(owner), TargetCell = GolemCell });
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, 0), (aimed.Resolution!.Refusal, aimed.Resolution.Rolls.Count));
    }

    [TestMethod]
    public void AnObjectAttackSpendsAShotAndTooFewRefuses()
    {
        // ARC-COMBAT-012 (A): the object attack spends a shot the same way (3 bullets, 2 a shot) and refuses with 1 left.
        using var owner = AmmoOwner(3);
        var crate = new SessionObjectTarget(Exterior, "sector:1:9", new SessionObjectBaseline { Kind = DamageableObjectKind.Scenery, Points = 1000, Material = ObjectMaterial.Cloth });
        var shot = owner.AttackObject(owner.Version, new() { Target = crate, HeroWorn = HeroWorn() });
        Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None, AttackRefusal.None), (shot.Failure, shot.StepFailure, shot.Resolution!.Refusal));
        Assert.AreEqual((GameSessionDisposition.Changed, 1), (shot.Ammunition!.Value.Disposition, shot.Ammunition.Value.UnitsAfter));
        var refused = owner.AttackObject(owner.Version, new() { Target = crate, HeroWorn = HeroWorn() });
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, (SessionAmmoUse?)null), (refused.Resolution!.Refusal, refused.Ammunition));
        Assert.AreEqual(1, owner.QueryHeroAmmo().Single().Units, "nothing spent");
    }

    [TestMethod]
    public void AShotThatEmptiesTheStackLeavesACriticalMissNothingToDestroy()
    {
        // ARC-COMBAT-012 with ARC-COMBAT-013: 3 bullets and 3 a shot; the shot takes them all, so a critical miss's "destroy
        // ammunition" that follows finds no stack.
        for (var seed = 1; seed < 400; seed++)
        {
            using var owner = AmmoOwner(3, 3, missChance: 100, seed: (ulong)seed);
            var golem = GolemCombat();
            var result = owner.AttackCritter(owner.Version, new(golem)
            {
                HeroWorn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new("kit:1", Sturdy) { Invulnerable = true, NoDrop = true } },
                TargetWorn = Worn(owner), TargetCell = GolemCell,
            });
            Assert.AreEqual((GameSessionFailure.None, 0), (result.Failure, result.Ammunition!.Value.UnitsAfter));
            if (result.Criticals.FirstOrDefault(step => step.Kind == SessionCriticalStepKind.DestroyAmmo) is not { } destroy) continue;
            Assert.AreEqual(SessionCriticalStepOutcome.NoAmmunition, destroy.Outcome);
            return;
        }
        Assert.Fail("no critical miss destroyed the ammunition in the seeds tried");
    }

    // The half-elf with the kit blade using bullets (type 1, `consumption` a shot) and a stack of `units` bullets picked up.
    private static GameSessionOwner AmmoOwner(int units, int consumption = 2, int missChance = 0, ulong? seed = null)
    {
        var descriptor = Descriptor() with { Hero = HalfElf() };
        var owner = GameSessionOwner.Create(seed is { } value ? descriptor with { RandomSeed = value } : descriptor).Owner!;
        var inventory = Seed();
        inventory = new(inventory.Holders, [.. inventory.Items, new SessionSeedItem("bullets", new(new(SessionItemType.Ammo, 7059, 100, 200), 1, 0, 0, units, []) { AmmoType = 1 },
            new CellCoord(105, 200))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, inventory).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "bullets", Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon,
            new(Kit[0].Kind, [], false, Blade(missChance, ammoType: 1, ammoConsumption: consumption)), (_, _) => true, Accept).Failure);
        return owner;
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
        // Where a dropped item lies: loose, or (ARC-WORLD-063) in a junk pile when another drop lies on its cell.
        CellCoord? Lies(string entity) => owner.QueryMapInventory(Exterior)!.Ground.FirstOrDefault(g => g.Item.Entity == entity)?.Cell
            ?? owner.QueryMapInventory(Exterior)!.JunkPiles.FirstOrDefault(p => p.Items.Any(item => item.Entity == entity))?.Cell;
        // An arm is a tenth of the rolled locations (an aimed attack at the arm misses too often to be worth it here).
        for (var attempt = 0; attempt < 4000; attempt++)
        {
            // A critical self-hit may stun or cripple the hero: it recovers between attacks.
            owner.ClearStun(owner.Version, SessionLifeTarget.Hero);
            owner.Heal(owner.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true });
            var result = owner.AttackCritter(owner.Version, new(golem)
            {
                HeroWorn = owner.QueryEquipment().Worn.ToDictionary(item => item.Slot, item => new SessionWearItem(item.Entity, Sturdy)),
                TargetWorn = worn.Where(pair => !owner.IsItemDestroyed(pair.Value.Entity) && Lies(pair.Value.Entity) is null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                TargetCell = GolemCell,
            });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Recipient != DamageRecipient.Target || result.Life?.Events.HasFlag(LifeEvents.ArmHitDropsItems) != true) continue;
            var steps = result.Criticals.Where(step => step.Kind is SessionCriticalStepKind.ArmHitWeapon or SessionCriticalStepKind.ArmHitShield).ToArray();
            // The sword's wear values are not in the golem's map data (no carried fields for it): its check cannot run.
            Assert.AreEqual((SessionCriticalStepOutcome.Unknown, SessionCriticalStepOutcome.Dropped), (steps[0].Outcome, steps[1].Outcome),
                string.Join("; ", result.Criticals) + " attempt " + attempt);
            Assert.AreEqual(GolemCell, Lies("golem-torch"));
            return;
        }
        Assert.Fail("no crippling arm hit within the bounded run");
    }
}
