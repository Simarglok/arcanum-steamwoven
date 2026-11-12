using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1117 (ledger OH2, OH4): the hover rings' rules over invented records and an invented game folder (no original data).</summary>
[TestClass]
public sealed class AreaHoverRingTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private const string Table = "{0}{255, 0, 0}\n{1}{0,255,0}\n{2}{0, 0, 255}\n{3}{10, 20, 30}\n{4}{40, 50, 60}\n{5}{70, 80, 90}\n{6}{100, 110, 120}\n";

    private static AreaHoverRingFrame Frame(int number, int frame, int width, int height, int hotX, int hotY, int deltaX = 0, int deltaY = 0)
    {
        var rgba = new byte[width * height * 4];
        var raw = unchecked((uint)(5 << 28 | number));
        var visual = new AreaVisualRecord(AreaHoverRingArts.VisualKey(number, frame), raw, raw, new(new(AreaSourceRole.Loose, -1), 0, 0, 0, 0, 0, new string('0', 64)),
            new string('1', 64), 1, 0, 0, 0, frame, 0, 0, false, width, height, hotX, hotY, deltaX, deltaY, 0, 0, 100, 1, rgba.Length, new string((char)('a' + frame), 64), AreaHoverRingArts.Policy);
        return new(visual, AreaFrameKey.From(visual), new(width, height, rgba), deltaX, deltaY);
    }

    private static AreaDrawCommand Body(int destX, int destY, (int X, int Y)? hot, int wadeRows = 0)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 1, 100, new string('b', 64), 100);
        var visual = new AreaVisualKey(AreaSourceKind.Mobile, new(new string('a', 64), 1, 1), 1, new string('c', 64));
        var frame = new AreaFrameKey(new(new(AreaSourceRole.Loose, 1), 0, 0, 0, 0, 0, new string('d', 64)), 0, 0, 0, "p", new string('e', 64), 1, new string('f', 64));
        return new AreaDrawCommand(visual, frame, key, new TerrainRect(0, 0, 40, 60), new TerrainRect(destX, destY, 40, 60), false, false, 4, 5, 6, 0, 0, 255, false)
        { Hot = hot, WadeRows = wadeRows, ArtClass = 2 };
    }

    [TestMethod]
    public void OnlyCrittersAndTrapsGetRings()
    {
        foreach (var type in new[] { 15, 16, 17 }) Assert.IsTrue(AreaHoverRingRules.Applies(type), $"type {type}");
        foreach (var type in new[] { 0, 1, 2, 3, 4, 5, 9, 14, 18 }) Assert.IsFalse(AreaHoverRingRules.Applies(type), $"type {type}");
    }

    [TestMethod]
    public void TheArtsAre467And468AndBoth555InCombatMode()
    {
        Assert.AreEqual((467, 468), AreaHoverRingRules.Arts(false));
        Assert.AreEqual((555, 555), AreaHoverRingRules.Arts(true));
    }

    [TestMethod]
    public void AnNpcRingTakesItsReactionCategorysColourAndEveryOtherRingIsWhite()
    {
        var table = AreaHoverRingRules.ParseColours(Mes(Table), out var detail);
        Assert.IsNotNull(table, detail);
        // 0x4C0FC0: <= 0 hatred (6), <= 20 dislike (5), <= 40 suspicious (4), <= 60 neutral (3), <= 80 courteous (2), <= 100 amiable (1), above love (0).
        foreach (var (reaction, row) in new[] { (-5, 6), (0, 6), (1, 5), (20, 5), (21, 4), (40, 4), (41, 3), (60, 3), (61, 2), (80, 2), (81, 1), (100, 1), (101, 0), (200, 0) })
            Assert.AreEqual(table![row], AreaHoverRingRules.Colour(AreaHoverRingRules.NpcType, reaction, table), $"reaction {reaction}");
        Assert.AreEqual(((byte)255, (byte)0, (byte)0), AreaHoverRingRules.Colour(16, 150, table));
        Assert.AreEqual(AreaHoverRingRules.White, AreaHoverRingRules.Colour(15, 50, table), "a PC is white");
        Assert.AreEqual(AreaHoverRingRules.White, AreaHoverRingRules.Colour(17, 50, table), "a trap is white");
        Assert.AreEqual(AreaHoverRingRules.White, AreaHoverRingRules.Colour(16, null, table), "an NPC with no reaction facts is white");
        Assert.AreEqual(AreaHoverRingRules.White, AreaHoverRingRules.Colour(16, 50, null), "no colour table leaves the rings white");
    }

    [TestMethod]
    public void TheColourTableReadsSevenRowsOfThreeNumbersAndRefusesAnythingElse()
    {
        var table = AreaHoverRingRules.ParseColours(Mes("{0}{ 300, -4, 7 }\n{1}{0,255,0}\n{2}{0, 0, 255}\n{3}{10, 20, 30}\n{4}{40, 50, 60}\n{5}{70, 80, 90}\n{6}{100, 110, 120}\n"), out _);
        Assert.AreEqual(((byte)255, (byte)0, (byte)7), table![0], "held to a byte");
        Assert.AreEqual(7, table.Count);
        Assert.IsNull(AreaHoverRingRules.ParseColours(Mes("{0}{1, 2, 3}\n{1}{1, 2, 3}\n"), out var missing));
        StringAssert.Contains(missing, "no row 2");
        Assert.IsNull(AreaHoverRingRules.ParseColours(Mes(Table.Replace("{10, 20, 30}", "{10, 20}")), out var short3));
        StringAssert.Contains(short3, "row 3");
        Assert.IsNull(AreaHoverRingRules.ParseColours(Mes(Table.Replace("{10, 20, 30}", "{10, x, 30}")), out var text));
        StringAssert.Contains(text, "row 3");
    }

    [TestMethod]
    public void TheRingIsPlacedAtTheBodysAnchorByTheFramesDeltaAndCentre()
    {
        // The body's anchor: its destination plus its hotspot (the tile's centre and the object's offsets).
        var body = Body(100, 200, (18, 52));
        var frame = Frame(467, 0, 80, 40, 40, 20);
        var command = AreaHoverRingRules.Command(body, frame)!;
        Assert.AreEqual(new TerrainRect(100 + 18 - 40, 200 + 52 - 20, 80, 40), command.Destination);
        Assert.AreEqual(new TerrainRect(0, 0, 80, 40), command.Source);
        Assert.IsTrue(command.Additive && command.Unlit && command.Alpha == 255 && !command.Multiply && command.WadeRows == 0);
        Assert.IsNull(command.Identity, "a ring is no object: it is neither picked nor hidden with a body");
        Assert.AreEqual(AreaHoverRingRules.InterfaceArtClass, command.ArtClass);
        Assert.AreEqual((frame.Visual.Key, frame.Key), (command.Visual, command.Frame));
        // The frame's delta is added, its centre taken away (0x443880: x = anchor + deltaX - centreX).
        var shifted = AreaHoverRingRules.Command(body, Frame(467, 1, 80, 40, 40, 20, deltaX: 3, deltaY: -2))!;
        Assert.AreEqual(new TerrainRect(100 + 18 - 40 + 3, 200 + 52 - 20 - 2, 80, 40), shifted.Destination);
    }

    [TestMethod]
    public void AWadingBodysRingIsFifteenPixelsLowerAndABodyWithoutAHotspotHasNone()
    {
        var frame = Frame(468, 0, 80, 40, 40, 20);
        var dry = AreaHoverRingRules.Command(Body(100, 200, (18, 52)), frame)!;
        var wading = AreaHoverRingRules.Command(Body(100, 200, (18, 52), wadeRows: 15), frame)!;
        Assert.AreEqual(dry.Destination.X, wading.Destination.X);
        Assert.AreEqual(dry.Destination.Y + 15, wading.Destination.Y);
        Assert.IsNull(AreaHoverRingRules.Command(Body(100, 200, null), frame));
    }

    [TestMethod]
    public void TheRingFollowsTheBodyWhenItMoves()
    {
        var frame = Frame(467, 0, 80, 40, 40, 20);
        var a = AreaHoverRingRules.Command(Body(100, 200, (18, 52)), frame)!;
        var b = AreaHoverRingRules.Command(Body(140, 220, (18, 52)), frame)!;
        Assert.AreEqual((40, 20), (b.Destination.X - a.Destination.X, b.Destination.Y - a.Destination.Y));
    }

    [TestMethod]
    public void EachRingStepsToTheNextFrameEveryThousandOverRateMillisecondsAndWraps()
    {
        var clock = new AreaHoverRingClock();
        clock.Start(5000);
        // 10 frames per second: 100 ms; 4 frames.
        Assert.AreEqual((0, 0), clock.Step(5000, 10, 4, 10, 4));
        Assert.AreEqual((0, 0), clock.Step(5099, 10, 4, 10, 4), "not due yet");
        Assert.AreEqual((1, 1), clock.Step(5100, 10, 4, 10, 4), "due at exactly the interval");
        Assert.AreEqual((1, 1), clock.Step(5150, 10, 4, 10, 4));
        Assert.AreEqual((2, 2), clock.Step(5200, 10, 4, 10, 4));
        Assert.AreEqual((3, 3), clock.Step(5300, 10, 4, 10, 4));
        Assert.AreEqual((0, 0), clock.Step(5400, 10, 4, 10, 4), "wraps to frame 0");
        // A late frame steps once and restamps (no catch-up).
        Assert.AreEqual((1, 1), clock.Step(5900, 10, 4, 10, 4));
        Assert.AreEqual((1, 1), clock.Step(5950, 10, 4, 10, 4));
    }

    [TestMethod]
    public void TheTwoRingsKeepTheirOwnRateAndAStartRestartsBoth()
    {
        var clock = new AreaHoverRingClock();
        clock.Start(0);
        Assert.AreEqual((1, 0), clock.Step(100, 10, 3, 5, 3), "10 fps is due at 100 ms, 5 fps at 200 ms");
        Assert.AreEqual((2, 1), clock.Step(200, 10, 3, 5, 3));
        clock.Start(1000);
        Assert.AreEqual((0, 0), clock.Step(1000, 10, 3, 5, 3), "a new hover starts at frame 0");
        Assert.AreEqual((0, 0), clock.Step(5000, 0, 3, -1, 3), "a rate of 0 or less never steps");
        Assert.AreEqual((0, 0), clock.Step(6000, 10, 0, 10, 0), "an art without frames draws frame 0");
        Assert.AreEqual((0, 0), clock.Step(6200, 10, 1, 10, 1), "a one-frame art stays on its frame");
    }

    [TestMethod]
    public void TheRingArtsAndTheColourTableAreReadFromTheGameFolder()
    {
        var root = Directory.CreateTempSubdirectory("arc-ui-1117-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat(
                ("art\\interface\\hoverover.art", AreaOwnerFixture.Art(60, 30, 30, 15, true)),
                ("art\\interface\\hoverunder.art", AreaOwnerFixture.Art(80, 40, 40, 20, true)),
                ("art\\interface\\interface.mes", Encoding.ASCII.GetBytes("{467}{hoverunder.art}\n{468}{hoverover.art}\n{555}{missing.art}\n")),
                ("rules\\reaction_colors.mes", Encoding.ASCII.GetBytes(Table))));
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            var opened = ContentStack.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!);
            Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
            using var stack = opened.Stack!;
            var content = AreaHoverRingContent.Load(stack);
            Assert.IsNotNull(content.Colours, content.Detail);
            Assert.AreEqual(((byte)255, (byte)0, (byte)0), content.Colours![0]);
            var under = content.Art(467)!;
            Assert.AreEqual(1, under.Frames.Count);
            Assert.AreEqual((80, 40, 40, 20), (under.Frames[0].Width, under.Frames[0].Height, under.Frames[0].Visual.HotX, under.Frames[0].Visual.HotY));
            // The fixture's ART header carries offsets 777 and -999, which a ring takes as the frame's delta.
            Assert.AreEqual((777, -999), (under.Frames[0].DeltaX, under.Frames[0].DeltaY));
            Assert.AreEqual(5u, under.Frames[0].Visual.RawArt >> 28, "interface art class");
            Assert.AreEqual(60, content.Art(468)!.Frames[0].Width);
            Assert.IsNull(content.Art(555), "a named file the folder lacks draws no ring");
            Assert.AreEqual(AreaHoverRingArtStatus.MissingFile, content.Arts[555].Status);
            StringAssert.Contains(content.Detail, "arts 2/3 read");
            Assert.AreNotEqual(under.Frames[0].Key, content.Art(468)!.Frames[0].Key);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void AFolderWithoutTheColourTableOrTheIndexReadsNoColoursAndNoArts()
    {
        var root = Directory.CreateTempSubdirectory("arc-ui-1117-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            var opened = ContentStack.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!);
            using var stack = opened.Stack!;
            var content = AreaHoverRingContent.Load(stack);
            Assert.IsNull(content.Colours);
            Assert.IsTrue(content.Arts.Values.All(load => load.Art is null && load.Status == AreaHoverRingArtStatus.Unnamed));
            Assert.AreEqual(AreaHoverRingRules.White, AreaHoverRingRules.Colour(16, 90, content.Colours));
        }
        finally { root.Delete(true); }
    }
}
