using Arcanum.Simulation.Party;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaFollowerMenuTextsTests
{
    [TestMethod]
    public void The_project_words_stand_when_no_file_was_read()
    {
        var texts = AreaFollowerMenuTexts.Project("no game folder");
        Assert.IsFalse(texts.FromGame);
        Assert.AreEqual("no game folder", texts.Detail);
        CollectionAssert.AreEqual(Enum.GetValues<FollowerMenuItem>(), texts.Rows.ToArray(), "FP8: all eight rows");
        Assert.AreEqual("Walk", texts.Text(FollowerMenuItem.Walk));
        Assert.AreEqual("Character Sheet", texts.Text(FollowerMenuItem.CharacterSheet));
    }

    [TestMethod]
    public void The_rows_take_the_words_of_messages_zero_to_seven()
    {
        var texts = AreaFollowerMenuTexts.From(number => number is >= 0 and <= 7 ? $"слово {number}" : "лишнее", "test");
        Assert.IsTrue(texts.FromGame);
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
            Assert.AreEqual($"слово {(int)item}", texts.Text(item), $"FP8: {item} is message {(int)item}");
        Assert.AreEqual(8, texts.Rows.Count);
    }

    [TestMethod]
    public void A_row_whose_message_is_missing_is_skipped_and_the_others_keep_their_words()
    {
        var texts = AreaFollowerMenuTexts.From(number => number is 2 or 5 ? null : $"m{number}", "test");
        Assert.IsNull(texts.Text(FollowerMenuItem.StayClose), "FP8: no message, no row (and no project word in its place)");
        Assert.IsNull(texts.Text(FollowerMenuItem.Wait));
        CollectionAssert.AreEqual(
            new[] { FollowerMenuItem.Walk, FollowerMenuItem.Attack, FollowerMenuItem.SpreadOut, FollowerMenuItem.BackOff, FollowerMenuItem.Inventory, FollowerMenuItem.CharacterSheet },
            texts.Rows.ToArray());
        Assert.AreEqual("m3", texts.Text(FollowerMenuItem.SpreadOut));
    }

    [TestMethod]
    public void A_file_with_no_messages_draws_no_rows_but_is_still_the_games()
    {
        var texts = AreaFollowerMenuTexts.From(_ => null, "empty");
        Assert.IsTrue(texts.FromGame);
        Assert.AreEqual(0, texts.Rows.Count);
    }
}
