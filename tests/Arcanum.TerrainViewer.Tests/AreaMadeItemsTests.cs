using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
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

    // Every step is open: a path of one step a tile.
    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    [TestMethod]
    public void AScriptCreatedItemOnTheGroundIsAMadeItemButAPlacedKitItemIsNot()
    {
        using var session = HeroWithProducts();
        var created = session.CreateGroundItem(session.Version, Product, new(79), new(300, 400), new OpenMap());
        Assert.AreEqual(GameSessionDisposition.Changed, created.Disposition, created.Failure.ToString());
        Assert.AreEqual(SessionPlacedSource.Created, session.QueryPlacedItems().Single().Source);
        Assert.AreEqual(Product, AreaMadeItems.Kind(session, created.Item!), "a created item reads its prototype");

        // The hero's kit parts were used up by the assembly: a hero that still holds its kit drops one.
        var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 0, 0, null, null, 385,
            [new(new SessionItemKind(SessionItemType.Generic, 2120, 1, 2))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic");
        using var kitHero = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var kit = kitHero.QueryHeroKit().First();
        var dropped = kitHero.DropHeroItem(kitHero.Version, kit.Entity, new SessionGridMoveItem(kit.Kind, false, []));
        Assert.AreEqual(GameSessionDisposition.Changed, dropped.Disposition, dropped.Failure.ToString());
        Assert.AreEqual(SessionPlacedSource.Kit, kitHero.QueryPlacedItems().Single().Source);
        Assert.IsNull(AreaMadeItems.Kind(kitHero, kit.Entity), "a placed kit item is no made item: the kit's reader answers for it");
        Assert.IsNull(AreaMadeItems.Kind(session, "made:99", "not an entity id"), "an invalid holder lists nothing");
    }
}
