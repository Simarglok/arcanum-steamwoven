using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-2371: the weapon's mana store (object field 92) as a session value, and the recharge event of kind 0x17 that restores it
// (ledger BC10, PC11; exe read 2026-11-16). The half-elf fixture's kit:2 and kit:3 stand for worn items; values are invented.
[TestClass]
public sealed class ManaStoreStateTests
{
    private const string Staff = "kit:2", Ring = "kit:3";

    private sealed class World : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Owner() => CreateWith(HalfElf());

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void A_written_store_is_the_sessions_copy_over_the_items_own_field()
    {
        using var owner = Owner();
        Assert.IsNull(owner.QueryItemManaStore(Staff), "an item whose store nothing changed holds the prototype's value");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteItemManaStore(owner.Version, Staff, 30, 30).Disposition, "no copy for the same value");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 30, 22).Disposition);
        Assert.AreEqual(22, owner.QueryItemManaStore(Staff));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 99, 0).Disposition, "the baseline matters until the copy exists");
        Assert.AreEqual(0, owner.QueryItemManaStore(Staff));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 0, 500).Disposition, "no cap at the prototype's value");
        Assert.AreEqual(500, owner.QueryItemManaStore(Staff));
    }

    [TestMethod]
    public void A_store_write_refuses_bad_input_and_items_the_session_does_not_track()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.WriteItemManaStore(owner.Version, "", 1, 0).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteItemManaStore(owner.Version with { Revision = 9 }, Staff, 1, 0).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.WriteItemManaStore(owner.Version, "kit:99", 1, 0).Failure);
        Assert.AreEqual(0, owner.Version.Revision, "a refusal changes nothing");
    }

    [TestMethod]
    public void A_drain_schedules_the_recharge_480_game_seconds_on_and_a_second_one_merges_into_it()
    {
        using var owner = Owner();
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        owner.ScheduleManaRecharge(Staff, 6, merge: true);
        Assert.AreEqual((Staff, 6, now, now + 480_000L), owner.QueryManaRecharges().Single());
        Advance(owner, 100_000);
        owner.ScheduleManaRecharge(Staff, 4, merge: true);
        Assert.AreEqual((Staff, 10, now, now + 480_000L), owner.QueryManaRecharges().Single(), "merged: the amount adds, the stamp and the deadline stay");
        owner.ScheduleManaRecharge(Staff, 0, merge: true);
        Assert.AreEqual((Staff, 10, now, now + 480_000L), owner.QueryManaRecharges().Single(), "a paid 0 merges a 0");
        owner.ScheduleManaRecharge(Ring, 3, merge: true);
        Assert.AreEqual(2, owner.QueryManaRecharges().Count, "another item has an event of its own");
        owner.ScheduleManaRecharge(Staff, 5, merge: false);
        Assert.AreEqual(3, owner.QueryManaRecharges().Count, "without merging a second event stands beside the first");
    }

    [TestMethod]
    public void The_event_restores_up_to_eight_points_each_480_seconds_and_sets_the_rest_up_again()
    {
        using var owner = Owner();
        owner.WriteItemManaStore(owner.Version, Staff, 30, 10);
        owner.ScheduleManaRecharge(Staff, 20, merge: true);
        Advance(owner, 479_999);
        owner.ProcessEvents(new World());
        Assert.AreEqual(10, owner.QueryItemManaStore(Staff), "not due a millisecond early");
        Advance(owner, 1);
        owner.ProcessEvents(new World());
        Assert.AreEqual(18, owner.QueryItemManaStore(Staff), "480 s = 4 units of 120 s: 2 × 4 points");
        var rest = owner.QueryManaRecharges().Single();
        Assert.AreEqual(12, rest.Pending);
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs, rest.StampMs, "the rest has a fresh stamp");
        Advance(owner, 480_000);
        owner.ProcessEvents(new World());
        Advance(owner, 480_000);
        owner.ProcessEvents(new World());
        Assert.AreEqual(30, owner.QueryItemManaStore(Staff), "8 + 8 + 4: the pending 20 is back, above nothing (no cap)");
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
    }

    [TestMethod]
    public void A_late_event_restores_more_up_to_what_is_pending()
    {
        using var owner = Owner();
        owner.WriteItemManaStore(owner.Version, Staff, 30, 0);
        owner.ScheduleManaRecharge(Staff, 30, merge: true);
        Advance(owner, 3_600_000);
        owner.ProcessEvents(new World());
        Assert.AreEqual(30, owner.QueryItemManaStore(Staff), "an hour: 2 × 30 points offered, 30 pending");
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
    }

    [TestMethod]
    public void A_zero_amount_event_and_one_for_an_untouched_store_end_without_a_write()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Staff, 0, merge: true);
        Advance(owner, 480_000);
        var before = owner.Version;
        owner.ProcessEvents(new World());
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
        Assert.IsNull(owner.QueryItemManaStore(Staff));
        Assert.AreEqual(before, owner.Version, "nothing was written");
    }

    [TestMethod]
    public void The_store_and_the_pending_event_are_saved_restored_and_go_with_a_deleted_item()
    {
        using var owner = Owner();
        owner.WriteItemManaStore(owner.Version, Ring, 30, 7);
        owner.WriteItemManaStore(owner.Version, Staff, 30, 12);
        owner.ScheduleManaRecharge(Staff, 5, merge: true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Listed = "\"manaStores\":[{\"entity\":\"kit:2\",\"store\":12},{\"entity\":\"kit:3\",\"store\":7}]";
        StringAssert.Contains(text, Listed);
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions()).Owner!;
        using var _ = restored;
        Assert.AreEqual(12, restored.QueryItemManaStore(Staff));
        Assert.AreEqual(7, restored.QueryItemManaStore(Ring));
        Assert.AreEqual(owner.QueryManaRecharges().Single(), restored.QueryManaRecharges().Single());

        void Fails(string to, SnapshotDecodeFailure failure) =>
            Assert.AreEqual(failure, GameSessionSnapshotCodec.TryDecode(text.Replace(Listed, to, StringComparison.Ordinal)).Failure, to);
        Fails("\"manaStores\":[{\"entity\":\"kit:3\",\"store\":7},{\"entity\":\"kit:2\",\"store\":12}]", SnapshotDecodeFailure.NotCanonical);
        Fails("\"manaStores\":[{\"entity\":\"kit:2\"}]", SnapshotDecodeFailure.MissingField);
        Fails("\"manaStores\":[{\"entity\":\"kit:2\",\"store\":2147483648}]", SnapshotDecodeFailure.OutOfRange);
        Fails("\"manaStores\":[{\"entity\":\"kit:2\",\"store\":1,\"extra\":1}]", SnapshotDecodeFailure.UnknownField);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, Staff, new(20, 0, 0, false)).Disposition);
        Assert.IsNull(owner.QueryItemManaStore(Staff), "the copy goes with the item");
        Assert.AreEqual(0, owner.QueryManaRecharges().Count, "and so does its event");
    }

    [TestMethod]
    public void A_schema_102_save_upgrades_with_no_mana_stores_and_a_damaged_step_is_refused()
    {
        using var owner = Owner();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema102(current);
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.IsNotNull(upgraded.Snapshot, $"{upgraded.Failure} {upgraded.Path}");
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot.SchemaVersion);
        Assert.AreEqual(0, upgraded.Snapshot.Durability.ManaStores.Count);
        var twice = older.Replace("\"charges\":[]", "\"charges\":[],\"manaStores\":[]");
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, GameSessionSnapshotCodec.TryDecode(twice).Failure, "a schema 102 text that already has the list");
    }

    // ARC-MAGIC-2401 (BC10): the store is the original's plain 32-bit value, not a count held within 0..1,000,000.
    [TestMethod]
    public void The_store_is_a_full_32_bit_value_a_negative_one_and_one_above_a_million_included()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 30, -5).Disposition);
        Assert.AreEqual(-5, owner.QueryItemManaStore(Staff), "a negative store is held");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 30, SessionItemUseLimits.MaxUses + 1).Disposition);
        Assert.AreEqual(1_000_001, owner.QueryItemManaStore(Staff), "no ceiling at a million");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 30, int.MinValue).Disposition);
        Assert.AreEqual(int.MinValue, owner.QueryItemManaStore(Staff));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, Staff, 30, int.MaxValue).Disposition);
        Assert.AreEqual(int.MaxValue, owner.QueryItemManaStore(Staff));
    }

    [TestMethod]
    public void A_negative_pending_amount_restores_the_old_negative_store_and_schedules_nothing()
    {
        using var owner = Owner();
        // The payment wrote 0 over a store of -5 and passed -5 to the recharge (0x459590): the handler adds min(pending, 2 x units) = -5.
        owner.WriteItemManaStore(owner.Version, Staff, -5, 0);
        owner.ScheduleManaRecharge(Staff, -5, merge: true);
        Assert.AreEqual(-5, owner.QueryManaRecharges().Single().Pending);
        Advance(owner, 480_000);
        owner.ProcessEvents(new World());
        Assert.AreEqual(-5, owner.QueryItemManaStore(Staff), "the store is back at its old negative value");
        Assert.AreEqual(0, owner.QueryManaRecharges().Count, "nothing is scheduled again for a negative rest");
    }

    [TestMethod]
    public void A_negative_pending_amount_that_merges_with_a_positive_one_adds_as_32_bit_values()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Staff, 6, merge: true);
        owner.ScheduleManaRecharge(Staff, -9, merge: true);
        Assert.AreEqual(-3, owner.QueryManaRecharges().Single().Pending);
        owner.ScheduleManaRecharge(Ring, int.MaxValue, merge: true);
        owner.ScheduleManaRecharge(Ring, 2, merge: true);
        Assert.AreEqual(int.MinValue + 1, owner.QueryManaRecharges().Single(e => e.Entity == Ring).Pending, "the original's 32-bit add wraps (0x459640)");
    }

    [TestMethod]
    public void A_recharge_above_a_million_is_held_and_a_negative_store_and_pending_amount_are_saved_and_restored()
    {
        using var owner = Owner();
        owner.WriteItemManaStore(owner.Version, Staff, 30, -7);
        owner.WriteItemManaStore(owner.Version, Ring, 30, 2_000_000);
        owner.ScheduleManaRecharge(Staff, -4, merge: true);
        owner.ScheduleManaRecharge(Ring, 3_000_000, merge: true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"manaStores\":[{\"entity\":\"kit:2\",\"store\":-7},{\"entity\":\"kit:3\",\"store\":2000000}]");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions()).Owner!;
        using var _ = restored;
        Assert.AreEqual(-7, restored.QueryItemManaStore(Staff));
        Assert.AreEqual(2_000_000, restored.QueryItemManaStore(Ring));
        CollectionAssert.AreEqual(owner.QueryManaRecharges().ToArray(), restored.QueryManaRecharges().ToArray());
        Assert.AreEqual(-4, restored.QueryManaRecharges().Single(e => e.Entity == Staff).Pending);
        Assert.AreEqual(3_000_000, restored.QueryManaRecharges().Single(e => e.Entity == Ring).Pending);
        // The ring recharges past a million: no ceiling.
        Advance(restored, 480_000);
        restored.ProcessEvents(new World());
        Assert.AreEqual(2_000_008, restored.QueryItemManaStore(Ring));
    }
}
