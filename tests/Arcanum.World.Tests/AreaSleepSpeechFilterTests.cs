using System.Text;
using Arcanum.Data.Messages;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

// ARC-WORLD-2310 (init 0x4F69D0): the sleeping filter from a synthetic copy of text_filter_sleep.mes (invented words).
[TestClass]
public sealed class AreaSleepSpeechFilterTests
{
    private static MesFile Mes(params (int Number, string Text)[] messages) =>
        MesFile.Parse(Encoding.Latin1.GetBytes(string.Concat(messages.Select(m => $"{{{m.Number}}}{{{m.Text}}}\n")))).File!;

    [TestMethod]
    public void Message_0_is_the_filler_and_the_kept_words_run_from_1_to_the_first_gap()
    {
        var (status, filter) = AreaSleepSpeechFilter.Parse(Mes((0, "zzz"), (1, "mm"), (2, "uh"), (4, "lost"), (9, "late")));
        Assert.AreEqual(AreaSleepFilterStatus.Ready, status);
        CollectionAssert.AreEqual(new[] { "mm", "uh" }, filter!.KeepWords.ToArray(), "4 is after the gap at 3");
        Assert.AreEqual("zzz mmz", filter.Transform("abc mmx"));
    }

    [TestMethod]
    public void A_file_without_a_filler_is_a_typed_status()
    {
        Assert.AreEqual(AreaSleepFilterStatus.EmptyFiller, AreaSleepSpeechFilter.Parse(Mes((1, "mm"))).Status);
        Assert.AreEqual(AreaSleepFilterStatus.EmptyFiller, AreaSleepSpeechFilter.Parse(Mes((0, ""))).Status);
    }

    [TestMethod]
    public void A_module_outside_a_game_folder_gives_a_typed_status()
    {
        var (status, filter, _) = AreaSleepSpeechFilter.Load(Path.Combine(Path.GetTempPath(), "arc-world-2310-no-module"));
        Assert.AreNotEqual(AreaSleepFilterStatus.Ready, status);
        Assert.IsNull(filter);
    }
}
