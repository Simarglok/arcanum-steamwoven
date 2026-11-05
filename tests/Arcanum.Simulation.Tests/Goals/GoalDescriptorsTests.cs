using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-131 (MIG13 S2): the 87 goal types and the descriptor table, against the rows GS8 and GS9 of docs/architecture/original-goals.md (read again from the executable's file image in the S2 slice).</summary>
[TestClass]
public sealed class GoalDescriptorsTests
{
    private static int[] Where(Func<GoalDescriptor, bool> predicate) => [.. GoalDescriptors.All.Where(predicate).Select(d => (int)d.Type)];

    [TestMethod]
    public void TheEightySevenGoalTypesHaveTheOriginalsNumbersAndNames()
    {
        var values = Enum.GetValues<GoalType>().Select(v => (int)v).Order().ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(0, GoalTypes.Count).ToArray(), values, "numbers 0..86, each once");
        var names = Enum.GetValues<GoalType>().Select(GoalTypes.OriginalName).ToArray();
        Assert.AreEqual(87, names.Distinct().Count(), "every executable name is different");
        Assert.IsTrue(names.All(n => n.StartsWith("ag_", StringComparison.Ordinal)));
        Assert.AreEqual("ag_animate", GoalTypes.OriginalName(GoalType.Animate));
        Assert.AreEqual("ag_move_near_obj", GoalTypes.OriginalName(GoalType.MoveNear));
        Assert.AreEqual("ag_unlock_door", GoalTypes.OriginalName((GoalType)13));
        Assert.AreEqual("ag_pick_weapon", GoalTypes.OriginalName((GoalType)22));
        Assert.AreEqual("ag_pend_closing_door", GoalTypes.OriginalName((GoalType)72));
        Assert.AreEqual("ag_throw_spell_w_cast_anim_2ndary", GoalTypes.OriginalName((GoalType)84));
        Assert.AreEqual("ag_attempt_use_pickpocket_skill_on", GoalTypes.OriginalName((GoalType)86));
        Assert.IsFalse(GoalTypes.IsDefined(-1));
        Assert.IsFalse(GoalTypes.IsDefined(87));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GoalTypes.OriginalName((GoalType)87));
    }

    [TestMethod]
    public void TheExecutorInterpretsTwentySixGoalsEachWithADescriptor()
    {
        // ARC-DEV-134 (S4): the executor's former AiGoalType is gone; its 26 goals are GoalType values with the original's numbers.
        var interpreted = AiExecutorRules.InterpretedGoals.Select(g => (int)g).Order().ToArray();
        CollectionAssert.AreEqual(new[] { 4, 5, 6, 8, 15, 16, 18, 19, 24, 25, 26, 27, 33, 34, 37, 39, 52, 56, 61, 67, 69, 79, 80, 82, 83, 86 }, interpreted);
        foreach (var goal in AiExecutorRules.InterpretedGoals) Assert.IsTrue(GoalDescriptors.Has(goal), $"goal {(int)goal} has a descriptor");
    }

    [TestMethod]
    public void EightyFiveGoalsHaveADescriptorAndGoalsThirteenAndTwentyTwoHaveNone()
    {
        Assert.AreEqual(GoalDescriptors.DescribedCount, GoalDescriptors.All.Length);
        Assert.AreEqual(85, GoalDescriptors.All.Length);
        CollectionAssert.AreEqual(new[] { 13, 22 }, Enum.GetValues<GoalType>().Where(t => !GoalDescriptors.Has(t)).Select(t => (int)t).ToArray());
        Assert.IsNull(GoalDescriptors.Of(GoalType.UnlockDoor));
        Assert.IsNull(GoalDescriptors.Of(GoalType.PickWeapon));
        Assert.IsNull(GoalDescriptors.Of((GoalType)87));
        Assert.IsTrue(GoalDescriptors.All.All(d => d.Priority is >= 1 and <= 5));
        Assert.IsTrue(GoalDescriptors.All.All(d => GoalDescriptors.Of(d.Type) == d));
    }

    [TestMethod]
    public void PrioritiesAreTheTableOfGs9()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 57, 77 }, Where(d => d.Priority == 1));
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8, 24, 52, 53, 55, 56, 61, 68, 69, 79, 80 }, Where(d => d.Priority == 2));
        CollectionAssert.AreEqual(new[] { 27, 54, 74, 83 }, Where(d => d.Priority == 4));
        CollectionAssert.AreEqual(new[] { 31, 32, 36, 43, 44, 45, 46, 47, 48, 58, 59, 75, 76, 84 }, Where(d => d.Priority == 5));
        Assert.AreEqual(47, Where(d => d.Priority == 3).Length, "every other type with a descriptor is priority 3");
        foreach (var number in new[] { 0, 9, 18, 19, 21, 25, 26, 28, 29, 30, 37, 38, 62, 63, 81 }) Assert.AreEqual(3, GoalDescriptors.Of((GoalType)number)!.Priority, $"goal {number}");
    }

    [TestMethod]
    public void TheNoInterruptNotCountedAndCarriedFlagsAreTheTableOfGs9()
    {
        CollectionAssert.AreEqual(new[] { 12, 44, 45, 46, 47, 48, 50, 58, 59, 69, 73, 74, 75, 76, 84 }, Where(d => d.NoInterrupt));
        CollectionAssert.AreEqual(new[] { 1, 44, 75, 76, 77 }, Where(d => d.NotCounted));
        CollectionAssert.AreEqual(new[] { 0, 1, 6, 11, 12, 29, 30, 31, 44, 45, 46, 47, 48, 49, 51, 57, 58, 59, 62, 70, 71, 72, 75, 76, 77, 84 }, Where(d => d.Carried));
        Assert.AreEqual(15, Where(d => d.NoInterrupt).Length);
        Assert.AreEqual(26, Where(d => d.Carried).Length);
    }

    [TestMethod]
    public void TheAliasListsAreTheTableOfGs9InTheExecutablesOrder()
    {
        var expected = new Dictionary<int, int[]>
        {
            [3] = [7, 4], [4] = [7, 3, 55], [7] = [4, 3, 55], [55] = [7, 4], [68] = [7, 4, 3],
            [18] = [19], [19] = [18, 20], [20] = [19],
            [24] = [56, 8], [69] = [56, 8],
            [26] = [27], [73] = [27], [82] = [27],
            [27] = [26], [74] = [26], [83] = [26],
            [65] = [7], [66] = [7],
        };
        foreach (var descriptor in GoalDescriptors.All)
        {
            var aliases = descriptor.Aliases.Select(a => (int)a).ToArray();
            CollectionAssert.AreEqual(expected.TryGetValue((int)descriptor.Type, out var list) ? list : [], aliases, $"aliases of goal {(int)descriptor.Type}");
        }
    }

    [TestMethod]
    public void TheAliasQueryReadsTheQueriedGoalsListSoItIsNotSymmetric()
    {
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.MoveToTile, GoalType.MoveToTile));
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.MoveToTile, GoalType.MoveNearTile), "3 lists 7");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.MoveNearTile, GoalType.MoveToTile), "7 lists 3");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.Wander, GoalType.MoveNearTile), "65 lists 7");
        Assert.IsFalse(GoalDescriptors.IsSameActivity(GoalType.MoveNearTile, GoalType.Wander), "7 does not list 65");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.AttemptAttack, GoalType.Kill));
        Assert.IsFalse(GoalDescriptors.IsSameActivity(GoalType.Attack, GoalType.Kill), "18 lists only 19");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.Follow, GoalType.MoveNear));
        Assert.IsFalse(GoalDescriptors.IsSameActivity(GoalType.RunNear, GoalType.Follow), "56 has no aliases");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.CastSpellFriendly, GoalType.AttemptSpell));
        Assert.IsFalse(GoalDescriptors.IsSameActivity(GoalType.CastSpell, GoalType.CastSpellWithAnimation), "26 lists 27 only");
        Assert.IsTrue(GoalDescriptors.IsSameActivity(GoalType.UnlockDoor, GoalType.UnlockDoor), "a goal with no descriptor matches itself");
        Assert.IsFalse(GoalDescriptors.IsSameActivity(GoalType.UnlockDoor, GoalType.Animate));
    }

    [TestMethod]
    public void ASignificantGoalHasPriorityTwoOrMoreAndNoNoInterruptFlag()
    {
        foreach (var d in GoalDescriptors.All) Assert.AreEqual(d.Priority >= 2 && !d.NoInterrupt, GoalDescriptors.IsSignificant(d), $"goal {(int)d.Type}");
        Assert.IsTrue(GoalDescriptors.IsSignificant(GoalDescriptors.Of(GoalType.Dying)!), "dying is priority 5 without the flag");
        Assert.IsFalse(GoalDescriptors.IsSignificant(GoalDescriptors.Of(GoalType.EyeCandy)!), "the overlays are not significant");
        Assert.IsFalse(GoalDescriptors.IsSignificant(GoalDescriptors.Of(GoalType.Fidget)!));
        Assert.ThrowsExactly<ArgumentNullException>(() => GoalDescriptors.IsSignificant(null!));
    }
}
