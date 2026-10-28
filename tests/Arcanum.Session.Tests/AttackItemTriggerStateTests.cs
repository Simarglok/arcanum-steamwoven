using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-025: the attack's other item events (contract docs/world/item-spell-triggers.md, ledger IC6, IC10, MG46): the attack
// start's attacks an opponent, attacks a location's one item, an item parent's own target hit, the attacker global as the run's
// other object, the MG46 chain and a critical self-hit's life events. The hero wields an invented blade (kit:1) and wears an
// invented vest (kit:2); a golem wears two items. Values and records are invented.
[TestClass]
public sealed class AttackItemTriggerStateTests
{
    private const string Golem = "golem", Blade = "kit:1", Vest = "kit:2", Grenade = "grenade", Hero = SessionSpellCaster.Hero;
    private static readonly string[] GolemWorn = ["golem-helmet", "golem-vest"];
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class Recorder : ISessionItemTriggers
    {
        public List<(uint Mask, string Item, string Holder, string? Other, SpellLocation Tile, string? RunOther)> Calls { get; } = [];
        public bool Supports(string item) => true;

        public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null, SpellLocation tile = default,
            string? runOther = null)
        {
            Calls.Add((eventMask, item, holder, other, tile, runOther));
            return SessionItemTriggerResult.None;
        }

        public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder) => SessionItemTriggerResult.None;
        public SessionItemTriggerResult Remove(string item, string holder) => SessionItemTriggerResult.None;
    }

    private static EquipmentItem BladeItem(int fatigue = 0) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2), Fatigue = new(fatigue, fatigue) }, SpeedFactor = 15,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
        },
    };

    private static EquipmentItem VestItem() => new()
    {
        Kind = EquipmentItemKind.Armor,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium },
    };

    private static GameSessionOwner Owner(bool vest = true, int fatigue = 0)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Blade, EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, BladeItem(fatigue)),
            (_, _) => true, Accept).Failure);
        if (vest)
            Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Vest, EquipmentSlot.Armor, new(Kit[1].Kind, [], false, VestItem()),
                (_, _) => true, Accept).Failure);
        return owner;
    }

    private static IReadOnlyList<string>? NpcWorn(string id) => id == Golem ? GolemWorn : null;

    [TestMethod]
    public void An_attack_start_walks_the_attacker_s_items_with_the_target_and_only_the_first_carries_the_attacker()
    {
        using var owner = Owner();
        var events = owner.QueryAttackStartItemEvents(Hero, Golem);
        CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentAttacksOpponent, Hero, Golem) }, events.ToArray());
        var host = new Recorder();
        Assert.AreEqual(GameSessionFailure.None, owner.FireItemEvents(host, events, NpcWorn).Failure);
        CollectionAssert.AreEqual(new (uint, string, string, string?, SpellLocation, string?)[]
        {
            (ParentAttacksOpponent, Blade, Hero, Golem, SpellLocation.None, Hero), (ParentAttacksOpponent, Vest, Hero, Golem, SpellLocation.None, null),
        }, host.Calls.ToArray(), "slot order: the weapon (1004), then the armour (1006)");

        // An NPC's swing walks its items as the host knows them.
        var npc = new Recorder();
        owner.FireItemEvents(npc, owner.QueryAttackStartItemEvents(Golem, Hero), NpcWorn);
        CollectionAssert.AreEqual(new (uint, string, string, string?, SpellLocation, string?)[]
        {
            (ParentAttacksOpponent, "golem-helmet", Golem, Hero, SpellLocation.None, Golem), (ParentAttacksOpponent, "golem-vest", Golem, Hero, SpellLocation.None, null),
        }, npc.Calls.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryAttackStartItemEvents("", Golem));
    }

    [TestMethod]
    public void Attacks_a_location_fires_its_one_item_at_the_tile_with_the_attacker_and_no_other_object()
    {
        using var owner = Owner();
        var host = new Recorder();
        var location = new SessionItemEvent(ItemTriggerEvent.ParentAttacksLocation, Hero, null) { Item = Grenade, Tile = new(7, 9) };
        owner.FireItemEvents(host, [location, new SessionItemEvent(ItemTriggerEvent.ParentAttacksLocation, Hero, null)], NpcWorn);
        CollectionAssert.AreEqual(new (uint, string, string, string?, SpellLocation, string?)[] { (ParentAttacksLocation, Grenade, Hero, null, new(7, 9), Hero) },
            host.Calls.ToArray(), "no walk over the worn items; no item, no call");
        Assert.ThrowsExactly<ArgumentException>(() => owner.FireItemEvents(host, [location with { Item = "" }], NpcWorn));
    }

    [TestMethod]
    public void An_item_parent_fires_its_own_target_hit_with_the_victim_as_holder_and_nothing_else()
    {
        using var owner = Owner();
        var host = new Recorder();
        owner.FireItemEvents(host,
        [
            new SessionItemEvent(ItemTriggerEvent.ParentHit, Golem, Grenade), new SessionItemEvent(ItemTriggerEvent.ParentDamagesOpponent, Grenade, Golem),
            new SessionItemEvent(ItemTriggerEvent.ParentGoingUnconscious, Grenade, null),
        ], NpcWorn, id => id == Grenade);
        CollectionAssert.AreEqual(new (uint, string, string, string?, SpellLocation, string?)[]
        {
            (ParentHit, "golem-helmet", Golem, Grenade, SpellLocation.None, null), (ParentHit, "golem-vest", Golem, Grenade, SpellLocation.None, null),
            (TargetHit, Grenade, Golem, Grenade, SpellLocation.None, null),
        }, host.Calls.ToArray());
    }

    [TestMethod]
    public void MG46_Steamwoven_skips_the_items_of_an_event_s_chain_and_the_original_fires_them()
    {
        using var owner = Owner();
        var hit = new SessionItemEvent(ItemTriggerEvent.ParentHit, Golem, Hero) { Chain = ["golem-vest"] };
        var host = new Recorder();
        owner.FireItemEvents(host, [hit], NpcWorn);
        CollectionAssert.AreEqual(new[] { "golem-helmet" }, host.Calls.Select(call => call.Item).ToArray());
        owner.SelectRules(RuleMode.Original);
        var original = new Recorder();
        owner.FireItemEvents(original, [hit], NpcWorn);
        CollectionAssert.AreEqual(GolemWorn, original.Calls.Select(call => call.Item).ToArray());
    }

    [TestMethod]
    public void A_critical_self_hit_returns_its_life_step_s_item_events()
    {
        // The blade also tires: a self-hit that empties the hero's last fatigue fires its going unconscious (IC4).
        using var owner = Owner(vest: false, fatigue: 50);
        var tough = SessionCritterCombat.From(Golem, AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;
        SessionAttackResult? self = null;
        var knocked = false;
        for (var attempt = 0; attempt < 2000 && self is null; attempt++)
        {
            var down = owner.QueryHeroLife()!.Status.IsUnconscious;
            var result = owner.AttackCritter(owner.Version, new(tough));
            Assert.AreEqual(GameSessionFailure.None, result.Failure);
            if (result.Recipient == DamageRecipient.Target) Assert.AreEqual(0, result.ItemEvents.Count, "the target's damage step fires its own");
            if (result.Recipient != DamageRecipient.Attacker) continue;
            self = result;
            knocked = !down && owner.QueryHeroLife()!.Status.IsUnconscious;
        }
        Assert.IsNotNull(self, "a critical self-hit within the bounded run");
        Assert.IsTrue(knocked, "the blade's fatigue empties the hero's");
        CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentGoingUnconscious, Hero, null) }, self!.ItemEvents.ToArray());
    }

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Grenade => new(id, 14, new(6, 6)),
            _ => null,
        };
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

    // An invented record firing on <paramref name="triggers"/> that deals its parent 0 (no aim bit: at the parent); its BEGIN area
    // takes the parent, or with <paramref name="tile"/> the target tile (high bit 0x8000).
    private static SessionSpellRecord Hurting(int number, uint triggers, bool tile = false) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: tile ? 0x8000UL << 32 : 0x4, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, 0, new DamageData(0, 0, 0, 0))])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(tile ? 0x8000UL << 32 : 0x4, 0, 0, 0, -1), 5)], Cost = 0, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0,
            DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0,
            DisallowedTargetCritterFlags = 0, ItemTriggers = triggers });

    private static SessionSpellCaster Caster(GameSessionOwner owner, params (string Item, SessionSpellRecord Record)[] items)
    {
        var records = items.ToDictionary(pair => pair.Record.Data.Number, pair => pair.Record);
        var caster = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner), new Events(), id => items.FirstOrDefault(pair => pair.Item == id) is
            { Item: not null } pair ? new SessionItemSpells(id, id == Grenade ? Grenade : Hero, id == Grenade ? 14 : 5, [pair.Record.Data.Number, 10000, 10000, 10000, 10000], -1, 0)
            : null);
        caster.ItemTriggers = new SessionItemTriggers(caster, new NoCombat());
        return caster;
    }

    private static int Started(IEnumerable<SessionItemTriggerFiring> firings) =>
        firings.Sum(f => f.Result.Spells.Count(spell => spell.Outcome == SessionItemSpellOutcome.Started));

    [TestMethod]
    public void MG46_through_the_cast_host_an_answering_item_still_fires_once_and_the_chain_ends()
    {
        // The blade answers its holder's damages an opponent and the vest its parent hit, each by hurting the holder: in the original
        // each damage step fires the other again until the technical bound; Steamwoven fires each once (the narrow fix).
        using var owner = Owner();
        var caster = Caster(owner, (Blade, Hurting(171, ParentDamagesOpponent)), (Vest, Hurting(172, ParentHit)));
        var start = new SessionItemEvent(ItemTriggerEvent.ParentDamagesOpponent, Hero, Hero);
        var steam = caster.FireItemEvents([start]);
        Assert.AreEqual(2, Started(steam), "the blade's run, then the vest's answer to its damage");
        CollectionAssert.AreEqual(new[] { Blade, Vest }, steam.Where(f => f.Result.Spells.Count > 0).Select(f => f.Item).ToArray());
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ItemEventsDropped));

        owner.SelectRules(RuleMode.Original);
        var original = caster.FireItemEvents([start]);
        Assert.IsTrue(Started(original) > 2);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ItemEventsDropped), "the chain ends only at the bound");
    }

    [TestMethod]
    public void A_thrown_item_s_location_run_has_the_item_as_parent_and_the_attacker_as_other_object()
    {
        using var owner = Owner();
        var caster = Caster(owner, (Grenade, Hurting(173, ParentAttacksLocation, tile: true)));
        var fired = caster.FireItemEvents([new SessionItemEvent(ItemTriggerEvent.ParentAttacksLocation, Hero, null) { Item = Grenade, Tile = new(6, 6) }]);
        var spell = fired.Single().Result.Spells.Single();
        Assert.AreEqual((SessionItemSpellOutcome.Started, ItemSpellAim.Location), (spell.Outcome, spell.Firing.Aim));
        var run = spell.Result!.Creation!.Run!;
        Assert.AreEqual((Grenade, Grenade, Hero, new SpellLocation(6, 6)), (run.Source!.Id, run.Parent!.Id, run.Other!.Id, run.TargetTile),
            "0x450A50: an item on the map is its own parent; the global is the run's other object");

        // The attack start's first worn item carries the attacker too; its run's parent is the holder.
        var started = Caster(owner, (Blade, Hurting(174, ParentAttacksOpponent))).FireItemEvents(owner.QueryAttackStartItemEvents(Hero, Golem));
        var swing = started.Single(f => f.Result.Spells.Count > 0).Result.Spells.Single().Result!.Creation!.Run!;
        Assert.AreEqual((Hero, Hero), (swing.Parent!.Id, swing.Other!.Id));
    }
}
