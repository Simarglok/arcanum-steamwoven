namespace Arcanum.Simulation.Goals;

/// <summary>What the hero's click landed on, in the classes the action decision 0x54ED30 tells apart (its type tables 0x54FC84 / 0x54FCA0 in combat mode and 0x54FCB4 / 0x54FCD0 out of it).</summary>
public enum HeroClickTarget
{
    /// <summary>A wall: refused out of combat mode, used in it (the combat table, 0x54FCA0).</summary>
    Wall,
    /// <summary>A door or window.</summary>
    Portal,
    /// <summary>Scenery.</summary>
    Scenery,
    /// <summary>A container.</summary>
    Container,
    /// <summary>An item on the ground.</summary>
    Item,
    /// <summary>A player character or an NPC (living or dead, <see cref="HeroClickFacts.TargetDead"/>).</summary>
    Critter,
    /// <summary>A trap: the hero walks to its tile.</summary>
    Trap,
    /// <summary>Anything else: refused.</summary>
    Other,
}

/// <summary>The goal an object click builds, by its goal type (GS26); the hero's click builds the goal itself and calls no starter.</summary>
public enum HeroClickGoal
{
    /// <summary>The click does nothing.</summary>
    None,
    /// <summary>Goal 18 <c>ag_attack</c>.</summary>
    Attack,
    /// <summary>Goal 19 <c>ag_attempt_attack</c> (Shift).</summary>
    AttemptAttack,
    /// <summary>Goal 21 <c>ag_talk</c>.</summary>
    Talk,
    /// <summary>Goal 38 <c>ag_use_object</c>.</summary>
    UseObject,
    /// <summary>Goal 81 <c>ag_use_container</c>.</summary>
    UseContainer,
    /// <summary>Goal 15 <c>ag_pickup_item</c>.</summary>
    Pickup,
    /// <summary>Goal 3 to the trap's tile.</summary>
    WalkToTrap,
    /// <summary>The Alt action 0x4445A0 (ARC-INPUT-1946): no goal; a loose item or a body within one tile of the hero is moved onto the hero's tile, farther away nothing moves.</summary>
    Immediate,
}

/// <summary>What the click decision reads (all facts of the hero and of the target at the moment of the press).</summary>
/// <param name="HeroActive">0x45D790: the hero can act.</param>
/// <param name="ClickedSelf">The target is the hero.</param>
/// <param name="HeroStoned">The hero carries spell flag 0x10000 (stoned).</param>
/// <param name="HeroStunned">The hero carries critter flags 0x60 (stunned or paralysed).</param>
/// <param name="CombatMode">The hero is in combat mode.</param>
/// <param name="Kind">The target's class.</param>
/// <param name="TargetDead">A critter target is dead (0x45D8D0).</param>
/// <param name="TargetIsPlayerCharacter">A critter target is another player character (a click on it does nothing out of combat mode).</param>
/// <param name="TargetTalksDead">A dead critter target carries spell flag 0x2000000: it is talked to instead of looted.</param>
/// <param name="TargetAttacksHero">The target's bottom goal is an attack (18 or 19) aimed at the hero or a follower (0x44E940).</param>
/// <param name="Alt">Alt is held.</param>
/// <param name="Shift">Shift is held.</param>
public readonly record struct HeroClickFacts(
    bool HeroActive, bool ClickedSelf, bool HeroStoned, bool HeroStunned, bool CombatMode, HeroClickTarget Kind,
    bool TargetDead = false, bool TargetIsPlayerCharacter = false, bool TargetTalksDead = false, bool TargetAttacksHero = false, bool Alt = false, bool Shift = false);

/// <summary>The click decision: the goal, and whether the hero enters combat mode first (a target that attacks the hero out of combat mode).</summary>
public readonly record struct HeroClickDecision(HeroClickGoal Goal, bool EntersCombatMode = false);

/// <summary>How the click's goal starts (GS27).</summary>
public enum HeroClickStart
{
    /// <summary>Nothing starts (an equal goal is queued with the auto attack option on, or a round whose hero is not idle).</summary>
    Ignored,
    /// <summary>The hero has no main run: the goal starts at once.</summary>
    Start,
    /// <summary>The hero's runs are interrupted at level 3, then the goal starts (a different order, or a repeated one: it restarts).</summary>
    InterruptAndStart,
    /// <summary>ARC-COMBAT-1949 (VC62): a repeated attack on the same target while the hero's main run goes on is put at the bottom of that run's stack (0x44DBE0 -> 0x44D9B0); nothing else changes.</summary>
    QueueBeneath,
    /// <summary>ARC-COMBAT-1949 (VC62): nothing new is added (a round whose main run is not the fidget, a main run whose goal index is 4 or more), so the click's run flags go to the hero's existing main run (label 0x54FB19).</summary>
    FlagsOnly,
}

/// <summary>
/// ARC-DEV-169 (MIG13 S11a, docs/architecture/goal-table.md, GS26 and GS27): the hero's object click as pure rules. <see cref="Decide"/> is the action decision 0x54ED30 (what goal the click builds); <see cref="StartOf"/> is its
/// start branch. The refusals come first and are silent (no message, no sound, S11 read GT115). The hero's
/// items armed on the cursor (the throw, goal 37) and the Alt drag are not decided here.
/// </summary>
public static class HeroClickRules
{
    /// <summary>The action decision of the hero's click on an object.</summary>
    [OriginalFunction(0x54ED30, OriginalFunctionScope.Partial)]
    public static HeroClickDecision Decide(HeroClickFacts facts)
    {
        // The refusals, in the order of 0x54ED30: the hero cannot act, clicks itself, is stoned, is stunned or paralysed.
        if (!facts.HeroActive || facts.ClickedSelf || facts.HeroStoned || facts.HeroStunned) return new(HeroClickGoal.None);
        // Out of combat mode, a target that attacks the hero puts the hero into combat mode first, and the click resolves through the combat table.
        var enters = !facts.CombatMode && facts.Kind == HeroClickTarget.Critter && !facts.TargetDead && facts.TargetAttacksHero;
        var combat = facts.CombatMode || enters;
        var goal = combat ? InCombat(facts) : OutOfCombat(facts);
        // ARC-COMBAT-1944: the hero enters combat mode before the type switch, even when the combat table then builds nothing (a refused or silent decision).
        return new(goal, enters);
    }

    // The combat-mode table (0x54FCA0): wall, portal, scenery and container use unless Alt attacks; items are picked up; a living critter is attacked (Shift: the attempt); a dead one is looted with Alt only.
    private static HeroClickGoal InCombat(HeroClickFacts f) => f.Kind switch
    {
        HeroClickTarget.Wall or HeroClickTarget.Portal or HeroClickTarget.Scenery => f.Alt ? AttackOf(f) : HeroClickGoal.UseObject,
        HeroClickTarget.Container => f.Alt ? AttackOf(f) : HeroClickGoal.UseContainer,
        HeroClickTarget.Item => HeroClickGoal.Pickup,
        HeroClickTarget.Critter when !f.TargetDead => AttackOf(f),
        HeroClickTarget.Critter => f.Alt ? HeroClickGoal.UseContainer : HeroClickGoal.None,
        HeroClickTarget.Trap => HeroClickGoal.WalkToTrap,
        _ => HeroClickGoal.None,
    };

    // ARC-COMBAT-1941 (VC62): the Shift test that picks goal 19 over 18 runs for every attack the table builds, the Alt attacks of a wall, portal, scenery and container as for a living critter.
    private static HeroClickGoal AttackOf(HeroClickFacts f) => f.Shift ? HeroClickGoal.AttemptAttack : HeroClickGoal.Attack;

    // The table out of combat mode (0x54FCD0): portal and scenery use, a container is opened, an item is picked up (Alt: the immediate action), a living NPC is talked to (another player character: nothing),
    // a dead critter is looted (Alt: the immediate action) unless it carries spell flag 0x2000000, which is talked to.
    private static HeroClickGoal OutOfCombat(HeroClickFacts f) => f.Kind switch
    {
        HeroClickTarget.Portal or HeroClickTarget.Scenery => HeroClickGoal.UseObject,
        HeroClickTarget.Container => HeroClickGoal.UseContainer,
        HeroClickTarget.Item => f.Alt ? HeroClickGoal.Immediate : HeroClickGoal.Pickup,
        HeroClickTarget.Critter when f.TargetDead && !f.TargetTalksDead => f.Alt ? HeroClickGoal.Immediate : HeroClickGoal.UseContainer,
        HeroClickTarget.Critter => f.TargetIsPlayerCharacter ? HeroClickGoal.None : HeroClickGoal.Talk,
        HeroClickTarget.Trap => HeroClickGoal.WalkToTrap,
        _ => HeroClickGoal.None,
    };

    /// <summary>
    /// The start branch of the click (GS27) for a goal the decision built. <paramref name="autoAttack"/>: the "auto attack" option (outside a round only): an equal goal already queued is ignored, otherwise the hero is interrupted at
    /// level 3 and the goal starts. With it off: a hero with no main run starts at once; outside a round a different order or a repeated one interrupts at level 3 and starts (a repeated order restarts it); in a round the click is
    /// taken only while the hero's run is its fidget (<paramref name="heroIdle"/>). The attack's insertion at the bottom of a running run belongs to the hero's combat actions (S12).
    /// </summary>
    public static HeroClickStart StartOf(bool autoAttack, bool inRound, bool hasMainRun, bool equalGoalQueued, bool heroIdle)
    {
        if (inRound) return !hasMainRun ? HeroClickStart.Start : heroIdle ? HeroClickStart.InterruptAndStart : HeroClickStart.Ignored;
        if (autoAttack) return equalGoalQueued ? HeroClickStart.Ignored : HeroClickStart.InterruptAndStart;
        return hasMainRun ? HeroClickStart.InterruptAndStart : HeroClickStart.Start;
    }

    /// <summary>The depth from which a repeated attack is no longer queued: the main run's current-goal index, which counts from 0, must be under 4 (signed compare, 0x54F9AC).</summary>
    public const int QueueDepthLimit = 4;

    /// <summary>
    /// ARC-COMBAT-1949 (VC62, GS27): the start branch of the click for a goal the decision built, as the exe reads it (0x54ED30 tail). <paramref name="autoAttack"/> is the "auto attack" option (0x4B8230; a round never reads it, so the
    /// caller may pass it as it is). <paramref name="mainDepth"/> is the main run's current-goal index (its goal count minus one), <paramref name="mainTopIdle"/> that its top goal is 1 or 2 (0x4373F0), <paramref name="fidgetMain"/> that
    /// the hero's first run is its main run with the fidget (2) as bottom goal (0x44E830), <paramref name="buildsAttack"/> that the goal is 18 or 19.
    /// <list type="bullet">
    /// <item>In a round the option and the equal test are skipped: no main run starts; a fidget main run whose goal index is under 4 is interrupted (top idle) or gets the goal beneath; everything else adds nothing (<see cref="HeroClickStart.FlagsOnly"/>).</item>
    /// <item>With the option on: an equal goal (0x44E6F0: the bottom goal of any run, the goal's alias counts, same objects) is ignored with no flags, otherwise the hero is interrupted and the goal starts.</item>
    /// <item>With it off: no main run starts at once; an equal attack while the main run is under four goals is queued beneath it (or, with its top idle, interrupts and starts); at four goals nothing is added; any other click interrupts and starts.</item>
    /// </list>
    /// </summary>
    public static HeroClickStart ClickStartOf(bool autoAttack, bool inRound, bool hasMainRun, bool equalGoalQueued, bool buildsAttack, int mainDepth, bool mainTopIdle, bool fidgetMain)
    {
        if (inRound)
        {
            if (!hasMainRun) return HeroClickStart.Start;
            if (!fidgetMain || mainDepth >= QueueDepthLimit) return HeroClickStart.FlagsOnly;
            return mainTopIdle ? HeroClickStart.InterruptAndStart : HeroClickStart.QueueBeneath;
        }
        if (autoAttack) return equalGoalQueued ? HeroClickStart.Ignored : HeroClickStart.InterruptAndStart;
        if (!hasMainRun) return HeroClickStart.Start;
        if (!equalGoalQueued || !buildsAttack) return HeroClickStart.InterruptAndStart;
        if (mainDepth >= QueueDepthLimit) return HeroClickStart.FlagsOnly;
        return mainTopIdle ? HeroClickStart.InterruptAndStart : HeroClickStart.QueueBeneath;
    }
}
