using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-WORLD-107: the native trap effects through the cast host over a synthetic map (the hero, an NPC, a trap object, a box and a
// shooter): victims, the damage step, the damage source, the shot's shooter search and the spells with no caster (ledger TT11–TT14).
// Scripts, counters and records are invented within the original's ranges.
[TestClass]
public sealed class TrapEffectTests
{
    private const string Hero = SessionSpellCaster.Hero, Guard = "oid:guard", Trap = "oid:trap", Box = "oid:box";
    private static readonly SpellLocation HeroTile = new(5, 5), GuardTile = new(6, 5), TrapTile = new(5, 6);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter guard) : ISpellCastMap
    {
        public List<(SpellLocation Centre, int Radius, int Mask)> Squares { get; } = [];

        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Guard => new(id, SpellTargetObject.Npc, GuardTile, HitPoints: 20),
            Trap => new(id, 17, TrapTile),
            Box => new(id, SpellTargetObject.Scenery, new(4, 6)),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask)
        {
            Squares.Add((centre, radius, typeMask));
            return [Object(Guard)!, Object(Hero)!];
        }
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity == Guard ? guard : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) =>
            id == Box ? (new Combatant(), AttackTargetKind.OtherObject) : null;
        public List<(string Id, AttackDamage Damage)> ObjectDamage { get; } = [];
        public bool DamageObject(string id, AttackDamage damage)
        {
            ObjectDamage.Add((id, damage));
            return true;
        }
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // A friendly spell 90 at any critter (BEGIN targets 0x40) that only deals 1 normal damage when it runs.
    private static SessionSpellRecord Spell() => SessionSpellRecords.Translate(Record(number: 90, info: SpellInfo.Friendly,
        components: [Damage(SpellAction.Begin, 0, 1, 1, 0)]) with
    {
        Cost = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
        DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0,
    });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Map Map) Session(uint guardFlags2 = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(), critterFlags: 0, flags2: guardFlags2);
        var map = new Map(owner, new(SessionCritterCombat.From(Guard, fields).Combat!, 0, 0, 0, 0, 0));
        var record = Spell();
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, map, new Events()), map);
    }

    private static int Counters(int spell, int tag, int second, int low) => unchecked((int)((uint)spell << 24 | (uint)tag << 16 | (uint)second << 8 | (uint)low));

    private static SessionTrapFiring Firing(int script, int counters, uint headerFlags, string? triggerer = Hero, int objectFlags = 0) =>
        new(Trap, triggerer, TrapRules.NativeRun(script, TrapRules.NonmagicalFlag | headerFlags)!.Value, counters, objectFlags);

    private static int HeroHitPoints(GameSessionOwner owner) => owner.QueryHeroLife()!.Status.HitPoints;

    [TestMethod]
    public void A_damage_trap_hits_every_critter_in_its_square_with_one_draw_each_and_no_kill_credit()
    {
        var (owner, caster, map) = Session();
        using var _ = owner;
        var before = HeroHitPoints(owner);
        var draws = owner.QueryRandom().Draws;
        var result = caster.ApplyTrapEffect(Firing(30001, Counters(0, 0, 6, 2), TrapRules.SquareTwoFlag));
        // The square is the trapped object's (0x4AE4E0) with half side 2 and the PC and NPC type bits.
        CollectionAssert.AreEqual(new[] { (TrapTile, 2, (1 << 15) | (1 << 16)) }, map.Squares);
        Assert.AreEqual(TrapEffectKind.Damage, result.Effect!.Value.Kind);
        Assert.IsNull(result.Killer, "a trap the PC did not place credits no one");
        CollectionAssert.AreEqual(new[] { Guard, Hero }, result.Victims.Select(v => v.Entity).ToArray(), "the map's order");
        Assert.AreEqual(draws + 2, owner.QueryRandom().Draws, "one draw per victim");
        var hero = result.Victims[1];
        Assert.IsTrue(hero.Amount is >= 2 and <= 6);
        Assert.AreEqual((SessionTrapHit.Damaged, hero.Amount, hero.Amount), (hero.Hit, hero.HitPointDamage, before - HeroHitPoints(owner)));
        Assert.AreEqual(new DamageByType(hero.Amount, 0, 0, 0, 0), hero.Step!.Rolled, "normal damage only, no multiplier");
        Assert.AreEqual((SpellCastAttemptOutcome.Nothing, (int?)null), (hero.Spell, hero.SpellRun), "top byte 0: no spell");
        var guard = result.Victims[0];
        Assert.AreEqual((SessionTrapHit.Damaged, guard.Amount), (guard.Hit, guard.HitPointDamage), "the NPC is hit too");
    }

    [TestMethod]
    public void Each_damage_script_fills_its_own_damage_slot()
    {
        foreach (var (script, type) in new[] { (30003, DamageType.Normal), (30004, DamageType.Fire), (30005, DamageType.Electrical), (30006, DamageType.Poison) })
        {
            var (owner, caster, _) = Session();
            using var __ = owner;
            var hit = caster.ApplyTrapEffect(Firing(script, Counters(0, 0, 4, 4), 0)).Victims.Single();
            Assert.AreEqual(DamageByType.Zero.With(type, 4), hit.Step!.Rolled, $"{script}");
        }
    }

    [TestMethod]
    public void A_radius_0_trap_hits_the_triggerer_alone_and_none_without_one_and_an_object_takes_it_through_the_map()
    {
        var (owner, caster, map) = Session();
        using var _ = owner;
        Assert.AreEqual(0, caster.ApplyTrapEffect(Firing(30001, Counters(0, 0, 3, 3), 0, triggerer: null)).Victims.Count);
        var box = caster.ApplyTrapEffect(Firing(30001, Counters(0, 0, 3, 3), 0, triggerer: Box)).Victims.Single();
        Assert.AreEqual((Box, SessionTrapHit.Damaged), (box.Entity, box.Hit));
        Assert.AreEqual(Box, map.ObjectDamage.Single().Id);
        Assert.AreEqual(0, map.Squares.Count, "no square is listed for radius 0");
    }

    [TestMethod]
    public void A_pc_placed_trap_makes_the_player_the_damage_source()
    {
        var (owner, caster, _) = Session();
        using var __ = owner;
        var result = caster.ApplyTrapEffect(Firing(30001, Counters(0, 0, 1, 1), 0, objectFlags: TrapRules.PlacedByPcFlag));
        Assert.AreEqual(Hero, result.Killer);
        Assert.AreEqual(SessionTrapHit.Damaged, result.Victims.Single().Hit);
    }

    [TestMethod]
    public void A_spell_trap_casts_the_top_bytes_spell_with_no_caster_at_each_victim()
    {
        var (owner, caster, _) = Session();
        using var __ = owner;
        var draws = owner.QueryRandom().Draws;
        var hit = caster.ApplyTrapEffect(Firing(30000, Counters(90, 0, 50, 40), 0)).Victims.Single();
        Assert.AreEqual((SessionTrapHit.None, 0, (AttackDamage?)null), (hit.Hit, hit.Amount, hit.Step), "no damage and no draw of the low bytes");
        Assert.AreEqual(SpellCastAttemptOutcome.Started, hit.Spell);
        var run = owner.QuerySpellRuns()[hit.SpellRun!.Value]!;
        Assert.AreEqual((90, (SpellRunObject?)null, (SpellRunObject?)null, Hero), (run.Spell, run.Parent, run.Source, run.Target?.Id));
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "starting the run draws nothing");
        // An unknown spell (the top byte names no record) casts nothing.
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.ApplyTrapEffect(Firing(30000, Counters(91, 0, 0, 0), 0)).Victims.Single().Spell);
    }

    [TestMethod]
    public void A_damage_with_a_top_byte_casts_its_spell_after_the_damage()
    {
        var (owner, caster, _) = Session();
        using var __ = owner;
        var hit = caster.ApplyTrapEffect(Firing(30004, Counters(90, 0, 2, 2), 0)).Victims.Single();
        Assert.AreEqual((SessionTrapHit.Damaged, SpellCastAttemptOutcome.Started), (hit.Hit, hit.Spell));
        Assert.AreEqual(Hero, owner.QuerySpellRuns()[hit.SpellRun!.Value]!.Target?.Id);
    }

    [TestMethod]
    public void A_shot_needs_a_shooter_with_its_tag_and_without_one_does_nothing_at_all()
    {
        var (owner, caster, _) = Session();
        using var __ = owner;
        var draws = owner.QueryRandom().Draws;
        var none = caster.ApplyTrapEffect(Firing(30002, Counters(90, 5, 8, 2), 0) with { Shooters = [new(Box, 30007, Counters(0, 4, 0, 0))] });
        Assert.AreEqual((SessionTrapHit.NoShooter, SpellCastAttemptOutcome.Nothing, (string?)null),
            (none.Victims.Single().Hit, none.Victims.Single().Spell, none.Victims.Single().Shooter));
        var shot = caster.ApplyTrapEffect(Firing(30002, Counters(90, 5, 8, 2), 0) with { Shooters = [new(Box, 30007, Counters(0, 5, 0, 0))] })
            .Victims.Single();
        Assert.AreEqual((SessionTrapHit.ShotNotReady, Box, 0, SpellCastAttemptOutcome.Nothing), (shot.Hit, shot.Shooter, shot.Amount, shot.Spell),
            "the shot is not ready: neither its damage nor its spell is applied");
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "nothing drawn");
        Assert.AreEqual(0, owner.QuerySpellRuns().Runs.Count(), "no run started");
    }

    [TestMethod]
    public void A_nigh_invulnerable_critter_takes_no_trap_damage_as_the_trapped_object_is_no_uber_weapon()
    {
        var (owner, caster, _) = Session(guardFlags2: 0x200);
        using var __ = owner;
        var result = caster.ApplyTrapEffect(Firing(30001, Counters(0, 0, 4, 4), TrapRules.SquareOneFlag));
        var guard = result.Victims.Single(v => v.Entity == Guard);
        Assert.AreEqual((SessionTrapHit.Prevented, DamagePrevention.NighInvulnerable, 0), (guard.Hit, guard.Step!.Prevented, guard.HitPointDamage));
        Assert.AreEqual(SessionTrapHit.Damaged, result.Victims.Single(v => v.Entity == Hero).Hit, "the hero still takes it");
    }

    [TestMethod]
    public void The_shooters_script_30007_does_nothing()
    {
        var (owner, caster, map) = Session();
        using var _ = owner;
        Assert.AreSame(SessionTrapEffectResult.None, caster.ApplyTrapEffect(Firing(30007, Counters(90, 0, 9, 9), TrapRules.SquareFourFlag)));
        Assert.AreEqual(0, map.Squares.Count);
    }

    // ARC-DEV-144 (MIG13 S9c): the burning object's damage packet through the same damage step, no attacker party.
    [TestMethod]
    public void A_burn_takes_the_damage_step_of_its_packet_off_a_critter_and_hands_other_objects_to_the_map()
    {
        var (owner, caster, map) = Session();
        using var _ = owner;
        var before = HeroHitPoints(owner);
        var hero = caster.ApplyBurn(Hero, FireDamageRules.Damage(normal: false, doubled: false));
        Assert.IsTrue(hero.Applied);
        Assert.AreEqual(new DamageByType(0, 0, 0, 3, 0), hero.Step!.Rolled, "3 fire, no roll");
        Assert.AreEqual(before - HeroHitPoints(owner), hero.HitPointDamage);
        Assert.IsTrue(hero.HitPointDamage > 0);
        var doubled = caster.ApplyBurn(Guard, FireDamageRules.Damage(normal: true, doubled: true));
        Assert.IsTrue(doubled.Applied, "the NPC's life takes it too");
        Assert.AreEqual(new DamageByType(2, 0, 0, 0, 0), doubled.Step!.Rolled, "2 normal in a round");
        var box = caster.ApplyBurn(Box, FireDamageRules.Damage(false, false));
        Assert.IsTrue(box.Applied);
        Assert.AreEqual(Box, map.ObjectDamage.Single().Id, "a non-critter goes through the map");
        // A dead critter takes nothing (0x4B4784).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, caster.LifeTarget(Guard)!).Disposition);
        Assert.IsFalse(caster.ApplyBurn(Guard, FireDamageRules.Damage(false, false)).Applied);
    }
}
