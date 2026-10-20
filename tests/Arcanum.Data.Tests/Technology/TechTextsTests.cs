using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-001: the <c>mes\tech.mes</c> texts (ledger "Schematic data") over synthetic tables (no game text).</summary>
[TestClass]
public sealed class TechTextsTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-tech-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    /// <summary>Message n reads "t{n}".</summary>
    internal static MesFile Full(params int[] without) =>
        Mes(string.Concat(Enumerable.Range(0, TechTexts.Messages).Except(without).Select(n => $"{{{n}}}{{t{n}}}\n")));

    internal static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    [TestMethod]
    public void The_ranges_are_names_degrees_descriptions_degree_descriptions_and_the_two_messages()
    {
        var read = TechTexts.Read(Full());
        Assert.AreEqual(TechnologyDataStatus.Read, read.Status);
        var texts = read.Texts!;
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).Select(n => $"t{n}").ToArray(), texts.DisciplineNames.ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(8, 8).Select(n => $"t{n}").ToArray(), texts.DegreeNames.ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(16, 8).Select(n => $"t{n}").ToArray(), texts.DisciplineDescriptions.ToArray());
        // 24 + discipline + 8 × degree: the discipline is the fast index.
        Assert.AreEqual("t24", texts.DegreeDescription(0, 0));
        Assert.AreEqual("t25", texts.DegreeDescription(1, 0));
        Assert.AreEqual("t32", texts.DegreeDescription(0, 1));
        Assert.AreEqual("t87", texts.DegreeDescription(7, 7));
        Assert.AreEqual(24 + 3 + 8 * 5, TechTexts.DegreeDescriptionMessage(3, 5));
        Assert.AreEqual(("t88", "t89"), (texts.AlreadyKnown, texts.Learned));
        Assert.AreEqual((0, 90), (texts.Missing.Count, texts.MessageCount));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.DegreeDescription(8, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.DegreeDescription(0, -1));
    }

    [TestMethod]
    public void A_missing_message_is_null_and_listed_and_messages_outside_0_to_89_are_not_read()
    {
        var file = Mes(string.Concat(Enumerable.Range(0, TechTexts.Messages).Except([3, 30, 89]).Select(n => $"{{{n}}}{{t{n}}}\n")) + "{90}{extra}\n{1000}{x}");
        var texts = TechTexts.Read(file).Texts!;
        Assert.IsNull(texts.DisciplineNames[3]);
        Assert.IsNull(texts.DegreeDescription(6, 0));
        Assert.IsNull(texts.Learned);
        CollectionAssert.AreEqual(new[] { 3, 30, 89 }, texts.Missing.ToArray());
    }

    [TestMethod]
    public void Open_reads_the_first_base_source_and_refuses_a_missing_empty_or_unparsable_file()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Assert.AreEqual((TechnologyDataStatus.FileMissing, TechTexts.Path), Refusal(Open(module, data)));

        Write(data, TechTexts.Path, "{0}{Base}");
        Write(module, TechTexts.Path, "{0}{Module}");
        var read = Open(module, data);
        Assert.AreEqual(TechnologyDataStatus.Read, read.Status);
        Assert.AreEqual("Base", read.Texts!.DisciplineNames[0], "an init file comes from the base sources only");

        Write(data, TechTexts.Path, "no braces");
        Assert.AreEqual((TechnologyDataStatus.FileEmpty, TechTexts.Path), Refusal(Open(module, data)));
        Write(data, TechTexts.Path, "{0}{Broken");
        Assert.AreEqual((TechnologyDataStatus.FileRefused, TechTexts.Path), Refusal(Open(module, data)));
        Assert.AreEqual(TechnologyDataStatus.FileEmpty, TechTexts.Read(Mes("")).Status);
    }

    private static (TechnologyDataStatus, string?) Refusal(TechTextsLoad load)
    {
        Assert.IsNull(load.Texts);
        return (load.Status, load.Refusal?.File);
    }

    private TechTextsLoad Open(string module, string data)
    {
        using var stack = SchematicCatalogTests.Stack(_root, module, data);
        return TechTexts.Open(stack);
    }

    internal static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}
