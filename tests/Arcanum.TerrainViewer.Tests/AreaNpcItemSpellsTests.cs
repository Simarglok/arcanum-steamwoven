using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    // A generic item (type 14) carried at a stored location of (5, 5), with a spell in its first slot and charges (the item block's slots
    // 49 and 54), as the combat-field reader needs it.
    private static AreaObjectRecord Item(int index, int spell, int charges) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, 5, 5,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, Carried), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 1005), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, spell), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, charges), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static AreaObjectRecord Holder(int index, int type, long x, long y) => new(Key(index), 0, type, 28359, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts() => new(new(SessionItemType.Generic, 15199, 6000, 6001), 1, 0, 1005, null, []);

    private sealed class NoEvents : ISessionEventWorld
    {
        public Arcanum.Simulation.Combat.LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // An NPC (0) at (10, 20) holding a wand (1, spell 171, 3 charges); a container (2) at (12, 20) holding a ring (3, spell 172).
    private static (AreaCombatHost Combat, AreaInventoryHost Inventory, AreaObjectRecord[] Rows) Host()
    {
        AreaObjectRecord[] rows = [Holder(0, 16, 10, 20), Item(1, 171, 3), Holder(2, 2, 12, 20), Item(3, 172, 1)];
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)]), new(Entity(2), SessionHolderKind.Container, false, false, [], [Entity(3)])],
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
