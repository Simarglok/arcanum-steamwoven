using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-103 (MIG9 S3, K1 §3.2): the residents of the open map over an invented map, no original data. The exterior (the hero's map, the hero at
// 100,200 in sector 1,3) lists a guard and a smith (NPCs), a chest (container) holding a bag, a sword the guard holds, an apple and a stone on the
// ground, a dynamic blood-pool-like scenery and a detached ring. Cells and ids are invented.
[TestClass]
public sealed class ResidencyStateTests
{
    private const string Guard = "oid:guard", Smith = "oid:smith", Chest = "oid:chest", Bag = "oid:bag", Sword = "oid:sword", Apple = "oid:apple",
        Stone = "oid:stone", Flame = "oid:flame", Ring = "oid:ring", Hero = "pc";
    private static readonly CellCoord GuardCell = new(10, 20), SmithCell = new(70, 20), ChestCell = new(130, 200), AppleCell = new(90, 200),
        StoneCell = new(91, 200), FlameCell = new(12, 22);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionResidentRow[] Rows() =>
    [
        new(Guard, SessionResidentKind.Npc, GuardCell), new(Smith, SessionResidentKind.Npc, SmithCell),
        new(Chest, SessionResidentKind.Container, ChestCell), new(Bag, SessionResidentKind.Generic, null, Chest), new(Sword, SessionResidentKind.Weapon, null, Guard),
        new(Apple, SessionResidentKind.Food, AppleCell), new(Stone, SessionResidentKind.Generic, StoneCell),
        new(Flame, SessionResidentKind.Scenery, FlameCell) { Dynamic = true }, new(Ring, SessionResidentKind.Generic, null),
    ];

    private static GameSessionOwner Owner(bool seed = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        if (seed)
        {
            var food = InventoryStateTests.Facts(SessionItemType.Food, 3600);
            var generic = InventoryStateTests.Facts(SessionItemType.Generic, 3601);
            var weapon = InventoryStateTests.Facts(SessionItemType.Weapon, 3602);
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new SessionMapInventorySeed(
                [new SessionSeedHolder(Chest, SessionHolderKind.Container, false, false, [], [Bag]), new SessionSeedHolder(Guard, SessionHolderKind.Npc, false, false, [], [Sword]),
                 new SessionSeedHolder(Smith, SessionHolderKind.Npc, false, false, [], [])],
                [new SessionSeedItem(Apple, food, AppleCell), new SessionSeedItem(Stone, generic, StoneCell), new SessionSeedItem(Bag, generic, null),
                 new SessionSeedItem(Sword, weapon, null)])).Failure);
        }
        Assert.AreEqual(new ResidencyOpenResult(ResidencyStatus.Open, 9, string.Empty), owner.OpenResidency(Exterior, Rows()));
        return owner;
    }

    private static SessionResident Of(GameSessionOwner owner, string entity) => owner.QueryResidents(Exterior).Residents.Single(r => r.Entity == entity);

    private static string[] In(GameSessionOwner owner, int x, int y) =>
        owner.QueryResidentsIn(Exterior, new ResidentSector(x, y)).Residents.Select(r => r.Entity).ToArray();

    [TestMethod]
    public void TheOpenMapsResidentsAreItsRowsAndTheHero()
    {
        using var owner = Owner();
        Assert.AreEqual(Exterior, owner.QueryResidencyMap());
        var all = owner.QueryResidents(Exterior);
        Assert.AreEqual(ResidencyStatus.Open, all.Status);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3", Apple, Bag, Chest, Flame, Guard, Ring, Smith, Stone, Sword, Hero },
            all.Residents.Select(r => r.Entity).ToArray(), "entity order, ordinal; the hero's kit is the hero's");
        Assert.IsTrue(all.Residents.Where(r => r.Entity.StartsWith("kit:")).All(r => r.Holder == Hero && r.Origin == SessionResidentOrigin.Created));
        Assert.AreEqual(new SessionResident(Guard, SessionResidentKind.Npc, SessionResidentOrigin.MapData, GuardCell, null, false, false), Of(owner, Guard));
        Assert.AreEqual(new SessionResident(Hero, SessionResidentKind.Pc, SessionResidentOrigin.Hero, Start.Cell, null, false, false), Of(owner, Hero));
        Assert.AreEqual(Chest, Of(owner, Bag).Holder);
        Assert.IsNull(Of(owner, Bag).Sector, "a held item has no tile");
        Assert.IsNull(Of(owner, Ring).Cell);
        Assert.IsNull(Of(owner, Ring).Holder, "a detached item is a resident with no place");
        Assert.AreEqual(new ResidentSector(1, 0), Of(owner, Smith).Sector);

        // The sector index answers any sector, held and detached items left out.
        CollectionAssert.AreEqual(new[] { Flame, Guard }, In(owner, 0, 0));
        CollectionAssert.AreEqual(new[] { Smith }, In(owner, 1, 0));
        CollectionAssert.AreEqual(new[] { Apple, Stone, Hero }, In(owner, 1, 3), "the hero at 100,200 is in sector 1,3");
        CollectionAssert.AreEqual(new[] { Chest }, In(owner, 2, 3));
        Assert.AreEqual(0, In(owner, 40, 40).Length, "an empty sector answers none");
        CollectionAssert.AreEqual(new[] { Apple, Stone }, owner.QueryResidentsInCells(Exterior, new(90, 200), new(91, 200)).Residents.Select(r => r.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { Apple, Chest, Flame, Guard, Smith, Stone, Hero },
            owner.QueryResidentsInCells(Exterior, new(int.MinValue, int.MinValue), new(int.MaxValue, int.MaxValue)).Residents.Select(r => r.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryResidentsInCells(Exterior, new(5, 5), new(4, 5)).Residents.Count);
    }

    [TestMethod]
    public void OnlyTheHerosMapOpensAndARefusalKeepsThePreviousResidency()
    {
        using var owner = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidents(Exterior).Status);
        Assert.IsNull(owner.QueryResidencyMap());
        Assert.AreEqual(ResidencyStatus.NotHeroMap, owner.OpenResidency(Cave, Rows()).Status);
        var version = owner.Version;
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.AreEqual(version, owner.Version, "derived data: no revision");
        Assert.AreEqual(ResidencyStatus.LimitExceeded, owner.OpenResidency(Exterior, Rows(), new ResidencyLimits(8)).Status);
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows(), new ResidencyLimits(9)).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.OpenResidency(Exterior, Rows(), new ResidencyLimits(0)));

        SessionResidentRow[][] invalid =
        [
            [.. Rows(), new("kit:1", SessionResidentKind.Generic, new(1, 1))],
            [.. Rows(), new("sector:5:2", SessionResidentKind.Wall, new(1, 1)) { Dynamic = true }],
            [.. Rows(), new("oid:wall", SessionResidentKind.Wall, new(1, 1))],
            [.. Rows(), new("oid:pc", SessionResidentKind.Pc, new(1, 1))],
            [.. Rows(), new("oid:x", (SessionResidentKind)18, new(1, 1))],
            [.. Rows(), new("oid:x", SessionResidentKind.Generic, new(1, 1), Chest)],
            [.. Rows(), new("oid:x", SessionResidentKind.Generic, new(-1, 1))],
            [.. Rows(), new("oid:x", SessionResidentKind.Npc, null, Chest)],
            [.. Rows(), new("oid:x", SessionResidentKind.Generic, null, Apple)],
            [.. Rows(), new("oid:x", SessionResidentKind.Generic, null, "oid:nobody")],
            [.. Rows(), new(Guard, SessionResidentKind.Npc, GuardCell)],
        ];
        foreach (var rows in invalid) Assert.AreEqual(ResidencyStatus.InvalidRows, owner.OpenResidency(Exterior, rows).Status, rows[^1].ToString());
        Assert.AreEqual(10, owner.QueryResidents(Exterior).Residents.Count, "the residency opened before stays (no kit: the hero has none)");
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, [.. Rows(), new("oid:door", SessionResidentKind.Portal, new(1, 1)) { Dynamic = true }]).Status,
            "a dynamic sector type is a resident");
        Assert.IsTrue(owner.CloseResidency());
        Assert.IsFalse(owner.CloseResidency());
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidentsIn(Exterior, new(0, 0)).Status);
    }

    [TestMethod]
    public void TheHerosMapChangeAndARestoreCloseTheResidency()
    {
        using var owner = Owner();
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidents(Cave).Status, "another map has none");
        Changed(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, 1);
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidents(Exterior).Status);
        Changed(owner.PlaceActor(owner.Version, Start), owner, 2);
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidents(Exterior).Status, "the map closed: coming back opens it again");
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Changed(owner.PlaceActor(owner.Version, Start with { Cell = new(300, 300) }), owner, 3);
        Assert.AreEqual(new CellCoord(300, 300), Of(owner, Hero).Cell, "a step on the map keeps it and moves the hero");
        Changed(owner.ApplyChanges(owner.Version, Start with { Map = Cave }, []), owner, 4);
        Assert.AreEqual(ResidencyStatus.NotOpen, owner.QueryResidents(Exterior).Status, "a change of place closes it too");

        using var other = Owner();
        using var restored = Restore(other.Capture());
        Assert.AreEqual(ResidencyStatus.NotOpen, restored.QueryResidents(Exterior).Status, "not saved: a load opens it again");
    }

    [TestMethod]
    public void AnNpcIsWhereTheSessionLeftItAndAwayNpcsTakeTheirItems()
    {
        using var owner = Owner();
        // A walk into another sector (MIG7c) moves it in the index.
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(200, 20), NeighborDirection.XPlus), owner, 1);
        Assert.AreEqual(new CellCoord(200, 20), Of(owner, Guard).Cell);
        CollectionAssert.AreEqual(new[] { Flame }, In(owner, 0, 0));
        CollectionAssert.AreEqual(new[] { Guard }, In(owner, 3, 0));
        // A companion's cell on the map wins over a walking resident's place.
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Guard, "npc.cell"), SessionNpcPlacement.Encode(new(5, 5), NeighborDirection.XPlus)), owner, 2);
        Assert.AreEqual(new CellCoord(5, 5), Of(owner, Guard).Cell);
        CollectionAssert.AreEqual(new[] { Flame, Guard }, In(owner, 0, 0));
        // Away on another map: not a resident here, nor the sword it holds.
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Guard, "npc.map"), Cave.RuntimeMap), owner, 3);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity is Guard or Sword));
        CollectionAssert.AreEqual(new[] { Flame }, In(owner, 0, 0));
    }

    [TestMethod]
    public void AVisitorStandsWhereItsCellIsRecordedOnTheOpenMap()
    {
        using var owner = Owner();
        const string Virgil = "oid:virgil";
        Changed(owner.SetMapChange(owner.Version, Cave, MapChangeKey.ForEntity(Virgil, "npc.cell"), SessionNpcPlacement.Encode(new(1, 1), NeighborDirection.XPlus)), owner, 1);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity == Virgil), "a cell on another map is not here");
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Virgil, "npc.cell"), SessionNpcPlacement.Encode(new(101, 201), NeighborDirection.XPlus)), owner, 2);
        Assert.AreEqual(new SessionResident(Virgil, SessionResidentKind.Npc, SessionResidentOrigin.Visitor, new(101, 201), null, false, false), Of(owner, Virgil));
        CollectionAssert.AreEqual(new[] { Apple, Stone, Virgil, Hero }, In(owner, 1, 3));
    }

    [TestMethod]
    public void OffAndDestroyedResidentsStayListedAndCarryTheGamesExclusion()
    {
        using var owner = Owner();
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Smith, "obj.off"), 1), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Guard, "obj.destroyed"), 1), owner, 2);
        Assert.AreEqual((true, false, true), (Of(owner, Smith).Off, Of(owner, Smith).Destroyed, Of(owner, Smith).Excluded));
        Assert.AreEqual((false, true, true), (Of(owner, Guard).Off, Of(owner, Guard).Destroyed, Of(owner, Guard).Excluded));
        CollectionAssert.AreEqual(new[] { Smith }, In(owner, 1, 0), "the index keeps it: the caller applies the exclusion");
        // The data's flag, and a record that clears it.
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, [.. Rows().Where(r => r.Entity != Stone), new(Stone, SessionResidentKind.Generic, StoneCell) { Off = true }]).Status);
        Assert.IsTrue(Of(owner, Stone).Off);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Stone, "obj.off"), 0), owner, 3);
        Assert.IsFalse(Of(owner, Stone).Off);
    }

    // ARC-WORLD-1436: a toggle state flips the object flag of the original, so a resident the data stores off is on once the session cleared the bit (the NPC of the first building), and off again when it sets it.
    [TestMethod]
    public void ADataOffResidentTheSessionSwitchedOnIsOnAndOffAgainWhenSwitchedOff()
    {
        using var owner = Owner();
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, [.. Rows().Where(r => r.Entity != Stone), new(Stone, SessionResidentKind.Generic, StoneCell) { Off = true }]).Status);
        Assert.IsTrue(Of(owner, Stone).Off);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Stone, SessionObjectFlagField.Object, 0x2, false, dataHas: true).Disposition);
        Assert.IsFalse(Of(owner, Stone).Off, "the session's change over the data's");
        Assert.IsFalse(Of(owner, Stone).Excluded);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Stone, SessionObjectFlagField.Object, 0x2, true, dataHas: false).Disposition);
        Assert.IsTrue(Of(owner, Stone).Off);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Smith, SessionObjectFlagField.Object, 0x2, true, dataHas: false).Disposition);
        Assert.IsTrue(Of(owner, Smith).Off, "a resident the data stores on, switched off by the flag");
    }

    [TestMethod]
    public void ItemsFollowTheHeroTheGroundAndTheirHolders()
    {
        using var owner = Owner(seed: true);
        // Taken: the hero holds it.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        Assert.AreEqual((Hero, (CellCoord?)null, SessionResidentOrigin.MapData), (Of(owner, Apple).Holder, Of(owner, Apple).Cell, Of(owner, Apple).Origin));
        CollectionAssert.AreEqual(new[] { Stone, Hero }, In(owner, 1, 3));
        // The hero's kit is the hero's too.
        Assert.AreEqual((Hero, SessionResidentOrigin.Created, (CellCoord?)null), (Of(owner, "kit:1").Holder, Of(owner, "kit:1").Origin, Of(owner, "kit:1").Cell));
        // Placed: on the ground; a second item on the cell makes a junk pile that holds both.
        var there = new CellCoord(300, 20);
        owner.PlaceHeroItem(Apple, there);
        Assert.AreEqual((there, (string?)null), (Of(owner, Apple).Cell, Of(owner, Apple).Holder));
        CollectionAssert.AreEqual(new[] { Apple }, In(owner, 4, 0));
        owner.PlaceHeroItem("kit:2", there);
        Assert.AreEqual(new SessionResident("junk:1", SessionResidentKind.Container, SessionResidentOrigin.Created, there, null, false, false), Of(owner, "junk:1"));
        Assert.AreEqual(("junk:1", "junk:1"), (Of(owner, Apple).Holder, Of(owner, "kit:2").Holder));
        CollectionAssert.AreEqual(new[] { "junk:1" }, In(owner, 4, 0));
    }

    [TestMethod]
    public void ACriticalsDropAndASpillLeaveItemsOnTheGround()
    {
        using var owner = Owner(seed: true);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Sword, GameSessionOwner.ItemDroppedProperty),
            GameSessionOwner.ItemDroppedValue(new(11, 20))), owner, 1);
        Assert.AreEqual((new CellCoord(11, 20), (string?)null), (Of(owner, Sword).Cell!.Value, Of(owner, Sword).Holder));
        CollectionAssert.AreEqual(new[] { Flame, Guard, Sword }, In(owner, 0, 0));
        // A container destroyed with a spill cell: its items lie there and it is destroyed.
        var spill = new CellCoord(131, 201);
        var word = (long)SessionObjectCondition.Destroyed | ((((long)spill.Y << 24) | (uint)spill.X) + 1) << 8;
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Chest, GameSessionOwner.ObjectConditionProperty), word), owner, 2);
        Assert.AreEqual((spill, (string?)null), (Of(owner, Bag).Cell!.Value, Of(owner, Bag).Holder));
        Assert.IsTrue(Of(owner, Chest).Destroyed);
        CollectionAssert.AreEqual(new[] { Bag, Chest }, In(owner, 2, 3));
    }

    [TestMethod]
    public void DeletedGivenAndCarriedItemsHaveTheirPlaces()
    {
        using var owner = Owner(seed: true);
        // Deleted: gone.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, "kit:3", new ItemCondition(10, 0, 0, false)).Disposition);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity == "kit:3"));
        // Given to an NPC: held by it, while the NPC is here; an NPC away takes it along.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, Smith, "kit:1", Accept).Disposition);
        Assert.AreEqual((Smith, SessionResidentOrigin.Created), (Of(owner, "kit:1").Holder, Of(owner, "kit:1").Origin));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Smith, "npc.map"), Cave.RuntimeMap).Disposition);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity is Smith or "kit:1"));
        // A map object with no row here (taken before this map's residency opened) is another map's: a visitor the hero carries.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows().Where(r => r.Entity != Apple).ToArray()).Status);
        Assert.AreEqual(new SessionResident(Apple, SessionResidentKind.Food, SessionResidentOrigin.Visitor, null, Hero, false, false), Of(owner, Apple));
        // Placed on another map: not here.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start with { Map = Cave }).Disposition);
        owner.PlaceHeroItem("kit:2", new(5, 5));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start).Disposition);
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, Rows()).Status);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity == "kit:2"));
        Assert.AreEqual((Hero, SessionResidentOrigin.MapData), (Of(owner, Apple).Holder, Of(owner, Apple).Origin));
    }

    [TestMethod]
    public void ABloodPoolIsAResidentUntilItDecays()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter(Guard, new LifeFacts(), default)).Disposition);
        var pool = owner.LeaveBloodPool(owner.Version, Guard, Exterior, new(11, 21), 0, 0, default).Pool!;
        Assert.AreEqual(new SessionResident(pool.Entity, SessionResidentKind.Scenery, SessionResidentOrigin.Created, new(11, 21), null, false, false), Of(owner, pool.Entity));
        CollectionAssert.AreEqual(new[] { Flame, Guard, pool.Entity }, In(owner, 0, 0), "the dead guard stays a resident");
        Assert.AreEqual(Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token,
            Arcanum.Simulation.Combat.BloodPoolRules.ObjectDecayDelayMs, Arcanum.Simulation.Clock.ClockAdvanceReason.OrdinaryAdvance).Disposition);
        CollectionAssert.AreEqual(new[] { Flame, Guard }, In(owner, 0, 0), "a decayed pool is gone");
    }

    [TestMethod]
    public void ARowThatIsNoMobileIdentityReadsTheMapsOwnChanges()
    {
        using var owner = Owner();
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, [.. Rows(), new("bandit", SessionResidentKind.Npc, new(1, 1))]).Status);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("bandit", "obj.off"), 1), owner, 1);
        Assert.IsTrue(Of(owner, "bandit").Off);
        Changed(owner.SetMapChange(owner.Version, Cave, MapChangeKey.ForEntity("bandit", "obj.destroyed"), 1), owner, 2);
        Assert.IsFalse(Of(owner, "bandit").Destroyed, "another map's change is another object's");
    }

    [TestMethod]
    public void EveryResidentOnTheGroundIsInItsSectorAfterChanges()
    {
        using var owner = Owner(seed: true);
        Changed(owner.RecordNpcPlace(owner.Version, Exterior, Guard, new CellCoord(200, 20), NeighborDirection.XPlus), owner, 1);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, Apple, Accept).Disposition);
        owner.PlaceHeroItem(Apple, new(300, 20));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Sword, GameSessionOwner.ItemDroppedProperty),
            GameSessionOwner.ItemDroppedValue(new(64, 64))).Disposition);
        var all = owner.QueryResidents(Exterior).Residents;
        var located = all.Where(r => r.Sector is not null).ToArray();
        foreach (var sector in located.Select(r => r.Sector!.Value).Distinct())
            CollectionAssert.AreEqual(located.Where(r => r.Sector == sector).ToArray(), owner.QueryResidentsIn(Exterior, sector).Residents.ToArray(), sector.ToString());
        CollectionAssert.AreEqual(located, owner.QueryResidentsInCells(Exterior, new(0, 0), new(1 << 20, 1 << 20)).Residents.ToArray());
        Assert.AreEqual(new ResidentSector(1, 1), Of(owner, Sword).Sector);
    }
}
