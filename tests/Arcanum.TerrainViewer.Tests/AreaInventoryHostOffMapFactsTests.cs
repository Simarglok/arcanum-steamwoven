using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-306 (B32): the facts of an item the hero took on another map. The window's use, wear and move read the item's object row; the
/// row of an item taken on a map the host does not hold is read through the resolver from the map the session names as its origin, so a
/// note taken on the main map is still used in the cave (before, "use facts do not read here"). Invented content, no original data.
/// </summary>
[TestClass]
public sealed class AreaInventoryHostOffMapFactsTests
{
    private const int TakenOn = 7;

    private sealed class Source(params AreaInventoryMap[] maps) : IMapRecordSource
    {
        public readonly List<int> Asked = [];

        public MapRecordsLoad Load(int runtimeMap, CancellationToken token)
        {
            Asked.Add(runtimeMap);
            return maps.FirstOrDefault(map => map.RuntimeMap == runtimeMap) is { } found ? new(found, string.Empty) : new(null, $"map {runtimeMap} unreadable");
        }
    }

    private static (GameSessionOwner Session, AreaInventoryMap Home) Session()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), TakenOn, load).Owner!;
        var spawn = AreaGridSession.Cell(AreaGridSession.Spawn);
        var entity = AreaItemUseTests.Entity(1);
        var seed = new SessionMapInventorySeed([],
            [new(entity, new(AreaItemUseTests.GenericKind, 1, 0, 0, null, []), new CellCoord(checked((int)spawn.X), checked((int)spawn.Y)))]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(TakenOn), seed).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition);
        var row = AreaItemUseTests.Generic(1, AreaItemUseFacts.CanUseBoxFlag, 0);
        return (session, new(TakenOn, seed, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord> { [entity] = row }));
    }

    [TestMethod]
    public void An_item_taken_on_a_map_the_host_does_not_hold_reads_its_facts_through_the_resolver()
    {
        var (session, home) = Session();
        using var _ = session;
        var entity = AreaItemUseTests.Entity(1);
        var source = new Source(home);
        var host = new AreaInventoryHost([], records: source);

        var facts = host.UseFacts(session, entity, null);
        Assert.IsNotNull(facts, "the use facts read from the origin map's row");
        Assert.IsTrue(facts.CanUseBox, "the stored item flags are the row's");
        Assert.AreEqual(AreaItemUseGate.Use, AreaItemUse.Gate(AreaItemUseSource.RightClick, facts, false, false, false));
        Assert.IsNotNull(host.GridMoveItem(session, entity, null), "the move facts read too");
        CollectionAssert.AreEqual(new[] { TakenOn }, source.Asked, "the origin map was read once, on demand");
    }

    [TestMethod]
    public void A_held_map_answers_without_the_source_and_a_host_with_neither_still_refuses()
    {
        var (session, home) = Session();
        using var _ = session;
        var entity = AreaItemUseTests.Entity(1);
        var source = new Source(home);
        var held = new AreaInventoryHost([home], records: source);
        Assert.IsTrue(held.UseFacts(session, entity, null)!.CanUseBox);
        Assert.AreEqual(0, source.Asked.Count, "a held map is not read again");

        Assert.IsNull(new AreaInventoryHost([]).UseFacts(session, entity, null), "no map and no source: the facts do not read");
        Assert.IsNull(new AreaInventoryHost([], records: new Source()).UseFacts(session, entity, null), "a source that cannot read the origin map: the facts do not read");
    }
}
