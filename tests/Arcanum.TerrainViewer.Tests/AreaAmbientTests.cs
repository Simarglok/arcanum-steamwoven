using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-RENDER-010. Literal ART ids and hand-computed hours; no production helper supplies expectations.
[TestClass]
public sealed class AreaAmbientTests
{
    [TestMethod]
    public void Channel_follows_art_type_and_tile_or_facade_type_bit()
    {
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0x00000000));   // tile, type bit 0
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0x00000100));  // tile, type bit 1
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0x000000C1));   // tile flip bits only
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0xB0000000));   // facade, type bit 0
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0xB2000000));  // facade, type bit 25
        Assert.AreEqual(AreaAmbientChannel.Indoor, AreaAmbientPolicy.Channel(0xB0000100));   // tile bit is not the facade bit
        foreach (var type in new uint[] { 1, 2, 3, 4, 7, 10, 12, 13 })
            Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(type << 28), $"type {type}");
        foreach (var type in new uint[] { 5, 8, 9, 14, 15 })
            Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(type << 28), $"type {type}");
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channel(0x60000000));  // item, ground disposition
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(0x60001000));    // item, inventory
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channel(0x60003000));    // item, schematic
    }

    [TestMethod]
    public void Plan_channels_cover_every_terrain_and_body_command()
    {
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var plan = AreaScenePlan.Create([], [body], [visual]);
        // The composition fixture body ART is scenery (0x4...): outdoor.
        var channels = AreaAmbientPolicy.Channels(plan, [], [visual]);
        Assert.AreEqual(1, channels.Count);
        Assert.AreEqual(AreaAmbientChannel.Outdoor, channels[visual.Key]);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaAmbientPolicy.Channels(plan, [], []));
    }

    private static string Rows(Func<int, string> row) => string.Concat(Enumerable.Range(0, 24).Select(h => $"{{{h}}}{{{row(h)}}}\n"));
    private static LightSchemeTable Table(int id, Func<int, string> row)
    {
        Assert.AreEqual(LightingFailure.None, LightSchemeParser.TryParse(id, Encoding.ASCII.GetBytes(Rows(row)), out var table));
        return table!;
    }
    private static readonly string Sha = new('b', 64);
    private static readonly long West = new AreaSectorCoordinate(1451, 1290).Packed, East = new AreaSectorCoordinate(1452, 1290).Packed;

    private static AreaLightingSource Source(int startHour) => new(Sha, 5000, 1, Sha,
        new Dictionary<long, int> { [West] = 0, [East] = 3 },
        new Dictionary<int, LightSchemeTable> { [1] = Table(1, h => $"{h},{h},{h},{100 + h},0,0"), [3] = Table(3, h => $"0,{h},0,0,{200 + h},0") },
        new StartupClockDeclaration(1885, startHour, Sha));

    private static GameSessionOwner Session(AreaLightingSource source, ProfileBinding? profile = null)
    {
        var created = AreaTransitionCoordinator.CreateSession(profile ?? AreaAmbientDriver.ClockProfile(source),
            Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), new(100, 100), 1);
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    [TestMethod]
    public void Driver_starts_at_declared_hour_and_applies_whole_hours_and_sector_changes()
    {
        using var session = Session(Source(15));
        var driver = AreaAmbientDriver.Create(Source(15), session, West);
        Assert.AreEqual(54_000_000, driver.Clock.GameTimestampMs);
        Assert.AreEqual((15, true), (driver.Clock.Hour, driver.Clock.IsDay));
        Assert.AreEqual(new AmbientSelection(1, true, West, 15, new(new(15, 15, 15), new(115, 0, 0))), driver.Selection);
        Assert.AreEqual(1, driver.Revision);
        Assert.AreEqual(driver.Selection.Pair, driver.Effective);
        Assert.AreEqual(new SessionLightingState(1, 15), session.QueryLighting());

        Assert.IsFalse(driver.Apply(West)); Assert.AreEqual(1, driver.Revision);
        Assert.IsTrue(driver.Apply(East)); Assert.AreEqual(2, driver.Revision);
        Assert.AreEqual(new AmbientSelection(3, false, East, 15, new(new(0, 15, 0), new(0, 215, 0))), driver.Selection);
        Assert.AreEqual(15, driver.Clock.Hour, "a sector change keeps the game hour");
        Assert.AreEqual(new SessionLightingState(3, 15), session.QueryLighting());

        Assert.IsTrue(driver.AdvanceHours(4, East));
        Assert.AreEqual((19, false, 68_400_000L), (driver.Clock.Hour, driver.Clock.IsDay, driver.Clock.GameTimestampMs));
        Assert.AreEqual(new AmbientPair(new(0, 19, 0), new(0, 219, 0)), driver.Selection.Pair);
        Assert.IsTrue(driver.AdvanceHours(6, West));
        Assert.AreEqual((1, 1L), (driver.Clock.Hour, driver.Clock.DayIndex));
        Assert.AreEqual(new AmbientSelection(1, true, West, 1, new(new(1, 1, 1), new(101, 0, 0))), driver.Selection);
        Assert.IsFalse(driver.AdvanceHours(24, West), "a whole day returns to the same hour and scheme");
        Assert.AreEqual((1, 2L), (driver.Clock.Hour, driver.Clock.DayIndex));
        Assert.AreEqual(4, driver.Revision);
        Assert.AreEqual(driver.Clock, session.QueryClockSnapshot(), "the driver has no clock of its own");

        driver.Enabled = false;
        Assert.AreEqual(AmbientPair.Neutral, driver.Effective);
        Assert.AreEqual(new AmbientPair(new(1, 1, 1), new(101, 0, 0)), driver.Selection.Pair);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => driver.AdvanceHours(0, West));
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Apply(new AreaSectorCoordinate(1, 1).Packed));
        var clock = driver.Clock; var selection = driver.Selection;
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.AdvanceHours(1, new AreaSectorCoordinate(1, 1).Packed));
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Tick(10_000, false, new AreaSectorCoordinate(1, 1).Packed));
        Assert.AreEqual(clock, driver.Clock, "a refused advance leaves the clock unchanged");
        Assert.AreEqual(selection, driver.Selection);
    }

    [TestMethod]
    public void Driver_refuses_a_session_started_from_another_profile()
    {
        using var session = Session(Source(15), AreaAmbientDriver.ClockProfile(Source(16)));
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaAmbientDriver.Create(Source(15), session, West));
    }

    [TestMethod]
    public void Tick_flows_eight_times_admitted_host_time_and_switches_the_scheme_at_the_hour()
    {
        // Start 15:00; 450,000 host ms at 8x is exactly one game hour. Ticks of 250 ms admit 2,000 game ms each.
        using var session = Session(Source(15));
        var driver = AreaAmbientDriver.Create(Source(15), session, West);
        Assert.IsFalse(driver.Tick(0, false, West), "the first observation admits nothing");
        var changes = 0;
        for (var host = 250L; host <= 449_750; host += 250) changes += driver.Tick(host, false, West) ? 1 : 0;
        Assert.AreEqual((0, 15, 54_000_000L + 3_598_000L), (changes, driver.Clock.Hour, driver.Clock.GameTimestampMs));
        Assert.IsTrue(driver.Tick(450_000, false, West), "the hour boundary re-applies the scheme");
        Assert.AreEqual((16, 0, 57_600_000L), (driver.Clock.Hour, driver.Clock.Minute, driver.Clock.GameTimestampMs));
        Assert.AreEqual(new AmbientSelection(1, true, West, 16, new(new(16, 16, 16), new(116, 0, 0))), driver.Selection);
        Assert.AreEqual(new SessionLightingState(1, 16), session.QueryLighting());
        Assert.IsFalse(driver.ModalPaused);
        // A hitch admits at most 250 host ms.
        driver.Tick(1_000_000, false, West);
        Assert.AreEqual(57_602_000L, driver.Clock.GameTimestampMs);
    }

    [TestMethod]
    public void Modal_window_pauses_the_clock_without_catch_up_and_wait_still_works()
    {
        using var session = Session(Source(15));
        var driver = AreaAmbientDriver.Create(Source(15), session, West);
        driver.Tick(0, false, West);
        driver.Tick(100, false, West);
        Assert.AreEqual(54_000_800L, driver.Clock.GameTimestampMs);

        driver.Tick(200, true, West);
        Assert.IsTrue(driver.ModalPaused);
        Assert.AreEqual(new PauseDisposition(true, PauseReason.Menu), session.QueryClockSnapshot().PauseDisposition);
        Assert.AreEqual(54_000_800L, driver.Clock.GameTimestampMs, "the interval observed while opening is not game time");
        for (var host = 450L; host < 3_600_000; host += 250) driver.Tick(host, true, West);
        driver.Tick(3_600_000, true, West);
        Assert.AreEqual(54_000_800L, driver.Clock.GameTimestampMs, "an hour of open dialog moves nothing");

        Assert.IsTrue(driver.AdvanceHours(1, West), "an explicit wait works while the dialog pause holds");
        Assert.AreEqual((16, true), (driver.Clock.Hour, driver.ModalPaused));
        Assert.AreEqual(new SessionLightingState(1, 16), session.QueryLighting());

        driver.Tick(3_600_100, false, West);
        Assert.IsFalse(driver.ModalPaused);
        Assert.AreEqual(57_600_800L + 800L, driver.Clock.GameTimestampMs, "resume admits only the interval after the last paused frame");

        // A pause the driver did not set (another reason) is not released by a closed modal.
        var user = session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.User));
        Assert.AreEqual(ClockMutationDisposition.Changed, user.Disposition);
        driver.Tick(3_600_400, false, West);
        Assert.AreEqual(new PauseDisposition(true, PauseReason.User), driver.Clock.PauseDisposition);
        Assert.AreEqual(57_601_600L, driver.Clock.GameTimestampMs);

        // Nor a Menu pause someone else set while this driver held none.
        var menu = session.SetPaused(session.QueryClockSnapshot().Token, new(true, PauseReason.Menu));
        Assert.AreEqual(ClockMutationDisposition.Changed, menu.Disposition);
        Assert.IsFalse(driver.ModalPaused);
        driver.Tick(3_600_800, false, West);
        Assert.AreEqual(new PauseDisposition(true, PauseReason.Menu), driver.Clock.PauseDisposition);
    }

    [TestMethod]
    public void Capture_carries_the_time_and_the_applied_scheme_and_restores_them()
    {
        using var session = Session(Source(15));
        var driver = AreaAmbientDriver.Create(Source(15), session, East);
        driver.AdvanceHours(5, East);
        driver.Tick(0, true, East);
        var text = GameSessionSnapshotCodec.Encode(session.Capture());
        StringAssert.Contains(text, "\"timestampMs\":72000000,\"paused\":true,\"pauseReason\":\"Menu\"");
        StringAssert.EndsWith(text, ",\"lighting\":{\"schemeId\":3,\"hour\":20}}");
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.AreEqual(SnapshotDecodeFailure.None, decoded.Failure);
        var identity = new Arcanum.Scripting.GlobalFlags.GlobalFlagIdentity(new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var restoredResult = GameSessionOwner.Restore(decoded.Snapshot, new(identity, AreaAmbientDriver.ClockProfile(Source(15)), new(Guid.NewGuid())));
        Assert.AreEqual(GameSessionFailure.None, restoredResult.Failure);
        using var restored = restoredResult.Owner!;
        Assert.AreEqual(new SessionLightingState(3, 20), restored.QueryLighting());
        // A driver on the restored session selects the same scheme for the restored hour at once.
        var resumed = AreaAmbientDriver.Create(Source(15), restored, East);
        Assert.AreEqual((3, 20), (resumed.Selection.SchemeId, resumed.Selection.Hour));
        Assert.IsTrue(resumed.ModalPaused);
        resumed.Tick(1_000, false, East);
        resumed.Tick(1_100, false, East);
        Assert.AreEqual(72_000_800L, resumed.Clock.GameTimestampMs);
    }

    [TestMethod]
    public void Sector_of_world_cell_uses_sixty_four_cell_sectors()
    {
        Assert.AreEqual(new AreaSectorCoordinate(1452, 1290).Packed, AreaAmbientDriver.SectorOf(92928, 82560));
        Assert.AreEqual(new AreaSectorCoordinate(1452, 1290).Packed, AreaAmbientDriver.SectorOf(92991, 82623));
        Assert.AreEqual(new AreaSectorCoordinate(1453, 1291).Packed, AreaAmbientDriver.SectorOf(92992, 82624));
    }
}
