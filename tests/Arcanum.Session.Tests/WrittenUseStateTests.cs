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

    // ARC-TECHNOLOGY-009, WRD3: the session's reading rules reach the markup, read at each query.
    [TestMethod]
    public void A_message_without_a_font_prefix_follows_the_sessions_rule_mode()
    {
        using var owner = Owner();
        var plain = WrittenTexts.Create(MesFile.Parse(Encoding.Latin1.GetBytes("{100}{Plain}{101}{12 Two}")).File, null, null, null, null);
        Assert.AreEqual("Plain|Two", string.Join("|", owner.UseWrittenItem(owner.Version, "kit:1", new(0, 100), plain).Document!.Body.Select(m => m.Text)), "Steamwoven");
        owner.SelectRules(RuleMode.Original);
        Assert.AreEqual("lain|Two", string.Join("|", owner.UseWrittenItem(owner.Version, "kit:1", new(0, 100), plain).Document!.Body.Select(m => m.Text)), "Original");
        Assert.AreEqual("lain", owner.QueryWrittenDocument(new(0, 100), plain).Body[0].Text);
        owner.SelectRules(RuleMode.Steamwoven);
        Assert.AreEqual("Plain", owner.QueryWrittenDocument(new(0, 100), plain).Body[0].Text);
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

    [TestMethod]
    public void A_dead_hero_cannot_read()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, owner.UseWrittenItem(owner.Version, "kit:1", new(0, 100), Texts).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget,
            owner.UseWrittenItem(owner.Version, "kit:1", new(SchematicKnowledgeRules.SchematicSubtype, 4150), Texts).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void The_written_facts_come_from_the_fields()
    {
        static Arcanum.Data.Combat.CombatFields Fields(IReadOnlyList<int> points, int spellMana, uint itemFlags) =>
            new(Arcanum.Data.Combat.CombatFieldOrigin.Prototype,
                new(13, 14092, 0, 0, 0, 0, 0, 1, 0, 0, 0, [0, 0, 0, 0, 0], 0) { ScriptPoints = points },
                new(itemFlags, 1, 1, 0, 0, 0, 0, [], spellMana, 0, 0, 0), null, null, null, null, null, null)
            { Written = new(0, 2, 1300, 0) };
        Assert.AreEqual(new SessionWrittenFacts(2, 1300) { Prototype = 14092 }, SessionWrittenFacts.FromCombatFields(Fields([0, 21], 0, 0)),
            "an examine or insert script is not a use script");
        Assert.IsTrue(SessionWrittenFacts.FromCombatFields(Fields([1], 0, 0))!.Value.UseScript, "attachment point 1");
        Assert.IsTrue(SessionWrittenFacts.FromCombatFields(Fields([], 5, 0))!.Value.ItemSpell, "a spell mana store");
        Assert.IsTrue(SessionWrittenFacts.FromCombatFields(Fields([], 0, 0x4))!.Value.ItemSpell, "the magical item flag");
        Assert.IsFalse(SessionWrittenFacts.FromCombatFields(Fields([], 0, 0x8))!.Value.ItemSpell);
        Assert.IsNull(SessionWrittenFacts.FromCombatFields(Fields([], 0, 0) with { Written = null }), "not a written item");
    }
}
