using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-031: the cast host's item lookup for the map's NPCs (invented values, no original data): an item an NPC holds is
/// its NPC's with the spell facts of its row, a container's or an unknown item is no NPC's, and the cast map places a carried item at
/// its holder through the lookup instead of at its stored location.</summary>
[TestClass]
public sealed class AreaNpcItemSpellsTests
{
    private const uint Carried = 0x1000;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A generic item (type 14) in the pack (no wear slot), carried at a stored location of (5, 5), with a spell in its first slot
    // and charges (the item block's slots 49 and 54), as the combat-field reader needs it.
    private static AreaObjectRecord Item(int index, int spell, int charges) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, 5, 5,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, Carried), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, spell), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, charges), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static AreaObjectRecord Holder(int index, int type, long x, long y) => new(Key(index), 0, type, 28359, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts() => new(new(SessionItemType.Generic, 15199, 6000, 6001), 1, 0, 0, null, []);

    private sealed class NoEvents : ISessionEventWorld
    {
        public Arcanum.Simulation.Combat.LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // An NPC (0) at (10, 20), dead with <paramref name="dead"/>, holding a wand (1, spell 171, 3 charges); a container (2) at
    // (12, 20) holding a ring (3, spell 172).
    private static (AreaCombatHost Combat, AreaInventoryHost Inventory, AreaObjectRecord[] Rows) Host(bool dead = false)
    {
        AreaObjectRecord[] rows = [Holder(0, 16, 10, 20), Item(1, 171, 3), Holder(2, 2, 12, 20), Item(3, 172, 1)];
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, dead, false, [], [Entity(1)]), new(Entity(2), SessionHolderKind.Container, false, false, [], [Entity(3)])],
                [new SessionSeedItem(Entity(1), Facts(), null), new SessionSeedItem(Entity(3), Facts(), null)]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        return (new AreaCombatHost(inventory), inventory, rows);
    }

    private static AreaStepSnapshot Steps()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 24; x++) for (var y = 0; y < 24; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    [TestMethod]
    public void An_item_an_npc_holds_is_its_npc_s_with_the_spell_facts_of_its_row()
    {
        using var session = ConversationSessionFixture.Create();
        var (combat, inventory, _) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var wand = combat.NpcItemSpells(session, 1, Entity(1))!;
        Assert.AreEqual((Entity(1), Entity(0), 14, 171, 3), (wand.Entity, wand.Holder, wand.Type, wand.Spells[0], wand.Charges));
        Assert.IsNull(combat.NpcItemSpells(session, 1, Entity(3)), "a container's item is no NPC's");
        Assert.IsNull(combat.NpcItemSpells(session, 1, Entity(0)), "the NPC itself is no item");
        Assert.IsNull(combat.NpcItemSpells(session, 1, "kit:1"), "an item no NPC of the map holds");
        Assert.IsNull(combat.NpcItemSpells(session, 2, Entity(1)), "a map the host does not cover");
    }

    // ARC-MAGIC-1336 (ledger IW44): an item of a container's list is a target of a spell on an item in an inventory; the cast host's lookup asks the container's list after the NPCs'.
    [TestMethod]
    public void An_item_a_container_holds_has_the_spell_facts_of_its_row_with_the_container_as_holder()
    {
        using var session = ConversationSessionFixture.Create();
        var (combat, inventory, _) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var ring = combat.ContainerItemSpells(session, Entity(3))!;
        Assert.AreEqual((Entity(3), Entity(2), 14, 172, 1), (ring.Entity, ring.Holder, ring.Type, ring.Spells[0], ring.Charges));
        Assert.IsNull(combat.ContainerItemSpells(session, Entity(1)), "an NPC's item is no container's");
        Assert.IsNull(combat.ContainerItemSpells(session, Entity(2)), "the container itself is no item");
        Assert.IsNull(combat.ContainerItemSpells(session, "kit:1"), "an item no container holds");
        var looted = session.LootItem(session.Version, Entity(2), Entity(3), _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionDisposition.Changed, looted.Disposition, looted.Failure.ToString());
        Assert.IsNull(combat.ContainerItemSpells(session, Entity(3)), "the session's next revision shows the item as the hero's");
    }

    // ARC-DEV-262: an item its NPC threw (or a critical dropped) lies loose and has left the NPC's list, but its spells are still its own: a thrown
    // grenade's explosion at the end of its flight reads them with the NPC that held it as the holder.
    [TestMethod]
    public void An_item_its_npc_threw_keeps_its_spell_facts_with_the_holder_it_came_from()
    {
        using var session = ConversationSessionFixture.Create();
        var (combat, inventory, _) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, new SessionMapId(1),
            MapChangeKey.ForEntity(Entity(1), GameSessionOwner.ItemDroppedProperty), GameSessionOwner.ItemDroppedValue(new(12, 22))).Disposition);
        Assert.IsNull(combat.NpcItemSpells(session, 1, Entity(1)), "it has left its NPC's list");
        var wand = combat.NpcItemSpells(session, Entity(1))!;
        Assert.AreEqual((Entity(1), Entity(0), 14, 171, 3), (wand.Entity, wand.Holder, wand.Type, wand.Spells[0], wand.Charges));
        Assert.IsNull(combat.NpcItemSpells(session, Entity(3)), "a container's item was never an NPC's");
        Assert.AreEqual(GameSessionDisposition.Changed, session.DestroyLyingItem(session.Version, Entity(1)).Disposition);
        Assert.IsNull(combat.NpcItemSpells(session, Entity(1)), "a destroyed item has no spells");
    }

    [TestMethod]
    public void A_dead_npc_s_item_is_its_own_until_the_hero_loots_it()
    {
        using var session = ConversationSessionFixture.Create();
        var (combat, inventory, _) = Host(dead: true);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        Assert.AreEqual(Entity(0), combat.NpcItemSpells(session, 1, Entity(1))?.Holder, "a dead NPC's items fire on its dying");
        var looted = session.LootItem(session.Version, Entity(0), Entity(1), _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionDisposition.Changed, looted.Disposition, looted.Failure.ToString());
        Assert.IsNull(combat.NpcItemSpells(session, 1, Entity(1)), "the session's next revision shows the item as the hero's");
    }

    [TestMethod]
    public void The_cast_host_places_an_npc_s_item_at_its_holder_through_the_lookup()
    {
        using var session = ConversationSessionFixture.Create();
        var (combat, inventory, rows) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var map = new AreaSpellCastMap(session, 1, Steps(), rows, combat, () => null, () => null);
        Assert.IsNull(map.Object(Entity(1)), "a carried item is no map object");
        Assert.AreEqual(new SpellLocation(10, 20), map.Object(Entity(0))!.Location);

        var caster = new SessionSpellCaster(session, _ => null, map, new NoEvents(), id => combat.NpcItemSpells(session, 1, id));
        Assert.AreEqual(Entity(0), caster.Item(Entity(1))!.Holder);
        var facts = caster.Facts(Entity(1))!;
        Assert.AreEqual((new SpellLocation(10, 20), SpellTargetObject.Inventory), (facts.Location, facts.ObjectFlags & SpellTargetObject.Inventory),
            "the item stands at its holder, in an inventory");
        Assert.IsNull(caster.Item(Entity(3)));
        Assert.IsNull(caster.Facts(Entity(3)), "a container's item the lookup does not know is no object either");
    }
}
