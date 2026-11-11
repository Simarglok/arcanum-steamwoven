using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-358 (MA-1, XR5: 0x454A10 and 0x455100 re-read): the ObjFlag component takes every bit of the spell flags, the object flags and the critter flags on a critter, with the side effects
// the original has (invisible, shrunk, water walking, stoned, don't-draw), and leaves the social and AI flags (MA-4) not ready. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterFlagTests
{
    private const string Target = "oid:target";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), TargetTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter, int dataObjectFlags = 0, int dataSpellFlags = 0, bool onMap = true) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Target when onMap => new(id, SpellTargetObject.Npc, TargetTile, ObjectFlags: dataObjectFlags, SpellFlags: dataSpellFlags, HitPoints: 5),
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
        public SpellCastCritter? Critter(string entity) => entity == Target && onMap ? critter with { ObjectFlags = dataObjectFlags, SpellFlags = dataSpellFlags } : null;
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

    private static SpellCastCritter TargetCritter()
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0);
        return new(SessionCritterCombat.From(Target, fields).Combat!, 0, 0, 0, 0, 0);
    }

    // A maintained spell whose BEGIN turns one flag on the target object and whose END turns it off; no stacking rule, so a second run of it may start.
    private static SessionSpellRecord Flag(int field, uint flag, int number = 93) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, true)),
                new(SpellAction.End, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(field, flag, false))])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0,
                SpellFlagsSet = field == SessionSpellCaster.SpellFlagsField ? flag : 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(SessionSpellRecord record, int dataObjectFlags = 0, int dataSpellFlags = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, TargetCritter(), dataObjectFlags, dataSpellFlags), new Events());
        return (owner, caster);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string to = Target)
    {
        var hero = caster.Facts(Hero)!;
        var at = caster.Facts(to)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, at, at.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        return run;
    }

    // The END component of a run, applied as the run's END action does.
    private static void EndComponent(GameSessionOwner owner, SessionSpellCaster caster, int run, string on = Target) =>
        caster.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(caster.RunObject(on), on == Hero ? HeroTile : TargetTile));

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void The_spell_flag_bits_without_a_side_effect_are_plain_writes_with_the_reference_count()
    {
        // Shielded, anti-magic shell, bonds of magick, full reflection, hardened hands, body of earth, enshrouded, tempus fugit, the four detection bits: the plain write (XR5).
        foreach (var flag in new uint[] { 0x400, 0x800, 0x1000, 0x2000, 0x80000000, 0x8, 0x20000000, 0x4000000, 0x40, 0x80, 0x100, 0x200 })
        {
            var record = Flag(SessionSpellCaster.SpellFlagsField, flag);
            var (owner, caster) = Session(record);
            using var _ = owner;
            var first = Cast(owner, caster, record);
            var mask = unchecked((int)flag);
            Assert.AreEqual(new SessionObjectFlags(mask, 0, 0, 0), owner.QueryObjectFlags(Target), $"0x{flag:X}: {Trail(caster)}");
            Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.ComponentNotReady or SpellCastEventKind.HoldStarted), $"0x{flag:X}: {Trail(caster)}");
            Assert.AreEqual(mask, caster.SpellFlags(caster.RunObject(Target)!), "the run rules read the written bit");
            // A shell and a reflection stop a second spell at the per-target stage (ARC-MAGIC-011), so the count is shown with the other bits: a second run providing the bit is kept.
            if (flag is 0x800 or 0x2000)
            {
                EndComponent(owner, caster, first);
                Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, $"0x{flag:X}: the clear leaves nothing behind");
                continue;
            }
            // The first's END keeps the bit while the second provides it, the last provider's END clears it.
            var second = Cast(owner, caster, record);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagKept, second, mask, Target, 0x1), caster.Log.Last(e => e.Kind is SpellCastEventKind.ObjectFlagKept or SpellCastEventKind.ObjectFlagWritten));
            EndComponent(owner, caster, first);
            Assert.AreEqual(mask, owner.QueryObjectFlags(Target).SpellSet, "one provider left");
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, caster, new(SessionSpellRunActionKind.Free, first)).Disposition);
            EndComponent(owner, caster, second);
            Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, $"0x{flag:X}: the clear leaves nothing behind");
        }
    }

    [TestMethod]
    public void A_set_over_a_bit_the_data_has_keeps_no_change_and_a_clear_of_it_keeps_the_cleared_bit()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, 0x400);
        var (owner, caster) = Session(record, dataSpellFlags: 0x400);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, "the set equals the data's value: no change");
        EndComponent(owner, caster, run);
        Assert.AreEqual(new SessionObjectFlags(0, 0x400, 0, 0), owner.QueryObjectFlags(Target), "the END clears the data's bit");
    }

    [TestMethod]
    public void Invisible_writes_the_invisible_and_translucent_object_flags_on_a_critter_and_the_translucent_one_alone_on_the_hero()
    {
        var record = Flag(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.InvisibleSpellFlag);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SessionObjectFlags(0x1, 0, 0, 0, SessionSpellCaster.ObjectInvisible | SessionSpellCaster.ObjectTranslucent, 0), owner.QueryObjectFlags(Target), Trail(caster));
        Assert.AreEqual(SessionSpellCaster.ObjectInvisible | SessionSpellCaster.ObjectTranslucent, caster.Facts(Target)!.ObjectFlags, "the host's facts read the object flags with the written ones");
        EndComponent(owner, caster, run);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
        // The local player: translucent only (0x454AF8 skips the invisible object flag).
        var self = Flag(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.InvisibleSpellFlag, 94);
        var (o, c) = Session(self);
        using var __ = o;
        var mine = Cast(o, c, self, Hero);
        Assert.AreEqual(new SessionObjectFlags(0x1, 0, 0, 0, SessionSpellCaster.ObjectTranslucent, 0), o.QueryObjectFlags(Hero));
        EndComponent(o, c, mine, Hero);
        Assert.IsTrue(o.QueryObjectFlags(Hero).IsEmpty);
    }

    [TestMethod]
    public void Shrunk_water_walking_and_stoned_write_their_object_flag_and_the_END_takes_it_back()
    {
        foreach (var (spell, objectFlag) in new[] { (SessionSpellCaster.ShrunkSpellFlag, SessionSpellCaster.ObjectShrunk), (SessionSpellCaster.WaterWalkingSpellFlag, SessionSpellCaster.ObjectWaterWalking),
            (SessionSpellCaster.StonedSpellFlag, SessionSpellCaster.ObjectStoned) })
        {
            var record = Flag(SessionSpellCaster.SpellFlagsField, (uint)spell);
            var (owner, caster) = Session(record);
            using var _ = owner;
            var run = Cast(owner, caster, record);
            Assert.AreEqual(new SessionObjectFlags(spell, 0, 0, 0, objectFlag, 0), owner.QueryObjectFlags(Target), $"0x{spell:X}: {Trail(caster)}");
            EndComponent(owner, caster, run);
            Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, $"0x{spell:X}");
        }
    }

    [TestMethod]
    public void Dont_draw_writes_the_object_flag_and_the_paralysed_bit_of_a_critter_and_its_clear_takes_both_back()
    {
        var record = Flag(SessionSpellCaster.ObjectFlagsField, SessionSpellCaster.ObjectDontDraw);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SessionObjectFlags(0, 0, SessionSpellCaster.ParalysedCritterFlag, 0, SessionSpellCaster.ObjectDontDraw, 0), owner.QueryObjectFlags(Target), Trail(caster));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.HoldStarted), "the plain write of the critter flag starts no hold (0x454D76)");
        Assert.AreEqual(SpellTargetObject.DontDraw, caster.Facts(Target)!.ObjectFlags & SpellTargetObject.DontDraw, "the target rules read it (a hidden object is no target)");
        EndComponent(owner, caster, run);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
    }

    [TestMethod]
    public void A_clear_after_the_critter_left_the_map_takes_both_dont_draw_bits_and_a_clear_on_an_unknown_object_writes_nothing()
    {
        var record = Flag(SessionSpellCaster.ObjectFlagsField, SessionSpellCaster.ObjectDontDraw);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsFalse(owner.QueryObjectFlags(Target).IsEmpty);
        // A host over a map that no longer has the critter (the run's END comes after it left).
        var away = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, TargetCritter(), onMap: false), new Events());
        away.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(new SpellRunObject(Target, SpellObjectFacts.NpcType), TargetTile));
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, $"object flag and paralysed bit both taken back: {Trail(away)}");
        // An object the map does not know and the session wrote nothing on is no critter to clear a bit on.
        away.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(new SpellRunObject("oid:other", SpellObjectFacts.NpcType), TargetTile));
        Assert.IsTrue(owner.QueryObjectFlags("oid:other").IsEmpty);
        Assert.IsTrue(away.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Object == "oid:other"), Trail(away));
    }

    [TestMethod]
    public void The_side_effect_writes_carry_the_side_effect_bit_of_their_log_detail()
    {
        var record = Flag(SessionSpellCaster.ObjectFlagsField, SessionSpellCaster.ObjectDontDraw);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, run, SessionSpellCaster.ObjectDontDraw, Target, 0x4 | 0x1), caster.Log.First(e => e.Kind == SpellCastEventKind.ObjectFlagWritten), "the component's own flag");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, run, SessionSpellCaster.ParalysedCritterFlag, Target, 0x2 | 0x1 | SessionSpellCaster.SideEffectDetail),
            caster.Log.Last(e => e.Kind == SpellCastEventKind.ObjectFlagWritten), "the paralysed bit, a side effect");
    }

    [TestMethod]
    public void The_social_and_AI_flags_the_original_gives_a_function_stay_not_ready_and_write_nothing()
    {
        // Charmed, mind controlled (MA-4); undead, fleeing, blinded (MA-4); the portal flags (MA-6) have no field of this host.
        foreach (var (field, flag) in new (int, uint)[] { (20, 0x800000), (20, 0x8000000), (218, 0x4), (218, 0x10), (218, 0x80), (46, 0x1) })
        {
            var record = Flag(field, flag);
            var (owner, caster) = Session(record);
            using var _ = owner;
            var run = Cast(owner, caster, record);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, run, (int)SpellComponentType.ObjFlag, Target), caster.Log.Single(e => e.Kind == SpellCastEventKind.ComponentNotReady), $"field {field} 0x{flag:X}");
            Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty);
        }
    }

    [TestMethod]
    public void Illusion_familiar_and_the_other_bits_with_no_case_in_the_original_are_plain_writes()
    {
        // XR5, XR9, XR10: illusion 0x8000, familiar 0x40000000 have no side effect; floating, mirrored, passwalled, magnetic inversion, drunk are plain too.
        foreach (var flag in new uint[] { 0x8000, 0x40000000, 0x2, 0x40000, 0x100000, 0x400000, 0x10000000 })
        {
            var record = Flag(SessionSpellCaster.SpellFlagsField, flag);
            var (owner, caster) = Session(record);
            using var _ = owner;
            Cast(owner, caster, record);
            Assert.AreEqual(unchecked((int)flag), owner.QueryObjectFlags(Target).SpellSet, $"0x{flag:X}: {Trail(caster)}");
            Assert.AreEqual(0, owner.QueryObjectFlags(Target).ObjectSet, "no object-flag side effect");
        }
    }

    [TestMethod]
    public void The_body_spell_the_spell_flags_hold_feeds_the_unarmed_damage_of_the_hero_and_an_NPC_in_the_originals_order()
    {
        var (owner, caster) = Session(Flag(SessionSpellCaster.SpellFlagsField, 0x8));
        using var _ = owner;
        Assert.AreEqual(UnarmedBodySpell.None, owner.QueryHeroAttack()!.Combatant.UnarmedBodySpell);
        Assert.AreEqual(UnarmedBodySpell.None, owner.UnarmedBodyOf(Target, 0));
        void Write(string who, int mask, bool on) => owner.WriteObjectFlag(owner.Version, who, SessionObjectFlagField.Spell, mask, on);
        Write(Hero, unchecked((int)0x80000000), true);
        Assert.AreEqual(UnarmedBodySpell.HardenedHands, owner.QueryHeroAttack()!.Combatant.UnarmedBodySpell);
        Write(Hero, 0x20, true);
        Assert.AreEqual(UnarmedBodySpell.BodyOfWater, owner.QueryHeroAttack()!.Combatant.UnarmedBodySpell, "water before hardened hands");
        Write(Hero, 0x10, true);
        Assert.AreEqual(UnarmedBodySpell.BodyOfFire, owner.QueryHeroAttack()!.Combatant.UnarmedBodySpell, "fire before water");
        Write(Hero, 0x8, true);
        Assert.AreEqual(UnarmedBodySpell.BodyOfEarth, owner.QueryHeroAttack()!.Combatant.UnarmedBodySpell, "earth first");
        // An NPC under the session's written flags, and over the flags its data stores.
        Write(Target, 0x10, true);
        Assert.AreEqual(UnarmedBodySpell.BodyOfFire, owner.UnarmedBodyOf(Target, 0));
        Assert.AreEqual(UnarmedBodySpell.BodyOfEarth, owner.UnarmedBodyOf(Target, 0x8));
        Assert.AreEqual(UnarmedBodySpell.BodyOfFire, owner.WithNpcEffects(TargetCritter().Combat).UnarmedBody);
        // The spell host's combatant of the NPC reads it (it goes through the NPC's effects and flags since MA-1).
        Assert.AreEqual(UnarmedBodySpell.BodyOfFire, caster.Combatant(new(Target, SpellObjectFacts.NpcType)).UnarmedBodySpell);
        // The flags the NPC's own data stores count too, and a spell that writes the same bit over them changes nothing.
        var stored = SessionCritterCombat.From(Target, AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0, spellFlags: 0x20)).Combat!;
        Assert.AreEqual(0x20, stored.DataSpellFlags);
        Assert.AreEqual(UnarmedBodySpell.BodyOfFire, owner.WithNpcEffects(stored).UnarmedBody, "the session's fire (written above) comes before the data's water");
        owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Spell, 0x10, false);
        Assert.AreEqual(UnarmedBodySpell.BodyOfWater, owner.WithNpcEffects(stored).UnarmedBody, "the data's own water bit");
    }
}
