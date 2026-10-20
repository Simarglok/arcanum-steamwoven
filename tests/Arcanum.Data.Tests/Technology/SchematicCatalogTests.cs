using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Technology.TechTextsTests;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-001: the schematic catalogue (ledger "Schematic data") over synthetic tables (no game text).</summary>
[TestClass]
public sealed class SchematicCatalogTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-schematic-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    /// <summary>A record's seven lines as MES text.</summary>
    private static string Record(int n, string name = "1", string description = "2", string art = "300", string c1 = "1000", string c2 = "2000",
        string product = "3000", string quantity = "1") =>
        $"{{{n}}}{{{name}}}{{{n + 1}}}{{{description}}}{{{n + 2}}}{{{art}}}{{{n + 3}}}{{{c1}}}{{{n + 4}}}{{{c2}}}{{{n + 5}}}{{{product}}}{{{n + 6}}}{{{quantity}}}\n";

    private static readonly MesFile Texts = Mes("{0}{a}{1}{b}{2}{c}");

    private static SchematicCatalog Catalog(string rules)
    {
        var load = SchematicCatalog.Create(Mes(rules), Texts);
        Assert.AreEqual(TechnologyDataStatus.Read, load.Status, $"{load.Refusal}");
        return load.Catalog!;
    }

    [TestMethod]
    public void A_record_is_seven_lines_with_three_alternative_lists_and_prototype_is_base_plus_20()
    {
        var catalog = Catalog(Record(2000, name: "1", description: "2", art: "301", c1: "1000", c2: "1100 1200 1300", product: "3000", quantity: "3"));
        var record = catalog.Read(2000).Record!;
        Assert.AreEqual((2000, SchematicKind.Learned, 1, 2, 301, 3), (record.Number, record.Kind, record.NameText, record.DescriptionText, record.Art, record.Quantity));
        CollectionAssert.AreEqual(new[] { 1000, 1000, 1000 }, record.Component1.Bases.ToArray(), "one token repeats to three");
        Assert.AreEqual(1, record.Component1.Tokens);
        CollectionAssert.AreEqual(new[] { 1100, 1200, 1300 }, record.Component2.Bases.ToArray());
        CollectionAssert.AreEqual(new[] { 1120, 1220, 1320 }, record.Component2.Prototypes.ToArray());
        CollectionAssert.AreEqual(new[] { 1000 }, record.Component1.DistinctBases.ToArray());
        Assert.IsTrue(record.IsComplete);
        Assert.AreEqual(("b", "c"), (catalog.Text(record.NameText), catalog.Text(record.DescriptionText)));
        Assert.IsNull(catalog.Text(99));
    }

    [TestMethod]
    public void The_parse_repeats_the_last_token_leaves_an_empty_line_empty_and_reads_numbers_with_atoi()
    {
        Assert.AreEqual("5,7,7/2", Parse("5 7"));
        Assert.AreEqual("5,5,5/1", Parse("  5  "));
        Assert.AreEqual("1,2,3/3", Parse("1 2 3"));
        Assert.AreEqual("12,-4,0/3", Parse("12abc -4 x"), "C atoi: digits to the first other character, a sign, 0 for none");
        Assert.AreEqual("/0", Parse(""));
        Assert.AreEqual("/0", Parse("   "));
        Assert.IsTrue(SchematicCatalog.ParseAlternatives("").Alternatives!.IsEmpty);
    }

    [TestMethod]
    public void More_than_three_tokens_or_a_line_the_buffer_cannot_hold_are_refused_TC6()
    {
        Assert.AreEqual(TechnologyDataStatus.TooManyTokens, SchematicCatalog.ParseAlternatives("1 2 3 4").Status);
        Assert.AreEqual(TechnologyDataStatus.LineTooLong, SchematicCatalog.ParseAlternatives(new string('1', SchematicCatalog.LineBufferBytes)).Status);
        Assert.AreEqual(TechnologyDataStatus.Read, SchematicCatalog.ParseAlternatives(new string('1', SchematicCatalog.LineBufferBytes - 1)).Status);

        var load = SchematicCatalog.Create(Mes(Record(2000) + Record(4000, product: "1 2 3 4")), Texts);
        Assert.AreEqual(TechnologyDataStatus.TooManyTokens, load.Status);
        Assert.IsNull(load.Catalog);
        Assert.AreEqual((TechnologyDataStatus.TooManyTokens, SchematicCatalog.Path, (int?)4005), (load.Refusal!.Status, load.Refusal.File, load.Refusal.Entry));
        // A name, description, art or quantity line is a plain number: extra words are not tokens.
        Assert.AreEqual(TechnologyDataStatus.Read, SchematicCatalog.Create(Mes(Record(2000, quantity: "1 2 3 4")), Texts).Status);
    }

    [TestMethod]
    public void A_missing_entry_reads_as_zero_and_is_flagged()
    {
        var rules = Record(2000).Replace("{2002}{300}", "").Replace("{2004}{2000}", "");
        var record = Catalog(rules).Read(2000).Record!;
        Assert.AreEqual(0, record.Art);
        CollectionAssert.AreEqual(new[] { 0, 0, 0 }, record.Component2.Bases.ToArray());
        Assert.IsFalse(record.IsComplete);
        CollectionAssert.AreEqual(new[] { false, false, true, false, true, false, false }, record.MissingLines.ToArray());

        // A number the file does not hold reads as all zeros with every line flagged, as the original reads it.
        var none = Catalog(Record(2000)).Read(4500);
        Assert.AreEqual(TechnologyDataStatus.Read, none.Status);
        Assert.AreEqual((SchematicKind.Found, 0, 0), (none.Record!.Kind, none.Record.NameText, none.Record.Quantity));
        Assert.IsTrue(none.Record.MissingLines.All(m => m));
    }

    [TestMethod]
    public void Ranges_learned_found_placeholder_and_the_learned_count()
    {
        Assert.AreEqual(SchematicKind.Other, SchematicCatalog.KindOf(1999));
        Assert.AreEqual(SchematicKind.Learned, SchematicCatalog.KindOf(2000));
        Assert.AreEqual(SchematicKind.Learned, SchematicCatalog.KindOf(3999));
        Assert.AreEqual(SchematicKind.Found, SchematicCatalog.KindOf(4000));
        Assert.AreEqual(SchematicKind.Placeholder, SchematicCatalog.KindOf(6000));
        Assert.AreEqual(SchematicKind.Placeholder, SchematicCatalog.KindOf(6070));
        Assert.AreEqual(SchematicKind.Found, SchematicCatalog.KindOf(6080));
        Assert.AreEqual(SchematicKind.Found, SchematicCatalog.KindOf(6005));
        Assert.AreEqual(6030, SchematicCatalog.PlaceholderNumber(3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SchematicCatalog.PlaceholderNumber(8));

        // Two learned records and a learned entry outside any record ((14 + 1) / 7 = 2); found records do not count.
        var catalog = Catalog(Record(2000) + Record(2010) + "{2408}{1}\n" + Record(4000) + Record(6000));
        Assert.AreEqual(2, catalog.LearnedCount);
        CollectionAssert.AreEqual(new[] { 2000, 2010, 2400, 4000, 6000 }, catalog.Records.Select(r => r.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 2408 }, catalog.Unread.ToArray());
        Assert.AreEqual(0, Catalog(Record(2000)).Unread.Count);
    }

    [TestMethod]
    public void The_learned_number_is_1990_plus_200_discipline_plus_10_degree()
    {
        Assert.AreEqual(2000, SchematicCatalog.LearnedNumber(0, 1));
        Assert.AreEqual(2060, SchematicCatalog.LearnedNumber(0, 7));
        Assert.AreEqual(3400, SchematicCatalog.LearnedNumber(7, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SchematicCatalog.LearnedNumber(0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SchematicCatalog.LearnedNumber(0, 8));
    }

    [TestMethod]
    public void The_discipline_of_a_learned_number_is_its_range_of_200_a_found_one_the_first_product_and_else_0()
    {
        var catalog = Catalog(Record(2000) + Record(4000, product: "5000 6000 7000") + Record(4010, product: ""));
        int? Lookup(int prototype) => prototype switch { 5020 => 6, 6020 => 1, _ => null };
        Assert.AreEqual(0, catalog.Discipline(2000, Lookup));
        Assert.AreEqual(0, catalog.Discipline(2199, Lookup));
        Assert.AreEqual(1, catalog.Discipline(2200, Lookup));
        Assert.AreEqual(7, catalog.Discipline(3400, Lookup));
        Assert.AreEqual(9, catalog.Discipline(3999, Lookup), "the formula, not a clamp");
        Assert.AreEqual(6, catalog.Discipline(4000, Lookup), "the first product alternative's prototype (base + 20)");
        Assert.IsNull(catalog.Discipline(4010, Lookup), "an empty product line: unknown");
        Assert.IsNull(catalog.Discipline(4020, Lookup), "missing lines read as base 0, prototype 20, which the lookup does not know");
        Assert.AreEqual(0, catalog.Discipline(1999, Lookup));
        Assert.AreEqual(0, catalog.Discipline(-5, Lookup));
    }

    [TestMethod]
    public void The_learned_selection_walks_the_full_list_with_the_degree_as_count_and_an_empty_tab_shows_the_placeholder()
    {
        var catalog = Catalog(Record(2000) + Record(2010) + Record(2020) + Record(2200) + Record(6000) + Record(6020));
        CollectionAssert.AreEqual(new[] { 2000, 2010, 2020, 2200 }, catalog.LearnedStarts.ToArray());
        CollectionAssert.AreEqual(new[] { 2000, 2010, 2020 }, catalog.Learned(0).ToArray());
        int[] degrees = [2, 1, 0, 0, 0, 0, 0, 0];
        Assert.AreEqual(2000, catalog.SelectLearned(degrees, 0, 0));
        Assert.AreEqual(2010, catalog.SelectLearned(degrees, 0, 1));
        Assert.AreEqual(2020, catalog.SelectLearned(degrees, 0, 2), "the selection does not compare the page with the count; the window does");
        Assert.AreEqual(2200, catalog.SelectLearned(degrees, 1, 0));
        Assert.AreEqual(-1, catalog.SelectLearned(degrees, 1, 1));
        Assert.AreEqual(-1, catalog.SelectLearned([0, 1, 0, 0, 0, 0, 0, 0], 0, 0), "a count of 0 selects nothing");
        Assert.AreEqual(-1, catalog.SelectLearned([0, 0, 7, 0, 0, 0, 0, 0], 2, 0), "a degree without a record");
        Assert.AreEqual(6020, catalog.RecordToShow(-1, 2).Record!.Number, "an empty tab shows 6000 + 10 × discipline");
        Assert.AreEqual(SchematicKind.Placeholder, catalog.RecordToShow(-1, 2).Record!.Kind);
        Assert.AreEqual(2010, catalog.RecordToShow(2010, 0).Record!.Number);
        Assert.ThrowsExactly<ArgumentException>(() => catalog.SelectLearned([8, 0, 0, 0, 0, 0, 0, 0], 0, 0));

        // The found tab: the crafter's list, grouped by the first product's discipline through the caller's lookup.
        var found = Catalog(Record(4000, product: "100") + Record(4010, product: "200") + Record(4020, product: "300"));
        int? Lookup(int prototype) => prototype switch { 120 => 2, 220 => 3, 320 => 2, _ => null };
        IReadOnlyList<int> list = [4020, 4010, 4000];
        Assert.AreEqual(4020, found.Select(list, 2, 2, 0, Lookup), "in the list's order");
        Assert.AreEqual(4000, found.Select(list, 2, 2, 1, Lookup));
        Assert.AreEqual(4010, found.Select(list, 1, 3, 0, Lookup));
        Assert.AreEqual(-1, found.Select(list, 1, 4, 0, Lookup));
        Assert.AreEqual(-1, found.Select(list, 0, 2, 0, Lookup));
    }

    [TestMethod]
    public void A_learned_range_the_full_list_cannot_hold_is_refused_TC11()
    {
        // Entry 2000 missing: the count is 0 and no list is built (no refusal).
        var none = Catalog(Record(2010));
        Assert.AreEqual((0, 0), (none.LearnedCount, none.LearnedStarts.Count));
        // A record without its last lines: more starts (2) than the count ((7 + 4) / 7 = 1).
        var short_ = SchematicCatalog.Create(Mes(Record(2000) + "{2010}{1}{2011}{2}{2012}{3}{2013}{4}\n"), Texts);
        Assert.AreEqual(TechnologyDataStatus.LearnedListMismatch, short_.Status);
        Assert.IsNull(short_.Catalog);
        // Entries past offset 6 of a record: fewer starts (1) than the count (14 / 7 = 2).
        var tail = string.Concat(new[] { 2007, 2008, 2009, 2017, 2018, 2019, 2027 }.Select(n => $"{{{n}}}{{0}}"));
        Assert.AreEqual(TechnologyDataStatus.LearnedListMismatch, SchematicCatalog.Create(Mes(Record(2000) + tail), Texts).Status);
        // A learned start at 3600 or above counts past the eight disciplines.
        var high = SchematicCatalog.Create(Mes(Record(2000) + Record(3600)), Texts);
        Assert.AreEqual((TechnologyDataStatus.LearnedDisciplineOutOfRange, (int?)3600), (high.Status, high.Refusal!.Entry));
        Assert.AreEqual(TechnologyDataStatus.Read, SchematicCatalog.Create(Mes(Record(2000) + Record(3400)), Texts).Status);
    }

    [TestMethod]
    public void Open_needs_both_files_from_the_base_sources()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Assert.AreEqual((TechnologyDataStatus.FileMissing, SchematicCatalog.Path), Refusal(Open(module, data)));
        Write(data, SchematicCatalog.Path, Record(2000));
        Assert.AreEqual((TechnologyDataStatus.FileMissing, SchematicCatalog.TextPath), Refusal(Open(module, data)), "without schematic_text.mes the init fails");
        Write(module, SchematicCatalog.TextPath, "{0}{module}");
        Assert.AreEqual((TechnologyDataStatus.FileMissing, SchematicCatalog.TextPath), Refusal(Open(module, data)), "a module copy is not read at init");
        Write(data, SchematicCatalog.TextPath, "{0}{base}");
        var load = Open(module, data);
        Assert.AreEqual(TechnologyDataStatus.Read, load.Status, $"{load.Refusal}");
        Assert.AreEqual("base", load.Catalog!.Text(0));
        Write(data, SchematicCatalog.TextPath, "");
        Assert.AreEqual((TechnologyDataStatus.FileEmpty, SchematicCatalog.TextPath), Refusal(Open(module, data)));
        Write(data, SchematicCatalog.TextPath, "{0}{base}");
        Write(data, SchematicCatalog.Path, Record(2000, c1: "1 2 3 4"));
        Assert.AreEqual((TechnologyDataStatus.TooManyTokens, SchematicCatalog.Path), Refusal(Open(module, data)));
    }

    private static string Parse(string line)
    {
        var parsed = SchematicCatalog.ParseAlternatives(line);
        Assert.AreEqual(TechnologyDataStatus.Read, parsed.Status);
        return $"{string.Join(',', parsed.Alternatives!.Bases)}/{parsed.Alternatives.Tokens}";
    }

    private static int? NoPrototype(int prototype) => null;

    private static (TechnologyDataStatus, string?) Refusal(SchematicCatalogLoad load)
    {
        if (load.Status != TechnologyDataStatus.Read) Assert.IsNull(load.Catalog);
        return (load.Status, load.Refusal?.File);
    }

    private SchematicCatalogLoad Open(string module, string data)
    {
        using var stack = Stack(_root, module, data);
        return SchematicCatalog.Open(stack);
    }

    internal static ContentStack Stack(string root, string module, string data)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }
}
