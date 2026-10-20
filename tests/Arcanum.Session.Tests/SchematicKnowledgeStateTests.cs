using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's found schematics (ARC-TECHNOLOGY-003). The hero carries two written documents in its kit (kit:1, kit:2) and a
// weapon (kit:3); schematic numbers are invented.
[TestClass]
public sealed class SchematicKnowledgeStateTests
{
    private static readonly SessionHeroKitItem Document = new(new(SessionItemType.Written, 14082, 9001, 14062));
    private static readonly SessionHeroKitItem Note = new(new(SessionItemType.Written, 14083, 9002, 14063));

    private static GameSessionOwner Owner(GameSessionLimits? limits = null) =>
        CreateWith(HalfElf() with { Kit = [Document, Note, Kit[0]] }, limits);

    private static SessionWrittenFacts Schematic(int number) => new(SchematicKnowledgeRules.SchematicSubtype, number);

    private static SessionReadingResult Ok(SessionReadingResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
        return result;
    }

    [TestMethod]
    public void A_new_schematic_is_appended_and_its_document_destroyed()
    {
        using var owner = Owner();
        CollectionAssert.AreEqual(Array.Empty<int>(), owner.QueryHeroFoundSchematics()!.Value.ToArray());
        var read = Ok(owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(4150)), owner, 1);
        Assert.AreEqual(((SchematicReading?)SchematicReading.Registered, (int?)4150, true), (read.Reading, read.Schematic, read.DocumentDestroyed));
        CollectionAssert.AreEqual(new[] { 4150 }, owner.QueryHeroFoundSchematics()!.Value.ToArray());
        Assert.IsTrue(owner.IsItemDestroyed("kit:1"));
        CollectionAssert.AreEqual(new[] { "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Ok(owner.ReadWrittenItem(owner.Version, "kit:2", Schematic(4010)), owner, 2);
        CollectionAssert.AreEqual(new[] { 4150, 4010 }, owner.QueryHeroFoundSchematics()!.Value.ToArray(), "registration order");
    }

    [TestMethod]
    public void A_known_schematic_and_another_subtype_change_nothing_and_keep_the_document()
    {
        using var owner = Owner();
        Ok(owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(4150)), owner, 1);
        var known = owner.ReadWrittenItem(owner.Version, "kit:2", Schematic(4150));
        Assert.AreEqual((GameSessionDisposition.NoChange, (SchematicReading?)SchematicReading.AlreadyKnown, false), (known.Disposition, known.Reading, known.DocumentDestroyed));
        Assert.IsFalse(owner.IsItemDestroyed("kit:2"));
        var book = owner.ReadWrittenItem(owner.Version, "kit:2", new(1, 300));
        Assert.AreEqual((GameSessionDisposition.NoChange, (SchematicReading?)SchematicReading.NotSchematic, (int?)null), (book.Disposition, book.Reading, book.Schematic));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, 1), owner.Version);
    }

    [TestMethod]
    public void A_document_numbered_zero_is_refused_in_steamwoven_and_registered_in_the_original_TC8()
    {
        using var owner = Owner();
        var refused = owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(0));
        Assert.AreEqual((GameSessionDisposition.NoChange, (SchematicReading?)SchematicReading.RefusedZero, false), (refused.Disposition, refused.Reading, refused.DocumentDestroyed));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Ok(owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(0)), owner, 1);
        CollectionAssert.AreEqual(new[] { 0 }, owner.QueryHeroFoundSchematics()!.Value.ToArray());
    }

    [TestMethod]
    public void Readings_are_refused_for_no_or_a_dead_hero_an_item_not_held_or_not_written_and_a_full_list()
    {
        using var none = Create();
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, none.ReadWrittenItem(none.Version, "kit:1", Schematic(4000)).Failure);
        Assert.IsNull(none.QueryHeroFoundSchematics());
        using var owner = Owner(new GameSessionLimits(maxFoundSchematics: 1));
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.ReadWrittenItem(owner.Version, "bad item", Schematic(4000)).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ReadWrittenItem(new(owner.Identity, 9), "kit:1", Schematic(4000)).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.ReadWrittenItem(owner.Version, "kit:9", Schematic(4000)).Failure);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.ReadWrittenItem(owner.Version, "kit:3", Schematic(4000)).Failure);
        Ok(owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(4000)), owner, 1);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.ReadWrittenItem(owner.Version, "kit:2", Schematic(4010)).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(4020)).Failure, "destroyed");
    }

    [TestMethod]
    public void The_found_list_is_saved_in_order_and_malformed_lists_are_refused()
    {
        using var owner = Owner();
        Ok(owner.ReadWrittenItem(owner.Version, "kit:1", Schematic(4150)), owner, 1);
        Ok(owner.ReadWrittenItem(owner.Version, "kit:2", Schematic(4010)), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"schematics\":{\"found\":[4150,4010]},");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.Failure);
        using var back = restored.Owner!;
        CollectionAssert.AreEqual(new[] { 4150, 4010 }, back.QueryHeroFoundSchematics()!.Value.ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));

        GameSessionFailure Restore(string json, GameSessionLimits? limits = null) => GameSessionOwner.Restore(Decode(json), RestoreOptions(limits)).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidSchematics, Restore(text.Replace("[4150,4010]", "[4150,4150]", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, Restore(text, new GameSessionLimits(maxFoundSchematics: 1)));
        using var heroless = Create();
        var plain = GameSessionSnapshotCodec.Encode(heroless.Capture()).Replace(NoSchematics, ",\"schematics\":{\"found\":[4000]}", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidSchematics, Restore(plain));
        Assert.AreEqual(SnapshotDecodeFailure.WrongType, GameSessionSnapshotCodec.TryDecode(text.Replace("[4150,4010]", "[\"4150\"]", StringComparison.Ordinal)).Failure);
    }
}
