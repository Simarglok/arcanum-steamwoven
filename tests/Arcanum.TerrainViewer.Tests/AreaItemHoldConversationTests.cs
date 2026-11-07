using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-194: the substitute container of a dialog NPC (BT11, 0x45F650) for the in and ni conditions, over an invented map: the container within 20 tiles of the NPC's row, not
/// beyond it, and none for an NPC the hero leads. Entities and cells are invented.</summary>
[TestClass]
public sealed class AreaItemHoldConversationTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Row(int index, int type, long x, long y) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(int prototype) => new(new(SessionItemType.Generic, prototype, 4200 + prototype, 34200), 1, 0, 0, null, []);

    // 0 a near merchant at (31,25) with its chest 1 at (33,25); 2 a far merchant at (31,60) with the same kind of chest 3 at (33,25) (35 tiles off).
    private static AreaInventoryMap Map() => new(14, new SessionMapInventorySeed(
            [
                new(Entity(0), SessionHolderKind.Npc, false, true, [], []),
                new(Entity(1), SessionHolderKind.Container, false, false, [], [Entity(4)]),
                new(Entity(2), SessionHolderKind.Npc, false, true, [], []),
                new(Entity(3), SessionHolderKind.Container, false, false, [], []),
            ],
            [new(Entity(4), Facts(7), null)]),
        new[] { Row(0, 16, 31, 25), Row(1, 2, 33, 25), Row(2, 16, 31, 60), Row(3, 2, 33, 25), Row(4, 14, 33, 25) }.ToDictionary(row => Entity(row.Key.SourceIndex)),
        substitutes: new Dictionary<string, AreaSubstituteContainer>
        {
            [Entity(0)] = new(Entity(1), 5, new(33, 25)),
            [Entity(2)] = new(Entity(3), 5, new(33, 25)),
        });

    [TestMethod]
    public void A_container_within_20_tiles_of_the_npc_is_its_substitute_and_a_farther_one_is_not()
    {
        using var session = ConversationSessionFixture.Create(14);
        var host = new AreaInventoryHost([Map()]);
        Assert.AreEqual(Entity(1), host.SubstituteContainer(session, Entity(0)), "2 tiles away");
        Assert.IsNull(host.SubstituteContainer(session, Entity(2)), "35 tiles away");
        Assert.IsNull(host.SubstituteContainer(session, Entity(1)), "a container has none");
        Assert.IsNull(host.SubstituteContainer(session, "oid:" + new string('f', 64)), "an unknown NPC has none");
    }
}
