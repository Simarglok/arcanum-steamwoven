using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1167 (MA-10, ledger RZ1-RZ3): the Heal component's raise asks the dead target's raise script (point 0x12) through the caster's port before the life step. The hero heals an NPC of the cast map; records, entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterRaiseTests
{
    private const string Npc = "oid:fallen";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), NpcTile = new(6, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, NpcTile, HitPoints: owner.QueryLife(critter.Combat.Target).View?.Status.HitPoints ?? 1),
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
        public SpellCastCritter? Critter(string entity) => entity == Npc ? critter : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // A spell whose BEGIN heals its target with the given Heal flags (Full 0x1, the raise bit 0x2).
    private static SessionSpellRecord Spell(uint flags) =>
        SessionSpellRecords.Translate(Record(number: 3, beginTargets: 0x1, info: SpellInfo.Friendly,
            components: [new(SpellAction.Begin, SpellComponentType.Heal, new SpellArea(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 5, 5, flags))]));

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionLifeTarget Target) Session(SessionSpellRecord record, bool dead, uint critterFlags = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(), critterFlags: critterFlags);
        var critter = new SpellCastCritter(SessionCritterCombat.From(Npc, fields).Combat!, 0, 0, 0, 0, 0);
        var caster = new SessionSpellCaster(owner, n => n == 3 ? record : null, new Map(owner, critter), new Events());
        if (dead) Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, critter.Combat.Target, new LifeDamage { HitPoints = 100_000 }).Disposition);
        Assert.AreEqual(dead, owner.QueryLife(critter.Combat.Target).View!.Status.IsDead);
        return (owner, caster, critter.Combat.Target);
    }

    // The component on its target as a run's BEGIN applies it: the hero the source and parent.
    private static void Heal(SessionSpellCaster caster)
    {
        var hero = caster.RunObject(Hero)!;
        var to = caster.RunObject(Npc)!;
        var run = new SpellRun(0, 3, SpellRunAction.Begin, hero, hero, HeroTile, to, NpcTile, null, SpellRunFlags.Active);
        caster.Apply(run, 0, new(to, NpcTile));
    }

    private static bool Dead(GameSessionOwner owner, SessionLifeTarget target) => owner.QueryLife(target).View!.Status.IsDead;

    [TestMethod]
    public void A_raise_asks_the_dead_targets_raise_script_with_the_healer_and_a_refusal_keeps_the_target_dead()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        var asked = new List<(string Target, string? Healer)>();
        caster.RaiseScript = (who, healer) => { asked.Add((who, healer)); return false; };
        Heal(caster);
        CollectionAssert.AreEqual(new[] { (Npc, (string?)Hero) }, asked, "asked once, the run's parent is the healer");
        Assert.IsTrue(Dead(owner, target), "the script skipped the default: the raise is refused");
    }

    [TestMethod]
    public void A_raise_the_script_allows_brings_the_dead_target_back()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        var asked = 0;
        caster.RaiseScript = (_, _) => { asked++; return true; };
        Heal(caster);
        Assert.AreEqual(1, asked);
        Assert.IsFalse(Dead(owner, target));
    }

    [TestMethod]
    public void A_raise_with_no_host_goes_on()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        Heal(caster);
        Assert.IsFalse(Dead(owner, target));
    }

    [TestMethod]
    public void The_raise_script_is_asked_only_for_a_dead_target_and_a_heal_with_the_raise_bit()
    {
        // RZ1: the applier reaches the script only when the target is dead (0x45D8D0) and the heal carries the raise bit (0x2); a living target or a plain heal never asks.
        foreach (var (flags, dead, expected) in new[] { (SpellDamageFlags.Raise | SpellDamageFlags.Full, false, 0), (SpellDamageFlags.Full, true, 0), (SpellDamageFlags.Full, false, 0), (SpellDamageFlags.Raise | SpellDamageFlags.Full, true, 1) })
        {
            var (owner, caster, _) = Session(Spell((uint)flags), dead);
            using var _o = owner;
            var asked = 0;
            caster.RaiseScript = (_, _) => { asked++; return false; };
            Heal(caster);
            Assert.AreEqual(expected, asked, $"flags={flags:X} dead={dead}");
        }
    }

    [TestMethod]
    public void A_raise_with_no_parent_asks_the_script_with_no_healer()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        var asked = new List<(string Target, string? Healer)>();
        caster.RaiseScript = (who, healer) => { asked.Add((who, healer)); return false; };
        var to = caster.RunObject(Npc)!;
        caster.Apply(new SpellRun(0, 3, SpellRunAction.Begin, null, null, HeroTile, to, NpcTile, null, SpellRunFlags.Active), 0, new(to, NpcTile));
        CollectionAssert.AreEqual(new[] { (Npc, (string?)null) }, asked);
        Assert.IsTrue(Dead(owner, target));
    }

    // ARC-MAGIC-1838 (RZ3): a refused raise (the script) tells the player message 605 and plays the resisted effect (entry 387, no run) on the target; an allowed one tells nothing.
    [TestMethod]
    public void A_refused_raise_shows_message_605_and_plays_the_resisted_effect_on_the_target()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        caster.RaiseScript = (_, _) => false;
        var start = caster.Log.Count;
        Heal(caster);
        Assert.IsTrue(Dead(owner, target));
        CollectionAssert.AreEqual(new[]
        {
            new SpellCastEvent(SpellCastEventKind.Message, 0, SessionSpellCaster.RaiseRefusedMessage),
            new SpellCastEvent(SpellCastEventKind.EyeCandyAdded, -1, SessionSpellCaster.RaiseRefusedEyeCandyEntry, Npc, 0),
        }, caster.Log.Skip(start).ToArray());
        Assert.AreEqual((605, 387), (SessionSpellCaster.RaiseRefusedMessage, SessionSpellCaster.RaiseRefusedEyeCandyEntry));
    }

    [TestMethod]
    public void An_allowed_raise_tells_nothing_of_a_refusal()
    {
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true);
        using var _ = owner;
        caster.RaiseScript = (_, _) => true;
        var start = caster.Log.Count;
        Heal(caster);
        Assert.IsFalse(Dead(owner, target));
        Assert.IsFalse(caster.Log.Skip(start).Any(e => e.Kind is SpellCastEventKind.Message or SpellCastEventKind.EyeCandyAdded), "no message and no effect");
    }

    [TestMethod]
    public void A_heal_that_asks_no_raise_tells_nothing_of_a_refusal()
    {
        // A plain heal of a dead target is not a raise (AlreadyDead): no refusal feedback.
        var (owner, caster, _) = Session(Spell(SpellDamageFlags.Full), dead: true);
        using var _o = owner;
        var start = caster.Log.Count;
        Heal(caster);
        Assert.IsFalse(caster.Log.Skip(start).Any(e => e.Kind is SpellCastEventKind.Message or SpellCastEventKind.EyeCandyAdded));
    }

    [TestMethod]
    public void A_raise_the_flags_refuse_shows_the_feedback_without_asking_the_script_to_allow_it()
    {
        // RZ2: the flag refusal (0x1000000, unresurrectable) goes to the same feedback; the script is asked first either way.
        var (owner, caster, target) = Session(Spell(SpellDamageFlags.Raise | SpellDamageFlags.Full), dead: true, critterFlags: 0x1000000);
        using var _ = owner;
        caster.RaiseScript = (_, _) => true;
        var start = caster.Log.Count;
        Heal(caster);
        Assert.IsTrue(Dead(owner, target));
        CollectionAssert.AreEqual(new[] { SpellCastEventKind.Message, SpellCastEventKind.EyeCandyAdded }, caster.Log.Skip(start).Select(e => e.Kind).ToArray());
    }
}
