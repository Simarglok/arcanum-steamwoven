using Arcanum.Presentation.Hud;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-026 maintain bar on the top strip (ledger MB1–MB9): layout, slot looks and the closing stops. Runs are invented.</summary>
[TestClass]
public sealed class AreaMaintainBarTests
{
    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        CollectionAssert.AreEqual(new[] { 281, 331, 381, 431, 481 }, Enumerable.Range(0, 5).Select(AreaMaintainBarLayout.SlotX).ToArray(), "0x5C6E40");
        CollectionAssert.AreEqual(new[] { 188, 189, 190, 191, 192 }, Enumerable.Range(0, 5).Select(AreaMaintainBarLayout.SlotArt).ToArray(), "SplSlt1-5");
        CollectionAssert.AreEqual(new[] { 628, 629, 630, 631, 632 }, Enumerable.Range(0, 5).Select(AreaMaintainBarLayout.PluggedArt).ToArray(), "SP_Plugged1-5");
        Assert.AreEqual((330, 2), AreaMaintainBarLayout.PluggedPlace(1), "one pixel up and left of the button");
        CollectionAssert.IsSubsetOf(AreaMaintainBarLayout.ArtNumbers.ToArray(), AreaGameBarContent.ArtNumbers.ToArray(), "read with the bars' art");
    }

    [TestMethod]
    public void Slots_show_their_runs_icon_their_slot_art_or_closed()
    {
        var bar = MaintainBarRules.Add(MaintainBar.Empty, 7, 12, 12).Bar;
        bar = MaintainBarRules.Stop(MaintainBarRules.Add(bar, 9, 30, 12).Bar, 1)!.Value.Bar;
        var looks = AreaMaintainBarLayout.Looks(bar, 3, run => run switch { 7 => 12, 9 => 30, _ => null });
        Assert.AreEqual(new AreaMaintainSlotLook(AreaMaintainSlotKind.Run, HotkeyBarRules.SpellIcon(12), 12, 7), looks[0]);
        Assert.AreEqual(AreaMaintainSlotKind.Run, looks[1].Kind, "a stopping slot keeps its icon until its run ends");
        Assert.AreEqual(new AreaMaintainSlotLook(AreaMaintainSlotKind.Empty, 190, -1, -1), looks[2]);
        Assert.AreEqual(new AreaMaintainSlotLook(AreaMaintainSlotKind.Closed, 191, -1, -1), looks[3]);
        Assert.AreEqual(AreaMaintainSlotKind.Closed, looks[4].Kind);
        Assert.AreEqual(AreaMaintainSlotKind.Empty, AreaMaintainBarLayout.Looks(bar, 3, _ => null)[0].Kind, "a run no longer there shows empty");
    }

    [TestMethod]
    public void A_drop_of_the_open_count_stops_the_runs_of_the_slots_it_closes()
    {
        var bar = MaintainBar.Empty;
        foreach (var run in new[] { 1, 2, 3 }) bar = MaintainBarRules.Add(bar, run, 5, 12).Bar;
        CollectionAssert.AreEqual(new[] { 1, 2 }, AreaMaintainBarLayout.ClosingRuns(bar, 3, 1).ToArray(), "slots 1 and 2 close while showing runs");
        Assert.AreEqual(0, AreaMaintainBarLayout.ClosingRuns(bar, null, 1).Count, "no known count yet");
        Assert.AreEqual(0, AreaMaintainBarLayout.ClosingRuns(bar, 1, 3).Count, "a rise closes nothing");
        var stopping = MaintainBarRules.Stop(bar, 2)!.Value.Bar;
        CollectionAssert.AreEqual(new[] { 1 }, AreaMaintainBarLayout.ClosingRuns(stopping, 3, 1).ToArray(), "a stopping slot takes no second click");
    }
}
