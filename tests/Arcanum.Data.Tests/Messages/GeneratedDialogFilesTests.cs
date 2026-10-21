using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

/// <summary>ARC-DATA-042: the generated-dialog files as read in the executable (ledger GD1–GD4) over invented files, and an
/// opt-in census of the installed module.</summary>
[TestClass]
public sealed class GeneratedDialogFilesTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-gd-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void The_files_are_named_in_the_originals_handle_order()
    {
        Assert.AreEqual(GeneratedDialogFiles.Count, GeneratedDialogFiles.Names.Count);
        Assert.AreEqual(GeneratedDialogFiles.Count, Enum.GetValues<GeneratedDialogFile>().Length);
        Assert.AreEqual("mes/gd_pc2m.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.PcToMale));
        Assert.AreEqual("mes/gd_cls_f2m.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.ClassFemaleToMale));
        Assert.AreEqual("mes/gd_cls_pc2f.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.ClassPcToFemale));
        Assert.AreEqual("mes/gd_npc_m2f.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.NpcMaleToFemale));
        Assert.AreEqual("mes/gd_cls_dumb_pc2m.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.ClassDumbPcToMale));
        Assert.AreEqual("mes/gd_rce_dumb_f2f.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.RaceDumbFemaleToFemale));
        Assert.AreEqual("mes/gd_sto_f2m.mes", GeneratedDialogFiles.PathOf(GeneratedDialogFile.StoryFemaleToMale));
        Assert.AreEqual(GeneratedDialogFiles.Count, GeneratedDialogFiles.Names.Distinct().Count());
    }

    [TestMethod]
    public void An_npc_line_reads_the_file_of_the_npc_then_the_pc_gender_and_the_dumb_files_for_a_dumb_npc()
    {
        // (npc male, pc male) → m2m, (male, female) → m2f, (female, female) → f2f, (female, male) → f2m.
        var pairs = new[] { (true, true, "m2m"), (true, false, "m2f"), (false, false, "f2f"), (false, true, "f2m") };
        foreach (var (npcMale, pcMale, pair) in pairs)
        {
            Assert.AreEqual("gd_cls_" + pair, Name(GeneratedLineGroup.Class, npcMale, pcMale, false));
            Assert.AreEqual("gd_cls_dumb_" + pair, Name(GeneratedLineGroup.Class, npcMale, pcMale, true));
            Assert.AreEqual("gd_rce_" + pair, Name(GeneratedLineGroup.Race, npcMale, pcMale, false));
            Assert.AreEqual("gd_rce_dumb_" + pair, Name(GeneratedLineGroup.Race, npcMale, pcMale, true));
            Assert.AreEqual("gd_npc_" + pair, Name(GeneratedLineGroup.Npc, npcMale, pcMale, false));
            Assert.AreEqual("gd_npc_" + pair, Name(GeneratedLineGroup.Npc, npcMale, pcMale, true), "gd_npc has no dumb files");
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GeneratedDialogFiles.NpcLineFile((GeneratedLineGroup)3, true, true, false));
    }

    [TestMethod]
    public void The_run_length_needs_the_first_entry_and_counts_stored_entries_up_to_the_last_gaps_or_not()
    {
        var mes = Mes("{1000}{a}{1001}{b}{1003}{c}{1049}{d}{1050}{e}{2001}{f}");
        Assert.AreEqual(4, mes.RunLength(1000, 1049), "1000, 1001, 1003, 1049: the gap at 1002 is not seen");
        Assert.AreEqual(2, mes.RunLength(1000, 1002));
        Assert.AreEqual(1, mes.RunLength(1049, 1049));
        Assert.AreEqual(0, mes.RunLength(2000, 2049), "no entry numbered exactly at the start");
        Assert.AreEqual(0, mes.RunLength(1050, 1000), "an end below the start counts nothing: the start entry is above it");
        Assert.AreEqual(0, Mes("").RunLength(0, 10));
    }

    [TestMethod]
    public void A_line_is_the_entry_or_a_missing_or_absent_result()
    {
        var files = new MesFile?[GeneratedDialogFiles.Count];
        files[(int)GeneratedDialogFile.ClassMaleToMale] = Mes("{1000}{Hello @pcname@}{1001}{Hi}");
        var set = GeneratedDialogFiles.Create(files);
        var line = set.Line(GeneratedDialogFile.ClassMaleToMale, 1000);
        Assert.AreEqual("Hello @pcname@", Encoding.ASCII.GetString(line.Message!.Bytes.Span), "raw bytes: names are the caller's");
        Assert.IsFalse(line.FileAbsent);
        var missing = set.Line(GeneratedDialogFile.ClassMaleToMale, 1002);
        Assert.IsNull(missing.Message);
        Assert.IsFalse(missing.FileAbsent);
        var absent = set.Line(GeneratedDialogFile.RaceMaleToMale, 1000);
        Assert.IsTrue(absent.FileAbsent);
        Assert.IsNull(absent.Message);
        Assert.AreEqual(2, set.RunLength(GeneratedDialogFile.ClassMaleToMale, 1000, 1049));
        Assert.AreEqual(0, set.RunLength(GeneratedDialogFile.RaceMaleToMale, 1000, 1049));
        Assert.AreEqual(31, set.Absent.Count);
        Assert.ThrowsExactly<ArgumentException>(() => GeneratedDialogFiles.Create(new MesFile?[4]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => set.File((GeneratedDialogFile)32));
    }

    [TestMethod]
    public void Load_reads_the_stack_winners_keeps_a_missing_file_absent_and_refuses_an_unparsable_one()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(data, "mes/gd_cls_m2m.mes", "{1000}{Base}");
        Write(module, "mes/gd_cls_m2m.mes", "{1000}{Module}{1001}{Two}");
        Write(data, "mes/gd_npc_f2m.mes", "{1500}{Data only}");
        var loaded = Load(module, data);
        Assert.AreEqual(GeneratedDialogStatus.Ready, loaded.Status);
        var files = loaded.Files!;
        Assert.AreEqual("Module", Encoding.ASCII.GetString(files.Line(GeneratedDialogFile.ClassMaleToMale, 1000).Message!.Bytes.Span), "the module copy wins");
        Assert.IsNotNull(files.File(GeneratedDialogFile.NpcFemaleToMale));
        Assert.AreEqual(30, files.Absent.Count, "a missing file stays absent where the original exits (project policy)");
        Assert.IsTrue(files.Line(GeneratedDialogFile.PcToMale, 1).FileAbsent);

        Write(module, "mes/gd_rce_f2f.mes", "{1000}{Broken");
        var broken = Load(module, data);
        Assert.AreEqual((GeneratedDialogStatus.ParseFailed, "mes/gd_rce_f2f.mes"), (broken.Status, broken.Path));
        Assert.IsNull(broken.Files);
        Assert.AreEqual(MesParseStatus.UnterminatedField, broken.Parse!.Status);
    }

    /// <summary>Opt-in census of the installed module (DAT_ROOT): counts only, no text.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_module_has_all_32_files_with_the_census_counts()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for this opt-in real-data check; it is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var loaded = GeneratedDialogFiles.Load(stack);
        Assert.AreEqual(GeneratedDialogStatus.Ready, loaded.Status, $"{loaded.Path} {loaded.Read} {loaded.Parse?.Status}");
        var files = loaded.Files!;
        Assert.AreEqual(0, files.Absent.Count, "every file the original would open exists");
        int[] counts =
        [
            281, 284, 4116, 4116, 4144, 4144, 420, 386, 846, 846, 846, 846, 733, 733, 733, 733,
            62, 58, 33, 33, 22, 22, 22, 22, 6, 6, 6, 6, 275, 275, 275, 275,
        ];
        CollectionAssert.AreEqual(counts, Enum.GetValues<GeneratedDialogFile>().Select(file => files.File(file)!.Count).ToArray());
    }

    private static string Name(GeneratedLineGroup group, bool npcMale, bool pcMale, bool dumb) =>
        GeneratedDialogFiles.Names[(int)GeneratedDialogFiles.NpcLineFile(group, npcMale, pcMale, dumb)];

    private GeneratedDialogLoad Load(string module, string data)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return GeneratedDialogFiles.Load(stack);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}
