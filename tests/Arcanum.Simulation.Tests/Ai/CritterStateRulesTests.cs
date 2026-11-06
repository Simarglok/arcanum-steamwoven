using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-137 (MIG13 S6, GT28–GT34): which critter-state goal an NPC's life step starts.</summary>
[TestClass]
public sealed class CritterStateRulesTests
{
    private static readonly LifeFacts Facts = new() { HitPointPoints = 10, FatiguePoints = 10, Constitution = 14 };

    private static LifeOutcome Outcome(LifeEvents events, LifeState? after = null, DeathAnimation death = DeathAnimation.None, LifeState? before = null) =>
        new(before ?? LifeState.Unharmed, after ?? LifeState.Unharmed, events, 0, death);

    [TestMethod]
    public void ADeathStartsDyingWithTheKillsAnimationWhateverElseTheStepDid()
    {
        var start = CritterStateRules.Decide(Outcome(LifeEvents.Died | LifeEvents.KnockedDown | LifeEvents.Stunned, new(LifeLimits.DeathHitPointDamage, 0, 0, LifeConditions.None, 0), DeathAnimation.Leg),
            Facts, lying: false);
        Assert.AreEqual(CritterStateKind.Dying, start.Kind);
        Assert.AreEqual(GoalType.Dying, start.Frame!.Goal);
        Assert.AreEqual(19, start.Frame.Animation);
        Assert.AreSame(GoalStarters.Generic, start.Plan, "a start through 0x44D4E0 at the goal's own priority 5");
    }

    [TestMethod]
    public void ADeathOfACritterAlreadyLyingStillStartsDyingWhoseThirdNodeKeepsTheFall()
    {
        var start = CritterStateRules.Decide(Outcome(LifeEvents.Died, new(LifeLimits.DeathHitPointDamage, 99, 0, LifeConditions.None, 0), DeathAnimation.Plain), Facts, lying: true);
        Assert.AreEqual(CritterStateKind.Dying, start.Kind);
    }

    [TestMethod]
    public void AKnockDownResultWinsOverAStunAndAKnockdownOfALyingCritterIsRefused()
    {
        var down = CritterStateRules.Decide(Outcome(LifeEvents.KnockedDown | LifeEvents.Stunned), Facts, lying: false);
        Assert.AreEqual(CritterStateKind.KnockDown, down.Kind);
        Assert.AreSame(GoalStarters.KnockDown, down.Plan, "interrupts at 5, then at the goal's own 3");
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.KnockedDown), Facts, lying: true).Kind);
        Assert.AreEqual(CritterStateKind.KnockDown, CritterStateRules.Decide(Outcome(LifeEvents.LostConsciousness, new(0, 999, 0, LifeConditions.None, 0)), Facts, lying: false).Kind);
    }

    [TestMethod]
    public void AStunStartsTheStunWithTheCountdownFromConstitution()
    {
        var stun = CritterStateRules.Decide(Outcome(LifeEvents.Stunned, new(0, 0, 0, LifeConditions.Stunned, 0)), Facts, lying: false);
        Assert.AreEqual(CritterStateKind.Stun, stun.Kind);
        Assert.AreEqual(3, stun.Frame!.Count);
        Assert.AreSame(GoalStarters.Stun, stun.Plan, "interrupts at 4, then at the goal's own 1");
    }

    [TestMethod]
    public void ACritterThatComesRoundGetsUpButADeadOrStillUnconsciousOneDoesNot()
    {
        var up = CritterStateRules.Decide(Outcome(LifeEvents.RegainedConsciousness, new(0, 3, 0, LifeConditions.None, 0)), Facts, lying: true);
        Assert.AreEqual(CritterStateKind.GetUp, up.Kind);
        Assert.AreSame(GoalStarters.Generic, up.Plan);
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.RegainedConsciousness, new(0, 999, 0, LifeConditions.None, 0)), Facts, lying: true).Kind);
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.RegainedConsciousness, new(LifeLimits.DeathHitPointDamage, 0, 0, LifeConditions.None, 0)), Facts, lying: true).Kind);
    }

    [TestMethod]
    public void AnythingElseStartsNothing()
    {
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.None), Facts, lying: false).Kind);
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.StunEnded), Facts, lying: false).Kind);
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.AlreadyDead | LifeEvents.Died), Facts, lying: false).Kind);
        Assert.AreEqual(CritterStateKind.None, CritterStateRules.Decide(Outcome(LifeEvents.Scarred | LifeEvents.Blinded), Facts, lying: false).Kind);
    }
}
