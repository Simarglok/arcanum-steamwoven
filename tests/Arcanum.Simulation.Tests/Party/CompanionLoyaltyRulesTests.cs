using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Party;

// ARC-WORLD-045: the wait timer and loyalty rules (ARC-RES-064 OL17–OL24). The AI row is made up (dislike ≤ −20, alignment
// gaps 300/200, level gate 5), as in FollowConsentRulesTests.
[TestClass]
public sealed class CompanionLoyaltyRulesTests
{
    private const long Minute = 60_000, Hour = 60 * Minute, Day = 24 * Hour;
    private static readonly FollowThresholds Row = new(-20, 300, 200, 5);

    private static LeaderCheckInput Check(CompanionLoyalty armed) => new(armed, MindControlledByLeader: false,
        LeaderPersuasion: PersuasionTraining.None, ForcedFollower: false, Reaction: 50, Thresholds: Row, LeaderAlignment: 0, NpcAlignment: 0);

    private const CompanionLoyalty Reaction = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestReaction;
    private const CompanionLoyalty Good = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood;
    private const CompanionLoyalty Bad = CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooBad;

    [TestMethod]
    public void Each_wait_index_has_its_duration_and_any_persuasion_training_adds_one_step()
    {
        long?[] plain = [0, 15 * Minute, 2 * Hour, Day, 7 * Day, 30 * Day, null, null];
        for (var index = 0; index < plain.Length; index++)
            Assert.AreEqual(plain[index], CompanionLoyaltyRules.WaitTimerMs(index, PersuasionTraining.None, 8, false), $"index {index}");
        foreach (var training in new[] { PersuasionTraining.Apprentice, PersuasionTraining.Expert, PersuasionTraining.Master })
        {
            Assert.AreEqual(15 * Minute, CompanionLoyaltyRules.WaitTimerMs(0, training, 8, false), $"{training}: index 0 becomes 15 min");
            Assert.AreEqual(30 * Day, CompanionLoyaltyRules.WaitTimerMs(4, training, 8, false));
            Assert.IsNull(CompanionLoyaltyRules.WaitTimerMs(5, training, 8, false), $"{training}: index 5 has no timer");
        }
        Assert.IsNull(CompanionLoyaltyRules.WaitTimerMs(int.MaxValue, PersuasionTraining.Expert, 8, false), "no wrap past the table");
        Assert.IsNull(CompanionLoyaltyRules.WaitTimerMs(-1, PersuasionTraining.None, 8, false), "a negative index has none");
        Assert.AreEqual(0L, CompanionLoyaltyRules.WaitTimerMs(-1, PersuasionTraining.Apprentice, 8, false), "the step comes before the cut");
    }

    [TestMethod]
    public void No_wait_timer_under_an_extraordinary_leader_or_mind_control()
    {
        Assert.AreEqual(Day, CompanionLoyaltyRules.WaitTimerMs(3, PersuasionTraining.None, 19, false));
        Assert.IsNull(CompanionLoyaltyRules.WaitTimerMs(3, PersuasionTraining.None, 20, false));
        Assert.IsNull(CompanionLoyaltyRules.WaitTimerMs(3, PersuasionTraining.None, 8, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.WaitTimerMs(3, (PersuasionTraining)9, 8, false));
    }

    [TestMethod]
    public void The_timeout_reaction_change_is_twice_the_charisma_below_20()
    {
        Assert.AreEqual(-24, CompanionLoyaltyRules.ExpiryReactionDelta(8));
        Assert.AreEqual(-2, CompanionLoyaltyRules.ExpiryReactionDelta(19));
        Assert.AreEqual(0, CompanionLoyaltyRules.ExpiryReactionDelta(20));
        Assert.AreEqual(-38, CompanionLoyaltyRules.ExpiryReactionDelta(1));
        Assert.AreEqual(2L * (int.MinValue - 20L), CompanionLoyaltyRules.ExpiryReactionDelta(int.MinValue), "no overflow");
    }

    [TestMethod]
    public void Arming_adds_check_leader_and_the_bit_of_its_cause()
    {
        Assert.AreEqual(Reaction, CompanionLoyaltyRules.ArmReaction(CompanionLoyalty.None));
        Assert.AreEqual(Reaction | CompanionLoyalty.TestTooBad, CompanionLoyaltyRules.ArmReaction(Bad));
        Assert.AreEqual(Good, CompanionLoyaltyRules.ArmAlignment(CompanionLoyalty.None, 10, 11));
        Assert.AreEqual(Bad, CompanionLoyaltyRules.ArmAlignment(CompanionLoyalty.None, 10, 9));
        Assert.AreEqual(Good | CompanionLoyalty.TestTooBad, CompanionLoyaltyRules.ArmAlignment(Bad, 0, 5), "both bits stay until a test");
        Assert.AreEqual(Reaction, CompanionLoyaltyRules.ArmAlignment(Reaction, 7, 7), "no change arms nothing");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.ArmReaction((CompanionLoyalty)16));
    }

    [TestMethod]
    public void Check_leader_tests_the_armed_bits_at_their_boundaries()
    {
        // Reaction: at most field 5 + 20 = 0.
        Assert.AreEqual(LeaderWarning.LowReaction, CompanionLoyaltyRules.CheckLeader(Check(Reaction) with { Reaction = 0 }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Reaction) with { Reaction = 1 }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Good) with { Reaction = -100 }), "only an armed bit is tested");
        // Too good: a gap above field 6 − 100 = 200.
        Assert.AreEqual(LeaderWarning.TooGood, CompanionLoyaltyRules.CheckLeader(Check(Good) with { LeaderAlignment = 201 }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Good) with { LeaderAlignment = 200 }));
        // Too bad: a gap above field 7 − 100 = 100, the leader's alignment at or below the NPC's.
        Assert.AreEqual(LeaderWarning.TooBad, CompanionLoyaltyRules.CheckLeader(Check(Bad) with { LeaderAlignment = -101 }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Bad) with { LeaderAlignment = -100 }));
        // One branch by which alignment is higher: a too-bad bit is not read while the leader is above the NPC.
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Bad) with { LeaderAlignment = 500 }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(Check(Good) with { LeaderAlignment = -500 }));
        // The reaction test comes first.
        Assert.AreEqual(LeaderWarning.LowReaction, CompanionLoyaltyRules.CheckLeader(Check(Reaction | Good) with { Reaction = -5, LeaderAlignment = 900 }));
        Assert.AreEqual(LeaderWarning.TooGood, CompanionLoyaltyRules.CheckLeader(Check(Reaction | Good) with { Reaction = 5, LeaderAlignment = 900 }));
        // 32-bit arithmetic that wraps, as the executable.
        Assert.AreEqual(LeaderWarning.LowReaction, CompanionLoyaltyRules.CheckLeader(Check(Reaction) with { Thresholds = Row with { DislikeReaction = int.MaxValue }, Reaction = int.MinValue }));
    }

    [TestMethod]
    public void Check_leader_gives_no_warning_under_mind_control_persuasion_master_or_a_forced_follower()
    {
        var low = Check(CompanionLoyalty.All) with { Reaction = -100, LeaderAlignment = 900 };
        Assert.AreEqual(LeaderWarning.LowReaction, CompanionLoyaltyRules.CheckLeader(low));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(low with { MindControlledByLeader = true }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(low with { LeaderPersuasion = PersuasionTraining.Master }));
        Assert.AreEqual(LeaderWarning.LowReaction, CompanionLoyaltyRules.CheckLeader(low with { LeaderPersuasion = PersuasionTraining.Expert }));
        Assert.AreEqual(LeaderWarning.None, CompanionLoyaltyRules.CheckLeader(low with { ForcedFollower = true }));
    }

    [TestMethod]
    public void The_step_skips_while_an_action_goal_runs()
    {
        var step = CompanionLoyaltyRules.Step(true, FollowConsent.Dislike, true, Check(Reaction) with { Reaction = -50 });
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.GoalRunning, null, false, LeaderWarning.None, Reaction), step);
        Assert.IsNull(step.RefusalLines);
        Assert.IsNull(step.WarningLines);
    }

    [TestMethod]
    public void A_refused_check_leaves_with_its_refusal_lines_and_keeps_the_marks()
    {
        var step = CompanionLoyaltyRules.Step(false, FollowConsent.TooBad, true, Check(Reaction));
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Left, FollowConsent.TooBad, false, LeaderWarning.None, Reaction), step);
        Assert.AreEqual(new LoyaltyLines(1700, 1799), step.RefusalLines);
        (FollowConsent Reason, int First)[] lines =
        [
            (FollowConsent.TooGood, 1600), (FollowConsent.TooBad, 1700), (FollowConsent.Dislike, 1800), (FollowConsent.AlreadyInGroup, 2600),
            (FollowConsent.LimitReached, 2700), (FollowConsent.NotAllowed, 2800), (FollowConsent.LowLevel, 2900),
        ];
        foreach (var (reason, first) in lines) Assert.AreEqual(new LoyaltyLines(first, first + 99), CompanionLoyaltyRules.RefusalLines(reason));
        Assert.IsNull(CompanionLoyaltyRules.RefusalLines(FollowConsent.Accepted));
    }

    [TestMethod]
    public void A_refused_leave_goes_on_to_the_leader_check()
    {
        var step = CompanionLoyaltyRules.Step(false, FollowConsent.Dislike, false, Check(Reaction) with { Reaction = -30 });
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Dislike, true, LeaderWarning.LowReaction, CompanionLoyalty.None), step);
        Assert.AreEqual(new LoyaltyLines(5400, 5499), step.WarningLines);
        Assert.IsNull(step.RefusalLines, "it did not leave");
        var unarmed = CompanionLoyaltyRules.Step(false, FollowConsent.Dislike, false, Check(CompanionLoyalty.TestReaction) with { Reaction = -30 });
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Dislike, true, LeaderWarning.None, CompanionLoyalty.TestReaction), unarmed,
            "a test bit without check-leader runs nothing and stays");
    }

    [TestMethod]
    public void A_passed_check_runs_the_armed_leader_check_and_clears_every_mark()
    {
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Accepted, false, LeaderWarning.TooGood, CompanionLoyalty.None),
            CompanionLoyaltyRules.Step(false, FollowConsent.Accepted, true, Check(CompanionLoyalty.All) with { LeaderAlignment = 250 }));
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Accepted, false, LeaderWarning.None, CompanionLoyalty.None),
            CompanionLoyaltyRules.Step(false, FollowConsent.Accepted, true, Check(Bad)), "a test that finds nothing still clears");
        Assert.AreEqual(new LoyaltyStepResult(LoyaltyStepKind.Stayed, FollowConsent.Accepted, false, LeaderWarning.None, CompanionLoyalty.None),
            CompanionLoyaltyRules.Step(false, FollowConsent.Accepted, true, Check(CompanionLoyalty.None)));
        Assert.AreEqual(new LoyaltyLines(5200, 5299), CompanionLoyaltyRules.WarningLines(LeaderWarning.TooGood));
        Assert.AreEqual(new LoyaltyLines(5300, 5399), CompanionLoyaltyRules.WarningLines(LeaderWarning.TooBad));
        Assert.IsNull(CompanionLoyaltyRules.WarningLines(LeaderWarning.None));
    }

    [TestMethod]
    public void Undefined_inputs_throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.Step(false, (FollowConsent)99, true, Check(CompanionLoyalty.None)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.Step(false, FollowConsent.Accepted, true, Check((CompanionLoyalty)32)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.CheckLeader(Check(Reaction) with { LeaderPersuasion = (PersuasionTraining)7 }));
        Assert.ThrowsExactly<ArgumentNullException>(() => CompanionLoyaltyRules.CheckLeader(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompanionLoyaltyRules.RefusalLines((FollowConsent)99));
    }
}
