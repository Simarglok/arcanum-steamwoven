using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-032 (MA-7, ledger XR1–XR5): the Identify component and Divine Magick's identified-item flag through the cast host. The half-elf fixture's kit:2 is a held item. Records, ids and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterIdentifyTests
{
    private const string Hero = SessionSpellCaster.Hero, Coat = "kit:2", Rat = "oid:rat", Box = "oid:box", Stranger = "oid:stranger";
    private static readonly SpellLocation HeroTile = new(5, 5), FarTile = new(8, 5);
    private static readonly SpellArea Aim = new(0x4, 0, 0, 0, -1);

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) =>
            id == Hero ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
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

    // Record 9's shape: the identified item flag first, then Identify (both aim at the target object); record 6's and 7's are the Identify alone.
    private static SessionSpellRecord Record9() =>
        SessionSpellRecords.Translate(Record(number: 9, beginTargets: 0x4, info: SpellInfo.Friendly,
            components:
            [
                new(SpellAction.Begin, SpellComponentType.ObjFlag, Aim, 0, 0, new ObjectFlagData(87, 0x1, true)),
                new(SpellAction.Begin, SpellComponentType.Identify, Aim, 0, 0, null),
                new(SpellAction.Begin, SpellComponentType.ObjFlag, Aim, 0, 0, new ObjectFlagData(87, 0x1, false)),
                new(SpellAction.Begin, SpellComponentType.ObjFlag, Aim, 0, 0, new ObjectFlagData(87, 0x4, true)),
                new(SpellAction.Begin, SpellComponentType.ObjFlag, Aim, 0, 0, new ObjectFlagData(20, 0x1, true)),
            ]));

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session()
    {
        var record = Record9();
        var owner = CreateWith(HalfElf());
        return (owner, new SessionSpellCaster(owner, n => n == 9 ? record : null, new Map(owner), new Events()));
    }

    private static SpellRun Run(string? parent) => new(1, 9, SpellRunAction.Begin, parent is null ? null : new(parent, parent == Hero ? SpellObjectFacts.PcType : SpellObjectFacts.NpcType), parent is null ? null : new(parent, parent == Hero ? SpellObjectFacts.PcType : SpellObjectFacts.NpcType),
        HeroTile, null, FarTile, null, SpellRunFlags.Active);

    private static SpellRunTarget At(string? id, int type) => new(id is null ? null : new(id, type), FarTile);

    private static SpellCastEvent[] Events_(SessionSpellCaster caster) => [.. caster.Log];

    [TestMethod]
    public void The_hero_s_identify_answers_by_the_target_type_and_writes_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        // Components of record 9: 0 the identified flag, 1 Identify.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(Hero, SpellTargetObject.Pc)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(Box, SpellTargetObject.Container)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(Coat, 5)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(Stranger, SpellTargetObject.Scenery)));
        CollectionAssert.AreEqual(new[]
        {
            new SpellCastEvent(SpellCastEventKind.IdentifyCritter, 1, SpellTargetObject.Npc, Rat),
            new SpellCastEvent(SpellCastEventKind.IdentifyCritter, 1, SpellTargetObject.Pc, Hero),
            new SpellCastEvent(SpellCastEventKind.IdentifyContainer, 1, SpellTargetObject.Container, Box),
            new SpellCastEvent(SpellCastEventKind.IdentifyItem, 1, 5, Coat),
            new SpellCastEvent(SpellCastEventKind.IdentifyItem, 1, SpellTargetObject.Scenery, Stranger),
        }, Events_(caster), "a PC and an NPC to the sheet, a container to its inventory, any other type to the description (0x4EE4C0, 0x4EE430, 0x4EE3A0)");
        Assert.AreEqual(version, owner.Version, "the component writes nothing");
        Assert.IsFalse(owner.QueryItemIdentified(Coat));
    }

    [TestMethod]
    public void Nothing_happens_without_a_target_or_for_a_caster_who_is_not_the_local_player()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 1, At(null, 0)), "no current target");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Rat), 1, At(Box, SpellTargetObject.Container)), "an NPC caster's Identify has no effect");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(null), 1, At(Box, SpellTargetObject.Container)), "a run without a parent");
        Assert.AreEqual(0, caster.Log.Count, "no answer and no 'not ready' line either: the component is taken");
        Assert.AreEqual(version, owner.Version);
    }

    [TestMethod]
    public void The_identified_flag_marks_a_held_item_once_and_the_mark_is_saved()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsFalse(owner.QueryItemIdentified(Coat));
        var version = owner.Version;
        caster.Apply(Run(Hero), 0, At(Coat, 5));
        Assert.IsTrue(owner.QueryItemIdentified(Coat));
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.ItemIdentified, 1, 0x1, Coat) }, Events_(caster));
        // A second cast on the same item changes nothing, and still answers that it is identified.
        var identified = owner.Version;
        caster.Apply(Run(Hero), 0, At(Coat, 5));
        Assert.AreEqual(identified, owner.Version);
        Assert.AreEqual(SpellCastEventKind.ItemIdentified, caster.Log[^1].Kind);
        // The mark is the inventory section's: it survives a save and a load.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.IsTrue(restored.QueryItemIdentified(Coat));
        Assert.IsFalse(restored.QueryItemIdentified("kit:3"), "only the item");
    }

    [TestMethod]
    public void An_item_the_session_does_not_hold_is_refused_and_the_spell_goes_on()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(Hero), 0, At("kit:99", 5)));
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.ItemIdentifyNotSaved, 1, (int)GameSessionFailure.ItemNotAvailable, "kit:99") }, Events_(caster));
        Assert.AreEqual(version, owner.Version);
        Assert.IsFalse(owner.QueryItemIdentified("kit:99"));
    }

    [TestMethod]
    public void The_mark_is_refused_for_a_stale_version_an_invalid_entity_and_a_deleted_item()
    {
        var (owner, _) = Session();
        using var _ = owner;
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MarkItemIdentified(owner.Version, Coat).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MarkItemIdentified(stale, "kit:3").Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.MarkItemIdentified(owner.Version, "").Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkItemIdentified(owner.Version, Coat).Disposition, "already marked");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.MarkItemIdentified(owner.Version, "kit:99").Failure);
    }

    [TestMethod]
    public void The_other_item_flag_lines_and_a_target_that_is_no_item_stay_not_ready()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        caster.Apply(Run(Hero), 2, At(Coat, 5));
        caster.Apply(Run(Hero), 3, At(Coat, 5));
        caster.Apply(Run(Hero), 4, At(Coat, 5));
        caster.Apply(Run(Hero), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(4, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.ObjFlag),
            "a clear, another item bit, another field and a critter target are logged as not ready, whatever the component's data says");
        Assert.AreEqual(version, owner.Version);
        Assert.IsFalse(owner.QueryItemIdentified(Coat));
    }
}
