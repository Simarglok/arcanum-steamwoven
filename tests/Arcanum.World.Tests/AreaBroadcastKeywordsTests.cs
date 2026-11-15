using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Party;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

// ARC-UI-2305 (ledger OL3, init 0x4C2C10): the order words from synthetic copies of the two message files (invented words).
[TestClass]
public sealed class AreaBroadcastKeywordsTests
{
    private static MesFile Mes(params (int Number, string Text)[] messages) =>
        MesFile.Parse(Encoding.Latin1.GetBytes(string.Concat(messages.Select(m => $"{{{m.Number}}}{{{m.Text}}}\n")))).File!;

    private static readonly string[] Names = ["none", "leave", "wait", "follow", "move", "close", "spread", "curse", "join", "disband", "attack", "walk", "stop", "other"];

    [TestMethod]
    public void The_keywords_run_from_message_100_to_the_first_gap_and_take_their_types_from_the_rules_file()
    {
        var broadcast = Mes((100, "hold"), (101, "Come"), (102, "scatter"), (104, "lost"), (500, "#walk"));
        var rules = Mes([.. Names.Select((name, i) => (i, name)), (100, "wait"), (101, "follow"), (102, "spread"), (104, "attack")]);
        var words = AreaBroadcastKeywords.Parse(broadcast, rules);
        Assert.AreEqual(3, words.Count, "104 is after the gap at 103");
        Assert.AreEqual(CompanionOrderType.Wait, words.Lookup("hold"));
        Assert.AreEqual(CompanionOrderType.Follow, words.Lookup("COME"));
        Assert.AreEqual(CompanionOrderType.SpreadOut, words.Lookup("scatter"));
        Assert.AreEqual(CompanionOrderType.None, words.Lookup("lost"));
    }

    [TestMethod]
    public void Without_keywords_or_type_entries_the_table_is_empty_or_inert()
    {
        Assert.AreEqual(0, AreaBroadcastKeywords.Parse(Mes((1, "x")), Mes((0, "none"))).Count);
        var words = AreaBroadcastKeywords.Parse(Mes((100, "hold")), Mes((0, "none"), (2, "wait")));
        Assert.AreEqual(CompanionOrderType.None, words.Lookup("hold"), "no rules entry for the keyword");
    }

    [TestMethod]
    public void A_module_outside_a_game_folder_gives_a_typed_status()
    {
        var (status, keywords, _) = AreaBroadcastKeywords.Load(Path.Combine(Path.GetTempPath(), "arc-ui-2305-no-module"));
        Assert.AreNotEqual(AreaBroadcastKeywordsStatus.Ready, status);
        Assert.IsNull(keywords);
    }
}
