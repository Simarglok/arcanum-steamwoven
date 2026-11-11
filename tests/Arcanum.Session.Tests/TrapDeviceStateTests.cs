using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-WORLD-124 (E6-b; ledger IA13, TP2, TP3): the trap device with a slot-1 spell, placed by the hero. Invented prototypes and records; the marker prototypes 4047 and 4049 are the ledger's (TP8).
[TestClass]
public sealed class TrapDeviceStateTests
{
    private const string Bomb = "kit:1", Dagger = "kit:2";
    private static readonly SpellLocation HeroTile = new(5, 5);
    private static readonly SpellLocation Spot = new(9, 7);

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

    private static SessionSpellRecord Bang(int number) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: SpellUiRules.TileBit, info: SpellInfo.Aggressive,
            components: [new(SpellAction.Begin, SpellComponentType.NoOp, new(0x4, 0, 0, 0, -1), 0, 0, null)])
        with { Cost = 0, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    // The kit: a generic device (slot 1 holds the given spell) and a weapon that is no device.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int slotOne = 176)
    {
        var records = new[] { 150, 176, 220 }.Select(Bang).ToDictionary(r => r.Data.Number);
        var owner = CreateWith(HalfElf() with { Kit = [new(new(SessionItemType.Generic, 15154, 4100, 15154)), Kit[0]] });
        var caster = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner), new Events(),
            id => id switch
            {
                Bomb => new SessionItemSpells(Bomb, SessionSpellCaster.Hero, 14, [10000, slotOne, 10000, 10000, 10000], 0, 10),
                Dagger => new SessionItemSpells(Dagger, SessionSpellCaster.Hero, 5, [10000, 176, 10000, 10000, 10000], 0, 10),
                _ => null,
            });
        return (owner, caster);
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void A_timed_device_on_a_tile_is_destroyed_leaves_its_marker_and_sets_the_fuse_in_one_revision()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var use = caster.UseTrapDevice(Bomb, Spot, onTile: true);
        Assert.AreEqual(SessionTrapDeviceOutcome.Placed, use.Outcome);
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 176, 4047, true, 88_000), use.Plan);
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.IsTrue(owner.IsItemDestroyed(Bomb), "the device is spent");
        var marker = use.Marker!;
        Assert.AreEqual((SessionCreatedKind.Scenery, 4047, new CellCoord(9, 7)), (marker.Kind, marker.Prototype, marker.Cell));
        Assert.AreEqual(marker, owner.QueryCreatedObject(marker.Entity));
        var fuse = owner.QueryTrapDevices().Single();
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + 88_000, fuse.DeadlineMs);
        CollectionAssert.AreEqual(new long[] { owner.QueryClockSnapshot().GameTimestampMs, 88_000, 176, 9, 7, 1 }, fuse.Payload.Values.ToArray());
        Assert.AreEqual(marker.Entity, fuse.Payload.Entities[0].Code);
    }

    [TestMethod]
    public void The_marker_prototype_follows_the_record_and_an_object_target_makes_none()
    {
        foreach (var (spell, marker) in new[] { (176, 4047), (220, 4049), (150, 0) })
        {
            var (owner, caster) = Session(spell);
            using var _ = owner;
            var use = caster.UseTrapDevice(Bomb, Spot, onTile: true);
            Assert.AreEqual(marker, use.Marker?.Prototype ?? 0, $"record {spell}");
            Assert.AreEqual(marker == 0 ? 0 : 1, owner.QueryCreatedObjects(owner.QueryActor().Map).Count);
        }
        var (onObject, objectCaster) = Session();
        using var __ = onObject;
        var placed = objectCaster.UseTrapDevice(Bomb, Spot, onTile: false);
        Assert.AreEqual((SessionTrapDeviceOutcome.Placed, null), (placed.Outcome, placed.Marker), "on an object the device only schedules the run at its place");
        Assert.AreEqual(0, onObject.QueryCreatedObjects(onObject.QueryActor().Map).Count);
        var fuse = onObject.QueryTrapDevices().Single();
        Assert.AreEqual((Bomb, 0L), (fuse.Payload.Entities[0].Code, fuse.Payload.Values[5]));
    }

    [TestMethod]
    public void A_script_device_an_unreadable_slot_and_a_non_device_change_nothing()
    {
        var (script, scriptCaster) = Session(10000);
        using var _ = script;
        Assert.AreEqual(SessionTrapDeviceOutcome.Script, scriptCaster.UseTrapDevice(Bomb, Spot, onTile: true).Outcome);
        var (invalid, invalidCaster) = Session(500);
        using var __ = invalid;
        Assert.AreEqual(SessionTrapDeviceOutcome.Invalid, invalidCaster.UseTrapDevice(Bomb, Spot, onTile: true).Outcome);
        var (owner, caster) = Session();
        using var ___ = owner;
        Assert.AreEqual(SessionTrapDeviceOutcome.Unknown, caster.UseTrapDevice("kit:9", Spot, onTile: true).Outcome);
        // A weapon with a slot-1 spell: the session refuses it (the device is a generic item), and nothing is made.
        var weapon = caster.UseTrapDevice(Dagger, Spot, onTile: true);
        Assert.AreEqual((SessionTrapDeviceOutcome.Refused, GameSessionFailure.InventoryMismatch), (weapon.Outcome, weapon.Failure));
        foreach (var session in new[] { script, invalid, owner })
        {
            Assert.AreEqual(0, session.Version.Revision);
            Assert.AreEqual(0, session.QueryTrapDevices().Count);
        }
    }

    [TestMethod]
    public void The_session_refuses_a_stale_version_a_bad_cell_and_a_full_fuse_budget_before_any_effect()
    {
        var (owner, _) = Session();
        using var _ = owner;
        var map = owner.QueryActor().Map;
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.PlaceTrapDevice(owner.Version with { Revision = 5 }, Bomb, map, new(1, 1), 176, 4047).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceTrapDevice(owner.Version, Bomb, map, new(1, 1), 223, 4047).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.QueryTrapDeviceRefusal(map, new(-1, 1), 176, 0));
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.PlaceTrapDevice(owner.Version, "", map, new(1, 1), 176, 4047).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PlaceTrapDevice(owner.Version, "kit:9", map, new(1, 1), 176, 4047).Failure);
        Assert.IsNull(owner.QueryTrapDeviceRefusal(map, new(1, 1), 176, 4047));
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.IsFalse(owner.IsItemDestroyed(Bomb));
    }

    [TestMethod]
    public void The_fuse_ends_after_88_seconds_destroys_the_marker_and_casts_the_record_at_the_tile_with_no_source()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseTrapDevice(Bomb, Spot, onTile: true).Marker!;
        Advance(owner, 87_999);
        Assert.AreEqual(0, caster.ProcessEvents().TrapDevices.Count, "not yet");
        Assert.IsNotNull(owner.QueryCreatedObject(marker.Entity));
        Advance(owner, 1);
        var pass = caster.ProcessEvents();
        var fired = pass.TrapDevices.Single();
        Assert.AreEqual((176, new CellCoord(9, 7), true, false), (fired.Spell, fired.Cell, fired.MarkerDestroyed, fired.Postponed));
        Assert.IsNull(owner.QueryCreatedObject(marker.Entity), "the marker is destroyed");
        Assert.AreEqual(0, owner.QueryTrapDevices().Count);
        var cast = caster.TakeTrapDeviceCasts().Single();
        Assert.AreEqual(SpellCastAttemptOutcome.Started, cast.Outcome);
        var run = owner.QuerySpellRuns()[cast.Run!.Value]!;
        Assert.AreEqual((Spot.X, Spot.Y), (run.TargetTile.X, run.TargetTile.Y));
        Assert.IsNull(run.Source, "a source-less run");
        Assert.IsNull(run.Parent, "and a parent-less one");
        Assert.AreEqual(0, caster.TakeTrapDeviceCasts().Count, "taken once");
    }

    [TestMethod]
    public void A_pass_without_the_cast_host_waits_a_minute_and_keeps_the_marker()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseTrapDevice(Bomb, Spot, onTile: true).Marker!;
        Advance(owner, 88_000);
        var plain = owner.ProcessEvents(new Events());
        var waited = plain.TrapDevices.Single();
        Assert.IsTrue(waited.Postponed);
        Assert.IsNotNull(owner.QueryCreatedObject(marker.Entity));
        var again = owner.QueryTrapDevices().Single();
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + 60_000, again.DeadlineMs);
        Advance(owner, 60_000);
        Assert.AreEqual(1, caster.ProcessEvents().TrapDevices.Count(f => !f.Postponed), "the cast host runs it");
        Assert.AreEqual(SpellCastAttemptOutcome.Started, caster.TakeTrapDeviceCasts().Single().Outcome);
    }

    [TestMethod]
    public void A_pending_fuse_and_its_marker_survive_a_save_and_a_restore_and_a_crafted_fuse_is_refused()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        caster.UseTrapDevice(Bomb, Spot, onTile: true);
        var snapshot = owner.Capture();
        var text = GameSessionSnapshotCodec.Encode(snapshot);
        using var restored = Restore(Decode(text));
        Assert.AreEqual(1, restored.QueryTrapDevices().Count);
        Assert.AreEqual(1, restored.QueryCreatedObjects(restored.QueryActor().Map).Count);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        // A fuse whose deadline is not its setting plus the fuse or the retry is refused.
        var deadline = snapshot.Events.Pending.Single().DeadlineMs;
        Assert.IsTrue(text.Contains($"\"deadlineMs\":{deadline}", StringComparison.Ordinal), "the field name the codec writes");
        var crafted = text.Replace($"\"deadlineMs\":{deadline}", $"\"deadlineMs\":{deadline + 1}", StringComparison.Ordinal);
        var refused = GameSessionOwner.Restore(Decode(crafted), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.InvalidEvents, refused.Failure);
    }
}
