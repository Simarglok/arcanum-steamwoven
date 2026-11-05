using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-104 (MIG9 S4): the viewer's residency open, arrival pre-cache and view square over invented rows of map 1; the hero stands at
// 100,200 (sector 1,3). No original data.
[TestClass]
public sealed class AreaResidencyTests
{
    private static readonly CellCoord HeroCell = new(100, 200);

    private static GameSessionOwner Session()
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        return created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
    }

    private static MapResidentRowsBuild Build(params SessionResidentRow[] rows) => new(MapResidentRowsStatus.Ready, rows, string.Empty);

    [TestMethod]
    public void TheArrivalPreCacheIsTheThreeByThreeSectorsAroundTheHeroAndTheViewSquareItsCorners()
    {
        CollectionAssert.AreEqual(new CellCoord[] { new(0, 128), new(64, 128), new(128, 128), new(0, 192), new(64, 192), new(128, 192), new(0, 256), new(64, 256), new(128, 256) },
            AreaResidency.ArrivalCells(HeroCell).ToArray());
        Assert.AreEqual(4, AreaResidency.ArrivalCells(new(5, 5)).Count, "no sector left of or above the map");
        CollectionAssert.AreEqual(new CellCoord[] { new(84, 184), new(116, 184), new(84, 216), new(116, 216) }, AreaResidency.ViewCells(HeroCell).ToArray());
        CollectionAssert.AreEqual(new CellCoord[] { new(0, 0), new(19, 0), new(0, 19), new(19, 19) }, AreaResidency.ViewCells(new(3, 3)).ToArray());
    }

    [TestMethod]
    public void OpeningFoldsTheNpcsAroundTheHeroAndTheSessionAnswersWhichSectorsAreLoaded()
    {
        using var session = Session();
        var map = new SessionMapId(1);
        var opened = AreaResidency.Open(session, map, Build(
            new("oid:near", SessionResidentKind.Npc, new(90, 190)), new("oid:far", SessionResidentKind.Npc, new(600, 600))), new(16, 16), HeroCell);
        Assert.AreEqual(ResidencyStatus.Open, opened.Residency.Status);
        Assert.AreEqual(SectorsOpenStatus.Open, opened.Sectors);
        Assert.AreEqual(GameSessionFailure.None, opened.Arrival!.Failure);
        Assert.AreEqual(9, opened.Arrival.Folds.Count);
        CollectionAssert.AreEqual(new[] { "oid:near" }, opened.Arrival.Folds.SelectMany(f => f.Npcs).ToArray());
        Assert.IsTrue(session.QueryAiTick("oid:near")!.Value.WakeUp);
        Assert.IsNull(session.QueryAiTick("oid:far"));
        Assert.IsTrue(session.QuerySectorLoaded(map, ResidentSector.Of(new(90, 190)))!.Value);
        Assert.IsFalse(session.QuerySectorLoaded(map, ResidentSector.Of(new(600, 600)))!.Value);

        // A refused build opens neither the residency nor the cache.
        using var other = Session();
        var refused = AreaResidency.Open(other, map, new MapResidentRowsBuild(MapResidentRowsStatus.WindowOnly, null, "window"), new(16, 16), HeroCell);
        Assert.AreEqual(ResidencyStatus.InvalidRows, refused.Residency.Status);
        Assert.IsNull(refused.Sectors);
        Assert.IsNull(other.QuerySectorLoaded(map, new(0, 0)));
    }

    // ARC-DEV-118 (MIG9 S5b): a window of the main map opens from the map's whole mobile read and its size in sectors, once: a window move finds the cache and opens nothing.
    [TestMethod]
    public void AWindowsMapOpensFromItsWholeMobileReadOnceAndAMoveKeepsTheCache()
    {
        using var session = Session();
        var map = new SessionMapId(1);
        var whole = new AreaWholeMobile("main", Build(new("oid:near", SessionResidentKind.Npc, new(90, 190)), new("oid:far", SessionResidentKind.Npc, new(4000, 4000))), new(100, 100));
        var opened = AreaResidency.OpenWhole(session, map, whole, HeroCell)!;
        Assert.AreEqual(ResidencyStatus.Open, opened.Residency.Status);
        Assert.AreEqual(SectorsOpenStatus.Open, opened.Sectors);
        Assert.AreEqual(new SectorMapExtent(100, 100), opened.Extent);
        Assert.AreEqual(9, opened.Arrival!.Folds.Count, "the 3 x 3 arrival pre-cache");
        Assert.AreEqual(true, session.QuerySectorLoaded(map, ResidentSector.Of(new(90, 190))));
        Assert.AreEqual(false, session.QuerySectorLoaded(map, ResidentSector.Of(new(4000, 4000))), "a row far away is a resident, its sector is not loaded");
        Assert.IsNull(AreaResidency.OpenWhole(session, map, whole, HeroCell), "a second window finds the map's cache and opens nothing");

        // A whole read the map's rows do not fit (a row past the extent) opens neither residency nor cache, with the reason.
        using var other = Session();
        var tooSmall = AreaResidency.OpenWhole(other, map, new("main", whole.Rows, new(10, 10)), HeroCell)!;
        Assert.AreEqual(ResidencyStatus.Open, tooSmall.Residency.Status);
        Assert.AreEqual(SectorsOpenStatus.InvalidExtent, tooSmall.Sectors);
        Assert.IsNull(other.QuerySectorLoaded(map, new(0, 0)));
    }
}
