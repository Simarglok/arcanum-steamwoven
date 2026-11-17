using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-2493 (ledger DD1–DD8): the script destroy of a map NPC runs the original's inventory-on-destroy (0x4639E0): only the items with object flag 0x400000 are taken out and dropped. Invented numbers only.</summary>
[TestClass]
public sealed class DestroyDropStateTests
{
    private static readonly CellCoord Spot = new(101, 202);

    private static GameSessionOwner WithNpc(params (string Name, bool Invulnerable, bool NoDrop)[] items)
    {
        var owner = Create();
        var facts = InventoryStateTests.Facts(SessionItemType.Food, 3600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], items.Select(item => item.Name).ToArray())],
            items.Select(item => new SessionSeedItem(item.Name, facts with { Invulnerable = item.Invulnerable, NoDrop = item.NoDrop }, null)).ToArray())).Failure);
        return owner;
    }

    private static long? Changed(GameSessionOwner owner, string entity, string property) => owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(entity, property));

    [TestMethod]
    public void OnlyTheInvulnerableItemsAreTakenOutAndDroppedAndTheNpcIsDestroyed()
    {
        using var owner = WithNpc(("bread", false, false), ("relic", true, false), ("sword", false, false), ("amulet", true, false));
        var before = owner.Version;
        var result = owner.DestroyMapNpc(before, Exterior, "npc-1", Spot);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(1L, Changed(owner, "npc-1", GameSessionOwner.ObjectDestroyedProperty));
        foreach (var lying in new[] { "relic", "amulet" })
        {
            Assert.IsNotNull(Changed(owner, lying, GameSessionOwner.ItemDroppedProperty), lying + " lies on the ground");
            Assert.IsFalse(owner.IsItemDestroyed(lying), lying);
        }
        foreach (var stays in new[] { "bread", "sword" })
        {
            Assert.IsNull(Changed(owner, stays, GameSessionOwner.ItemDroppedProperty), stays + " stays in the destroyed list");
            Assert.IsFalse(owner.IsItemDestroyed(stays), stays + " is neither destroyed nor dropped");
        }
        CollectionAssert.AreEquivalent(new[] { "relic", "amulet" }, owner.QueryItemDecays().Select(decay => decay.Entity).ToArray(), "a drop schedules its decay");
    }

    [TestMethod]
    public void TheTakeOutRunsNoRemoveCheckSoANoDropInvulnerableItemGoesToo()
    {
        using var owner = WithNpc(("cursed", true, true));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot).Disposition);
        Assert.IsNotNull(Changed(owner, "cursed", GameSessionOwner.ItemDroppedProperty));
    }

    [TestMethod]
    public void TheItemsGatherIntoAJunkPileOnTheNpcsCell()
    {
        using var owner = WithNpc(("a", true, false), ("b", true, false), ("c", true, false));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot).Disposition);
        Assert.IsTrue(owner.QueryJunkPiles().Count > 0, "the drop rule (0x466E50) piles the second and later items");
        Assert.IsTrue(owner.QueryJunkPiles().All(pile => pile.Cell == Spot && pile.Map == Exterior));
        // The walk runs last item first (DD3): c lies alone, then b gathers c into a new pile, then a joins it.
        CollectionAssert.AreEqual(new[] { "c", "b", "a" }, owner.QueryJunkPiles().Single().Items.Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void ARefusedDropRefusesTheWholeDestroyAndTheRefusalIsAskedBeforehand()
    {
        using var owner = WithNpc(("relic", true, false));
        var bad = new CellCoord(-1, 5);
        Assert.AreEqual(GameSessionFailure.DropTileUnfit, owner.DestroyMapNpcRefusal(Exterior, "npc-1", bad));
        Assert.IsNull(owner.DestroyMapNpcRefusal(Exterior, "npc-1", Spot));
        Assert.IsNull(owner.DestroyMapNpcRefusal(Exterior, "npc-9", bad), "an NPC with nothing to drop has no cell to fit");
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", bad).Disposition);
        Assert.IsNull(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty));
        Assert.IsNull(Changed(owner, "npc-1", GameSessionOwner.ObjectDestroyedProperty));
    }

    [TestMethod]
    public void AnInvulnerableItemTheHeroGaveTheNpcIsTakenOutAsAPlacedItemAndTheSaveRestores()
    {
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], [])],
            [new SessionSeedItem("gem", plain with { Invulnerable = true }, new CellCoord(101, 200)), new SessionSeedItem("dull", plain, new CellCoord(101, 200))])).Failure);
        foreach (var entity in new[] { "gem", "dull" })
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
        }
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot).Disposition);
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("gem", SessionPlacedSource.Pc, Exterior, Spot), (placed.Entity, placed.Source, placed.Map, placed.Cell));
        Assert.IsFalse(owner.IsItemDestroyed("dull"), "the item without the flag is neither dropped nor destroyed");
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEquivalent(owner.QueryPlacedItems().ToArray(), back.QueryPlacedItems().ToArray());
    }

    [TestMethod]
    public void ANpcWithNoInvulnerableItemOrNoInventoryOnlyGetsTheDestroy()
    {
        using var plain = WithNpc(("bread", false, false));
        Assert.AreEqual(GameSessionDisposition.Changed, plain.DestroyMapNpc(plain.Version, Exterior, "npc-1", Spot).Disposition);
        Assert.AreEqual(1L, Changed(plain, "npc-1", GameSessionOwner.ObjectDestroyedProperty));
        Assert.AreEqual(0, plain.QueryItemDecays().Count);
        using var bare = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, bare.DestroyMapNpc(bare.Version, Exterior, "npc-9", Spot).Disposition);
        Assert.AreEqual(1L, Changed(bare, "npc-9", GameSessionOwner.ObjectDestroyedProperty));
    }

    [TestMethod]
    public void ADestroyedNpcIsLeftAsItIsAndAStaleVersionOrABadCellIsRefused()
    {
        using var owner = WithNpc(("relic", true, false));
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot).Disposition, "TD2: a destroyed object is left as it is");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DestroyMapNpc(stale, Exterior, "npc-1", Spot).Failure);
        using var other = WithNpc(("relic", true, false));
        Assert.AreEqual(GameSessionFailure.DropTileUnfit, other.DestroyMapNpc(other.Version, Exterior, "npc-1", new(-1, 5)).Failure);
        Assert.IsNull(Changed(other, "npc-1", GameSessionOwner.ObjectDestroyedProperty), "a refused destroy changes nothing");
    }

    [TestMethod]
    public void TheDropRestoresFromASave()
    {
        using var owner = WithNpc(("relic", true, false), ("amulet", true, false));
        owner.DestroyMapNpc(owner.Version, Exterior, "npc-1", Spot);
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty), Changed(back, "relic", GameSessionOwner.ItemDroppedProperty));
        CollectionAssert.AreEquivalent(owner.QueryItemDecays().ToArray(), back.QueryItemDecays().ToArray());
        CollectionAssert.AreEquivalent(owner.QueryJunkPiles().Select(pile => (pile.Cell, string.Join(",", pile.Items.Select(item => item.Entity)))).ToArray(),
            back.QueryJunkPiles().Select(pile => (pile.Cell, string.Join(",", pile.Items.Select(item => item.Entity)))).ToArray());
        Assert.AreEqual(Changed(owner, "npc-1", GameSessionOwner.ObjectDestroyedProperty), Changed(back, "npc-1", GameSessionOwner.ObjectDestroyedProperty));
    }

    // ARC-WORLD-2502 (DD1–DD8): the script destroy of a container runs the same inventory-on-destroy (0x4639E0).
    private static GameSessionOwner WithContainer(params (string Name, bool Invulnerable, bool NoDrop)[] items)
    {
        var owner = Create();
        var facts = InventoryStateTests.Facts(SessionItemType.Food, 3600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("crate-1", SessionHolderKind.Container, false, false, [], items.Select(item => item.Name).ToArray())],
            items.Select(item => new SessionSeedItem(item.Name, facts with { Invulnerable = item.Invulnerable, NoDrop = item.NoDrop }, null)).ToArray())).Failure);
        return owner;
    }

    private static SessionObjectCondition Condition(GameSessionOwner owner, string entity) =>
        owner.QueryObjectCondition(Exterior, entity, Arcanum.Simulation.Combat.DamageableObjectKind.Container);

    [TestMethod]
    public void OnlyTheInvulnerableItemsOfADestroyedContainerAreTakenOutAndDropped()
    {
        using var owner = WithContainer(("bread", false, false), ("relic", true, false), ("sword", false, false), ("amulet", true, true), ("rune", true, false));
        var result = owner.DestroyMapContainer(owner.Version, Exterior, "crate-1", Spot, junkPile: false);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, Condition(owner, "crate-1"));
        foreach (var lying in new[] { "relic", "amulet", "rune" })
        {
            Assert.IsNotNull(Changed(owner, lying, GameSessionOwner.ItemDroppedProperty), lying + " lies on the ground");
            Assert.AreEqual(Spot, owner.QueryDroppedItem(lying)?.Cell, lying + " at the container's cell");
            Assert.IsFalse(owner.IsItemDestroyed(lying), lying);
        }
        foreach (var stays in new[] { "bread", "sword" })
        {
            Assert.IsNull(Changed(owner, stays, GameSessionOwner.ItemDroppedProperty), stays + " stays in the destroyed list");
            Assert.IsNull(owner.QueryDroppedItem(stays), stays);
            Assert.IsFalse(owner.IsItemDestroyed(stays), stays + " is neither destroyed nor dropped");
        }
        CollectionAssert.AreEquivalent(new[] { "relic", "amulet", "rune" }, owner.QueryItemDecays().Select(decay => decay.Entity).ToArray(), "a drop schedules its decay");
        Assert.IsTrue(owner.QueryJunkPiles().Count > 0 && owner.QueryJunkPiles().All(pile => pile.Cell == Spot), "the second and later items gather into a junk pile (DD5)");
        // Not a spill: no spill cell, so the items that stayed do not lie on the ground.
        Assert.AreEqual(3, owner.QueryMapInventory(Exterior)!.Ground.Count + owner.QueryMapInventory(Exterior)!.JunkPiles.Sum(pile => pile.Items.Count));
    }

    [TestMethod]
    public void AJunkPileIsOnlyDestroyedAndALeftContainerGetsTheDestroyAlone()
    {
        using var pile = WithContainer(("relic", true, false));
        Assert.AreEqual(GameSessionDisposition.Changed, pile.DestroyMapContainer(pile.Version, Exterior, "crate-1", Spot, junkPile: true).Disposition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, Condition(pile, "crate-1"));
        Assert.IsNull(Changed(pile, "relic", GameSessionOwner.ItemDroppedProperty), "DD2: a junk pile returns at once");
        Assert.AreEqual(0, pile.QueryItemDecays().Count);
        using var bare = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, bare.DestroyMapContainer(bare.Version, Exterior, "crate-9", Spot, junkPile: false).Disposition);
        Assert.AreEqual(SessionObjectCondition.Destroyed, Condition(bare, "crate-9"));
    }

    [TestMethod]
    public void ADestroyedOrBrokenContainerIsLeftAsItIsAndAStaleVersionOrABadCellIsRefused()
    {
        using var owner = WithContainer(("relic", true, false));
        var stale = owner.Version;
        var bad = new CellCoord(-1, 5);
        Assert.AreEqual(GameSessionFailure.DropTileUnfit, owner.DestroyMapContainerRefusal(Exterior, "crate-1", bad, junkPile: false));
        Assert.IsNull(owner.DestroyMapContainerRefusal(Exterior, "crate-1", bad, junkPile: true), "a junk pile drops nothing");
        Assert.IsNull(owner.DestroyMapContainerRefusal(Exterior, "crate-9", bad, junkPile: false), "a container with nothing to drop has no cell to fit");
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.DestroyMapContainer(owner.Version, Exterior, "crate-1", bad, junkPile: false).Disposition);
        Assert.AreEqual(SessionObjectCondition.Whole, Condition(owner, "crate-1"), "a refused destroy changed nothing");
        Assert.IsNull(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapContainer(owner.Version, Exterior, "crate-1", Spot, junkPile: false).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DestroyMapContainer(owner.Version, Exterior, "crate-1", Spot, junkPile: false).Disposition, "TD2: a destroyed object is left as it is");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DestroyMapContainer(stale, Exterior, "crate-1", Spot, junkPile: false).Failure);
        Assert.IsNull(owner.DestroyMapContainerRefusal(Exterior, "crate-1", bad, junkPile: false), "a destroyed container is left as it is");
        // A container that broke (its items spilled) is already destroyed: the script destroy leaves it.
        using var broken = WithContainer(("relic", true, false));
        var target = new SessionObjectTarget(Exterior, "crate-1", new SessionObjectBaseline
        {
            Kind = Arcanum.Simulation.Combat.DamageableObjectKind.Container, Points = 6, Material = Arcanum.Simulation.Combat.ObjectMaterial.Cloth, Cell = Spot,
            Openable = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Container, 0, 0, 0),
        });
        Assert.AreEqual(GameSessionDisposition.Changed, broken.DamageObject(broken.Version, target, 100).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, broken.DestroyMapContainer(broken.Version, Exterior, "crate-1", Spot, junkPile: false).Disposition);
    }

    [TestMethod]
    public void TheContainersDroppedItemsAndItsDestroyComeBackFromASave()
    {
        using var owner = WithContainer(("relic", true, false), ("bread", false, false));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapContainer(owner.Version, Exterior, "crate-1", Spot, junkPile: false).Disposition);
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(SessionObjectCondition.Destroyed, Condition(back, "crate-1"));
        Assert.IsNotNull(Changed(back, "relic", GameSessionOwner.ItemDroppedProperty));
    }
}
