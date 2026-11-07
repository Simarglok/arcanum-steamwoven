using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-025 (ledger OD1–OD4, OD12, DH9, GT94–GT97): damage that is no hero attack — a script's, a spell's, an acid's, a burn's — reaches an item
/// lying on the shown map's ground and an item a critter wears, through the cast map's item rules (invented rows, no original data): a guard (0) wearing a body
/// armour (1) with a cup (2) in its pack, a vase (3) on the ground at (10, 22) and a chest (4) with a pearl (5) in it.</summary>
[TestClass]
public sealed class AreaSpellCastItemDamageTests
{
    private const int VaseX = 10, VaseY = 22;
    private static readonly SessionItemKind Generic = new(SessionItemType.Generic, 15199, 6000, 6001);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A generic item (type 14): hit points 10, material 4 (flesh), normal resistance 50, at an inventory location; lying (positioned) when x is given.
    private static AreaObjectRecord Item(int index, int location, long? x = null, long? y = null) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s",
        AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30, 50), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, location), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static AreaObjectRecord Armour(int index) => new(Key(index), 0, 7, 8114, "p", "p", "r", "s", AreaScope.Core, null, null, null,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30, 20), Empty(31),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, (long)(CombatItemArt.ItemArtType << 28)), Field(43, 1006),
            Field(44, 0x60000000), Field(45, 0), Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000),
            Field(54, 0), Field(55, 0),
            Field(59, 0x7), Field(61, 0), Field(62, 0), Array(63, 20, 0, 0, 0, 0), Array(64), Field(65, 0), Field(66, 0), Field(67, 0),
        ], true, true, true);

    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 8, 8, 8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47), Field(57, 5), Field(63, 0),
            Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    // A container (type 2) with the common block and its type flags (36) and key (38).
    private static AreaObjectRecord Chest(int index) => new(Key(index), 0, 2, 1000, "p", "p", "r", "s", AreaScope.Core, null, 12, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x30000400), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 12), Field(27, 0), Field(28, 0), Field(29, 2),
            Array(30, 0), Empty(31), Field(36, 0), Field(38, 0), Field(24, -1),
        ], true, true, true);

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 30; x++) for (var y = 0; y < 30; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private sealed class NoEvents : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed record Fixture(GameSessionOwner Session, AreaSpellCastMap Map, AreaObjectTargets Targets, SessionSpellCaster Caster, AreaObjectRecord[] Rows, AreaCombatHost Combat);

    private static Fixture Create()
    {
        AreaObjectRecord[] rows = [Npc(0), Armour(1), Item(2, 0), Item(3, 0, VaseX, VaseY), Chest(4), Item(5, 0)];
        var inventoryMap = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1), Entity(2)]),
                    new(Entity(4), SessionHolderKind.Container, false, false, [], [Entity(5)])],
                [new SessionSeedItem(Entity(1), new(new(SessionItemType.Armor, 8114, 6002, 6003), 1, 0, 1006, null, []), null),
                    new SessionSeedItem(Entity(2), new(Generic, 1, 0, 0, null, []), null),
                    new SessionSeedItem(Entity(3), new(Generic, 1, 0, 0, null, []), new CellCoord(VaseX, VaseY)),
                    new SessionSeedItem(Entity(5), new(Generic, 1, 0, 0, null, []), null)]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([inventoryMap]);
        var session = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory);
        var targets = new AreaObjectTargets(1, rows, new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), inventoryMap);
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, combat, () => null, () => null, objects: row => targets.Target(session, row.Key));
        return new(session, map, targets, new SessionSpellCaster(session, _ => null, map, new NoEvents(), _ => null), rows, combat);
    }

    private static AttackDamage Hit(int points) =>
        AttackResolver.DamageStep(new() { Attacker = new Combatant(), Target = new Combatant(), TargetKind = AttackTargetKind.OtherObject, Damage = DamageByType.Zero.With(DamageType.Normal, points) });

    [TestMethod]
    public void AnItemOnTheGroundAnswersItsMaterialAndTakesAScriptsDamageThroughItsDurability()
    {
        var f = Create();
        var vase = f.Map.Id(f.Rows[3]);
        var (defender, kind) = f.Map.ObjectCombat(vase)!.Value;
        Assert.AreEqual((AttackTargetKind.OtherObject, ObjectMaterial.Flesh, 50), (kind, defender.Material, defender.Resistances.Normal));
        // 10 normal damage, unresistable: the item loses 4 of its 10 hit points (no material reduction for an item).
        var first = f.Caster.ApplyScriptDamage(vase, 4, 0, unresistable: true);
        Assert.IsTrue(first.Applied);
        Assert.AreEqual(4, first.HitPointDamage);
        Assert.AreEqual(4, f.Session.QueryItemCondition(Entity(3), new ItemCondition(10, 0, 0, false))!.Value.Damage);
        var hits = f.Map.TakeItemHits();
        Assert.AreEqual((vase, ItemBreakStep.None, SessionObjectHostSteps.None), (hits.Single().Id, hits.Single().Item.Break, hits.Single().Host));
        Assert.AreEqual(0, f.Map.TakeItemHits().Count, "taken once");
        Assert.AreEqual(0, f.Map.TakeObjectHits().Count, "an item is no object hit");

        // The resistance of 50 % halves a resisted 8 into 4: 4 + 4 + 4 more than the 10 breaks it (BUST script, bust sound), the next deletes it.
        Assert.AreEqual(4, f.Caster.ApplyScriptDamage(vase, 8, 0, unresistable: false).HitPointDamage);
        var broke = f.Caster.ApplyScriptDamage(vase, 2, 0, unresistable: true);
        Assert.IsTrue(broke.Applied);
        Assert.AreEqual((ItemBreakStep.Broken, SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound),
            (f.Map.TakeItemHits().Last().Item.Break, SessionObjectHostSteps.BustScript | SessionObjectHostSteps.BustSound));
        Assert.IsTrue(f.Caster.ApplyScriptDamage(vase, 100, 0, unresistable: true).Applied);
        Assert.IsTrue(f.Session.IsItemDestroyed(Entity(3)));
        Assert.IsFalse(f.Caster.ApplyScriptDamage(vase, 1, 0, unresistable: true).Applied, "a deleted item takes nothing");
        Assert.AreEqual(0, f.Map.GroundItemsAt(new(VaseX, VaseY)).Count, "and burns no more");
    }

    [TestMethod]
    public void AnItemInAChestOrAPackIsOutOfReachAndAnNpcRowAnswersNone()
    {
        var f = Create();
        Assert.IsNull(f.Map.ObjectCombat(f.Map.Id(f.Rows[0])), "an NPC is a critter");
        Assert.IsNull(f.Map.ObjectCombat(Entity(5)), "a pearl in a chest");
        Assert.IsFalse(f.Map.DamageObject(Entity(5), Hit(3)));
        Assert.IsFalse(f.Map.DamageObject(Entity(2), Hit(3)), "a cup in a pack, never listed as worn");
        Assert.AreEqual(0, f.Map.TakeItemHits().Count);
    }

    [TestMethod]
    public void ABurnHurtsTheItemsLyingOnTheTileAndTheBurnerCountsThemForTheRoundGate()
    {
        var f = Create();
        var vase = f.Map.Id(f.Rows[3]);
        CollectionAssert.AreEqual(new[] { vase }, f.Map.GroundItemsAt(new(VaseX, VaseY)).ToArray());
        Assert.AreEqual(0, f.Map.GroundItemsAt(new(VaseX + 1, VaseY)).Count);
        var burned = f.Caster.ApplyBurn(vase, FireDamageRules.Damage(normal: false, doubled: false));
        Assert.IsTrue(burned.Applied);
        Assert.AreEqual(3, burned.HitPointDamage, "3 fire, the item has no fire resistance");
        Assert.AreEqual(3, f.Session.QueryItemCondition(Entity(3), new ItemCondition(10, 0, 0, false))!.Value.Damage);

        var burner = new AreaFireBurner(f.Session, () => f.Caster, _ => [], _ => null, cell => f.Map.GroundItemsAt(cell));
        Assert.AreEqual(1, burner.Burn(new CellCoord(VaseX, VaseY), normal: true, doubled: true), "the item is a victim");
        Assert.AreEqual((1, 1, 1), (burner.Reached, burner.Hurt, (int)burner.HitPointsTaken), "1 normal doubled is 2, half of it resisted");
        Assert.AreEqual(0, burner.Burn(new CellCoord(VaseX + 1, VaseY), true, false), "an empty tile reaches nobody");
        Assert.AreEqual(4, f.Session.QueryItemCondition(Entity(3), new ItemCondition(10, 0, 0, false))!.Value.Damage);
    }

    [TestMethod]
    public void AnAcidsWornItemsAreTheNpcsWornItemsAndTakeTheDamageUntilTheyBreakOffItsBuild()
    {
        var f = Create();
        var guard = Entity(0);
        var worn = f.Map.WornItems(guard);
        var armour = worn.Single();
        Assert.AreEqual((Entity(1), (int)EquipmentSlot.Armor, 20), (armour.Id, armour.Slot, armour.NormalResistance));
        var (defender, kind) = f.Map.ObjectCombat(Entity(1))!.Value;
        Assert.AreEqual((AttackTargetKind.OtherObject, 20), (kind, defender.Resistances.Normal));
        Assert.IsTrue(f.Map.DamageObject(Entity(1), Hit(4)));
        Assert.AreEqual(4, f.Session.QueryItemCondition(Entity(1), new ItemCondition(10, 0, 0, false))!.Value.Damage);
        Assert.AreEqual(1, f.Map.TakeItemHits().Count);

        // The first break takes it off (the build loses its resistance), so the next acid lists nothing and the stale id is no target any more.
        Assert.IsTrue(f.Map.DamageObject(Entity(1), Hit(6)));
        Assert.AreEqual(ItemBreakStep.Broken, f.Map.TakeItemHits().Single().Item.Break);
        Assert.AreEqual(0, f.Map.WornItems(guard).Count);
        Assert.IsNull(f.Map.ObjectCombat(Entity(1)), "no longer worn");
        Assert.IsFalse(f.Map.DamageObject(Entity(1), Hit(1)));
    }

    [TestMethod]
    public void AnUnlistedItemOrWithoutTheItemHostTakesNone()
    {
        var f = Create();
        // Never listed as worn: the id is unknown to the map.
        Assert.IsNull(f.Map.ObjectCombat(Entity(1)));
        Assert.IsFalse(f.Map.DamageObject(Entity(1), Hit(2)));
        var plain = new AreaSpellCastMap(f.Session, 1, Steps(), f.Rows, f.Combat, () => null, () => null);
        Assert.IsNull(plain.ObjectCombat(plain.Id(f.Rows[3])), "without the object host a lying item is not ready");
        Assert.AreEqual(0, plain.GroundItemsAt(new(VaseX, VaseY)).Count);
    }
}
