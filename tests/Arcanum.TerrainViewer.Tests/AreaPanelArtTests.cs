using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-024 panel art (ledger PA1–PA6, CH9): the vials' art numbers, the compact panel and follower frame layouts, and the
/// stored portrait of a critter record.</summary>
[TestClass]
public sealed class AreaPanelArtTests
{
    private static AreaObjectRecord Critter(int type, params AreaFieldValue[] fields) =>
        new(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 3, 300, new string('b', 64), 100), 0, type, 0, "p", "p", "r", "s", AreaScope.Core,
            null, 10, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);

    private static AreaFieldValue Portrait(long? number, AreaPresence presence = AreaPresence.Value) =>
        new(AreaPortraits.PortraitSlot, presence, AreaValueOrigin.Prototype, number, [], null);

    [TestMethod]
    public void The_vials_and_the_compact_panel_are_the_originals()
    {
        Assert.AreEqual((18, 17, 19, 20), (AreaGameBarLayout.HealthVialArt, AreaGameBarLayout.PoisonedVialArt, AreaGameBarLayout.FatigueVialArt, AreaGameBarLayout.EmptyVialArt), "PA1");
        Assert.AreEqual((14, 472, 28, 88), AreaGameBarLayout.HealthGauge, "0x5C63D8");
        Assert.AreEqual((754, 473, 28, 88), AreaGameBarLayout.FatigueGauge);
        var (x, y, w, h) = AreaCompactPanelLayout.Window;
        Assert.AreEqual((800, 600), (x + w, y + h), "PA4: the compact panel ends at the screen's bottom right");
        Assert.AreEqual(AreaCompactPanelLayout.HealthX + AreaCompactPanelLayout.ColumnWidth, AreaCompactPanelLayout.Divider.X, "the divider sits between the columns");
    }

    [TestMethod]
    public void Follower_frames_stand_down_the_left_edge()
    {
        Assert.AreEqual((11, 50), AreaFollowerFrameLayout.Slot(0), "CH9");
        Assert.AreEqual((11, 360), AreaFollowerFrameLayout.Slot(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaFollowerFrameLayout.Slot(6), "six slots");
        CollectionAssert.AreEqual(new[] { 503, 504, 505, 616 }, AreaFollowerFrameLayout.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void A_critters_stored_portrait_is_its_field_0xE7()
    {
        Assert.AreEqual(1050, AreaPortraits.Stored(Critter(16, Portrait(1050))), "an NPC");
        Assert.AreEqual(1001, AreaPortraits.Stored(Critter(15, Portrait(1001))), "a PC");
        Assert.IsNull(AreaPortraits.Stored(Critter(16, Portrait(0))), "0 is none");
        Assert.IsNull(AreaPortraits.Stored(Critter(16, Portrait(-3))), "a negative id is none (the original's path bug, PO2)");
        Assert.IsNull(AreaPortraits.Stored(Critter(16, Portrait(null, AreaPresence.Absent))), "absent");
        Assert.IsNull(AreaPortraits.Stored(Critter(16)), "no field");
        Assert.IsNull(AreaPortraits.Stored(Critter(14, Portrait(1050))), "not a critter: slot 49 is something else");
    }

    [TestMethod]
    public void Portrait_content_without_a_game_folder_is_empty()
    {
        var content = AreaPortraitContent.Empty("self-test");
        Assert.IsFalse(content.ArtReady);
        Assert.IsNull(content.Catalogue);
        Assert.AreEqual(0, content.Portraits.Count);
    }

    /// <summary>The installed game's frame art and a portrait image read (sizes only).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_frame_art_and_portraits_read()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var content = AreaPortraitContent.Load(module, [1000]);
        Console.WriteLine(content.Detail);
        Assert.IsTrue(content.ArtReady, content.Detail);
        Assert.AreEqual((40, 49), (content.Art[503].Frames[0].Width, content.Art[503].Frames[0].Height), "the frame");
        foreach (var bar in new[] { 504, 505, 616 }) Assert.AreEqual((34, 3), (content.Art[bar].Frames[0].Width, content.Art[bar].Frames[0].Height), $"bar {bar}");
        var file = content.Portraits[1000];
        Assert.AreEqual(AreaPortraitStatus.Ready, file.Status, file.Detail);
        Assert.IsTrue(file.Path!.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) && !file.Path.EndsWith("_b.bmp", StringComparison.OrdinalIgnoreCase), "the small image for 32 × 32");
        Assert.AreEqual("BM", System.Text.Encoding.ASCII.GetString(file.Bytes!, 0, 2));
        var more = AreaPortraitContent.Load(module, [1000, 1001], content);
        Assert.AreSame(content.Catalogue, more.Catalogue, "a later load keeps the catalogue");
        Assert.AreEqual(2, more.Portraits.Count);
        var bars = AreaGameBarContent.Load(module);
        foreach (var vial in new[] { 17, 18, 19 }) Assert.AreEqual((28, 96), (bars.Art[vial].Frames[0].Width, bars.Art[vial].Frames[0].Height), $"vial {vial}");
        Assert.AreEqual(15, bars.Art[18].Frames.Count, "redvial has 15 frames; the gauge draws frame 0");
        Assert.AreEqual((34, 90), (bars.Art[20].Frames[0].Width, bars.Art[20].Frames[0].Height), "the empty vial");
    }
}
