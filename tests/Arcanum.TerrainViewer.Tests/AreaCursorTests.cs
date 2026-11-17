using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-056 the original's cursor art (ledger CU1–CU6, CUA1): the art by mode, combat mode and called-shot key, the
/// animation frames, and the installed art.</summary>
[TestClass]
public sealed class AreaCursorTests
{
    [TestMethod]
    public void The_mode_picks_the_cursor_and_the_target_never_does()
    {
        foreach (var combat in new[] { false, true })
        foreach (var aim in new HitLocation?[] { null, HitLocation.Head, HitLocation.Arm, HitLocation.Leg })
        foreach (var swapped in new[] { false, true })
        {
            Assert.AreEqual(AreaCursorArt.Spell, AreaCursorArt.Art(AreaCursorMode.SpellTarget, combat, aim, swapped), "CU1 mode 1: spellcur");
            Assert.AreEqual(AreaCursorArt.Skill, AreaCursorArt.Art(AreaCursorMode.SkillTarget, combat, aim, swapped), "CU1 mode 2: skillcur");
            Assert.AreEqual(AreaCursorArt.Gauntlet, AreaCursorArt.Art(AreaCursorMode.Window, combat, aim, swapped), "CU1 other modes: the gauntlet");
            Assert.AreEqual(AreaCursorArt.Spell, AreaCursorArt.Art(AreaCursorMode.Quill, combat, aim, swapped), "WM30: the note quill's cursor is art 21 (ARC-UI-106)");
            if (!combat) Assert.AreEqual(AreaCursorArt.Gauntlet, AreaCursorArt.Art(AreaCursorMode.Game, combat, aim, swapped), "CU2 no weapon: the gauntlet");
        }
        Assert.AreEqual(AreaCursorArt.Battle, AreaCursorArt.Art(AreaCursorMode.Game, true, null, false), "CU2 combat mode: battlecur");
        Assert.AreEqual(AreaCursorArt.Battle, AreaCursorArt.Art(AreaCursorMode.Game, true, null, true));
        Assert.AreEqual(AreaCursorArt.Battle, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Torso, false), "no key aims at the torso");
    }

    [TestMethod]
    public void Called_shot_cursors_follow_the_key_in_original_and_the_location_in_steamwoven()
    {
        // CU3 / CUA1: the executable picks by key (comma 818, period 819 leg picture, slash 820 arm picture); FA4 aims period at the
        // arm and slash at the leg.
        Assert.AreEqual(AreaCursorArt.CalledHead, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Head, GameRules.Original.Interface.CalledShotCursorsSwapped));
        Assert.AreEqual(AreaCursorArt.CalledLeg, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Arm, GameRules.Original.Interface.CalledShotCursorsSwapped));
        Assert.AreEqual(AreaCursorArt.CalledArm, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Leg, GameRules.Original.Interface.CalledShotCursorsSwapped));
        Assert.AreEqual(AreaCursorArt.CalledHead, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Head, GameRules.Steamwoven.Interface.CalledShotCursorsSwapped));
        Assert.AreEqual(AreaCursorArt.CalledArm, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Arm, GameRules.Steamwoven.Interface.CalledShotCursorsSwapped));
        Assert.AreEqual(AreaCursorArt.CalledLeg, AreaCursorArt.Art(AreaCursorMode.Game, true, HitLocation.Leg, GameRules.Steamwoven.Interface.CalledShotCursorsSwapped));
    }

    [TestMethod]
    public void Animated_cursors_step_at_their_rate_and_wrap()
    {
        // CU5: 1000 / fps milliseconds a frame; spellcur.art has 7 frames at 10 fps.
        Assert.AreEqual(0, AreaCursorArt.Frame(0, 7, 10));
        Assert.AreEqual(0, AreaCursorArt.Frame(99, 7, 10));
        Assert.AreEqual(1, AreaCursorArt.Frame(100, 7, 10));
        Assert.AreEqual(6, AreaCursorArt.Frame(699, 7, 10));
        Assert.AreEqual(0, AreaCursorArt.Frame(700, 7, 10), "wraps to the first frame");
        Assert.AreEqual(0, AreaCursorArt.Frame(12345, 1, 8), "one frame never steps");
        Assert.AreEqual(0, AreaCursorArt.Frame(12345, 3, 0), "no rate never steps");
        Assert.AreEqual(2, AreaCursorArt.Frame(2 * 333, 3, 3), "the step is the integer 1000 / fps");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaCursorArt.Frame(-1, 7, 10));
    }

    [TestMethod]
    public void Content_is_ready_only_with_every_art()
    {
        Assert.IsFalse(AreaCursorContent.Empty("none").Ready);
        var frame = new AreaInterfaceFrame(1, 1, [0, 0, 0, 255]);
        var all = AreaCursorArt.ArtNumbers.ToDictionary(n => n, n => new AreaInterfaceArtLoad(n, AreaInterfaceArtStatus.Ready, $"{n}.art", [frame], "synthetic"));
        Assert.IsTrue(new AreaCursorContent(all, "synthetic").Ready);
        var missing = new Dictionary<int, AreaInterfaceArtLoad>(all) { [AreaCursorArt.Spell] = new(AreaCursorArt.Spell, AreaInterfaceArtStatus.MissingFile, "spellcur.art", [], "missing") };
        Assert.IsFalse(new AreaCursorContent(missing, "synthetic").Ready);
        var empty = new Dictionary<int, AreaInterfaceArtLoad>(all) { [AreaCursorArt.Battle] = new(AreaCursorArt.Battle, AreaInterfaceArtStatus.Ready, "battlecur.art", [], "no frames") };
        Assert.IsFalse(new AreaCursorContent(empty, "synthetic").Ready);
        var outside = new Dictionary<int, AreaInterfaceArtLoad>(all) { [AreaCursorArt.Gauntlet] = all[AreaCursorArt.Gauntlet] with { Frames = [frame with { HotX = 1 }] } };
        Assert.IsFalse(new AreaCursorContent(outside, "synthetic").Ready, "a hot point outside the image");
        var large = new Dictionary<int, AreaInterfaceArtLoad>(all) { [AreaCursorArt.Gauntlet] = all[AreaCursorArt.Gauntlet] with { Frames = [new(257, 1, new byte[257 * 4])] } };
        Assert.IsFalse(new AreaCursorContent(large, "synthetic").Ready, "an image larger than a system cursor");
    }

    [TestMethod]
    public void The_cursor_sheet_lays_each_art_in_a_row_and_marks_the_hot_point()
    {
        // ARC-UI-2489 (H13): a 2 x 2 opaque white frame with the hot point at (1, 0) for every art, the spell art with two frames.
        byte[] white = [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255];
        var frame = new AreaInterfaceFrame(2, 2, white, 1, 0);
        var art = AreaCursorArt.ArtNumbers.ToDictionary(n => n, n => new AreaInterfaceArtLoad(n, AreaInterfaceArtStatus.Ready, $"{n}.art",
            n == AreaCursorArt.Spell ? [frame, frame] : [frame], "synthetic", 10));
        var sheet = AreaCursorSheet.Compose(new AreaCursorContent(art, "synthetic"))!.Value;
        var pad = AreaCursorSheet.Pad;
        Assert.AreEqual(pad + 2 * (2 + pad), sheet.Width, "the widest row (two frames) sets the width");
        Assert.AreEqual(pad + AreaCursorArt.ArtNumbers.Count * (2 + pad), sheet.Height, "one row each");
        Assert.AreEqual(sheet.Width * sheet.Height * 4, sheet.Rgba.Length);
        Assert.AreEqual(0x58, sheet.Rgba[0], "a grey ground in the corner");
        var firstFrame = ((pad + 1) * sheet.Width + pad) * 4; // frame pixel (0, 1): off the hot cross at (1, 0)
        Assert.AreEqual(255, sheet.Rgba[firstFrame + 1], "the frame's own pixel, opaque white");
        var hot = (pad * sheet.Width + pad + 1) * 4;
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x20, 0x20, 0xFF }, sheet.Rgba[hot..(hot + 4)], "the hot point is marked red");
        Assert.IsNull(AreaCursorSheet.Compose(AreaCursorContent.Empty("none")), "no art, no sheet");
        Assert.ThrowsExactly<ArgumentNullException>(() => AreaCursorSheet.Compose(null!));
    }

    /// <summary>The installed game's cursor art read (sizes, frame counts, rates and hot points only).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_cursor_art_reads()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var content = AreaCursorContent.Load(Path.Combine(root, "modules", "Arcanum.dat"));
        Console.WriteLine(content.Detail);
        Assert.IsTrue(content.Ready, content.Detail);
        Assert.AreEqual("cursor.art", content.Art[AreaCursorArt.Gauntlet].Name);
        Assert.AreEqual("spellcur.art", content.Art[AreaCursorArt.Spell].Name);
        Assert.AreEqual("battlecur.art", content.Art[AreaCursorArt.Battle].Name);
        Assert.AreEqual("Cursor-Called-Leg.ART", content.Art[AreaCursorArt.CalledLeg].Name);
        Assert.AreEqual((7, 10), (content.Art[AreaCursorArt.Spell].Frames.Count, content.Art[AreaCursorArt.Spell].FramesPerSecond), "the spell cursor is animated");
        foreach (var number in AreaCursorArt.ArtNumbers.Where(n => n != AreaCursorArt.Spell)) Assert.AreEqual(1, content.Art[number].Frames.Count, $"{number} is still");
        var gauntlet = content.Art[AreaCursorArt.Gauntlet].Frames[0];
        Assert.AreEqual((24, 27, 0, 3), (gauntlet.Width, gauntlet.Height, gauntlet.HotX, gauntlet.HotY), "the gauntlet points from its top left");
        var called = content.Art[AreaCursorArt.CalledHead].Frames[0];
        Assert.AreEqual((32, 37, 0, 0), (called.Width, called.Height, called.HotX, called.HotY));
    }
}
