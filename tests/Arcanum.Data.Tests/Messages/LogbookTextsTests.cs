using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

/// <summary>ARC-UI-008: the logbook texts of quests and rumors (ledger LB7, LB8, LB10) over synthetic tables.</summary>
[TestClass]
public sealed class LogbookTextsTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-logbook-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void A_quest_reads_the_low_description_only_when_it_exists_and_intelligence_is_four_or_less()
    {
        var texts = LogbookTexts.Create(true, Mes("{1000}{Normal A}\n{1001}{Normal B}\n{1999}{Last}\n{2000}{Outside}"), Mes("{1000}{Low A}\n{1002}{Low C}"));
        Assert.AreEqual("Low A", Text(texts.QuestDescription(1000, 4)));
        Assert.AreEqual("Low A", Text(texts.QuestDescription(1000, 1)));
        Assert.AreEqual("Normal A", Text(texts.QuestDescription(1000, 5)));
        // No low description: the normal one, whatever the intelligence.
        Assert.AreEqual("Normal B", Text(texts.QuestDescription(1001, 3)));
        // A low description without a normal one is read only by a low intelligence.
        Assert.AreEqual("Low C", Text(texts.QuestDescription(1002, 4)));
        Assert.IsNull(texts.QuestDescription(1002, 5));
        Assert.AreEqual("Last", Text(texts.QuestDescription(1999, 10)));
        Assert.IsNull(texts.QuestDescription(2000, 10), "only quests 1000..1999 have descriptions");
        Assert.IsNull(texts.QuestDescription(999, 10));
    }

    [TestMethod]
    public void Without_the_quest_table_no_quest_description_is_loaded()
    {
        var texts = LogbookTexts.Create(false, Mes("{1000}{Normal A}"), Mes("{1000}{Low A}"));
        Assert.IsFalse(texts.QuestDescriptionsLoaded);
        Assert.IsNull(texts.QuestDescription(1000, 10));
        Assert.IsNull(texts.QuestDescription(1000, 1));
    }

    [TestMethod]
    public void A_rumor_reads_message_twenty_times_its_number_from_the_file_of_its_intelligence()
    {
        var texts = LogbookTexts.Create(true, rumors: Mes("{20000}{Rumor 1000}\n{20001}{Greeting}\n{20020}{Rumor 1001}"), rumorsLow: Mes("{20000}{Low 1000}"));
        Assert.AreEqual("Rumor 1000", Text(texts.RumorText(1000, 5)));
        Assert.AreEqual("Low 1000", Text(texts.RumorText(1000, 4)));
        Assert.AreEqual("Rumor 1001", Text(texts.RumorText(1001, 5)));
        Assert.IsNull(texts.RumorText(1001, 4), "no fallback from the low file to the normal one");
        Assert.IsNull(texts.RumorText(1002, 5));
        Assert.IsNull(texts.RumorText(0, 5));
        Assert.IsNull(texts.RumorText(int.MaxValue, 5));
        Assert.IsNull(LogbookTexts.Create(true).RumorText(1000, 5), "an absent file has no text");
    }

    [TestMethod]
    public void Load_reads_each_file_from_the_stack_winner_and_gates_quests_on_the_quest_table()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(data, "mes/gamequestlog.mes", "{1000}{Base A}");
        Write(module, "mes/gamequestlog.mes", "{1000}{Module A}");
        Write(module, "mes/gamequestlogdumb.mes", "{1000}{Module low A}");
        Write(module, "mes/game_rd_npc_m2m.mes", "{20000}{Module rumor}");
        var loaded = Load(module, data);
        Assert.AreEqual(LogbookTextStatus.Ready, loaded.Status);
        Assert.IsFalse(loaded.Texts!.QuestDescriptionsLoaded, "no rules/gamequest.mes: no descriptions");
        Assert.IsNull(loaded.Texts.QuestDescription(1000, 10));
        Assert.AreEqual("Module rumor", Text(loaded.Texts.RumorText(1000, 10)));
        Assert.IsNull(loaded.Texts.RumorText(1000, 4), "no low rumor file");

        Write(data, "rules/gamequest.mes", "{1000}{1 0}");
        loaded = Load(module, data);
        Assert.IsTrue(loaded.Texts!.QuestDescriptionsLoaded);
        Assert.AreEqual("Module A", Text(loaded.Texts.QuestDescription(1000, 10)), "the module copy wins at module load");
        Assert.AreEqual("Module low A", Text(loaded.Texts.QuestDescription(1000, 2)));
    }

    [TestMethod]
    public void Load_refuses_an_unparsable_or_oversized_file()
    {
        var data = Path.Combine(_root, "data"); var module = Path.Combine(_root, "module");
        Write(module, "rules/gamequest.mes", "{1000}{1 0}");
        Write(module, "mes/gamequestlogdumb.mes", "{1000}{Broken");
        var broken = Load(module, data);
        Assert.AreEqual((LogbookTextStatus.ParseFailed, LogbookTexts.QuestLogLowPath, MesParseStatus.UnterminatedField),
            (broken.Status, broken.Path, broken.Parse!.Status));
        Assert.IsNull(broken.Texts);

        Write(module, "mes/gamequestlogdumb.mes", "{1000}{Low}");
        Write(module, "mes/game_rd_npc_m2m_dumb.mes", "{20000}{A longer low rumor}");
        Write(module, "rules/gamequest.mes", "{1000}{1 0");
        var rules = Load(module, data);
        Assert.AreEqual((LogbookTextStatus.ParseFailed, LogbookTexts.QuestRulesPath), (rules.Status, rules.Path), "the quest table is read and parsed too");
        Write(module, "rules/gamequest.mes", "{1000}{1 0}");
        var tooLarge = Load(module, data, new() { MaxBytes = 16 });
        Assert.AreEqual((LogbookTextStatus.ParseFailed, LogbookTexts.RumorLowPath, MesParseStatus.TooLarge), (tooLarge.Status, tooLarge.Path, tooLarge.Parse!.Status));
    }

    private LogbookTextLoad Load(string module, string data, MesReaderOptions? options = null)
    {
        Directory.CreateDirectory(module); Directory.CreateDirectory(data);
        var plan = new ContentMountPlan(_root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module), new(ContentSourceRole.DataDirectory, 0, data)]);
        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        return LogbookTexts.Load(stack, options);
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
