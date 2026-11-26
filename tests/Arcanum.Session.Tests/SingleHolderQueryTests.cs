using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-RENDER-3126: the one-holder query the per-heartbeat readers use answers as the whole map's inventory lists that holder. Entities and numbers are invented.</summary>
[TestClass]
public sealed class SingleHolderQueryTests
{
    private static void EveryHolderMatches(GameSessionOwner owner, string step)
    {
        var listed = owner.QueryMapInventory(Exterior)!.Holders;
        Assert.AreNotEqual(0, listed.Count, step);
        foreach (var holder in listed)
        {
            var one = owner.QueryMapHolder(Exterior, holder.Entity);
            Assert.IsNotNull(one, $"{step}: {holder.Entity}");
            Assert.AreEqual((holder.Entity, holder.Kind, holder.Dead), (one.Entity, one.Kind, one.Dead), step);
            CollectionAssert.AreEqual(holder.Items.ToArray(), one.Items.ToArray(), $"{step}: {holder.Entity}");
        }
    }

    [TestMethod]
    public void AMapWithNoSeedHasNoInventoryAndNoHolder()
    {
        using var owner = Create();
        Assert.IsFalse(owner.HasMapInventory(Exterior));
        Assert.IsNull(owner.QueryMapHolder(Exterior, "virgil"));
    }

    [TestMethod]
    public void OneHolderIsListedAsInTheWholeMapsInventoryThroughItsChanges()
    {
        using var owner = InventoryStateTests.Attached();
        Assert.IsTrue(owner.HasMapInventory(Exterior));
        Assert.IsNull(owner.QueryMapHolder(Exterior, "nobody"), "a holder the seed does not list");
        EveryHolderMatches(owner, "attached");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", "note", _ => SessionInsertVerdict.Accepted).Disposition);
        EveryHolderMatches(owner, "an item looted");
        CollectionAssert.DoesNotContain(owner.QueryMapHolder(Exterior, "corpse")!.Items.Select(item => item.Entity).ToArray(), "note");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "virgil", "apple", _ => SessionInsertVerdict.Accepted).Disposition);
        EveryHolderMatches(owner, "an item given");
        CollectionAssert.Contains(owner.QueryMapHolder(Exterior, "virgil")!.Items.Select(item => item.Entity).ToArray(), "apple");
    }

    private static string[] ItemsOf(GameSessionOwner owner, string holder) => owner.QueryMapHolder(Exterior, holder)!.Items.Select(item => item.Entity).ToArray();

    // ARC-DEV-3363: the changes a holder's view depends on besides the moves of an item between lists.
    [TestMethod]
    public void AKilledNpcIsListedDeadByBoth()
    {
        using var owner = InventoryStateTests.Attached();
        Assert.IsFalse(owner.QueryMapHolder(Exterior, "virgil")!.Dead);
        var killed = owner.Kill(owner.Version, SessionLifeTarget.Critter("virgil", new LifeFacts(), default));
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.IsTrue(owner.QueryMapHolder(Exterior, "virgil")!.Dead);
        EveryHolderMatches(owner, "an NPC killed");
    }

    [TestMethod]
    public void ASpilledContainerListsNothingInBoth()
    {
        using var owner = Create();
        var seed = InventoryStateTests.Seed();
        var attached = owner.AttachMapInventory(Exterior, seed with
        {
            Holders = [.. seed.Holders.Where(holder => holder.Entity != "chest"), new("chest", SessionHolderKind.Container, false, false, [], ["cup-2", "knife"])],
            Items =
            [
                .. seed.Items,
                new("cup-2", InventoryStateTests.Facts(SessionItemType.Generic, 5650), null),
                new("knife", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null),
            ],
        });
        Assert.AreEqual(GameSessionFailure.None, attached.Failure);
        CollectionAssert.AreEqual(new[] { "cup-2", "knife" }, ItemsOf(owner, "chest"));
        EveryHolderMatches(owner, "a full chest");
        // The record a broken container leaves: destroyed, with the cell its items lie at.
        var cell = new CellCoord(131, 201);
        var word = (long)SessionObjectCondition.Destroyed | ((((long)cell.Y << 24) | (uint)cell.X) + 1) << 8;
        var broken = owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("chest", GameSessionOwner.ObjectConditionProperty), word);
        Assert.AreEqual(GameSessionDisposition.Changed, broken.Disposition);
        CollectionAssert.AreEqual(Array.Empty<string>(), ItemsOf(owner, "chest"));
        CollectionAssert.IsSubsetOf(new[] { "cup-2", "knife" }, owner.QueryMapInventory(Exterior)!.Ground.Select(ground => ground.Item.Entity).ToArray(), "the chest spilled");
        EveryHolderMatches(owner, "the chest spilled");
    }

    [TestMethod]
    public void ADroppedItemLeavesItsHoldersListInBoth()
    {
        using var owner = InventoryStateTests.Attached();
        CollectionAssert.Contains(ItemsOf(owner, "virgil"), "sword");
        var dropped = owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("sword", GameSessionOwner.ItemDroppedProperty),
            GameSessionOwner.ItemDroppedValue(new(101, 201)));
        Assert.AreEqual(GameSessionDisposition.Changed, dropped.Disposition);
        CollectionAssert.DoesNotContain(ItemsOf(owner, "virgil"), "sword");
        CollectionAssert.Contains(owner.QueryMapInventory(Exterior)!.Ground.Select(ground => ground.Item.Entity).ToArray(), "sword");
        EveryHolderMatches(owner, "an item dropped");
    }

    [TestMethod]
    public void ACreatedHolderIsAnsweredWhileItsCritterExists()
    {
        using var owner = InventoryStateTests.Attached();
        var made = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, 17087, Exterior, new(103, 200));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (made.Disposition, made.Failure));
        var critter = made.Object!.Entity;
        CollectionAssert.AreEqual(Array.Empty<string>(), ItemsOf(owner, critter));
        EveryHolderMatches(owner, "a critter created");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, critter, "apple", _ => SessionInsertVerdict.Accepted).Disposition);
        CollectionAssert.AreEqual(new[] { "apple" }, ItemsOf(owner, critter));
        EveryHolderMatches(owner, "an item given to the created critter");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter).Disposition);
        Assert.IsNull(owner.QueryMapHolder(Exterior, critter));
        CollectionAssert.DoesNotContain(owner.QueryMapInventory(Exterior)!.Holders.Select(holder => holder.Entity).ToArray(), critter);
        EveryHolderMatches(owner, "the critter destroyed");
    }
}
