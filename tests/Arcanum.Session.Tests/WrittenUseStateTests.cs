using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Using a written item (ARC-UI-049, ledger WR rows). The hero carries two written items (kit:1, kit:2) and a weapon (kit:3); texts
// and numbers are invented.
[TestClass]
public sealed class WrittenUseStateTests
{
    private static readonly SessionHeroKitItem Written = new(new(SessionItemType.Written, 14082, 9001, 14062));
    private static readonly WrittenTexts Texts = WrittenTexts.Create(
        MesFile.Parse(Encoding.Latin1.GetBytes("{100}{12 One}{101}{12 Two}")).File, null, null, null, null);

    private static GameSessionOwner Owner() => CreateWith(HalfElf() with { Kit = [Written, Written, Kit[0]] });

    [TestMethod]
    public void A_book_gives_its_document_and_changes_nothing()
    {
        using var owner = Owner();
        var use = owner.UseWrittenItem(owner.Version, "kit:1", new(0, 100), Texts);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, (SchematicReading?)SchematicReading.NotSchematic), (use.Disposition, use.Failure, use.Reading));
        Assert.AreEqual((WrittenReadStatus.Read, 2, "One"), (use.Document!.Status, use.Document.Body.Count, use.Document.Body[0].Text));
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.AreEqual(3, owner.QueryHeroKit().Count, "reading destroys nothing");
        var missing = owner.UseWrittenItem(owner.Version, "kit:1", new(1, 100), Texts);
        Assert.AreEqual((GameSessionDisposition.NoChange, WrittenReadStatus.TextsUnavailable), (missing.Disposition, missing.Document!.Status), "the note file did not load");
        Assert.AreEqual(WrittenReadStatus.Read, owner.QueryWrittenDocument(new(0, 101), Texts).Status);
    }

    [TestMethod]
    public void A_schematic_document_registers_through_the_tech_3_rule()
    {
        using var owner = Owner();
        var use = owner.UseWrittenItem(owner.Version, "kit:1", new(SchematicKnowledgeRules.SchematicSubtype, 4150), Texts);
        Assert.AreEqual((GameSessionDisposition.Changed, (SchematicReading?)SchematicReading.Registered, (int?)4150, true, (WrittenDocument?)null),
            (use.Disposition, use.Reading, use.Schematic, use.DocumentDestroyed, use.Document));
        CollectionAssert.AreEqual(new[] { 4150 }, owner.QueryHeroFoundSchematics()!.Value.ToArray());
        var known = owner.UseWrittenItem(owner.Version, "kit:2", new(SchematicKnowledgeRules.SchematicSubtype, 4150), Texts);
        Assert.AreEqual((GameSessionDisposition.NoChange, (SchematicReading?)SchematicReading.AlreadyKnown, false), (known.Disposition, known.Reading, known.DocumentDestroyed));
    }

    [TestMethod]
    public void Uses_the_session_cannot_run_are_refused_before_any_effect()
    {
        using var owner = Owner();
        GameSessionFailure Refused(string item, SessionWrittenFacts facts) => owner.UseWrittenItem(owner.Version, item, facts, Texts).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidItem, Refused("bad id", new(0, 100)));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, Refused("kit:9", new(0, 100)));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, Refused("kit:3", new(0, 100)), "not a written item");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, Refused("kit:1", new(0, 100) { UseScript = true }), "the use script would run first");
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, Refused("kit:1", new(0, 100) { ItemSpell = true }), "the item-spell path");
        Assert.AreEqual(GameSessionFailure.InvalidItem, Refused("kit:1", new(9, 100)), "a subtype outside 0..6 (both modes)");
        Assert.AreEqual(GameSessionFailure.InvalidItem, Refused("kit:1", new(4, 5000)), "a picture art outside 0..4095 (both modes)");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.UseWrittenItem(new GameSessionVersion(owner.Identity, 7), "kit:1", new(0, 100), Texts).Failure);
        Assert.AreEqual(0, owner.Version.Revision);
        using var heroless = Create();
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, heroless.UseWrittenItem(heroless.Version, "kit:1", new(0, 100), Texts).Failure);
        Assert.AreEqual(WrittenReadStatus.Read, heroless.QueryWrittenDocument(new(0, 100), Texts).Status, "a script opener needs no hero");
    }
}
