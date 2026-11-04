using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-086 (MIG6c): the one create path and the one destroy path of the session's objects (GameSessionObjectLifecycle). The fixture is
// the junk-pile one: the half-elf's kit (kit:1..kit:3) and an apple the PC took from the exterior's seed. Values are invented.
[TestClass]
public sealed class ObjectLifecycleTests
{
    private static readonly CellCoord AppleCell = new(90, 200), There = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts AppleFacts = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { InventoryWidth = 2 };

    private sealed class World(bool shows) : ISessionLootWindowWorld
    {
        public bool LootWindowShowsContainer => shows;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple", AppleFacts, AppleCell)])).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    // A pile of kit:2 and the apple on There; kit:3 joins when asked.
    private static GameSessionOwner PileOwner(bool third = false)
    {
        var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        if (third) owner.PlaceHeroItem("kit:3", There);
        Assert.AreEqual(third ? 3 : 2, owner.QueryJunkPiles().Single().Items.Count);
        return owner;
    }

    private static SessionItemKind Kind(int prototype) => new(SessionItemType.Food, prototype, 0, 0);

    [TestMethod]
    public void DestroyingAnItemOfAPileOfTwoDissolvesItAtOnceAndTheLastItemIsDroppedAgain()
    {
        // B04: a path other than the decay step (any destroyer) used to leave the pile with one item or none standing.
        using var owner = PileOwner();
        owner.DestroyObject("kit:2");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "left with the apple, the pile dissolved");
        Assert.IsTrue(owner.IsItemDestroyed("kit:2"));
        Assert.AreEqual(("apple", There), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single());
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.DelayMs, owner.QueryItemDecays().Single(d => d.Entity == "apple").DeadlineMs,
            "the remaining item's two days start over");
        Assert.IsNull(owner.QueryObject("junk:1"), "the dissolved pile is gone from the directory");
    }

    [TestMethod]
    public void AnEmptiedPileStandsWhileTheWindowShowsItAndTheCloseRemovesIt()
    {
        using var owner = PileOwner();
        owner.DestroyObject("kit:2", windowShowsContainer: true);
        owner.DestroyObject("apple", windowShowsContainer: true);
        Assert.AreEqual(0, owner.QueryJunkPiles().Single().Items.Count, "an empty pile stands while the window shows it (JP5)");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "no empty pile stays after the close");
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);
    }

    [TestMethod]
    public void APileOfThreeStandsAfterOneDestroyAndItsRecordIsGone()
    {
        using var owner = PileOwner(third: true);
        owner.DestroyObject("kit:3");
        CollectionAssert.AreEqual(new[] { "kit:2", "apple" }, owner.QueryJunkPiles().Single().Items.Select(i => i.Entity).ToArray());
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject("kit:3")!.Place);
        Assert.IsFalse(owner.QueryItemDecays().Any(d => d.Entity == "kit:3"), "its decay ended");
    }

    [TestMethod]
    public void WhileTheLootWindowShowsAContainerADestroyLeavesThePileAndTheCloseDissolvesIt()
    {
        // JP5: the removal's check runs only when no container window is open; closing the window runs it.
        using var owner = PileOwner();
        owner.DestroyObject("kit:2", windowShowsContainer: true);
        Assert.AreEqual("apple", owner.QueryJunkPiles().Single().Items.Single().Entity, "the pile stands while the window shows it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
    }

    [TestMethod]
    public void TheDecayStepUsesTheSameDestroy()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 1000, ClockAdvanceReason.OrdinaryAdvance);
        owner.PlaceHeroItem("apple", There);
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ItemDecayRules.DelayMs - 1000, ClockAdvanceReason.OrdinaryAdvance);
        var fired = owner.ProcessEvents(new World(shows: false)).Decays.Single();
        Assert.AreEqual(("kit:2", ItemDecayStep.Destroy), (fired.Entity, fired.Step));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.AreEqual("apple", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
    }

    [TestMethod]
    public void DestroyingAPileEntityRemovesOnlyItsRecordAndItsItemsLieLoose()
    {
        using var owner = PileOwner();
        owner.DestroyObject("junk:1");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.IsNull(owner.QueryObject("junk:1"));
        CollectionAssert.AreEquivalent(new[] { "kit:2", "apple" }, owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray(), "the items stay where they lie");
        owner.PlaceHeroItem("kit:3", new CellCoord(121, 210));
        owner.PlaceHeroItem("kit:1", new CellCoord(121, 210));
        Assert.AreEqual("junk:2", owner.QueryJunkPiles().Single().Entity, "the destroyed pile's number is not given again (TM1)");
    }

    [TestMethod]
    public void ADecayedPoolIsDestroyedByTheNextLeaveAndItsNumberIsNotGivenAgain()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var map = new SessionMapId(79);
        foreach (var wolf in new[] { "oid:wolf", "oid:bear" })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter(wolf, new LifeFacts(), default)).Disposition);
        var first = owner.LeaveBloodPool(owner.Version, "oid:wolf", map, new(105, 200), 0, 0, default).Pool!;
        Assert.AreEqual("pool:1", first.Entity);
        Assert.AreEqual(SessionObjectPlace.Ground, owner.QueryObject("pool:1")!.Place);
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 2L * 365 * 24 * 3600 * 1000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject("pool:1")!.Place, "a decayed pool reads as destroyed");
        var second = owner.LeaveBloodPool(owner.Version, "oid:bear", map, new(106, 200), 0, 0, default).Pool!;
        Assert.AreEqual("pool:2", second.Entity);
        Assert.IsNull(owner.QueryObject("pool:1"), "the decayed pool's record was destroyed by the next creation");
        CollectionAssert.AreEqual(new[] { "pool:2" }, owner.QueryObjects(SessionObjectOrigin.BloodPool).Select(e => e.Entity).ToArray());
    }

    [TestMethod]
    public void ACreatedMadeItemIsRegisteredWhereItLandsWithItsWeightAndComplexity()
    {
        using var owner = Owner();
        var hero = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        Assert.AreEqual("made:1", hero);
        owner.CreateMadeItem(new(hero, Kind(10), SessionCreatePlace.HeroList, new(5, 0, 0)));
        Assert.AreEqual(SessionObjectPlace.HeroPack, owner.QueryObject(hero)!.Place);
        Assert.AreEqual(5, owner.QueryItemWeight(hero)!.Value.Weight);
        Assert.AreEqual("made:2", owner.NextObjectEntity(SessionObjectOrigin.Made), "the next made number follows the created one");

        var onGround = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        owner.CreateMadeItem(new(onGround, Kind(11), SessionCreatePlace.Ground, new(0, 0, 0), 3, Map: Exterior, Cell: There, Source: SessionPlacedSource.Made));
        var ground = owner.QueryObject(onGround)!;
        Assert.AreEqual((SessionObjectPlace.Ground, Exterior, There), (ground.Place, ground.Map, ground.Cell));
        Assert.AreEqual(3, owner.QueryPlacedItems().Single(p => p.Entity == onGround).Complexity);

        var held = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        owner.CreateMadeItem(new(held, Kind(12), SessionCreatePlace.NpcList, new(0, 0, 0), Holder: "oid:wolf"));
        var listed = owner.QueryObject(held)!;
        Assert.AreEqual((SessionObjectPlace.NpcList, "oid:wolf"), (listed.Place, listed.Holder));
        owner.DestroyObject(held);

        owner.DestroyObject(onGround);
        owner.DestroyObject(hero);
        Assert.IsNull(owner.QueryItemWeight(hero), "destroyed objects keep no weight facts");
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject(hero)!.Place);
        Assert.AreEqual("made:4", owner.NextObjectEntity(SessionObjectOrigin.Made), "a destroyed made number is not given again");
    }

    [TestMethod]
    public void TheJunkAndPoolEntitiesComeFromTheOneAllocatorAndNeverRepeat()
    {
        using var owner = PileOwner();
        Assert.AreEqual("junk:2", owner.NextObjectEntity(SessionObjectOrigin.JunkPile));
        Assert.AreEqual("pool:1", owner.NextObjectEntity(SessionObjectOrigin.BloodPool));
        owner.DestroyObject("junk:1");
        Assert.AreEqual("junk:2", owner.NextObjectEntity(SessionObjectOrigin.JunkPile), "the mark keeps the destroyed pile's number out");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.NextObjectEntity(SessionObjectOrigin.Kit));
    }
}
