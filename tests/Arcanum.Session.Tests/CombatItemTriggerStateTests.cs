using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-023: the item events of the hero's damage step on an NPC (contract docs/world/item-spell-triggers.md, ledger IC rows).
// The hero wields an invented blade (kit:1, reach 1); a golem wears a helmet, a sword and a vest from its map seed. A recording host
// shows the executor calls; one test runs them through the cast host. Values, records and items are invented.
[TestClass]
public sealed class CombatItemTriggerStateTests
{
    private const string Golem = "golem", Blade = "kit:1", Hero = SessionSpellCaster.Hero;
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static readonly Dictionary<EquipmentSlot, SessionWearItem> GolemItems = new()
    {
        [EquipmentSlot.Armor] = new("golem-vest", Sturdy),
        [EquipmentSlot.Helmet] = new("golem-helmet", Sturdy),
        [EquipmentSlot.Weapon] = new("golem-sword", Sturdy),
    };

    // Records each call and, with a probe, whether the holder still held the item when the call came.
    private sealed class Recorder(Func<string, string, bool>? holds = null) : ISessionItemTriggers
    {
        public List<(uint Mask, string Item, string Holder, string? Other)> Calls { get; } = [];
        public List<bool> Held { get; } = [];
        public bool Supports(string item) => true;

        public List<(SpellLocation Tile, string? RunOther)> Contexts { get; } = [];

        public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default,
            string? runOther = null)
        {
            Calls.Add((eventMask, item, holder, other));
            Contexts.Add((tile, runOther));
            if (holds is not null) Held.Add(holds(item, holder));
            return SessionItemTriggerResult.None;
        }

        public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder) => SessionItemTriggerResult.None;
        public SessionItemTriggerResult Remove(string item, string holder) => SessionItemTriggerResult.None;
    }

    private static EquipmentItem Weapon(int criticalChance) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
            MagicCriticalHitChance = criticalChance, MagicCriticalHitEffect = criticalChance,
        },
    };

    private static SessionCritterCombat GolemCombat(int hpAdjustment = 5000)
    {
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: hpAdjustment, critterFlags: 0);
        return SessionCritterCombat.From(Golem, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }).Combat!;
    }

    private static SessionMapInventorySeed Seed() => new(
        [new(Golem, SessionHolderKind.Npc, false, false, [], [.. GolemItems.Values.Select(item => item.Entity)])],
        [.. GolemItems.Select(pair => new SessionSeedItem(pair.Value.Entity,
            InventoryStateTests.Facts(pair.Key == EquipmentSlot.Weapon ? SessionItemType.Weapon : SessionItemType.Armor, 2900 + (int)pair.Key - 1000,
                (int)pair.Key), null))]);

    private static GameSessionOwner Owner(int criticalChance = 0)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Blade, EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Weapon(criticalChance)),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> Worn(GameSessionOwner owner) =>
        GolemItems.Where(pair => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Golem).Items.Any(i => i.Entity == pair.Value.Entity))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static string[] HeroItems(GameSessionOwner owner) => [.. owner.QueryEquipment().Worn.OrderBy(w => w.Slot).Select(w => w.Entity)];

    // The calls the step makes for the golem's and the hero's items when nothing changes them.
    private static (uint, string, string, string?)[] HitAndDamages(GameSessionOwner owner) =>
    [
        (ParentHit | WeaponMelee, "golem-helmet", Golem, Hero), (ParentHit | WeaponMelee, "golem-sword", Golem, Hero),
        (ParentHit | WeaponMelee, "golem-vest", Golem, Hero),
        .. HeroItems(owner).SelectMany(item => item == Blade
            ? new (uint, string, string, string?)[] { (ParentDamagesOpponent, item, Hero, Golem), (TargetHit, item, Hero, Golem), (ParentDamagesOpponentWithItem, item, Hero, Golem) }
            : [(ParentDamagesOpponent, item, Hero, Golem), (TargetHit, item, Hero, Golem)]),
    ];

    [TestMethod]
    public void A_hit_fires_the_golem_s_parent_hit_then_the_hero_s_damages_opponent_in_slot_order()
    {
        using var owner = Owner();
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var host = new Recorder();
            var result = owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner), ItemTriggers = host });
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            if (result.Life is null)
            {
                Assert.AreEqual(0, host.Calls.Count, "a miss fires nothing");
                continue;
            }
            if (result.Recipient != DamageRecipient.Target || result.Resolution!.Damage!.HitEffects != CriticalHitEffects.None) continue;
            CollectionAssert.AreEqual(HitAndDamages(owner), host.Calls.ToArray());
            CollectionAssert.AreEqual(host.Calls.ToArray(), result.ItemSpells.Select(f => (f.Mask, f.Item, f.Holder, f.Other)).ToArray());
            Assert.AreEqual(ItemTriggerEvent.ParentHit, result.ItemSpells[0].Event);

            // Without a host nothing fires; without the golem's item data its items fire nothing, the hero's still do.
            Assert.AreEqual(0, owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner) }).ItemSpells.Count);
            for (var next = 0; next < 200; next++)
            {
                var blind = new Recorder();
                var unknown = owner.AttackCritter(owner.Version, new(golem) { ItemTriggers = blind });
                if (unknown.Recipient != DamageRecipient.Target) continue;
                Assert.IsTrue(blind.Calls.Count > 0 && blind.Calls.All(call => call.Holder == Hero), "only the hero's items walk");
                return;
            }
            Assert.Fail("no second hit within the bounded run");
        }
        Assert.Fail("no plain hit within the bounded run");
    }

    [TestMethod]
    public void A_kill_fires_dying_with_the_hero_before_the_parent_hit()
    {
        using var owner = Owner();
        var golem = GolemCombat(hpAdjustment: -3);
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var host = new Recorder();
            var result = owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner), ItemTriggers = host });
            if (result.Life is not { } life || !life.Events.HasFlag(LifeEvents.Died)) continue;
            var dying = host.Calls.TakeWhile(call => call.Mask != (ParentHit | WeaponMelee)).Where(call => call.Mask == ParentDying).ToArray();
            CollectionAssert.AreEqual(new[] { "golem-helmet", "golem-sword", "golem-vest" }, dying.Select(call => call.Item).ToArray());
            Assert.IsTrue(dying.All(call => call.Holder == Golem && call.Other == Hero));
            Assert.AreEqual(3, host.Calls.Count(call => call.Mask == (ParentHit | WeaponMelee)), "the dead golem's items still take the parent hit");
            // An attack on the dead golem stops before any damage step: nothing fires.
            var after = new Recorder();
            owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner), ItemTriggers = after });
            Assert.AreEqual(0, after.Calls.Count);
            return;
        }
        Assert.Fail("no kill within the bounded run");
    }

    [TestMethod]
    public void A_critical_s_dropped_sword_takes_the_stun_but_not_the_later_parent_hit()
    {
        using var owner = Owner(criticalChance: 100);
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var host = new Recorder((item, holder) => holder != Golem || Worn(owner).Values.Any(worn => worn.Entity == item));
            var result = owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner), TargetCell = new(101, 200), ItemTriggers = host });
            Assert.AreEqual(GameSessionFailure.None, result.StepFailure);
            if (!result.Criticals.Any(step => step is { Kind: SessionCriticalStepKind.DropWeapon, Outcome: SessionCriticalStepOutcome.Dropped }))
            {
                Assert.AreEqual(GolemItems.Count, Worn(owner).Count, "nothing dropped yet");
                continue;
            }
            var effects = result.Resolution!.Damage!.HitEffects;
            var down = (effects & (CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout)) != 0;
            Assert.IsTrue(down || effects.HasFlag(CriticalHitEffects.Stun), $"the seeded run's drop comes with a stun or a knockdown: {effects}");
            var hit = host.Calls.Where(call => call.Mask == (ParentHit | WeaponMelee)).Select(call => call.Item).ToArray();
            CollectionAssert.AreEqual(new[] { "golem-helmet", "golem-vest" }, hit, "the drop came before the parent hit");
            Assert.IsTrue(host.Held.All(held => held), "every walk named items the golem held at its call: the stun's and knockdown's before the drop");
            if (!down)
                CollectionAssert.AreEqual(new[] { "golem-helmet", "golem-sword", "golem-vest" },
                    host.Calls.Where(call => call.Mask == ParentStunned).Select(call => call.Item).ToArray(), "the stun came before it");
            else
            {
                CollectionAssert.AreEqual(new[] { "golem-helmet", "golem-sword", "golem-vest" },
                    host.Calls.Where(call => call.Mask == ParentGoingUnconscious && call.Other == Hero).Select(call => call.Item).ToArray());
                Assert.IsTrue(host.Calls.Any(call => call is { Mask: TargetGoingUnconscious, Item: Blade, Holder: Hero, Other: Golem }));
            }
            return;
        }
        Assert.Fail("no dropped sword within the bounded run");
    }

    // A bow (kit:1, range 10) with arrows picked up from the ground, the golem's items in the same seed.
    private static GameSessionOwner BowOwner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = Seed();
        seed = new(seed.Holders, [.. seed.Items,
            new SessionSeedItem("arrows", new(new(SessionItemType.Ammo, 7058, 100, 200), 1, 0, 0, 40, []) { AmmoType = 0 }, new Arcanum.Simulation.Movement.CellCoord(101, 200))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "arrows", Accept).Disposition);
        var bow = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Bow, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 10, Range = 10, AmmoType = 0, AmmoConsumption = 1 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Blade, EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, bow), (_, _) => true, Accept).Failure);
        return owner;
    }

    [TestMethod]
    public void A_landed_arrow_fires_without_the_melee_bit_and_the_bow_takes_its_own_mask()
    {
        using var owner = BowOwner();
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var shot = owner.AttackCritter(owner.Version, new(golem) { Distance = 3, Flight = true, TargetWorn = Worn(owner), ItemTriggers = new Recorder() });
            Assert.AreEqual(GameSessionFailure.None, shot.Failure);
            Assert.AreEqual(0, shot.ItemSpells.Count, "the flight carries no damage step");
            if (shot.Resolution!.Outcome is not AttackOutcome.Hit || shot.Shots.Count != 1)
            {
                foreach (var flying in shot.Shots) owner.EndShot(flying);
                continue;
            }
            var host = new Recorder();
            var landed = owner.LandShot(owner.Version, shot.Shot!.Value, new(golem) { TargetWorn = Worn(owner), ItemTriggers = host });
            Assert.AreEqual(GameSessionFailure.None, landed.Failure);
            CollectionAssert.AreEqual(new[] { "golem-helmet", "golem-sword", "golem-vest" },
                host.Calls.Where(call => call.Mask == ParentHit).Select(call => call.Item).ToArray(), "a reach of 10: no melee bit");
            Assert.IsFalse(host.Calls.Any(call => (call.Mask & WeaponMelee) != 0));
            Assert.IsTrue(host.Calls.Any(call => call is { Mask: ParentDamagesOpponentWithItem, Item: Blade, Holder: Hero, Other: Golem }),
                "the projectile's item is the worn bow");
            return;
        }
        Assert.Fail("no single hit within the bounded run");
    }

    // ARC-MAGIC-025 (IC10): every end of the arrow's flight fires the bow's attacks a location at that tile.
    [TestMethod]
    public void A_shot_s_end_names_the_projectile_s_item_at_its_tile_while_it_flies()
    {
        using var owner = BowOwner();
        var golem = GolemCombat();
        var shot = owner.AttackCritter(owner.Version, new(golem) { Distance = 3, Flight = true, TargetWorn = Worn(owner) });
        Assert.AreEqual(GameSessionFailure.None, shot.Failure);
        var flying = shot.Shots[0];
        var tile = new Arcanum.Simulation.Movement.CellCoord(104, 200);
        var events = owner.QueryShotEndItemEvents(flying, tile);
        CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentAttacksLocation, Hero, null) { Item = Blade, Tile = new(104, 200) } },
            events.ToArray());
        var host = new Recorder();
        owner.FireItemEvents(host, events, _ => null);
        CollectionAssert.AreEqual(new (uint, string, string, string?)[] { (ParentAttacksLocation, Blade, Hero, null) }, host.Calls.ToArray());
        CollectionAssert.AreEqual(new[] { (new SpellLocation(104, 200), (string?)Hero) }, host.Contexts.ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryShotEndItemEvents(flying, new(-1, 0)));
        foreach (var each in shot.Shots) owner.EndShot(each);
        Assert.AreEqual(0, owner.QueryShotEndItemEvents(flying, tile).Count, "a shot no longer in flight");
    }

    // ARC-COMBAT-1719 (0x4B2870, PF9, PS5): the weapon's scripts at the end of a flight run for the hero as the shooter and the bow as the parent item, with the shot's target as the extra object.
    [TestMethod]
    public void A_shot_s_end_names_the_shooter_the_weapon_and_the_target_for_the_weapon_s_scripts()
    {
        using var owner = BowOwner();
        var golem = GolemCombat();
        var shot = owner.AttackCritter(owner.Version, new(golem) { Distance = 3, Flight = true, TargetWorn = Worn(owner) });
        Assert.AreEqual(GameSessionFailure.None, shot.Failure);
        var flying = shot.Shots[0];
        var ends = owner.QueryShotEndScripts(flying)!.Value;
        Assert.AreEqual((Hero, Blade, false), (ends.Attacker, ends.Weapon, ends.Thrown), "the hero shoots with the worn bow, which stays in the hand");
        Assert.AreEqual(shot.Resolution!.Outcome is AttackOutcome.CriticalHit or AttackOutcome.CriticalMiss, ends.Critical, "the critical flag is the decided check's");
        Assert.IsTrue(ends.IsWeapon, "ARC-COMBAT-1766: the wielded bow is a weapon (the critical points ask it)");
        foreach (var each in shot.Shots) owner.EndShot(each);
        Assert.IsNull(owner.QueryShotEndScripts(flying), "a shot no longer in flight runs nothing");
    }

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            id == Hero ? new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
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

    // Record 170 fires on damages opponent with the item and deals its holder 2 (it aims at the parent: no aim bit).
    private static SessionSpellRecord Record170() =>
        SessionSpellRecords.Translate(Record(number: 170, beginTargets: 0x4, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, 0, new DamageData(0, 2, 2, 0))])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(0x4, 0, 0, 0, -1), 5)], Cost = 0, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0,
            DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0,
            DisallowedTargetCritterFlags = 0, ItemTriggers = ParentDamagesOpponentWithItem });

    [TestMethod]
    public void Through_the_cast_host_the_blade_s_spell_runs_from_the_blade_at_its_holder()
    {
        using var owner = Owner();
        var record = Record170();
        var caster = new SessionSpellCaster(owner, n => n == 170 ? record : null, new Map(owner), new Events(),
            id => id == Blade ? new SessionItemSpells(Blade, Hero, 5, [170, 10000, 10000, 10000, 10000], -1, 0) : null);
        var host = new SessionItemTriggers(caster, new NoCombat());
        var golem = GolemCombat();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var hp = owner.QueryHeroLife()!.Status.HitPoints;
            var result = owner.AttackCritter(owner.Version, new(golem) { TargetWorn = Worn(owner), ItemTriggers = host });
            if (result.Recipient != DamageRecipient.Target) continue;
            var started = result.ItemSpells.Where(f => f.Result.Spells.Count > 0).ToArray();
            Assert.AreEqual((ItemTriggerEvent.ParentDamagesOpponent, ParentDamagesOpponentWithItem, Blade), (started.Single().Event, started[0].Mask, started[0].Item));
            var spell = started[0].Result.Spells.Single();
            Assert.AreEqual((SessionItemSpellOutcome.Started, ItemSpellAim.Parent), (spell.Outcome, spell.Firing.Aim));
            Assert.AreEqual(SpellCastGoalOutcome.Started, started[0].Result.Goals.Single()!.Outcome);
            Assert.AreEqual(hp - 2, owner.QueryHeroLife()!.Status.HitPoints, "the blade's run hurt its holder");
            Assert.AreEqual(result.Version, owner.Version, "the result's version follows the runs");
            return;
        }
        Assert.Fail("no hit within the bounded run");
    }
}
