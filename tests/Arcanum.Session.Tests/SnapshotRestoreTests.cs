using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class SnapshotRestoreTests
{
    [TestMethod]
    public void EncodingIsCanonicalAndMatchesTheLiteralSchema()
    {
        using var owner = GoldenSession();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(GoldenJson, text);
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(Decode(GoldenJson)));
        var spaced = GoldenJson.Replace(",", " ,\n ").Replace(":", " : ");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(Decode(spaced)));
    }

    [TestMethod]
    public void RestoreRebuildsEveryComponentUnderAFreshIdentity()
    {
        using var owner = GoldenSession();
        var clock = owner.QueryClockSnapshot();
        var paused = owner.SetPaused(owner.AdvanceGameTime(clock.Token, 90_000_000, ClockAdvanceReason.OrdinaryAdvance).Snapshot.Token,
            new(true, PauseReason.TurnBased));
        Assert.AreEqual(ClockMutationDisposition.Changed, paused.Disposition);
        WriteFlag(owner, 3, true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordLighting(new(1, 16)).Disposition);
        var snapshot = owner.Capture();

        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(snapshot)));
        Assert.AreNotSame(owner.Identity, restored.Identity);
        Assert.AreEqual(2, restored.Version.Revision);
        Assert.AreEqual(Start, restored.QueryActor());
        var restoredClock = restored.QueryClockSnapshot();
        Assert.AreEqual(144_000_000, restoredClock.GameTimestampMs);
        Assert.AreEqual(new PauseDisposition(true, PauseReason.TurnBased), restoredClock.PauseDisposition);
        Assert.AreEqual(2, restoredClock.Revision);
        Assert.AreEqual(1, restoredClock.DayIndex);
        Assert.AreEqual(16, restoredClock.Hour);
        Assert.AreNotSame(paused.Snapshot.OwnerIdentity, restoredClock.OwnerIdentity);
        Assert.AreEqual(true, ReadFlag(restored, 3));
        Assert.AreEqual(true, ReadFlag(restored, 7));
        Assert.IsNull(ReadFlag(restored, 9), "Unknown stays Unknown, never false");
        Assert.AreEqual(new GlobalFlagVersion(Identity, RestoreEpoch, 1), restored.FlagVersion);
        Assert.AreEqual(new SessionLightingState(1, 16), restored.QueryLighting());
        Assert.AreEqual(GameSessionSnapshotCodec.Encode(snapshot), GameSessionSnapshotCodec.Encode(restored.Capture()));

        // Versions and tokens from the earlier owner do not authorize writes on the restored one.
        Rejected(restored.PlaceActor(owner.Version, Start with { Map = Cave }), restored, GameSessionFailure.StaleVersion, 2);
        Assert.AreEqual(ClockFailure.StaleToken, restored.SetPaused(paused.Snapshot.Token, default).Failure);
        var staleFlag = restored.Flags.Write(new(new(Guid.Parse("77777777-7777-7777-7777-777777777777")), owner.FlagVersion, new FlagId(3), false));
        Assert.AreEqual(GlobalFlagReason.StaleEpoch, staleFlag.Reason);
    }

    [TestMethod]
    public void IncompatibleSnapshotsAreRefusedAndThePreviousSessionStaysUsable()
    {
        using var current = GoldenSession();
        var before = GameSessionSnapshotCodec.Encode(current.Capture());
        var snapshot = Decode(GoldenJson);
        var otherWorld = new GlobalFlagIdentity(new(Guid.Parse("88888888-8888-8888-8888-888888888888")), Module);
        var otherModule = new GlobalFlagIdentity(World, new(Guid.Parse("99999999-9999-9999-9999-999999999999")));

        void Refused(GameSessionSnapshot? s, GameSessionRestoreOptions? options, GameSessionFailure failure,
            ClockFailure clock = ClockFailure.None, GlobalFlagReason flags = GlobalFlagReason.None)
        {
            var result = GameSessionOwner.Restore(s, options);
            Assert.IsNull(result.Owner);
            Assert.AreEqual(failure, result.Failure);
            Assert.AreEqual(clock, result.ClockFailure);
            Assert.AreEqual(flags, result.FlagReason);
        }

        Refused(null, RestoreOptions(), GameSessionFailure.InvalidSnapshot);
        Refused(snapshot, null, GameSessionFailure.InvalidDescriptor);
        Refused(snapshot, RestoreOptions() with { ExpectedIdentity = otherWorld }, GameSessionFailure.WrongWorld);
        Refused(snapshot, RestoreOptions() with { ExpectedIdentity = otherModule }, GameSessionFailure.WrongModule);
        Refused(snapshot, RestoreOptions() with { ExpectedClockProfile = Profile(hour: 16) }, GameSessionFailure.IncompatibleClockProfile);
        Refused(snapshot, RestoreOptions() with { ExpectedClockProfile = Profile() with { SourceDataSha256 = new string('b', 64) } },
            GameSessionFailure.IncompatibleClockProfile);
        Refused(snapshot, RestoreOptions(new GameSessionLimits(maxChangesPerMap: 1)), GameSessionFailure.BudgetExceeded);
        Refused(snapshot, RestoreOptions(new GameSessionLimits(maxTotalChanges: 1)), GameSessionFailure.BudgetExceeded);
        Refused(snapshot, RestoreOptions() with { FlagEpoch = default }, GameSessionFailure.FlagsRejected, flags: GlobalFlagReason.InvalidEpoch);

        // Decoded text the clock itself refuses: unsupported clock schema and policy.
        var clockSchema = Decode(GoldenJson.Replace("\"clock\":{\"schemaVersion\":1", "\"clock\":{\"schemaVersion\":2"));
        Refused(clockSchema, RestoreOptions() with { ExpectedClockProfile = clockSchema.Clock.Profile },
            GameSessionFailure.ClockRejected, ClockFailure.InvalidSchema);
        var emptyFlags = Decode(GoldenJson.Replace(
            "\"entries\":[{\"flag\":3,\"value\":false},{\"flag\":7,\"value\":true},{\"flag\":9,\"value\":null}]", "\"entries\":[]"));
        Refused(emptyFlags, RestoreOptions(), GameSessionFailure.FlagsRejected, flags: GlobalFlagReason.InvalidDomain);

        Assert.AreEqual(before, GameSessionSnapshotCodec.Encode(current.Capture()));
        Changed(current.PlaceActor(current.Version, Start with { Map = Cave }), current, 3);
    }

    [TestMethod]
    public void DecodeRejectsDamagedTextWithTypedFailureAndPath()
    {
        void Fails(string? text, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text);
            Assert.IsNull(result.Snapshot, text);
            Assert.AreEqual(failure, result.Failure, text);
            Assert.AreEqual(path, result.Path, text);
        }

        string With(string from, string to)
        {
            Assert.IsTrue(GoldenJson.Contains(from, StringComparison.Ordinal), from);
            return GoldenJson.Replace(from, to);
        }

        Fails(null, SnapshotDecodeFailure.MalformedJson, "$");
        Fails("", SnapshotDecodeFailure.MalformedJson, "$");
        Fails(GoldenJson[..^40], SnapshotDecodeFailure.MalformedJson, "$");
        Fails(GoldenJson + "{}", SnapshotDecodeFailure.MalformedJson, "$");
        Fails(With("\"hour\":15}}", "\"hour\":15},}"), SnapshotDecodeFailure.MalformedJson, "$");
        Fails(GoldenJson.Replace("\"quelledRumors\":[]},", "\"quelledRumors\":[],},"), SnapshotDecodeFailure.MalformedJson, "$");
        Fails("/*x*/" + GoldenJson, SnapshotDecodeFailure.MalformedJson, "$");
        Fails(new string(' ', GameSessionSnapshotCodec.MaxEncodedCharacters + 1), SnapshotDecodeFailure.TooLarge, "$");
        Fails("[]", SnapshotDecodeFailure.WrongType, "$");
        Fails(With("arcanum-steamwoven-session", "other-save"), SnapshotDecodeFailure.WrongFormat, "$.format");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":26,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-WORLD-045: schema 24 has no loyalty marks in its party records and is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":24,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-TECHNOLOGY-004: schema 23 has no made items and is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":23,\"world\"").Replace(",\"made\":[]", ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With(",\"made\":[]", ""), SnapshotDecodeFailure.MissingField, "$.inventory.made");
        // ARC-TECHNOLOGY-003: schema 22 has no schematics section and is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":22,\"world\"").Replace(NoSchematics, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With(NoSchematics, ""), SnapshotDecodeFailure.MissingField, "$.schematics");
        // ARC-WORLD-053: schema 21 has no durability section and is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":21,\"world\"").Replace(NoSchematics, "").Replace(NoDurability, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With(NoDurability, ""), SnapshotDecodeFailure.MissingField, "$.durability");
        // ARC-SKILLS-002: schema 20 has the same structure (a heroless snapshot has no innate skill changes) and is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":20,\"world\""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-WORLD-036: schema 19 has no advancement sections.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":19,\"world\"").Replace(NoAdvancement, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-WORLD-040: schema 17 has no sound state.
        // ARC-UI-008: schema 18 has no quelled rumors.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":18,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":17,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoSound, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-DEV-009: schema 15 has the same structure but a clock bound to the old source; it is refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":15,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // ARC-WORLD-035: schema 16 has no blessing or curse lists.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":16,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoEffects, ",\"effects\":{\"hero\":[],\"definitions\":[]}"),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        // Schema 1 (no story, no lighting), schema 2 (story without lighting), schema 3 (no party), schema 4 (no inventory),
        // schema 5 (no hero, no gold), schema 6 (no random), schema 7 (no life), schema 8 (no events), schema 9 (no reactions), schema 10 (no combat), schema 11 (no equipment), schema 12 (no effects), schema 13 (no experience) and schema 14 (no combat mode) are refused, not migrated.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":1,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(EmptyStory.Replace(QuelledRumors, "") + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents + LightingTail, "}"),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":2,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents + LightingTail, "}"),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":3,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":4,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":5,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents, ",\"inventory\":{\"pc\":[]}"),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":6,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoEvents, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":7,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment, "").Replace(NoLife + NoReactions + NoCombat + NoEvents, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":8,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment, "").Replace(NoReactions + NoCombat + NoEvents, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":9,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment, "").Replace(NoReactions + NoCombat, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":10,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment, "").Replace(NoCombat, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":11,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, "").Replace(NoEquipment, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":12,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":13,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":14,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With(EmptyParty, ""), SnapshotDecodeFailure.MissingField, "$.party");
        Fails(With(EmptyInventory, ""), SnapshotDecodeFailure.MissingField, "$.inventory");
        Fails(With(NoHero, ""), SnapshotDecodeFailure.MissingField, "$.hero");
        Fails(With(FreshRandom, ""), SnapshotDecodeFailure.MissingField, "$.random");
        Fails(With(NoLife, ""), SnapshotDecodeFailure.MissingField, "$.life");
        Fails(With(NoReactions, ""), SnapshotDecodeFailure.MissingField, "$.reactions");
        Fails(With(NoCombat, ""), SnapshotDecodeFailure.MissingField, "$.combat");
        Fails(With(NoCombatMode, ""), SnapshotDecodeFailure.MissingField, "$.combatMode");
        Fails(With(NoEquipment, ""), SnapshotDecodeFailure.MissingField, "$.equipment");
        Fails(With(NoEffects, ""), SnapshotDecodeFailure.MissingField, "$.effects");
        Fails(With(NoExperience, ""), SnapshotDecodeFailure.MissingField, "$.experience");
        Fails(With(NoAdvancement, ""), SnapshotDecodeFailure.MissingField, "$.advancement");
        Fails(With(NoEvents, ""), SnapshotDecodeFailure.MissingField, "$.events");
        Fails(With("\"revision\":2,", "\"revision\":2,\"extra\":0,"), SnapshotDecodeFailure.UnknownField, "$.extra");
        Fails(With("\"revision\":2,", "\"revision\":2,\"revision\":3,"), SnapshotDecodeFailure.DuplicateField, "$.revision");
        Fails(With("\"revision\":2,", ""), SnapshotDecodeFailure.MissingField, "$.revision");
        Fails(With("\"revision\":2,", "\"revision\":\"2\","), SnapshotDecodeFailure.WrongType, "$.revision");
        Fails(With("\"revision\":2,", "\"revision\":-1,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With("\"revision\":2,", "\"revision\":2.0,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With("\"revision\":2,", "\"revision\":2e0,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With("\"revision\":2,", "\"revision\":9223372036854775808,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With("11111111-1111-1111-1111-111111111111", "AAAAAAAA-1111-1111-1111-111111111111"), SnapshotDecodeFailure.OutOfRange, "$.world");
        Fails(With("11111111-1111-1111-1111-111111111111", "{11111111-1111-1111-1111-111111111111}"), SnapshotDecodeFailure.OutOfRange, "$.world");
        Fails(With("\"startHour\":15", "\"startHour\":24"), SnapshotDecodeFailure.OutOfRange, "$.clock.startHour");
        Fails(With("\"timestampMs\":54000000", "\"timestampMs\":-1"), SnapshotDecodeFailure.OutOfRange, "$.clock.timestampMs");
        Fails(With("\"paused\":false", "\"paused\":true"), SnapshotDecodeFailure.OutOfRange, "$.clock.pauseReason");
        Fails(With("\"pauseReason\":\"None\"", "\"pauseReason\":\"none\""), SnapshotDecodeFailure.OutOfRange, "$.clock.pauseReason");
        Fails(With("\"paused\":false", "\"paused\":0"), SnapshotDecodeFailure.WrongType, "$.clock.paused");
        Fails(With("{\"flag\":3,\"value\":false},{\"flag\":7", "{\"flag\":7,\"value\":false},{\"flag\":7"), SnapshotDecodeFailure.NotCanonical, "$.flags.entries[1].flag");
        Fails(With("{\"flag\":9,\"value\":null}", "{\"flag\":9,\"value\":\"true\"}"), SnapshotDecodeFailure.WrongType, "$.flags.entries[2].value");
        Fails(With("{\"flag\":9,\"value\":null}", "{\"flag\":9}"), SnapshotDecodeFailure.MissingField, "$.flags.entries[2].value");
        Fails(With("\"facing\":\"XPlus\"", "\"facing\":\"5\""), SnapshotDecodeFailure.OutOfRange, "$.actor.facing");
        Fails(With("\"map\":79,\"x\"", "\"map\":0,\"x\""), SnapshotDecodeFailure.OutOfRange, "$.actor.map");
        Fails(With("\"x\":100", "\"x\":-100"), SnapshotDecodeFailure.OutOfRange, "$.actor.x");
        Fails(With("\"maps\":[{\"map\":79,", "\"maps\":[{\"map\":80,\"changes\":[{\"scope\":\"sector\",\"sector\":1,\"property\":\"p\",\"value\":1}]},{\"map\":79,"),
            SnapshotDecodeFailure.NotCanonical, "$.maps[1].map");
        Fails(With("\"maps\":[{\"map\":79,\"changes\":[", "\"maps\":[{\"map\":79,\"changes\":[]},{\"map\":80,\"changes\":["),
            SnapshotDecodeFailure.OutOfRange, "$.maps[0].changes");
        Fails(With("\"sector\":5,", "\"sector\":5,\"entity\":\"npc-1\","), SnapshotDecodeFailure.UnknownField, "$.maps[0].changes[0].entity");
        Fails(With("\"scope\":\"sector\"", "\"scope\":\"Sector\""), SnapshotDecodeFailure.OutOfRange, "$.maps[0].changes[0].scope");
        Fails(With("\"property\":\"door.open\"", "\"property\":\"door open\""), SnapshotDecodeFailure.OutOfRange, "$.maps[0].changes[0].property");
        Fails(With("\"entity\":\"npc-1\"", "\"entity\":\"\""), SnapshotDecodeFailure.OutOfRange, "$.maps[0].changes[1].entity");
        Fails(With("{\"scope\":\"entity\",\"entity\":\"npc-1\",\"property\":\"hp\",\"value\":12}",
                "{\"scope\":\"sector\",\"sector\":5,\"property\":\"door.open\",\"value\":2}"),
            SnapshotDecodeFailure.NotCanonical, "$.maps[0].changes[1]");
        Fails(With("\"value\":12}", "\"value\":12,\"x\":[[[[[[[[1]]]]]]]]}"), SnapshotDecodeFailure.MalformedJson, "$");
        Fails(With("\"revision\":2,", "\"revision\":-0,"), SnapshotDecodeFailure.OutOfRange, "$.revision");
        Fails(With("steamwoven-normalized-clock-v1", "\\ud800"), SnapshotDecodeFailure.MalformedJson, "$.clock.policyId");
        Fails(With("arcanum-steamwoven-session", "\\udc00x"), SnapshotDecodeFailure.MalformedJson, "$.format");
        Fails(With("\"entity\":\"npc-1\"", "\"entity\":\"\\ud800\\ud800\""), SnapshotDecodeFailure.MalformedJson, "$.maps[0].changes[1].entity");
        Fails(With("\"format\":\"arcanum-steamwoven-session\",", ""), SnapshotDecodeFailure.WrongFormat, "$.format");

        Fails(With(LightingTail, "}"), SnapshotDecodeFailure.MissingField, "$.lighting");
        Fails(With("\"schemeId\":1", "\"schemeId\":0"), SnapshotDecodeFailure.OutOfRange, "$.lighting.schemeId");
        Fails(With("\"hour\":15}}", "\"hour\":24}}"), SnapshotDecodeFailure.OutOfRange, "$.lighting.hour");
        Fails(With("\"hour\":15}}", "\"hour\":15,\"sector\":1}}"), SnapshotDecodeFailure.UnknownField, "$.lighting.sector");
        Fails(With(LightingTail, ",\"lighting\":[]}"), SnapshotDecodeFailure.WrongType, "$.lighting");
        Fails(With(LightingTail, ",\"lighting\":{\"hour\":15}}"), SnapshotDecodeFailure.MissingField, "$.lighting.schemeId");

        // A newer schema with different fields is reported as unsupported, not as damage.
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":26,\"wardrobe\":[],\"world\"").Replace("\"revision\":2,", ""),
            SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        Fails(With("\"schemaVersion\":25,\"world\"", "\"schemaVersion\":4294967297,\"world\"").Replace(NoAdvancement, "").Replace(QuelledRumors, "").Replace(NoCombatMode, "").Replace(NoExperience, "").Replace(NoEffects, ""), SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");

        var tooManyMaps = string.Join(",", Enumerable.Range(1, GameSessionLimits.PolicyMaxMaps + 1)
            .Select(map => $"{{\"map\":{map},\"changes\":[{{\"scope\":\"sector\",\"sector\":1,\"property\":\"p\",\"value\":1}}]}}"));
        Fails(GoldenJson[..GoldenJson.IndexOf("\"maps\":[", StringComparison.Ordinal)] + "\"maps\":[" + tooManyMaps + "]" + EmptyStory + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoSound + NoEvents + LightingTail,
            SnapshotDecodeFailure.BudgetExceeded, "$.maps");
        var tooManyFlags = string.Join(",", Enumerable.Range(0, GlobalFlagLimits.MaxFlags + 1).Select(flag => $"{{\"flag\":{flag},\"value\":null}}"));
        Fails(With("\"entries\":[{\"flag\":3,\"value\":false},{\"flag\":7,\"value\":true},{\"flag\":9,\"value\":null}]", "\"entries\":[" + tooManyFlags + "]"),
            SnapshotDecodeFailure.BudgetExceeded, "$.flags.entries");
    }

    [TestMethod]
    public void DecodedSnapshotWithinPolicyMaximaRestoresUnderDefaultLimits()
    {
        var changes = string.Join(",", Enumerable.Range(0, GameSessionLimits.PolicyMaxChangesPerMap)
            .Select(sector => $"{{\"scope\":\"sector\",\"sector\":{sector},\"property\":\"p\",\"value\":{sector}}}"));
        var text = GoldenJson[..GoldenJson.IndexOf("\"maps\":[", StringComparison.Ordinal)] +
            "\"maps\":[{\"map\":79,\"changes\":[" + changes + "]}]" + EmptyStory + EmptyParty + NoHero + EmptyInventory + NoEquipment + NoDurability + NoSchematics + NoEffects + NoExperience + NoAdvancement + FreshRandom + NoLife + NoReactions + NoCombat + NoCombatMode + NoSound + NoEvents + LightingTail;
        using var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionLimits.PolicyMaxChangesPerMap, restored.QueryMapChanges(Exterior).Count);
        var last = GameSessionLimits.PolicyMaxChangesPerMap - 1;
        Assert.AreEqual(last, restored.QueryMapChange(Exterior, MapChangeKey.ForSector(last, "p")));
        Rejected(restored.SetMapChange(restored.Version, Exterior, MapChangeKey.ForSector(last + 1, "p"), 1), restored,
            GameSessionFailure.BudgetExceeded, 2);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void RevisionCapacityRefusesChangesButKeepsNoChangesAndState()
    {
        using var owner = Restore(Decode(GoldenJson.Replace("\"revision\":2,", "\"revision\":9223372036854775807,")));
        var key = MapChangeKey.ForSector(5, "door.open");
        Assert.AreEqual(long.MaxValue, owner.Version.Revision);
        Rejected(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Rejected(owner.SetMapChange(owner.Version, Exterior, key, 2), owner, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Rejected(owner.ClearMapChange(owner.Version, Exterior, key), owner, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Rejected(owner.SetQuestState(owner.Version, 1000, SessionQuestState.Mentioned), owner, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Rejected(owner.LearnRumor(owner.Version, 1000), owner, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Assert.IsNull(owner.QueryQuest(1000));
        Assert.IsNull(owner.QueryRumor(1000));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetMapChange(owner.Version, Exterior, key, 1).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.PlaceActor(owner.Version, Start).Disposition);
        Assert.AreEqual(Start, owner.QueryActor());
        Assert.AreEqual(1, owner.QueryMapChange(Exterior, key));
    }

    [TestMethod]
    public void FlagsViewDoesNotExposeTheOwnerLifetime()
    {
        using var owner = Create();
        Assert.IsNotInstanceOfType<GlobalFlagOwner>(owner.Flags);
        Assert.IsNotInstanceOfType<IDisposable>(owner.Flags);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 9, false).Disposition);
        Assert.AreEqual(false, ReadFlag(owner, 9));
    }
}
