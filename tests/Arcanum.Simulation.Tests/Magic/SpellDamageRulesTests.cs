using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-012: the Damage and Heal components, the acid's steps, the shared damage step and the executor (ledger DH rows,
// MG35–MG38, DF4). Objects, components and numbers are invented.
[TestClass]
public sealed class SpellDamageRulesTests
{
    private static readonly SpellRunObject Mage = new("mage", SpellObjectFacts.PcType, 60);
    private static readonly SpellRunObject Orc = new("orc", SpellObjectFacts.NpcType);

    private sealed class Rolls(params int[] values) : ISpellRollSource
    {
        private int _next;
        public List<(SpellRollPurpose Purpose, int Min, int Max)> Asked { get; } = [];

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return values[_next++];
        }
    }

    private static SpellRun Run(SpellRunObject? parent = null, int margin = 0, SpellRunObject? other = null) =>
        new(0, 7, SpellRunAction.Begin, parent ?? Mage, parent ?? Mage, SpellLocation.None, Orc, SpellLocation.None, other, SpellRunFlags.Active,
            Margin: margin);

    private static SpellDamageComponent Component(int type, int min, int max, int flags = 0) => new(type, min, max, flags);

    [TestMethod]
    public void Damage_rolls_its_range_or_scales_it_by_the_parents_captured_aptitude()
    {
        var rolls = new Rolls(9);
        var rolled = SpellDamageRules.Damage(Component(3, 5, 30), Run(), 20, rolls, MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((SpellDamageRoute.Step, 9, (SpellRollPurpose.Damage, 5, 30)), (rolled.Route, rolled.Damage.Fire, rolls.Asked.Single()));
        var scaled = SpellDamageRules.Damage(Component(0, 10, 60, SpellDamageFlags.Scaled), Run(), 20, new Rolls(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((40, 0), (scaled.Damage.Normal, scaled.Flags), "10 + 50 × 60 % with no draw; Scaled is cleared");
        Assert.AreEqual(10, SpellDamageRules.Damage(Component(0, 10, 60, SpellDamageFlags.Scaled), Run(Mage with { Aptitude = -5 }), 0, new Rolls(),
            MagicAnomalyProfile.Steamwoven).Damage.Normal, "the minimum at 0 or less");
        Assert.AreEqual(60, SpellDamageRules.Damage(Component(0, 10, 60, SpellDamageFlags.Scaled), Run(Mage with { Aptitude = 100 }), 0, new Rolls(),
            MagicAnomalyProfile.Steamwoven).Damage.Normal, "a fate spell at maximum (aptitude 100)");
        var trap = new SpellRunObject("trap", 17);
        Assert.AreEqual(33, SpellDamageRules.Damage(Component(0, 10, 60, SpellDamageFlags.Scaled), Run(trap), 0, new Rolls(33), MagicAnomalyProfile.Steamwoven)
            .Damage.Normal, "a parent that is no critter rolls the range");
    }

    [TestMethod]
    public void Full_takes_the_fatigue_or_leaves_the_step_the_hit_points_and_a_margin_cancels_a_death_hit()
    {
        var fatigue = SpellDamageRules.Damage(Component(4, 0, 0, SpellDamageFlags.Full), Run(), 23, new Rolls(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((33, false), (fatigue.Damage.Fatigue, fatigue.FullAmount), "current fatigue + 10, Full cleared");
        var kill = SpellDamageRules.Damage(Component(0, 0, 0, SpellDamageFlags.Full | SpellDamageFlags.Death), Run(), 0, new Rolls(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((SpellDamageRoute.Step, true, true), (kill.Route, kill.FullAmount, kill.PlainDeath));
        Assert.AreEqual(SpellDamageRoute.Cancelled, SpellDamageRules.Damage(Component(0, 0, 0, SpellDamageFlags.Full | SpellDamageFlags.Death), Run(margin: 5), 0,
            new Rolls(), MagicAnomalyProfile.Steamwoven).Route);
    }

    [TestMethod]
    public void A_margin_takes_its_share_at_least_one_and_the_original_takes_it_from_no_damage_Mg37()
    {
        Assert.AreEqual(30, SpellDamageRules.Damage(Component(0, 40, 40), Run(margin: 25), 0, new Rolls(), MagicAnomalyProfile.Steamwoven).Damage.Normal);
        Assert.AreEqual(2, SpellDamageRules.Damage(Component(0, 3, 3), Run(margin: 10), 0, new Rolls(), MagicAnomalyProfile.Steamwoven).Damage.Normal,
            "10 % of 3 is 0: at least 1");
        var stun = Component(0, 0, 0, SpellDamageFlags.Stunned);
        Assert.AreEqual(-1, SpellDamageRules.Damage(stun, Run(margin: 10), 0, new Rolls(), MagicAnomalyProfile.Original).Damage.Normal, "a 1-point heal");
        Assert.AreEqual(0, SpellDamageRules.Damage(stun, Run(margin: 10), 0, new Rolls(), MagicAnomalyProfile.Steamwoven).Damage.Normal);
        var acid = SpellDamageRules.Damage(Component(5, 30, 30), Run(margin: 50), 0, new Rolls(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((SpellDamageRoute.Acid, 30), (acid.Route, acid.Damage.Normal), "acid fills the normal slot without the margin");
    }

    [TestMethod]
    public void Acid_hits_every_worn_item_then_the_wearer_takes_a_third_and_body_armour_spares_it_only_in_Steamwoven_Mg38()
    {
        var packet = new SpellDamagePacket(SpellDamageRoute.Acid, DamageByType.Zero with { Normal = 90 }, 0);
        SpellAcidItem[] worn = [new("armour", 1006, false, 50), new("helmet", 1000, false, 0), new("ring", 1002, true, 80)];
        var original = SpellDamageRules.Acid(packet, true, worn, MagicAnomalyProfile.Original);
        Assert.AreEqual("helmet:90 ring:90 armour:90", string.Join(" ", original.Items.Select(i => $"{i.Item.Id}:{i.Damage.Normal}")),
            "slot order; an untouched item lowers nothing");
        Assert.AreEqual(15, original.Wearer!.Value.Normal, "a third of what the armour's resistance left");
        Assert.IsNull(SpellDamageRules.Acid(packet, true, worn, MagicAnomalyProfile.Steamwoven).Wearer);
        Assert.AreEqual((0, 30), (SpellDamageRules.Acid(packet, false, worn, MagicAnomalyProfile.Original).Items.Count,
            SpellDamageRules.Acid(packet, false, worn, MagicAnomalyProfile.Original).Wearer!.Value.Normal), "an object takes a third at once");
    }

    [TestMethod]
    public void Heal_fills_the_slot_of_its_type_and_its_flags_give_the_cures_and_the_raise()
    {
        var heal = SpellDamageRules.Heal(Component(1, 50, 50), Run(), new Rolls());
        Assert.AreEqual((0, 50, 0), (heal.HitPoints, heal.Poison, heal.Fatigue));
        Assert.AreEqual(8, SpellDamageRules.Heal(Component(3, 1, 10), Run(), new Rolls(8)).HitPoints, "fire heals hit points");
        var raise = SpellDamageRules.Heal(Component(0, 0, 0, SpellDamageFlags.Full | SpellDamageFlags.Raise | SpellDamageFlags.Reanimate | SpellDamageFlags.Scars), Run(), new Rolls());
        Assert.AreEqual((true, true, true, true, 0), (raise.Full, raise.Resurrect, raise.Reanimate, raise.Scars, raise.HitPoints));
        var cure = SpellDamageRules.Heal(Component(0, 40, 80, SpellDamageFlags.Scaled | 0xE00), Run(), new Rolls());
        Assert.AreEqual((64, true, true, true), (cure.HitPoints, cure.Blindness, cure.CrippledArms, cure.CrippledLegs));
    }

    private static Combatant Pc => new() { IsPc = true };

    [TestMethod]
    public void The_damage_step_takes_the_current_hit_points_for_a_kill_and_the_original_scales_them_by_difficulty_Mg35()
    {
        DamageStepRequest Kill(bool scales) => new()
        {
            Attacker = Pc, Target = new Combatant(), Difficulty = GameDifficulty.Hard, FullAmount = 40, DifficultyScalesFullAmount = scales,
            Damage = DamageByType.Zero with { Normal = 3 },
        };
        Assert.AreEqual(30, AttackResolver.DamageStep(Kill(true)).HitPointDamage, "three quarters: the target lives");
        Assert.AreEqual(40, AttackResolver.DamageStep(Kill(false)).HitPointDamage);
        var plain = AttackResolver.DamageStep(new() { Attacker = Pc, Target = new() { Resistances = new(50, 0, 0, 20) }, Difficulty = GameDifficulty.Hard,
            Damage = DamageByType.Zero with { Normal = 20, Fire = 10 } });
        Assert.AreEqual((10 + 8 - 4, true), (plain.HitPointDamage, plain.DamagesArmor), "resistances, then hard takes a quarter; fire wears armour");
    }

    [TestMethod]
    public void A_nigh_invulnerable_critter_takes_a_spell_only_from_a_caster_with_an_uber_weapon_in_the_original_Mg36()
    {
        var world = new World { Weapon = new CombatWeapon { Kind = WeaponKind.Weapon, Uber = true } };
        world.Targets["orc"] = new Combatant { Conditions = CombatantConditions.NighInvulnerable };
        var component = Component(0, 12, 12);
        var original = SpellLifeComponents.Damage(component, Run(), Orc, world, new Rolls(), MagicAnomalyProfile.Original).Steps.Single();
        var steamwoven = SpellLifeComponents.Damage(component, Run(), Orc, world, new Rolls(), MagicAnomalyProfile.Steamwoven).Steps.Single();
        Assert.AreEqual((12, DamagePrevention.NighInvulnerable), (original.Damage.HitPointDamage, steamwoven.Damage.Prevented));
        Assert.IsNull(steamwoven.Life);
    }

    [TestMethod]
    public void The_executor_passes_the_rule_modes_kill_and_weapon_switches_to_the_step_Mg35_Mg36()
    {
        var hard = new World { Hard = true, Weapon = new CombatWeapon { Kind = WeaponKind.Weapon, IgnoresResistance = true } };
        hard.Targets["orc"] = new Combatant { Resistances = new(50, 0, 0, 0) };
        var kill = Component(0, 0, 0, SpellDamageFlags.Full | SpellDamageFlags.Death);
        Assert.AreEqual(23, SpellLifeComponents.Damage(kill, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Original).Steps.Single().Damage.HitPointDamage,
            "a quarter of 30 off on hard: the orc lives");
        Assert.AreEqual(30, SpellLifeComponents.Damage(kill, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Steamwoven).Steps.Single().Damage.HitPointDamage);
        var bolt = Component(0, 40, 40);
        Assert.AreEqual(30, SpellLifeComponents.Damage(bolt, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Original).Steps.Single().Damage.HitPointDamage,
            "the caster's weapon ignores the orc's resistance; hard takes a quarter");
        Assert.AreEqual(15, SpellLifeComponents.Damage(bolt, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Steamwoven).Steps.Single().Damage.HitPointDamage,
            "resisted, then a quarter off");
        hard.Worn.Add(new SpellAcidItem("helmet", 1000, false, 50));
        hard.Targets["helmet"] = new Combatant { Resistances = new(50, 0, 0, 0) };
        var acid = Component(5, 60, 60);
        Assert.AreEqual("helmet:45 orc:15", string.Join(" ", SpellLifeComponents.Damage(acid, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Original).Steps
            .Select(s => $"{s.Object}:{s.Damage.HitPointDamage}")), "the weapon ignores the helmet's resistance: the wearer's third of 60, then hard");
        Assert.AreEqual("helmet:23 orc:4", string.Join(" ", SpellLifeComponents.Damage(acid, Run(), Orc, hard, new Rolls(), MagicAnomalyProfile.Original with
            { SpellDamageUsesCastersWeapon = false }).Steps.Select(s => $"{s.Object}:{s.Damage.HitPointDamage}")), "the helmet's step halves what the wearer gets");
    }

    [TestMethod]
    public void The_executor_hands_the_host_the_life_damage_and_the_acid_steps()
    {
        var world = new World();
        world.Worn.Add(new SpellAcidItem("gloves", 1007, false, 0));
        var kill = SpellLifeComponents.Damage(Component(0, 0, 0, SpellDamageFlags.Full | SpellDamageFlags.Death | SpellDamageFlags.Stunned), Run(other: Orc),
            Orc, world, new Rolls(), MagicAnomalyProfile.Steamwoven);
        var life = kill.Steps.Single().Life!;
        Assert.AreEqual((30, true, CriticalHitEffects.Stun, "orc"), (life.HitPoints, life.PlainDeath, life.Effects, kill.Source!.Id),
            "the current hit points; the run's other object is the source");
        var acid = SpellLifeComponents.Damage(Component(5, 30, 30), Run(), Orc, world, new Rolls(), MagicAnomalyProfile.Original);
        Assert.AreEqual("gloves:30 orc:10", string.Join(" ", acid.Steps.Select(s => $"{s.Object}:{s.Damage.HitPointDamage}")));
        Assert.IsNull(acid.Steps[0].Life, "an item is the host's object damage");
        var dead = LifeRules.ApplyDamage(LifeState.Unharmed, new LifeFacts(), life, new NoRolls());
        Assert.AreEqual(DeathAnimation.Plain, dead.Death);
    }

    [TestMethod]
    public void A_raise_asks_the_raise_script_which_runs_even_for_a_refused_target()
    {
        var facts = new LifeFacts { Traits = LifeTraits.Unresurrectable };
        var dead = LifeState.Unharmed with { HitPointDamage = LifeLimits.DeathHitPointDamage };
        var asked = 0;
        var refused = LifeRules.Heal(dead, facts, new LifeHeal { Resurrect = true, HitPoints = 5 }, () => { asked++; return true; });
        Assert.AreEqual((LifeEvents.ResurrectionRefused, 1), (refused.Events, asked));
        var vetoed = LifeRules.Heal(dead, new LifeFacts(), new LifeHeal { Resurrect = true }, () => false);
        Assert.AreEqual(LifeEvents.ResurrectionRefused, vetoed.Events);
        Assert.IsTrue(LifeRules.Heal(dead, new LifeFacts(), new LifeHeal { Resurrect = true }, () => true).Events.HasFlag(LifeEvents.Resurrected));
    }

    private sealed class NoRolls : ICombatRollSource
    {
        public int Roll(CombatRollPurpose purpose, int min, int max) => throw new AssertFailedException("no roll");
    }

    private sealed class World : ISpellDamageWorld
    {
        public Dictionary<string, Combatant> Targets { get; } = [];
        public CombatWeapon Weapon { get; init; } = CombatWeapon.Unarmed();
        public List<SpellAcidItem> Worn { get; } = [];
        public Combatant Combatant(SpellRunObject who) => who.Id == "mage" ? Pc : Targets.TryGetValue(who.Id, out var c) ? c : new Combatant();
        public AttackTargetKind Kind(SpellRunObject who) => who.IsCritter ? AttackTargetKind.Critter : AttackTargetKind.OtherObject;
        public CombatWeapon WieldedWeapon(SpellRunObject who) => Weapon;
        public bool Hard { get; init; }
        public GameDifficulty Difficulty => Hard ? GameDifficulty.Hard : GameDifficulty.Normal;
        public int CurrentHitPoints(SpellRunObject who) => 30;
        public int CurrentFatigue(SpellRunObject who) => 18;
        public IReadOnlyList<SpellAcidItem> WornItems(SpellRunObject critter) => Worn;
    }
}
