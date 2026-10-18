using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

// ARC-WORLD-036: synthetic scheme and spell tables in the shapes the original looks up.
[TestClass]
public sealed class AutoLevelTablesTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    private static string? Text(ReadOnlyMemory<byte>? bytes) => bytes is { } b ? Encoding.ASCII.GetString(b.Span) : null;

    [TestMethod]
    public void SchemesOneToFortyNineAreTheUserFileAndFiftyToOneNinetyNineTheGameFile()
    {
        var game = Mes("{3}{game 3}\n{50}{game 50}\n{199}{game 199}\n{200}{game 200}\n");
        var user = Mes("{3}{user 3}\n{49}{user 49}\n{50}{user 50}\n");
        Assert.AreEqual("user 3", Text(AutoLevelTables.Rule(game, user, 3)));
        Assert.AreEqual("user 49", Text(AutoLevelTables.Rule(game, user, 49)));
        Assert.AreEqual("game 50", Text(AutoLevelTables.Rule(game, user, 50)));
        Assert.AreEqual("game 199", Text(AutoLevelTables.Rule(game, user, 199)));
        Assert.IsNull(AutoLevelTables.Rule(game, user, 0));
        Assert.IsNull(AutoLevelTables.Rule(game, user, 200));
        Assert.IsNull(AutoLevelTables.Rule(game, user, -1));
        Assert.IsNull(AutoLevelTables.Rule(game, user, 51));
    }

    [TestMethod]
    public void ASpellsWillpowerIsItsIqKeyOrTheRankDefault()
    {
        // Spell 0's second line is 1001, spell 7's 1351, spell 79's 4951.
        var read = AutoLevelTables.ReadSpellWillpower(Mes("{1000}{Range: 3}\n{1001}{  IQ: 14, Cost: 5}\n{1351}{Cost: 2, IQ: 30}\n{4951}{IQ:-2}\n"));
        Assert.AreEqual(ExperienceTableStatus.Read, read.Status);
        Assert.AreEqual(80, read.Minima!.Count);
        // A key that does not start the line is not read (the original's keys are taken in order from the cursor).
        Assert.AreEqual((14, 12, -2, 9), (read.Minima[0], read.Minima[7], read.Minima[79], read.Minima[1]));
        var bad = AutoLevelTables.ReadSpellWillpower(Mes("{1051}{IQ: high, Cost: 1}\n"));
        Assert.AreEqual((ExperienceTableStatus.InvalidNumber, 1051), (bad.Status, bad.Number));
        CollectionAssert.AreEqual(new[] { 6, 9, 12, 15, 18 }, AutoLevelTables.ReadSpellWillpower(Mes("")).Minima!.Take(5).ToArray());
    }
}
