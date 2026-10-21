using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// ARC-WORLD-030. The threshold table is invented: level n needs 1000·(n−1) + 10·(n−1)², so level 2 needs 1010, level 3 2040,
// level 50 73 010 and level 51 75 000.
[TestClass]
public sealed class ExperienceRulesTests
{
    internal static int[] Thresholds() => Enumerable.Range(1, ExperienceRules.MaxLevel).Select(n => 1000 * (n - 1) + 10 * (n - 1) * (n - 1)).ToArray();

    internal static ExperienceTable Table() => ExperienceTable.Create(Thresholds())!;

    private static int T(int level) => Table().Threshold(level);

    [TestMethod]
    public void TheTableNeedsFiftyOneStrictlyIncreasingLevelsFromZero()
    {
        Assert.AreEqual(1010, T(2));
        Assert.AreEqual(75_000, T(51));
        Assert.IsNull(ExperienceTable.Create(null));
        Assert.IsNull(ExperienceTable.Create(Thresholds()[..50]));
        Assert.IsNull(ExperienceTable.Create([.. Thresholds(), 60_000]));
        var shifted = Thresholds();
        shifted[0] = 5;
        Assert.IsNull(ExperienceTable.Create(shifted));
        var flat = Thresholds();
        flat[10] = flat[9];
        Assert.IsNull(ExperienceTable.Create(flat));
        var huge = Thresholds();
        huge[50] = ExperienceRules.MaxExperience + 1;
        Assert.IsNull(ExperienceTable.Create(huge));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Table().Threshold(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Table().Threshold(52));
    }

    // ARC-WORLD-033, ledger XP3. Level 2 needs 3,000,000, so at level 1 the step is 3,000,000 and 1000 × remaining stops fitting
    // in 32 bits above 2,147,483 remaining.
    [TestMethod]
    public void OriginalWrapsTheProgressProductAboveTheXp3BoundAndSteamwovenDoesNot()
    {
        var table = ExperienceTable.Create([0, .. Enumerable.Range(0, ExperienceRules.MaxLevel - 1).Select(i => 3_000_000 + 1000 * i)])!;
        int Progress(int remaining, ExperienceRuleProfile rules) => ExperienceRules.Progress(table, 1, 3_000_000 - remaining, rules);
        // Below the bound (1000 − 666) and at it (1000 × 2,147,483 = 2,147,483,000 fits; 1000 − 715), in both modes.
        Assert.AreEqual(334, Progress(2_000_000, ExperienceRuleProfile.Steamwoven));
        Assert.AreEqual(334, Progress(2_000_000, ExperienceRuleProfile.Original));
        Assert.AreEqual(285, Progress(2_147_483, ExperienceRuleProfile.Steamwoven));
        Assert.AreEqual(285, Progress(2_147_483, ExperienceRuleProfile.Original));
        // Just above: 2,147,484,000 wraps to −2,147,483,296; −2,147,483,296 / 3,000,000 = −715, so the original shows 1715.
        Assert.AreEqual(285, Progress(2_147_484, ExperienceRuleProfile.Steamwoven));
        Assert.AreEqual(1715, Progress(2_147_484, ExperienceRuleProfile.Original));
        // Far above (nothing earned yet): 3,000,000,000 wraps to −1,294,967,296, / 3,000,000 = −431, so 1431 against 0.
        Assert.AreEqual(0, Progress(3_000_000, ExperienceRuleProfile.Steamwoven));
        Assert.AreEqual(1431, Progress(3_000_000, ExperienceRuleProfile.Original));
        Assert.IsTrue(ExperienceRuleProfile.Original.ProgressProductWraps);
        Assert.IsFalse(ExperienceRuleProfile.Steamwoven.ProgressProductWraps);
        Assert.AreSame(ExperienceRuleProfile.Original, ExperienceRuleProfile.FromName("original"));
        Assert.IsNull(ExperienceRuleProfile.FromName("Original"));
    }

    [TestMethod]
    public void ToNextLevelAndProgressFollowTheSourceFormulas()
    {
        var table = Table();
        Assert.AreEqual(1010, ExperienceRules.ToNextLevel(table, 1, 0));
        Assert.AreEqual(0, ExperienceRules.ToNextLevel(table, 1, 1010));
        Assert.AreEqual(-5, ExperienceRules.ToNextLevel(table, 1, 1015));
        Assert.AreEqual(-7, ExperienceRules.ToNextLevel(table, 0, 7));
        Assert.AreEqual(ExperienceRules.NoNextLevel, ExperienceRules.ToNextLevel(table, 51, 0));

        // Level 2 → 3 is a step of 1030; at 1600 the PC still needs 440: 1000 − 440 000 / 1030 = 1000 − 427. Both modes agree
        // on every table step below the XP3 bound.
        foreach (var rules in ExperienceRuleProfile.All)
        {
            Assert.AreEqual(573, ExperienceRules.Progress(table, 2, 1600, rules));
            Assert.AreEqual(0, ExperienceRules.Progress(table, 2, 1010, rules));
            Assert.AreEqual(1000, ExperienceRules.Progress(table, 2, 2040, rules));
            Assert.AreEqual(ExperienceRules.ProgressFallback, ExperienceRules.Progress(table, 2, 2041, rules));
            Assert.AreEqual(ExperienceRules.ProgressFallback, ExperienceRules.Progress(table, 0, 0, rules));
            Assert.AreEqual(0, ExperienceRules.Progress(table, 51, 0, rules));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.Progress(table, 52, 0, rules));
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => ExperienceRules.Progress(table, 2, 1600, null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.ToNextLevel(table, -1, 0));
    }

    [TestMethod]
    public void EveryLevelGivesOnePointAndEveryFifthTwo()
    {
        Assert.AreEqual(1, ExperienceRules.BonusPoints(0, 1));
        Assert.AreEqual(1, ExperienceRules.BonusPoints(1, 2));
        Assert.AreEqual(2, ExperienceRules.BonusPoints(4, 5));
        Assert.AreEqual(11, ExperienceRules.BonusPoints(1, 10));
        Assert.AreEqual(0, ExperienceRules.BonusPoints(5, 5));
        Assert.AreEqual(0, ExperienceRules.BonusPoints(6, 5));
    }

    [TestMethod]
    public void AdvanceWritesEveryReachedLevelWithItsPoints()
    {
        var table = Table();
        Assert.AreEqual(new ExperienceAdvance(1, 1009, 5, 0, 0, null), ExperienceRules.Advance(table, 1, 1009, 5));
        Assert.AreEqual(new ExperienceAdvance(2, 1010, 6, 1, 1, new(1, 2)), ExperienceRules.Advance(table, 1, 1010, 5));
        // From 1 to 5 in one award: levels 2, 3, 4 give one point each, level 5 two.
        Assert.AreEqual(new ExperienceAdvance(5, T(5), 10, 4, 5, new(1, 5)), ExperienceRules.Advance(table, 1, T(5), 5));
        Assert.AreEqual(new ExperienceAdvance(5, T(6) - 1, 10, 4, 5, new(1, 5)), ExperienceRules.Advance(table, 1, T(6) - 1, 5));
        // Level 0 reaches level 1 at once.
        Assert.AreEqual(new ExperienceAdvance(1, 0, 1, 1, 1, new(0, 1)), ExperienceRules.Advance(table, 0, 0, 0));
    }

    [TestMethod]
    public void EveryLevelWrittenIsReportedBeforeItsPointsAreCapped()
    {
        // ARC-UI-046 (OL27): one report per level of the normal branch, with the unspent points stored before it.
        var written = new List<ExperienceLevelWritten>();
        ExperienceRules.Advance(Table(), 3, T(6), 54, written: written.Add);
        CollectionAssert.AreEqual(new ExperienceLevelWritten[] { new(4, 54, 1), new(5, 55, 2), new(6, 56, 1) }, written);
        // The scheme spends after the report: the next level's report sees what it left.
        written.Clear();
        ExperienceRules.Advance(Table(), 1, T(3), 5, (_, unspent) => 0, written.Add);
        CollectionAssert.AreEqual(new ExperienceLevelWritten[] { new(2, 5, 1), new(3, 0, 1) }, written);
        // The last-level branch writes no level and reports none.
        written.Clear();
        ExperienceRules.Advance(Table(), 49, T(51), 3, written: written.Add);
        CollectionAssert.AreEqual(new ExperienceLevelWritten[] { new(50, 3, 2) }, written);
        written.Clear();
        ExperienceRules.Advance(Table(), 50, T(51), 3, written: written.Add);
        Assert.AreEqual(0, written.Count);
    }

    [TestMethod]
    public void TheLevelMessageNamesThePointsBeforeTheCapOnlyInTheOriginalSetting()
    {
        // OL28 / D6: 55 stored plus a 2-point level is 57 in the original's message; Steamwoven names the 56 the PC holds.
        Assert.AreEqual(57, ExperienceRules.LevelMessagePoints(new(5, 55, 2), ExperienceRuleProfile.Original));
        Assert.AreEqual(56, ExperienceRules.LevelMessagePoints(new(5, 55, 2), ExperienceRuleProfile.Steamwoven));
        Assert.AreEqual(8, ExperienceRules.LevelMessagePoints(new(2, 7, 1), ExperienceRuleProfile.Original));
        Assert.AreEqual(8, ExperienceRules.LevelMessagePoints(new(2, 7, 1), ExperienceRuleProfile.Steamwoven));
        Assert.IsTrue(ExperienceRuleProfile.Original.LevelMessagePointsBeforeCap);
        Assert.IsFalse(ExperienceRuleProfile.Steamwoven.LevelMessagePointsBeforeCap);
    }

    [TestMethod]
    public void UnspentPointsStopAtFiftySix()
    {
        var advance = ExperienceRules.Advance(Table(), 7, T(10), 54);
        Assert.AreEqual((10, 56, 3, 4), (advance.Level, advance.UnspentPoints, advance.LevelsGained, advance.PointsGained));
    }

    [TestMethod]
    public void TheLastLevelBranchKeepsTheHeroAtFiftyWithItsThreshold()
    {
        var table = Table();
        Assert.AreEqual(new ExperienceAdvance(50, T(51) - 1, 0, 0, 0, null), ExperienceRules.Advance(table, 50, T(51) - 1, 0));
        // Reaching level 51's experience writes level 50's back; nothing else changes; followers still step to 51.
        Assert.AreEqual(new ExperienceAdvance(50, T(50), 3, 0, 0, new(50, 51)), ExperienceRules.Advance(table, 50, T(51), 3));
        Assert.AreEqual(new ExperienceAdvance(50, T(50), 3, 0, 0, new(50, 51)), ExperienceRules.Advance(table, 50, ExperienceRules.MaxExperience, 3));
        // From 48 past 51: levels 49 and 50 are written (1 + 2 points), then the last-level branch.
        Assert.AreEqual(new ExperienceAdvance(50, T(50), 3, 2, 3, new(48, 51)), ExperienceRules.Advance(table, 48, T(51) + 5, 0));
        Assert.AreEqual(new ExperienceAdvance(51, 7, 0, 0, 0, null), ExperienceRules.Advance(table, 51, 7, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.Advance(table, 1, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.Advance(table, 1, 0, 57));
    }

    [TestMethod]
    public void FollowersKeepTheirGapOrArePulledToThePcAndNeverReachTheMaximum()
    {
        Assert.AreEqual(new FollowerLevelUp(5, 3), ExperienceRules.FollowerLevel(3, new(4, 6)));
        Assert.AreEqual(new FollowerLevelUp(6, 3), ExperienceRules.FollowerLevel(4, new(4, 6)));
        Assert.AreEqual(new FollowerLevelUp(9, 0), ExperienceRules.FollowerLevel(9, new(4, 6)));
        Assert.AreEqual(new FollowerLevelUp(6, 1), ExperienceRules.FollowerLevel(5, new(4, 6)));
        Assert.AreEqual(new FollowerLevelUp(50, 2), ExperienceRules.FollowerLevel(49, new(50, 51)));
        Assert.AreEqual(new FollowerLevelUp(50, 0), ExperienceRules.FollowerLevel(50, new(50, 51)));
        Assert.AreEqual(new FollowerLevelUp(48, 0), ExperienceRules.FollowerLevel(48, new(40, 45)));
        Assert.AreEqual(new FollowerLevelUp(48, 5), ExperienceRules.FollowerLevel(44, new(40, 48)));
        Assert.AreEqual(new FollowerLevelUp(51, 0), ExperienceRules.FollowerLevel(51, new(1, 2)));
    }

    [TestMethod]
    public void DifficultyAddsHalfOnEasyAndTakesAQuarterOnHard()
    {
        Assert.AreEqual(101, ExperienceRules.AdjustForDifficulty(101, GameDifficulty.Normal));
        Assert.AreEqual(151, ExperienceRules.AdjustForDifficulty(101, GameDifficulty.Easy));
        Assert.AreEqual(-151, ExperienceRules.AdjustForDifficulty(-101, GameDifficulty.Easy));
        Assert.AreEqual(76, ExperienceRules.AdjustForDifficulty(101, GameDifficulty.Hard));
        Assert.AreEqual(75, ExperienceRules.AdjustForDifficulty(100, GameDifficulty.Hard));
        Assert.AreEqual(-6, ExperienceRules.AdjustForDifficulty(-7, GameDifficulty.Hard));
        Assert.AreEqual(3, ExperienceRules.AdjustForDifficulty(3, GameDifficulty.Hard));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.AdjustForDifficulty(1, (GameDifficulty)7));
    }

    [TestMethod]
    public void KillsPayAFifthAndHitsTheirShareOfThePool()
    {
        Assert.AreEqual(380, ExperienceRules.KillAward(1900));
        Assert.AreEqual(1, ExperienceRules.KillAward(7));
        Assert.AreEqual(0, ExperienceRules.KillAward(4));

        // 10 of 30 hit points is 33 %: 330 of a worth of 1000.
        Assert.AreEqual((330, 670), ExperienceRules.DamageShare(1000, 1000, 10, 30));
        Assert.AreEqual((100, 0), ExperienceRules.DamageShare(1000, 100, 10, 30));
        Assert.AreEqual((0, 0), ExperienceRules.DamageShare(1000, 0, 10, 30));
        Assert.AreEqual((0, 500), ExperienceRules.DamageShare(1000, 500, 0, 30));
        // A hit above the maximum counts above 100 %.
        Assert.AreEqual((500, 0), ExperienceRules.DamageShare(200, 500, 90, 30));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ExperienceRules.DamageShare(1, 1, 1, 0));
    }

    [TestMethod]
    public void TheWaitingFollowersRectangleReachesTenTilesInTheOriginalSetting()
    {
        // ARC-WORLD-047 (OL29, D7): the original's 800 × 400 play view reaches 10 tiles from the PC on both axes.
        Assert.AreEqual(10, ExperienceRules.WaitingStepReach());
        Assert.AreEqual(12, ExperienceRules.WaitingStepReach(800, 600));
        Assert.AreEqual(16, ExperienceRules.WaitingStepReach(1024, 768));
        var original = ExperienceRuleProfile.Original;
        Assert.IsTrue(ExperienceRules.WaitingFollowerGetsStep((10, -10), false, original));
        Assert.IsFalse(ExperienceRules.WaitingFollowerGetsStep((11, 0), false, original));
        Assert.IsFalse(ExperienceRules.WaitingFollowerGetsStep((0, -11), false, original));
        Assert.IsFalse(ExperienceRules.WaitingFollowerGetsStep(null, false, original), "not on the loaded map");
        Assert.IsFalse(ExperienceRules.WaitingFollowerGetsStep((0, 0), true, original), "dead");
        // Steamwoven: every waiting follower wherever it waits, never a dead one.
        var steamwoven = ExperienceRuleProfile.Steamwoven;
        Assert.IsTrue(ExperienceRules.WaitingFollowerGetsStep(null, false, steamwoven));
        Assert.IsTrue(ExperienceRules.WaitingFollowerGetsStep((400, 0), false, steamwoven));
        Assert.IsFalse(ExperienceRules.WaitingFollowerGetsStep(null, true, steamwoven));
        Assert.IsTrue(original.WaitingFollowersStepOnlyNearPc);
        Assert.IsFalse(steamwoven.WaitingFollowersStepOnlyNearPc);
    }

    [TestMethod]
    public void MissedStepsAppliedLaterGiveTheSameLevelsAndPointsAsInTurn()
    {
        // Every PC level history the recalculation can produce: rising levels up to 50 (each award's step continues the last), then
        // the step into the last-level branch (to 51) and its repeats (50 → 51). Applying the recorded list later gives every follower
        // level the same level and points as the steps one after the other.
        IEnumerable<FollowerLevelStep[]> Histories()
        {
            for (var start = 1; start <= 50; start++)
            for (var mid = start + 1; mid <= 51; mid++)
            for (var repeats = 0; repeats <= 3; repeats++)
            {
                var steps = new List<FollowerLevelStep> { new(start, mid) };
                if (mid < 51) steps.Add(new(mid, Math.Min(51, mid + 3)));
                // The repeat comes only once the PC is in the last-level branch (its last step reached 51).
                if (repeats > 0 && steps[^1].NewPcLevel != 51) continue;
                for (var i = 0; i < repeats; i++) steps.Add(new(50, 51));
                yield return steps.ToArray();
            }
        }
        foreach (var history in Histories())
        {
            IReadOnlyList<MissedLevelStep> recorded = [];
            foreach (var step in history) recorded = ExperienceRules.AppendMissedStep(recorded, step)!;
            Assert.IsTrue(recorded.Count <= history.Length, "one entry per step at most: " + string.Join(", ", recorded));
            for (var level = 1; level < ExperienceRules.MaxLevel; level++)
            {
                int Apply(IEnumerable<FollowerLevelStep> steps, out int points)
                {
                    var (now, sum) = (level, 0);
                    foreach (var step in steps) { var up = ExperienceRules.FollowerLevel(now, step); sum += up.PointsGained; now = up.Level; }
                    points = sum;
                    return now;
                }
                var inTurn = Apply(history, out var inTurnPoints);
                var later = Apply(recorded.SelectMany(e => Enumerable.Repeat(e.Step, e.Times)), out var laterPoints);
                Assert.AreEqual((inTurn, inTurnPoints), (later, laterPoints), $"level {level}, history {string.Join(", ", history)}");
            }
        }
        // The repeat count stops at the maximum level and loses nothing: 60 repeats in turn raise a level-0 follower as the capped 51 do.
        IReadOnlyList<MissedLevelStep> many = [];
        for (var i = 0; i < 60; i++) many = ExperienceRules.AppendMissedStep(many, new(50, 51))!;
        Assert.AreEqual(new MissedLevelStep(new(50, 51), ExperienceRules.MaxLevel), many.Single());
        var (sixty, capped) = (0, 0);
        for (var i = 0; i < 60; i++) sixty = ExperienceRules.FollowerLevel(sixty, new(50, 51)).Level;
        for (var i = 0; i < many.Single().Times; i++) capped = ExperienceRules.FollowerLevel(capped, new(50, 51)).Level;
        Assert.AreEqual(sixty, capped);
        IReadOnlyList<MissedLevelStep> full = Enumerable.Range(0, ExperienceRules.MaxMissedStepEntries).Select(i => new MissedLevelStep(new(i % 50, i % 50 + 1), 1)).ToArray();
        Assert.IsNull(ExperienceRules.AppendMissedStep(full, new(7, 9)));
        // Every rising history from level 1 to the last-level branch with repeats fits.
        IReadOnlyList<MissedLevelStep> longest = [];
        for (var level = 1; level < 50; level++) longest = ExperienceRules.AppendMissedStep(longest, new(level, level + 1))!;
        longest = ExperienceRules.AppendMissedStep(longest, new(50, 51))!;
        longest = ExperienceRules.AppendMissedStep(longest, new(50, 51))!;
        Assert.AreEqual(50, longest.Count);
    }
}
