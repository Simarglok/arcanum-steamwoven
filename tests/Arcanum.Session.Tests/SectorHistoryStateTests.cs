using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-106 (MIG9 S6, K1 §3.6, RL19, RL20, ledger CX10, CX11): the open map's sector eviction history in the snapshot (schema 68, the
/// <c>residency</c> section), the load that no longer folds every sector, the Original setting's live stamp, the light fold of the loaded sectors and the
/// 67 → 68 step. The map, the cells and the ids are invented (the same exterior as <see cref="SectorFoldTests"/>); no original data.
/// </summary>
[TestClass]
public sealed class SectorHistoryStateTests
{
    private const long T0 = 54_000_000; // 15:00 on day 0, the fixture's start hour.
    private const string Guard = "oid:guard", Watch = "oid:watch", Smith = "oid:smith";
    private static readonly SectorMapExtent Extent = new(16, 16);
    private static readonly CellCoord InZero = new(10, 20), InOne = new(70, 20), InFar = new(5 * 64, 5 * 64);
    private static readonly long SectorOne = SectorCacheRules.SectorId(1, 0), SectorFar = SectorCacheRules.SectorId(5, 5);

    private static SessionResidentRow[] Rows() =>
    [
        new(Guard, SessionResidentKind.Npc, new(10, 20)), new(Watch, SessionResidentKind.Npc, new(12, 20)), new(Smith, SessionResidentKind.Npc, new(70, 20)),
    ];

    private static GameSessionOwner Opened(GameSessionOwner owner, SectorCacheLimits? limits = null)
    {
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(SectorsOpenStatus.Open, owner.OpenSectors(Exterior, Extent, limits));
        return owner;
    }

    private static GameSessionOwner Fresh(RuleMode rules = RuleMode.Steamwoven) =>
        Opened(GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules }).Owner!);

    private static SectorTouchResult Touch(GameSessionOwner owner, params CellCoord[] cells)
    {
        var result = owner.TouchSectors(owner.Version, Exterior, cells);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static GameSessionOwner Reload(GameSessionOwner owner, RuleMode rules = RuleMode.Steamwoven) =>
        Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions() with { Rules = rules });

    private static GameSessionFailure RestoreFailure(string text)
    {
        var result = GameSessionOwner.Restore(Decode(text), RestoreOptions());
        Assert.IsNull(result.Owner);
        return result.Failure;
    }

    private static string WithHistory(string text, string residency) => text.Replace(NoResidency, ",\"residency\":" + residency);

    [TestMethod]
    public void ASaveListsEveryLoadedSectorStampedWithTheSavesTimeAndTheTextRoundTrips()
    {
        using var owner = Fresh();
        Assert.IsTrue(owner.Capture().Residency.IsEmpty, "a cache with nothing loaded and nothing evicted writes no history");
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(owner.Capture()), NoResidency);
        Touch(owner, InOne, InZero);
        Advance(owner, 4_000);
        var saved = owner.Capture();
        Assert.AreEqual(Exterior, saved.Residency.Map);
        CollectionAssert.AreEqual(new[] { new SessionSectorEviction(0, T0 + 4_000), new SessionSectorEviction(SectorOne, T0 + 4_000) }, saved.Residency.History.ToArray(),
            "ascending sector id, the save's game time");
        var text = GameSessionSnapshotCodec.Encode(saved);
        StringAssert.Contains(text, $",\"residency\":{{\"map\":79,\"history\":[[0,{T0 + 4_000}],[{SectorOne},{T0 + 4_000}]]}},\"lighting\":");
        using var restored = Reload(owner);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "a restored session saves the history it was given, before its cache opens");
        Assert.AreEqual(saved.Residency.History.Count, restored.Capture().Residency.History.Count);
    }

    [TestMethod]
    public void ASaveChangesTheLiveHistoryOnlyUnderTheOriginalSetting()
    {
        foreach (var (rules, listed) in new[] { (RuleMode.Steamwoven, 0), (RuleMode.Original, 2) })
        {
            using var owner = Fresh(rules);
            Touch(owner, InZero, InOne);
            Advance(owner, 10);
            var version = owner.Version;
            var first = owner.Capture();
            Assert.AreEqual(2, first.Residency.History.Count, rules.ToString());
            Assert.AreEqual(listed, owner.QuerySectors(Exterior)!.History.Length, $"{rules}: the session's own history after a save");
            Assert.AreEqual(version, owner.Version, "a save is not a change of the session");
            Assert.AreEqual(GameSessionSnapshotCodec.Encode(first), GameSessionSnapshotCodec.Encode(owner.Capture()), "a second save writes the same text");
            Assert.AreEqual(2, owner.QuerySectors(Exterior)!.Slots.Length, "the sectors stay loaded");
        }
    }

    [TestMethod]
    public void Cx10UnderTheOriginalSettingASectorEvictedAfterASaveIsMeasuredFromTheSave()
    {
        foreach (var (rules, marked) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            SectorCacheLimits.TryCreate(8, out var eight);
            using var owner = Opened(GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules }).Owner!, eight);
            Touch(owner, InZero);
            _ = owner.Capture();                // the save, at T0
            Advance(owner, 10_000);
            Touch(owner, Enumerable.Range(2, 8).Select(x => new CellCoord(x * 64, 512)).ToArray()); // evicts sector 0,0 at T0 + 10 s
            Assert.IsFalse(owner.QuerySectorLoaded(Exterior, new(0, 0))!.Value);
            Advance(owner, 1_000);              // back 1 s after the eviction, 11 s after the save
            Assert.AreEqual(marked, Touch(owner, InZero).Folds.Single(f => f.Sector == new ResidentSector(0, 0)).Marked, rules.ToString());
        }
    }

    [TestMethod]
    public void ALoadNoLongerFoldsTheSectorsTheSaveListedAndFoldsTheRestWithTheMarker()
    {
        foreach (var rules in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Fresh(rules);
            Touch(owner, InZero);
            Advance(owner, 20_000);
            using var restored = Reload(owner, rules);
            Assert.AreEqual(ResidencyStatus.Open, restored.OpenResidency(Exterior, Rows()).Status);
            Assert.AreEqual(SectorsOpenStatus.Open, restored.OpenSectors(Exterior, Extent));
            CollectionAssert.AreEqual(new[] { new SectorEvictionRecord(0, T0 + 20_000) }, restored.QuerySectors(Exterior)!.History.ToArray());
            // The arrival pre-cache after the load: the saved sector is not away, the other has no record.
            var touch = Touch(restored, InZero, InOne);
            var zero = touch.Folds.Single(f => f.Sector == new ResidentSector(0, 0));
            Assert.IsFalse(zero.Marked, $"{rules}: a sector the save listed does not fold");
            Assert.AreEqual((T0 + 20_001, false), restored.QueryAiTick(Guard), "an immediate tick without the wake-up marker");
            Assert.IsTrue(touch.Folds.Single(f => f.Sector == new ResidentSector(1, 0)).Marked, "a sector the save did not list folds");
            Assert.AreEqual((T0 + 20_001, true), restored.QueryAiTick(Smith));
            Assert.AreEqual(0, restored.QuerySectors(Exterior)!.History.Length, "both loads took their records out");
        }
    }

    [TestMethod]
    public void AGameLoadedWithNoHistoryFoldsEverySectorOnce()
    {
        using var owner = Fresh();
        Advance(owner, 5);
        using var restored = Reload(owner);
        Opened(restored);
        Assert.IsTrue(Touch(restored, InZero).Folds.Single().Marked, "the schema 67 step and a save with no cache list nothing");
    }

    [TestMethod]
    public void TheHistoryWaitsForTheCacheInTheSessionAndGoesWithTheMapItBelongsTo()
    {
        using var owner = Fresh();
        Touch(owner, InZero);
        using var restored = Reload(owner);
        var text = GameSessionSnapshotCodec.Encode(restored.Capture());
        // The residency opens, the cache not yet: the history is still carried by the session.
        Assert.AreEqual(ResidencyStatus.Open, restored.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(SectorsOpenStatus.NoResidency, restored.OpenSectors(Cave, Extent), "another map claims nothing");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // The cache opens and takes it; a save now writes the cache's history (the same text: the loaded sectors are listed after the touch).
        Assert.AreEqual(SectorsOpenStatus.Open, restored.OpenSectors(Exterior, Extent));
        Assert.AreEqual(1, restored.QuerySectors(Exterior)!.History.Length);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // A cache opened again starts empty, as any map close clears the history.
        Assert.AreEqual(ResidencyStatus.Open, restored.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(SectorsOpenStatus.Open, restored.OpenSectors(Exterior, Extent));
        Assert.AreEqual(0, restored.QuerySectors(Exterior)!.History.Length);

        // Unclaimed, the history goes with the residency's close and with the hero's map change.
        using var closed = Reload(owner);
        Assert.IsFalse(closed.Capture().Residency.IsEmpty);
        closed.CloseResidency();
        Assert.IsTrue(closed.Capture().Residency.IsEmpty);
        using var left = Reload(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, left.PlaceActor(left.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.IsTrue(left.Capture().Residency.IsEmpty, "the map the history belonged to has closed");
    }

    [TestMethod]
    public void AHistoryLongerThanTheCachesBoundIsRefusedAtOpenAndStaysForALargerCache()
    {
        using var owner = Fresh();
        var records = string.Join(",", Enumerable.Range(0, 20).Select(i => $"[{i},{i}]"));
        var text = WithHistory(GameSessionSnapshotCodec.Encode(owner.Capture()), "{\"map\":79,\"history\":[" + records + "]}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(ResidencyStatus.Open, restored.OpenResidency(Exterior, Rows()).Status);
        SectorCacheLimits.TryCreate(8, out var eight);
        Assert.AreEqual(SectorsOpenStatus.InvalidHistory, restored.OpenSectors(Exterior, Extent, eight), "8 slots keep 16 records");
        Assert.IsNull(restored.QuerySectors(Exterior));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "the history is kept");
        Assert.AreEqual(SectorsOpenStatus.Open, restored.OpenSectors(Exterior, Extent));
        Assert.AreEqual(20, restored.QuerySectors(Exterior)!.History.Length);
    }

    [TestMethod]
    public void TheDecodeAndTheRestoreRefuseAHistoryTheCacheCouldNotHaveWritten()
    {
        using var owner = Fresh();
        var plain = GameSessionSnapshotCodec.Encode(owner.Capture());
        void Decodes(string residency, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(WithHistory(plain, residency));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), residency);
        }
        Decodes("{\"map\":79,\"history\":[]}", SnapshotDecodeFailure.NotCanonical, "$.residency.map");
        Decodes("{\"map\":null,\"history\":[[1,2]]}", SnapshotDecodeFailure.NotCanonical, "$.residency.map");
        Decodes("{\"map\":0,\"history\":[[1,2]]}", SnapshotDecodeFailure.OutOfRange, "$.residency.map");
        Decodes("{\"map\":79,\"history\":[[1]]}", SnapshotDecodeFailure.WrongType, "$.residency.history[0]");
        Decodes("{\"map\":79,\"history\":[[-1,2]]}", SnapshotDecodeFailure.OutOfRange, "$.residency.history[0][0]");
        Decodes("{\"map\":79,\"history\":[[1,-2]]}", SnapshotDecodeFailure.OutOfRange, "$.residency.history[0][1]");
        Decodes("{\"map\":79,\"history\":[" + string.Join(",", Enumerable.Repeat("[1,2]", 257)) + "]}", SnapshotDecodeFailure.BudgetExceeded, "$.residency.history");
        Decodes("{\"map\":79}", SnapshotDecodeFailure.MissingField, "$.residency.history");
        Decodes("{\"map\":79,\"history\":[[1,2]],\"extra\":1}", SnapshotDecodeFailure.UnknownField, "$.residency.extra");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, GameSessionSnapshotCodec.TryDecode(plain.Replace(NoResidency, "")).Failure);

        // What the decode takes and the restore refuses: a map that is not the hero's, a time after the saved clock, an unordered list, a sector that is not an id.
        var now = T0;
        foreach (var residency in new[]
        {
            "{\"map\":80,\"history\":[[1,5]]}", $"{{\"map\":79,\"history\":[[1,{now + 1}]]}}", "{\"map\":79,\"history\":[[1,5],[2,4]]}",
            $"{{\"map\":79,\"history\":[[{1L << 52},5]]}}",
        })
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, RestoreFailure(WithHistory(plain, residency)), residency);
        // The bound is the largest cache's (128 slots keep 256 records); the cache that opens applies its own.
        var big = "{\"map\":79,\"history\":[" + string.Join(",", Enumerable.Range(0, 256).Select(i => $"[{i},{i}]")) + "]}";
        using var kept = Restore(Decode(WithHistory(plain, big)));
        Assert.AreEqual(256, kept.Capture().Residency.History.Count);
        // The same sector twice and equal times are what the original's history can hold.
        using var twice = Restore(Decode(WithHistory(plain, "{\"map\":79,\"history\":[[1,5],[1,5],[2,5]]}")));
        Assert.AreEqual(3, twice.Capture().Residency.History.Count);
    }

    [TestMethod]
    public void TheSixtySevenToSixtyEightStepAddsTheEmptySectionAndRefusesOneThatIsAlreadyThere()
    {
        using var owner = Fresh();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema67(current);
        StringAssert.Contains(older, "\"schemaVersion\":67,\"world\"");
        Assert.IsFalse(older.Contains("\"residency\"", StringComparison.Ordinal));
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(Decode(older)), "only the schema differs");
        using var restored = Restore(Decode(older));
        Assert.IsTrue(restored.Capture().Residency.IsEmpty);
        // A schema 67 text that carries the section is damage: the step adds it, never merges.
        var carries = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoResidency + ",\"lighting\":"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.residency"), (carries.Failure, carries.Path));
    }

    [TestMethod]
    public void TheLightFoldFollowsTheClockHourForTheLoadedSectorsOnly()
    {
        using var owner = Fresh();
        Assert.IsNull(owner.QuerySectorLights(Cave), "no cache for another map");
        Assert.AreEqual(0, owner.QuerySectorLights(Exterior)!.Count, "nothing loaded");
        var first = Touch(owner, InZero);
        Assert.AreEqual(15, first.Folds.Single().LightHour);
        CollectionAssert.AreEqual(new[] { new SectorLight(new(0, 0), 15, true) }, owner.QuerySectorLights(Exterior)!.ToArray());
        Assert.AreEqual(0, Touch(owner, InZero).Folds.Count, "a hit folds nothing and keeps the light");

        // The hourly update: 18:00 is night for the scenery (day is hours 6 to 17); a sector loaded then is folded to that hour.
        Advance(owner, 3 * 3_600_000L);
        Touch(owner, InOne);
        CollectionAssert.AreEqual(new[] { new SectorLight(new(0, 0), 18, false), new SectorLight(new(1, 0), 18, false) }, owner.QuerySectorLights(Exterior)!.ToArray());
        Advance(owner, 12 * 3_600_000L); // 06:00 on day 1: light day again
        CollectionAssert.AreEqual(new[] { new SectorLight(new(0, 0), 6, true), new SectorLight(new(1, 0), 6, true) }, owner.QuerySectorLights(Exterior)!.ToArray());
        // Within an hour nothing moves.
        Advance(owner, 30 * 60_000L);
        Assert.IsTrue(owner.QuerySectorLights(Exterior)!.All(l => l.Hour == 6));

        // An evicted sector leaves the light state and a sector away through an hour folds to the hour it loads at.
        SectorCacheLimits.TryCreate(8, out var eight);
        using var small = Opened(GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!, eight);
        Touch(small, InZero);
        var fill = Touch(small, Enumerable.Range(2, 8).Select(x => new CellCoord(x * 64, 512)).ToArray());
        Assert.IsTrue(fill.Folds.Any(f => f.Evicted == new ResidentSector(0, 0)));
        Assert.IsFalse(small.QuerySectorLights(Exterior)!.Any(l => l.Sector == new ResidentSector(0, 0)));
        Advance(small, 5 * 3_600_000L);
        Assert.IsTrue(small.QuerySectorLights(Exterior)!.All(l => l.Hour == 20));
        Assert.AreEqual(20, Touch(small, InZero).Folds.Single(f => f.Sector == new ResidentSector(0, 0)).LightHour);
        Assert.AreEqual(8, small.QuerySectorLights(Exterior)!.Count);
    }

    [TestMethod]
    public void TheLightStateIsDerivedAndIsNotSaved()
    {
        using var owner = Fresh();
        Touch(owner, InZero, InFar);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.IsFalse(text.Contains("LightHour", StringComparison.OrdinalIgnoreCase));
        using var restored = Restore(Decode(text));
        Assert.IsNull(restored.QuerySectorLights(Exterior), "no cache until the host opens one");
        Opened(restored);
        Assert.AreEqual(0, restored.QuerySectorLights(Exterior)!.Count);
        Touch(restored, InFar);
        CollectionAssert.AreEqual(new[] { new SectorLight(new(5, 5), 15, true) }, restored.QuerySectorLights(Exterior)!.ToArray());
        Assert.AreEqual(SectorFar, SectorCacheRules.SectorId(5, 5));
    }
}
