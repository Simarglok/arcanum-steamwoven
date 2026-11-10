using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-314 (B01): an item the hero took in one window of the main map keeps its shown name and weight after the window moves. A window is runtime map 1 each
/// time, so the next host holds the new window's rows only; the rows of the held items are handed over (<see cref="AreaInventoryHost.HeldRows"/>,
/// <see cref="AreaInventoryHost.Retain"/>). Before, the window listed "[unknown object]" for every item taken in the old window. Invented content, no original data.
/// </summary>
[TestClass]
public sealed class AreaInventoryHostHeldRowsTests
{
    private const int Map = 1;

    private static (GameSessionOwner Session, AreaInventoryMap Window) Session()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), Map, load).Owner!;
        var spawn = AreaGridSession.Cell(AreaGridSession.Spawn);
        var entity = AreaItemUseTests.Entity(1);
        // A second item of the window stays on the ground: only the held one is handed over.
        var seed = new SessionMapInventorySeed([],
            [new(entity, new(AreaItemUseTests.GenericKind, 7, 0, 0, null, []), new CellCoord(checked((int)spawn.X), checked((int)spawn.Y))),
             new(AreaItemUseTests.Entity(2), new(AreaItemUseTests.GenericKind, 3, 0, 0, null, []), new CellCoord(checked((int)spawn.X), checked((int)spawn.Y)))]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(Map), seed).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition);
        var row = AreaItemUseTests.Generic(1, AreaItemUseFacts.CanUseBoxFlag, 0);
        return (session, new(Map, seed, new Dictionary<string, Arcanum.Data.Area.AreaObjectRecord> { [entity] = row, [AreaItemUseTests.Entity(2)] = AreaItemUseTests.Generic(2, 0, 0) }));
    }

    private static string Line(AreaInventoryHost host, GameSessionOwner session) => host.View(session, null, Map).Pc.Single(line => line.Entity == AreaItemUseTests.Entity(1)).Text;

    [TestMethod]
    public void A_held_item_keeps_its_name_and_weight_after_the_window_moves()
    {
        var (session, window) = Session();
        using var _ = session;
        var before = new AreaInventoryHost([window]);
        var named = Line(before, session);
        Assert.IsFalse(named.Contains("[unknown object]", StringComparison.Ordinal), "the first window names the item: " + named);
        Assert.IsTrue(named.Contains("weight 7", StringComparison.Ordinal), "and gives its weight: " + named);

        var moved = new AreaInventoryHost([]);
        Assert.IsTrue(Line(moved, session).Contains("[unknown object]", StringComparison.Ordinal), "a host of the next window knows nothing of the row alone");
        moved.Retain(before.HeldRows(session));
        Assert.AreEqual(named, Line(moved, session), "the rows handed over give the same line");

        var again = new AreaInventoryHost([]);
        again.Retain(moved.HeldRows(session));
        Assert.AreEqual(named, Line(again, session), "a second window move keeps it");
    }

    [TestMethod]
    public void A_row_of_a_held_map_is_not_replaced_by_a_retained_one()
    {
        var (session, window) = Session();
        using var _ = session;
        var host = new AreaInventoryHost([window]);
        var entity = AreaItemUseTests.Entity(1);
        host.Retain(new Dictionary<string, AreaHeldRow> { [entity] = new(AreaItemUseTests.Generic(2, 0, 0), 99) });
        Assert.IsTrue(Line(host, session).Contains("weight 7", StringComparison.Ordinal), "the held map's weight wins");
    }

    [TestMethod]
    public void Only_the_items_the_hero_holds_are_handed_over()
    {
        var (session, window) = Session();
        using var _ = session;
        var host = new AreaInventoryHost([window]);
        var rows = host.HeldRows(session);
        CollectionAssert.AreEqual(new[] { AreaItemUseTests.Entity(1) }, rows.Keys.ToArray());
        Assert.AreEqual(0, new AreaInventoryHost([]).HeldRows(session).Count, "a host with no row of it has nothing to hand over");
    }

    /// <summary>ARC-DEV-336 (B51): the use script of a taken note asks for the item's row by its origin map and entity (<see cref="AreaInventoryHost.RowOn"/>); after a window
    /// move the origin map number is the new window's, which does not list the item, so the rows handed over answer first (the use said "not a row of a loaded map").</summary>
    [TestMethod]
    public void The_use_script_row_of_a_held_item_is_found_after_the_window_moves()
    {
        var (session, window) = Session();
        using var _ = session;
        var entity = AreaItemUseTests.Entity(1);
        var before = new AreaInventoryHost([window]);
        Assert.IsNotNull(before.RowOn(Map, entity), "the window the item was taken in lists it");

        var moved = new AreaInventoryHost([]);
        Assert.IsNull(moved.RowOn(Map, entity), "a host of the next window knows nothing of the row alone");
        moved.Retain(before.HeldRows(session));
        Assert.IsNotNull(moved.RowOn(Map, entity), "the rows handed over answer for the item");
        Assert.IsNull(moved.RowOn(Map, AreaItemUseTests.Entity(2)), "an item the hero does not hold is still no row");
    }
}
