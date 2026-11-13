using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1658 (schema 99): the hero's stored resistance array and stored age, saved at the end of the hero section.
[TestClass]
public sealed class HeroStoredFieldsStateTests
{
    private static string HeroText() => GameSessionSnapshotCodec.Encode(CreateWith(HalfElf()).Capture());

    [TestMethod]
    public void A_new_hero_stores_no_resistance_and_no_age_and_they_are_saved_at_the_end_of_its_section()
    {
        var text = HeroText();
        StringAssert.Contains(text, "\"scripts\":{\"flags\":[],\"variables\":[]}" + NoHeroStoredKeys + "}");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, restored.QueryHero()!.Resistances.ToArray());
        Assert.IsNull(restored.QueryHero()!.Age);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void A_stored_age_and_resistances_round_trip_and_a_created_hero_keeps_its_prototypes_age()
    {
        using var owner = CreateWith(HalfElf() with { Age = 31 });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroResistance(owner.Version, 3, int.MinValue).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"resistances\":[0,0,0,-2147483648,0],\"age\":31}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(int.MinValue, restored.QueryHeroStoredResistance(3));
        Assert.AreEqual(31, restored.QueryHeroStoredAge());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void A_schema_98_save_loads_with_zero_resistances_and_an_unread_age_and_the_step_refuses_keys_it_adds()
    {
        var current = HeroText();
        var older = SnapshotUpgradeTests.Schema98(current);
        Assert.IsFalse(older.Contains("\"resistances\""));
        var result = GameSessionSnapshotCodec.TryDecode(older);
        Assert.IsNotNull(result.Snapshot, result.Failure + " " + result.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(result.Snapshot));
        // A schema 98 text that already holds the keys is not one any build wrote.
        var tainted = GameSessionSnapshotCodec.TryDecode(current.Replace("\"schemaVersion\":100,", "\"schemaVersion\":98,"));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, tainted.Failure);
        Assert.AreEqual("$.hero.resistances", tainted.Path);
        // A session without a hero has nothing to add.
        var none = SnapshotUpgradeTests.Schema98(GoldenJson);
        Assert.IsNotNull(GameSessionSnapshotCodec.TryDecode(none).Snapshot);
    }

    [TestMethod]
    public void Decode_and_restore_refuse_a_resistance_array_of_the_wrong_length_and_a_negative_age()
    {
        var current = HeroText();
        var shortArray = GameSessionSnapshotCodec.TryDecode(current.Replace("\"resistances\":[0,0,0,0,0]", "\"resistances\":[0,0,0,0]"));
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, shortArray.Failure);
        Assert.AreEqual("$.hero.resistances", shortArray.Path);
        var negative = GameSessionSnapshotCodec.TryDecode(current.Replace("\"age\":null", "\"age\":-1"));
        Assert.IsNotNull(negative.Snapshot);
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Restore(negative.Snapshot, RestoreOptions()).Failure);
    }
}
