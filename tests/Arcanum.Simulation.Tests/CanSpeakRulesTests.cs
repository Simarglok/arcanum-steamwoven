using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

// ARC-DEV-304 (GT217): the head of the can-speak test 0x4AD800. Invented facts; the answers are the executable's order of tests.
[TestClass]
public sealed class CanSpeakRulesTests
{
    [TestMethod]
    public void A_living_conscious_npc_goes_on_to_the_tail()
    {
        Assert.IsNull(CanSpeakRules.Head(new CanSpeakFacts(IsNpc: true)));
    }

    [TestMethod]
    public void A_dead_npc_answers_one_unless_it_is_spoken_with_by_spell()
    {
        Assert.AreEqual(CanSpeakRules.DeadAnswer, CanSpeakRules.Head(new(Dead: true)));
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Head(new(Dead: true, SpokenWithDead: true)));
        Assert.IsFalse(CanSpeakRules.DeadMayTalk(false));
        Assert.IsTrue(CanSpeakRules.DeadMayTalk(true));
        // The flag on a living NPC changes nothing; and the flag skips the unconscious test below it.
        Assert.IsNull(CanSpeakRules.Head(new(SpokenWithDead: true)));
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Head(new(Dead: true, SpokenWithDead: true, Unconscious: true)));
        Assert.AreEqual(CanSpeakRules.Resting, CanSpeakRules.Head(new(Unconscious: true)));
    }

    [TestMethod]
    public void The_tests_before_the_dead_one_win_in_their_order()
    {
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Head(new(Destroyed: true, IsNpc: false)), "destroyed or off first, for any object");
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Head(new(IsNpc: false, Stoned: true)), "a non-NPC is free");
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Head(new(StunnedParalysedOrMute: true, Asleep: true)));
        Assert.AreEqual(CanSpeakRules.Resting, CanSpeakRules.Head(new(Asleep: true, Stoned: true)));
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Head(new(Spell58: true, Dead: true, SpokenWithDead: true)), "spell 58 refuses even a spoken-with corpse");
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Head(new(Stoned: true, Dead: true, SpokenWithDead: true)));
    }

    // ARC-AI-1677 (0x4AD800 read in the export): the tail with the fifth argument 0 and 1.
    [TestMethod]
    public void The_tail_with_flag_zero_asks_the_combat_focus_then_the_goal_interrupt_then_the_partner()
    {
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Tail(new(), skipsActivityTests: false));
        Assert.AreEqual(CanSpeakRules.Fighting, CanSpeakRules.Tail(new(InCombatFocusOrFlee: true, GoalRefusesInterrupt: true, OtherPartner: true), skipsActivityTests: false));
        Assert.AreEqual(CanSpeakRules.Busy, CanSpeakRules.Tail(new(GoalRefusesInterrupt: true, OtherPartner: true), skipsActivityTests: false));
        Assert.AreEqual(CanSpeakRules.OtherConversation, CanSpeakRules.Tail(new(OtherPartner: true), skipsActivityTests: false));
    }

    [TestMethod]
    public void The_tail_with_flag_one_asks_only_the_partner()
    {
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Tail(new(InCombatFocusOrFlee: true, GoalRefusesInterrupt: true), skipsActivityTests: true), "a fighting follower may still say its line");
        Assert.AreEqual(CanSpeakRules.OtherConversation, CanSpeakRules.Tail(new(InCombatFocusOrFlee: true, OtherPartner: true), skipsActivityTests: true));
    }
}
