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

// ARC-SCRIPT-1587 (ledger PZ11): a deferred call's script damage runs inside the session's event pass (the viewer's DamageByScript through
// the cast host's ApplyScriptDamage). The item events of the hit (a worn spell-trigger item's parent-hit event) cannot fire inside the pass
// (GameSessionOwner.FireItemEvents refuses there); they wait in the cast host and fire when the pass ends. Records and numbers are invented.
[TestClass]
public sealed class ScriptDamageInPassTests
{
    private const string Coat = "kit:2";
    private static readonly SpellLocation HeroTile = new(5, 5);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;
    private static readonly SessionScriptRole Pc = new(new(1), "pc");

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

    // The host's event world: a deferred call damages the hero through the cast host, as the viewer's DamageByScript does.
    private sealed class Scripts : ISessionScriptWorld
    {
        public SessionSpellCaster? Caster { get; set; }
        public int Damage { get; set; } = 3;
        public int Type { get; set; }
        public bool? Applied { get; private set; }
        public Func<int>? Probe { get; set; }
        public int? HitPointsInside { get; private set; }
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public bool ObjectExists(SessionMapId map, string entity) => true;
        public bool RunScriptCall(SessionScriptCall call) { Applied = Caster!.ApplyScriptDamage(SessionSpellCaster.Hero, Damage, Type, unresistable: false).Applied; HitPointsInside = Probe?.Invoke(); return true; }
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    // Record 150's shape (the coat's technology record), but its BEGIN deals 2 damage on the going-unconscious event and 5 on the parent-hit event (which a script's damage never fires: it has no attacker).
    private static SessionSpellRecord HitRecord() =>
        SessionSpellRecords.Translate(Record(number: 150, beginTargets: 0x4, info: SpellInfo.Friendly, technology: true,
            components:
            [
                new(SpellAction.Begin, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, 0, null),
                new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, ParentGoingUnconscious, new DamageData(0, 2, 2, 0)),
                new(SpellAction.Begin, SpellComponentType.Damage, new(0x4, 0, 0, 0, -1), 0, ParentHit, new DamageData(0, 5, 5, 0)),
                new(SpellAction.Maintain, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, Wear, null),
            ])
        with { Areas = [.. Enumerable.Repeat(new SpellArea(0x4, 0, 0, 0, -1), 5)], Cost = 0, MaintainCost = 0, MaintainPeriod = 5, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1,
            NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, ItemTriggers = Wear | ParentGoingUnconscious | ParentHit });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Scripts Scripts) WornCoat()
    {
        var spell = HitRecord();
        var owner = CreateWith(HalfElf());
        var scripts = new Scripts();
        var caster = new SessionSpellCaster(owner, n => n == spell.Data.Number ? spell : null, new Map(owner), scripts,
            id => id == Coat ? new SessionItemSpells(Coat, SessionSpellCaster.Hero, 7, [150, 10000, 10000, 10000, 10000], -1, -20) : null);
        scripts.Caster = caster;
        var triggers = new SessionItemTriggers(caster, new NoCombat());
        caster.ItemTriggers = triggers;
        var worn = owner.Equip(owner.Version, Coat, EquipmentSlot.Armor, new SessionWearableItem(Kit[1].Kind, [], true, new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium },
        }), Drawn, Accept, triggers: triggers);
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, $"{worn.Failure}");
        return (owner, caster, scripts);
    }

    private static int HitPoints(GameSessionOwner owner) => owner.QueryHeroLife()!.Status.HitPoints;

    private const int Fatigue = 4, Overkill = 1_000;

    private static void Defer(GameSessionOwner owner)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleScriptCall(owner.Version, new(2664, 0, Pc, Pc), 10_000).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 10_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
    }

    [TestMethod]
    public void A_script_damage_outside_a_pass_fires_the_item_event_of_the_hit_at_once()
    {
        var (owner, caster, _) = WornCoat();
        using var _ = owner;
        var hp = HitPoints(owner);
        Assert.IsTrue(caster.ApplyScriptDamage(SessionSpellCaster.Hero, Overkill, Fatigue, unresistable: false).Applied);
        Assert.AreEqual(hp - 2, HitPoints(owner), "the fatigue damage knocks the hero out: the worn item's going-unconscious spell dealt 2");
    }

    [TestMethod]
    public void A_deferred_calls_script_damage_inside_the_event_pass_fires_the_item_event_of_the_hit_when_the_pass_ends()
    {
        var (owner, caster, scripts) = WornCoat();
        using var _ = owner;
        scripts.Damage = Overkill;
        scripts.Type = Fatigue;
        scripts.Probe = () => HitPoints(owner);
        var hp = HitPoints(owner);
        Defer(owner);
        var pass = caster.ProcessEvents();
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(hp, scripts.HitPointsInside, "inside the pass the item event has not fired yet (the session refuses it there)");
        Assert.AreEqual((true, true), (pass.ScriptCalls.Single().Ran, scripts.Applied), "the call ran and its damage was applied inside the pass");
        Assert.AreEqual(hp - 2, HitPoints(owner), "the worn item's event of the hit fired once the pass ended, not lost");
    }

    [TestMethod]
    public void The_item_event_of_a_hit_inside_a_pass_fires_once_across_two_passes()
    {
        var (owner, caster, scripts) = WornCoat();
        using var _ = owner;
        scripts.Damage = Overkill;
        scripts.Type = Fatigue;
        var hp = HitPoints(owner);
        Defer(owner);
        caster.ProcessEvents();
        caster.ProcessEvents();
        Assert.AreEqual(hp - 2, HitPoints(owner));
    }

    [TestMethod]
    public void A_script_damage_with_no_attacker_fires_no_parent_hit_event_so_a_hit_item_stays_quiet_inside_the_pass()
    {
        // The original's damage step fires the parent hit only with an attacker (0x4B4390 → 0x4CBAD0); a script's damage has none.
        var (owner, caster, scripts) = WornCoat();
        using var _ = owner;
        scripts.Damage = 3;
        var hp = HitPoints(owner);
        Defer(owner);
        caster.ProcessEvents();
        Assert.AreEqual(hp - 3, HitPoints(owner), "only the script's own damage: the worn item's parent-hit spell (5 damage) did not fire");
    }

    [TestMethod]
    public void A_scheduled_poison_call_inside_the_event_pass_poisons_the_hero()
    {
        // AC1 of ARC-SCRIPT-1587: the poison script's damage (type 1, no attacker) reaches the hero's poison level through the pass.
        var (owner, caster, scripts) = WornCoat();
        using var _ = owner;
        scripts.Damage = 10;
        scripts.Type = 1;
        Assert.AreEqual(0, owner.QueryHeroLife()!.Record.State.PoisonLevel);
        Defer(owner);
        var pass = caster.ProcessEvents();
        Assert.AreEqual((true, true), (pass.ScriptCalls.Single().Ran, scripts.Applied));
        Assert.IsTrue(owner.QueryHeroLife()!.Record.State.PoisonLevel > 0, "the hero is poisoned");
    }
}
