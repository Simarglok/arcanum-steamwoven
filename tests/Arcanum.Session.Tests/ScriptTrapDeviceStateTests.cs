using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-125 (E6-c; ledger IA13, TP2): the script trap device (slot 1 empty) armed by the hero on a portal, a container or a tile. Invented scripts and entities; the marker prototypes 26000..26001 and the
// native script numbers 30000..30007 are the ledger's.
[TestClass]
public sealed class ScriptTrapDeviceStateTests
{
    private const string Device = "kit:1", Second = "kit:2", Dagger = "kit:3";
    private static readonly SpellLocation Spot = new(9, 7);
    private static readonly SessionTrapScript Native = new(30003, 0x5, 0x00120A0B);
    private static readonly SessionScriptTrapTarget Chest = new("oid:static:chest", TrapDeviceRules.ObjectTypeContainer, 0);

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            id == SessionSpellCaster.Hero ? new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
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

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int slotOne = 10000)
    {
        var owner = CreateWith(HalfElf() with { Kit = [new(new(SessionItemType.Generic, 15100, 4100, 15100)), new(new(SessionItemType.Generic, 15120, 4101, 15120)), Kit[0]] });
        var caster = new SessionSpellCaster(owner, _ => null, new Map(owner), new Events(),
            id => id switch
            {
                Device or Second => new SessionItemSpells(id, SessionSpellCaster.Hero, 14, [10000, slotOne, 10000, 10000, 10000], 0, 10),
                Dagger => new SessionItemSpells(Dagger, SessionSpellCaster.Hero, 5, [10000, 10000, 10000, 10000, 10000], 0, 10),
                _ => null,
            });
        return (owner, caster);
    }

    private static SessionTrapBaseline MarkerBaseline(GameSessionOwner owner, SessionCreatedObject marker, uint header = 0x1) =>
        owner.QueryMarkerTrapBaseline(marker.Map, marker.Entity, header)!.Value;

    [TestMethod]
    public void A_script_trap_on_a_tile_makes_a_marker_that_carries_the_script_and_spends_the_device_in_one_revision()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var use = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, trapObjectOnTile: false);
        Assert.AreEqual(SessionScriptTrapOutcome.Armed, use.Outcome);
        Assert.AreEqual(new ScriptTrapPlan(true, true, 26003, 0), use.Plan);
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.IsTrue(owner.IsItemDestroyed(Device), "the device is spent");
        var marker = use.Marker!;
        Assert.AreEqual((SessionCreatedKind.Scenery, 26003, new CellCoord(9, 7)), (marker.Kind, marker.Prototype, marker.Cell));
        Assert.AreEqual(Native, owner.QueryPlacedTrapScript(marker.Map, marker.Entity));
        Assert.AreEqual(marker, owner.QueryScriptTrapMarkers(marker.Map, new(9, 7)).Single());
        Assert.AreEqual(0, owner.QueryScriptTrapMarkers(marker.Map, new(9, 8)).Count);
        Assert.AreEqual(0, owner.QueryTrapDevices().Count, "a script trap has no fuse");
    }

    [TestMethod]
    public void The_marker_is_a_spotted_trap_the_hero_placed_so_its_state_reads_the_script_and_the_flags()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, false).Marker!;
        var baseline = MarkerBaseline(owner, marker);
        Assert.AreEqual((TrapCarrier.TrapObject, 30003, 0x00120A0B), (baseline.Carrier, baseline.Script, baseline.Counters));
        var state = owner.QueryTrap(new(marker.Map, marker.Entity), baseline);
        Assert.AreEqual((TrapClass.Nonmagical, 30003, true, true, false), (state.Class, state.Script, state.Spotted, state.PlacedByPc, state.Removed));
        Assert.AreEqual(0, state.ObjectFlags & TrapRules.DontDrawFlag, "spotted, so no longer don't-draw");
        Assert.IsTrue(owner.TrapKnownTo(new(marker.Map, marker.Entity), baseline, TrapObserver.Player));
        Assert.IsFalse(owner.TrapKnownTo(new(marker.Map, marker.Entity), baseline, TrapObserver.FreeNpc), "a free NPC does not know a trap the PC placed");
    }

    [TestMethod]
    public void A_script_trap_on_a_container_writes_the_slot_onto_it_and_makes_no_marker()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var use = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, Chest, false);
        Assert.AreEqual((SessionScriptTrapOutcome.Armed, Chest.Entity, null), (use.Outcome, use.Target, use.Marker));
        Assert.AreEqual(new ScriptTrapPlan(true, false, 0, 0), use.Plan);
        var map = owner.QueryActor().Map;
        Assert.AreEqual(Native, owner.QueryPlacedTrapScript(map, Chest.Entity));
        Assert.AreEqual(0, owner.QueryCreatedObjects(map).Count);
        // A trapped object keeps its own baseline: the host's row data; the placement adds the placed and spotted marks.
        var baseline = new SessionTrapBaseline(TrapCarrier.TrappedObject, 30003, 0x1, 0x00120A0B, 0);
        var state = owner.QueryTrap(new(map, Chest.Entity), baseline);
        Assert.AreEqual((true, true), (state.Spotted, state.PlacedByPc));
        Assert.IsTrue(owner.IsItemDestroyed(Device));
    }

    [TestMethod]
    public void The_refusals_keep_the_device_and_answer_the_lines_of_trap_mes()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        foreach (var target in new SessionScriptTrapTarget[] { new("oid:static:bush", 4, 0), new("oid:static:door", TrapDeviceRules.ObjectTypePortal, 7), new("oid:static:npc", 16, 0) })
        {
            var refused = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, target, false);
            Assert.AreEqual((SessionScriptTrapOutcome.TargetRefused, 2), (refused.Outcome, refused.Plan.RefusalLine), target.Entity);
        }
        var first = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, trapObjectOnTile: true);
        Assert.AreEqual((SessionScriptTrapOutcome.TileRefused, 3), (first.Outcome, first.Plan.RefusalLine), "the map data holds a trap object there");
        Assert.AreEqual(0, owner.Version.Revision, "nothing changed");
        Assert.IsFalse(owner.IsItemDestroyed(Device));
    }

    [TestMethod]
    public void A_wall_takes_the_tile_form_and_a_marker_refuses_a_second_script_trap_on_its_tile()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var wall = new SessionScriptTrapTarget("oid:static:wall", TrapDeviceRules.ObjectTypeWall, 0);
        var use = caster.UseScriptTrapDevice(Device, Native, TrapClass.Magical, Spot, wall, false);
        Assert.AreEqual((SessionScriptTrapOutcome.Armed, 26003), (use.Outcome, use.Marker?.Prototype ?? 0), "a native script takes its table's marker even when magical");
        Assert.IsNull(use.Target);
        // The marker stands on the tile (0x4BCAB0): another device is refused with line 3 and stays, a tile next to it takes one.
        var again = caster.UseScriptTrapDevice(Second, Native, TrapClass.Nonmagical, Spot, null, false);
        Assert.AreEqual((SessionScriptTrapOutcome.TileRefused, 3), (again.Outcome, again.Plan.RefusalLine));
        Assert.IsFalse(owner.IsItemDestroyed(Second));
        Assert.AreEqual(SessionScriptTrapOutcome.Armed, caster.UseScriptTrapDevice(Second, Native, TrapClass.Nonmagical, new(10, 7), null, false).Outcome);
    }

    [TestMethod]
    public void A_non_native_script_takes_the_marker_of_its_class()
    {
        foreach (var (kind, marker) in new[] { (TrapClass.Magical, 26000), (TrapClass.Nonmagical, 26001), (TrapClass.None, 26001) })
        {
            var (owner, caster) = Session();
            using var _ = owner;
            var use = caster.UseScriptTrapDevice(Device, new(777, 0, 0), kind, Spot, null, false);
            Assert.AreEqual(marker, use.Marker?.Prototype ?? 0, kind.ToString());
        }
    }

    [TestMethod]
    public void A_device_that_is_not_a_script_trap_or_not_the_heros_changes_nothing()
    {
        var (timed, timedCaster) = Session(176);
        using var _ = timed;
        Assert.AreEqual(SessionScriptTrapOutcome.Invalid, timedCaster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, false).Outcome, "slot 1 holds a spell: the timed form");
        var (owner, caster) = Session();
        using var __ = owner;
        Assert.AreEqual(SessionScriptTrapOutcome.Unknown, caster.UseScriptTrapDevice("kit:9", Native, TrapClass.Nonmagical, Spot, null, false).Outcome);
        var weapon = caster.UseScriptTrapDevice(Dagger, Native, TrapClass.Nonmagical, Spot, null, false);
        Assert.AreEqual((SessionScriptTrapOutcome.Refused, GameSessionFailure.InventoryMismatch), (weapon.Outcome, weapon.Failure), "the device is a generic item");
        Assert.AreEqual(0, timed.Version.Revision + owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryCreatedObjects(owner.QueryActor().Map).Count);
    }

    [TestMethod]
    public void The_session_refuses_a_bad_form_a_bad_script_a_stale_version_and_a_bad_cell_before_any_effect()
    {
        var (owner, _) = Session();
        using var _ = owner;
        var map = owner.QueryActor().Map;
        var v = owner.Version;
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.PlaceScriptTrapDevice(v with { Revision = 5 }, Device, map, new(1, 1), null, Native, 26001).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(1, 1), null, Native, 0).Failure, "neither a target nor a marker");
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(1, 1), "oid:static:x", Native, 26001).Failure, "both");
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(1, 1), "oid:static:x", new(0, 0, 0), 0).Failure, "script 0 is nothing to run");
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(1, 1), "oid:static:x", new(100_000, 0, 0), 0).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(-1, 1), null, Native, 26001).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidTrap, owner.PlaceScriptTrapDevice(v, Device, map, new(1, 1), Device, Native, 0).Failure, "the device is not its own target");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.PlaceScriptTrapDevice(v, "", map, new(1, 1), null, Native, 26001).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PlaceScriptTrapDevice(v, "kit:9", map, new(1, 1), null, Native, 26001).Failure);
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.IsFalse(owner.IsItemDestroyed(Device));
        Assert.AreEqual(0, owner.QueryCreatedObjects(map).Count);
    }

    [TestMethod]
    public void An_armed_marker_and_a_target_survive_a_save_and_a_restore()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, false).Marker!;
        var (second, secondCaster) = Session();
        using var __ = second;
        secondCaster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, Chest, false);
        foreach (var session in new[] { owner, second })
        {
            var text = GameSessionSnapshotCodec.Encode(session.Capture());
            using var restored = Restore(Decode(text));
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
            var entity = session == owner ? marker.Entity : Chest.Entity;
            Assert.AreEqual(Native, restored.QueryPlacedTrapScript(restored.QueryActor().Map, entity));
            Assert.AreEqual(session == owner ? 1 : 0, restored.QueryScriptTrapMarkers(restored.QueryActor().Map, new(9, 7)).Count);
            Assert.IsTrue(restored.IsItemDestroyed(Device));
        }
    }

    [TestMethod]
    public void Disarming_a_placed_trap_gives_the_players_device_and_a_re_armed_object_forgets_the_removal()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, false).Marker!;
        var trapped = new SessionTrapped(marker.Map, marker.Entity);
        var baseline = MarkerBaseline(owner, marker);
        var disarm = owner.DisarmTrap(owner.Version, trapped, baseline, TrapObserver.Player, success: true, critical: true, disarmTraining: 2);
        Assert.AreEqual((TrapDisarm.RemovedWithDevice, TrapRules.PlacedDevice), (disarm.Outcome, disarm.DevicePrototype), "a PC-placed trap's device: 6047 (counters' top byte 0)");
        Assert.IsTrue(disarm.State.Removed);
        Assert.IsTrue(owner.QueryTrap(trapped, baseline).PlacedByPc, "the placed mark stays through the removal");
        // The chest's old trap was removed; a new device writes the slot again and the removal no longer holds.
        var (second, secondCaster) = Session();
        using var __ = second;
        var map = second.QueryActor().Map;
        var chest = new SessionTrapped(map, Chest.Entity);
        var old = new SessionTrapBaseline(TrapCarrier.TrappedObject, 30003, 0x1, 0x00120A0B, 0);
        Assert.AreEqual(GameSessionDisposition.Changed, second.MarkTrapSpotted(second.Version, chest, old, TrapObserver.Player).Disposition, "the old trap is spotted first: a disarmer must know it");
        Assert.AreEqual(GameSessionDisposition.Changed, second.DisarmTrap(second.Version, chest, old, TrapObserver.Player, true, false, 0).Mutation.Disposition);
        Assert.IsTrue(second.QueryTrap(chest, old).Removed);
        Assert.AreEqual(SessionScriptTrapOutcome.Armed, secondCaster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, Chest, false).Outcome);
        Assert.IsFalse(second.QueryTrap(chest, old).Removed);
        Assert.IsTrue(second.QueryTrap(chest, old).Trapped);
    }

    [TestMethod]
    public void A_disarmed_marker_frees_its_tile_for_the_next_device()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var marker = caster.UseScriptTrapDevice(Device, Native, TrapClass.Nonmagical, Spot, null, false).Marker!;
        var refused = caster.UseScriptTrapDevice(Second, Native, TrapClass.Nonmagical, Spot, null, false);
        Assert.AreEqual(SessionScriptTrapOutcome.TileRefused, refused.Outcome);
        var disarm = owner.DisarmTrap(owner.Version, new(marker.Map, marker.Entity), MarkerBaseline(owner, marker), TrapObserver.Player, success: true, critical: false, disarmTraining: 0);
        Assert.AreEqual(TrapDisarm.Removed, disarm.Outcome);
        Assert.AreEqual(0, owner.QueryScriptTrapMarkers(marker.Map, new(9, 7)).Count, "a removed trap object is destroyed");
        Assert.AreEqual(SessionScriptTrapOutcome.Armed, caster.UseScriptTrapDevice(Second, Native, TrapClass.Nonmagical, Spot, null, false).Outcome);
    }

    [TestMethod]
    public void A_full_created_object_budget_refuses_a_marker_before_any_effect_and_keeps_the_device()
    {
        var (owner, _) = Session();
        using var _ = owner;
        var map = owner.QueryActor().Map;
        for (var i = 0; i < SessionCreatedObject.MaxObjects; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 4047, map, new(1, 1)).Disposition);
        var revision = owner.Version.Revision;
        var full = owner.PlaceScriptTrapDevice(owner.Version, Device, map, new(2, 2), null, Native, 26001);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (full.Disposition, full.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsFalse(owner.IsItemDestroyed(Device));
        Assert.IsNull(owner.QueryPlacedTrapScript(map, "scenery:1"));
    }
}
