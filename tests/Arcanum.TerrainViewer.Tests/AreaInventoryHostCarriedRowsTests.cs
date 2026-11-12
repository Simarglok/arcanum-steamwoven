using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-354: an item the hero took in one window of the main map, in a run that never handed its row over (a fresh run loading the save, or a load into
/// another window): the host of the shown window reads the row by identity from the item's origin map, the window around the object's stored cell
/// (<see cref="AreaInventoryHost.ReadCarriedRows"/>, <see cref="IMapRecordSource.LoadAround"/>). Before, the item had no row at all: "[unknown object]",
/// no facts, no use. Invented content, no original data.
/// </summary>
[TestClass]
public sealed class AreaInventoryHostCarriedRowsTests
{
    private const int Map = 1;

    private static (GameSessionOwner Session, AreaInventoryMap Window) Session()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), Map, load).Owner!;
        var spawn = AreaGridSession.Cell(AreaGridSession.Spawn);
        var cell = new CellCoord(checked((int)spawn.X), checked((int)spawn.Y));
        var seed = new SessionMapInventorySeed([],
            [new(AreaItemUseTests.Entity(1), new(AreaItemUseTests.GenericKind, 7, 0, 0, null, []), cell),
             new(AreaItemUseTests.Entity(2), new(AreaItemUseTests.GenericKind, 3, 0, 0, null, []), cell)]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(Map), seed).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, AreaItemUseTests.Entity(1), _ => SessionInsertVerdict.Accepted).Disposition);
        return (session, new(Map, seed, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord>
        {
            [AreaItemUseTests.Entity(1)] = AreaItemUseTests.Generic(1, AreaItemUseFacts.CanUseBoxFlag, 0),
            [AreaItemUseTests.Entity(2)] = AreaItemUseTests.Generic(2, 0, 0),
        }));
    }

    /// <summary>Another window of the same map: it lists nothing of the first.</summary>
    private static AreaInventoryMap OtherWindow() => new(Map, new SessionMapInventorySeed([], []) { Window = true }, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord>());

    /// <summary>A record source that reads the window around an object's stored cell: <paramref name="window"/> for the entities it lists.</summary>
    private sealed class WindowSource(AreaInventoryMap? window) : IMapRecordSource
    {
        public List<string> Asked { get; } = [];
        public MapRecordsLoad Load(int runtimeMap, CancellationToken token) => new(null, "a window-only map");
        public MapRecordsLoad LoadAround(int runtimeMap, string entity, CancellationToken token)
        {
            Asked.Add(entity);
            return window is not null && window.TryGetRow(entity, out _) ? new(window, "the window around the stored cell") : new(null, "no stored cell");
        }
    }

    private static string Line(AreaInventoryHost host, GameSessionOwner session) => host.View(session, null, Map).Pc.Single(line => line.Entity == AreaItemUseTests.Entity(1)).Text;

    [TestMethod]
    public void A_fresh_host_of_another_window_reads_the_carried_row_from_its_origin_window()
    {
        var (session, window) = Session();
        using var _ = session;
        var named = Line(new AreaInventoryHost([window]), session);
        var source = new WindowSource(window);
        var fresh = new AreaInventoryHost([OtherWindow()], records: source);
        Assert.IsTrue(Line(fresh, session).Contains("[unknown object]", StringComparison.Ordinal), "before the read the other window knows nothing of the item");

        Assert.AreEqual((1, 0), fresh.ReadCarriedRows(session), "the one carried item is read");
        Assert.AreEqual(named, Line(fresh, session), "the same name and weight as in the window it was taken in");
        Assert.IsNotNull(fresh.UseFacts(session, AreaItemUseTests.Entity(1), null), "its use facts read");
        Assert.IsNotNull(fresh.RowOn(Map, AreaItemUseTests.Entity(1)), "its use script finds its row");
        Assert.IsNotNull(fresh.KnownRow(AreaItemUseTests.Entity(1)), "the host knows the row without reading");
        CollectionAssert.AreEqual(new[] { AreaItemUseTests.Entity(1) }, source.Asked, "only the carried item was asked for, once");

        Assert.AreEqual((0, 0), fresh.ReadCarriedRows(session), "a second call (a load this host keeps) reads nothing again");
        Assert.AreEqual(1, source.Asked.Count);
        var next = new AreaInventoryHost([]);
        next.Retain(fresh.HeldRows(session));
        Assert.AreEqual(named, Line(next, session), "the row read is handed over at the next window move like any other");
    }

    [TestMethod]
    public void A_carried_row_that_no_source_gives_stays_unknown_and_is_counted()
    {
        var (session, _) = Session();
        using var owner = session;
        var bare = new AreaInventoryHost([OtherWindow()]);
        Assert.AreEqual((0, 1), bare.ReadCarriedRows(session), "no source: the item is missing");
        Assert.IsTrue(Line(bare, session).Contains("[unknown object]", StringComparison.Ordinal));

        var empty = new AreaInventoryHost([OtherWindow()], records: new WindowSource(null));
        Assert.AreEqual((0, 1), empty.ReadCarriedRows(session), "a source with no window of it: missing, nothing guessed");
        Assert.IsNull(empty.KnownRow(AreaItemUseTests.Entity(1)));
    }

    [TestMethod]
    public void A_row_the_host_already_holds_is_not_read()
    {
        var (session, window) = Session();
        using var _ = session;
        var source = new WindowSource(window);
        var host = new AreaInventoryHost([window], records: source);
        Assert.AreEqual((0, 0), host.ReadCarriedRows(session), "the shown window lists it");
        var moved = new AreaInventoryHost([OtherWindow()], records: source);
        moved.Retain(host.HeldRows(session));
        Assert.AreEqual((0, 0), moved.ReadCarriedRows(session), "a row handed over is not read again");
        Assert.AreEqual(0, source.Asked.Count);
    }

    /// <summary>ARC-DEV-1151: a row no source gives is asked once, the next host of a map change is told and does not ask again; a load the host keeps asks once more.</summary>
    [TestMethod]
    public void An_unreadable_carried_row_is_not_asked_again_by_the_next_host()
    {
        var (session, window) = Session();
        using var owner = session;
        var first = new WindowSource(null);
        var host = new AreaInventoryHost([OtherWindow()], records: first);
        Assert.AreEqual((0, 1), host.ReadCarriedRows(session));
        CollectionAssert.AreEqual(new[] { AreaItemUseTests.Entity(1) }, first.Asked);
        var unread = host.UnreadCarried(session);
        Assert.AreEqual(1, unread.Count, "the host names the item it could not read");

        var second = new WindowSource(null);
        var next = new AreaInventoryHost([OtherWindow()], records: second);
        next.RetainUnread(unread);
        Assert.AreEqual((0, 0), next.ReadCarriedRows(session), "told at the map change: not asked again");
        Assert.AreEqual(0, second.Asked.Count);
        Assert.AreEqual(1, next.UnreadCarried(session).Count, "and still handed on to the host after it");

        var fresh = new AreaInventoryHost([OtherWindow()], records: new WindowSource(null));
        Assert.AreEqual((0, 1), fresh.ReadCarriedRows(session), "a host nobody told (a fresh run, a load) asks once");

        var again = new WindowSource(window);
        var kept = new AreaInventoryHost([OtherWindow()], records: again);
        kept.RetainUnread(unread);
        Assert.AreEqual((0, 0), kept.ReadCarriedRows(session));
        Assert.AreEqual((1, 0), kept.ReadCarriedRows(session, retryUnread: true), "a load the host keeps asks again, and the row now reads");
        Assert.AreEqual(0, kept.UnreadCarried(session).Count, "a row that read is no longer unread");
    }
}
