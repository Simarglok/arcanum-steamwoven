using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1232 (DW2 of ARC-RES-1109, XR15 of ARC-DEV-357): a Damage component with the weapon-drop flag (Weap_Dropped 0x2000; Disarm is one component
// of amount 0) takes the TARGET's weapon through the session's critical drop, at the drop place of the attack paths, and the zero amount ends nothing.
// A rat holds an invented sword in its map seed; the hero wields the invented kit blade. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterDisarmTests
{
    private const string Rat = "oid:rat", Thug = "oid:thug", Sword = "rat-sword", Hero = SessionSpellCaster.Hero;
    private const uint Weap_Dropped = 0x2000;
    private static readonly SpellLocation HeroTile = new(5, 5), RatTile = new(6, 5), ThugTile = new(7, 5);
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class Map(GameSessionOwner owner, SpellCastCritter rat, SpellCastCritter thug) : ISpellCastMap
    {
        public Dictionary<string, Dictionary<EquipmentSlot, SessionWearItem>> Worn { get; } = [];
        public IDropMap? Drops { get; set; }

        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Rat => new(id, SpellTargetObject.Npc, RatTile, HitPoints: 20),
            Thug => new(id, SpellTargetObject.Npc, ThugTile, HitPoints: 20),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) =>
            new[] { Hero, Rat, Thug }.Select(Object).Where(o => o is not null && o.Location == tile).Select(o => o!).ToArray();
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity == Rat ? rat : entity == Thug ? thug : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
        public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? WornBySlot(string entity) => Worn.GetValueOrDefault(entity);
        public IDropMap? DropMap => Drops;
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

    private static SpellCastCritter Npc(string entity, uint flags2 = 0)
    {
        var stats = AttackStateTests.Stats(willpower: 9);
        stats[4] = 12;
        return new(SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: stats, critterFlags: 0, flags2: flags2)).Combat!, 0, 0, 0, 0, unchecked((int)flags2));
    }

    // Disarm's shape: one BEGIN Damage component of amount `amount` with the weapon-drop flag at a critter target.
    private static SessionSpellRecord Disarm(int amount = 0, uint flags = Weap_Dropped) =>
        SessionSpellRecords.Translate(Record(number: 91, beginTargets: 0x44, components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x44, 0, 0, 0, -1), 0, 0, new DamageData(0, amount, amount, flags))])
        with { Cost = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0x4, MinimumWillpower = 0 });

    private static EquipmentItem Blade() => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 1000, MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15, CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed },
    };

    private static SessionMapInventorySeed Seed() => new(
        [new(Rat, SessionHolderKind.Npc, false, false, [], [Sword])],
        [new SessionSeedItem(Sword, InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon), null)]);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Map Map) Session(SessionSpellRecord record, bool ratSword = true, bool heroBlade = false, bool nighInvulnerable = false)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        if (heroBlade)
            Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Blade()), (_, _) => true, Accept).Failure);
        var map = new Map(owner, Npc(Rat, nighInvulnerable ? 0x200u : 0), Npc(Thug));
        if (ratSword) map.Worn[Rat] = new() { [EquipmentSlot.Weapon] = new(Sword, Sturdy) };
        if (heroBlade) map.Worn[Hero] = new() { [EquipmentSlot.Weapon] = new("kit:1", Sturdy) };
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, map, new Events()), map);
    }

    // Casts `record` from `parent` at `target` and releases it.
    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent, string target)
    {
        var from = caster.Facts(parent)!;
        var to = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, from, from, to, to.Location, ParentIsLocalPlayer: parent == Hero), Free: true),
            record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        return run;
    }

    [TestMethod]
    public void Disarm_takes_an_npcs_weapon_to_its_cell_though_the_amount_is_zero()
    {
        var record = Disarm();
        var (owner, caster, _) = Session(record);
        using var _ = owner;
        var life = owner.QueryLife(caster.LifeTarget(Rat)!).View!.Status.HitPoints;
        Cast(owner, caster, record, Hero, Rat);
        var step = caster.TakeDamageSteps().Single();
        Assert.AreEqual(0, step.Damage.HitPointDamage, "Disarm deals no damage");
        Assert.IsTrue(step.Damage.HitEffects.HasFlag(CriticalHitEffects.DropWeapon));
        var drop = step.Criticals.Single();
        Assert.AreEqual((SessionCriticalStepKind.DropWeapon, DamageRecipient.Target, EquipmentSlot.Weapon, Sword, SessionCriticalStepOutcome.Dropped, (CellCoord?)new CellCoord(6, 5)),
            (drop.Kind, drop.Whose, drop.Slot, drop.Entity, drop.Outcome, drop.Cell), "the target's weapon, at its cell (no drop map: no distance)");
        Assert.AreEqual(life, owner.QueryLife(caster.LifeTarget(Rat)!).View!.Status.HitPoints);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.WeaponDropped && e.Object == Rat && e.Value == (int)SessionCriticalStepOutcome.Dropped), string.Join(" | ", caster.Log));
        // The seed item left the rat's list and lies on the ground; ARC-WORLD-062's decay is scheduled as for a critical drop.
        var view = owner.QueryMapInventory(Exterior)!;
        Assert.IsFalse(view.Holders.Single(h => h.Entity == Rat).Items.Any(i => i.Entity == Sword));
        Assert.AreEqual(new CellCoord(6, 5), view.Ground.Single(g => g.Item.Entity == Sword).Cell);
        Assert.AreEqual((Sword, Exterior), owner.QueryItemDecays().Select(d => (d.Entity, d.Map)).Single());

        // The drop is a map change: saved and restored, the sword still lies there.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(new CellCoord(6, 5), restored.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == Sword).Cell);
    }

    [TestMethod]
    public void Disarm_on_a_target_without_a_weapon_drops_nothing()
    {
        var record = Disarm();
        var (owner, caster, map) = Session(record, ratSword: false);
        using var _ = owner;
        // The host lists no items at all (nothing to take), then an item list without the weapon slot.
        Cast(owner, caster, record, Hero, Rat);
        Assert.AreEqual(SessionCriticalStepOutcome.Unknown, caster.TakeDamageSteps().Single().Criticals.Single().Outcome);
        map.Worn[Rat] = [];
        Cast(owner, caster, record, Hero, Rat);
        Assert.AreEqual(SessionCriticalStepOutcome.NoItem, caster.TakeDamageSteps().Single().Criticals.Single().Outcome);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Rat).Items.Any(i => i.Entity == Sword), "the sword stays in the rat's list");
    }

    [TestMethod]
    public void A_target_whose_step_the_game_stops_keeps_its_weapon()
    {
        // DH5/XR15: a nigh-invulnerable critter (critter flags 2, 0x200) takes no step (it returns before the weapon block), and a taking-damage script that cancels ends that
        // victim's step. (A save made against a damaging record still applies with a margin of at least 50, 0x4537B0: Disarm is no target a plain save skips.)
        var record = Disarm();
        var (owner, caster, _) = Session(record, nighInvulnerable: true);
        using var _ = owner;
        Cast(owner, caster, record, Hero, Rat);
        Assert.AreEqual(0, caster.TakeDamageSteps().Count);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.WeaponDropped));
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);

        var (o, c, _) = Session(record);
        using var __ = o;
        c.TakingDamage = (_, _) => false;
        Cast(o, c, record, Hero, Rat);
        Assert.AreEqual(0, c.TakeDamageSteps().Count);
        Assert.IsTrue(c.Log.Any(e => e.Kind == SpellCastEventKind.DamageVetoed && e.Object == Rat), string.Join(" | ", c.Log));
        Assert.IsFalse(c.Log.Any(e => e.Kind == SpellCastEventKind.WeaponDropped));
        Assert.AreEqual(0, o.QueryMapInventory(Exterior)!.Ground.Count);
    }

    [TestMethod]
    public void A_critter_already_dead_takes_no_weapon_block()
    {
        var record = Disarm();
        var (owner, caster, _) = Session(record);
        using var _ = owner;
        Assert.IsTrue(caster.ApplyScriptDamage(Rat, 1000, 0, unresistable: true).Applied, "the rat is dead");
        caster.TakeDamageSteps();
        Cast(owner, caster, record, Hero, Rat);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.WeaponDropped), string.Join(" | ", caster.Log));
        Assert.IsFalse(caster.TakeDamageSteps().Any(step => step.Criticals.Count > 0));
        Assert.IsFalse(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == Sword));
    }

    // An open map without bounds (as the critical-effects tests): a path is the straight line, critters stand on the listed cells.
    private sealed class OpenMap(params CellCoord[] critters) : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var rotations = new List<int>();
            for (var cell = from; cell != to; cell = Step(cell, rotations[^1])!.Value)
                rotations.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - cell.X), Math.Sign(to.Y - cell.Y))));
            return new(rotations.Count, 0, rotations);
        }
        public bool HasCritter(CellCoord cell) => critters.Contains(cell);
    }

    [TestMethod]
    public void With_the_hosts_drop_map_the_weapon_lands_where_the_drops_tries_lead_and_a_refused_drop_still_revises()
    {
        var record = Disarm();
        var (owner, caster, map) = Session(record);
        using var _ = owner;
        map.Drops = new OpenMap(new CellCoord(6, 5));
        Cast(owner, caster, record, Hero, Rat);
        var drop = caster.TakeDamageSteps().Single().Criticals.Single();
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, drop.Outcome);
        Assert.AreNotEqual(new CellCoord(6, 5), drop.Cell, "the rat's own tile has a critter: the item lands off it");
        Assert.IsTrue(Math.Abs(drop.Cell!.Value.X - 6) <= 4 && Math.Abs(drop.Cell.Value.Y - 5) <= 4, "within the drop's 2..4 tiles");
        Assert.AreEqual(drop.Cell, owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == Sword).Cell);

        // A refused drop (no-drop flag) draws its distance first: the draw is a revision of the session.
        var (o, c, m) = Session(record);
        using var __ = o;
        m.Drops = new OpenMap();
        m.Worn[Rat] = new() { [EquipmentSlot.Weapon] = new(Sword, Sturdy) { NoDrop = true } };
        var before = o.Version;
        Cast(o, c, record, Hero, Rat);
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, c.TakeDamageSteps().Single().Criticals.Single().Outcome);
        Assert.AreNotEqual(before, o.Version);
    }

    [TestMethod]
    public void A_no_drop_weapon_is_refused_and_a_spell_without_the_flag_drops_nothing()
    {
        var record = Disarm();
        var (owner, caster, map) = Session(record);
        using var _ = owner;
        map.Worn[Rat] = new() { [EquipmentSlot.Weapon] = new(Sword, Sturdy) { NoDrop = true } };
        Cast(owner, caster, record, Hero, Rat);
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, caster.TakeDamageSteps().Single().Criticals.Single().Outcome, "the drop's own no-drop test (0x466DA0)");
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);

        var plain = Disarm(amount: 3, flags: 0);
        var (o, c, m) = Session(plain);
        using var __ = o;
        Cast(o, c, plain, Hero, Rat);
        var step = c.TakeDamageSteps().Single();
        Assert.IsTrue(step.Damage.HitPointDamage > 0 && step.Criticals.Count == 0, "no flag, no weapon block");
        Assert.AreEqual(0, o.QueryMapInventory(Exterior)!.Ground.Count);
        Assert.IsFalse(c.Log.Any(e => e.Kind == SpellCastEventKind.WeaponDropped));
    }

    [TestMethod]
    public void Another_spell_with_the_flag_and_damage_hurts_and_disarms()
    {
        // AC4: the weapon block does not depend on the amount: any Damage component with the flag takes the same path.
        var record = Disarm(amount: 3);
        var (owner, caster, _) = Session(record);
        using var _ = owner;
        Cast(owner, caster, record, Hero, Rat);
        var step = caster.TakeDamageSteps().Single();
        Assert.IsTrue(step.Damage.HitPointDamage > 0 && step.Life.HitPointDamage > 0);
        Assert.AreEqual((Sword, SessionCriticalStepOutcome.Dropped), (step.Criticals.Single().Entity, step.Criticals.Single().Outcome));
        Assert.IsTrue(owner.QueryMapInventory(Exterior)!.Ground.Any(g => g.Item.Entity == Sword));
    }

    [TestMethod]
    public void An_npcs_disarm_takes_the_heros_weapon_to_its_cell_and_the_hero_is_bare_handed_after_a_restore()
    {
        // XR15: a spell's step has no critical procedure, so the attack rule "not when an NPC hits a PC" does not apply.
        var record = Disarm();
        var (owner, caster, _) = Session(record, ratSword: false, heroBlade: true);
        using var _ = owner;
        var kit = owner.QueryHeroKit().Select(item => item.Entity).ToArray();
        Cast(owner, caster, record, Thug, Hero);
        var drop = caster.TakeDamageSteps().Single().Criticals.Single();
        Assert.AreEqual((DamageRecipient.Target, "kit:1", SessionCriticalStepOutcome.Dropped, (CellCoord?)Start.Cell), (drop.Whose, drop.Entity, drop.Outcome, drop.Cell), "at the hero's own cell");
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "no longer wielded");
        Assert.IsFalse(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"));
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("kit:1", SessionPlacedSource.Kit, Exterior, Start.Cell), (placed.Entity, placed.Source, placed.Map, placed.Cell));
        Assert.AreEqual("kit:1", owner.QueryItemDecays().Single().Entity);

        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(placed, restored.QueryPlacedItems().Single());
        Assert.AreEqual(0, restored.QueryEquipment().Worn.Count);
        // Picked up again it returns to the kit at its number's place.
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "kit:1", Accept).Disposition);
        CollectionAssert.AreEqual(kit, restored.QueryHeroKit().Select(item => item.Entity).ToArray());
    }
}
