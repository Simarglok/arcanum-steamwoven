using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-036 (MA-5, ledger MV1–MV3; 0x4524C0 read for this slice): the Movement component's type 0 (the run's target tile) of Spatial Distortion. The cast host logs the relocation request the host carries out;
// a resisted run, a target tile of (0, 0) and a missing target object move nothing; the other types and an object the host cannot move are not ready. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterMovementTests
{
    private const string Npc = "oid:mover", Stranger = "oid:stranger";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), NpcTile = new(6, 5), Aimed = new(9, 7);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, NpcTile, HitPoints: 5),
            Stranger => new(id, SpellTargetObject.Scenery, NpcTile, HitPoints: 5),
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

    private static SessionSpellRecord Spell(int location)
    {
        var area = new SpellArea(0x1, 0, 0, 0, -1);
        return SessionSpellRecords.Translate(Record(number: 3, beginTargets: 0x1, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.Movement, area, 0, 0, new MovementData(location, location == SpellComponentTables.RandomTileInRadius ? 3 : null))]));
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionSpellRecord Record) Session(int location = SpellComponentTables.TargetTile)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var record = Spell(location);
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(), critterFlags: 0);
        var critter = new SpellCastCritter(SessionCritterCombat.From(Npc, fields).Combat!, 0, 0, 0, 0, 0);
        return (owner, new SessionSpellCaster(owner, n => n == 3 ? record : null, new Map(owner, critter), new Events()), record);
    }

    // The component on its target as a run's BEGIN applies it: the hero the source and parent, the aimed tile the run's target tile.
    private static SpellComponentOutcome? Apply(SessionSpellCaster caster, string target, SpellLocation tile, int margin = 0, bool noObject = false)
    {
        var hero = caster.RunObject(Hero)!;
        var to = caster.RunObject(target)!;
        var run = new SpellRun(0, 3, SpellRunAction.Begin, hero, hero, HeroTile, to, tile, null, SpellRunFlags.Active) with { Margin = margin };
        return caster.Apply(run, 0, new(noObject ? null : to, tile));
    }

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void The_target_tile_type_asks_the_host_to_relocate_the_hero_along_the_path_to_the_runs_target_tile()
    {
        var (owner, caster, _) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Movement, 0, 9, Hero, 7), caster.Log.Single(), "the tile's x in the value, y in the detail, the target object the critter that moves");
    }

    [TestMethod]
    public void It_moves_a_critter_the_host_knows_too()
    {
        var (owner, caster, _) = Session();
        using var _ = owner;
        Apply(caster, Npc, Aimed);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Movement, 0, 9, Npc, 7), caster.Log.Single(), Trail(caster));
    }

    [TestMethod]
    public void A_resisted_run_a_zero_tile_and_a_missing_target_object_move_nothing_and_are_not_not_ready()
    {
        var (owner, caster, _) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed, margin: 2));
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, new(0, 0)));
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed, noObject: true));
        Assert.AreEqual(0, caster.Log.Count, Trail(caster));
        // A tile with one zero coordinate is a tile (the original tests the pair).
        Apply(caster, Hero, new(0, 4));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Movement, 0, 0, Hero, 4), caster.Log.Single());
    }

    [TestMethod]
    public void The_other_move_locations_and_an_object_the_host_cannot_move_are_not_ready()
    {
        // ARC-MAGIC-038: the types 2 and 3 are the teleport host's: without one they are not ready.
        foreach (var location in new[] { SpellComponentTables.TeleportTile, SpellComponentTables.TeleportAreaPortal })
        {
            var (owner, caster, _) = Session(location);
            using var _ = owner;
            Apply(caster, Hero, Aimed);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, 0, (int)SpellComponentType.Movement, Hero), caster.Log.Single(), Trail(caster));
        }
        var (stranger, strangerCaster, _) = Session();
        using var _s = stranger;
        Apply(strangerCaster, Stranger, Aimed);
        Assert.IsFalse(strangerCaster.Log.Any(e => e.Kind == SpellCastEventKind.Movement), "a scenery is no critter the host moves");
        Assert.IsTrue(strangerCaster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Movement), Trail(strangerCaster));
    }

    // ---- ARC-MAGIC-038 (MA-5 second half, ledger TP1–TP5): the teleport types ----

    private sealed class Teleport : ISpellTeleportHost
    {
        public bool Allow = true;
        public SpellPortalPlan Plan = new(3, true, new(700, 800));
        public readonly List<string> Asked = [];
        public bool Allowed(string subject, SpellLocation? target) { Asked.Add(subject); return Allow; }
        public SpellPortalPlan Portal(string subject) { Asked.Add("portal:" + subject); return Plan; }
    }

    private static SpellComponentOutcome? ApplyTeleport(SessionSpellCaster caster, string target, int spell = 3, int margin = 0, bool noParent = false, bool noObject = false)
    {
        var hero = caster.RunObject(Hero)!;
        var to = caster.RunObject(target)!;
        var run = new SpellRun(0, spell, SpellRunAction.Begin, hero, noParent ? null : hero, HeroTile, to, new(0, 0), null, SpellRunFlags.Active) with { Margin = margin };
        return caster.Apply(run, 0, new(noObject ? null : to, new(0, 0)));
    }

    [TestMethod]
    public void The_area_portal_sends_an_object_to_the_start_map_at_its_areas_location_through_the_host()
    {
        var (owner, caster, _) = Session(SpellComponentTables.TeleportAreaPortal);
        using var _ = owner;
        var host = new Teleport();
        caster.Teleport = host;
        Assert.AreEqual(SpellComponentOutcome.None, ApplyTeleport(caster, Hero));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TeleportPortal, -1, 700, Hero, 800), caster.Log.Single(), Trail(caster));
        Assert.AreEqual("portal:" + Hero, host.Asked.Single());
    }

    [TestMethod]
    public void The_area_portal_with_no_area_or_a_refusal_shows_message_10000_to_the_local_player_only()
    {
        var (owner, caster, _) = Session(SpellComponentTables.TeleportAreaPortal);
        using var _ = owner;
        var host = new Teleport { Plan = new(0, true, default) };
        caster.Teleport = host;
        ApplyTeleport(caster, Hero);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Message, -1, SessionSpellCaster.TeleportRefusedMessage), caster.Log.Single(), "no area: the local player is told");
        host.Plan = new(3, false, new(700, 800));
        ApplyTeleport(caster, Hero);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.Message && e.Value == 10000), "the test refuses");
        var before = caster.Log.Count;
        ApplyTeleport(caster, Npc);
        Assert.AreEqual(before, caster.Log.Count, "an NPC is told nothing and nothing happens: " + Trail(caster));
    }

    [TestMethod]
    public void The_teleports_do_nothing_for_a_resisted_run_or_without_a_target_object()
    {
        foreach (var location in new[] { SpellComponentTables.TeleportAreaPortal, SpellComponentTables.TeleportTile })
        {
            var (owner, caster, _) = Session(location);
            using var _ = owner;
            caster.Teleport = new Teleport();
            Assert.AreEqual(SpellComponentOutcome.None, ApplyTeleport(caster, Hero, margin: 3));
            Assert.AreEqual(SpellComponentOutcome.None, ApplyTeleport(caster, Hero, noObject: true));
            Assert.AreEqual(0, caster.Log.Count, Trail(caster));
        }
    }

    [TestMethod]
    public void The_teleport_tile_tests_the_runs_parent_then_opens_the_world_maps_teleport_mode()
    {
        var (owner, caster, _) = Session(SpellComponentTables.TeleportTile);
        using var _ = owner;
        var host = new Teleport();
        caster.Teleport = host;
        Assert.AreEqual(SpellComponentOutcome.None, ApplyTeleport(caster, Hero));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TeleportMap, 0, 3, Hero), caster.Log.Single(), "the source and the spell for the world map; nothing moves here");
        Assert.AreEqual(Hero, host.Asked.Single());
    }

    [TestMethod]
    public void The_teleport_tile_refused_by_the_test_shows_message_10000_whoever_the_parent_is()
    {
        var (owner, caster, _) = Session(SpellComponentTables.TeleportTile);
        using var _ = owner;
        caster.Teleport = new Teleport { Allow = false };
        ApplyTeleport(caster, Hero);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Message, 0, 10000), caster.Log.Single());
        // A run with no parent has no handle to test: refused as well (0x4BDD30 refuses a zero handle).
        var (second, other, _) = Session(SpellComponentTables.TeleportTile);
        using var _2 = second;
        other.Teleport = new Teleport();
        ApplyTeleport(other, Hero, noParent: true);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Message, 0, 10000), other.Log.Single(), Trail(other));
    }

    [TestMethod]
    public void The_teleport_cost_is_paid_when_the_world_map_confirms_and_only_for_the_local_player()
    {
        var (owner, caster, _) = Session(SpellComponentTables.TeleportTile);
        using var _ = owner;
        Assert.IsTrue(caster.PayTeleportCost(Npc, 3), "another payer is not charged here");
        Assert.AreEqual(0, caster.Log.Count);
        Assert.IsTrue(caster.PayTeleportCost(Hero, 3), Trail(caster));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.FatiguePaid && e.Object == Hero), Trail(caster));
    }

    [TestMethod]
    public void The_random_tile_type_draws_the_direction_and_asks_the_host_to_walk_the_radius()
    {
        var (owner, caster, _) = Session(SpellComponentTables.RandomTileInRadius);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed));
        var entry = caster.Log.Single();
        Assert.AreEqual((SpellCastEventKind.MovementRandom, 0, 3, Hero), (entry.Kind, entry.Run, entry.Value, entry.Object), "the radius of the component's Tile_Radius: in the value, the run's object");
        Assert.IsTrue(entry.Detail is >= 0 and <= 8, $"the one draw of 0x4CEF00 over 0..8, got {entry.Detail}");
        // The type ignores the run's target tile: a (0, 0) tile moves it all the same.
        Apply(caster, Npc, new(0, 0));
        Assert.AreEqual(SpellCastEventKind.MovementRandom, caster.Log[^1].Kind);
    }

    [TestMethod]
    public void The_random_tile_type_of_a_resisted_run_or_without_a_target_object_does_nothing_and_a_scenery_is_not_ready()
    {
        var (owner, caster, _) = Session(SpellComponentTables.RandomTileInRadius);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed, margin: 1));
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, Aimed, noObject: true));
        Assert.AreEqual(0, caster.Log.Count, Trail(caster));
        Apply(caster, Stranger, Aimed);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Movement), Trail(caster));
    }
}
