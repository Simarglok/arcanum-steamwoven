using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-080 (GI6 follow-up): the viewer's new sessions are seeded from the clock the way the original seeds its process generator,
// so a load right after a relaunch under the Original setting does not start from a fixed seed; tests and smokes keep seed 0.
[TestClass]
public sealed class AreaRandomSeedTests
{
    private static readonly Guid Module = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly ActorPlacement Outside = new(new(1), new(92910, 82598), NeighborDirection.XPlus);

    private static ProfileBinding Clock() => new(1, AreaAmbientDriver.ClockPolicy, AreaAmbientDriver.ClockSourceRevision,
        string.Concat(Enumerable.Repeat("aa", 32)), new string('c', 64), 1885, 15);

    private static GameSessionOwner Session(ulong? seed = null) => (seed is { } value
        ? AreaTransitionCoordinator.CreateSession(Clock(), Guid.NewGuid(), Module, new AreaStepCell(92928, 82560), 1, randomSeed: value)
        : AreaTransitionCoordinator.CreateSession(Clock(), Guid.NewGuid(), Module, new AreaStepCell(92928, 82560), 1)).Owner
        ?? throw new AssertFailedException("session");

    [TestMethod]
    public void The_clock_seed_is_whole_unix_seconds_in_32_bits_like_the_originals_time_call()
    {
        Assert.AreEqual(0UL, AreaRandomSeed.FromClock(DateTimeOffset.UnixEpoch));
        Assert.AreEqual(1_793_800_000UL, AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_000)));
        Assert.AreEqual(1_793_800_000UL, AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeMilliseconds(1_793_800_000_999)),
            "a fraction of a second does not move it, as `time(NULL)` returns whole seconds");
        Assert.AreEqual(5UL, AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds((1L << 32) + 5)), "a 32-bit counter wraps");
        Assert.AreNotEqual(AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_000)),
            AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_001)));
    }

    [TestMethod]
    public void A_test_or_smoke_launch_keeps_seed_zero_and_a_player_launch_takes_the_clock()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_793_800_000);
        Assert.AreEqual(0UL, AreaRandomSeed.ForNewSession(testLaunch: true, now));
        Assert.AreEqual(1_793_800_000UL, AreaRandomSeed.ForNewSession(testLaunch: false, now));
    }

    [TestMethod]
    public void A_session_made_without_a_seed_starts_at_zero_and_with_one_starts_there()
    {
        using var plain = Session();
        Assert.AreEqual(new SessionRandomState(0, 0), plain.QueryRandom());
        using var seeded = Session(1_793_800_000UL);
        Assert.AreEqual(new SessionRandomState(1_793_800_000UL, 0), seeded.QueryRandom());
    }

    [TestMethod]
    public void Two_launches_at_different_times_roll_differently_and_one_launch_rolls_the_same_again()
    {
        static int[] Rolls(ulong seed)
        {
            using var session = Session(seed);
            return Enumerable.Range(0, 8).Select(_ => session.Roll("test.launch", 1, 100).Roll!.Value).ToArray();
        }
        var first = AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_000));
        var later = AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_060));
        CollectionAssert.AreEqual(Rolls(first), Rolls(first));
        CollectionAssert.AreNotEqual(Rolls(first), Rolls(later));
    }

    [TestMethod]
    public void Under_Original_a_load_right_after_a_relaunch_goes_on_with_the_launchs_clock_seed_not_zero()
    {
        var directory = Directory.CreateTempSubdirectory("arc-randomseed-").FullName;
        try
        {
            var slot = GameSessionSaveSlot.QuickSave(directory);
            using (var saver = Session(7))
                Assert.IsTrue(AreaQuickSave.Save(saver, Outside, slot).Saved);
            // The relaunched viewer's main-menu placeholder session, seeded from the clock of that launch.
            var launch = AreaRandomSeed.FromClock(DateTimeOffset.FromUnixTimeSeconds(1_793_800_000));
            using var placeholder = Session(launch);
            Assert.AreEqual(GameSessionDisposition.Changed, placeholder.SelectRules(RuleMode.Original));
            using var loaded = AreaQuickSave.Load(slot, placeholder, _ => null).Owner ?? throw new AssertFailedException("load");
            Assert.AreEqual(new SessionRandomState(launch, 0), loaded.QueryRandom());
            Assert.AreNotEqual(0UL, loaded.QueryRandom().Seed);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
