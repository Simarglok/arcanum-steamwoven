using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-037: older saves load in newer builds through one upgrade step per schema.</summary>
[TestClass]
public sealed class SnapshotUpgradeTests
{
    private const string Current = "\"schemaVersion\":58,\"world\"";

    // The schema 56 text of a schema 58 one whose NPCs were never dressed and that left no pool of blood: the same text without
    // `inventory.npcWorn` and `inventory.bloodPools`.
    private static string Schema56(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, ",\"npcWorn\":[],\"bloodPools\":[]}");
        return text.Replace(Current, "\"schemaVersion\":56,\"world\"").Replace(",\"npcWorn\":[],\"bloodPools\":[]}", "}");
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

    // ARC-RENDER-041: the schema 57 text of a schema 58 one that left no pool of blood: the same text without `inventory.bloodPools`.
    private static string Schema57(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, ",\"bloodPools\":[]}");
        return text.Replace(Current, "\"schemaVersion\":57,\"world\"").Replace(",\"bloodPools\":[]}", "}");
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
        Fails(GoldenJson.Replace(Current, "\"schemaVersion\":59,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
    }
}
