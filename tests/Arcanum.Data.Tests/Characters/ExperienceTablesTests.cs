using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

// ARC-WORLD-030: synthetic MES tables in the shapes the source reads.
[TestClass]
public sealed class ExperienceTablesTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    [TestMethod]
    public void LevelsAreReadByNumberWithLevelOneAtZeroAndMissingLevelsAtZero()
    {
        var read = ExperienceTables.ReadLevels(Mes("{1}{5}\n{2}{ 2300 }\n{3}{+4400}\n{51}{9000}\n{52}{1}\n{0}{7}\n"));
        Assert.AreEqual(ExperienceTableStatus.Read, read.Status);
        Assert.AreEqual(ExperienceTables.Levels, read.Thresholds!.Count);
        Assert.AreEqual((0, 2300, 4400, 0, 9000), (read.Thresholds[0], read.Thresholds[1], read.Thresholds[2], read.Thresholds[3], read.Thresholds[50]));
    }

    [TestMethod]
    public void AnEntryThatIsNotOneNumberRefusesTheTable()
    {
        foreach (var text in new[] { "12x", "", "  ", "1 2", "-", "99999999999", "2147483648", "1.5" })
        {
            var read = ExperienceTables.ReadLevels(Mes("{1}{0}\n{4}{" + text + "}\n"));
            Assert.AreEqual((ExperienceTableStatus.InvalidNumber, 4), (read.Status, read.Number), text);
            Assert.IsNull(read.Thresholds);
        }
        Assert.AreEqual(ExperienceTableStatus.Read, ExperienceTables.ReadLevels(Mes("{4}{-2147483648}\n")).Status);
        // Entries the level table does not read are not checked: level 1 is always 0, and 52 and above are outside it.
        Assert.AreEqual(ExperienceTableStatus.Read, ExperienceTables.ReadLevels(Mes("{60}{text}\n")).Status);
        Assert.AreEqual(ExperienceTableStatus.Read, ExperienceTables.ReadLevels(Mes("{1}{text}\n")).Status);
    }

    [TestMethod]
    public void ValueTablesKeepEveryEntry()
    {
        var read = ExperienceTables.ReadValues(Mes("{0}{15}\n{1}{170}\n{50}{-3}\n"));
        Assert.AreEqual(ExperienceTableStatus.Read, read.Status);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 50 }, read.Values!.Keys.ToArray());
        Assert.AreEqual((15, 170, -3), (read.Values[0], read.Values[1], read.Values[50]));
        var refused = ExperienceTables.ReadValues(Mes("{0}{15}\n{7}{x}\n"));
        Assert.AreEqual((ExperienceTableStatus.InvalidNumber, 7), (refused.Status, refused.Number));
    }

    [TestMethod]
    public void QuestLevelsAreTheFirstNumberOfEachQuestEntry()
    {
        var read = ExperienceTables.ReadQuestLevels(Mes("{1}{not a quest}\n{1000}{5 -10 1 2 3}\n{1999}{  12  0}\n{2000}{x}\n"));
        Assert.AreEqual(ExperienceTableStatus.Read, read.Status);
        Assert.AreEqual(2, read.Values!.Count);
        Assert.AreEqual((5, 12), (read.Values[1000], read.Values[1999]));
        Assert.AreEqual((ExperienceTableStatus.MissingField, 1500), (ExperienceTables.ReadQuestLevels(Mes("{1500}{   }\n")).Status, ExperienceTables.ReadQuestLevels(Mes("{1500}{   }\n")).Number));
        Assert.AreEqual(ExperienceTableStatus.InvalidNumber, ExperienceTables.ReadQuestLevels(Mes("{1500}{5x 1}\n")).Status);
    }
}
