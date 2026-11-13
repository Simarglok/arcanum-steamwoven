using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-1542 (ledger GB13, schema 98): the lit state of the top strip's buttons and the Map button's face in the save. The session only keeps what
// the host records; a save of schema 97 or older has none, which loads as all idle.
[TestClass]
public sealed class BarLightsStateTests
{
    private static readonly SessionBarLightsState Lit = new(0b10010, 3);

    [TestMethod]
    public void A_session_starts_with_no_record_and_a_record_is_kept_without_moving_the_revision()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryBarLights());
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordBarLights(Lit).Disposition);
        Assert.AreEqual(Lit, owner.QueryBarLights());
        Assert.AreEqual(revision, owner.Version.Revision, "a host's record, like the lighting record, is not a session revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordBarLights(Lit).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordBarLights(new(0, 2)).Disposition, "all idle with the town map's face is a record too");
    }

    [TestMethod]
    public void A_mask_beyond_five_slots_and_a_face_other_than_the_two_map_slots_are_refused()
    {
        using var owner = Create();
        foreach (var bad in new SessionBarLightsState[] { new(-1, 2), new(32, 2), new(0, 1), new(0, 4), new(0, 0) })
        {
            var result = owner.RecordBarLights(bad);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidBarLights), (result.Disposition, result.Failure), bad.ToString());
        }
        Assert.IsNull(owner.QueryBarLights());
    }

    [TestMethod]
    public void The_record_is_saved_and_a_load_restores_it_and_a_session_without_one_saves_null()
    {
        using var owner = Create();
        var none = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(none, NoBarLights + ",\"lighting\":");
        owner.RecordBarLights(Lit);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"barLights\":{\"lit\":[false,true,false,false,true],\"mapMode\":3},\"lighting\":");
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure, decoded.Path);
        Assert.AreEqual(Lit, decoded.Snapshot!.BarLights);
        using var restored = Restore(decoded.Snapshot);
        Assert.AreEqual(Lit, restored.QueryBarLights());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "a load and a save again make the same text");
        using var idle = Restore(GameSessionSnapshotCodec.TryDecode(none).Snapshot!);
        Assert.IsNull(idle.QueryBarLights());
    }

    [TestMethod]
    public void A_schema_97_save_upgrades_with_no_record_and_a_damaged_one_is_refused()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema97(current);
        StringAssert.Contains(older, "\"schemaVersion\":97,");
        Assert.IsFalse(older.Contains("barLights", StringComparison.Ordinal));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot!.SchemaVersion);
        Assert.IsNull(upgraded.Snapshot.BarLights, "an older save loads idle");
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot));
        // The older schema cannot carry the section: one in a 97 text is an unknown field.
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoBarLights + ",\"lighting\":"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.barLights"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void A_damaged_section_is_refused_with_its_path()
    {
        using var owner = Create();
        owner.RecordBarLights(Lit);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Section = "\"barLights\":{\"lit\":[false,true,false,false,true],\"mapMode\":3}";
        (string Replacement, SnapshotDecodeFailure Failure, string Path)[] cases =
        [
            ("\"barLights\":[]", SnapshotDecodeFailure.WrongType, "$.barLights"),
            ("\"barLights\":{\"lit\":[false,true,false,false,true]}", SnapshotDecodeFailure.MissingField, "$.barLights.mapMode"),
            ("\"barLights\":{\"lit\":[false,true,false,false,true],\"mapMode\":3,\"x\":1}", SnapshotDecodeFailure.UnknownField, "$.barLights.x"),
            ("\"barLights\":{\"lit\":[false,true,false,false],\"mapMode\":3}", SnapshotDecodeFailure.OutOfRange, "$.barLights.lit"),
            ("\"barLights\":{\"lit\":[false,true,false,false,1],\"mapMode\":3}", SnapshotDecodeFailure.WrongType, "$.barLights.lit[4]"),
            ("\"barLights\":{\"lit\":[false,true,false,false,true],\"mapMode\":1}", SnapshotDecodeFailure.OutOfRange, "$.barLights.mapMode"),
            ("\"barLights\":{\"lit\":[false,true,false,false,true],\"mapMode\":4}", SnapshotDecodeFailure.OutOfRange, "$.barLights.mapMode"),
        ];
        foreach (var (replacement, failure, path) in cases)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text.Replace(Section, replacement));
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path), replacement);
        }
    }

    [TestMethod]
    public void A_restore_refuses_a_record_built_by_hand_that_the_codec_would_not_write()
    {
        using var owner = Create();
        var saved = owner.Capture();
        foreach (var bad in new SessionBarLightsState[] { new(32, 2), new(0, 5) })
        {
            var snapshot = new GameSessionSnapshot(saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags, saved.Actor, saved.Maps.ToArray(), saved.Story, saved.Party, saved.Hero,
                saved.Inventory, saved.Equipment, saved.Durability, saved.Schematics, saved.Effects, saved.Experience, saved.Advancement, saved.Random, saved.Life, saved.Reactions, saved.Combat, saved.CombatMode,
                saved.Concealment, saved.Danger, saved.Events, saved.Lighting, saved.Sound, saved.Hotkeys, saved.Objects, saved.Scripts, saved.Residency, saved.Goals, saved.Spells, saved.Newspaper, saved.Flights,
                saved.Generators, saved.Encounters, bad);
            var result = GameSessionOwner.Restore(snapshot, RestoreOptions());
            result.Owner?.Dispose();
            Assert.AreEqual(GameSessionFailure.InvalidBarLights, result.Failure, bad.ToString());
        }
    }
}
