using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-106: the viewer's NPC combat values read what the session's NPC wears (invented values, no original data): a guard
/// (0) wearing a torch (1) in its shield slot with a cup (2) in its pack; a break takes the torch off its build, and after a regeneration
/// dressed it with a made torch (prototype 15199, which has no object row) the build and the item spells read the made torch's prototype
/// fields.</summary>
[TestClass]
public sealed class AreaNpcSessionWearTests
{
    private const int Source = 5, TorchPrototype = 15199;
    private static readonly SessionItemKind Torch = new(SessionItemType.Generic, TorchPrototype, 6000, 6001);
    private static readonly SessionItemKind Pile = new(SessionItemType.Gold, 9056, 3200, 33200);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A generic item (type 14) with the common, item and generic blocks: a torch (shield location, spell 171) or a cup (pack).
    private static AreaObjectRecord Item(int index, int location, int spell = 10000) => new(Key(index), 0, 14, TorchPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, location), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, spell), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    // A body armour (type 7, torso coverage in its inventory art, every size) worn at 1006 with normal resistance 20.
    private static AreaObjectRecord Armour(int index) => new(Key(index), 0, 7, 8114, "p", "p", "r", "s", AreaScope.Core, null, null, null,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, (long)(CombatItemArt.ItemArtType << 28)), Field(43, 1006),
            Field(44, 0x60000000), Field(45, 0), Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000),
            Field(54, 0), Field(55, 0),
            Field(59, 0x7), Field(61, 0), Field(62, 0), Array(63, 20, 0, 0, 0, 0), Array(64), Field(65, 0), Field(66, 0), Field(67, 0),
        ], true, true, true);

    // A human NPC (type 16, no monster flags) with stats 8, level 1, no skills, 20 hit points.
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 8, 8, 8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47), Field(57, Source), Field(63, 0),
            Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private static SessionItemFacts Facts(int location) => new(Torch, 1, 0, location, null, []);

    // armour false: the NPC's map data lists no body armour (the armour row stays a lone item of the map's seed, in no list); dead: the NPC is a body that can be looted.
    private static (AreaCombatHost Host, AreaInventoryHost Inventory) Host(Func<int, CombatFields?>? prototypes, bool armour = true, bool dead = false)
    {
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, dead, false, [], armour ? [Entity(1), Entity(2), Entity(3)] : [Entity(1), Entity(2)])],
                [new SessionSeedItem(Entity(1), Facts(1005), null), new SessionSeedItem(Entity(2), Facts(0), null),
                    new SessionSeedItem(Entity(3), new(new(SessionItemType.Armor, 8114, 6002, 6003), 1, 0, 1006, null, []), armour ? null : new CellCoord(3, 3))]),
            new[] { Npc(0), Item(1, 1005), Item(2, 0), Armour(3) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        return (new AreaCombatHost(inventory, prototypes), inventory);
    }

    // The guard's regeneration: its set creates one torch, which its best-items wield puts in the shield slot.
    private sealed class World : ISessionRestockWorld
    {
        private static readonly EquipmentItem TorchItem = new() { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true, HitPoints = 10, MaxHitPoints = 10 };
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => entity == Entity(0) ? new(RestockHolderKind.Npc, Source, false, false, 0, false) : null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Read, new(0, 0, [new(100, 1)]));
        public SessionRestockItem? RestockItem(int basicPrototype) => basicPrototype == 1 ? new(Torch) : null;
        public SessionItemKind RestockPileKind => Pile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
        public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => new(new EquipmentWearer(), new EquipmentChooser { IsPc = false },
            item => item.Facts.Kind == Torch ? new SessionWearableItem(Torch, [], false, TorchItem) { Worth = 5 } : null, (_, _) => true);
    }

    [TestMethod]
    public void The_npcs_build_follows_what_the_session_says_it_wears()
    {
        var torchFields = CombatFieldReader.Read(Item(9, 0, spell: 171)).Fields!;
        var prototypes = new Dictionary<int, CombatFields> { [TorchPrototype] = torchFields };
        using var session = ConversationSessionFixture.Create();
        var (host, inventory) = Host(number => prototypes.GetValueOrDefault(number));
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var guard = Entity(0);

        var first = host.Build(session, 1, guard);
        Assert.AreEqual(SessionCritterCombatFailure.None, first.Failure);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Shield, Entity(1)), (EquipmentSlot.Armor, Entity(3)) },
            host.Worn(session, 1, guard)!.Select(item => (item.Slot, item.Entity)).ToArray());
        Assert.AreEqual(20, first.Combat!.Facts.NormalResistance, "the armour's normal resistance");
        Assert.AreEqual(Entity(3), host.Entity(1, Key(3)));
        Assert.AreEqual(Key(3), host.WornArmor(session, 1, guard)!.Key, "the hit sounds on the worn armour");
        Assert.AreSame(first, host.Build(session, 1, guard), "the same worn set reuses the build");
        var watching = new AreaCombatHost(inventory, null, () => session);
        Assert.AreEqual(20, watching.Facts(1, guard)!.NormalResistance, "the event world's facts read the shown session's build");

        // A broken torch and armour are taken off: a new build without them, and the event world's facts lose the resistance.
        var wear = host.WornItems(session, 1, guard)!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(1), wear[EquipmentSlot.Shield].Baseline).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(3), wear[EquipmentSlot.Armor].Baseline).Disposition);
        var bare = host.Build(session, 1, guard);
        Assert.AreNotSame(first, bare);
        Assert.AreEqual(SessionCritterCombatFailure.None, bare.Failure);
        Assert.AreEqual(0, host.Worn(session, 1, guard)!.Count);
        Assert.AreEqual(0, watching.Facts(1, guard)!.NormalResistance);
        Assert.AreEqual(20, host.Facts(1, guard)!.NormalResistance, "without a session the map data's armour counts");
        Assert.IsNull(host.WornArmor(session, 1, guard));
        Assert.IsNotNull(host.Critter(1, guard), "the map data's build is kept for the wearer and the chooser");

        // The regeneration clears the torch and the cup and dresses the guard with a made torch, which has no row: its prototype's fields.
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleRestock(session.Version, guard, hasSource: true).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            session.JumpClocks(session.QueryClockSnapshot().Token, RestockRules.MaxDelayMs).Disposition);
        var done = session.ProcessEvents(new World()).Restocks.Single().Regenerations.Single();
        Assert.AreEqual(GameSessionFailure.None, done.Wield!.Failure);
        var made = session.QueryNpcWorn(guard)!.Worn.Single();
        Assert.AreEqual(EquipmentSlot.Shield, made.Slot);
        var worn = host.Worn(session, 1, guard)!.Single();
        Assert.AreEqual((EquipmentSlot.Shield, made.Item), (worn.Slot, worn.Entity));
        Assert.AreSame(torchFields, worn.Fields, "the made torch's prototype fields");
        Assert.AreNotSame(bare, host.Build(session, 1, guard));
        Assert.AreEqual(SessionCritterCombatFailure.None, host.Build(session, 1, guard).Failure);
        Assert.AreEqual(171, host.NpcItemSpells(session, 1, made.Item)!.Spells[0], "the made torch's item spells");
        Assert.AreEqual(guard, host.NpcItemSpells(session, 1, made.Item)!.Holder);

        // Without the prototypes (not loaded yet) the made torch has no facts: it is skipped, not a failure.
        var (blind, _) = Host(null);
        Assert.AreEqual(0, blind.Worn(session, 1, guard)!.Count);
        Assert.IsNull(blind.NpcItemSpells(session, 1, made.Item));

        // A session that lists no inventory for the map keeps the map data's worn items.
        using var other = ConversationSessionFixture.Create();
        Assert.IsNull(host.Worn(other, 1, guard));
        Assert.AreEqual(20, host.Build(other, 1, guard).Combat!.Facts.NormalResistance);
    }

    // ARC-DEV-315: the same regeneration with an armour set item: its wield puts a made body armour (prototype 8114, which has no object row once the map
    // data's armour is broken and cleared) in the armour slot.
    private sealed class ArmourWorld : ISessionRestockWorld
    {
        public static readonly SessionItemKind ArmourKind = new(SessionItemType.Armor, 8114, 6002, 6003);
        private static readonly EquipmentItem MadeArmour = new() { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium }, HitPoints = 10, MaxHitPoints = 10 };
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => entity == Entity(0) ? new(RestockHolderKind.Npc, Source, false, false, 0, false) : null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Read, new(0, 0, [new(100, 1)]));
        public SessionRestockItem? RestockItem(int basicPrototype) => basicPrototype == 1 ? new(ArmourKind) : null;
        public SessionItemKind RestockPileKind => Pile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
        public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => new(new EquipmentWearer(), new EquipmentChooser { IsPc = false },
            item => item.Facts.Kind == ArmourKind ? new SessionWearableItem(ArmourKind, [], false, MadeArmour) { Worth = 5 } : null, (_, _) => true);
    }

    [TestMethod]
    public void The_worn_armour_is_named_by_entity_whether_it_has_a_row_or_is_a_made_item()
    {
        var armourFields = CombatFieldReader.Read(Armour(9)).Fields!;
        var prototypes = new Dictionary<int, CombatFields> { [TorchPrototype] = CombatFieldReader.Read(Item(9, 0, spell: 171)).Fields!, [8114] = armourFields };
        using var session = ConversationSessionFixture.Create();
        var (host, inventory) = Host(number => prototypes.GetValueOrDefault(number));
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var guard = Entity(0);

        Assert.AreEqual(Entity(3), host.WornArmorEntity(session, 1, guard), "the map data's armour has a row");
        Assert.AreEqual(Entity(3), host.WornArmorEntity(session, guard), "by identity too");
        Assert.AreEqual(Key(3), host.WornArmor(session, 1, guard)!.Key, "its row is still read");
        Assert.IsNull(host.WornArmorEntity(session, 2, guard), "a map the host does not cover");
        Assert.IsNull(host.WornArmorEntity(session, 1, Entity(1)), "an item wears nothing");

        // The armour breaks and the regeneration dresses the guard with a made armour, which has no row.
        var wear = host.WornItems(session, 1, guard)!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(3), wear[EquipmentSlot.Armor].Baseline).Disposition);
        Assert.IsNull(host.WornArmorEntity(session, 1, guard), "a broken armour is taken off");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleRestock(session.Version, guard, hasSource: true).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, RestockRules.MaxDelayMs).Disposition);
        var done = session.ProcessEvents(new ArmourWorld()).Restocks.Single().Regenerations.Single();
        Assert.AreEqual(GameSessionFailure.None, done.Wield!.Failure);
        var made = session.QueryNpcWorn(guard)!.Worn.Single();
        Assert.AreEqual(EquipmentSlot.Armor, made.Slot);
        Assert.IsTrue(made.Item.StartsWith("made:", StringComparison.Ordinal), made.Item);

        Assert.AreEqual(made.Item, host.WornArmorEntity(session, 1, guard), "a made armour is named by its entity");
        Assert.AreEqual(made.Item, host.WornArmorEntity(session, guard));
        Assert.IsNull(host.WornArmor(session, 1, guard), "it has no row: the row reader still knows none");
        Assert.AreEqual(ArmourWorld.ArmourKind, AreaMadeItems.Kind(session, made.Item, guard), "the NPC's holder names its prototype");
        Assert.AreEqual(8114, AreaMadeItems.Prototype(session, made.Item, guard));
        Assert.IsNull(AreaMadeItems.Kind(session, made.Item), "without the holder an NPC's list is not searched");
        Assert.IsNull(AreaMadeItems.Kind(session, made.Item, "not an entity id"), "an invalid holder lists nothing");
    }

    // ARC-SCRIPT-2342 (AR2, AR7): get armor of an NPC the session dressed. The wear hooks write the critter art's armour part when body armour goes on or off, so the answer follows the session's worn record, not the stored art.
    [TestMethod]
    public void Get_armor_of_a_dressed_npc_follows_the_body_armour_it_wears_now()
    {
        var armourFields = CombatFieldReader.Read(Armour(9)).Fields!;
        var prototypes = new Dictionary<int, CombatFields> { [TorchPrototype] = CombatFieldReader.Read(Item(9, 0, spell: 171)).Fields!, [8114] = armourFields };
        using var session = ConversationSessionFixture.CreateWithHero(1);
        var (host, inventory) = Host(number => prototypes.GetValueOrDefault(number));
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var guard = Entity(0);
        // The guard's stored current art is a critter art with armour part 3 (chain), as its map data saved it.
        var row = Npc(0);
        row = row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = (2u << 28) | (3u << 20) } : f)] };
        uint? use = (6u << 28) | (4u << 6);
        var read = true;
        var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, NpcBodyArmor: npc => read && npc == guard ? new AreaScriptWornArmor(use) : null);
        var scripts = new AreaUseScriptHost(session, 1, [row], [], null, world);
        var who = new ScrObjectRef(1, guard);
        Assert.AreEqual(3, scripts.ArmorOf(who), "the session never dressed it: the stored art stands");

        // The armour breaks and the regeneration dresses the guard with a made body armour: the hook gives the art that armour's part.
        var wear = host.WornItems(session, 1, guard)!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(3), wear[EquipmentSlot.Armor].Baseline).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ScheduleRestock(session.Version, guard, hasSource: true).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, RestockRules.MaxDelayMs).Disposition);
        Assert.AreEqual(GameSessionFailure.None, session.ProcessEvents(new ArmourWorld()).Restocks.Single().Regenerations.Single().Wield!.Failure);
        Assert.AreEqual(EquipmentSlot.Armor, session.QueryNpcWorn(guard)!.Worn.Single().Slot);
        Assert.AreEqual(4, scripts.ArmorOf(who), "dressed: the part of the worn armour's use art");
        use = (6u << 28) | (9u << 6);
        Assert.AreEqual(0, scripts.ArmorOf(who), "a part a critter art cannot hold is refused by the wield and the part stays 0");
        use = (6u << 28) | (8u << 6);
        Assert.AreEqual(8, scripts.ArmorOf(who), "part 8 is the last a critter art holds");
        read = false;
        Assert.IsNull(scripts.ArmorOf(who), "a world that does not read the worn armour's art: not modelled");
        uint? none = null;
        var unreadWorld = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, NpcBodyArmor: _ => new AreaScriptWornArmor(none));
        Assert.IsNull(new AreaUseScriptHost(session, 1, [row], [], null, unreadWorld).ArmorOf(who), "the record wears armour but the world finds none: not modelled, not bare");

        // The art type decides what a wield can hold: a unique NPC's art only part 0, a monster art up to 7.
        AreaObjectRecord WithArt(uint art) => row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = art } : f)] };
        use = (6u << 28) | (4u << 6);
        read = true;
        Assert.AreEqual(0, new AreaUseScriptHost(session, 1, [WithArt(13u << 28)], [], null, world).ArmorOf(who), "a unique NPC's art holds no part");
        Assert.AreEqual(4, new AreaUseScriptHost(session, 1, [WithArt((12u << 28) | (1u << 23))], [], null, world).ArmorOf(who), "a monster art holds parts up to 7");
        use = (6u << 28) | (8u << 6);
        Assert.AreEqual(0, new AreaUseScriptHost(session, 1, [WithArt((12u << 28) | (1u << 23))], [], null, world).ArmorOf(who), "part 8 does not stand in a monster art");
        use = (6u << 28) | (8u << 6);

        // A polymorph takes the worn items off and gives a monster body with part 0 (the dressed record is then moot).
        read = true; use = (6u << 28) | (4u << 6);
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteTraitArt(session.Version, guard, new(5, 0)).Disposition);
        Assert.AreEqual(0, scripts.ArmorOf(who));
    }

    private sealed record Rig(GameSessionOwner Session, AreaCombatHost Host, AreaUseScriptHost Scripts) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    // ARC-SCRIPT-2351 (AR8): get armor of an NPC the session never dressed whose map-data body armour was taken off since. The removal's wield-off hook sets the art's armour part to 0 (AR2), so the stored part no longer stands;
    // an NPC whose data never listed body armour keeps the stored part (its art may show armour its data does not list), and so does one that lost another item.
    [TestMethod]
    public void Get_armor_of_an_undressed_npc_answers_0_once_its_map_data_body_armour_is_taken_off()
    {
        var prototypes = new Dictionary<int, CombatFields> { [TorchPrototype] = CombatFieldReader.Read(Item(9, 0, spell: 171)).Fields!, [8114] = CombatFieldReader.Read(Armour(9)).Fields! };
        var row = Npc(0);
        row = row with { EffectiveFields = [.. row.EffectiveFields.Select(f => f.Slot == 0 ? f with { Number = (2u << 28) | (3u << 20) } : f)] };
        var guard = Entity(0);
        var who = new ScrObjectRef(1, guard);
        var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null);

        Rig Fresh(bool armour = true, bool dead = false)
        {
            var session = ConversationSessionFixture.CreateWithHero(1);
            var (host, inventory) = Host(number => prototypes.GetValueOrDefault(number), armour, dead);
            Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
            return new Rig(session, host, new AreaUseScriptHost(session, 1, [row], [], null, world));
        }
        SessionInsertCheck accept = _ => SessionInsertVerdict.Accepted;

        using (var rig = Fresh())
        {
            var (session, _, scripts) = (rig.Session, rig.Host, rig.Scripts);
            Assert.AreEqual(3, scripts.ArmorOf(who), "nothing taken off yet: the stored art stands");
            Assert.IsFalse(session.QueryNpcBodyArmorTakenOff(guard));
            Assert.AreEqual(GameSessionDisposition.Changed, session.TakeItemFromNpc(session.Version, guard, Entity(2), accept).Disposition, "its pack item taken (not worn)");
            Assert.AreEqual(GameSessionDisposition.Changed, session.TakeItemFromNpc(session.Version, guard, Entity(1), accept, unwield: true).Disposition, "its shield-slot item taken off");
            Assert.AreEqual(3, scripts.ArmorOf(who), "another worn item left: the part stands");
            Assert.IsNull(session.QueryNpcWorn(guard), "the session never dressed it");
            // A take (the barter doll view and a steal's move: unwield) of the body armour.
            Assert.AreEqual(GameSessionDisposition.Changed, session.TakeItemFromNpc(session.Version, guard, Entity(3), accept, unwield: true).Disposition);
            Assert.IsTrue(session.QueryNpcBodyArmorTakenOff(guard));
            Assert.AreEqual(0, scripts.ArmorOf(who), "taken: the wield-off hook cleared the part");
        }
        using (var rig = Fresh())
        {
            var (session, _, scripts) = (rig.Session, rig.Host, rig.Scripts);
            Assert.AreEqual(GameSessionDisposition.Changed, session.GiveItemToPc(session.Version, guard, 6002, accept).Disposition, "a script take by name");
            Assert.AreEqual(0, scripts.ArmorOf(who));
        }
        using (var rig = Fresh(dead: true))
        {
            var (session, _, scripts) = (rig.Session, rig.Host, rig.Scripts);
            Assert.AreEqual(GameSessionDisposition.Changed, session.LootItem(session.Version, guard, Entity(3), accept).Disposition, "the loot of its body");
            Assert.AreEqual(0, scripts.ArmorOf(who));
        }
        using (var rig = Fresh())
        {
            var (session, host, scripts) = (rig.Session, rig.Host, rig.Scripts);
            var wear = host.WornItems(session, 1, guard)!;
            Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(3), wear[EquipmentSlot.Armor].Baseline).Disposition, "the first break takes the armour off (it stays in the list, broken)");
            Assert.AreEqual(0, scripts.ArmorOf(who));
            Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(3), wear[EquipmentSlot.Armor].Baseline).Disposition, "the second break deletes it");
            Assert.AreEqual(0, scripts.ArmorOf(who));
        }
        using (var rig = Fresh(armour: false))
        {
            var (session, _, scripts) = (rig.Session, rig.Host, rig.Scripts);
            Assert.AreEqual(GameSessionDisposition.Changed, session.TakeItemFromNpc(session.Version, guard, Entity(1), accept, unwield: true).Disposition);
            Assert.IsFalse(session.QueryNpcBodyArmorTakenOff(guard));
            Assert.AreEqual(3, scripts.ArmorOf(who), "its data listed no body armour: its stored art shows what it shows");
        }
        using (var rig = Fresh())
        {
            Assert.IsFalse(rig.Session.QueryNpcBodyArmorTakenOff(Entity(5)), "an entity no seed lists");
            Assert.ThrowsExactly<ArgumentException>(() => rig.Session.QueryNpcBodyArmorTakenOff("not an entity id"));
        }
    }
}
