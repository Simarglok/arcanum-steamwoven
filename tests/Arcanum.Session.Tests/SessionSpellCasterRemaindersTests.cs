using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1146 (ledger RM1–RM8; exe read 0x452F10, 0x454920, 0x452E40, 0x459290, 0x4593F0): the EnvFlag stub, the Recharge component, Trait64 and the spell stop on an object through the cast host. The half-elf fixture's kit:2 is a held item
// (a coat of object type 5 here). Records, ids and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterRemaindersTests
{
    private const string Hero = SessionSpellCaster.Hero, Coat = "kit:2", Rat = "oid:rat", Box = "row:Mobile:7", BoxScriptEntity = "oid:box";
    private static readonly SpellLocation HeroTile = new(5, 5), FarTile = new(8, 5);
    private static readonly SpellArea Aim = new(0x4, 0, 0, 0, -1);
    private const int EnvNumber = 120, RechargeNumber = 121, TeleportTileNumber = 122, FleeNumber = 123, BadTraitNumber = 124, MaxedNumber = 125, StopNumber = 126, OtherStopNumber = 127;

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            id == Hero ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints)
            : id == Box ? new(id, 3, FarTile, HitPoints: 10) : null;
        // ARC-SCRIPT-1576: a script names the box by its own entity, the cast host holds it under another id.
        public string CastIdOfScriptEntity(string entity) => entity == BoxScriptEntity ? Box : entity;
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

    private static SessionSpellRecord Spell(int number, SpellComponentType type, SpellComponentData? data, SpellAction action = SpellAction.Begin) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral, components: [new(action, type, Aim, 0, 0, data)])
            with { Cost = 0, MaintainCost = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[]
    {
        Spell(EnvNumber, SpellComponentType.EnvFlag, new EnvironmentFlagData(1u << 4, true)),
        Spell(RechargeNumber, SpellComponentType.Recharge, new RechargeData(5, 9999)),
        Spell(MaxedNumber, SpellComponentType.Recharge, new RechargeData(5, 10)),
        Spell(TeleportTileNumber, SpellComponentType.Trait64, new Trait64Data(242, 0)),
        Spell(FleeNumber, SpellComponentType.Trait64, new Trait64Data(230, 1)),
        Spell(BadTraitNumber, SpellComponentType.Trait64, new Trait64Data(230, 0)),
        Spell(StopNumber, SpellComponentType.NoOp, null),
        Spell(OtherStopNumber, SpellComponentType.NoOp, null),
    }.ToDictionary(r => r.Data.Number);

    // The coat is an item of type 5 with 3 charges of its own (and no spells); the host knows it by its id.
    private static SessionItemSpells CoatSpells(string id, int charges) => new(id, Hero, 5, [10000, 10000, 10000, 10000, 10000], charges, 0);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int coatCharges = 3)
    {
        var owner = CreateWith(HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner), new Events(), id => id == Coat ? CoatSpells(id, coatCharges) : null));
    }

    // A run that never began (Apply takes the component directly): parent the hero, the target given by the test.
    private static SpellRun Run(int spell, string? parent = Hero) => new(1, spell, SpellRunAction.Begin, parent is null ? null : new(parent, SpellObjectFacts.PcType), parent is null ? null : new(parent, SpellObjectFacts.PcType),
        HeroTile, null, FarTile, null, SpellRunFlags.Active);

    private static SpellRunTarget At(string? id, int type) => new(id is null ? null : new(id, type), FarTile);

    private static bool NotReady(SessionSpellCaster caster) => caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady);

    [TestMethod]
    public void The_environment_flag_component_is_the_originals_stub_and_does_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(EnvNumber), 0, At(Hero, SpellTargetObject.Pc)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(EnvNumber), 0, new(null, FarTile)));
        Assert.AreEqual(0, caster.Log.Count, "taken, not 'not ready'");
        Assert.AreEqual(version, owner.Version);
    }

    [TestMethod]
    public void Recharge_adds_the_amount_to_an_items_charges_up_to_the_maximum()
    {
        var (owner, caster) = Session(coatCharges: 3);
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RechargeNumber), 0, At(Coat, 5)));
        Assert.AreEqual(8, owner.QueryItemCharges(Coat), "3 + 5, the session keeps the count");
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.ItemRecharged, 1, 8, Coat, 5) }, caster.Log.ToArray());
        // The maximum clips the sum (0x454920: the smaller of charges + amount and the maximum), and charges at the maximum are left alone.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MaxedNumber), 0, At(Coat, 5)));
        Assert.AreEqual(10, owner.QueryItemCharges(Coat));
        var full = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MaxedNumber), 0, At(Coat, 5)));
        Assert.AreEqual(full, owner.Version, "already at the maximum: nothing changes");
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ItemRecharged), "no line for the unchanged one");
        // The count is saved with the session.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(10, restored.QueryItemCharges(Coat));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Recharge_leaves_a_critter_a_tile_and_an_item_the_host_does_not_know()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        // A critter, a container and a tile are no items (types 5..14 only): the original answers 0 and the component does nothing.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RechargeNumber), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RechargeNumber), 0, At(Box, SpellTargetObject.Container)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RechargeNumber), 0, new(null, FarTile)));
        Assert.AreEqual(0, caster.Log.Count);
        Assert.AreEqual(version, owner.Version);
        // An item of the right type that the host cannot read is not ready.
        Assert.IsNull(caster.Apply(Run(RechargeNumber), 0, At("kit:99", 5)).JumpTo);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Recharge));
    }

    // ARC-MAGIC-2443: the original's handler writes field 105 of any item target, so an item the session does not track (no seed lists it) is recharged from the host's own field.
    [TestMethod]
    public void Recharge_of_an_item_the_session_does_not_track_is_written()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var cast = new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner), new Events(), id => id == "kit:77" ? CoatSpells(id, 3) : null);
        Assert.AreEqual(SpellComponentOutcome.None, cast.Apply(Run(RechargeNumber), 0, At("kit:77", 5)));
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.ItemRecharged, 1, 8, "kit:77", 5) }, cast.Log.ToArray());
        Assert.AreEqual(8, owner.QueryItemCharges("kit:77"), "the count went up from the host's own field");
    }

    [TestMethod]
    public void Recharge_of_a_deleted_item_is_still_logged_not_saved()
    {
        var (owner, _) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, Coat, new(15, 5, 0, false)).Disposition);
        var cast = new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner), new Events(), id => id == Coat ? CoatSpells(id, 3) : null);
        cast.Apply(Run(RechargeNumber), 0, At(Coat, 5));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ItemRechargeNotSaved, 1, (int)GameSessionFailure.ItemNotAvailable, Coat), cast.Log.Single());
    }

    [TestMethod]
    public void Trait64_stores_the_target_tile_and_the_current_map_in_an_npcs_teleport_fields()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsNull(owner.QueryTeleportTile(Rat));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(TeleportTileNumber), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual((8u, 5u), owner.QueryTeleportTile(Rat));
        Assert.AreEqual(owner.QueryActor().Map.RuntimeMap, owner.QueryTeleportMap(Rat), "field 243 gets the current map");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.Trait64Written, 1, 242, Rat, 0) }, caster.Log.ToArray());
        // The same again changes nothing but still tells it, and the pair is saved.
        var written = owner.Version;
        caster.Apply(Run(TeleportTileNumber), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(written, owner.Version);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.Trait64Written), "the unchanged write is told too");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual((8u, 5u), restored.QueryTeleportTile(Rat));
        Assert.AreEqual(owner.QueryTeleportMap(Rat), restored.QueryTeleportMap(Rat));
    }

    [TestMethod]
    public void Trait64_writes_the_run_parent_as_the_one_an_npc_flees_from_and_clears_it_without_a_parent()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsNull(owner.QueryDanger(Rat).FleeingFrom);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(FleeNumber), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(Rat).FleeingFrom);
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.Trait64Written, 1, 230, Rat, 1) }, caster.Log.ToArray());
        // A run without a parent writes the null handle: the field clears.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(FleeNumber, parent: null), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.IsNull(owner.QueryDanger(Rat).FleeingFrom);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.IsNull(restored.QueryDanger(Rat).FleeingFrom);
    }

    [TestMethod]
    public void Trait64_that_the_host_does_not_model_is_not_ready_and_a_missing_target_does_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(TeleportTileNumber), 0, new(null, FarTile)), "no target object");
        Assert.AreEqual(0, caster.Log.Count);
        // The hero, an item, and a field and source pair this host has no state for.
        Assert.IsNull(caster.Apply(Run(TeleportTileNumber), 0, At(Hero, SpellTargetObject.Pc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(TeleportTileNumber), 0, At(Coat, 5)).JumpTo);
        Assert.IsNull(caster.Apply(Run(BadTraitNumber), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, "Target_Tile into the fleeing-from field");
        Assert.AreEqual(3, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Trait64));
        Assert.AreEqual(version, owner.Version);
    }

    private static SpellRun Started(GameSessionOwner owner, SessionSpellCaster caster, int number, string target)
    {
        var record = Records[number];
        var me = caster.Facts(Hero)!;
        var aim = caster.Facts(target) ?? me;
        var started = owner.StartSpellRun(owner.Version, new(new(number, me, me, aim, aim.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return owner.QuerySpellRuns()[started.Creation!.Run!.Id]!;
    }

    [TestMethod]
    public void The_spell_stop_finds_the_first_active_run_of_the_spell_on_the_object_and_interrupts_it()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var first = Started(owner, caster, StopNumber, Hero);
        var second = Started(owner, caster, StopNumber, Hero);
        var other = Started(owner, caster, OtherStopNumber, Hero);
        Assert.AreEqual(first.Id, caster.FindRunOnObject(StopNumber, Hero)?.Id, "slot order: the first one");
        Assert.AreEqual(other.Id, caster.FindRunOnObject(OtherStopNumber, Hero)?.Id);
        Assert.IsNull(caster.FindRunOnObject(StopNumber, Rat), "no run on that object");
        Assert.IsNull(caster.FindRunOnObject(999, Hero), "no run of that spell");
        Assert.IsTrue(caster.StopSpellOnObject(StopNumber, Hero));
        Assert.IsNull(owner.QuerySpellRuns()[first.Id], "the interrupted run ended");
        Assert.IsNotNull(owner.QuerySpellRuns()[second.Id], "only the first is stopped");
        Assert.IsNotNull(owner.QuerySpellRuns()[other.Id]);
        Assert.AreEqual(second.Id, caster.FindRunOnObject(StopNumber, Hero)?.Id);
        Assert.IsTrue(caster.StopSpellOnObject(StopNumber, Hero));
        Assert.IsFalse(caster.StopSpellOnObject(StopNumber, Hero), "none left: the answer is 0");
        Assert.IsFalse(caster.StopSpellOnObject(StopNumber, Rat));
    }

    // ARC-SCRIPT-1576 (RM5/RM6): the script host's name of a scenery object is not always the cast host's id; the stop spell looks the runs up by the cast id.
    [TestMethod]
    public void The_spell_stop_of_a_script_finds_a_run_on_a_scenery_object_by_its_cast_id()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(Hero, caster.CastIdOf(Hero), "the PC passes through");
        Assert.AreEqual(Rat, caster.CastIdOf(Rat), "an entity the map names alike passes through");
        Assert.AreEqual(Box, caster.CastIdOf(BoxScriptEntity), "the script's name of the scenery is translated");
        var run = Started(owner, caster, StopNumber, Box);
        Assert.IsNull(caster.FindRunOnObject(StopNumber, BoxScriptEntity), "the script's own name finds no run: the host holds the cast id");
        Assert.AreEqual(run.Id, caster.FindRunOnObject(StopNumber, caster.CastIdOf(BoxScriptEntity))?.Id);
        Assert.IsTrue(caster.StopSpellOnObject(StopNumber, caster.CastIdOf(BoxScriptEntity)));
        Assert.IsNull(owner.QuerySpellRuns()[run.Id], "the run on the scenery ended");
    }
}
