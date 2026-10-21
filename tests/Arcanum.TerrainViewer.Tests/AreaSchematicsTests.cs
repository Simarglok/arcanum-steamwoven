using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-TECHNOLOGY-005 schematic window over invented records, texts and items (no original data): the learned and found
/// tabs, the page wrap, the placeholder of an empty tab, the button state and its text, the completion text and sound, the refusals,
/// and one assembly through the session with the window's facts. The hero has degree 2 in discipline 0 and a kit of two neutral
/// parts (prototype 2120, discipline 3) and nothing else; a gear (2220, complexity −10, discipline 3) is never held.</summary>
[TestClass]
public sealed class AreaSchematicsTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(0);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static string Record(int n, string c1, string c2, string product = "2300", string quantity = "1") =>
        $"{{{n}}}{{4}}{{{n + 1}}}{{5}}{{{n + 2}}}{{300}}{{{n + 3}}}{{{c1}}}{{{n + 4}}}{{{c2}}}{{{n + 5}}}{{{product}}}{{{n + 6}}}{{{quantity}}}\n";

    private static AreaSchematicData Data()
    {
        var catalog = SchematicCatalog.Create(Mes(Record(2000, "2100", "2100", quantity: "2") + Record(2010, "2100", "2200")),
            Mes("{0}{Missing}{1}{Expertise}{2}{Done}{3}{Hover}{4}{Widget}{5}{A widget.}"));
        Assert.AreEqual(TechnologyDataStatus.Read, catalog.Status, $"{catalog.Refusal}");
        var texts = TechTexts.Read(Mes(string.Concat(Enumerable.Range(0, TechTexts.Messages).Select(n => $"{{{n}}}{{t{n}}}\n"))));
        Assert.AreEqual(TechnologyDataStatus.Read, texts.Status);
        return new(catalog.Catalog!, texts.Texts!, new Dictionary<int, (ComponentFacts, SessionItemKind)>
        {
            [2120] = (new(0, 3), Part),
            [2220] = (new(-10, 3), new(SessionItemType.Generic, 2220, 3, 4)),
            [2320] = (new(0, 3), Product),
        });
    }

    private static GameSessionOwner Hero(bool hero = true)
    {
        var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 15, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new(Part), new(Part)], Advancement: SessionHeroAdvancement.None with { TechDegrees = [2, 0, 0, 0, 0, 0, 0, 0] });
        var load = hero ? new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic") : null;
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    private static AreaSchematicsView View(GameSessionOwner session, AreaSchematicData? data, AreaSchematicTab tab, int discipline, int page)
    {
        var build = AreaSchematics.Build(session, data, tab, discipline, page, InBag, MesTextProfile.Windows1252);
        Assert.AreEqual(AreaSchematicRefusal.None, build.Refusal);
        return build.View!;
    }

    [TestMethod]
    public void The_learned_tab_pages_through_the_degrees_records_and_wraps()
    {
        using var session = Hero();
        var data = Data();
        var first = View(session, data, AreaSchematicTab.Learned, 0, 0);
        Assert.AreEqual((2000, 0, 2, false), (first.Record, first.Page, first.Pages, first.Placeholder));
        Assert.AreEqual(("Widget", "A widget."), (first.Name, first.Description));
        Assert.AreEqual((AssemblyButton.CanAssemble, (string?)null, "Hover"), (first.Button, first.ButtonText, first.HoverText));
        Assert.AreEqual((2120, true, true, 2120, true), (first.Component1!.Prototype, first.Component1.Found, first.Component1.ExpertiseOk,
            first.Component2!.Prototype, first.Component2.Found), "component 1 is reserved, so component 2 takes the other part");
        Assert.AreEqual((2320, 6, 2), (first.Product!.Prototype, first.Product.Description, first.Quantity));
        Assert.AreEqual(data.Texts.DisciplineNames[0], first.DisciplineName);
        Assert.AreEqual(2010, View(session, data, AreaSchematicTab.Learned, 0, 1).Record);
        Assert.AreEqual(2000, View(session, data, AreaSchematicTab.Learned, 0, 2).Record, "the page wraps into the tab's count");
        Assert.AreEqual(2010, View(session, data, AreaSchematicTab.Learned, 0, -1).Record);
        CollectionAssert.AreEqual(new[] { 2000, 2010 }, AreaSchematics.Records(session, data, AreaSchematicTab.Learned, 0).ToArray());

        var gear = View(session, data, AreaSchematicTab.Learned, 0, 1);
        Assert.AreEqual((AssemblyButton.ExpertiseLacking, "Expertise"), (gear.Button, gear.ButtonText), "degree 0 in discipline 3 gives 0 < 10");
        Assert.AreEqual((2220, false, false, -10, 3), (gear.Component2!.Prototype, gear.Component2.Found, gear.Component2.ExpertiseOk,
            gear.Component2.Complexity, gear.Component2.Discipline));
    }

    [TestMethod]
    public void An_empty_tab_shows_its_disciplines_placeholder_and_missing_data_is_named()
    {
        using var session = Hero();
        var data = Data();
        var empty = View(session, data, AreaSchematicTab.Learned, 1, 3);
        Assert.AreEqual((SchematicCatalog.PlaceholderNumber(1), 0, 0, true, "no schematic in this tab"), (empty.Record, empty.Page, empty.Pages, empty.Placeholder, empty.Note));
        Assert.IsNull(empty.Component1);
        var found = View(session, data, AreaSchematicTab.Found, 0, 0);
        Assert.AreEqual((SchematicCatalog.PlaceholderNumber(0), true), (found.Record, found.Placeholder), "nothing found yet");
        var unloaded = View(session, null, AreaSchematicTab.Learned, 0, 0);
        Assert.AreEqual((true, "schematic data not loaded"), (unloaded.Placeholder, unloaded.Note));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaSchematics.Build(session, data, AreaSchematicTab.Learned, 8, 0, InBag, MesTextProfile.Windows1252));
        Assert.AreEqual(("Learned", "Found"), (AreaSchematics.TabName(AreaSchematicTab.Learned), AreaSchematics.TabName(AreaSchematicTab.Found)));
    }

    [TestMethod]
    public void A_session_without_a_hero_is_refused()
    {
        using var session = Hero(hero: false);
        var build = AreaSchematics.Build(session, Data(), AreaSchematicTab.Learned, 0, 0, InBag, MesTextProfile.Windows1252);
        Assert.AreEqual((AreaSchematicRefusal.NoHero, (AreaSchematicsView?)null), (build.Refusal, build.View));
    }

    [TestMethod]
    public void The_assemble_button_makes_the_product_with_the_windows_facts_then_the_page_misses_an_item()
    {
        using var session = Hero();
        var data = Data();
        var record = data.Catalog.Read(2000).Record!;
        var result = session.AssembleSchematic(session.Version, record, InBag, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, result.Made.ToArray());
        var after = View(session, data, AreaSchematicTab.Learned, 0, 0);
        Assert.AreEqual((AssemblyButton.ItemMissing, "Missing"), (after.Button, after.ButtonText));
        Assert.AreEqual("Done", AreaSchematics.CompletedText(data, MesTextProfile.Windows1252));
        Assert.AreEqual(3018, AreaSchematics.CompletedSound(0));
        Assert.AreEqual(3025, AreaSchematics.CompletedSound(7), "3018 + the current discipline (SW6), not the tab");
        var refused = session.AssembleSchematic(session.Version, record, InBag, data.Facts, data.Kind, AreaInventoryHost.DiagnosticInsertCheck);
        Assert.AreEqual("an item is missing", AreaSchematics.Refused(refused));
    }
}
