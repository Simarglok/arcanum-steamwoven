using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-169 (MIG13 S11a, GS26 and GS27): the hero's object click as pure rules.</summary>
[TestClass]
public sealed class HeroClickRulesTests
{
    private static HeroClickFacts Facts(HeroClickTarget kind, bool combat = false, bool dead = false, bool alt = false, bool shift = false,
        bool pc = false, bool talksDead = false, bool attacksHero = false, bool air = false, bool poly = false) =>
        new(true, false, false, false, combat, kind, dead, pc, talksDead, attacksHero, alt, shift, air, poly);

    /// <summary>ARC-COMBAT-1943 (0x54ED30): Body of Air builds no attack goal and asks for line 25; it is read in combat mode only, where the tables build attacks.</summary>
    [TestMethod]
    public void BodyOfAirRefusesEveryAttackGoalWithTheMessage()
    {
        foreach (var kind in new[] { HeroClickTarget.Critter })
        {
            var plain = HeroClickRules.Decide(Facts(kind, combat: true, air: true));
            Assert.AreEqual(HeroClickGoal.None, plain.Goal); Assert.IsTrue(plain.BodyOfAirMessage);
            var attempt = HeroClickRules.Decide(Facts(kind, combat: true, shift: true, air: true));
            Assert.AreEqual(HeroClickGoal.None, attempt.Goal); Assert.IsTrue(attempt.BodyOfAirMessage, "Shift only changes the goal number");
        }
        foreach (var kind in new[] { HeroClickTarget.Wall, HeroClickTarget.Portal, HeroClickTarget.Scenery, HeroClickTarget.Container })
        {
            var alt = HeroClickRules.Decide(Facts(kind, combat: true, alt: true, air: true));
            Assert.AreEqual(HeroClickGoal.None, alt.Goal, $"{kind}: the Alt attack"); Assert.IsTrue(alt.BodyOfAirMessage, $"{kind}: line 25");
        }
        // The use clicks, the item pick up and the loot are no attacks: they go on as without the flag.
        var use = HeroClickRules.Decide(Facts(HeroClickTarget.Portal, combat: true, air: true));
        Assert.AreEqual(HeroClickGoal.UseObject, use.Goal); Assert.IsFalse(use.BodyOfAirMessage);
        Assert.AreEqual(HeroClickGoal.Pickup, HeroClickRules.Decide(Facts(HeroClickTarget.Item, combat: true, air: true)).Goal);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, dead: true, alt: true, air: true)).Goal);
        // Out of combat mode no table case builds an attack: the talk and the rest are unchanged, with no message.
        var talk = HeroClickRules.Decide(Facts(HeroClickTarget.Critter, air: true));
        Assert.AreEqual(HeroClickGoal.Talk, talk.Goal); Assert.IsFalse(talk.BodyOfAirMessage);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Container, air: true)).Goal);
    }

    /// <summary>The entering of combat mode runs before the type switch and so before the Body of Air test.</summary>
    [TestMethod]
    public void BodyOfAirStillEntersCombatModeForAnAttacker()
    {
        var decision = HeroClickRules.Decide(Facts(HeroClickTarget.Critter, attacksHero: true, air: true));
        Assert.IsTrue(decision.EntersCombatMode); Assert.AreEqual(HeroClickGoal.None, decision.Goal); Assert.IsTrue(decision.BodyOfAirMessage);
    }

    /// <summary>ARC-COMBAT-1943: polymorph refuses silently out of combat mode a portal, a container, an item and a dead body's loot (before the Alt action), not scenery, a talk or a trap.</summary>
    [TestMethod]
    public void PolymorphRefusesTheOutOfCombatTargetsItListsOnly()
    {
        foreach (var (kind, dead) in new[] { (HeroClickTarget.Portal, false), (HeroClickTarget.Container, false), (HeroClickTarget.Item, false), (HeroClickTarget.Critter, true) })
        {
            Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(kind, dead: dead, poly: true)).Goal, $"{kind} dead={dead}");
            Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(kind, dead: dead, alt: true, poly: true)).Goal, $"{kind} dead={dead} Alt: before the immediate action");
            Assert.IsFalse(HeroClickRules.Decide(Facts(kind, dead: dead, poly: true)).BodyOfAirMessage);
        }
        Assert.AreEqual(HeroClickGoal.UseObject, HeroClickRules.Decide(Facts(HeroClickTarget.Scenery, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.Talk, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, dead: true, talksDead: true, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.WalkToTrap, HeroClickRules.Decide(Facts(HeroClickTarget.Trap, poly: true)).Goal);
    }

    /// <summary>ARC-COMBAT-1943: in combat mode polymorph refuses a container (Alt too), an item, and the use of a wall, portal and scenery; the Alt attacks, a living target and a dead body's loot go on.</summary>
    [TestMethod]
    public void PolymorphRefusesTheCombatModeTargetsItListsOnly()
    {
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Container, combat: true, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Container, combat: true, alt: true, poly: true)).Goal, "refused before the Alt attack");
        Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(HeroClickTarget.Item, combat: true, poly: true)).Goal);
        foreach (var kind in new[] { HeroClickTarget.Wall, HeroClickTarget.Portal, HeroClickTarget.Scenery })
        {
            Assert.AreEqual(HeroClickGoal.None, HeroClickRules.Decide(Facts(kind, combat: true, poly: true)).Goal, $"{kind} use");
            Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(kind, combat: true, alt: true, poly: true)).Goal, $"{kind} Alt attack");
        }
        Assert.AreEqual(HeroClickGoal.Attack, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.UseContainer, HeroClickRules.Decide(Facts(HeroClickTarget.Critter, combat: true, dead: true, alt: true, poly: true)).Goal);
        Assert.AreEqual(HeroClickGoal.WalkToTrap, HeroClickRules.Decide(Facts(HeroClickTarget.Trap, combat: true, poly: true)).Goal);
    }

    /// <summary>Polymorph plus Body of Air: a container with Alt is refused silently first, so the message does not show.</summary>
    [TestMethod]
    public void PolymorphRefusesAContainerBeforeBodyOfAirReadsTheAttack()
    {
        var decision = HeroClickRules.Decide(Facts(HeroClickTarget.Container, combat: true, alt: true, air: true, poly: true));
        Assert.AreEqual(HeroClickGoal.None, decision.Goal); Assert.IsFalse(decision.BodyOfAirMessage);
    }

    [TestMethod]
    public void PolymorphRefusesTheCursorItemThrow()
    {
        Assert.IsTrue(HeroClickRules.ThrowRefused(true));
        Assert.IsFalse(HeroClickRules.ThrowRefused(false));
    }

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

    // ARC-COMBAT-1949 (VC62): the click's start branch with the main run's depth and top goal, as the exe reads it.
    [TestMethod]
    public void TheClickStartBranchWithTheOptionOnIgnoresAnEqualGoalAndOtherwiseInterrupts()
    {
        Assert.AreEqual(HeroClickStart.Ignored, HeroClickRules.ClickStartOf(true, false, true, true, false, 1, false, false), "an equal talk, use, loot or pickup click");
        Assert.AreEqual(HeroClickStart.Ignored, HeroClickRules.ClickStartOf(true, false, true, true, true, 1, false, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(true, false, true, false, true, 1, false, false));
        // The option on never queues beneath, whatever the depth.
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(true, false, true, false, true, 0, false, false));
    }

    [TestMethod]
    public void TheClickStartBranchWithTheOptionOffQueuesARepeatedAttackBeneathTheMainRun()
    {
        Assert.AreEqual(HeroClickStart.Start, HeroClickRules.ClickStartOf(false, false, false, false, true, 0, false, false));
        Assert.AreEqual(HeroClickStart.QueueBeneath, HeroClickRules.ClickStartOf(false, false, true, true, true, 1, false, false));
        Assert.AreEqual(HeroClickStart.QueueBeneath, HeroClickRules.ClickStartOf(false, false, true, true, true, 3, false, false), "the index must be under 4");
        Assert.AreEqual(HeroClickStart.FlagsOnly, HeroClickRules.ClickStartOf(false, false, true, true, true, 4, false, false), "a deep stack adds nothing");
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(false, false, true, true, true, 1, true, false), "an idle main run is interrupted");
        // Only an attack is queued; an equal talk restarts (a repeated order), and so does a different goal.
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(false, false, true, true, false, 1, false, false));
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(false, false, true, false, true, 1, false, false));
    }

    [TestMethod]
    public void TheClickStartBranchInARoundSkipsTheOptionAndTheEqualTest()
    {
        Assert.AreEqual(HeroClickStart.Start, HeroClickRules.ClickStartOf(true, true, false, true, true, 0, false, false));
        Assert.AreEqual(HeroClickStart.FlagsOnly, HeroClickRules.ClickStartOf(true, true, true, true, true, 1, false, false), "a main run that is not the fidget");
        Assert.AreEqual(HeroClickStart.FlagsOnly, HeroClickRules.ClickStartOf(false, true, true, false, false, 4, true, true), "a fidget at four goals");
        Assert.AreEqual(HeroClickStart.InterruptAndStart, HeroClickRules.ClickStartOf(false, true, true, false, false, 0, true, true));
        Assert.AreEqual(HeroClickStart.QueueBeneath, HeroClickRules.ClickStartOf(false, true, true, false, false, 1, false, true));
    }
}
