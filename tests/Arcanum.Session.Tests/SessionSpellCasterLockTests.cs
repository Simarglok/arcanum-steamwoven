using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-037 (MA-6, ledger LM1–LM5; 0x454A10, 0x4551C0, 0x45A520, 0x4AEE50, 0x451C40 re-read for this slice): the ObjFlag component on the lock flags of a portal (46) and a container (56) — Unlocking Cantrip's
// locked-off with the aptitude gate, Magelock's magically-held on and off — written plainly (no Lock event), and the Dispel component's clear of the held bit. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterLockTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SessionOpenable Door = new(new(1), "sector:1:door"), Chest = new(new(1), "sector:1:chest");
    private static readonly SpellLocation HeroTile = new(5, 5), DoorTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SessionOpenableBaseline door, SessionOpenableBaseline chest, int doorLevel, int chestLevel) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            "sector:1:door" => new(id, SpellTargetObject.Portal, DoorTile, HitPoints: 1, PortalFlags: owner.QueryOpenable(Door, door).Flags),
            "sector:1:chest" => new(id, SpellTargetObject.Container, DoorTile, HitPoints: 1, ContainerFlags: owner.QueryOpenable(Chest, chest).Flags),
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
        public SpellOpenable? Openable(string id) => id switch
        {
            "sector:1:door" => new(Door, door, doorLevel),
            "sector:1:chest" => new(Chest, chest, chestLevel),
            _ => null,
        };
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static readonly SessionOpenableBaseline LockedDoor = new(OpenableKind.Portal, LockRules.LockedBit, 7, 0), LockedChest = new(OpenableKind.Container, LockRules.LockedBit, 7, 0);

    private static SessionSpellRecord Spell(int number, params (SpellAction Action, int Field, uint Flag, bool On)[] flags) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: flags.Select(f => new SpellComponent(f.Action, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(f.Field, f.Flag, f.On))).ToArray())
            with { Cost = 0, MaintainCost = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static readonly SessionSpellRecord Unlock = Spell(1, (SpellAction.Begin, 46, 1, false), (SpellAction.Begin, 56, 1, false)),
        Magelock = Spell(75, (SpellAction.Begin, 46, 4, true), (SpellAction.End, 46, 4, false)), Dispel = SessionSpellRecords.Translate(Record(number: 41, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.Dispel, new(0x4, 0, 0, 0, -1), 0, 0, null)]));

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int doorLevel = 0, int chestLevel = 0, SessionOpenableBaseline? door = null, SessionOpenableBaseline? chest = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var records = new[] { Unlock, Magelock, Dispel }.ToDictionary(r => r.Data.Number);
        return (owner, new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner, door ?? LockedDoor, chest ?? LockedChest, doorLevel, chestLevel), new Events()));
    }

    // The component of a record applied as the run's action would, with the hero as parent (its aptitude replaced) and the object as target.
    private static SpellComponentOutcome? Apply(SessionSpellCaster caster, SessionSpellRecord record, int index, string target, int? aptitude = null, bool noParent = false)
    {
        var hero = caster.RunObject(Hero)!;
        var parent = noParent ? null : aptitude is { } a ? hero with { Aptitude = a } : hero;
        var to = caster.RunObject(target)!;
        var c = record.Components[index];
        return caster.Apply(new SpellRun(0, record.Data.Number, c.Action, hero, parent, HeroTile, to, DoorTile, null, SpellRunFlags.Active), c.Index, new(to, DoorTile));
    }

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void Unlocking_a_portal_and_a_container_clears_the_locked_bit_by_a_plain_write_that_schedules_no_lock_event()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Unlock, 0, Door.Entity), Trail(caster));
        Assert.AreEqual(new SessionOpenableState(0, 0, false), owner.QueryOpenable(Door, LockedDoor));
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor, false));
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Unlock, 1, Chest.Entity), Trail(caster));
        Assert.AreEqual(new SessionOpenableState(0, 0, false), owner.QueryOpenable(Chest, LockedChest));
        Assert.AreEqual(0, owner.QueryEvents().Count, "LM1: the field write is not the lock function (0x441DD0), so no Lock event relocks the object an hour later");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), Trail(caster));
        CollectionAssert.AreEqual(new[] { (SpellCastEventKind.LockGuardAlert, Door.Entity, 0), (SpellCastEventKind.OpenableFlagWritten, Door.Entity, 0),
            (SpellCastEventKind.LockGuardAlert, Chest.Entity, 0), (SpellCastEventKind.OpenableFlagWritten, Chest.Entity, 2) },
            caster.Log.Select(e => (e.Kind, e.Object!, e.Detail)).ToArray(), "LM2: the guard alert comes before the write, the container's detail has bit 0x2");
    }

    [TestMethod]
    public void The_lock_flag_of_the_other_kind_is_not_ready_and_an_already_unlocked_object_writes_nothing()
    {
        var (owner, caster) = Session(door: LockedDoor with { Flags = 0 });
        using var _ = owner;
        // A component the host does not take is logged not ready by the caller of the dispatch (Apply), as the census reads it.
        Apply(caster, Unlock, 1, Door.Entity);
        Apply(caster, Unlock, 0, Chest.Entity);
        Apply(caster, Unlock, 0, Hero);
        Assert.AreEqual(3, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.ObjFlag), "field 56 on a portal, field 46 on a container, a critter has no lock flags");
        Assert.AreEqual(0, caster.Log.Count(e => e.Kind == SpellCastEventKind.OpenableFlagWritten));
        var version = owner.Version.Revision;
        var logged = caster.Log.Count;
        Apply(caster, Unlock, 0, Door.Entity);
        Assert.AreEqual(version, owner.Version.Revision, "the same write again changes nothing");
        Assert.IsFalse(caster.Log.Skip(logged).Any(e => e.Kind == SpellCastEventKind.OpenableFlagWritten), "and tells no line: the door was unlocked already");
    }

    [TestMethod]
    public void The_unlock_gate_refuses_a_lock_above_the_casters_aptitude_and_lets_level_nineteen_and_below_through()
    {
        var (owner, caster) = Session(doorLevel: 40);
        using var _ = owner;
        // 0x4551C0: a lock level above 19 needs the parent's captured aptitude to reach it.
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Unlock, 0, Door.Entity, aptitude: 39));
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(Door, LockedDoor, false), "the lock stays");
        CollectionAssert.AreEqual(new[] { SpellCastEventKind.UnlockRefused, SpellCastEventKind.Resisted, SpellCastEventKind.Message, SpellCastEventKind.LockGuardAlert }, caster.Log.Select(e => e.Kind).ToArray(), Trail(caster));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.UnlockRefused, 0, 40, Door.Entity), caster.Log[0]);
        Assert.AreEqual(SessionSpellCaster.ResistedMessage, caster.Log[2].Value, "the local player is shown spell.mes 643");
        Assert.AreEqual(1, caster.Log[3].Detail, "the guard alert comes after a refusal too");
        Assert.AreEqual(0, owner.Version.Revision, "nothing was written");
        Apply(caster, Unlock, 0, Door.Entity, aptitude: 40);
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor, false), "the aptitude reaches the level");
        var (easy, easyCaster) = Session(doorLevel: 19);
        using var __ = easy;
        Apply(easyCaster, Unlock, 0, Door.Entity, aptitude: -50);
        Assert.AreEqual(OpenAttempt.Opens, easy.AttemptOpen(Door, LockedDoor, false), "a lock of level 19 or less needs no aptitude");
    }

    [TestMethod]
    public void The_unlock_gate_passes_without_a_parent_or_with_a_non_critter_parent_and_while_the_armed_fate_spell_at_maximum_is_used()
    {
        var (owner, caster) = Session(doorLevel: 99);
        using var _ = owner;
        Apply(caster, Unlock, 0, Door.Entity, noParent: true);
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor, false), "no parent: the gate returns 1 (0x4551C0 tests the parent handle first)");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.LockGuardAlert), "the guard alert needs a parent too (0x4AEE50 returns at once)");
        // A parent that is no PC or NPC (an item's own run): the gate passes at any level and the alert is not raised (0x4AEE50 returns for it).
        var (item, itemCaster) = Session(doorLevel: 99);
        using var ___ = item;
        var itemRun = new SpellRun(0, 1, SpellRunAction.Begin, new("oid:thing", SpellTargetObject.Scenery), new("oid:thing", SpellTargetObject.Scenery, -100), HeroTile, itemCaster.RunObject(Door.Entity), DoorTile, null, SpellRunFlags.Active);
        itemCaster.Apply(itemRun, Unlock.Components[0].Index, new(itemCaster.RunObject(Door.Entity), DoorTile));
        Assert.AreEqual(OpenAttempt.Opens, item.AttemptOpen(Door, LockedDoor, false), "a parent that is no critter passes the gate");
        Assert.IsFalse(itemCaster.Log.Any(e => e.Kind == SpellCastEventKind.LockGuardAlert), "and calls no guards");
        // The fate: the run system asks it once per pass; the flag 0x5E75A8 then makes the gate pass whatever the level.
        var (fated, fatedCaster) = Session(doorLevel: 500);
        using var __ = fated;
        fated.GrantHeroFatePoint(fated.Version);
        fated.ArmHeroFate(fated.Version, FateKind.SpellAtMaximum);
        fatedCaster.Collect(new SpellRun(0, 1, SpellRunAction.Begin, fatedCaster.RunObject(Hero), fatedCaster.RunObject(Hero), HeroTile, fatedCaster.RunObject(Door.Entity), DoorTile, null, SpellRunFlags.Active));
        Assert.IsTrue(fatedCaster.UseSpellAtMaximum(fatedCaster.RunObject(Hero)!));
        Apply(fatedCaster, Unlock, 0, Door.Entity, aptitude: 0);
        Assert.AreEqual(OpenAttempt.Opens, fated.AttemptOpen(Door, LockedDoor, false), "the used fate lets any lock through");
        // The flag is the pass's: the next pass that does not use the fate (Collect resets it) is refused again at level 500.
        fated.SetOpenableLocked(fated.Version, Door, LockedDoor, true);
        fatedCaster.Collect(new SpellRun(0, 1, SpellRunAction.Begin, fatedCaster.RunObject(Hero), fatedCaster.RunObject(Hero), HeroTile, fatedCaster.RunObject(Door.Entity), DoorTile, null, SpellRunFlags.Active));
        Apply(fatedCaster, Unlock, 0, Door.Entity, aptitude: 0);
        Assert.AreEqual(OpenAttempt.Locked, fated.AttemptOpen(Door, LockedDoor, false), "the fate flag does not outlive its pass");
    }

    [TestMethod]
    public void Magelock_sets_the_held_bit_at_the_begin_and_clears_it_at_the_end_with_no_reference_count()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var open = LockedDoor with { Flags = 0 };
        var (free, freeCaster) = Session(door: open);
        using var __ = free;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(freeCaster, Magelock, 0, Door.Entity), Trail(freeCaster));
        Assert.AreEqual(LockRules.MagicallyHeldBit, free.QueryOpenable(Door, open).Flags);
        Assert.AreEqual(OpenAttempt.MagicallyHeld, free.AttemptOpen(Door, open, false), "LK7: an unlocked door the spell holds does not open");
        Assert.AreEqual(0, free.QueryEvents().Count);
        // Two Magelocks, one END: the bit goes (0x455100 counts only the spell flags field).
        Apply(freeCaster, Magelock, 0, Door.Entity);
        Apply(freeCaster, Magelock, 1, Door.Entity);
        Assert.AreEqual(0, free.QueryOpenable(Door, open).Flags);
        Assert.AreEqual(OpenAttempt.Opens, free.AttemptOpen(Door, open, false));
        Assert.IsFalse(freeCaster.Log.Any(e => e.Kind is SpellCastEventKind.LockGuardAlert or SpellCastEventKind.ObjectFlagKept), "only the locked clear has the gate and the alert, and nothing counts the runs");
        // A held, locked door keeps its lock: the held bit is tested first.
        Apply(caster, Magelock, 0, Door.Entity);
        Assert.AreEqual(LockRules.LockedBit | LockRules.MagicallyHeldBit, owner.QueryOpenable(Door, LockedDoor).Flags);
        Assert.AreEqual(OpenAttempt.MagicallyHeld, owner.AttemptOpen(Door, LockedDoor, false));
    }

    [TestMethod]
    public void Dispel_clears_the_held_bit_of_a_portal_or_container_by_a_plain_write_and_leaves_the_other_bits()
    {
        var held = LockedDoor with { Flags = LockRules.LockedBit | LockRules.MagicallyHeldBit | LockRules.JammedBit };
        var heldChest = LockedChest with { Flags = LockRules.MagicallyHeldBit };
        var (owner, caster) = Session(door: held, chest: heldChest);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Dispel, 0, Door.Entity), Trail(caster));
        Assert.AreEqual(LockRules.LockedBit | LockRules.JammedBit, owner.QueryOpenable(Door, held).Flags);
        Apply(caster, Dispel, 0, Chest.Entity);
        Assert.AreEqual(0, owner.QueryOpenable(Chest, heldChest).Flags);
        Assert.AreEqual(0, owner.QueryEvents().Count);
        var written = caster.Log.Where(e => e.Kind == SpellCastEventKind.OpenableFlagWritten).ToArray();
        CollectionAssert.AreEqual(new[] { (LockRules.MagicallyHeldBit, Door.Entity, 0), (LockRules.MagicallyHeldBit, Chest.Entity, 2) }, written.Select(e => (e.Value, e.Object!, e.Detail)).ToArray());
        var version = owner.Version.Revision;
        Apply(caster, Dispel, 0, Door.Entity);
        Assert.AreEqual(version, owner.Version.Revision, "a portal that is not held is left alone");
    }

    [TestMethod]
    public void The_session_write_refuses_a_bad_baseline_and_a_zero_mask()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        // An object the lock domain rejects (a baseline frame beyond the ART id's bits).
        var bad = new SpellOpenable(Door, new(OpenableKind.Portal, 1, 0, 99), 0);
        var result = owner.WriteOpenableFlag(owner.Version, bad.Openable, bad.Baseline, 1, false);
        Assert.AreEqual(GameSessionDisposition.Rejected, result.Disposition);
        Assert.AreEqual(GameSessionFailure.InvalidOpenable, result.Failure);
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.WriteOpenableFlag(owner.Version, Door, LockedDoor, 0, true).Disposition, "a zero mask is no write");
    }
}
