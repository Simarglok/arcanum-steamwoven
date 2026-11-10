using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-305 (B36, B01): the inventory host reads the facts of an item the session made (a <c>made:</c> entity, no object row in any map)
/// from its prototype: wear slots, use facts and the window's lines, over invented records (no original data). The hero assembles a product whose
/// prototype is the wear test's torch (a generic item on the shield location).</summary>
[TestClass]
public sealed class AreaMadeItemFactsTests
{
    private const int ProductPrototype = 2320;
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, ProductPrototype, 5, 6);
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(0);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static string Record(int n, string c1, string c2, string product = "2300", string quantity = "1") =>
        $"{{{n}}}{{4}}{{{n + 1}}}{{5}}{{{n + 2}}}{{300}}{{{n + 3}}}{{{c1}}}{{{n + 4}}}{{{c2}}}{{{n + 5}}}{{{product}}}{{{n + 6}}}{{{quantity}}}\n";

    private static AreaSchematicData Data()
    {
        var catalog = SchematicCatalog.Create(Mes(Record(2000, "2100", "2100", quantity: "2")), Mes("{0}{Missing}{1}{Expertise}{2}{Done}{3}{Hover}{4}{Widget}{5}{A widget.}"));
        var texts = TechTexts.Read(Mes(string.Concat(Enumerable.Range(0, TechTexts.Messages).Select(n => $"{{{n}}}{{t{n}}}\n"))));
        return new(catalog.Catalog!, texts.Texts!, new Dictionary<int, (ComponentFacts, SessionItemKind)>
        {
            [2120] = (new(0, 3), Part),
            [ProductPrototype] = (new(0, 3), Product),
        });
    }

    // A hero holding two parts with degree 2 in discipline 0, as the schematic tests', who assembles two products: made:1 and made:2.
    internal static GameSessionOwner HeroWithProducts()
    {
        var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 15, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new(Part), new(Part)], Advancement: SessionHeroAdvancement.None with { TechDegrees = [2, 0, 0, 0, 0, 0, 0, 0] });
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var data = Data();
        var made = session.AssembleSchematic(session.Version, data.Catalog.Read(2000).Record!, InBag, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
        Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition, made.Failure.ToString());
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, made.Made.ToArray());
        return session;
    }

    internal static CombatFields? ProductFields(int prototype) =>
        prototype == ProductPrototype ? CombatFieldReader.Read(AreaInventoryWearTests.Torch(1)).Fields : null;

    [TestMethod]
    public void AMadeItemReadsItsWearAndUseFactsFromItsPrototype()
    {
        using var session = HeroWithProducts();
        var host = new AreaInventoryHost([], prototypeFields: ProductFields);
        var fields = ProductFields(ProductPrototype)!;

        var wear = host.Wearable(session, "made:1", null);
        Assert.IsNotNull(wear, "a made item has wear values (B36)");
        Assert.AreEqual(SessionEquipmentFacts.Wearable(Product, fields), wear);
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, AreaInventoryHost.WearSlots(wear).ToArray());
        Assert.AreEqual(AreaItemUseFacts.From("made:1", fields), host.UseFacts(session, "made:1", null));
        Assert.IsNull(host.Wearable(session, "made:3", null), "an item the session never made");
        Assert.IsNull(host.UseFacts(session, "made:3", null));
    }

    [TestMethod]
    public void AMadeItemIsWornThroughTheHostAndItsWindowLineOffersTheSlot()
    {
        using var session = HeroWithProducts();
        var host = new AreaInventoryHost([], prototypeFields: ProductFields);

        var line = host.View(session, null, 1).Pc.Single(l => l.Entity == "made:1");
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, line.WearSlots.ToArray(), "the made item offers its wear slot");

        var worn = session.Equip(session.Version, "made:1", EquipmentSlot.Shield, host.Wearable(session, "made:1", null)!, (_, _) => true, host.InsertCheck);
        Assert.AreEqual((GameSessionDisposition.Changed, (string?)null), (worn.Disposition, worn.Displaced), worn.Failure.ToString());
        var view = host.View(session, null, 1);
        Assert.AreEqual("made:1", view.Doll.Single(slot => slot.Slot == EquipmentSlot.Shield).Entity, "the doll shows it");
        Assert.AreEqual(0, view.Pc.Single(l => l.Entity == "made:1").WearSlots.Count, "a worn item offers no slot");
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, view.Pc.Single(l => l.Entity == "made:2").WearSlots.ToArray(), "the second one still does");

        var swap = session.Equip(session.Version, "made:2", EquipmentSlot.Shield, host.Wearable(session, "made:2", null)!, (_, _) => true, host.InsertCheck);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1"), (swap.Disposition, swap.Displaced), "a made item swaps with a worn one");
    }

    [TestMethod]
    public void WithoutAReadablePrototypeAMadeItemHasNoFactsAsBefore()
    {
        using var session = HeroWithProducts();
        Assert.IsNull(new AreaInventoryHost([]).Wearable(session, "made:1", null), "no prototype reader");
        Assert.IsNull(new AreaInventoryHost([]).UseFacts(session, "made:1", null));
        var unreadable = new AreaInventoryHost([], prototypeFields: _ => null);
        Assert.IsNull(unreadable.Wearable(session, "made:1", null), "the prototype does not read");
        Assert.IsNull(unreadable.UseFacts(session, "made:1", null));
        Assert.AreEqual(0, unreadable.View(session, null, 1).Pc.Single(l => l.Entity == "made:1").WearSlots.Count);
    }

    [TestMethod]
    public void AMadeItemsMoveAndSpellFactsComeFromItsPrototypeAndAnotherHoldersMadeItemHasNone()
    {
        using var session = HeroWithProducts();
        var fields = ProductFields(ProductPrototype)!;
        var host = new AreaInventoryHost([], prototypeFields: ProductFields);
        Assert.AreEqual(AreaInventoryHost.GridMoveFacts(Product, fields), host.GridMoveItem(session, "made:1", null), "the no-drop flag and script points are the prototype's");
        Assert.AreEqual(new SessionGridMoveItem(Product, false, []), new AreaInventoryHost([]).GridMoveItem(session, "made:1", null), "no reader: no flag, no scripts, as before");
        var spells = host.ItemSpells(session, "made:1", null);
        Assert.IsNotNull(spells, "a held made item has spell facts");
        Assert.AreEqual((SessionSpellCaster.Hero, "made:1"), (spells.Holder, spells.Entity), "its spells name the hero as caster");
        CollectionAssert.AreEqual(SessionItemSpells.FromCombatFields("made:1", SessionSpellCaster.Hero, fields)!.Spells.ToArray(), spells.Spells.ToArray());
        Assert.IsNull(host.ItemSpells(session, "made:3", null), "an item the hero does not hold");
        Assert.IsNull(host.RepairBaseline(session, "made:3", null));
        Assert.IsNull(host.HealingItem(session, "made:3", null));
        Assert.IsNull(host.Lockpick(session, "made:3", null));
    }
}
