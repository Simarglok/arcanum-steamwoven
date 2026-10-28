using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-020: an item's wear and unwear spells through the cast host (contract docs/world/item-spell-triggers.md). The half-elf
// fixture's kit:2 is a coat carrying record 150, a maintained technology record like the installed 168: its BEGIN has an ungated
// NoOp (so the pass applies something and the run is maintained) and deals 2 damage on the unwear only; its MAINTAIN is the wear's.
// Every action aims at the run's target object (0x4), as record 168's MAINTAIN does. Records and numbers are invented.
[TestClass]
public sealed class SessionItemSpellTests
{
    private const string Coat = "kit:2", Ring = "kit:3";
    private static readonly SpellLocation HeroTile = new(5, 5);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            id == SessionSpellCaster.Hero ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    private static SessionSpellRecord Record150(bool technology = true, int cost = 0) =>
        SessionSpellRecords.Translate(Record(number: 150, beginTargets: 0x4, info: SpellInfo.Friendly, technology: technology,
            components:
            [
                new(SpellAction.Begin, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, 0, null),
                new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, Unwear, new DamageData(0, 2, 2, 0)),
                new(SpellAction.Maintain, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, Wear, null),
            ])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(0x4, 0, 0, 0, -1), 5)], Cost = cost, MaintainCost = 0, MaintainPeriod = 5, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1,
            NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, ItemTriggers = Wear | Unwear });

    // Record 151 fires on the wear with only the melee aim bit: the executor sets no target and starts no run (IT3).
    private static SessionSpellRecord Melee151() =>
        SessionSpellRecords.Translate(Record(number: 151, beginTargets: 0x4, info: SpellInfo.Friendly, technology: true,
            components: [new(SpellAction.Begin, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, 0, null)])
        with { ItemTriggers = Wear | WeaponMelee, Cost = 0, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionItemTriggers Triggers) Session(RuleMode mode = RuleMode.Steamwoven,
        int charges = -1, SessionSpellRecord? record = null)
    {
        var spell = record ?? Record150();
        var melee = Melee151();
        var owner = CreateWith(HalfElf());
        owner.SelectRules(mode);
        var caster = new SessionSpellCaster(owner, n => n == spell.Data.Number ? spell : n == melee.Data.Number ? melee : null, new Map(owner), new Events(),
            id => id switch
            {
                Coat => new SessionItemSpells(Coat, SessionSpellCaster.Hero, 7, [150, 10000, 10000, 10000, 10000], charges, -20),
                Ring => new SessionItemSpells(Ring, SessionSpellCaster.Hero, 7, [151, 150, 10000, 10000, 10000], -1, -20),
                _ => null,
            });
        return (owner, caster, new SessionItemTriggers(caster, new NoCombat()));
    }

    private static SessionWearableItem Offer(bool spells = true) => new(Kit[1].Kind, [], spells, new EquipmentItem
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium },
    });

    private static int HitPoints(GameSessionOwner owner) => owner.QueryHeroLife()!.Status.HitPoints;

    private static SpellRun[] Runs(GameSessionOwner owner) => owner.QuerySpellRuns().Runs.ToArray();

    // Game time past a few maintenance periods, then the event pass through the host.
    private static void Tick(GameSessionOwner owner, SessionSpellCaster caster)
    {
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 80_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.IsTrue(caster.ProcessEvents().SpellRuns.Any(f => f.Ran), "a maintenance tick ran");
    }

    [TestMethod]
    public void Without_a_trigger_host_an_item_with_spells_is_still_refused()
    {
        var (owner, _, triggers) = Session(charges: 3);
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported,
            owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers).Failure,
            "a positive charge count needs saved charges: not ready");
        Assert.AreEqual(0, owner.Version.Revision);
    }

    [TestMethod]
    public void Wearing_starts_the_wear_spell_with_the_item_as_source_and_unwearing_stops_it_and_casts_the_unwear()
    {
        var (owner, caster, triggers) = Session();
        using var _ = owner;
        var hp = HitPoints(owner);
        var worn = owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{worn.Failure}");
        var wear = worn.ItemSpells.Single();
        Assert.AreEqual(new ItemSpellFiring(0, 150, ItemSpellAim.Parent), wear.Spells.Single().Firing);
        Assert.AreEqual(SpellCastGoalOutcome.Started, wear.Goals.Single()!.Outcome);
        Assert.AreEqual(SpellRunRules.GoalCastWithoutGoal, wear.Spells.Single().Result!.Creation!.CastGoal, "the executor's flag 0x1");
        var run = Runs(owner).Single();
        Assert.AreEqual((Coat, SessionSpellCaster.Hero, SessionSpellCaster.Hero, (int)Wear, SpellRunAction.Maintain),
            (run.Source!.Id, run.Parent!.Id, run.Target!.Id, run.Triggers, run.Action), "a maintained run after its BEGIN");
        Assert.AreEqual(hp, HitPoints(owner), "the BEGIN's damage component is the unwear's only");
        Tick(owner, caster);
        Assert.AreEqual(run.Id, Runs(owner).Single().Id, "the wear's MAINTAIN applies: the run is maintained");
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady), "the BEGIN's and the tick's NoOp");

        // Without a host the coat cannot come off while its spell runs.
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept).Failure);

        var off = owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, off.Disposition, $"{off.Failure}");
        var unwear = off.ItemSpells.Single();
        CollectionAssert.AreEqual(new[] { run.Id }, unwear.Stopped.ToArray());
        Assert.AreEqual(new ItemSpellFiring(0, 150, ItemSpellAim.Parent, true), unwear.Spells.Single().Firing);
        Assert.AreEqual(hp - 2, HitPoints(owner), "the recast's BEGIN runs the unwear's damage");
        // The recast is maintained too; its MAINTAIN components are the wear's, so its first tick applies nothing and ends it.
        var recast = Runs(owner).Single();
        Assert.AreEqual(((int)Unwear, SpellRunAction.Maintain), (recast.Triggers, recast.Action));
        Assert.AreEqual(SpellRunRules.GoalCast, unwear.Spells.Single().Result!.Creation!.CastGoal, "the withdrawal's recast plays the casting goal");
        Tick(owner, caster);
        Assert.AreEqual(0, Runs(owner).Length, "the recast's first tick applies nothing and ends it");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == SessionSpellCaster.Hero));
    }

    [TestMethod]
    public void It10_a_fired_run_whose_goal_start_refuses_stays_in_the_original_and_is_freed_unpaid_in_Steamwoven()
    {
        // No charges left: the pre-animation check refuses the goal (an item source pays a charge); goal 73 has no end node.
        foreach (var (mode, stays) in new[] { (RuleMode.Original, true), (RuleMode.Steamwoven, false) })
        {
            var (owner, _, triggers) = Session(mode, charges: 0, record: Record150(technology: false, cost: 5));
            using var _ = owner;
            var worn = owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers);
            Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{mode} {worn.Failure}");
            Assert.AreEqual(SpellCastGoalOutcome.CannotAfford, worn.ItemSpells.Single().Goals.Single()!.Outcome, $"{mode}");
            Assert.AreEqual(stays, Runs(owner).Length == 1, $"{mode}");
            if (stays) Assert.AreEqual(SpellRunAction.Begin, Runs(owner).Single().Action, "it never begins");
        }
    }

    [TestMethod]
    public void An_item_without_spells_moves_through_the_host_without_firing()
    {
        var (owner, _, triggers) = Session();
        using var _ = owner;
        var worn = owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(10, 30) } },
        }), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{worn.Failure}");
        Assert.AreEqual(0, worn.ItemSpells.Single().Spells.Count);
        Assert.AreEqual(0, Runs(owner).Length);
    }

    [TestMethod]
    public void A_ring_moved_to_the_other_ring_slot_unwears_and_wears_again_and_a_melee_aim_alone_starts_nothing()
    {
        var (owner, _, triggers) = Session();
        using var _ = owner;
        SessionWearableItem Band() => new(Kit[2].Kind, [], true, new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, Sizes = ArmorSizes.None },
        });
        var worn = owner.Equip(owner.Version, Ring, EquipmentSlot.Ring1, Band(), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{worn.Failure}");
        Assert.AreEqual(owner.Version, worn.Version, "the result carries the version after the host's runs");
        CollectionAssert.AreEqual(new[] { (151, SessionItemSpellOutcome.NoTarget), (150, SessionItemSpellOutcome.Started) },
            worn.ItemSpells.Single().Spells.Select(s => (s.Firing.Spell, s.Outcome)).ToArray());
        var first = Runs(owner).Single().Id;

        var moved = owner.Equip(owner.Version, Ring, EquipmentSlot.Ring2, Band(), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, moved.Disposition, $"{moved.Failure}");
        Assert.AreEqual(2, moved.ItemSpells.Count, "the ring's unwear, then its wear");
        CollectionAssert.AreEqual(new[] { first }, moved.ItemSpells[0].Stopped.ToArray());
        Assert.IsTrue(moved.ItemSpells[0].Spells.Single().Firing.CastGoal);
        Assert.AreEqual(SessionItemSpellOutcome.Started, moved.ItemSpells[1].Spells.Single(s => s.Firing.Spell == 150).Outcome);
        Assert.AreEqual(1, Runs(owner).Count(r => r.Triggers == (int)Wear), "one wear run again");
    }
}
