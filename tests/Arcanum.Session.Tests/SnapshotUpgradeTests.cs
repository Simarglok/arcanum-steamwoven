using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-037: older saves load in newer builds through one upgrade step per schema.</summary>
[TestClass]
public sealed class SnapshotUpgradeTests
{
    private const string Current = "\"schemaVersion\":60,\"world\"";

    // The schema 56 text of a schema 59 one without a saved round, whose NPCs were never dressed and that left no pool of blood: the same text without
    // `inventory.npcWorn` and `inventory.bloodPools`.
    private static string Schema56(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, ",\"npcWorn\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}");
        return text.Replace(Current, "\"schemaVersion\":56,\"world\"").Replace(",\"npcWorn\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}", "}");
    }

    [TestMethod]
    public void Every_older_readable_schema_has_a_step_to_the_next()
    {
        CollectionAssert.AreEqual(
            Enumerable.Range(GameSessionSnapshotCodec.OldestReadableSchemaVersion,
                GameSessionSnapshot.CurrentSchemaVersion - GameSessionSnapshotCodec.OldestReadableSchemaVersion).ToArray(),
            GameSessionSnapshotCodec.UpgradableSchemaVersions.ToArray(),
            "A schema bump adds the step from the previous schema (GameSessionSnapshotUpgrade.cs), so the saves of older builds keep loading.");
    }

    // ARC-RENDER-041: the schema 57 text of a schema 59 one without a saved round that left no pool of blood: the same text without `inventory.bloodPools`.
    private static string Schema57(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, ",\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}");
        return text.Replace(Current, "\"schemaVersion\":57,\"world\"").Replace(",\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}", "}");
    }

    // MIG6 (ARC-DEV-084): the schema 59 text of a schema 60 one: the same text without the junk and pool high-water marks.
    private static string Schema59(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, ",\"lastJunkNumber\":0,\"lastPoolNumber\":0}");
        return text.Replace(Current, "\"schemaVersion\":59,\"world\"").Replace(",\"lastJunkNumber\":0,\"lastPoolNumber\":0}", "}");
    }

    // GR19 (ARC-DEV-078): the schema 58 text of a schema 59 one: the same text without the saved round's `combat.subturnStarts`.
    private static string Schema58(string text)
    {
        StringAssert.Contains(text, Current);
        var older = Schema59(text).Replace("\"schemaVersion\":59,\"world\"", "\"schemaVersion\":58,\"world\"");
        var at = older.IndexOf(",\"subturnStarts\":", StringComparison.Ordinal);
        if (at < 0) return older;
        var end = older.IndexOf('}', at);
        return older.Remove(at, end - at);
    }

    [TestMethod]
    public void A_schema_58_save_decodes_as_the_current_schema()
    {
        foreach (var current in new[] { GoldenJson, HeroStateTests.HeroGolden })
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(Decode(Schema58(current))), "no round: only the schema differs");
        // A saved round gets no subturn starts, as the older load restored none.
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() });
        using var owner = result.Owner!;
        var hero = SessionCombatant.Hero;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StartCombatRound(owner.Version, new OneHeroWorld()).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"actionPoints\":9,\"subturnStarts\":1}");
        var older = Schema58(text);
        StringAssert.Contains(older, ",\"actionPoints\":9},");
        var upgraded = Decode(older);
        Assert.AreEqual(0, upgraded.Combat!.SubturnStarts);
        Assert.AreEqual(text.Replace(",\"subturnStarts\":1}", ",\"subturnStarts\":0}"), GameSessionSnapshotCodec.Encode(upgraded));
        // A schema 56 save with a round goes through every step to the same round.
        StringAssert.Contains(older, ",\"npcWorn\":[],\"bloodPools\":[]}");
        var oldest = older.Replace("\"schemaVersion\":58,\"world\"", "\"schemaVersion\":56,\"world\"").Replace(",\"npcWorn\":[],\"bloodPools\":[]}", "}");
        Assert.AreEqual(GameSessionSnapshotCodec.Encode(upgraded), GameSessionSnapshotCodec.Encode(Decode(oldest)));
        using var restored = Restore(upgraded);
        Assert.AreEqual((hero, 9, 0L), (restored.QueryCombatRound()!.Actor, restored.QueryCombatRound()!.ActionPoints, restored.QueryCombatSubturnStarts()));
        // A schema 58 round that already carries the field is refused.
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.combat.subturnStarts"),
            (GameSessionSnapshotCodec.TryDecode(text.Replace(Current, "\"schemaVersion\":58,\"world\"")).Failure,
             GameSessionSnapshotCodec.TryDecode(text.Replace(Current, "\"schemaVersion\":58,\"world\"")).Path));
        // The step needs the combat section the older schema always had.
        var missing = older.Replace(",\"combat\":{", ",\"combatX\":{");
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.combat"), (GameSessionSnapshotCodec.TryDecode(missing).Failure, GameSessionSnapshotCodec.TryDecode(missing).Path));
        var wrong = Schema58(GoldenJson).Replace(",\"combat\":null", ",\"combat\":[]");
        StringAssert.Contains(wrong, ",\"combat\":[]");
        Assert.AreEqual((SnapshotDecodeFailure.WrongType, "$.combat"), (GameSessionSnapshotCodec.TryDecode(wrong).Failure, GameSessionSnapshotCodec.TryDecode(wrong).Path));
    }

    // The hero alone in the square.
    private sealed class OneHeroWorld : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [new(SessionCombatant.Hero, new(0, 0))];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    [TestMethod]
    public void A_schema_59_save_decodes_as_the_current_schema()
    {
        foreach (var current in new[] { GoldenJson, HeroStateTests.HeroGolden })
        {
            var older = Decode(Schema59(current));
            Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, older.SchemaVersion);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(older), "The upgraded save is the current save it stands for.");
            using var restored = Restore(older);
            Assert.AreEqual(0, restored.Capture().Inventory.LastJunkNumber, "The older load's rule: the highest number held now, none here.");
            Assert.AreEqual(0, restored.Capture().Inventory.LastPoolNumber);
        }
        // A schema 59 text that already carries a mark is refused.
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.inventory.lastJunkNumber"),
            (GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(Current, "\"schemaVersion\":59,\"world\"")).Failure,
             GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(Current, "\"schemaVersion\":59,\"world\"")).Path));
    }

    [TestMethod]
    public void A_schema_57_save_decodes_as_the_current_schema()
    {
        foreach (var current in new[] { GoldenJson, HeroStateTests.HeroGolden })
        {
            var older = Decode(Schema57(current));
            Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, older.SchemaVersion);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(older), "The upgraded save is the current save it stands for.");
            using var restored = Restore(older);
            Assert.AreEqual(0, restored.Capture().Inventory.BloodPools.Count, "No death of a schema 57 session left a pool of blood.");
        }
        var text = Schema57(GoldenJson);
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, GameSessionSnapshotCodec.TryDecode(text.Replace(",\"npcWorn\":[]}", ",\"npcWorn\":[],\"bloodPools\":[]}")).Failure);
    }

    [TestMethod]
    public void A_schema_56_save_decodes_as_the_current_schema()
    {
        foreach (var current in new[] { GoldenJson, HeroStateTests.HeroGolden })
        {
            var older = Decode(Schema56(current));
            Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, older.SchemaVersion);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(older), "The upgraded save is the current save it stands for.");
            using var restored = Restore(older);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(restored.Capture()));
            Assert.AreEqual(0, restored.Capture().Inventory.NpcWorn.Count, "No NPC of a schema 56 session was dressed.");
        }
        // Whitespace in the older text does not matter, as in a current one (the plain fixture has no colon inside a value).
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(Decode(Schema56(GoldenJson).Replace(",", " ,\n ").Replace(":", " : "))));
    }

    [TestMethod]
    public void A_schema_56_save_file_loads_and_is_left_as_written()
    {
        var directory = Directory.CreateTempSubdirectory("arc-save-upgrade-").FullName;
        try
        {
            var slot = GameSessionSaveSlot.QuickSave(directory);
            var bytes = Encoding.ASCII.GetBytes(Schema56(GoldenJson));
            File.WriteAllBytes(slot.Path, bytes);
            var read = slot.Read();
            Assert.AreEqual(SaveSlotFailure.None, read.Failure, read.DecodePath);
            Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(read.Snapshot!));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(slot.Path), "Loading never rewrites the older file.");
            // The next save of the loaded session writes the current schema.
            Assert.AreEqual(SaveSlotFailure.None, slot.Write(read.Snapshot!).Failure);
            Assert.AreEqual(GoldenJson, File.ReadAllText(slot.Path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void A_damaged_older_save_is_refused_with_a_typed_failure()
    {
        void Fails(string text, SnapshotDecodeFailure failure, string? path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text);
            Assert.IsNull(result.Snapshot, text);
            Assert.AreEqual(failure, result.Failure, text);
            if (path is not null) Assert.AreEqual(path, result.Path, text);
        }
        var older = Schema56(GoldenJson);
        string With(string from, string to)
        {
            StringAssert.Contains(older, from);
            return older.Replace(from, to);
        }
        var inventory = older.IndexOf(",\"inventory\":{", StringComparison.Ordinal);
        var equipment = older.IndexOf(",\"equipment\":", StringComparison.Ordinal);
        Assert.IsTrue(inventory > 0 && equipment > inventory);
        // The step needs the inventory it extends; it never adds a field the older schema could not hold.
        Fails(older.Remove(inventory, equipment - inventory), SnapshotDecodeFailure.MissingField, "$.inventory");
        Fails(older.Remove(inventory, equipment - inventory).Insert(inventory, ",\"inventory\":[]"), SnapshotDecodeFailure.WrongType, "$.inventory");
        Fails(With(",\"grid\":[]", ",\"grid\":[],\"npcWorn\":[]"), SnapshotDecodeFailure.UnknownField, "$.inventory.npcWorn");
        // After the upgrade every check of the current schema applies.
        Fails(With("\"revision\":2,", "\"revision\":-2,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With(",\"grid\":[]", ""), SnapshotDecodeFailure.MissingField, "$.inventory.grid");
        // A repeated property in an object the step unpacks is found while upgrading, elsewhere by the strict decode at its path.
        Fails(With("\"revision\":2,", "\"revision\":2,\"revision\":2,"), SnapshotDecodeFailure.DuplicateField, "$");
        Fails(With("\"hour\":15}", "\"hour\":15,\"hour\":15}"), SnapshotDecodeFailure.DuplicateField, "$.lighting.hour");
        // A lone surrogate escaped in a property name is damaged text, in an older and in a current save.
        Fails(With("\"revision\":2,", "\"\\uD800\":2,\"revision\":2,"), SnapshotDecodeFailure.MalformedJson, "$");
        Fails(GoldenJson.Replace("\"revision\":2,", "\"\\uD800\":2,\"revision\":2,"), SnapshotDecodeFailure.MalformedJson, "$");
        Fails(With("\"world\":\"11111111-1111-1111-1111-111111111111\"", "\"world\":\"\\uD800\""), SnapshotDecodeFailure.MalformedJson, null);
        Fails(With("\"format\":\"arcanum-steamwoven-session\"", "\"format\":\"other-save\""), SnapshotDecodeFailure.WrongFormat, "$.format");
        // Schemas older than the oldest readable one, and newer ones, stay refused.
        Fails(GoldenJson.Replace(Current, "\"schemaVersion\":55,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(GoldenJson.Replace(Current, "\"schemaVersion\":61,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
    }
}
