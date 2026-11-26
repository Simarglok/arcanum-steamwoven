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
}
