using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-352 (E1-b; ARC-RES-101, SUM1-SUM16): the Summon component for scenery, the summoned list, the Destroy of a summoned object and the eye-candy callbacks (EC11), on invented records shaped
// like Fireflash (spell 22: the flash on an empty strike tile, destroyed at the callback) and Wall of Fire (spell 21: five pieces along the wall line, destroyed together at the END callback).
[TestClass]
public sealed class SessionSpellSummonTests
{
    private const int FlashSpell = 22, WallSpell = 21, FlashBasic = 4050, WallBasic = 4031, Flash = FlashBasic + 20, Piece = WallBasic + 20;
    private const string Npc = "npc-a", Wall = "row:wall";
    private const int NpcType = SpellTargetObject.Npc, Blocking = 0, NoBlock = SpellTargetObject.NoBlock;
    private const ulong Self = 0x1, Tile = 0x8000UL << 32, Empty = 0x40000UL << 32, EmptyImmobiles = 0x80000UL << 32, WallLine = 0x400000UL << 32, Summoned = 0x4000UL << 32;
    private static readonly SpellLocation HeroTile = new(5, 5), Strike = new(8, 5);

    // The hero, an NPC and a blocking wall object where a test puts them; the tiles are open; every direction is 7 (step +1, 0: the wall line runs along x).
    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellLocation? NpcAt { get; set; }
        public int NpcFlags { get; set; } = Blocking;
        public SpellLocation? WallAt { get; set; }
        public string? NpcLeader { get; set; }

        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc when NpcAt is { } at => new(id, NpcType, at, NpcFlags, HitPoints: 10, PcLeader: NpcLeader),
            Wall when WallAt is { } at => new(id, 0, at, Blocking, HitPoints: 10),
            _ => null,
        };
        private IEnumerable<SpellTargetObject> All() => new[] { Object(SessionSpellCaster.Hero), Object(Npc), Object(Wall) }.OfType<SpellTargetObject>();
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) =>
            All().Where(o => (typeMask & (1 << o.Type)) != 0 && Math.Max(Math.Abs((long)o.Location.X - centre.X), Math.Abs((long)o.Location.Y - centre.Y)) <= radius).ToArray();
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => All().Where(o => (typeMask & (1 << o.Type)) != 0 && o.Location == tile).ToArray();
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 7;
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

    private static SpellComponent Line(SpellAction action, SpellComponentType type, ulong area = 0, ulong apply = 0, SpellComponentData? data = null, int radius = 0) =>
        new(action, type, new(area, 0, 0, radius, -1), apply, 0, data);

    private static SpellRecord Shaped(int number, IReadOnlyList<SpellComponent> components, SpellArea[] areas, int maintainPeriod) =>
        Record(number: number, info: SpellInfo.Aggressive, components: components)
        with { Cost = 0, MaintainCost = 0, MaintainPeriod = maintainPeriod, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, NoResist = true, LinesPresent = [true, true, true, true, true], Areas = areas };

    // Fireflash's shape (SUM12): BEGIN on the strike tile: the Summon (an empty tile), then EyeCandy entry 2 Add with Play: Callback re-aimed to the summoned; CALLBACK: Destroy on Tgt_Summoned.
    private static SessionSpellRecord Fireflash() => SessionSpellRecords.Translate(Shaped(FlashSpell,
        [Line(SpellAction.Begin, SpellComponentType.Summon, Tile | Empty, data: new SummonData(FlashBasic, 0, 0, -1)),
         Line(SpellAction.Begin, SpellComponentType.EyeCandy, apply: Summoned, data: new EyeCandyData(2, true, SessionSpellCaster.PlayCallback)),
         Line(SpellAction.Callback, SpellComponentType.Destroy)],
        [new(Tile, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1), new(0, 0, 0, 0, -1)], maintainPeriod: 0));

    // Wall of Fire's shape (SUM13): BEGIN on the target tile and the wall line of radius 2: one Summon (no blocking immobile); maintained; END: EyeCandy entry 2 Add with Play: End_Callback on the
    // summoned (no art: not played); END_CALLBACK: Destroy on Tgt_Summoned.
    private static SessionSpellRecord WallOfFire() => SessionSpellRecords.Translate(Shaped(WallSpell,
        [Line(SpellAction.Begin, SpellComponentType.Summon, Tile | EmptyImmobiles, data: new SummonData(WallBasic, 0, 0, -1)),
         Line(SpellAction.End, SpellComponentType.EyeCandy, data: new EyeCandyData(2, true, SessionSpellCaster.PlayEndCallback)),
         Line(SpellAction.EndCallback, SpellComponentType.Destroy)],
        [new(Tile | WallLine, 0, 0, 2, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1)], maintainPeriod: 10));

    // The wall with SUM14's BEGIN eye candy on Tgt_Summoned (no callback) and an END eye candy that plays and calls the END callback back (a fade-out).
    private static SessionSpellRecord FadingWall() => SessionSpellRecords.Translate(Shaped(WallSpell,
        [Line(SpellAction.Begin, SpellComponentType.Summon, Tile | EmptyImmobiles, data: new SummonData(WallBasic, 0, 0, -1)),
         Line(SpellAction.Begin, SpellComponentType.EyeCandy, apply: Summoned, data: new EyeCandyData(2, true, 0)),
         Line(SpellAction.End, SpellComponentType.EyeCandy, data: new EyeCandyData(2, true, SessionSpellCaster.PlayEndCallback)),
         Line(SpellAction.EndCallback, SpellComponentType.Destroy)],
        [new(Tile | WallLine, 0, 0, 2, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1)], maintainPeriod: 10));

    private static SessionSummonPrototype? Prototypes(int file) => file switch
    {
        Flash => new(SpellTargetObject.Scenery, NoBlock | SpellTargetObject.Invulnerable),
        Piece => new(SpellTargetObject.Scenery, NoBlock | SpellTargetObject.Invulnerable, 0x10),
        _ => null,
    };

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Map Map, SessionSpellRecord Record) Setup(SessionSpellRecord record, bool plays)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var map = new Map(owner);
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, map, new Events())
        {
            SummonPrototypes = Prototypes, SummonMap = SessionFixtures.Exterior, EyeCandyPlays = _ => plays,
        };
        return (owner, caster, map, record);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, SpellLocation tile)
    {
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, null, tile), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Begin, run)).Disposition);
        return run;
    }

    private static IReadOnlyList<SessionCreatedObject> Made(GameSessionOwner owner) => owner.QueryCreatedObjects(SessionFixtures.Exterior);

    [TestMethod]
    public void Fireflash_on_an_empty_tile_makes_a_summoned_flash_that_waits_for_the_eye_candy_and_goes_at_its_callback()
    {
        var (owner, caster, _, record) = Setup(Fireflash(), plays: true);
        using var _ = owner;
        var run = Cast(owner, caster, record, Strike);
        var flash = Made(owner).Single();
        Assert.AreEqual((SessionCreatedKind.Scenery, Flash, 8, 5, 0, 0), (flash.Kind, flash.Prototype, flash.Cell.X, flash.Cell.Y, flash.OffsetX, flash.OffsetY), "the flash: basic 4050 + 20 at the strike tile");
        Assert.AreEqual((SessionCreatedObject.SummonedFlag, SessionSpellCaster.Hero, (int?)run), (flash.SpellFlags, flash.Summoner, flash.Run), "summoned, by the run's parent, on the run's list");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Object == flash.Entity && e.Value == SpellEyeCandyRules.EntryIndex(FlashSpell, 2)),
            "the eye candy is re-aimed to the summoned flash");
        Assert.IsNotNull(owner.QuerySpellRuns()[run], "the run waits for the animation's callback");
        Assert.IsNull(caster.EyeCandyCallback(run, 4), "an END callback is no answer for a run waiting in its BEGIN");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.EyeCandyCallback(run, 3)!.Disposition);
        Assert.AreEqual(0, Made(owner).Count, "the CALLBACK's Destroy took the flash away");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.SummonDestroyed && e.Object == flash.Entity));
        Assert.IsNull(owner.QuerySpellRuns()[run], "the run ended after its callback");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), "every line was done");
        Assert.IsNull(caster.EyeCandyCallback(run, 3), "a callback for a run that is gone does nothing");
    }

    [TestMethod]
    public void Fireflash_on_a_tile_a_critter_stands_on_makes_no_flash_and_the_run_ends_at_once()
    {
        var (owner, caster, map, record) = Setup(Fireflash(), plays: true);
        using var _ = owner;
        map.NpcAt = Strike;
        var run = Cast(owner, caster, record, Strike);
        Assert.AreEqual(0, Made(owner).Count, "Tgt_Tile_Empty: a standing critter makes the tile not empty (SUM9)");
        Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.Summoned or SpellCastEventKind.EyeCandyAdded), "nothing summoned, nothing to re-aim the eye candy to");
        Assert.IsNull(owner.QuerySpellRuns()[run], "no callback is pending: the run ends");
    }

    [TestMethod]
    public void A_critter_that_does_not_block_leaves_the_tile_empty_and_a_flash_whose_eye_candy_does_not_play_goes_at_once()
    {
        var (owner, caster, map, record) = Setup(Fireflash(), plays: false);
        using var _ = owner;
        map.NpcAt = Strike;
        map.NpcFlags = NoBlock;
        var run = Cast(owner, caster, record, Strike);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Summoned), "an object with OF_NO_BLOCK does not make the tile full");
        Assert.AreEqual(0, Made(owner).Count, "the eye candy did not play: the callback ran at once (EC11) and destroyed the flash");
        Assert.IsNull(owner.QuerySpellRuns()[run]);
    }

    [TestMethod]
    public void Wall_of_fire_makes_five_pieces_along_the_wall_line_skips_a_blocking_immobile_and_ends_with_all_of_them_at_once()
    {
        var (owner, caster, map, record) = Setup(WallOfFire(), plays: false);
        using var _ = owner;
        map.NpcAt = new(9, 5);
        var run = Cast(owner, caster, record, Strike);
        var pieces = Made(owner);
        Assert.AreEqual(5, pieces.Count, "one Summon on five list entries (SUM13); a critter does not stop a piece (Tgt_Tile_Empty_Immobiles)");
        CollectionAssert.AreEquivalent(new[] { 6, 7, 8, 9, 10 }, pieces.Select(p => p.Cell.X).ToArray());
        Assert.IsTrue(pieces.All(p => p.Prototype == Piece && p.Cell.Y == 5 && p.Run == run && p.Summoner == SessionSpellCaster.Hero && p.SpellFlags == SessionCreatedObject.SummonedFlag));
        Assert.AreEqual(SpellRunAction.Maintain, owner.QuerySpellRuns()[run]!.Action, "the wall is maintained");
        // The interrupt (0x457110) from MAINTAIN goes to END: its EyeCandy line does not play, the END callback runs at once and its Destroy takes every piece.
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Interrupt, run)).Disposition);
        Assert.AreEqual(0, Made(owner).Count, "all five pieces went together");
        Assert.AreEqual(5, caster.Log.Count(e => e.Kind == SpellCastEventKind.SummonDestroyed));
        Assert.IsNull(owner.QuerySpellRuns()[run]);

        var (second, secondCaster, secondMap, secondRecord) = Setup(WallOfFire(), plays: false);
        using var __ = second;
        secondMap.WallAt = new(10, 5);
        Cast(second, secondCaster, secondRecord, Strike);
        CollectionAssert.AreEquivalent(new[] { 6, 7, 8, 9 }, Made(second).Select(p => p.Cell.X).ToArray(), "a blocking wall object keeps a piece off its tile");
    }

    [TestMethod]
    public void A_freed_run_lets_go_of_its_summoned_objects_and_a_summon_that_is_not_a_scenery_is_not_ready()
    {
        var (owner, caster, _, record) = Setup(WallOfFire(), plays: false);
        using var _ = owner;
        var run = Cast(owner, caster, record, Strike);
        // Without an END callback the run ends without destroying anything (SUM7): free it directly; the pieces stay, no longer on its list.
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Free, run)).Disposition);
        Assert.IsNull(owner.QuerySpellRuns()[run]);
        Assert.AreEqual(5, Made(owner).Count, "the end of a run destroys nothing by itself");
        Assert.IsTrue(Made(owner).All(p => p.Run is null), "the freed slot's list is let go");

        var critter = SessionSpellRecords.Translate(Shaped(14, [Line(SpellAction.Begin, SpellComponentType.Summon, Tile | Empty, data: new SummonData(9999, 0, 0, -1))],
            [new(Tile, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)], maintainPeriod: 0));
        var (other, otherCaster, _, _) = Setup(critter, plays: false);
        using var ___ = other;
        Cast(other, otherCaster, critter, new(12, 5));
        Assert.AreEqual(0, Made(other).Count);
        Assert.IsTrue(otherCaster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Summon), "a prototype the host does not read as a scenery or a critter is not ready");
    }

    [TestMethod]
    public void A_burn_with_a_source_spares_it_and_adds_its_bonus()
    {
        var (owner, caster, map, _) = Setup(WallOfFire(), plays: false);
        using var _ = owner;
        Assert.AreEqual(new SessionBurnHit(false, 0, null, Spared: true), caster.ApplyBurn(SessionSpellCaster.Hero, FireDamageRules.Damage(false, false), "scenery:1", SessionSpellCaster.Hero),
            "the summoner is spared (GT94)");
        Assert.AreEqual(5, FireDamageRules.Damage(false, false, 2).Fire);
        Assert.AreEqual(10, FireDamageRules.Damage(false, true, 2).Fire, "the bonus is added before the round's doubling");
        Assert.AreEqual(0, FireDamageRules.Damage(false, false, -9).Fire, "never below 0");
        Assert.AreEqual(0, caster.BurnBonus(null));
        map.NpcAt = Strike;
        Assert.AreEqual(0, caster.BurnBonus(Npc), "an NPC with no stats has no bonus");
    }

    [TestMethod]
    public void A_refused_entry_re_aims_to_the_previous_piece_and_the_end_destroys_the_list_newest_first_after_a_played_end_callback()
    {
        var (owner, caster, map, record) = Setup(FadingWall(), plays: true);
        using var _ = owner;
        map.WallAt = new(10, 5);
        var run = Cast(owner, caster, record, Strike);
        var pieces = Made(owner);
        Assert.AreEqual("8,9,7,6", string.Join(",", pieces.Select(p => p.Cell.X)), "the wall gather's order: the centre, then one step each way, then two (TA12)");
        // SUM1: the summoned slot holds the last piece made; the refused entry (10,5) re-aims its eye candy to the piece of (7,5).
        var aimed = caster.Log.Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Run == run).Select(e => Made(owner).Single(p => p.Entity == e.Object).Cell.X);
        Assert.AreEqual("8,9,7,7,6", string.Join(",", aimed));
        Assert.IsNull(caster.AwaitedCallback(run), "a maintained run waits for nothing");
        // The interrupt reaches END; its eye candy plays with end_callback: the run waits at END for the animation.
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual(SpellRunAction.End, owner.QuerySpellRuns()[run]!.Action);
        Assert.AreEqual(4, caster.AwaitedCallback(run));
        Assert.AreEqual(4, Made(owner).Count, "nothing goes before the END callback");
        Assert.IsNull(caster.EyeCandyCallback(run, 3), "a CALLBACK is no answer at END");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.EyeCandyCallback(run, 4)!.Disposition);
        Assert.AreEqual(0, Made(owner).Count);
        Assert.AreEqual(string.Join(",", pieces.Reverse().Select(p => p.Entity)),
            string.Join(",", caster.Log.Where(e => e.Kind == SpellCastEventKind.SummonDestroyed).Select(e => e.Object)), "Tgt_Summoned walks the run's list newest first (SUM7)");
        Assert.IsNull(owner.QuerySpellRuns()[run]);
    }

    [TestMethod]
    public void A_flash_run_waiting_for_its_callback_says_so_and_a_pc_led_npc_is_spared_by_the_burn()
    {
        var (owner, caster, map, record) = Setup(Fireflash(), plays: true);
        using var _ = owner;
        var run = Cast(owner, caster, record, Strike);
        Assert.AreEqual(3, caster.AwaitedCallback(run), "a processed BEGIN with CALLBACK lines and no duration or maintenance can only be waiting");
        map.NpcAt = new(1, 1);
        map.NpcLeader = SessionSpellCaster.Hero;
        Assert.IsTrue(caster.ApplyBurn(Npc, FireDamageRules.Damage(false, false), "scenery:9", SessionSpellCaster.Hero).Spared, "an NPC led by the PC source is spared (0x45DDA0)");
        Assert.IsFalse(caster.ApplyBurn(Npc, FireDamageRules.Damage(false, false), "scenery:9", null).Spared, "without a source nobody is spared");
    }
}
