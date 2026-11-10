using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaMadeItemFactsTests;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-315 (B36, B01): the one answer to "is this entity an item the session made, and which prototype is it" that the readers looking an
/// object row up by entity (projectile and thrown-item art, audio cues, the NPC armour read) share, over invented records (no original data): the hero's
/// made items, made items lying on the ground and created ones, and made items added to an NPC.</summary>
[TestClass]
public sealed class AreaMadeItemsTests
{
    private const int ProductPrototype = 2320;
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, ProductPrototype, 5, 6);

    [TestMethod]
    public void AHeroMadeItemIsAMadeItemOfItsSavedPrototype()
    {
        using var session = HeroWithProducts();
        Assert.AreEqual(Product, AreaMadeItems.Kind(session, "made:1"));
        Assert.AreEqual(Product, AreaMadeItems.Kind(session, "made:2"));
        Assert.AreEqual(ProductPrototype, AreaMadeItems.Prototype(session, "made:2"));
        Assert.IsNull(AreaMadeItems.Kind(session, "made:3"), "an item the session never made");
        Assert.IsNull(AreaMadeItems.Prototype(session, "kit:1"), "a kit item is no made item: the kit's reader answers for it");
        Assert.IsNull(AreaMadeItems.Prototype(session, "oid:" + new string('a', 64)), "an item with an object row is no made item");
        Assert.IsNull(AreaMadeItems.Prototype(session, ""));
    }

    [TestMethod]
    public void ThePrototypesArtIsTheMadeItemsArtAndOnlyForAMadeItem()
    {
        using var session = HeroWithProducts();
        uint? Art(int prototype) => prototype == ProductPrototype ? 0x30000099u : null;
        Assert.AreEqual(0x30000099u, AreaMadeItems.Art(session, "made:1", Art));
        Assert.IsNull(AreaMadeItems.Art(session, "made:3", Art));
        Assert.IsNull(AreaMadeItems.Art(session, "kit:1", Art));
        Assert.IsNull(AreaMadeItems.Art(session, "made:1", _ => null), "a prototype without art has none");
    }

    [TestMethod]
    public void AMadeItemOnTheGroundIsStillAMadeItemOfItsPrototype()
    {
        using var session = HeroWithProducts();
        var host = new AreaInventoryHost([], prototypeFields: ProductFields);
        var dropped = session.DropHeroItem(session.Version, "made:1", host.GridMoveItem(session, "made:1", null)!);
        Assert.AreEqual(GameSessionDisposition.Changed, dropped.Disposition, dropped.Failure.ToString());
        Assert.AreEqual(SessionPlacedSource.Made, session.QueryPlacedItems().Single().Source);
        Assert.IsFalse(session.QueryMadeItems().Any(item => item.Entity == "made:1"), "it left the hero's list");
        Assert.AreEqual(Product, AreaMadeItems.Kind(session, "made:1"), "its prototype is read from the ground store");
        Assert.AreEqual(Product, AreaMadeItems.Kind(session, "made:2"), "the other one is still the hero's");
    }
}
