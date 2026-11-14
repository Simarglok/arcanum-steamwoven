using Arcanum.Simulation.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

/// <summary>ARC-AI-2084 (ledger GW1-GW8): the guard witness rule's map-free parts (the object's key, the NPC filter, the effects by mode and sleep).</summary>
[TestClass]
public sealed class GuardWitnessRulesTests
{
    [TestMethod]
    public void A_portal_or_container_keys_on_its_notify_npc_and_no_notify_npc_means_no_witness()
    {
        Assert.AreEqual(6402, GuardWitnessRules.Key(1, 6402));
        Assert.AreEqual(77, GuardWitnessRules.Key(2, 77));
        Assert.IsNull(GuardWitnessRules.Key(1, 0), "notify NPC 0: not even a guard reacts");
        Assert.IsNull(GuardWitnessRules.Key(2, 0));
    }

    [TestMethod]
    public void Scenery_keys_on_minus_one_so_only_guards_match_and_other_types_return()
    {
        Assert.AreEqual(-1, GuardWitnessRules.Key(3, 0));
        Assert.AreEqual(-1, GuardWitnessRules.Key(3, 5), "scenery has no notify NPC to read");
        foreach (var type in new[] { 0, 4, 5, 15, 16, 17 }) Assert.IsNull(GuardWitnessRules.Key(type, 6402), $"type {type} returns at once");
    }

    [TestMethod]
    public void A_witness_has_the_notify_npc_name_or_is_a_guard_and_is_alive_and_free()
    {
        Assert.IsTrue(GuardWitnessRules.Qualifies(6402, 6402, guard: false, dead: false, mindControlled: false), "the name");
        Assert.IsTrue(GuardWitnessRules.Qualifies(6402, 1, guard: true, dead: false, mindControlled: false), "the guard class");
        Assert.IsFalse(GuardWitnessRules.Qualifies(6402, 1, guard: false, dead: false, mindControlled: false));
        Assert.IsFalse(GuardWitnessRules.Qualifies(6402, 6402, guard: true, dead: true, mindControlled: false), "the dead");
        Assert.IsFalse(GuardWitnessRules.Qualifies(6402, 6402, guard: true, dead: false, mindControlled: true), "the mind-controlled");
        Assert.IsFalse(GuardWitnessRules.Qualifies(-1, 5, guard: false, dead: false, mindControlled: false), "scenery: a name never matches -1 in the data");
    }

    [TestMethod]
    public void A_done_crime_or_a_sleeping_witness_gets_the_attack_event_only()
    {
        var expected = new GuardWitnessPlan(AttackEvent: true, ReactionDelta: 0, ChecksReaction: false, Shouts: false);
        Assert.AreEqual(expected, GuardWitnessRules.Plan(GuardWitnessMode.Done, sleeping: false));
        Assert.AreEqual(expected, GuardWitnessRules.Plan(GuardWitnessMode.Done, sleeping: true));
        Assert.AreEqual(expected, GuardWitnessRules.Plan(GuardWitnessMode.Attempt, sleeping: true));
    }

    [TestMethod]
    public void An_awake_witness_of_an_attempt_lowers_its_reaction_tests_it_and_shouts()
    {
        Assert.AreEqual(new GuardWitnessPlan(AttackEvent: false, ReactionDelta: -20, ChecksReaction: true, Shouts: true), GuardWitnessRules.Plan(GuardWitnessMode.Attempt, sleeping: false));
        Assert.AreEqual(4000, GuardWitnessRules.ShoutLines.First);
        Assert.AreEqual(4099, GuardWitnessRules.ShoutLines.Last);
    }
}
