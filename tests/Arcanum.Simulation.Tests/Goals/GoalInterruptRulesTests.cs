using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-131 (MIG13 S2): the interrupt rule 0x44E2C0 and its levels (ledger GS16..GS18) against the descriptors.</summary>
[TestClass]
public sealed class GoalInterruptRulesTests
{
    private static GoalDescriptor Of(GoalType type) => GoalDescriptors.Of(type)!;

    [TestMethod]
    public void EachLevelInterruptsTheSetOfPrioritiesOfGs18()
    {
        // Priority 1 fidget, 2 walk, 3 attack, 4 spell attempt, 5 dying (no flag).
        var byPriority = new[] { Of(GoalType.Fidget), Of(GoalType.MoveToTile), Of(GoalType.Attack), Of(GoalType.AttemptSpell), Of(GoalType.Dying) };
        Assert.IsTrue(byPriority.Select(d => d.Priority).SequenceEqual([1, 2, 3, 4, 5]));
        // Level 0 and 1 stop nothing; 2 stops 1-2; 3 and 4 stop 1-3; 5 stops 1-4; 6 stops everything.
        var expected = new Dictionary<int, bool[]>
        {
            [0] = [false, false, false, false, false],
            [1] = [false, false, false, false, false],
            [2] = [true, true, false, false, false],
            [3] = [true, true, true, false, false],
            [4] = [true, true, true, false, false],
            [5] = [true, true, true, true, false],
            [6] = [true, true, true, true, true],
        };
        foreach (var (level, interrupts) in expected)
            for (var i = 0; i < byPriority.Length; i++)
            {
                var verdict = GoalInterruptRules.Verdict(byPriority[i], level);
                Assert.AreEqual(interrupts[i] ? GoalInterruptVerdict.Interrupt : GoalInterruptVerdict.Refuse, verdict, $"level {level}, priority {byPriority[i].Priority}");
            }
    }

    [TestMethod]
    public void ANoInterruptGoalIsLeftRunningBelowLevelSixAndInterruptedAtSix()
    {
        foreach (var type in new[] { GoalType.AttemptOpenDoor, GoalType.EyeCandy, GoalType.Floating, GoalType.SpreadOut, GoalType.CastSpellFriendly, GoalType.AttemptSpellFriendly, GoalType.CastSpellWithAnimationSecondary, GoalType.AttemptCloseDoor })
        {
            Assert.IsTrue(Of(type).NoInterrupt);
            for (var level = 0; level < 6; level++) Assert.AreEqual(GoalInterruptVerdict.LeaveRunning, GoalInterruptRules.Verdict(Of(type), level), $"{type} at {level}");
            Assert.AreEqual(GoalInterruptVerdict.Interrupt, GoalInterruptRules.Verdict(Of(type), 6), $"{type} at 6");
        }
    }

    [TestMethod]
    public void APriorityFiveGoalRefusesEveryLevelBelowSix()
    {
        foreach (var type in new[] { GoalType.Dying, GoalType.DestroyObject, GoalType.Projectile, GoalType.Knockback })
        {
            Assert.AreEqual(5, Of(type).Priority);
            Assert.IsFalse(Of(type).NoInterrupt);
            for (var level = 0; level < 6; level++) Assert.AreEqual(GoalInterruptVerdict.Refuse, GoalInterruptRules.Verdict(Of(type), level), $"{type} at {level}");
            Assert.AreEqual(GoalInterruptVerdict.Interrupt, GoalInterruptRules.Verdict(Of(type), 6));
        }
    }

    [TestMethod]
    public void AKnockDownAndAStunAreStoppedByTheLevelsTheStartersUse()
    {
        // Knock-down (priority 3) stops a spell release (4) at its level 5; the stun (priority 1) is stopped by level 2 and more.
        Assert.AreEqual(GoalInterruptVerdict.Interrupt, GoalInterruptRules.Verdict(Of(GoalType.AttemptSpell), 5));
        Assert.AreEqual(GoalInterruptVerdict.Refuse, GoalInterruptRules.Verdict(Of(GoalType.AttemptSpell), 4));
        Assert.AreEqual(GoalInterruptVerdict.Refuse, GoalInterruptRules.Verdict(Of(GoalType.AnimateStunned), 1));
        Assert.AreEqual(GoalInterruptVerdict.Interrupt, GoalInterruptRules.Verdict(Of(GoalType.AnimateStunned), 2));
    }

    [TestMethod]
    public void ALevelOutsideZeroToSixIsRefusedAsAnArgument()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GoalInterruptRules.Verdict(Of(GoalType.Attack), -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GoalInterruptRules.Verdict(Of(GoalType.Attack), 7));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalInterruptRules.Verdict(null!, 3));
    }
}
