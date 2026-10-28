using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-023: the combat and life wrappers around the item trigger executor (ledger IC1–IC6): the damage step's calls in order
// and each wrapper's walk over the parent's worn items. Facts and amounts are invented.
[TestClass]
public sealed class ItemSpellTriggerEventsTests
{
    private sealed class Rolls(int value) : ICombatRollSource
    {
        public int Roll(CombatRollPurpose purpose, int min, int max) => Math.Clamp(value, min, max);
    }

    private static readonly LifeFacts Facts = new() { HitPointPoints = 30, FatiguePoints = 30 };

    private static (LifeDamage Damage, LifeOutcome Outcome) Step(LifeDamage damage, LifeState? state = null, LifeFacts? facts = null) =>
        (damage, LifeRules.ApplyDamage(state ?? LifeState.Unharmed, facts ?? Facts, damage, new Rolls(15)));

    private static IReadOnlyList<ItemTriggerStep> Steps(LifeDamage damage, LifeState? state = null, LifeFacts? facts = null, bool attacker = true,
        bool killer = true, bool melee = true)
    {
        var (d, outcome) = Step(damage, state, facts);
        return DamageStep(facts ?? Facts, d, outcome, attacker, killer, melee);
    }

    private static ItemTriggerStep S(ItemTriggerEvent e, ItemTriggerParty parent, ItemTriggerParty other, bool melee = false) => new(e, parent, other, melee);

    private const ItemTriggerParty Attacker = ItemTriggerParty.Attacker, Target = ItemTriggerParty.Target, None = ItemTriggerParty.None,
        Killer = ItemTriggerParty.Killer;

    [TestMethod]
    public void A_plain_hit_fires_the_target_s_parent_hit_then_the_attacker_s_damages_opponent()
    {
        CollectionAssert.AreEqual(new[]
        {
            S(ItemTriggerEvent.ParentHit, Target, Attacker, melee: true), S(ItemTriggerEvent.ParentDamagesOpponent, Attacker, Target),
        }, Steps(new LifeDamage { HitPoints = 2 }).ToArray());
        Assert.AreEqual(0, Steps(new LifeDamage { HitPoints = 2 }, attacker: false, killer: false).Count, "no attacker: no parent hit, no damages");
    }

    [TestMethod]
    public void The_step_fires_nothing_for_an_invulnerable_or_already_dead_critter_but_does_for_a_stoned_one()
    {
        Assert.AreEqual(0, Steps(new LifeDamage { HitPoints = 2 }, facts: Facts with { Traits = LifeTraits.Invulnerable }).Count);
        var dead = new LifeState(LifeLimits.DeathHitPointDamage, 0, 0, LifeConditions.None, 0);
        Assert.AreEqual(0, Steps(new LifeDamage { HitPoints = 2 }, dead).Count);
        Assert.AreEqual(2, Steps(new LifeDamage { HitPoints = 2 }, facts: Facts with { Traits = LifeTraits.Stoned }).Count, "stoned takes 0 and fires");
    }

    [TestMethod]
    public void A_stun_fires_before_its_start_and_not_on_a_stunned_critter_or_with_a_knockdown()
    {
        var stun = new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.Stun };
        Assert.AreEqual(S(ItemTriggerEvent.ParentStunned, Target, Attacker), Steps(stun)[0]);
        // An unconscious critter cannot start the stun (0x45D790), but the wrapper ran before the start.
        var max = LifeRules.MaxFatigue(Facts);
        Assert.AreEqual(S(ItemTriggerEvent.ParentStunned, Target, Attacker), Steps(stun, new LifeState(0, max, 0, LifeConditions.None, 0))[0]);
        Assert.AreEqual(ItemTriggerEvent.ParentHit, Steps(stun, new LifeState(0, 0, 0, LifeConditions.Stunned, 0))[0].Event, "already stunned");
        Assert.IsFalse(Steps(stun with { Effects = CriticalHitEffects.Stun | CriticalHitEffects.Knockdown }).Any(s => s.Event == ItemTriggerEvent.ParentStunned));
        Assert.AreEqual(S(ItemTriggerEvent.ParentStunned, Target, None), Steps(stun, attacker: false)[0], "a null attacker is the other object");
    }

    [TestMethod]
    public void A_knockout_fires_both_wrappers_with_the_other_party_and_the_fatigue_setter_s_going_unconscious_without_one()
    {
        CollectionAssert.AreEqual(new[]
        {
            S(ItemTriggerEvent.ParentGoingUnconscious, Target, Attacker), S(ItemTriggerEvent.TargetGoingUnconscious, Attacker, Target),
            S(ItemTriggerEvent.ParentGoingUnconscious, Target, None),
            S(ItemTriggerEvent.ParentHit, Target, Attacker, melee: true), S(ItemTriggerEvent.ParentDamagesOpponent, Attacker, Target),
        }, Steps(new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.Knockout }).ToArray());
        // A knockdown alone fires the two wrappers too; a conscious critter stays conscious.
        CollectionAssert.AreEqual(new[] { ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerEvent.TargetGoingUnconscious, ItemTriggerEvent.ParentHit,
            ItemTriggerEvent.ParentDamagesOpponent }, Steps(new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.Knockdown }).Select(s => s.Event).ToArray());
        // Without an attacker only the target's own walk runs.
        CollectionAssert.AreEqual(new[] { S(ItemTriggerEvent.ParentGoingUnconscious, Target, None), S(ItemTriggerEvent.ParentGoingUnconscious, Target, None) },
            Steps(new LifeDamage { Effects = CriticalHitEffects.Knockout }, attacker: false, killer: false).ToArray());
    }

    [TestMethod]
    public void Fatigue_to_zero_fires_going_unconscious_also_when_the_hit_kills_and_never_for_the_undead()
    {
        var max = LifeRules.MaxFatigue(Facts);
        var tired = new LifeDamage { HitPoints = 1, Fatigue = max };
        Assert.AreEqual(S(ItemTriggerEvent.ParentGoingUnconscious, Target, None), Steps(tired)[0]);
        var killing = Steps(tired with { HitPoints = 500 });
        CollectionAssert.AreEqual(new[] { ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerEvent.ParentDying, ItemTriggerEvent.ParentHit,
            ItemTriggerEvent.ParentDamagesOpponent }, killing.Select(s => s.Event).ToArray(), "the setter reads the fatigue only (0x45D800)");
        Assert.AreEqual(ItemTriggerEvent.ParentHit, Steps(tired, facts: Facts with { Traits = LifeTraits.Undead })[0].Event);
        Assert.AreEqual(ItemTriggerEvent.ParentHit, Steps(tired, new LifeState(0, max, 0, LifeConditions.None, 0))[0].Event, "already unconscious");
    }

    [TestMethod]
    public void A_death_fires_dying_with_the_killer_only_when_there_is_one()
    {
        Assert.AreEqual(S(ItemTriggerEvent.ParentDying, Target, Killer), Steps(new LifeDamage { HitPoints = 500 })[0]);
        Assert.IsFalse(Steps(new LifeDamage { HitPoints = 500 }, killer: false).Any(s => s.Event == ItemTriggerEvent.ParentDying));
    }

    [TestMethod]
    public void The_melee_bit_needs_an_attack_entry_and_no_weapon_or_a_reach_of_one()
    {
        Assert.IsTrue(MeleeHit(attackEntry: true, weaponRange: null));
        Assert.IsTrue(MeleeHit(attackEntry: true, weaponRange: 1));
        Assert.IsTrue(MeleeHit(attackEntry: true, weaponRange: 0));
        Assert.IsFalse(MeleeHit(attackEntry: true, weaponRange: 2));
        Assert.IsFalse(MeleeHit(attackEntry: false, weaponRange: null), "a spell's damage step has no attack entry flag");
        Assert.AreEqual(ParentHit | WeaponMelee, Mask(ItemTriggerEvent.ParentHit, melee: true));
        Assert.AreEqual(ParentHit, Mask(ItemTriggerEvent.ParentHit));
    }

    [TestMethod]
    public void A_walk_calls_each_worn_item_and_damages_opponent_calls_three_masks_for_the_weapon()
    {
        CollectionAssert.AreEqual(new[] { new ItemTriggerCall(0, ParentStunned), new ItemTriggerCall(1, ParentStunned) },
            WornCalls(ItemTriggerEvent.ParentStunned, 2).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            new ItemTriggerCall(0, ParentDamagesOpponent), new ItemTriggerCall(0, TargetHit),
            new ItemTriggerCall(1, ParentDamagesOpponent), new ItemTriggerCall(1, TargetHit), new ItemTriggerCall(1, ParentDamagesOpponentWithItem),
        }, WornCalls(ItemTriggerEvent.ParentDamagesOpponent, 2, weapon: 1).ToArray());
        Assert.AreEqual(4, WornCalls(ItemTriggerEvent.ParentDamagesOpponent, 2, weapon: null).Count, "a thrown or no weapon is not worn");
        CollectionAssert.AreEqual(new[] { ParentGoingUnconscious, ParentDying, ParentAttacksOpponent, TargetGoingUnconscious },
            new[] { ItemTriggerEvent.ParentGoingUnconscious, ItemTriggerEvent.ParentDying, ItemTriggerEvent.ParentAttacksOpponent,
                ItemTriggerEvent.TargetGoingUnconscious }.Select(e => Mask(e)).ToArray());
        Assert.AreEqual(0, WornCalls(ItemTriggerEvent.ParentHit, 0).Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WornCalls(ItemTriggerEvent.ParentHit, WornSlots + 1));
    }

    // ARC-MAGIC-024: the life steps outside a damage step (IC4), the dying animation's end (IC5) and a spell's damage step (DH1, MG46).

    [TestMethod]
    public void A_life_step_that_empties_the_fatigue_fires_going_unconscious_with_no_other_object_once()
    {
        var max = LifeRules.MaxFatigue(Facts);
        var spent = LifeRules.ChargeFatigue(LifeState.Unharmed, Facts, max);
        CollectionAssert.AreEqual(new[] { S(ItemTriggerEvent.ParentGoingUnconscious, Target, None) }, LifeStep(Facts, spent).ToArray());
        Assert.AreEqual(0, LifeStep(Facts, LifeRules.ChargeFatigue(LifeState.Unharmed, Facts, max - 1)).Count, "still conscious");
        Assert.AreEqual(0, LifeStep(Facts, LifeRules.ChargeFatigue(spent.After, Facts, 3)).Count, "already unconscious");
        Assert.AreEqual(0, LifeStep(Facts with { Traits = LifeTraits.Undead }, LifeRules.ChargeFatigue(LifeState.Unharmed, Facts with { Traits = LifeTraits.Undead }, max)).Count,
            "an undead critter is never unconscious");
        // The setter reads the fatigue only: a kill that also empties it fires it too; a kill alone does not.
        var both = LifeRules.ApplyDamage(LifeState.Unharmed, Facts, new LifeDamage { HitPoints = 10_000, Fatigue = 10_000 }, new Rolls(15));
        Assert.IsTrue(both.Events.HasFlag(LifeEvents.Died));
        Assert.AreEqual(1, LifeStep(Facts, both).Count);
        Assert.AreEqual(0, LifeStep(Facts, LifeRules.Kill(LifeState.Unharmed, Facts)).Count);
    }

    [TestMethod]
    public void The_dying_animation_s_end_fires_dying_with_no_other_object_for_a_dead_critter_only()
    {
        var dead = LifeRules.Kill(LifeState.Unharmed, Facts).After;
        CollectionAssert.AreEqual(new[] { S(ItemTriggerEvent.ParentDying, Target, None) }, DyingEnd(Facts, dead).ToArray());
        Assert.AreEqual(0, DyingEnd(Facts, LifeState.Unharmed).Count);
    }

    [TestMethod]
    public void A_spell_s_damage_step_has_no_melee_bit_and_an_item_run_s_hooks_follow_the_setting()
    {
        var (damage, outcome) = Step(new LifeDamage { HitPoints = 2 });
        var hooks = new[] { S(ItemTriggerEvent.ParentHit, Target, Attacker), S(ItemTriggerEvent.ParentDamagesOpponent, Attacker, Target) };
        CollectionAssert.AreEqual(hooks, SpellDamageStep(Facts, damage, outcome, true, true, false, MagicAnomalyProfile.Steamwoven).ToArray());
        CollectionAssert.AreEqual(hooks, SpellDamageStep(Facts, damage, outcome, true, true, true, MagicAnomalyProfile.Original).ToArray(), "MG46 kept");
        Assert.AreEqual(0, SpellDamageStep(Facts, damage, outcome, true, true, true, MagicAnomalyProfile.Steamwoven).Count, "MG46 fixed");
        // The fix leaves the other calls: a kill still fires dying with the run's damage source.
        var (kill, killed) = Step(new LifeDamage { HitPoints = 10_000 });
        CollectionAssert.AreEqual(new[] { S(ItemTriggerEvent.ParentDying, Target, Killer) },
            SpellDamageStep(Facts, kill, killed, true, true, true, MagicAnomalyProfile.Steamwoven).ToArray());
        Assert.AreEqual(0, SpellDamageStep(Facts, kill, killed, false, false, false, MagicAnomalyProfile.Original).Count, "no parent, no source");
    }
}
