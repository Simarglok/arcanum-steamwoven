using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-040 (MA-8, ledger TR1–TR6; 0x452AD0, 0x452B00 and 0x452CD0 read for this slice): the Trait component of art type of Polymorph and the body spells. The cast host keeps the monster body the spell chose in
// the session and tells the host what the original does around it (the unwield of every slot, the elemental flags, the restore's best items). Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterTraitTests
{
    private const string Npc = "oid:shape", Stranger = "oid:stranger";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), NpcTile = new(6, 5);

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

    private static SessionSpellRecord Spell(int field, int value, int palette = 0)
    {
        var area = new SpellArea(0x4, 0, 0, 0, -1);
        return SessionSpellRecords.Translate(Record(number: 49, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.Trait, area, 0, 0, new TraitData(field, value, palette)),
                new(SpellAction.End, SpellComponentType.Trait, area, 0, 0, new TraitData(SessionSpellCaster.ArtNumberField, SessionSpellCaster.TraitRestoreValue, 0))]));
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(SessionSpellRecord? record = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(), critterFlags: 0);
        var critter = new SpellCastCritter(SessionCritterCombat.From(Npc, fields).Combat!, 0, 0, 0, 0, 0);
        record ??= Spell(SessionSpellCaster.ArtNumberField, 20);
        return (owner, new SessionSpellCaster(owner, n => n == 49 ? record : null, new Map(owner, critter), new Events()));
    }

    // The component on its target as a run's BEGIN applies it: the hero the source and parent.
    private static SpellComponentOutcome? Apply(SessionSpellCaster caster, string target, bool noObject = false, SpellRunAction action = SpellRunAction.Begin)
    {
        var hero = caster.RunObject(Hero)!;
        var to = caster.RunObject(target)!;
        var run = new SpellRun(0, 49, action, hero, hero, HeroTile, to, NpcTile, null, SpellRunFlags.Active);
        return caster.Apply(run, 0, new(noObject ? null : to, NpcTile));
    }

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void The_apply_keeps_the_species_and_palette_of_the_hero_and_tells_the_host()
    {
        var (owner, caster) = Session(Spell(SessionSpellCaster.ArtNumberField, 20, 2));
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero));
        Assert.AreEqual(new SessionTraitArt(20, 2), owner.QueryTraitArt(Hero));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitApplied, 0, 20, Hero, 2 | SessionSpellCaster.TraitStateChanged), caster.Log.Single(), Trail(caster));
    }

    [TestMethod]
    public void The_apply_works_on_an_npc_the_host_knows_and_the_second_apply_changes_no_state_but_still_asks_the_host_to_unwield()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Apply(caster, Npc);
        Apply(caster, Npc);
        Assert.AreEqual(new SessionTraitArt(20, 0), owner.QueryTraitArt(Npc));
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.TraitApplied), Trail(caster));
        Assert.AreEqual(SessionSpellCaster.TraitStateChanged, caster.Log[0].Detail);
        Assert.AreEqual(0, caster.Log[1].Detail, "the same body again changes no record, the unwield and the redraw still happen in the original");
    }

    [TestMethod]
    public void The_three_elemental_species_are_told_to_the_host_for_the_flags_of_MG10()
    {
        foreach (var species in new[] { 25, 26, 27, 28, 29 })
        {
            var (owner, caster) = Session(Spell(SessionSpellCaster.ArtNumberField, species));
            using var _ = owner;
            Apply(caster, Hero);
            var elemental = species is >= 26 and <= 28;
            Assert.AreEqual(elemental, (caster.Log.Single().Detail & SessionSpellCaster.TraitElemental) != 0, $"species {species}: {Trail(caster)}");
        }
    }

    [TestMethod]
    public void A_restore_takes_the_body_off_and_tells_the_host_the_species_it_had()
    {
        var (owner, caster) = Session(Spell(SessionSpellCaster.ArtNumberField, 27));
        using var _ = owner;
        Apply(caster, Hero);
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, action: SpellRunAction.End));
        Assert.IsNull(owner.QueryTraitArt(Hero));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitRestored, 0, 27, Hero, SessionSpellCaster.TraitElemental | SessionSpellCaster.TraitStateChanged), caster.Log.Last(), Trail(caster));
    }

    [TestMethod]
    public void A_restore_of_a_critter_with_no_body_still_asks_the_host_to_choose_the_best_items_again()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Apply(caster, Hero, action: SpellRunAction.End);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitRestored, 0, -1, Hero, 0), caster.Log.Single(), Trail(caster));
    }

    [TestMethod]
    public void A_second_apply_goes_over_the_first_and_the_first_restore_takes_the_body_off_whatever_runs_remain()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Apply(caster, Hero);
        var other = Spell(SessionSpellCaster.ArtNumberField, 28);
        var again = new SessionSpellCaster(owner, n => n == 49 ? other : null, new Map(owner, null!), new Events());
        Apply(again, Hero);
        Assert.AreEqual(new SessionTraitArt(28, 0), owner.QueryTraitArt(Hero), "no reference count: the later body replaces the earlier");
        Apply(caster, Hero, action: SpellRunAction.End);
        Assert.IsNull(owner.QueryTraitArt(Hero), "the first run's END restores although the second still holds the body");
    }

    [TestMethod]
    public void A_missing_target_object_and_a_target_that_is_no_critter_do_nothing_and_are_not_not_ready()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Hero, noObject: true));
        Assert.AreEqual(SpellComponentOutcome.None, Apply(caster, Stranger));
        Assert.AreEqual(0, caster.Log.Count, Trail(caster));
        Assert.IsNull(owner.QueryTraitArt(Hero));
    }

    [TestMethod]
    public void Another_field_and_a_species_or_palette_the_art_id_cannot_hold_are_not_ready()
    {
        foreach (var (field, value, palette) in new[] { (5, 20, 0), (SessionSpellCaster.ArtNumberField, 32, 0), (SessionSpellCaster.ArtNumberField, 600, 0), (SessionSpellCaster.ArtNumberField, -2, 0), (SessionSpellCaster.ArtNumberField, 20, 4) })
        {
            var (owner, caster) = Session(Spell(field, value, palette));
            using var _ = owner;
            Apply(caster, Hero);
            Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, 0, (int)SpellComponentType.Trait, Hero), caster.Log.Single(), $"{field} {value} {palette}: {Trail(caster)}");
            Assert.IsNull(owner.QueryTraitArt(Hero));
        }
    }

    [TestMethod]
    public void A_critter_the_host_does_not_know_is_not_ready()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var hero = caster.RunObject(Hero)!;
        var stranger = new SpellRunObject("oid:nowhere", SpellObjectFacts.NpcType);
        var run = new SpellRun(0, 49, SpellRunAction.Begin, hero, hero, HeroTile, stranger, NpcTile, null, SpellRunFlags.Active);
        caster.Apply(run, 0, new(stranger, NpcTile));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, 0, (int)SpellComponentType.Trait, "oid:nowhere"), caster.Log.Single(), Trail(caster));
        Assert.IsNull(owner.QueryTraitArt("oid:nowhere"));
    }

    [TestMethod]
    public void The_monster_bodies_are_saved_and_restored()
    {
        var (owner, caster) = Session(Spell(SessionSpellCaster.ArtNumberField, 3, 1));
        using var _ = owner;
        Apply(caster, Hero);
        Apply(caster, Npc);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"traitArt\":[{\"entity\":\"oid:shape\",\"species\":3,\"palette\":1},{\"entity\":\"pc\",\"species\":3,\"palette\":1}]");
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.IsNotNull(decoded.Snapshot, $"{decoded.Failure} {decoded.Path}");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(decoded.Snapshot));
        CollectionAssert.AreEqual(new[] { new SessionTraitArtRecord(Npc, new(3, 1)), new SessionTraitArtRecord(Hero, new(3, 1)) }, decoded.Snapshot.Spells.TraitArt.ToArray());
    }

    [TestMethod]
    public void A_save_with_a_body_the_art_id_cannot_hold_or_the_rows_out_of_order_is_refused()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Apply(caster, Hero);
        Apply(caster, Npc);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var big = GameSessionSnapshotCodec.TryDecode(text.Replace("\"species\":20", "\"species\":32"));
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, big.Failure, big.Path);
        var swapped = GameSessionSnapshotCodec.TryDecode(text.Replace("\"entity\":\"oid:shape\"", "\"entity\":\"zzz\"").Replace("\"entity\":\"pc\"", "\"entity\":\"oid:shape\"").Replace("\"entity\":\"zzz\"", "\"entity\":\"pc\""));
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, swapped.Failure, swapped.Path);
    }

    [TestMethod]
    public void The_eighty_eight_to_eighty_nine_step_adds_the_empty_list_of_monster_bodies_and_a_save_without_the_spells_section_is_refused()
    {
        using var owner = SessionFixtures.Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema88(current);
        StringAssert.Contains(older, "\"schemaVersion\":88,\"world\"");
        Assert.IsFalse(older.Contains("traitArt", StringComparison.Ordinal));
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.IsNotNull(upgraded.Snapshot, $"{upgraded.Failure} {upgraded.Path}");
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot.SchemaVersion);
        Assert.AreEqual(0, upgraded.Snapshot.Spells.TraitArt.Count);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace("\"spells\":", "\"spellz\":"));
        Assert.AreEqual((SnapshotDecodeFailure.MissingField, "$.spells"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void The_write_refuses_an_entity_that_is_no_critter_and_a_body_the_id_cannot_hold()
    {
        var (owner, _) = Session();
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.WriteTraitArt(owner.Version, "scenery:1", new(1, 0)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.WriteTraitArt(owner.Version, Hero, new(32, 0)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.WriteTraitArt(owner.Version, Hero, new(1, 4)).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteTraitArt(owner.Version, Hero, null).Disposition);
    }
}
