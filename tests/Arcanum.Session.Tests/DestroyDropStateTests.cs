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
    }
}
