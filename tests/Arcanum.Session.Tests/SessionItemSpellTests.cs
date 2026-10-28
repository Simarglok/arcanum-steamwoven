using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
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
        var (owner, _, _) = Session(charges: 3);
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept).Failure);
        Assert.AreEqual(0, owner.Version.Revision);
    }

    [TestMethod]
    public void A_spent_charge_is_saved_with_the_magical_flag_and_survives_a_restore()
    {
        // ARC-MAGIC-022: a positive charge count is supported now; the BEGIN's payment by the item source spends one (0x450420, DV1).
        var (owner, caster, triggers) = Session(charges: 3, record: Record150(technology: false, cost: 5));
        using var _ = owner;
        Assert.IsTrue(triggers.Supports(Coat));
        Assert.IsNull(owner.QueryItemCharges(Coat));
        Assert.IsFalse(caster.Item(Coat)!.Magical);
        var worn = owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{worn.Failure}");
        Assert.AreEqual(SpellCastGoalOutcome.Started, worn.ItemSpells.Single().Goals.Single()!.Outcome);
        Assert.AreEqual(2, owner.QueryItemCharges(Coat));
        Assert.AreEqual((2, true), (caster.Item(Coat)!.Charges, caster.Item(Coat)!.Magical), "the host reads the session's count and the flag");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ChargeSpent && e.Value == 2 && e.Object == Coat));
        Assert.IsTrue(owner.QueryEquipment().Worn.Single().ItemSpells, "worn through the host");

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"charges\":[{\"entity\":\"kit:2\",\"charges\":2}]");
        StringAssert.Contains(text, "\"itemSpells\":true}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionItemCharges(Coat, 2), restored.QueryDurability().Charges.Single());
        Assert.IsTrue(restored.QueryEquipment().Worn.Single().ItemSpells);
        // Runs are not saved (MT9b): the mark alone keeps the coat on until a host takes it off with its unwear.
        Assert.AreEqual(0, restored.QuerySpellRuns().Runs.Count());
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, restored.Unequip(restored.Version, EquipmentSlot.Armor, Accept).Failure);
        var negative = GameSessionSnapshotCodec.TryDecode(text.Replace("\"charges\":2}", "\"charges\":-1}"));
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.durability.charges[0].charges"), (negative.Failure, negative.Path));
    }

    [TestMethod]
    public void The_last_charge_goes_and_an_empty_item_then_refuses_its_payment()
    {
        var (owner, caster, triggers) = Session(charges: 1, record: Record150(technology: false, cost: 5));
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers).Disposition);
        Assert.AreEqual(0, owner.QueryItemCharges(Coat), "no charge left; the row keeps the magical flag");
        var off = owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, off.Disposition, $"{off.Failure}");
        Assert.IsFalse(owner.QueryEquipment().Worn.Any());
        Assert.AreEqual(SpellCastGoalOutcome.CannotAfford, off.ItemSpells.Single().Goals.Single()!.Outcome, "the unwear's recast has no charge to pay");
        Assert.AreEqual(0, owner.QueryItemCharges(Coat));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SpendItemCharge(owner.Version, Coat, 1).Disposition);
    }

    [TestMethod]
    public void Spending_a_charge_refuses_bad_input_and_items_the_session_does_not_track()
    {
        var (owner, _, _) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.SpendItemCharge(owner.Version, Coat, 0).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.SpendItemCharge(owner.Version, "", 1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SpendItemCharge(owner.Version with { Revision = 7 }, Coat, 1).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.SpendItemCharge(owner.Version, "kit:99", 1).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendItemCharge(owner.Version, Coat, 4).Disposition);
        Assert.AreEqual(3, owner.QueryItemCharges(Coat));
        Assert.AreEqual(1, owner.Version.Revision);
    }

    [TestMethod]
    public void Charge_rows_are_canonical_belong_to_live_items_and_go_with_a_deleted_item()
    {
        var (owner, _, _) = Session();
        using var _ = owner;
        owner.SpendItemCharge(owner.Version, Coat, 4);
        owner.SpendItemCharge(owner.Version, Ring, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Listed = "\"charges\":[{\"entity\":\"kit:2\",\"charges\":3},{\"entity\":\"kit:3\",\"charges\":1}]";
        StringAssert.Contains(text, Listed);
        void Fails(string to, SnapshotDecodeFailure failure) =>
            Assert.AreEqual(failure, GameSessionSnapshotCodec.TryDecode(text.Replace(Listed, to, StringComparison.Ordinal)).Failure, to);
        Fails("\"charges\":[{\"entity\":\"kit:3\",\"charges\":1},{\"entity\":\"kit:2\",\"charges\":3}]", SnapshotDecodeFailure.NotCanonical);
        Fails("\"charges\":[{\"entity\":\"kit:2\",\"charges\":3},{\"entity\":\"kit:2\",\"charges\":1}]", SnapshotDecodeFailure.NotCanonical);
        Fails("\"charges\":[{\"entity\":\"kit:2\"}]", SnapshotDecodeFailure.MissingField);
        Fails("\"charges\":[{\"entity\":\"kit:2\",\"charges\":3,\"extra\":1}]", SnapshotDecodeFailure.UnknownField);
        Fails("\"charges\":[{\"entity\":\"kit:2\",\"charges\":1000001}]", SnapshotDecodeFailure.OutOfRange);
        var destroyed = text.Replace("\"destroyed\":[]", "\"destroyed\":[\"kit:2\"]", StringComparison.Ordinal);
        Assert.AreNotEqual(text, destroyed);
        Assert.AreEqual(GameSessionFailure.InvalidDurability, GameSessionOwner.Restore(Decode(destroyed), SessionFixtures.RestoreOptions()).Failure,
            "a charge row of a deleted item");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, Coat, new(20, 0, 0, false)).Disposition);
        Assert.IsNull(owner.QueryItemCharges(Coat), "the row goes with the item");
        Assert.AreEqual(new SessionItemCharges(Ring, 1), owner.QueryDurability().Charges.Single());
        Assert.IsNull(owner.QueryItemCharges(""));
    }

    [TestMethod]
    public void A_marked_ring_moves_only_through_a_host()
    {
        var (owner, caster, triggers) = Session();
        using var _ = owner;
        SessionWearableItem Band() => new(Kit[2].Kind, [], true, new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring, Sizes = ArmorSizes.None },
        });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, Ring, EquipmentSlot.Ring1, Band(), Drawn, Accept, triggers: triggers).Disposition);
        // The run ends (its source cleared), so only the mark keeps the ring's moves on the host.
        foreach (var run in Runs(owner)) caster.Act(new(SessionSpellRunActionKind.ClearSource, run.Id));
        Assert.AreEqual(0, Runs(owner).Count(r => r.Source is not null));
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.Equip(owner.Version, Ring, EquipmentSlot.Ring2, Band(), Drawn, Accept).Failure,
            "the marked ring itself");
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.Unequip(owner.Version, EquipmentSlot.Ring1, Accept).Failure);
        var moved = owner.Equip(owner.Version, Ring, EquipmentSlot.Ring2, Band(), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, moved.Disposition, $"{moved.Failure}");
        Assert.IsTrue(owner.QueryEquipment().Worn.Single().ItemSpells, "the mark moves with the ring");
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
    public void It10_a_fired_run_whose_goal_start_refuses_is_freed_unpaid_in_both_modes()
    {
        // No charges left: the pre-animation check refuses the goal (an item source pays a charge); goal 73 has no end node, so the
        // original keeps the run in its slot forever; the owner fixed that leak in both modes (2026-10-28).
        foreach (var mode in new[] { RuleMode.Original, RuleMode.Steamwoven })
        {
            var (owner, _, triggers) = Session(mode, charges: 0, record: Record150(technology: false, cost: 5));
            using var _ = owner;
            var worn = owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, Offer(), Drawn, Accept, triggers: triggers);
            Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{mode} {worn.Failure}");
            Assert.AreEqual(SpellCastGoalOutcome.CannotAfford, worn.ItemSpells.Single().Goals.Single()!.Outcome, $"{mode}");
            Assert.AreEqual(0, Runs(owner).Length, $"{mode}: freed");
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

    // ---- ARC-MAGIC-022: the item use's spell path (0x462CC0) ----

    private const string Wand = "kit:1", Scroll = "kit:2", Potion = "kit:3";

    // A record whose BEGIN deals 2 damage at the run's target; `begin` is its BEGIN area, `triggers` its item triggers (its damage is
    // gated by them when given).
    private static SessionSpellRecord Use(int number, ulong begin, uint triggers = 0, int duration = 0) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: begin, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(begin == 0x1 ? 0x1UL : 0x4UL, 0, 0, 0, -1), 0, triggers, new DamageData(0, 2, 2, 0))])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(begin, 0, 0, 0, -1), 5)], Cost = 5, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = duration,
            DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0,
            DisallowedTargetCritterFlags = 0, ItemTriggers = triggers });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionItemTriggers Triggers) UseSession(int wandCharges = 20, int scrollSpell = 162,
        int[]? stats = null, int potionDuration = 0)
    {
        var disabled = SessionSpellRecords.Translate(Use(165, 0x4).Data with { Enabled = false });
        var records = new[] { Use(160, 0x4), Use(161, 0x4, ItemUsed | Other, potionDuration), Use(162, 0x1), Use(163, 0x2000000), disabled }
            .ToDictionary(r => r.Data.Number);
        // The kit: a weapon (the wand), a scroll and a food item (the potion); prototypes and names are invented.
        var owner = CreateWith(HalfElf(stats) with
        {
            Kit = [Kit[0], new(new(SessionItemType.Scroll, 9700, 4100, 9700)), new(new(SessionItemType.Food, 9800, 4200, 9800))],
        });
        var caster = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner), new Events(),
            id => id switch
            {
                Wand => new SessionItemSpells(Wand, SessionSpellCaster.Hero, 5, [160, 10000, 10000, 10000, 10000], wandCharges, 10),
                Scroll => new SessionItemSpells(Scroll, SessionSpellCaster.Hero, SessionItemSpells.ScrollType, [scrollSpell, 10000, 10000, 10000, 10000], 1, 10),
                Potion => new SessionItemSpells(Potion, SessionSpellCaster.Hero, SessionItemSpells.FoodType, [161, 10000, 10000, 10000, 10000], 1, 10),
                _ => null,
            });
        return (owner, caster, new SessionItemTriggers(caster, new NoCombat()));
    }

    [TestMethod]
    public void A_wand_casts_its_slot_zero_spell_at_the_target_from_the_item_and_spends_a_saved_charge()
    {
        var (owner, caster, triggers) = UseSession();
        using var _ = owner;
        var hp = HitPoints(owner);
        var use = triggers.UseItem(Wand, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.AtTarget, SpellCastAttemptOutcome.Started), (use.Outcome, use.Cast, use.Attempt!.Outcome));
        Assert.AreEqual(SpellCastGoalOutcome.Started, use.Goal!.Outcome);
        Assert.AreEqual(SpellRunRules.GoalCast, use.Attempt.Run!.Creation!.CastGoal, "the activation's cast plays the casting goal");
        Assert.AreEqual(hp - 2, HitPoints(owner));
        Assert.AreEqual(19, owner.QueryItemCharges(Wand), "the item paid, not the hero");
        Assert.AreEqual(0, owner.QueryHeroLife()!.Record.State.FatigueDamage, "no fatigue for the hero");
        Assert.AreEqual(0, use.ItemUsed!.Spells.Count, "record 160 has no item triggers");
        Assert.IsFalse(use.Spent);
        Assert.IsFalse(owner.IsItemDestroyed(Wand));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ChargeSpent && e.Object == Wand));
    }

    [TestMethod]
    public void An_item_without_charges_or_magic_takes_the_other_branches_and_an_exhausted_wand_casts_but_cannot_pay()
    {
        var (owner, _, triggers) = UseSession(wandCharges: 0);
        using var _ = owner;
        Assert.AreEqual(SessionItemUseOutcome.NoMagic, triggers.UseItem(Wand, SessionSpellCaster.Hero).Outcome);
        Assert.AreEqual(SessionItemUseOutcome.Unknown, triggers.UseItem("kit:9", SessionSpellCaster.Hero).Outcome);
        Assert.AreEqual(0, owner.Version.Revision);

        var (charged, _, host) = UseSession(wandCharges: 1);
        using var __ = charged;
        Assert.AreEqual(SpellCastGoalOutcome.Started, host.UseItem(Wand, SessionSpellCaster.Hero).Goal!.Outcome);
        Assert.AreEqual(0, charged.QueryItemCharges(Wand));
        // No charge left, but the magical flag the spend set keeps the spell path; the item source skips the cost check at the cast,
        // and the goal's check refuses (the item pays nothing).
        var again = host.UseItem(Wand, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SpellCastGoalOutcome.CannotAfford), (again.Outcome, again.Goal!.Outcome));
        Assert.AreEqual(0, charged.QuerySpellRuns().Runs.Count(), "the refused goal frees the run");
    }

    [TestMethod]
    public void A_potion_fires_item_used_at_the_target_and_is_spent()
    {
        var (owner, _, triggers) = UseSession();
        using var _ = owner;
        var hp = HitPoints(owner);
        var use = triggers.UseItem(Potion, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.Triggered), (use.Outcome, use.Cast), "slot 0's record has item triggers");
        var fired = use.ItemUsed!.Spells.Single();
        Assert.AreEqual((161, ItemSpellAim.Other, SessionItemSpellOutcome.Started), (fired.Firing.Spell, fired.Firing.Aim, fired.Outcome));
        Assert.AreEqual(SpellRunRules.GoalCastWithoutGoal, fired.Result!.Creation!.CastGoal);
        Assert.AreEqual(hp - 2, HitPoints(owner), "the gated damage runs: the run's triggers are Item_Used");
        Assert.IsTrue(use.Spent);
        Assert.IsTrue(owner.IsItemDestroyed(Potion));
        Assert.IsNull(owner.QueryItemCharges(Potion), "the spent charge's row went with the potion");
        Assert.IsTrue(owner.QuerySpellRuns().Runs.All(r => r.Source?.Id != Potion), "the spend cleared the runs' source");
    }

    [TestMethod]
    public void A_scroll_with_a_self_spell_casts_at_the_holder_s_tile_and_an_inventory_spell_is_not_ready()
    {
        var (owner, caster, triggers) = UseSession();
        using var _ = owner;
        var use = triggers.UseItem(Scroll, "kit:1");
        Assert.AreEqual((SessionItemUseCast.AtCaster, SpellCastAttemptOutcome.Started), (use.Cast, use.Attempt!.Outcome), "the use's target is not read");
        Assert.AreEqual(caster.Facts(SessionSpellCaster.Hero)!.Location, use.Attempt.Run!.Creation!.Run!.TargetTile);
        Assert.IsTrue(use.Spent);
        Assert.IsFalse(caster.ScrollTooHard(Wand));

        var (inventory, _, host) = UseSession(scrollSpell: 163);
        using var __ = inventory;
        var notReady = host.UseItem(Scroll, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseCast.InventoryTarget, true), (notReady.Cast, notReady.Spent), "the scroll is still spent, as in the original");
    }

    // ---- ARC-MAGIC-022: pickup and drop through the session's inventory moves (IU9, IU10) ----

    [TestMethod]
    public void Giving_an_item_away_ends_its_runs_and_drops_and_taking_it_back_fires_its_pickup()
    {
        // Record 170 fires on the pickup and the drop and deals 2 at the holder; record 164 is the wand's lasting spell (30 s).
        var moved = Use(170, 0x4, Pickup | Drop);
        var lasting = Use(164, 0x4, duration: 30);
        var records = new[] { moved, lasting }.ToDictionary(r => r.Data.Number);
        var owner = CreateWith(HalfElf());
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(SessionFixtures.Exterior, InventoryStateTests.Seed()).Failure);
        var caster = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner), new Events(),
            id => id switch
            {
                // The lookup follows the move: once given, the coat is the merchant's.
                Coat => new SessionItemSpells(Coat, owner.QueryHeroKit().Any(k => k.Entity == Coat) ? SessionSpellCaster.Hero : "merchant", 7,
                    [170, 10000, 10000, 10000, 10000], -1, 10),
                Wand => new SessionItemSpells(Wand, SessionSpellCaster.Hero, 5, [164, 10000, 10000, 10000, 10000], 5, 10),
                _ => null,
            });
        var triggers = new SessionItemTriggers(caster, new NoCombat());
        var hp = HitPoints(owner);

        var given = owner.GiveItemToNpc(owner.Version, "merchant", Coat, Accept, triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, given.Disposition, $"{given.Failure}");
        var drop = given.ItemSpells.Single();
        Assert.AreEqual(new ItemSpellFiring(0, 170, ItemSpellAim.Parent, true), drop.Spells.Single().Firing, "the drop's withdrawal recasts at the holder");
        Assert.AreEqual(SessionItemSpellOutcome.Started, drop.Spells.Single().Outcome, "the coat counts as the hero's while it withdraws");
        Assert.AreEqual(hp - 2, HitPoints(owner));
        Assert.AreEqual(given.Version, owner.Version);

        var taken = owner.TakeItemFromNpc(owner.Version, "merchant", Coat, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, taken.Disposition, $"{taken.Failure}");
        Assert.AreEqual(new ItemSpellFiring(0, 170, ItemSpellAim.Parent), taken.ItemSpells.Single().Spells.Single().Firing, "the pickup (0x4CBAA0)");
        Assert.AreEqual(hp - 4, HitPoints(owner));

        // A run the wand is the source of: without a host the wand cannot leave; with one its run ends by event.
        var use = triggers.UseItem(Wand, SessionSpellCaster.Hero);
        Assert.AreEqual(SpellCastGoalOutcome.Started, use.Goal!.Outcome);
        var run = use.Attempt!.RunId!.Value;
        Assert.AreEqual(Wand, owner.QuerySpellRuns()[run]!.Source!.Id, "a lasting run with the wand as source");
        Assert.AreEqual(GameSessionFailure.ItemEffectUnsupported, owner.GiveItemToNpc(owner.Version, "merchant", Wand, Accept).Failure);
        var gone = owner.GiveItemToNpc(owner.Version, "merchant", Wand, Accept, triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, gone.Disposition, $"{gone.Failure}");
        CollectionAssert.AreEqual(new[] { run }, gone.ItemSpells.Single().Stopped.ToArray(), "0x457450 ends the wand's run");
        Assert.AreEqual(0, gone.ItemSpells.Single().Spells.Count, "record 164 has no drop trigger");
    }

    [TestMethod]
    public void The_slot_zero_cast_does_nothing_for_an_empty_slot_or_a_disabled_spell_and_a_surviving_potion_run_loses_its_source()
    {
        var (owner, _, triggers) = UseSession(scrollSpell: 10000, potionDuration: 30);
        using var _ = owner;
        var empty = triggers.UseItem(Scroll, SessionSpellCaster.Hero);
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.NoSpell, true), (empty.Outcome, empty.Cast, empty.Spent), "IU6: nothing cast");
        var potion = triggers.UseItem(Potion, SessionSpellCaster.Hero);
        var run = potion.ItemUsed!.Spells.Single().Run!.Value;
        Assert.IsNotNull(owner.QuerySpellRuns()[run], "a lasting run");
        Assert.IsNull(owner.QuerySpellRuns()[run]!.Source, "0x4574D0 cleared the spent potion as source");

        var (disabledOwner, _, host) = UseSession(scrollSpell: 165);
        using var __ = disabledOwner;
        Assert.AreEqual(SessionItemUseCast.Nothing, host.UseItem(Scroll, SessionSpellCaster.Hero).Cast);
    }

    [TestMethod]
    public void A_scroll_needs_intelligence_five_and_only_food_or_a_scroll_the_hero_holds_is_destroyed_as_used()
    {
        var stats = Defaults();
        stats[(int)Arcanum.Simulation.Character.CharacterStat.Intelligence] = 3;
        var (dull, caster, _) = UseSession(stats: stats);
        using var _ = dull;
        Assert.IsTrue(caster.ScrollTooHard(Scroll));
        Assert.IsFalse(caster.ScrollTooHard(Potion));

        var (owner, _, _) = UseSession();
        using var __ = owner;
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.DestroyUsedItem(owner.Version, Wand).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DestroyUsedItem(owner.Version, "kit:9").Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DestroyUsedItem(owner.Version with { Revision = 7 }, Scroll).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DestroyUsedItem(owner.Version, "").Failure);
        Assert.AreEqual(0, owner.Version.Revision);
    }
}
