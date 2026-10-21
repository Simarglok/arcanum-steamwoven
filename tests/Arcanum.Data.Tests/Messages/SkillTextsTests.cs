using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

/// <summary>ARC-UI-009: the skill texts of <c>mes\skill.mes</c> (ledger SR12, SP3, SP6) over synthetic tables.</summary>
[TestClass]
public sealed class SkillTextsTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-skilltexts-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Names_trainings_and_descriptions_come_from_their_number_ranges()
    {
        var texts = SkillTexts.Create(Mes("{0}{Bow}\n{11}{Persuade}\n{12}{Fix}\n{15}{Defuse}\n{16}{None}\n{19}{Top}\n{20}{Bow text}\n{31}{Persuade text}\n{32}{Fix text}\n{35}{Defuse text}"));
        Assert.AreEqual(("Bow", "Persuade"), (Text(texts.BasicName(0)), Text(texts.BasicName(11))));
        Assert.AreEqual(("Fix", "Defuse"), (Text(texts.TechName(0)), Text(texts.TechName(3))));
        Assert.AreEqual(("None", "Top"), (Text(texts.TrainingName(0)), Text(texts.TrainingName(3))));
        Assert.AreEqual(("Bow text", "Persuade text"), (Text(texts.BasicDescription(0)), Text(texts.BasicDescription(11))));
        Assert.AreEqual(("Fix text", "Defuse text"), (Text(texts.TechDescription(0)), Text(texts.TechDescription(3))));
        Assert.IsNull(texts.BasicName(1), "a number the file does not hold");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.BasicName(12));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.TechName(4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => texts.TrainingName(-1));
    }

    [TestMethod]
    public void The_file_is_read_from_the_base_sources_as_the_init_reads_it()
    {
        var module = Path.Combine(_root, "module"); var data = Path.Combine(_root, "data");
        Write(module, SkillTexts.Path, "{0}{Module bow}");
        Assert.AreEqual(SkillTextStatus.FileMissing, Load(module, data).Status, "a module's file is not read at init");
        Write(data, SkillTexts.Path, "{0}{Base bow}");
        var loaded = Load(module, data);
        Assert.AreEqual(("Base bow", SkillTextStatus.Ready), (Text(loaded.Texts!.BasicName(0)), loaded.Status));
        Write(data, SkillTexts.Path, "{0}{Bow");
        Assert.AreEqual(SkillTextStatus.ParseFailed, Load(module, data).Status);
        Write(data, SkillTexts.Path, "{0}{Base bow}");
        Assert.AreEqual(MesParseStatus.TooLarge, Load(module, data, new() { MaxBytes = 4 }).Parse!.Status);
    }

    private SkillTextLoad Load(string module, string data, MesReaderOptions? options = null)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return SkillTexts.Load(stack, options);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static string? Text(MesMessage? message) => message is null ? null : Encoding.ASCII.GetString(message.Bytes.Span);

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}

/// <summary>ARC-UI-009 on the installed original (opt-in, <c>DAT_ROOT</c>): every skill text the page shows is present. Counts
/// only; no text is asserted or printed.</summary>
[TestClass]
public sealed class SkillTextsRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_skill_file_holds_every_name_training_and_description()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = SkillTexts.Load(stack);
        Assert.AreEqual(SkillTextStatus.Ready, loaded.Status);
        var texts = loaded.Texts!;
        var names = Enumerable.Range(0, SkillTexts.BasicSkills).Count(i => texts.BasicName(i) is not null) + Enumerable.Range(0, SkillTexts.TechSkills).Count(i => texts.TechName(i) is not null);
        var descriptions = Enumerable.Range(0, SkillTexts.BasicSkills).Count(i => texts.BasicDescription(i) is not null)
            + Enumerable.Range(0, SkillTexts.TechSkills).Count(i => texts.TechDescription(i) is not null);
        var trainings = Enumerable.Range(0, SkillTexts.Trainings).Count(i => texts.TrainingName(i) is not null);
        Console.WriteLine($"skill names {names}, descriptions {descriptions}, training names {trainings}");
        Assert.AreEqual((16, 16, 4), (names, descriptions, trainings));
    }
}
