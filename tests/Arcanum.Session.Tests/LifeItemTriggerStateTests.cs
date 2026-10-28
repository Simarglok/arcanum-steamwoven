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

// ARC-MAGIC-024: the item events of life steps outside the hero's attack (contract docs/world/item-spell-triggers.md, ledger IC4,
// IC5, DH1 and MG46): the fatigue setter's going unconscious, the dying animation's end, a spell's damage step and its firing after
// the run's pass. The hero wields an invented blade (kit:1); a golem is a life target of its own facts. Values and records are invented.
[TestClass]
public sealed class LifeItemTriggerStateTests
{
    private const string Golem = "golem", Blade = "kit:1", Hero = SessionSpellCaster.Hero;
    private static readonly string[] GolemWorn = ["golem-helmet", "golem-vest"];
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionLifeTarget GolemTarget = SessionLifeTarget.Critter(Golem, new LifeFacts { HitPointPoints = 500, FatiguePoints = 500 },
        LifeState.Unharmed);

    private sealed class Recorder : ISessionItemTriggers
    {
        public List<(uint Mask, string Item, string Holder, string? Other)> Calls { get; } = [];
        public bool Supports(string item) => true;

        public SessionItemTriggerResult Fire(uint eventMask, string item, string holder, string? other = null)
        {
            Calls.Add((eventMask, item, holder, other));
            return SessionItemTriggerResult.None;
        }

        public SessionItemTriggerResult Withdraw(uint eventMask, string item, string holder) => SessionItemTriggerResult.None;
        public SessionItemTriggerResult Remove(string item, string holder) => SessionItemTriggerResult.None;
    }

    private static EquipmentItem BladeItem() => new()
    {
        Kind = EquipmentItemKind.Weapon,
        HitPoints = 1000,
        MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon
        {
            Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15,
            CriticalHitChart = CriticalHitChart.Cutting, CriticalMissChart = CriticalMissChart.Bladed,
        },
    };

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Blade, EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, BladeItem()),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    private static IReadOnlyList<string>? NpcWorn(string id) => id == Golem ? GolemWorn : null;

    [TestMethod]
    public void A_payment_that_empties_the_fatigue_calls_for_going_unconscious_with_no_other_object_over_the_hero_s_items()
    {
        using var owner = Owner();
        var spent = owner.PaySpellFatigue(owner.Version, SessionLifeTarget.Hero, 1000);
        Assert.AreEqual(GameSessionDisposition.Changed, spent.Disposition);
        CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentGoingUnconscious, Hero, null) }, spent.ItemEvents.ToArray());
        Assert.AreEqual(0, owner.PaySpellFatigue(owner.Version, SessionLifeTarget.Hero, 5).ItemEvents.Count, "already unconscious");
        Assert.AreEqual(0, owner.PaySpellFatigue(owner.Version, SessionLifeTarget.Hero, 0).ItemEvents.Count, "a refusal calls for nothing");

        var host = new Recorder();
        var fired = owner.FireItemEvents(host, spent.ItemEvents, NpcWorn);
        Assert.AreEqual(GameSessionFailure.None, fired.Failure);
        CollectionAssert.AreEqual(new[] { (ParentGoingUnconscious, Blade, Hero, (string?)null) }, host.Calls.ToArray());
        Assert.AreEqual((ItemTriggerEvent.ParentGoingUnconscious, Blade), (fired.Firings.Single().Event, fired.Firings[0].Item));
    }

    [TestMethod]
    public void A_kill_calls_for_nothing_until_the_dying_animation_ends()
    {
        using var owner = Owner();
        Assert.AreEqual(0, owner.QueryDyingEndItemEvents(GolemTarget).Count, "alive");
        var killed = owner.Kill(owner.Version, GolemTarget);
        Assert.IsTrue(killed.Outcome!.Events.HasFlag(LifeEvents.Died));
        Assert.AreEqual(0, killed.ItemEvents.Count, "a kill without a killer fires no dying at once");
        var end = owner.QueryDyingEndItemEvents(GolemTarget);
        CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentDying, Golem, null) }, end.ToArray());

        var host = new Recorder();
        owner.FireItemEvents(host, end, NpcWorn);
        CollectionAssert.AreEqual(GolemWorn.Select(item => (ParentDying, item, Golem, (string?)null)).ToArray(), host.Calls.ToArray());
        // An NPC the host does not know walks nothing (IC1: the parent must be a PC or NPC).
        var blind = new Recorder();
        owner.FireItemEvents(blind, end, _ => null);
        Assert.AreEqual(0, blind.Calls.Count);
    }

    [TestMethod]
    public void A_spell_damage_step_calls_for_the_hooks_with_its_parties_and_the_setting_decides_an_item_run_s()
    {
        using var owner = Owner();
        var hit = new LifeDamage { HitPoints = 1 };
        var cast = owner.ApplyDamage(owner.Version, GolemTarget, hit, new(Hero, Hero));
        CollectionAssert.AreEqual(new[]
        {
            new SessionItemEvent(ItemTriggerEvent.ParentHit, Golem, Hero), new SessionItemEvent(ItemTriggerEvent.ParentDamagesOpponent, Hero, Golem),
        }, cast.ItemEvents.ToArray());
        Assert.AreEqual(0, owner.ApplyDamage(owner.Version, GolemTarget, hit).ItemEvents.Count, "without parties only the fatigue setter's");
        using (var other = Owner())
            CollectionAssert.AreEqual(new[] { new SessionItemEvent(ItemTriggerEvent.ParentGoingUnconscious, Golem, null) },
                other.ApplyDamage(other.Version, GolemTarget, new LifeDamage { Fatigue = 10_000 }).ItemEvents.ToArray(), "the setter's needs no parties");
        Assert.AreEqual(0, owner.ApplyDamage(owner.Version, GolemTarget, hit, new(Hero, Hero, ItemRun: true)).ItemEvents.Count, "MG46 fixed");
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.ApplyDamage(owner.Version, GolemTarget, hit, new("", Hero)).Disposition);
        owner.SelectRules(RuleMode.Original);
        Assert.AreEqual(2, owner.ApplyDamage(owner.Version, GolemTarget, hit, new(Hero, Hero, ItemRun: true)).ItemEvents.Count, "MG46 kept");

        // The walks: the golem's items with the hero, then the hero's blade with the golem; no melee bit and no weapon (DH1).
        var host = new Recorder();
        owner.FireItemEvents(host, cast.ItemEvents, NpcWorn);
        CollectionAssert.AreEqual(new (uint, string, string, string?)[]
        {
            (ParentHit, "golem-helmet", Golem, Hero), (ParentHit, "golem-vest", Golem, Hero),
            (ParentDamagesOpponent, Blade, Hero, Golem), (TargetHit, Blade, Hero, Golem),
        }, host.Calls.ToArray());

        // A kill credits the damage source: the run's other object.
        var dying = owner.ApplyDamage(owner.Version, GolemTarget, new LifeDamage { HitPoints = 10_000 }, new(Hero, "imp"));
        Assert.AreEqual(new SessionItemEvent(ItemTriggerEvent.ParentDying, Golem, "imp"), dying.ItemEvents[0]);
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

    // Record 171 fires on damages opponent and deals its holder 0..0 (it aims at the parent: no aim bit), so its own damage step
    // calls for damages opponent again.
    private static SessionSpellRecord Record171() =>
        SessionSpellRecords.Translate(Record(number: 171, beginTargets: 0x4, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, 0, new DamageData(0, 0, 0, 0))])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(0x4, 0, 0, 0, -1), 5)], Cost = 0, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0,
            DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0,
            DisallowedTargetCritterFlags = 0, ItemTriggers = ParentDamagesOpponent });

    private static SessionSpellCaster Caster(GameSessionOwner owner)
    {
        var record = Record171();
        var caster = new SessionSpellCaster(owner, n => n == 171 ? record : null, new Map(owner), new Events(),
            id => id == Blade ? new SessionItemSpells(Blade, Hero, 5, [171, 10000, 10000, 10000, 10000], -1, 0) : null);
        caster.ItemTriggers = new SessionItemTriggers(caster, new NoCombat());
        return caster;
    }

    private static int Started(IEnumerable<SessionItemTriggerFiring> firings) =>
        firings.Sum(f => f.Result.Spells.Count(spell => spell.Outcome == SessionItemSpellOutcome.Started));

    [TestMethod]
    public void Through_the_cast_host_an_item_run_s_damage_fires_its_events_after_the_pass_and_the_setting_decides_the_chain()
    {
        using var owner = Owner();
        var caster = Caster(owner);
        var start = new SessionItemEvent(ItemTriggerEvent.ParentDamagesOpponent, Hero, Hero);
        // Steamwoven (MG46 fixed): the blade's run damages its holder, and its damage step fires no hooks: one run.
        var fixedChain = caster.FireItemEvents([start]);
        Assert.AreEqual(1, Started(fixedChain));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged), "the run's damage step ran");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ItemEventsDropped));

        // Original (MG46 kept): each run's damage step fires the blade again, after the run's pass, until the technical bound.
        owner.SelectRules(RuleMode.Original);
        var chain = caster.FireItemEvents([start]);
        Assert.AreEqual(SessionSpellCaster.MaxItemEventsPerFiring / 2, Started(chain), "one run per damage step's two walks");
        Assert.IsTrue(chain.All(f => f.Holder == Hero && f.Other == Hero));
        var dropped = caster.Log.Single(e => e.Kind == SpellCastEventKind.ItemEventsDropped);
        Assert.IsTrue(dropped.Value > 0);
        Assert.AreEqual(0, owner.QuerySpellRuns().Count, "every run ended");

        // Without executors nothing fires and the events are dropped.
        caster.ItemTriggers = null;
        Assert.AreEqual(0, caster.FireItemEvents([start]).Count);
    }

    [TestMethod]
    public void Through_the_cast_host_the_dying_animation_s_end_walks_a_dead_hero_s_items()
    {
        using var owner = Owner();
        var caster = Caster(owner);
        var host = new Recorder();
        caster.ItemTriggers = host;
        Assert.AreEqual(0, caster.DyingAnimationEnded(Hero).Count, "alive");
        Assert.AreEqual(0, caster.DyingAnimationEnded(Golem).Count, "not a critter of the map");
        owner.Kill(owner.Version, SessionLifeTarget.Hero);
        var fired = caster.DyingAnimationEnded(Hero);
        CollectionAssert.AreEqual(new[] { (ParentDying, Blade, Hero, (string?)null) }, host.Calls.ToArray());
        Assert.AreEqual(ItemTriggerEvent.ParentDying, fired.Single().Event);
        Assert.ThrowsExactly<ArgumentException>(() => caster.FireItemEvents([null!]));
    }
}
