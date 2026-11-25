using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-3118: the barter window's open and the holder's list for a companion carried to another map (its seed is its home map's, Cave;
// the hero stands on Exterior, whose seed lists nobody called "pal"). Entities and numbers are invented.
[TestClass]
public sealed class CrossMapBarterStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Locket = new(SessionItemType.Generic, 5700, 5700, 35700);

    private static SessionMapInventorySeed HomeSeed() => new(
        [new("pal", SessionHolderKind.Npc, false, false, [], ["locket", "bow", "locket-2"])],
        [
            new("locket", Facts(SessionItemType.Generic, 5700), null),
            new("bow", Facts(SessionItemType.Armor, 2100, location: 1006), null),
            new("locket-2", Facts(SessionItemType.Generic, 5700), null),
        ]);

    private static GameSessionOwner Both()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Cave, HomeSeed()).Failure);
        return owner;
    }

    [TestMethod]
    public void TheHolderQueryFindsACompanionOnItsHomeMapAndReadsItsList()
    {
        using var owner = Both();
        var found = owner.QueryNpcHolder("pal")!.Value;
        Assert.AreEqual(Cave, found.Map);
        CollectionAssert.AreEqual(new[] { "locket", "bow", "locket-2" }, found.Holder.Items.Select(item => item.Entity).ToArray());
        Assert.IsNull(owner.QueryNpcHolder("nobody"));
        Assert.AreEqual(Cave, owner.QueryNpcHolder("pal")!.Value.Map);
    }

    [TestMethod]
    public void TheOpenIdentifiesTheCompanionsItemsAndRefusesAnUnlistedNpc()
    {
        using var owner = Both();
        var opened = owner.OpenBarter(owner.Version, "pal", null, identify: true, GoldPile, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 3), (opened.Disposition, opened.Failure, opened.Identified));
        Assert.IsTrue(owner.QueryItemIdentified("locket"));
        Assert.IsTrue(owner.QueryItemIdentified("bow"));
        var refused = owner.OpenBarter(owner.Version, "nobody", null, identify: true, GoldPile, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), (refused.Disposition, refused.Failure));
    }

    [TestMethod]
    public void TheHeroBuysFromTheCompanionAndPaysItsPile()
    {
        using var owner = Both();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.OpenBarter(owner.Version, "pal", null, identify: false, GoldPile, Accept).Disposition, "nothing to move or mark");
        var gold = owner.QueryGold();
        var bought = owner.BarterBuy(owner.Version, "pal", "locket", 1, 7, Locket, GoldPile, Accept, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "locket"), (bought.Disposition, bought.Failure, bought.Item));
        Assert.AreEqual(gold - 7, owner.QueryGold());
        Assert.AreEqual(7, owner.QueryNpcGold("pal"));
        CollectionAssert.AreEqual(new[] { "bow", "locket-2" }, owner.QueryNpcHolder("pal")!.Value.Holder.Items.Where(item => !item.Facts.Kind.Equals(GoldPile)).Select(item => item.Entity).ToArray());
    }
}
