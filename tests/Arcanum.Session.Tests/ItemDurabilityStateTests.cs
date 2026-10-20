using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's item durability (ARC-WORLD-053). The kit is the half-elf fixture's (kit:1 a weapon, kit:2 and kit:3 armour), the
// map seed InventoryStateTests'; conditions are invented.
[TestClass]
public sealed class ItemDurabilityStateTests
{
    private static readonly ItemCondition Baseline = new(15, 5, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static readonly EquipmentItem Blade = new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 20,
        MaxHitPoints = 20,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 15, MinimumStrength = 6,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
        },
    };

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Failure);
        return owner;
    }

    private static ItemDurabilityResult Ok(ItemDurabilityResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(ItemDurabilityResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    [TestMethod]
    public void An_item_keeps_its_baseline_until_damaged_and_then_the_record()
    {
        using var owner = Owner();
        Assert.AreEqual(Baseline, owner.QueryItemCondition("sword", Baseline));
        var hit = Ok(owner.DamageItem(owner.Version, "sword", Baseline, 6), owner, 1);
        Assert.AreEqual((ItemBreakStep.None, (EquipmentSlot?)null), (hit.Break, hit.Unworn));
        Assert.AreEqual(Baseline with { Damage = 6 }, owner.QueryItemCondition("sword", Baseline));
        // The record wins over any later baseline the host passes.
        Assert.AreEqual(Baseline with { Damage = 6 }, owner.QueryItemCondition("sword", Baseline with { Points = 99 }));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DamageItem(owner.Version, "sword", Baseline, 0).Disposition);
        Assert.AreEqual(1, owner.QueryDurability().Items.Count);
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryItemCondition("", Baseline));
    }

    [TestMethod]
    public void Calls_are_refused_for_bad_input_a_stale_version_and_items_the_session_does_not_track()
    {
        using var owner = Owner();
        Refused(owner.DamageItem(owner.Version, "sword", Baseline, -1), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.DamageItem(owner.Version, "sword", Baseline with { Damage = -1 }, 1), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.DamageItem(owner.Version, "bad item", Baseline, 1), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.DamageItem(new(owner.Identity, 7), "sword", Baseline, 1), owner, GameSessionFailure.StaleVersion, 0);
        Refused(owner.DamageItem(owner.Version, "elsewhere", Baseline, 1), owner, GameSessionFailure.ItemNotAvailable, 0);
        Refused(owner.RepairItem(owner.Version, "sword", Baseline, 0, 5, RepairTraining.Master), owner, GameSessionFailure.InvalidItem, 0);
        Refused(owner.RepairItem(owner.Version, "sword", Baseline, 1, 101, RepairTraining.Master), owner, GameSessionFailure.InvalidItem, 0);
    }

    [TestMethod]
    public void A_worn_item_follows_its_condition_and_a_first_break_takes_it_off()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Disposition);
        var condition = new ItemCondition(20, 0, 0, false);
        Ok(owner.DamageItem(owner.Version, "kit:1", condition, 5), owner, 2);
        var worn = owner.QueryEquipment().Worn.Single().Item;
        Assert.AreEqual((15, 20, false), (worn.HitPoints, worn.MaxHitPoints, worn.Broken));

        var broke = Ok(owner.DamageItem(owner.Version, "kit:1", condition, 15), owner, 3);
        Assert.AreEqual((ItemBreakStep.Broken, (EquipmentSlot?)EquipmentSlot.Weapon), (broke.Break, broke.Unworn));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);
        Assert.AreEqual(condition with { Broken = true, InventoryArtBroken = true }, broke.Condition);
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"), "a broken item stays in the inventory");

        // The wear check reads the session's broken bit whatever the host's data say.
        var again = owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade), (_, _) => true, Accept);
        Assert.AreEqual((GameSessionFailure.WearRefused, EquipmentRefusal.Broken), (again.Failure, again.Refusal));
    }

    [TestMethod]
    public void A_second_break_deletes_the_item_from_every_list_and_seed()
    {
        using var owner = Owner();
        Ok(owner.BreakItem(owner.Version, "kit:2", Baseline), owner, 1);
        var deleted = Ok(owner.DamageItem(owner.Version, "kit:2", Baseline, 20), owner, 2);
        Assert.AreEqual((ItemBreakStep.Deleted, (ItemCondition?)null), (deleted.Break, deleted.Condition));
        Assert.IsTrue(owner.IsItemDestroyed("kit:2"));
        Assert.IsNull(owner.QueryItemCondition("kit:2", Baseline));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Refused(owner.DamageItem(owner.Version, "kit:2", Baseline, 1), owner, GameSessionFailure.ItemNotAvailable, 2);

        // A seed item: deleted at once (the destroying critical), gone from its holder's list and from loot.
        Ok(owner.DestroyItem(owner.Version, "helmet", Baseline), owner, 3);
        var corpse = owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "corpse");
        Assert.IsFalse(corpse.Items.Any(item => item.Entity == "helmet"));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "corpse", "helmet", Accept).Failure);
        CollectionAssert.AreEqual(new[] { "helmet", "kit:2" }, owner.QueryDurability().Destroyed.ToArray());
        Assert.AreEqual(0, owner.QueryDurability().Items.Count, "a deleted item keeps no record");
    }

    [TestMethod]
    public void A_repair_writes_the_rule_and_reports_what_it_did_not_do()
    {
        using var owner = Owner();
        var none = owner.RepairItem(owner.Version, "sword", Baseline, 5, 10, RepairTraining.None);
        Assert.AreEqual((GameSessionDisposition.NoChange, (ItemRepairStatus?)ItemRepairStatus.NothingToRepair), (none.Disposition, none.Repair));
        Ok(owner.DamageItem(owner.Version, "sword", Baseline, 10), owner, 1);
        var repaired = Ok(owner.RepairItem(owner.Version, "sword", Baseline, 4, 10, RepairTraining.None), owner, 2);
        Assert.AreEqual((ItemRepairStatus?)ItemRepairStatus.Repaired, repaired.Repair);
        Assert.AreEqual(Baseline with { Damage = 6, Adjustment = 3 }, repaired.Condition, "hp_adj 5 − 20 × 10 / 100");

        Ok(owner.BreakItem(owner.Version, "vest", Baseline), owner, 3);
        var refused = owner.RepairItem(owner.Version, "vest", Baseline, 5, 5, RepairTraining.Expert);
        Assert.AreEqual((GameSessionDisposition.NoChange, (ItemRepairStatus?)ItemRepairStatus.NothingToRepair), (refused.Disposition, refused.Repair));
        var mended = Ok(owner.RepairItem(owner.Version, "vest", Baseline, 5, 5, RepairTraining.Master), owner, 4);
        Assert.AreEqual((false, true), (mended.Condition!.Value.Broken, mended.Condition.Value.InventoryArtBroken));
    }

    [TestMethod]
    public void Durability_is_saved_in_canonical_order_and_restored()
    {
        using var owner = Owner();
        Ok(owner.DamageItem(owner.Version, "sword", Baseline, 3), owner, 1);
        Ok(owner.BreakItem(owner.Version, "apple", Baseline), owner, 2);
        Ok(owner.DestroyItem(owner.Version, "kit:3", Baseline), owner, 3);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"durability\":{\"items\":[" +
            "{\"entity\":\"apple\",\"points\":15,\"adjustment\":5,\"damage\":0,\"broken\":true,\"inventoryArtBroken\":true}," +
            "{\"entity\":\"sword\",\"points\":15,\"adjustment\":5,\"damage\":3,\"broken\":false,\"inventoryArtBroken\":false}]," +
            "\"destroyed\":[\"kit:3\"]},\"effects\":");
        StringAssert.Contains(text, "\"schemaVersion\":24,");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.Failure);
        using var back = restored.Owner!;
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));
        Assert.AreEqual(GameSessionFailure.None, back.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Failure);
        Assert.AreEqual(Baseline with { Damage = 3 }, back.QueryItemCondition("sword", Baseline));
        Assert.IsTrue(back.IsItemDestroyed("kit:3"));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2" }, back.QueryHeroKit().Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void Malformed_durability_is_refused()
    {
        using var owner = Owner();
        Ok(owner.DamageItem(owner.Version, "sword", Baseline, 3), owner, 1);
        Ok(owner.DamageItem(owner.Version, "vest", Baseline, 3), owner, 2);
        Ok(owner.DestroyItem(owner.Version, "kit:2", Baseline), owner, 3);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        string With(string from, string to)
        {
            Assert.IsTrue(text.Contains(from, StringComparison.Ordinal), from);
            return text.Replace(from, to, StringComparison.Ordinal);
        }
        void DecodeFails(string json, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(json);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        GameSessionFailure Restore(string json) => GameSessionOwner.Restore(Decode(json), RestoreOptions()).Failure;

        // Decode: order, ranges, fields.
        DecodeFails(With("{\"entity\":\"sword\"", "{\"entity\":\"aaa\"").Replace("{\"entity\":\"vest\"", "{\"entity\":\"aab\"")
            .Replace("\"aaa\"", "\"zzz\""), SnapshotDecodeFailure.NotCanonical, "$.durability.items[1].entity");
        DecodeFails(With("\"damage\":3,\"broken\":false,\"inventoryArtBroken\":false},{\"entity\":\"vest\"",
            "\"damage\":-1,\"broken\":false,\"inventoryArtBroken\":false},{\"entity\":\"vest\""), SnapshotDecodeFailure.OutOfRange, "$.durability.items[0].damage");
        DecodeFails(With("\"destroyed\":[\"kit:2\"]", "\"destroyed\":[\"kit:2\",\"kit:2\"]"), SnapshotDecodeFailure.NotCanonical, "$.durability.destroyed[1]");
        DecodeFails(With(",\"inventoryArtBroken\":false}", "}"), SnapshotDecodeFailure.MissingField, "$.durability.items[0].inventoryArtBroken");

        // Restore: a deleted item with a record, one still in the kit, a deleted kit number skipped wrongly, a broken worn item.
        Assert.AreEqual(GameSessionFailure.InvalidDurability, Restore(With("\"destroyed\":[\"kit:2\"]", "\"destroyed\":[\"kit:2\",\"sword\"]")));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(With("\"destroyed\":[\"kit:2\"]", "\"destroyed\":[\"kit:1\",\"kit:2\"]")));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(With("\"destroyed\":[\"kit:2\"]", "\"destroyed\":[]")),
            "the kit is kit:1 and kit:3: only the deleted list explains the gap");
        Assert.AreEqual(GameSessionFailure.None, Restore(text));
    }

    [TestMethod]
    public void A_worn_items_record_must_agree_with_the_worn_values()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade),
            (_, _) => true, Accept).Disposition);
        Ok(owner.DamageItem(owner.Version, "kit:1", new(20, 0, 0, false), 5), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(text), RestoreOptions()).Failure);
        var broken = text.Replace("\"damage\":5,\"broken\":false", "\"damage\":5,\"broken\":true", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidDurability, GameSessionOwner.Restore(Decode(broken), RestoreOptions()).Failure);
        var other = text.Replace("\"damage\":5,\"broken\":false", "\"damage\":6,\"broken\":false", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidDurability, GameSessionOwner.Restore(Decode(other), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void A_deleted_or_repaired_worn_item_and_the_best_item_choice_follow_the_record()
    {
        using var owner = Owner();
        var blade = new SessionWearableItem(Kit[0].Kind, [], false, Blade);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, blade, (_, _) => true, Accept).Disposition);
        var condition = new ItemCondition(15, 5, 0, false);
        Ok(owner.DamageItem(owner.Version, "kit:1", condition, 10), owner, 2);
        // A repair lowers hp_adj, so the maximum the worn values carry falls with it.
        Ok(owner.RepairItem(owner.Version, "kit:1", condition, 4, 10, RepairTraining.None), owner, 3);
        var worn = owner.QueryEquipment().Worn.Single().Item;
        Assert.AreEqual((12, 18), (worn.HitPoints, worn.MaxHitPoints), "damage 6, hp_adj 5 − 2");
        var gone = Ok(owner.DestroyItem(owner.Version, "kit:1", condition), owner, 4);
        Assert.AreEqual((ItemBreakStep.Deleted, (EquipmentSlot?)EquipmentSlot.Weapon), (gone.Break, gone.Unworn));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count);

        // The best-item choice reads the session's broken bit: a broken blade is not wielded.
        using var chooser = Owner();
        SessionWearableLookup lookup = entity => entity == "kit:1" ? blade : null;
        Ok(chooser.BreakItem(chooser.Version, "kit:1", condition), chooser, 1);
        chooser.EquipBest(chooser.Version, lookup, (_, _) => true, Accept);
        Assert.AreEqual(0, chooser.QueryEquipment().Worn.Count);
        using var whole = Owner();
        whole.EquipBest(whole.Version, lookup, (_, _) => true, Accept);
        Assert.AreEqual("kit:1", whole.QueryEquipment().Worn.Single().Entity);
    }

    [TestMethod]
    public void The_record_budget_and_a_deleted_pc_item_are_refused()
    {
        using var owner = Create(new GameSessionLimits(maxDurabilityRecords: 1));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Failure);
        Ok(owner.DamageItem(owner.Version, "sword", Baseline, 1), owner, 1);
        Ok(owner.DamageItem(owner.Version, "sword", Baseline, 1), owner, 2);
        Refused(owner.DamageItem(owner.Version, "vest", Baseline, 1), owner, GameSessionFailure.BudgetExceeded, 2);

        using var picker = Owner();
        Assert.AreEqual(GameSessionFailure.None, picker.PickUpItem(picker.Version, "apple", Accept).Failure);
        var text = GameSessionSnapshotCodec.Encode(picker.Capture());
        var listed = text.Replace("\"destroyed\":[]", "\"destroyed\":[\"apple\"]", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(listed), RestoreOptions()).Failure);
        var over = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"destroyed\":[]", "\"destroyed\":[\"x\"]", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(Decode(over), RestoreOptions(new GameSessionLimits(maxDurabilityRecords: 1))).Failure,
            "two records over a limit of one");
    }
}
