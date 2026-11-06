using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-137 (MIG13 S6, goal-table.md §3.5, GT28–GT34): an NPC's life step starts its critter-state goal on the session's table: dying for a death, the knock-down for a
/// knock-down result or lost consciousness (refused for a critter already lying), the stun for a stun result (the stunned bit dropped again when its start fails), the get-up for a
/// critter that comes round; a stunned NPC whose turn a round skips loses 3 of its countdown. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class CritterStateGoalTests
{
    private const string Wolf = "oid:wolf";

    // Constitution 8: the stun's count is (20 − 8) / 2 = 6.
    private static readonly LifeFacts Facts = new();

    private static SessionLifeTarget Npc(string entity = Wolf) => SessionLifeTarget.Critter(entity, Facts, LifeState.Unharmed);

    private static GameSessionOwner Session() => Create();

    private static void Hit(GameSessionOwner owner, LifeDamage damage, string entity = Wolf) =>
        Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.ApplyDamage(owner.Version, Npc(entity), damage).Disposition);

    private static GoalRun[] Runs(GameSessionOwner owner, string entity = Wolf) => owner.QueryGoals().RunsOf(entity).ToArray();

    private static GoalType[] Goals(GameSessionOwner owner, string entity = Wolf) => Runs(owner, entity).Select(r => r.Bottom.Goal).ToArray();

    [TestMethod]
    public void AKillStartsDyingWithTheAnimationOfTheDeathAndItsFirstStep()
    {
        using var owner = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc()).Disposition);
        var run = Runs(owner).Single();
        Assert.AreEqual(GoalType.Dying, run.Bottom.Goal);
        Assert.AreEqual(DyingAnimationRules.FallDown, run.Bottom.Animation, "a kill takes the fall");
        Assert.IsNotNull(owner.QueryGoalStep(run.Handle), "the first step is queued");
    }

    [TestMethod]
    public void ADeathByDamageStartsDyingAndEndsTheRunsBeneathItsPriority()
    {
        using var owner = Session();
        var walk = owner.StartGoal(new(Wolf, GoalStarters.Walk, new GoalFrame(GoalType.MoveToTile)), new Ports());
        Assert.IsTrue(walk.Started);
        Hit(owner, new LifeDamage { HitPoints = 500 });
        CollectionAssert.AreEqual(new[] { GoalType.Dying }, Goals(owner), "the walk (priority 2) was interrupted at 5");
        Assert.IsTrue(owner.DrainGoalEnds().Any(), "its end is told to the host, which runs the end nodes");
    }

    [TestMethod]
    public void AKnockdownResultStartsTheKnockDownAndAnUnconsciousCritterIsNotKnockedDownAgain()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Knockdown });
        CollectionAssert.AreEqual(new[] { GoalType.KnockDown }, Goals(owner));
        // A second knockdown while the first run plays is refused (the art shows the fall).
        var first = Runs(owner).Single().Handle;
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Knockdown, HitPoints = 1 });
        Assert.AreEqual(first, Runs(owner).Single().Handle);
    }

    [TestMethod]
    public void LosingConsciousnessStartsTheKnockDownOnceAndTheKnockoutOfAnUnconsciousCritterStartsNothing()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Fatigue = 500 });
        CollectionAssert.AreEqual(new[] { GoalType.KnockDown }, Goals(owner));
        var run = Runs(owner).Single();
        owner.KillGoal(run.Handle);
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout, HitPoints = 1 });
        Assert.AreEqual(0, Runs(owner).Length, "the critter already lies (unconscious): the starter refuses");
    }

    [TestMethod]
    public void AStunResultStartsTheStunWithItsCountdownAndLeavesTheBitToTheGoal()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Stun });
        var run = Runs(owner).Single();
        Assert.AreEqual(GoalType.AnimateStunned, run.Bottom.Goal);
        Assert.AreEqual(6, run.Bottom.Count);
        Assert.IsTrue(owner.QueryLifeRecord(Wolf)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned), "the bit stays until the goal ends");
    }

    [TestMethod]
    public void AStunWhoseStartIsRefusedByAPriorityFiveRunDropsTheStunnedBitAgain()
    {
        using var owner = Session();
        // A knockback (priority 5) is current: the stun's level-4 interrupt is refused (GT36).
        var knockback = owner.StartGoal(new(Wolf, GoalStarters.Generic, new GoalFrame(GoalType.Knockback)), new Ports());
        Assert.IsTrue(knockback.Started);
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Stun });
        CollectionAssert.AreEqual(new[] { GoalType.Knockback }, Goals(owner));
        Assert.IsFalse(owner.QueryLifeRecord(Wolf)!.Value.State.Conditions.HasFlag(LifeConditions.Stunned), "the damage path drops the bit when the start fails");
    }

    [TestMethod]
    public void AKnockDownEndsARunningStunAndTellsTheHostToRunItsEndNode()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Stun });
        Assert.IsTrue(owner.DrainGoalEnds().IsEmpty);
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Knockdown, HitPoints = 1 });
        CollectionAssert.AreEqual(new[] { GoalType.KnockDown }, Goals(owner));
        var ends = owner.DrainGoalEnds();
        Assert.AreEqual(GoalType.AnimateStunned, ends.Single().Run.Bottom.Goal);
    }

    [TestMethod]
    public void ACritterThatComesRoundStartsTheGetUp()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Fatigue = 500 });
        owner.KillGoal(Runs(owner).Single().Handle);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Heal(owner.Version, Npc(), new LifeHeal { Fatigue = 1000 }).Disposition);
        CollectionAssert.AreEqual(new[] { GoalType.GetUp }, Goals(owner));
    }

    [TestMethod]
    public void ARoundThatSkipsAStunnedNpcTakesThreeOffItsCountdownAndEndsTheRunAtZero()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Stun });
        owner.CountStunnedTurn(Wolf);
        Assert.AreEqual(3, Runs(owner).Single().Bottom.Count);
        owner.CountStunnedTurn(Wolf);
        Assert.AreEqual(0, Runs(owner).Length, "at 0 the run was interrupted at level 6");
        Assert.AreEqual(GoalType.AnimateStunned, owner.DrainGoalEnds().Single().Run.Bottom.Goal);
        // A critter whose first run is not the stun is left alone.
        owner.CountStunnedTurn(Wolf);
        Assert.AreEqual(0, Runs(owner).Length);
    }

    [TestMethod]
    public void AStunAndALostConsciousnessInOneHitStartTheKnockDownAndLeaveNoStunnedBit()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Effects = CriticalHitEffects.Stun, Fatigue = 500 });
        var state = owner.QueryLifeRecord(Wolf)!.Value.State;
        CollectionAssert.AreEqual(new[] { GoalType.KnockDown }, Goals(owner));
        Assert.IsFalse(state.Conditions.HasFlag(LifeConditions.Stunned), "the knock-down won; the damage path drops the bit of a stun that did not start");
    }

    [TestMethod]
    public void ALifeStepInsideTheSpellSystemStartsItsGoalWhenTheSystemEnds()
    {
        using var owner = Session();
        var busy = typeof(GameSessionOwner).GetField("_spellRunsBusy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        busy.SetValue(owner, true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc()).Disposition);
        Assert.AreEqual(0, Runs(owner).Length, "the table is held by the spell-run rules");
        busy.SetValue(owner, false);
        owner.DrainCritterStarts();
        CollectionAssert.AreEqual(new[] { GoalType.Dying }, Goals(owner), "the start the damage asked for ran at the system's end");
        owner.DrainCritterStarts();
        Assert.AreEqual(1, Runs(owner).Length, "and only once");
    }

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    [TestMethod]
    public void ADeathInsideTheNpcsOwnStepMarksItsRunDoneAndTheSessionClaimsItsEnd()
    {
        using var owner = Session();
        var walk = owner.StartGoal(new(Wolf, GoalStarters.Walk, new GoalFrame(GoalType.MoveToTile)), new Ports());
        Assert.IsTrue(walk.Started);
        var handle = walk.Handle!.Value;
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, 500).Disposition);
        owner.ProcessEvents(new NoWorld());
        Assert.IsNotNull(owner.BeginGoalStep(handle), "the walk's step is open");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc()).Disposition);
        Assert.IsTrue(owner.DrainGoalEnds().IsEmpty, "the stepped run is only marked done: its close reports the end");
        var closed = owner.EndGoalStep(handle, SessionGoalStepAnswer.After(100, null));
        Assert.AreEqual(SessionGoalStepOutcome.Ended, closed.Outcome);
        Assert.AreEqual(GoalType.MoveToTile, owner.DrainGoalEnds().Single().Run.Bottom.Goal, "the host is told to run the end nodes once, as an end of the session's own making");
        CollectionAssert.AreEqual(new[] { GoalType.Dying }, Goals(owner));
    }

    [TestMethod]
    public void ADeathOfAnUnconsciousCritterCarriesTheLyingFlagIntoTheDyingGoal()
    {
        using var owner = Session();
        Hit(owner, new LifeDamage { Fatigue = 500 });
        owner.KillGoal(Runs(owner).Single().Handle);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Npc()).Disposition);
        Assert.AreEqual(1, Runs(owner).Single().Bottom.Count, "it lay when killed: the dying goal keeps the fall even if the host lost its art");
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
