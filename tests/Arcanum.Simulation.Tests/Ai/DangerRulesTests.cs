using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-WORLD-079 (AI slice I3): the danger state read (0x4AAB00), the setter's rule order (0x4AABE0) with the CL4 request
/// and the FO14 switch, and the enemy-list add and remove (0x4AF930, 0x4AF9D0).</summary>
[TestClass]
public sealed class DangerRulesTests
{
    private static readonly CombatAnomalyProfile Steamwoven = CombatAnomalyProfile.Steamwoven, Original = CombatAnomalyProfile.Original;
    private const DangerFlags Entered = DangerFlags.Fighting | DangerFlags.CheckWield | DangerFlags.CheckGrenade;

    private static DangerTransition Set(DangerFlags flags, DangerState state, DangerSetterFacts facts = default, CombatAnomalyProfile? profile = null) =>
        DangerRules.Set(flags, state, facts, profile ?? Steamwoven);

    [TestMethod]
    public void TheStateReadTakesFleeingThenSurrenderedThenFighting()
    {
        Assert.AreEqual(DangerState.None, DangerRules.Read(DangerFlags.None));
        Assert.AreEqual(DangerState.None, DangerRules.Read(DangerFlags.CheckWield | DangerFlags.NoAttack | DangerFlags.TargetLocked));
        Assert.AreEqual(DangerState.CombatFocus, DangerRules.Read(DangerFlags.Fighting));
        Assert.AreEqual(DangerState.Surrender, DangerRules.Read(DangerFlags.Surrendered | DangerFlags.Fighting));
        Assert.AreEqual(DangerState.Flee, DangerRules.Read(DangerFlags.Fleeing | DangerFlags.Surrendered | DangerFlags.Fighting));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DangerRules.Read((DangerFlags)(1 << 9)));
    }

    [TestMethod]
    public void AFightEntersCombatOnceAndWritesFocusAndThePcReactionOnEveryCall()
    {
        var first = Set(DangerFlags.None, DangerState.CombatFocus, new(TargetIsPc: true));
        Assert.AreEqual((DangerState.CombatFocus, DangerSetterRule.None, Entered), (first.Applied, first.Rule, first.Flags));
        Assert.AreEqual((DangerScriptPoint?)DangerScriptPoint.EnterCombat, first.Script);
        Assert.IsTrue(first.AlertSound && first.LowersReaction && first.WritesFocus);
        Assert.IsFalse(first.WritesFleeingFrom || first.LeavesCombatMode || first.FleeLine);

        // Already fighting: no hook or sound, but rules 8 and 9 again; the flags stay as they are.
        var again = Set(Entered | DangerFlags.BackingOff, DangerState.CombatFocus);
        Assert.AreEqual((Entered | DangerFlags.BackingOff, (DangerScriptPoint?)null, false), (again.Flags, again.Script, again.AlertSound));
        Assert.IsTrue(again.WritesFocus);
        Assert.IsFalse(again.LowersReaction, "the target is not a PC");
    }

    [TestMethod]
    public void AnInvulnerableNpcCalmsDownWhateverIsAsked()
    {
        foreach (var state in Enum.GetValues<DangerState>())
        {
            var step = Set(Entered | DangerFlags.Fleeing, state, new(Invulnerable: true, NoFlee: true, LeaderExtraordinaryCharisma: true));
            Assert.AreEqual((DangerState.None, DangerSetterRule.Invulnerable), (step.Applied, step.Rule), state.ToString());
            Assert.AreEqual(DangerFlags.CheckWield | DangerFlags.CheckGrenade | DangerFlags.DemaintainSpells, step.Flags);
            Assert.AreEqual((DangerScriptPoint?)DangerScriptPoint.ExitCombat, step.Script);
        }
    }

    [TestMethod]
    public void NoFleeTurnsAFlightIntoAFightButNotASurrender()
    {
        var flee = Set(DangerFlags.None, DangerState.Flee, new(NoFlee: true, LeaderExtraordinaryCharisma: true));
        Assert.AreEqual((DangerState.CombatFocus, DangerSetterRule.NoFlee), (flee.Applied, flee.Rule), "rule 2 skips rule 3");
        var surrender = Set(DangerFlags.None, DangerState.Surrender, new(NoFlee: true));
        Assert.AreEqual((DangerState.Surrender, DangerFlags.Surrendered), (surrender.Applied, surrender.Flags));
        var led = Set(DangerFlags.None, DangerState.Surrender, new(LeaderExtraordinaryCharisma: true));
        Assert.AreEqual((DangerState.CombatFocus, DangerSetterRule.LeaderCharisma), (led.Applied, led.Rule));
    }

    [TestMethod]
    public void ASwitchedOffOrStoppedFightBecomesNone()
    {
        var off = Set(DangerFlags.Fleeing, DangerState.CombatFocus, new(NpcFightingOff: true));
        Assert.AreEqual((DangerState.None, DangerSetterRule.FightingOff, DangerFlags.None), (off.Applied, off.Rule, off.Flags));
        var stopped = Set(DangerFlags.NoAttack, DangerState.Flee, new(NoFlee: true));
        Assert.AreEqual((DangerState.None, DangerSetterRule.NoAttack, DangerFlags.NoAttack), (stopped.Applied, stopped.Rule, stopped.Flags));
        Assert.IsFalse(stopped.WritesFocus || stopped.WritesFleeingFrom);
        Assert.IsNull(stopped.Script, "not fighting: no exit hook");
    }

    [TestMethod]
    public void AFlightLeavesTheFightWithoutDemaintainingAndFloatsItsLineOnce()
    {
        var flee = Set(Entered | DangerFlags.BackingOff, DangerState.Flee, new(FloatsLine: true));
        Assert.AreEqual(DangerFlags.Fleeing | DangerFlags.CheckWield | DangerFlags.CheckGrenade, flee.Flags);
        Assert.IsTrue(flee.WritesFleeingFrom && flee.FleeLine);
        Assert.IsFalse(flee.LeavesCombatMode || flee.WritesFocus);
        Assert.AreEqual((DangerScriptPoint?)DangerScriptPoint.ExitCombat, flee.Script);

        var surrender = Set(DangerFlags.Fleeing | DangerFlags.Fighting, DangerState.Surrender, new(FloatsLine: true));
        Assert.AreEqual(DangerFlags.Surrendered | DangerFlags.DemaintainSpells, surrender.Flags);
        Assert.IsTrue(surrender.LeavesCombatMode && surrender.WritesFleeingFrom);
        Assert.IsFalse(surrender.FleeLine, "already fleeing");
        Assert.IsFalse(Set(DangerFlags.None, DangerState.Flee).FleeLine, "no float callback or not active");
    }

    [TestMethod]
    public void ALockedNpcIgnoresAFightButLosesItsFlight()
    {
        var locked = DangerFlags.TargetLocked | DangerFlags.Fleeing;
        var step = Set(locked, DangerState.CombatFocus, new(TargetIsPc: true));
        Assert.AreEqual((DangerState.CombatFocus, DangerSetterRule.TargetLocked, DangerFlags.TargetLocked), (step.Applied, step.Rule, step.Flags));
        Assert.IsFalse(step.WritesFocus || step.LowersReaction || step.AlertSound);
        Assert.IsNull(step.Script);
        Assert.AreEqual(DangerState.None, DangerRules.Read(step.Flags), "locked but not fighting reads NONE");
    }

    [TestMethod]
    public void Fo14TheOriginalKeepsTheLockThroughAFlight()
    {
        var locked = Entered | DangerFlags.TargetLocked;
        Assert.AreEqual(DangerFlags.None, Set(locked, DangerState.Flee).Flags & DangerFlags.TargetLocked);
        Assert.AreEqual(DangerFlags.None, Set(locked, DangerState.None).Flags & DangerFlags.TargetLocked);
        var flight = Set(locked, DangerState.Flee, profile: Original);
        Assert.AreEqual(DangerFlags.TargetLocked, flight.Flags & DangerFlags.TargetLocked);
        var calm = Set(flight.Flags, DangerState.None, profile: Original);
        Assert.AreEqual(DangerState.None, DangerRules.Read(calm.Flags));
        var fight = Set(calm.Flags, DangerState.CombatFocus, profile: Original);
        Assert.AreEqual((DangerState.CombatFocus, DangerSetterRule.TargetLocked), (fight.Applied, fight.Rule),
            "the setter answers COMBAT_FOCUS without any write");
        Assert.AreEqual(DangerState.None, DangerRules.Read(fight.Flags));
    }

    [TestMethod]
    public void TheEnemyListRemoveFillsHolesWithTheLastEntry()
    {
        CollectionAssert.AreEqual(new[] { "c", "b" }, DangerRules.RemoveEnemy(["a", "b", "a", "c"], "a"));
        CollectionAssert.AreEqual(new[] { "d", "b", "c" }, DangerRules.RemoveEnemy(["a", "b", "c", "d"], "a"));
        CollectionAssert.AreEqual(new[] { "a" }, DangerRules.RemoveEnemy(["a"], "b"));
        CollectionAssert.AreEqual(System.Array.Empty<string>(), DangerRules.RemoveEnemy(["a", "a"], "a"));
        Assert.IsTrue(DangerRules.SkipsEnemy(["a"], "a", false));
        Assert.IsTrue(DangerRules.SkipsEnemy([], "a", true));
        Assert.IsFalse(DangerRules.SkipsEnemy(["b"], "a", false));
    }
}
