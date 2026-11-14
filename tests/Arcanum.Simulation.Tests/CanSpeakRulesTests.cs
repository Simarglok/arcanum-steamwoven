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

    // ARC-UI-2030: the whole test as the dialog start asks it (flag 0): the head, then the combat focus or flight, then the level-2 interrupt, then the partner.
    [TestMethod]
    public void The_talk_start_test_answers_the_head_first_and_never_interrupts_for_it()
    {
        var interrupts = 0;
        bool Interrupt() { interrupts++; return false; }
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Test(new(StunnedParalysedOrMute: true), false, () => true, Interrupt, false));
        Assert.AreEqual(CanSpeakRules.Resting, CanSpeakRules.Test(new(Unconscious: true), false, () => false, Interrupt, true));
        Assert.AreEqual(CanSpeakRules.DeadAnswer, CanSpeakRules.Test(new(Dead: true), false, () => false, Interrupt, false));
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Test(new(Dead: true, SpokenWithDead: true), false, () => true, Interrupt, true), "a spoken-with corpse is free at once, no tail");
        Assert.AreEqual(0, interrupts);
    }

    [TestMethod]
    public void The_talk_start_test_refuses_a_fighting_npc_without_interrupting_it()
    {
        var interrupts = 0;
        Assert.AreEqual(CanSpeakRules.Fighting, CanSpeakRules.Test(new(IsNpc: true), false, () => true, () => { interrupts++; return true; }, true));
        Assert.AreEqual(0, interrupts, "the interrupt is made only when the focus test passed");
    }

    [TestMethod]
    public void The_talk_start_test_interrupts_once_and_refuses_with_three_when_a_run_refuses()
    {
        var interrupts = 0;
        Assert.AreEqual(CanSpeakRules.Busy, CanSpeakRules.Test(new(IsNpc: true), false, () => false, () => { interrupts++; return true; }, true));
        Assert.AreEqual(1, interrupts);
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Test(new(IsNpc: true), false, () => false, () => { interrupts++; return false; }, false));
        Assert.AreEqual(2, interrupts, "an accepted talk has interrupted the NPC too");
        Assert.AreEqual(CanSpeakRules.OtherConversation, CanSpeakRules.Test(new(IsNpc: true), false, () => false, () => false, true));
    }

    [TestMethod]
    public void The_follower_line_test_skips_the_activity_tests()
    {
        Assert.AreEqual(CanSpeakRules.Free, CanSpeakRules.Test(new(IsNpc: true), true, () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), false));
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Test(new(Spell58: true), true, () => false, () => false, false));
    }

    [TestMethod]
    public void The_talk_start_test_refuses_a_destroyed_or_muted_npc_and_a_follower_line_still_asks_the_partner()
    {
        Func<bool> never = () => throw new InvalidOperationException("the tail must not be asked");
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Test(new(IsNpc: true, Destroyed: true), false, never, never, false));
        Assert.AreEqual(CanSpeakRules.Unable, CanSpeakRules.Test(new(IsNpc: true, Spell58: true), false, never, never, false));
        Assert.AreEqual(CanSpeakRules.OtherConversation, CanSpeakRules.Test(new(IsNpc: true), true, never, never, true));
    }
}
