using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-037: older saves load in newer builds through one upgrade step per schema.</summary>
[TestClass]
public sealed class SnapshotUpgradeTests
{
    private const string Current = "\"schemaVersion\":67,\"world\"";
    private const string Current66 = "\"schemaVersion\":66,\"world\"";
    private const string Current65 = "\"schemaVersion\":65,\"world\"";
    private const string EmptyScripts = "\"scripts\":{\"globalVariables\":[],\"pcFlags\":[],\"pcVariables\":[]},";
    private const string Current64 = "\"schemaVersion\":64,\"world\"";
    private const string Current63 = "\"schemaVersion\":63,\"world\"";
    private const string Current62 = "\"schemaVersion\":62,\"world\"";
    private const string Current61 = "\"schemaVersion\":61,\"world\"";
    private const string ObjectsSection = "},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[]}";
    private const string ObjectsSection61 = "},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}";

    // MIG8 (ARC-DEV-098): the schema 66 text of a schema 67 one with no script store written: the same text without the `scripts` section. A text with
    // a store has its own hand-made schema 66 literal (ScriptStoreStateTests).
    internal static string Schema66(string text)
    {
        StringAssert.Contains(text, Current);
        StringAssert.Contains(text, EmptyScripts);
        return text.Replace(Current, Current66).Replace(EmptyScripts, "");
    }

    // MIG8 (ARC-DEV-096): the schema 65 text of a schema 66 one with no script attachment: the layout is the same (the attachments were map changes in
    // the maps section, and a text with none is the same text), so the inverse is the schema number alone. A text with attachments has its own hand-made
    // schema 65 literal (ScriptAttachmentStateTests).
    internal static string Schema65(string text)
    {
        var older = Schema66(text);
        StringAssert.Contains(older, Current66);
        return older.Replace(Current66, Current65);
    }

    // MIG7d (ARC-DEV-095): the schema 64 text of a schema 65 one with no link: the same text without `objects.links` (the danger records of a text with
    // no link are the same in both layouts). A text with links has its own hand-made schema 64 literal (ObjectLinkTests).
    internal static string Schema64(string text)
    {
        var older = Schema65(text);
        StringAssert.Contains(older, Current65);
        StringAssert.Contains(older, ",\"links\":[]}");
        return older.Replace(Current65, Current64).Replace(",\"links\":[]}", "}");
    }

    // MIG7c (ARC-DEV-094): the schema 63 text of a schema 64 one with no `npc.pos` field: the layout is the same (a text with no such field is the same
    // text), so the inverse is the schema number alone.
    internal static string Schema63(string text)
    {
        var older = Schema64(text);
        StringAssert.Contains(older, Current64);
        return older.Replace(Current64, Current63);
    }

    // MIG7b (ARC-DEV-093): the schema 62 text of a schema 63 one with no static object field: the layout is the same (the state rows of schema 62 are the
    // mobile objects', and a text with no object field is the same text), so the inverse is the schema number alone.
    internal static string Schema62(string text)
    {
        var older = Schema63(text);
        StringAssert.Contains(older, Current63);
        return older.Replace(Current63, Current62);
    }

    // MIG7a (ARC-DEV-092): the schema 61 text of a schema 62 one with no object field: the same text without `objects.state` (a hand-made
    // inverse of the upgrade step, so the step is checked against the older layout and not against itself).
    internal static string Schema61(string text)
    {
        var older = Schema62(text);
        StringAssert.Contains(older, Current62);
        StringAssert.Contains(older, ObjectsSection);
        return older.Replace(Current62, Current61).Replace(ObjectsSection, ObjectsSection61);
    }

    // MIG6d-3 (ARC-DEV-091): the schema 60 text of a schema 61 one with no junk pile and no pool: the junk piles, the pools and the two marks
    // inside the inventory section, written the way the schema 60 build wrote them (a hand-made inverse of the upgrade step, so the step
    // is checked against the older layout and not against itself).
    internal static string Schema60(string text)
    {
        var older = Schema61(text);
        StringAssert.Contains(older, Current61);
        StringAssert.Contains(older, ObjectsSection61);
        StringAssert.Contains(older, ",\"identified\":[");
        return older.Replace(Current61, "\"schemaVersion\":60,\"world\"")
            .Replace(",\"identified\":[", ",\"junkPiles\":[],\"identified\":[")
            .Replace(ObjectsSection61, ",\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}");
    }

    // The schema 56 text of a schema 59 one without a saved round, whose NPCs were never dressed and that left no pool of blood: the same text without
    // `inventory.npcWorn` and `inventory.bloodPools`.
    private static string Schema56(string text) =>
        Schema57(text).Replace("\"schemaVersion\":57,\"world\"", "\"schemaVersion\":56,\"world\"").Replace(",\"npcWorn\":[]}", "}");

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
    private static string Schema57(string text) =>
        Schema59(text).Replace("\"schemaVersion\":59,\"world\"", "\"schemaVersion\":57,\"world\"").Replace(",\"bloodPools\":[]}", "}");

    // MIG6 (ARC-DEV-084): the schema 59 text of a schema 60 one: the same text without the junk and pool high-water marks.
    private static string Schema59(string text) =>
        Schema60(text).Replace("\"schemaVersion\":60,\"world\"", "\"schemaVersion\":59,\"world\"").Replace(",\"lastJunkNumber\":0,\"lastPoolNumber\":0}", "}");

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
            Assert.AreEqual(0, restored.Capture().Objects.LastJunkNumber, "The older load's rule: the highest number held now, none here.");
            Assert.AreEqual(0, restored.Capture().Objects.LastPoolNumber);
        }
        // A schema 59 text that already carries a mark is refused.
        var withMark = Schema59(GoldenJson).Replace(",\"bloodPools\":[]}", ",\"bloodPools\":[],\"lastJunkNumber\":0}");
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.inventory.lastJunkNumber"),
            (GameSessionSnapshotCodec.TryDecode(withMark).Failure, GameSessionSnapshotCodec.TryDecode(withMark).Path));
    }

    // MIG6d-3 (ARC-DEV-091): a schema 60 save keeps its junk piles, pools and marks in the inventory section; the step moves them to the
    // objects section as they were, in order, and the saved numbers stay.
    [TestMethod]
    public void A_schema_60_save_decodes_as_the_current_schema()
    {
        foreach (var current in new[] { GoldenJson, HeroStateTests.HeroGolden })
        {
            var older = Schema60(current);
            StringAssert.Contains(older, "\"schemaVersion\":60");
            var upgraded = Decode(older);
            Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded), "The upgraded save is the current save it stands for.");
            using var restored = Restore(upgraded);
            Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(restored.Capture()));
        }
        // A save with rows in all four fields, written as the schema 60 build wrote it (a literal, not made by the code under test): the
        // junk pile, the pool of blood and the marks (a higher mark than the held numbers) reach the objects section unchanged.
        const string Pile = "{\"entity\":\"junk:2\",\"map\":79,\"x\":120,\"y\":210,\"items\":[{\"entity\":\"kit:2\",\"slot\":0,\"width\":1,\"height\":1}," +
            "{\"entity\":\"apple\",\"slot\":1,\"width\":2,\"height\":1}]}";
        const string Pool = "{\"entity\":\"pool:3\",\"critter\":\"oid:wolf\",\"deathTimeMs\":10,\"map\":79,\"x\":105,\"y\":200,\"offsetX\":12,\"offsetY\":-7,\"createdMs\":20}";
        var populated = Schema60(GoldenJson)
            .Replace(",\"junkPiles\":[]", ",\"junkPiles\":[" + Pile + "]")
            .Replace(",\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0}", ",\"bloodPools\":[" + Pool + "],\"lastJunkNumber\":5,\"lastPoolNumber\":7}");
        var snapshot = Decode(populated);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        CollectionAssert.AreEqual(new[] { "junk:2" }, snapshot.Objects.JunkPiles.Select(pile => pile.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { ("kit:2", 0, 1, 1), ("apple", 1, 2, 1) }, snapshot.Objects.JunkPiles[0].Items.Select(item => (item.Entity, item.Slot, item.Width, item.Height)).ToArray());
        CollectionAssert.AreEqual(new[] { "pool:3" }, snapshot.Objects.BloodPools.Select(pool => pool.Entity).ToArray());
        Assert.AreEqual((5, 7), (snapshot.Objects.LastJunkNumber, snapshot.Objects.LastPoolNumber));
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        StringAssert.Contains(text, ",\"objects\":{\"junkPiles\":[" + Pile + "],\"bloodPools\":[" + Pool + "],\"lastJunkNumber\":5,\"lastPoolNumber\":7,\"state\":[],\"links\":[]}");
        Assert.AreEqual(-1, text.IndexOf("\"junkPiles\"", text.IndexOf("\"inventory\"", StringComparison.Ordinal), text.IndexOf("\"objects\"", StringComparison.Ordinal) - text.IndexOf("\"inventory\"", StringComparison.Ordinal), StringComparison.Ordinal),
            "the inventory section no longer carries them");
        // The loaded session holds the records and never gives a number at or below the saved marks.
        using (var loaded = Restore(snapshot))
        {
            CollectionAssert.AreEqual(new[] { "junk:2" }, loaded.QueryJunkPiles().Select(pile => pile.Entity).ToArray());
            Assert.AreEqual("junk:6", loaded.NextObjectEntity(SessionObjectOrigin.JunkPile));
            Assert.AreEqual("pool:8", loaded.NextObjectEntity(SessionObjectOrigin.BloodPool));
        }
        // Damaged older saves: each of the four fields the step moves must be there, and the objects section cannot exist yet.
        var golden60 = Schema60(GoldenJson);
        foreach (var (name, from, to) in new[]
        {
            ("junkPiles", ",\"junkPiles\":[],\"identified\"", ",\"identified\""),
            ("bloodPools", ",\"bloodPools\":[],\"lastJunkNumber\"", ",\"lastJunkNumber\""),
            ("lastJunkNumber", ",\"lastJunkNumber\":0,\"lastPoolNumber\"", ",\"lastPoolNumber\""),
            ("lastPoolNumber", ",\"lastPoolNumber\":0}", "}"),
        })
        {
            StringAssert.Contains(golden60, from);
            var result = GameSessionSnapshotCodec.TryDecode(golden60.Replace(from, to));
            Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.inventory." + name), (result.Failure, result.Path), name);
        }
        var both = Schema60(GoldenJson).Replace(",\"lighting\":{", ",\"objects\":{},\"lighting\":{");
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.objects"), (GameSessionSnapshotCodec.TryDecode(both).Failure, GameSessionSnapshotCodec.TryDecode(both).Path));
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
            Assert.AreEqual(0, restored.Capture().Objects.BloodPools.Count, "No death of a schema 57 session left a pool of blood.");
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
        Fails(GoldenJson.Replace(Current, "\"schemaVersion\":68,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
    }
}
