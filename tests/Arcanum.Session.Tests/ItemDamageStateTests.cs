using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-025: damage that is no attack of the hero (a script's, a spell's, an acid's, a burn's, a trap's) reaches an item lying on the ground or worn
// (ledger OD1–OD4, OD12, DH9). Items, materials and hit points are invented; the map seed is ObjectAttackStateTests' (a vase on the ground, a cup and a knife
// in a chest) and the kit is the half-elf fixture's.
[TestClass]
public sealed class ItemDamageStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionObjectBaseline Glass = new() { Kind = DamageableObjectKind.Item, Material = ObjectMaterial.Glass };
    private static readonly SessionWearItem Vase = new("vase", new ItemCondition(10, 0, 0, false));

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new("chest", SessionHolderKind.Container, false, false, [], ["cup", "knife"])],
            [
                new("cup", InventoryStateTests.Facts(SessionItemType.Generic, 5650), null),
                new("knife", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null),
                new("vase", InventoryStateTests.Facts(SessionItemType.Generic, 5651), new CellCoord(101, 200)),
            ]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    [TestMethod]
    public void AnItemTakesTheStoredResistanceAndNoMaterialReduction()
    {
        using var owner = Owner();
        var facts = Glass with { Resistances = new(50, 0, 0, 0) };
        var (defender, kind) = owner.QueryItemCombat(Vase, facts)!.Value;
        Assert.AreEqual((AttackTargetKind.OtherObject, ObjectMaterial.Glass, 50), (kind, defender.Material, defender.Resistances.Normal));
        // OD2/OD3: 10 normal damage, half resisted; glass takes no reduction (only portals and containers do).
        var step = AttackResolver.DamageStep(new() { Attacker = new Combatant(), Target = defender, TargetKind = kind, Damage = DamageByType.Zero.With(DamageType.Normal, 10) });
        Assert.AreEqual(5, step.HitPointDamage);
        Assert.AreEqual(AttackTargetKind.OtherObject, owner.QueryItemCombat(Vase, facts with { Resistances = new(500, 0, 0, 0) })!.Value.Kind);
        Assert.AreEqual(95, owner.QueryItemCombat(Vase, facts with { Resistances = new(500, 0, 0, 0) })!.Value.Combatant.Resistances.Normal, "clamped to 95 as the getter does");
        Assert.IsTrue(owner.QueryItemCombat(Vase with { Invulnerable = true }, facts)!.Value.Combatant.Conditions.HasFlag(CombatantConditions.Invulnerable));
        Assert.IsNull(owner.QueryItemCombat(Vase, facts with { Kind = DamageableObjectKind.Scenery }), "only an item's facts");
        Assert.IsNull(owner.QueryItemCombat(new("vase", new ItemCondition(ItemDurabilityLimits.MaxMagnitude + 1, 0, 0, false)), facts));
    }

    [TestMethod]
    public void DamageToAnItemOnTheGroundWearsItDownAndBreaksItTwice()
    {
        using var owner = Owner();
        var hit = owner.DamageItemUnattacked(owner.Version, Vase, Glass, 4, onGround: true);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, SessionObjectHostSteps.None), (hit.Disposition, hit.Failure, hit.Host));
        Assert.AreEqual(6, hit.Item!.Value.Condition!.Value.HitPoints);
        Assert.AreEqual(4, owner.QueryItemCondition("vase", Vase.Baseline)!.Value.Damage);

        // OD12/DU4: at 0 hit points the first break marks the item, resets its damage and runs its BUST script; the second deletes it.
        var broke = owner.DamageItemUnattacked(owner.Version, Vase, Glass, 6, true);
        Assert.AreEqual(ItemBreakStep.Broken, broke.Item!.Value.Break);
        Assert.AreEqual(SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound, broke.Host);
        var broken = owner.QueryItemCondition("vase", Vase.Baseline)!.Value;
        Assert.AreEqual((0, true), (broken.Damage, broken.Broken));
        var deleted = owner.DamageItemUnattacked(owner.Version, Vase, Glass, 10, true);
        Assert.AreEqual((ItemBreakStep.Deleted, SessionObjectHostSteps.DestroySound), (deleted.Item!.Value.Break, deleted.Host));
        Assert.IsTrue(owner.IsItemDestroyed("vase"));
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable, owner.DamageItemUnattacked(owner.Version, Vase, Glass, 1, true).Failure, "a deleted item takes nothing");
    }

    [TestMethod]
    public void ATrappedItemFiresItsTrapAtTheBreakAndNotBefore()
    {
        using var owner = Owner();
        var trapped = Glass with { Trapped = true };
        Assert.AreEqual(SessionObjectHostSteps.None, owner.DamageItemUnattacked(owner.Version, Vase, trapped, 3, true).Host);
        Assert.AreEqual(SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound,
            owner.DamageItemUnattacked(owner.Version, Vase, trapped, 7, true).Host);
    }

    [TestMethod]
    public void ANegativeAmountOrOneThatLeavesTheItemWholeChangesNothing()
    {
        using var owner = Owner();
        var before = owner.Version;
        var none = owner.DamageItemUnattacked(before, Vase, Glass, -5, true);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, SessionObjectHostSteps.None), (none.Disposition, none.Failure, none.Host));
        Assert.AreEqual(before, owner.Version);
    }

    [TestMethod]
    public void AnItemNotOnTheGroundOrNotTrackedOrStaleIsRefusedBeforeAnyChange()
    {
        using var owner = Owner();
        var version = owner.Version;
        Assert.AreEqual(GameSessionFailure.ObjectUnavailable,
            owner.DamageItemUnattacked(version, new("cup", new(10, 0, 0, false)), Glass, 1, onGround: true).Failure, "an item in a chest is out of reach");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable,
            owner.DamageItemUnattacked(version, new("nowhere", new(10, 0, 0, false)), Glass, 1, onGround: false).Failure, "the session tracks no such item");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DamageItemUnattacked(version, Vase, Glass with { Kind = DamageableObjectKind.Trap }, 1, true).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DamageItemUnattacked(version, new("", Vase.Baseline), Glass, 1, true).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DamageItemUnattacked(new(version.Owner, version.Revision + 1), Vase, Glass, 1, true).Failure);
        Assert.AreEqual(version, owner.Version, "nothing was written");
        // A carried item (not asked for on the ground) is the caller's to name: the cup in the chest takes the damage as an item of a holder would.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DamageItemUnattacked(owner.Version, new("cup", new(10, 0, 0, false)), Glass, 1, onGround: false).Disposition);
    }

    [TestMethod]
    public void AWornItemLeavesItsSlotAtTheFirstBreak()
    {
        using var owner = Owner();
        var blade = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon
            {
                Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 15, MinimumStrength = 6,
                CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            },
        };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, blade), (_, _) => true, Accept).Disposition);
        var item = new SessionWearItem("kit:1", new ItemCondition(20, 0, 0, false));
        var worn = owner.DamageItemUnattacked(owner.Version, item, Glass, 5, onGround: false);
        Assert.AreEqual(15, owner.QueryEquipment().Worn.Single().Item.HitPoints, "the worn record follows the condition");
        Assert.AreEqual(SessionObjectHostSteps.None, worn.Host);
        var broke = owner.DamageItemUnattacked(owner.Version, item, Glass, 15, onGround: false);
        Assert.AreEqual((ItemBreakStep.Broken, (EquipmentSlot?)EquipmentSlot.Weapon), (broke.Item!.Value.Break, broke.Item.Value.Unworn));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
    }

    [TestMethod]
    public void TheDamageIsSavedAsTheItemsDurabilityRecordWithNoNewState()
    {
        using var owner = Owner();
        owner.DamageItemUnattacked(owner.Version, Vase, Glass, 4, true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(4, restored.QueryItemCondition("vase", Vase.Baseline)!.Value.Damage);
    }
}
