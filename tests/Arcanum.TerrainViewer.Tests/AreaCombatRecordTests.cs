using Arcanum.Data.Area;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-072 (MIG1, TM3, FM3): <see cref="AreaCombatHost"/> answers an NPC's base record by identity, not by the shown map. Invented
/// maps, no original data: map 1 holds Virgil (a healer with a healing item) and a guard; map 2 is the shown map with one NPC. Virgil
/// travels to map 2; the session names map 1 as his home (<c>npc.map</c>). Whether this run loaded map 1 or not, his fields, build,
/// holder and worn set are the same as on map 1; a map the run never loaded is read on demand once.
/// </summary>
[TestClass]
public sealed class AreaCombatRecordTests
{
    private const int HealingPrototype = 8000;
    private static readonly SessionItemKind Healing = new(SessionItemType.Generic, HealingPrototype, 6000, 6001);
    private static readonly CellCoord HeroCell = new(14, 20);

    private static AreaObjectKey Key(int map, int index) => new(new string('a', 64), map, AreaSourceKind.Mobile, 1, index, index * 100L, (map * 100 + index).ToString("x").PadLeft(64, '0'), 100);
    private static string Entity(int map, int index) => "oid:" + Key(map, index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A human NPC (type 16) with every primary stat 20, level 1, 20 hit points; hit points differ by `hp` so two NPCs have unlike fields.
    private static AreaObjectRecord Npc(int map, int index, int hp = 20, int heal = 0) => new(Key(map, index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, hp), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, heal, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    private static AreaObjectRecord Item(int map, int index) => new(Key(map, index), 0, 14, HealingPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x08), Field(60, 10), Field(61, 3),
        ], true, true, true);

    // Map 1: Virgil (0, 30 hit points, holds the healing item 2) and a guard (1, 20). Virgil is the roster companion of the tests.
    private static AreaInventoryMap Home() => new(1,
        new([new SessionSeedHolder(Entity(1, 0), SessionHolderKind.Npc, false, false, [], [Entity(1, 2)]),
             new SessionSeedHolder(Entity(1, 1), SessionHolderKind.Npc, false, false, [], [])],
            [new SessionSeedItem(Entity(1, 2), new(Healing, 1, 0, 0, null, []), null)]),
        new Dictionary<string, AreaObjectRecord> { [Entity(1, 0)] = Npc(1, 0, 30, 5), [Entity(1, 1)] = Npc(1, 1), [Entity(1, 2)] = Item(1, 2) });

    // Map 2, the shown map: one NPC.
    private static AreaInventoryMap Shown() => new(2,
        new([new SessionSeedHolder(Entity(2, 0), SessionHolderKind.Npc, false, false, [], [])], []),
        new Dictionary<string, AreaObjectRecord> { [Entity(2, 0)] = Npc(2, 0, 25) });

    private sealed class Source(params AreaInventoryMap[] maps) : IMapRecordSource
    {
        public readonly List<int> Asked = [];
        public MapRecordsLoad Load(int runtimeMap, CancellationToken token)
        {
            Asked.Add(runtimeMap);
            return maps.FirstOrDefault(map => map.RuntimeMap == runtimeMap) is { } found ? new(found, string.Empty) : new(null, "unreadable");
        }
    }

    // A session on map 2 with the shown map's seed attached, and Virgil (map 1's) named as standing on map 2 (his home map's npc.map).
    private static GameSessionOwner Session(AreaInventoryHost inventory, bool travelled = true)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(2), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        if (travelled)
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(1), AreaCompanionPlacement.MapKey(Entity(1, 0)), 2).Disposition);
        return session;
    }

    // LifeFacts carries arrays, so equal facts are not equal records: compare the values the build decides.
    private static void SameFacts(Arcanum.Simulation.Combat.LifeFacts expected, Arcanum.Simulation.Combat.LifeFacts actual)
    {
        Assert.AreEqual(expected.HitPointPoints, actual.HitPointPoints);
        Assert.AreEqual(expected.FatiguePoints, actual.FatiguePoints);
        Assert.AreEqual(expected.Level, actual.Level);
        Assert.AreEqual(expected.HealRate, actual.HealRate);
        Assert.AreEqual(expected.NormalResistance, actual.NormalResistance);
    }

    [TestMethod]
    public void A_companion_of_a_map_this_run_never_loaded_resolves_as_it_does_on_its_home_map()
    {
        // The reference: both maps given, as a run that loaded them.
        var reference = new AreaInventoryHost([Home(), Shown()]);
        var referenceSession = Session(reference);
        var onHome = new AreaCombatHost(reference, session: () => referenceSession);

        // The run under test holds only the shown map; map 1 is read on demand.
        var source = new Source(Home());
        var inventory = new AreaInventoryHost([Shown()], records: source);
        var session = Session(inventory);
        var combat = new AreaCombatHost(inventory, session: () => session);
        var virgil = Entity(1, 0);

        Assert.IsFalse(inventory.Resolver.Holds(1));
        Assert.AreEqual(1, combat.HomeMap(virgil));
        CollectionAssert.AreEqual(new[] { 1 }, source.Asked);
        Assert.IsTrue(combat.Covers(1));

        Assert.AreEqual(onHome.Fields(1, virgil)!.Object.HitPoints, combat.Fields(virgil)!.Object.HitPoints);
        Assert.AreEqual(30, combat.Fields(virgil)!.Object.HitPoints, "Virgil's own row, not the guard's or the shown NPC's");
        Assert.IsNotNull(combat.Fields(virgil));
        var build = combat.Build(virgil);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        SameFacts(onHome.Build(1, virgil).Combat!.Facts, build.Combat!.Facts);
        SameFacts(onHome.Build(referenceSession, 1, virgil).Combat!.Facts, combat.Build(session, virgil).Combat!.Facts);

        // His holder and the item he carries come from the home map's seed, attached to this session on first use.
        var holder = combat.Holder(session, virgil);
        Assert.IsNotNull(holder);
        CollectionAssert.AreEqual(new[] { Entity(1, 2) }, holder.Items.Select(item => item.Entity).ToArray());
        Assert.IsNotNull(session.QueryMapInventory(new(1)), "the home seed is attached");
        // Nothing in the fixture is worn: the identity question lists him (a holder of the home seed) with an empty set, as the map-keyed one.
        Assert.AreEqual(0, onHome.Worn(referenceSession, 1, virgil)!.Count);
        Assert.AreEqual(0, combat.Worn(session, virgil)!.Count);
        Assert.IsNull(combat.Worn(session, Entity(2, 5)), "an entity no map lists has no worn set");

        // The shown map's own NPC is unchanged, and the guard of map 1 resolves though he never left home.
        Assert.AreEqual(2, combat.HomeMap(Entity(2, 0)));
        Assert.AreEqual(1, combat.HomeMap(Entity(1, 1)));
        CollectionAssert.AreEqual(new[] { 1 }, source.Asked, "the home map was read once");
    }

    [TestMethod]
    public void The_map_keyed_questions_keep_their_meaning_and_the_identity_ones_do_not_depend_on_the_map()
    {
        var inventory = new AreaInventoryHost([Home(), Shown()]);
        var session = Session(inventory);
        var combat = new AreaCombatHost(inventory, session: () => session);
        var virgil = Entity(1, 0);

        // On the shown map there is no row for Virgil (the map-keyed question is about that map's own rows)...
        Assert.IsNull(combat.Fields(2, virgil));
        Assert.AreEqual(SessionCritterCombatFailure.NotAnNpc, combat.Build(2, virgil).Failure);
        // ...while the identity question finds the record wherever he stands.
        Assert.AreEqual(combat.Fields(1, virgil)!.Object.HitPoints, combat.Fields(virgil)!.Object.HitPoints);
        Assert.AreEqual(1, combat.Record(virgil)!.HomeMap);
        SameFacts(combat.Build(session, 1, virgil).Combat!.Facts, combat.Build(session, virgil).Combat!.Facts);
        Assert.IsNull(combat.Record("oid:" + new string('f', 64)), "an unknown entity has no record");
        Assert.IsNull(combat.Holder(session, "oid:" + new string('f', 64)));
        Assert.AreEqual(SessionCritterCombatFailure.NotAnNpc, combat.Build(session, "oid:" + new string('f', 64)).Failure);
    }

    [TestMethod]
    public void An_entity_the_session_does_not_place_and_no_map_lists_asks_no_source_and_an_unreadable_home_gives_no_record()
    {
        var source = new Source();
        var inventory = new AreaInventoryHost([Shown()], records: source);
        var session = Session(inventory, travelled: false);
        var combat = new AreaCombatHost(inventory, session: () => session);
        Assert.IsNull(combat.HomeMap(Entity(1, 0)), "no npc.map change: no home to read");
        Assert.AreEqual(0, source.Asked.Count);

        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(1), AreaCompanionPlacement.MapKey(Entity(1, 0)), 2).Disposition);
        Assert.IsNull(combat.HomeMap(Entity(1, 0)), "the source cannot read map 1");
        Assert.IsNull(combat.Fields(Entity(1, 0)));
        Assert.IsNull(combat.Holder(session, Entity(1, 0)));
        CollectionAssert.AreEqual(new[] { 1 }, source.Asked, "the unreadable map was asked once");
    }

    [TestMethod]
    public void The_entity_home_is_the_map_that_carries_the_away_change_and_a_restored_session_attaches_the_maps_read_on_demand_again()
    {
        var source = new Source(Home());
        var inventory = new AreaInventoryHost([Shown()], records: source);
        var session = Session(inventory);
        Assert.AreEqual(1, EntityHomes.HomeOf(session, Entity(1, 0)));
        Assert.IsNull(EntityHomes.HomeOf(session, Entity(1, 1)));
        var combat = new AreaCombatHost(inventory, session: () => session);
        Assert.IsNotNull(combat.Holder(session, Entity(1, 0)));

        // A new session (a load) gets every held map's seed again, those read on demand too.
        var loaded = Session(inventory);
        Assert.IsNotNull(loaded.QueryMapInventory(new(1)));
        Assert.IsNotNull(loaded.QueryMapInventory(new(2)));
        Assert.AreEqual(1, source.Asked.Count, "attaching did not read again");
    }

    [TestMethod]
    public void A_map_read_on_demand_that_the_session_refuses_does_not_refuse_the_session_and_its_npcs_have_no_holder()
    {
        // Map 3 (read on demand) lists the item of map 1: the session refuses a second map for one item (an object lies on one map).
        var clash = new AreaInventoryMap(3,
            new([new SessionSeedHolder(Entity(3, 0), SessionHolderKind.Npc, false, false, [], [Entity(1, 2)])],
                [new SessionSeedItem(Entity(1, 2), new(Healing, 1, 0, 0, null, []), null)]),
            new Dictionary<string, AreaObjectRecord> { [Entity(3, 0)] = Npc(3, 0), [Entity(1, 2)] = Item(1, 2) });
        var inventory = new AreaInventoryHost([Home(), Shown()], records: new Source(clash));
        var session = Session(inventory, travelled: false);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new(3), AreaCompanionPlacement.MapKey(Entity(3, 0)), 2).Disposition);
        var combat = new AreaCombatHost(inventory, session: () => session);
        Assert.AreEqual(3, combat.HomeMap(Entity(3, 0)), "the record resolves");
        Assert.IsNull(combat.Holder(session, Entity(3, 0)), "but the refused seed gives no holder");
        Assert.IsNotNull(combat.Holder(session, Entity(1, 0)), "the given maps are unaffected");

        // A later attach (a load) skips the refused read map instead of refusing the whole session.
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(Session(inventory, travelled: false)));
    }
}

/// <summary>ARC-DEV-072: the viewer's record source over the installed game (opt-in: <c>DAT_ROOT</c> = the game folder).</summary>
[TestClass]
public sealed class AreaMapRecordsRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Original_shop_map_is_read_by_runtime_number_and_the_main_map_window_is_refused()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var launch = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"),
            "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", "Arcanum1-024-fixed"]);
        var source = new AreaMapRecords(launch);

        // The shop map is runtime map 14 (the shop map test of AreaShopMerchantTests); the source builds the same records as the shop loader.
        var shop = AreaShopMap.Load(launch);
        Assert.AreEqual(AreaShopMapStatus.Ready, shop.Status, shop.Detail);
        var load = source.Load(shop.Map!.RuntimeMap, CancellationToken.None);
        Assert.IsNotNull(load.Map, load.Detail);
        Assert.AreEqual(shop.Map.RuntimeMap, load.Map.RuntimeMap);
        Assert.AreEqual(shop.Map.Inventory.Seed.Holders.Count, load.Map.Seed.Holders.Count);
        Assert.AreEqual(shop.Map.Inventory.Seed.Items.Count, load.Map.Seed.Items.Count);
        Assert.IsTrue(load.Map.Seed.Holders.Any(holder => holder.Entity == shop.Map.Merchant));

        // Through the resolver: the merchant of a map the run never held is found by identity once its home is named.
        var resolver = new RecordResolver([], source);
        var found = resolver.Resolve(shop.Map.Merchant, home: shop.Map.RuntimeMap);
        Assert.AreEqual(RecordStatus.Found, found.Status, found.Detail);
        Assert.AreEqual(shop.Map.RuntimeMap, found.Record!.HomeMap);

        // The main world map opens only as a window: refused with a reason, not read.
        var main = source.Load(1, CancellationToken.None);
        Console.WriteLine($"main map runtime 1: {main.Detail}");
        Assert.IsNull(main.Map);
        Assert.IsFalse(source.Load(9999, CancellationToken.None).Map is not null);
    }
}
