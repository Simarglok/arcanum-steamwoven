using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-047 (owner decision D7, Steamwoven): the level steps a waiting follower missed while its sheet could not be read, kept in the
// session and applied when its map loads. Followers and schemes are invented; the half-elf hero and the tables are the shared fixtures'.
[TestClass]
public sealed class MissedLevelStepTests
{
    private static GameSessionOwner Owner(bool autoLevel = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with
        {
            Hero = HalfElf(), Experience = ExperienceStateTests.Rules(), AutoLevel = autoLevel ? AdvancementStateTests.Rules() : null,
        });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static GameSessionOwner Waiting(string entity = "virgil")
    {
        var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(), false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, entity).Disposition);
        return owner;
    }

    // Level 1, nothing to spend, scheme 62 ("dx 10").
    private static SessionNpcAdvancement Follower(string entity = "virgil", int level = 1) => new(entity, new(CharacterRace.Human, false,
        [.. Enumerable.Repeat(8, 8)], 0, 0, level, 0, [.. new int[12]], [.. new int[4]], [.. new int[16]], [.. new int[8]], 1, 2, 0, 0), 62);

    [TestMethod]
    public void Missed_steps_merge_and_apply_in_one_revision()
    {
        using var owner = Waiting();
        var revision = owner.Version.Revision;
        var first = owner.DeferFollowerLevel(owner.Version, "virgil", new(1, 3));
        Assert.AreEqual(GameSessionDisposition.Changed, first.Disposition);
        // A step that continues the last merges with it; the same step again counts once more.
        owner.DeferFollowerLevel(owner.Version, "virgil", new(3, 5));
        CollectionAssert.AreEqual(new MissedLevelStep[] { new(new(1, 5), 1) }, owner.QueryMissedLevelSteps("virgil")!.ToArray());
        Assert.AreEqual(revision + 2, owner.Version.Revision);

        var applied = owner.ApplyMissedFollowerLevels(owner.Version, Follower(), AdvancementModifiers.None);
        Assert.AreEqual(GameSessionDisposition.Changed, applied.Disposition);
        // Level 1 keeps its gap below the PC's old level 1: 5 + 1 − 1 = 5, with the points of levels 2..5 for its scheme.
        Assert.AreEqual(5, applied.Record!.Sheet.Level);
        Assert.AreEqual(5, owner.QueryNpcAdvancement("virgil")!.Sheet.Level);
        Assert.IsNull(owner.QueryMissedLevelSteps("virgil"));
        Assert.AreEqual(revision + 3, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyMissedFollowerLevels(owner.Version, Follower(), AdvancementModifiers.None).Disposition);
    }

    [TestMethod]
    public void Applying_missed_steps_matches_the_steps_in_turn()
    {
        // The last-level branch repeats 50 → 51: each repeat raises a follower below 50 once more.
        using var later = Waiting();
        foreach (var step in new FollowerLevelStep[] { new(45, 51), new(50, 51), new(50, 51) })
            Assert.AreEqual(GameSessionDisposition.Changed, later.DeferFollowerLevel(later.Version, "virgil", step).Disposition);
        CollectionAssert.AreEqual(new MissedLevelStep[] { new(new(45, 51), 1), new(new(50, 51), 2) }, later.QueryMissedLevelSteps("virgil")!.ToArray());
        var applied = later.ApplyMissedFollowerLevels(later.Version, Follower(level: 40), AdvancementModifiers.None);

        using var inTurn = Waiting();
        var record = Follower(level: 40);
        foreach (var step in new FollowerLevelStep[] { new(45, 51), new(50, 51), new(50, 51) })
            record = inTurn.LevelFollower(inTurn.Version, record, AdvancementModifiers.None, step).Record!;
        Assert.AreEqual(record, applied.Record!, "the same sheet, scheme purchases included");
        Assert.AreEqual(48, applied.Record!.Sheet.Level, "40 + 51 − 45 = 46, then 47 and 48");
        // At the repeat cap one more step changes nothing.
        for (var i = 0; i < ExperienceRules.MaxLevel; i++) later.DeferFollowerLevel(later.Version, "virgil", new(50, 51));
        Assert.AreEqual(GameSessionDisposition.NoChange, later.DeferFollowerLevel(later.Version, "virgil", new(50, 51)).Disposition);
    }

    [TestMethod]
    public void Deferring_is_refused_for_bad_requests()
    {
        using var owner = Waiting();
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, owner.DeferFollowerLevel(owner.Version, "virgil", new(3, 3)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, owner.DeferFollowerLevel(owner.Version, "virgil", new(3, 52)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, owner.DeferFollowerLevel(owner.Version, "", new(1, 2)).Failure);
        Assert.AreEqual(GameSessionFailure.NotInParty, owner.DeferFollowerLevel(owner.Version, "sogg", new(1, 2)).Failure, "a free NPC");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DeferFollowerLevel(new GameSessionVersion(owner.Identity, 0), "virgil", new(1, 2)).Failure);
        using var plain = Owner(autoLevel: false);
        Assert.AreEqual(GameSessionFailure.AdvancementUnavailable, plain.DeferFollowerLevel(plain.Version, "virgil", new(1, 2)).Failure);
        Assert.IsNull(owner.QueryMissedLevelSteps("virgil"));
    }

    [TestMethod]
    public void Missed_steps_survive_save_and_load()
    {
        using var owner = Waiting();
        owner.DeferFollowerLevel(owner.Version, "virgil", new(1, 3));
        owner.DeferFollowerLevel(owner.Version, "virgil", new(7, 9));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"missed\":[{\"entity\":\"virgil\",\"steps\":[{\"oldPcLevel\":1,\"newPcLevel\":3,\"times\":1},{\"oldPcLevel\":7,\"newPcLevel\":9,\"times\":1}]}]");
        using var restored = Restore(Decode(text), RestoreOptions() with { AutoLevel = AdvancementStateTests.Rules(), Experience = ExperienceStateTests.Rules() });
        CollectionAssert.AreEqual(owner.QueryMissedLevelSteps("virgil")!.ToArray(), restored.QueryMissedLevelSteps("virgil")!.ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // A count outside 1..51 is refused when decoding; a step that does not rise decodes but is refused by Restore.
        var bad = GameSessionSnapshotCodec.TryDecode(text.Replace("\"times\":1}]", "\"times\":0}]"));
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, bad.Failure);
        var flat = Decode(text.Replace("\"newPcLevel\":9", "\"newPcLevel\":7"));
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement,
            GameSessionOwner.Restore(flat, RestoreOptions() with { AutoLevel = AdvancementStateTests.Rules(), Experience = ExperienceStateTests.Rules() }).Failure);
    }
}
