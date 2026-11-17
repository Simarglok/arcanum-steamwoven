using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-169 (MIG13 S11a, GS26 and GS27): the hero's object click as pure rules.</summary>
[TestClass]
public sealed class HeroClickRulesTests
{
    private static HeroClickFacts Facts(HeroClickTarget kind, bool combat = false, bool dead = false, bool alt = false, bool shift = false,
        bool pc = false, bool talksDead = false, bool attacksHero = false) =>
        new(true, false, false, false, combat, kind, dead, pc, talksDead, attacksHero, alt, shift);

    [TestMethod]
    public void AClickRefusedByTheHerosStateDoesNothing()
    {
        var live = Facts(HeroClickTarget.Critter);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(live with { HeroActive = false }).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(live with { ClickedSelf = true }).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(live with { HeroStoned = true }).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(live with { HeroStunned = true }).Goal);
    }

    [TestMethod]
    public void OutOfCombatModeALivingNpcIsTalkedToAndAnotherPlayerCharacterIsNot()
    {
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(Facts(HeroClickTarget.Critter)).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, pc: true)).Goal);
        // Alt and Shift change nothing for a talk.
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, alt: true, shift: true)).Goal);
    }

    [TestMethod]
    public void OutOfCombatModeTheOtherTargetsBuildTheirGoals()
    {
        Assert.AreEqual(HeroClickGoal.UseObject, HeroClickRules.Decide(Facts(HeroClickTarget.Portal)).Goal);
        Assert.AreEqual(HeroClickGoal.UseObject, HeroClickRules.Decide(Facts(HeroClickTarget.Scenery)).Goal);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Container)).Goal);
        Assert.AreEqual(HeroClickGoal.Pickup, HeroClickRules.Decide(Facts(HeroClickTarget.Item)).Goal);
        Assert.AreEqual(HeroClickGoal.Immediate, HeroClickRules.Decide(Facts(HeroClickTarget.Item, alt: true)).Goal);
        Assert.AreEqual(HeroClickGoal.WalkToTrap, HeroClickRules.Decide(Facts(HeroClickTarget.Trap)).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Wall)).Goal);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, dead: true)).Goal, "a corpse is looted");
        Assert.AreEqual(HeroClickGoal.Immediate, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, dead: true, alt: true)).Goal);
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, dead: true, talksDead: true)).Goal, "a corpse with spell flag 0x2000000 is talked to");
    }

    [TestMethod]
    public void InCombatModeALivingCritterIsAttackedWithShiftTheAttempt()
    {
        Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true)).Goal);
        Assert.AreEqual(HeroClickGoal.AttemptAttack, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, shift: true)).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, dead: true)).Goal);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, dead: true, alt: true)).Goal);
        Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(HeroClickTarget.Container, combat: true, alt: true)).Goal);
        Assert.AreEqual(HeroClickGoal.UseObject, HeroClickRules.Decide(Facts(HeroClickTarget.Scenery, combat: true)).Goal);
        Assert.AreEqual(HeroClickGoal.UseObject, HeroClickRules.Decide(Facts(HeroClickTarget.Wall, combat: true)).Goal, "a wall is used in combat mode (refused out of it)");
        Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(HeroClickTarget.Wall, combat: true, alt: true)).Goal);
        Assert.AreEqual(HeroClickGoal.Pickup, HeroClickRules.Decide(Facts(HeroClickTarget.Item, combat: true, alt: true)).Goal);
    }

    // ARC-COMBAT-1941 (VC62, 0x54ED30): the Shift test that picks 19 over 18 runs for the Alt attacks of a wall, portal, scenery and container as for a living critter; without Alt the click still uses the object.
    [TestMethod]
    public void InCombatModeShiftWithAltOnAWallPortalSceneryOrContainerIsTheAttemptAttack()
    {
        foreach (var kind in new[] { HeroClickTarget.Wall, HeroClickTarget.Portal, HeroClickTarget.Scenery, HeroClickTarget.Container })
        {
            Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(kind, combat: true, alt: true)).Goal, $"{kind}: Alt attacks");
            Assert.AreEqual(HeroClickGoal.AttemptAttack, HeroClickRules.Decide(Facts(kind, combat: true, alt: true, shift: true)).Goal, $"{kind}: Shift+Alt is the attempt");
            var use = kind == HeroClickTarget.Container ? HeroClickGoal.UseContainer : HeroClickGoal.UseObject;
            Assert.AreEqual(use, HeroClickRules.Decide(Facts(kind, combat: true, shift: true)).Goal, $"{kind}: Shift alone still uses it");
        }
    }

    [TestMethod]
    public void ATargetThatAttacksTheHeroPutsItIntoCombatModeAndIsAttacked()
    {
        var decision = HeroClickRules.Decide(Facts(HeroClickTarget.Critter, attacksHero: true));
        Assert.AreEqual(HeroClickGoal.Attack, decision.Goal);
        Assert.IsTrue(decision.EntersCombatMode);
        Assert.IsFalse(HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, attacksHero: true)).EntersCombatMode, "already in combat mode");
        Assert.IsFalse(HeroClickRules.Decide(Facts(HeroClickTarget.Critter)).EntersCombatMode);
    }

    // ARC-COMBAT-1944 (0x54ED30): the combat mode is entered before the type switch, only by a living critter and only when the hero can act.
    [TestMethod]
    public void OnlyALivingAttackerEntersCombatModeAndAHeroThatCannotActDoesNot()
    {
        Assert.IsFalse(HeroClickRules.Decide(Facts(HeroClickTarget.Critter, dead: true, attacksHero: true)).EntersCombatMode, "a corpse");
        Assert.IsFalse(HeroClickRules.Decide(Facts(HeroClickTarget.Scenery, attacksHero: true)).EntersCombatMode, "no critter");
        Assert.IsFalse(HeroClickRules.Decide(new HeroClickFacts(false, false, false, false, false, HeroClickTarget.Critter, TargetAttacksHero: true)).EntersCombatMode, "the hero cannot act");
        Assert.IsFalse(HeroClickRules.Decide(new HeroClickFacts(true, false, true, false, false, HeroClickTarget.Critter, TargetAttacksHero: true)).EntersCombatMode, "the hero is stoned");
        // A player character that attacks the hero's side enters combat mode too and is attacked (the combat table has no player-character refusal).
        var pc = HeroClickRules.Decide(Facts(HeroClickTarget.Critter, pc: true, attacksHero: true));
        Assert.IsTrue(pc.EntersCombatMode);
        Assert.AreEqual(HeroClickGoal.Attack, pc.Goal);
    }

    [TestMethod]
    public void TheStartBranchFollowsGs27()
    {
        // Auto attack on: an equal goal queued is ignored; otherwise interrupt at level 3 and start.
        Assert.AreEqual(HeroClickStart.Ignored, HeroClickRules.StartOf(true, false, true, true, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.StartOf(true, false, true, false, false));
        // Off: no main run starts at once; otherwise the hero's runs are interrupted at level 3 (a repeated order restarts).
        Assert.AreEqual(HeroClickStart.Start, HeroClickRules.StartOf(false, false, false, false, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.StartOf(false, false, true, true, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.StartOf(false, false, true, false, false));
        // In a round: only while the hero idles.
        Assert.AreEqual(HeroClickStart.Start, HeroClickRules.StartOf(false, true, false, false, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.StartOf(false, true, true, false, true));
        Assert.AreEqual(HeroClickStart.Ignored, HeroClickRules.StartOf(false, true, true, false, false));
    }
}
