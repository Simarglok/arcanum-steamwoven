using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's experience and the NPCs' worth and pool (ARC-WORLD-030). Tables are invented: level n needs
// 1000·(n−1) + 10·(n−1)² (level 2 1010, level 3 2040, level 5 4160, level 50 73 010, level 51 75 000); quest experience
// level 1 gives 700 and level 5 1300; quest 1003 is level 5, every other quest level 1. The half-elf fixture hero starts at
// level 1 with 5 unspent points and 30 hit points.
[TestClass]
public sealed class ExperienceStateTests
{
    private static int T(int level) => 1000 * (level - 1) + 10 * (level - 1) * (level - 1);

    internal static SessionExperienceRules Rules() => SessionExperienceRules.Create(
        ExperienceTable.Create(Enumerable.Range(1, 51).Select(T).ToArray()),
        new Dictionary<int, int> { [1] = 700, [5] = 1300 }, new Dictionary<int, int> { [1003] = 5 })!;

    /// <summary>The same level table with small quest experience (level 1 gives 100, level 5 gives 1300): several awards of a move stay under level 2 (1010).</summary>
    internal static SessionExperienceRules SmallQuestRules() => SessionExperienceRules.Create(
        ExperienceTable.Create(Enumerable.Range(1, 51).Select(T).ToArray()),
        new Dictionary<int, int> { [1] = 100, [5] = 1300 }, new Dictionary<int, int> { [1003] = 5 })!;

    private static GameSessionOwner Owner(SessionHeroDefinition? hero = null, bool rules = true, GameSessionLimits? limits = null,
        GameDifficulty difficulty = GameDifficulty.Normal)
    {
        var result = GameSessionOwner.Create(Descriptor(limits) with
        {
            Hero = hero ?? HalfElf(), Experience = rules ? Rules() : null, Difficulty = difficulty,
        });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionHeroDefinition AtLevel(int level, int experience, int unspent = 0) =>
        HalfElf() with { Level = level, Experience = experience, UnspentPoints = unspent };

    private static SessionExperienceResult Changed(SessionExperienceResult result, GameSessionOwner owner)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(SessionExperienceResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.IsNull(result.Award);
        Assert.AreEqual(revision, result.Version.Revision);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    private static (int Level, int Experience, int Unspent) Hero(GameSessionOwner owner)
    {
        var view = owner.QueryHeroExperience()!;
        return (view.Level, view.Experience, view.UnspentPoints);
    }

    // ARC-WORLD-033: the hero's progress reads the session's rules when it is asked for (ledger XP3), with no revision.
    [TestMethod]
    public void HeroProgressFollowsTheRuleModeAtTheMomentItIsRead()
    {
        var wide = SessionExperienceRules.Create(
            ExperienceTable.Create([0, .. Enumerable.Range(0, 50).Select(i => 3_000_000 + 1000 * i)]),
            new Dictionary<int, int>(), new Dictionary<int, int>())!;
        var result = GameSessionOwner.Create(Descriptor() with { Hero = AtLevel(1, 0), Experience = wide });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        var revision = owner.Version.Revision;
        Assert.AreEqual(0, owner.QueryHeroExperience()!.Progress);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Assert.AreEqual(1431, owner.QueryHeroExperience()!.Progress);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Steamwoven));
        Assert.AreEqual(0, owner.QueryHeroExperience()!.Progress);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void AnAwardAddsExperienceAndWritesEveryLevelReached()
    {
        using var owner = Owner();
        Assert.AreEqual(new SessionExperienceView(1, 0, 5, 1010, 0), owner.QueryHeroExperience());
        var small = Changed(owner.AwardHeroExperience(owner.Version, 500), owner);
        Assert.AreEqual(new SessionExperienceAward(500, 0, 0, null), small.Award);
        Assert.IsNull(small.Npc);
        Assert.AreEqual((1, 500, 5), Hero(owner));
        Assert.AreEqual(1, owner.Version.Revision);

        // 500 + 3700 = 4200 reaches levels 2, 3, 4 and 5 (one point each, two for level 5).
        var big = Changed(owner.AwardHeroExperience(owner.Version, 3700), owner);
        Assert.AreEqual(new SessionExperienceAward(3700, 4, 5, new(1, 5)), big.Award! with { LevelUps = null });
        // ARC-UI-046: one level-up message per level written, with the points held after it.
        CollectionAssert.AreEqual(new SessionLevelUp[] { new(2, 6, null), new(3, 7, null), new(4, 8, null), new(5, 10, null) }, big.Award!.LevelUps!.ToArray());
        Assert.AreEqual((5, 4200, 10), Hero(owner));
        Assert.AreEqual(new SessionExperienceView(5, 4200, 10, T(6) - 4200, 1000 - 1000 * (T(6) - 4200) / (T(6) - T(5))),
            owner.QueryHeroExperience());
        Assert.AreEqual(5, owner.QueryHero()!.Level);
        Assert.AreEqual(4200, owner.Capture().Hero!.Experience);
    }

    [TestMethod]
    public void LevelsRaiseTheHerosMaximumHitPointsAndKeepItsDamage()
    {
        using var owner = Owner();
        var before = owner.QueryHeroLife()!.Status.MaxHitPoints;
        owner.AwardHeroExperience(owner.Version, T(3));
        Assert.AreEqual(before + 4, owner.QueryHeroLife()!.Status.MaxHitPoints);
        Assert.AreEqual(0, owner.QueryHeroLife()!.Record.State.HitPointDamage);
    }

    [TestMethod]
    public void ZeroNegativeAndClampedAwardsFollowTheSource()
    {
        using var owner = Owner(AtLevel(3, 2200));
        var zero = owner.AwardHeroExperience(owner.Version, 0);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionExperienceAward?)null, 0L), (zero.Disposition, zero.Award, owner.Version.Revision));
        // Experience can fall, never below 0; levels never fall.
        Changed(owner.AwardHeroExperience(owner.Version, -5000), owner);
        Assert.AreEqual((3, 0, 0), Hero(owner));
        var floor = owner.AwardHeroExperience(owner.Version, -1);
        Assert.AreEqual((GameSessionDisposition.NoChange, -1), (floor.Disposition, floor.Award!.Award));
        Refused(owner.AwardHeroExperience(owner.Version, SessionExperienceLimits.MaxAward + 1), owner, GameSessionFailure.InvalidExperience, 1);
        Refused(owner.AwardHeroExperience(owner.Version, -SessionExperienceLimits.MaxAward - 1), owner, GameSessionFailure.InvalidExperience, 1);
        Refused(owner.AwardHeroExperience(new(owner.Identity, 0), 10), owner, GameSessionFailure.StaleVersion, 1);
    }

    [TestMethod]
    public void TheHeroStaysAtFiftyAndFallsBackToItsThresholdAtTheLastLevel()
    {
        using var owner = Owner(AtLevel(50, T(50) + 100, 7));
        var below = Changed(owner.AwardHeroExperience(owner.Version, T(51) - T(50) - 101), owner);
        Assert.IsNull(below.Award!.Followers);
        Assert.AreEqual((50, T(51) - 1, 7), Hero(owner));
        var last = Changed(owner.AwardHeroExperience(owner.Version, 1), owner);
        Assert.AreEqual(new SessionExperienceAward(1, 0, 0, new(50, 51)), last.Award);
        Assert.AreEqual((50, T(50), 7), Hero(owner));
    }

    [TestMethod]
    public void UnspentPointsStopAtFiftySix()
    {
        using var owner = Owner(AtLevel(1, 0, 55));
        var award = Changed(owner.AwardHeroExperience(owner.Version, T(5)), owner).Award!;
        Assert.AreEqual((4, 5), (award.LevelsGained, award.PointsGained));
        Assert.AreEqual((5, T(5), 56), Hero(owner));
    }

    [TestMethod]
    public void DifficultyIsASettingThatChangesAwards()
    {
        using var owner = Owner(difficulty: GameDifficulty.Easy);
        Assert.AreEqual(GameDifficulty.Easy, owner.Difficulty);
        Assert.AreEqual(151, owner.AwardHeroExperience(owner.Version, 101).Award!.Award);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectDifficulty(GameDifficulty.Hard));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SelectDifficulty(GameDifficulty.Hard));
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.SelectDifficulty((GameDifficulty)9));
        Assert.AreEqual((GameDifficulty.Hard, revision), (owner.Difficulty, owner.Version.Revision));
        Assert.AreEqual(76, owner.AwardHeroExperience(owner.Version, 101).Award!.Award);
        Assert.AreEqual(227, Hero(owner).Experience);

        Assert.AreEqual(GameSessionFailure.InvalidDescriptor,
            GameSessionOwner.Create(Descriptor() with { Difficulty = (GameDifficulty)9 }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDescriptor,
            GameSessionOwner.Restore(owner.Capture(), RestoreOptions() with { Difficulty = (GameDifficulty)9 }).Failure);
        using var restored = Restore(owner.Capture(), RestoreOptions() with { Difficulty = GameDifficulty.Easy });
        Assert.AreEqual(GameDifficulty.Easy, restored.Difficulty);
        using var plain = Restore(owner.Capture());
        Assert.AreEqual(GameDifficulty.Normal, plain.Difficulty);
    }

    [TestMethod]
    public void TheHerosExperienceEffectsChangeTheAwardAfterTheDifficulty()
    {
        using var owner = Owner(difficulty: GameDifficulty.Easy);
        var learned = new SessionEffectDefinition(350, [new EffectChange(EffectAttributeKind.Special, 9, EffectChangeOperator.Percent, 20)]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, learned, EffectCause.Background).Disposition);
        // 200 on easy is 300, and +20 % is 360.
        Assert.AreEqual(360, owner.AwardHeroExperience(owner.Version, 200).Award!.Award);
    }

    [TestMethod]
    public void ADeadHeroGetsNothing()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        var revision = owner.Version.Revision;
        var award = owner.AwardHeroExperience(owner.Version, 5000);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionExperienceAward?)null, revision), (award.Disposition, award.Award, owner.Version.Revision));
        Assert.AreEqual((1, 0, 5), Hero(owner));
    }

    [TestMethod]
    public void WithoutAHeroOrDataNothingIsAwarded()
    {
        using var noData = Owner(rules: false);
        Assert.IsNull(noData.Experience);
        Assert.AreEqual(new SessionExperienceView(1, 0, 5, null, null), noData.QueryHeroExperience());
        Refused(noData.AwardHeroExperience(noData.Version, 10), noData, GameSessionFailure.ExperienceUnavailable, 0);
        Refused(noData.AwardQuestExperience(noData.Version, 1), noData, GameSessionFailure.ExperienceUnavailable, 0);
        Refused(noData.RecordNpcDamage(noData.Version, new("npc-1", 100, 100), 5, 10, false), noData, GameSessionFailure.ExperienceUnavailable, 0);
        // A completed quest still completes, without experience.
        SessionFixtures.Changed(noData.SetQuestState(noData.Version, 1003, SessionQuestState.Completed, out var none), noData, 1);
        Assert.IsNull(none);
        Assert.AreEqual(0, Hero(noData).Experience);

        using var noHero = GameSessionOwner.Create(Descriptor() with { Experience = Rules() }).Owner!;
        Assert.IsNull(noHero.QueryHeroExperience());
        Refused(noHero.AwardHeroExperience(noHero.Version, 10), noHero, GameSessionFailure.ExperienceUnavailable, 0);
        Refused(noHero.RecordNpcKill(noHero.Version, new("npc-1", 100, 100), true), noHero, GameSessionFailure.ExperienceUnavailable, 0);
        // Another attacker's hit needs no hero: the pool still empties.
        Changed(noHero.RecordNpcDamage(noHero.Version, new("npc-1", 100, 100), 5, 10, false), noHero);
        Assert.AreEqual(new SessionNpcExperience("npc-1", 100, 50), noHero.QueryNpcExperience("npc-1"));
        SessionFixtures.Changed(noHero.SetQuestState(noHero.Version, 1003, SessionQuestState.Completed), noHero, 2);
    }

    [TestMethod]
    public void QuestExperienceComesFromTheQuestTables()
    {
        using var owner = Owner();
        Assert.AreEqual(700, Changed(owner.AwardQuestExperience(owner.Version, 1), owner).Award!.Award);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AwardQuestExperience(owner.Version, 77).Disposition);
        Assert.AreEqual(700, Hero(owner).Experience);

        // Completing quest 1003 (level 5) gives 1300 in the quest change's revision; quest 1004 is level 1.
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Accepted, out var accepted), owner, 2);
        Assert.IsNull(accepted);
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Completed, out var completed), owner, 3);
        Assert.AreEqual(new SessionExperienceAward(1300, 1, 1, new(1, 2)), completed! with { LevelUps = null });
        CollectionAssert.AreEqual(new SessionLevelUp[] { new(2, 6, null) }, completed!.LevelUps!.ToArray(), "a quest's experience posts its level-up message too");
        Assert.AreEqual((2, 2000, 6), Hero(owner));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetQuestState(owner.Version, 1003, SessionQuestState.Completed).Disposition);
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1004, SessionQuestState.Completed), owner, 4);
        Assert.AreEqual(2700, Hero(owner).Experience);
        Assert.AreEqual(SessionQuestState.Completed, owner.QueryQuest(1004)!.Value.Effective);
    }

    [TestMethod]
    public void QuestExperienceFollowsTheRequestNotTheStoredState()
    {
        using var owner = Owner();
        // A quest another PC completed: the request for Completed is stored as OtherCompleted and still pays.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture())
            .Replace("\"globalQuests\":[]", "\"globalQuests\":[{\"quest\":1005,\"state\":\"Completed\"}]", StringComparison.Ordinal);
        using var shared = Restore(Decode(text), RestoreOptions() with { Experience = Rules() });
        SessionFixtures.Changed(shared.SetQuestState(shared.Version, 1005, SessionQuestState.Completed, out var other), shared, 1);
        Assert.AreEqual(SessionQuestState.OtherCompleted, shared.QueryQuest(1005)!.Value.Effective);
        Assert.AreEqual(700, other!.Award);
        Assert.AreEqual(700, Hero(shared).Experience);

        // A request for OtherCompleted pays nothing; a botched quest takes no completion.
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1006, SessionQuestState.OtherCompleted, out var none), owner, 1);
        Assert.IsNull(none);
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1007, SessionQuestState.Botched), owner, 2);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetQuestState(owner.Version, 1007, SessionQuestState.Completed, out var botched).Disposition);
        Assert.IsNull(botched);
        Assert.AreEqual(0, Hero(owner).Experience);

        // A dead hero's completion still completes the quest, without experience.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        SessionFixtures.Changed(owner.SetQuestState(owner.Version, 1008, SessionQuestState.Completed, out var dead), owner, 4);
        Assert.IsNull(dead);
        Assert.AreEqual(SessionQuestState.Completed, owner.QueryQuest(1008)!.Value.Effective);
        Assert.AreEqual(0, Hero(owner).Experience);
    }

    [TestMethod]
    public void AnAwardThatChangesNothingStillStepsTheFollowersAtTheLastLevel()
    {
        using var owner = Owner(AtLevel(50, T(50)));
        // Reaching level 51's experience again writes level 50's threshold back: the hero is unchanged (no revision), but the
        // source still runs the followers' update, so the step is reported with NoChange.
        var again = owner.AwardHeroExperience(owner.Version, T(51) - T(50));
        Assert.AreEqual((GameSessionDisposition.NoChange, 0L), (again.Disposition, owner.Version.Revision));
        Assert.AreEqual(new SessionExperienceAward(T(51) - T(50), 0, 0, new(50, 51)), again.Award);
        Assert.AreEqual((50, T(50), 0), Hero(owner));
    }

    [TestMethod]
    public void ANewHeroOutsideTheExperienceRangeIsRefused()
    {
        foreach (var experience in new[] { -1, SessionHeroLimits.MaxExperience + 1 })
            Assert.AreEqual(GameSessionFailure.InvalidHero,
                GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Experience = experience } }).Failure);
        using var top = Owner(HalfElf() with { Experience = SessionHeroLimits.MaxExperience });
        Assert.AreEqual(SessionHeroLimits.MaxExperience, Hero(top).Experience);
    }

    [TestMethod]
    public void HitsTakeTheirShareOfThePoolAndOnlyTheHerosPay()
    {
        using var owner = Owner();
        var loaded = new SessionNpcExperience("npc-1", 1000, 1000);
        // A follower's hit of 10 of 30 hit points takes 330 from the pool for nobody.
        var follower = Changed(owner.RecordNpcDamage(owner.Version, loaded, 10, 30, false), owner);
        Assert.AreEqual((null, new SessionNpcExperience("npc-1", 1000, 670)), (follower.Award, follower.Npc));
        Assert.AreEqual(0, Hero(owner).Experience);
        // The record wins over the loaded values from now on.
        var hero = Changed(owner.RecordNpcDamage(owner.Version, loaded, 20, 30, true), owner);
        Assert.AreEqual((660, new SessionNpcExperience("npc-1", 1000, 10)), (hero.Award!.Award, hero.Npc));
        Assert.AreEqual(660, Hero(owner).Experience);
        Assert.AreEqual(10, Changed(owner.RecordNpcDamage(owner.Version, loaded, 30, 30, true), owner).Award!.Award);
        var empty = owner.RecordNpcDamage(owner.Version, loaded, 30, 30, true);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionExperienceAward?)null, 0), (empty.Disposition, empty.Award, empty.Npc!.Value.Pool));
        Assert.AreEqual(670, Hero(owner).Experience);

        var revision = owner.Version.Revision;
        Refused(owner.RecordNpcDamage(owner.Version, loaded with { Entity = "bad id" }, 1, 30, true), owner, GameSessionFailure.InvalidExperienceTarget, revision);
        Refused(owner.RecordNpcDamage(owner.Version, loaded with { Worth = -1 }, 1, 30, true), owner, GameSessionFailure.InvalidExperienceTarget, revision);
        Refused(owner.RecordNpcDamage(owner.Version, loaded with { Pool = SessionExperienceLimits.MaxWorth + 1 }, 1, 30, true), owner,
            GameSessionFailure.InvalidExperienceTarget, revision);
        Refused(owner.RecordNpcDamage(owner.Version, loaded, -1, 30, true), owner, GameSessionFailure.InvalidExperience, revision);
        Refused(owner.RecordNpcDamage(owner.Version, loaded, 1, 0, true), owner, GameSessionFailure.InvalidExperience, revision);
        Refused(owner.RecordNpcDamage(new(owner.Identity, 0), loaded, 1, 30, true), owner, GameSessionFailure.StaleVersion, revision);
    }

    [TestMethod]
    public void AKillPaysAFifthOfTheWorthOnce()
    {
        using var owner = Owner();
        var loaded = new SessionNpcExperience("npc-2", 1900, 1900);
        var other = owner.RecordNpcKill(owner.Version, loaded, false);
        Assert.AreEqual((GameSessionDisposition.NoChange, loaded), (other.Disposition, other.Npc!.Value));
        var kill = Changed(owner.RecordNpcKill(owner.Version, loaded, true), owner);
        Assert.AreEqual((380, new SessionNpcExperience("npc-2", 0, 1900)), (kill.Award!.Award, kill.Npc));
        Assert.AreEqual(380, Hero(owner).Experience);
        var again = owner.RecordNpcKill(owner.Version, loaded, true);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SessionExperienceAward?)null), (again.Disposition, again.Award));

        // A dead hero gets nothing, but the worth is still spent.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        var dead = Changed(owner.RecordNpcKill(owner.Version, new("npc-3", 500, 500), true), owner);
        Assert.AreEqual(((SessionExperienceAward?)null, new SessionNpcExperience("npc-3", 0, 500)), (dead.Award, dead.Npc));
        Assert.AreEqual(380, Hero(owner).Experience);
    }

    [TestMethod]
    public void NewNpcRecordsStopAtTheBudget()
    {
        using var owner = Owner(limits: new(maxExperienceRecords: 1));
        Changed(owner.RecordNpcKill(owner.Version, new("npc-1", 100, 100), true), owner);
        var revision = owner.Version.Revision;
        Refused(owner.RecordNpcKill(owner.Version, new("npc-2", 100, 100), true), owner, GameSessionFailure.BudgetExceeded, revision);
        Assert.AreEqual(20, Hero(owner).Experience);
        // The recorded NPC is not refused: its spent worth makes the hit worth nothing.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordNpcDamage(owner.Version, new("npc-1", 100, 100), 10, 10, false).Disposition);
    }

    [TestMethod]
    public void ExperienceRoundTripsThroughTheSnapshot()
    {
        using var owner = Owner();
        owner.AwardHeroExperience(owner.Version, 1500);
        owner.RecordNpcDamage(owner.Version, new("npc-b", 100, 100), 5, 10, false);
        owner.RecordNpcKill(owner.Version, new("npc-a", 50, 40), true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"level\":2,\"experience\":1510,\"unspentPoints\":6,");
        StringAssert.Contains(text, ",\"experience\":{\"npcs\":[{\"entity\":\"npc-a\",\"worth\":0,\"pool\":40},{\"entity\":\"npc-b\",\"worth\":100,\"pool\":50}]},");
        using var restored = Restore(Decode(text), RestoreOptions() with { Experience = Rules() });
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(owner.QueryHeroExperience(), restored.QueryHeroExperience());
        Assert.AreEqual(new SessionNpcExperience("npc-a", 0, 40), restored.QueryNpcExperience("npc-a"));
        Assert.IsNotNull(restored.Experience);
        using var withoutData = Restore(Decode(text));
        Assert.IsNull(withoutData.Experience);
        Assert.AreEqual(1510, Hero(withoutData).Experience);
    }

    [TestMethod]
    public void DecodeAndRestoreRefuseExperienceNoCallProduces()
    {
        using var owner = Owner();
        owner.RecordNpcKill(owner.Version, new("npc-a", 50, 40), true);
        owner.RecordNpcKill(owner.Version, new("npc-b", 50, 40), true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string records = "{\"entity\":\"npc-a\",\"worth\":0,\"pool\":40},{\"entity\":\"npc-b\",\"worth\":0,\"pool\":40}";
        StringAssert.Contains(text, records);

        void DecodeFails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        DecodeFails(text.Replace(records, "{\"entity\":\"npc-b\",\"worth\":0,\"pool\":40},{\"entity\":\"npc-a\",\"worth\":0,\"pool\":40}"),
            SnapshotDecodeFailure.NotCanonical, "$.experience.npcs[1].entity");
        DecodeFails(text.Replace(records, "{\"entity\":\"npc-a\",\"worth\":0}"), SnapshotDecodeFailure.MissingField, "$.experience.npcs[0].pool");
        DecodeFails(text.Replace("\"experience\":20,", "\"experience\":20.5,"), SnapshotDecodeFailure.OutOfRange, "$.hero.experience");

        void RestoreFails(string damaged, GameSessionFailure failure, GameSessionLimits? limits = null) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(damaged), RestoreOptions(limits)).Failure);
        RestoreFails(text.Replace("\"worth\":0,\"pool\":40},{", "\"worth\":-1,\"pool\":40},{"), GameSessionFailure.InvalidExperienceTarget);
        RestoreFails(text.Replace("\"worth\":0,\"pool\":40}]", "\"worth\":0,\"pool\":100000001}]"), GameSessionFailure.InvalidExperienceTarget);
        RestoreFails(text, GameSessionFailure.BudgetExceeded, new(maxExperienceRecords: 1));
        RestoreFails(text.Replace("\"experience\":20,", "\"experience\":-1,"), GameSessionFailure.InvalidHero);
        RestoreFails(text.Replace("\"experience\":20,", "\"experience\":2000000001,"), GameSessionFailure.InvalidHero);
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(text.Replace("\"experience\":20,", "\"experience\":2000000000,")),
            RestoreOptions()).Failure);
    }

    [TestMethod]
    public void TheRevisionCannotOverflow()
    {
        using var owner = Owner();
        var text = System.Text.RegularExpressions.Regex.Replace(GameSessionSnapshotCodec.Encode(owner.Capture()),
            "(\"module\":\"[0-9a-f-]+\",\"revision\":)0,", "${1}9223372036854775807,");
        using var full = Restore(Decode(text), RestoreOptions() with { Experience = Rules() });
        Refused(full.AwardHeroExperience(full.Version, 10), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Refused(full.RecordNpcKill(full.Version, new("npc-1", 100, 100), true), full, GameSessionFailure.RevisionExhausted, long.MaxValue);
        Assert.AreEqual(GameSessionDisposition.NoChange, full.AwardHeroExperience(full.Version, 0).Disposition);
        Assert.AreEqual((1, 0, 5), Hero(full));
        Assert.IsNull(full.QueryNpcExperience("npc-1"));
    }

    [TestMethod]
    public void TheRulesRefuseDataOutsideTheirBounds()
    {
        var table = ExperienceTable.Create(Enumerable.Range(1, 51).Select(T).ToArray());
        var none = new Dictionary<int, int>();
        Assert.IsNull(SessionExperienceRules.Create(null, none, none));
        Assert.IsNull(SessionExperienceRules.Create(table, null, none));
        Assert.IsNull(SessionExperienceRules.Create(table, none, null));
        Assert.IsNull(SessionExperienceRules.Create(table, new Dictionary<int, int> { [1] = SessionExperienceLimits.MaxAward + 1 }, none));
        Assert.IsNull(SessionExperienceRules.Create(table, none, new Dictionary<int, int> { [999] = 1 }));
        var rules = SessionExperienceRules.Create(table, new Dictionary<int, int> { [1] = -SessionExperienceLimits.MaxAward }, none)!;
        Assert.AreEqual((-SessionExperienceLimits.MaxAward, 0, 1), (rules.QuestExperience(1), rules.QuestExperience(2), rules.QuestLevel(1500)));
    }
}
