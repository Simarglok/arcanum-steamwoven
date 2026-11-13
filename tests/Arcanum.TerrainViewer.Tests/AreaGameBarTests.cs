using Arcanum.Data.Messages;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-018 interface bars (ledger GB1–GB7): layout, frames, the button states and sounds, texts and interface art names.</summary>
[TestClass]
public sealed class AreaGameBarTests
{
    [TestMethod]
    public void The_layout_is_the_originals_table()
    {
        Assert.AreEqual((0, 0, 800, 41), AreaGameBarLayout.StripRect(AreaBarStrip.Top), "GB1 window 0");
        Assert.AreEqual((0, 441, 800, 159), AreaGameBarLayout.StripRect(AreaBarStrip.Bottom), "GB1 window 1");
        CollectionAssert.AreEqual(
            new[] { "Skills 693,456 472 Toggle 1001", "Spells 649,494 473 Toggle 1000", "Combat 86,457 470 Push 1002", "Schematics 693,539 471 Push 1003",
                "Logbook 41,2 187 Push 1004", "Character 4,2 169 Push 1005", "Inventory 115,2 186 Push 1006", "Map 78,2 193 Push 1007",
                "FatePoints 157,9 137 Push 1008", "Sleep 605,9 137 Push 1009" },
            AreaGameBarLayout.Buttons.Select(b => $"{b.Button} {b.X},{b.Y} {b.Art} {b.Kind} {b.HoverMessage}").ToArray(), "GB2");
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var (x, y, w, h) = AreaGameBarLayout.StripRect(spec.Strip);
            Assert.IsTrue(spec.X >= x && spec.X < x + w && spec.Y >= y && spec.Y < y + h, $"{spec.Button} lies in its strip");
        }
        CollectionAssert.AreEquivalent(new[] { 185, 184, 472, 473, 470, 471, 187, 169, 186, 193, 137, 18, 17, 19, 20, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216,
                772, 773, 774, 775, 776, 777, 778, 779, 780, 781, 782, 194, 195, 558, 559, 560, 561 },
            AreaGameBarLayout.ArtNumbers.ToArray(), "the clock's ten arts (ARC-UI-1111), the experience strip's eleven (ARC-UI-1365) and the lights' idle and lit arts (ARC-UI-1364) are read with the bars");
    }

    [TestMethod]
    public void Frames_step_and_wrap_as_the_originals_button_draw()
    {
        Assert.AreEqual(0, AreaGameBarLayout.Frame(AreaBarButtonLook.Idle, 3));
        Assert.AreEqual(1, AreaGameBarLayout.Frame(AreaBarButtonLook.Down, 3));
        Assert.AreEqual(2, AreaGameBarLayout.Frame(AreaBarButtonLook.Hovered, 3));
        Assert.AreEqual(1, AreaGameBarLayout.Frame(AreaBarButtonLook.Down, 2));
        Assert.AreEqual(0, AreaGameBarLayout.Frame(AreaBarButtonLook.Hovered, 2), "a two-frame art wraps to frame 0 (GB4)");
        Assert.AreEqual(0, AreaGameBarLayout.Frame(AreaBarButtonLook.Down, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaGameBarLayout.Frame(AreaBarButtonLook.Idle, 0));
    }

    [TestMethod]
    public void A_push_button_sounds_on_press_and_acts_on_the_release_over_it()
    {
        var bar = new AreaGameBar();
        var enter = bar.Enter(AreaBarButton.Character);
        Assert.IsTrue(enter.HoverChanged); Assert.AreEqual(1005, enter.HoverMessage); Assert.AreEqual(0, enter.Sounds.Count, "no hover sound (GB6)");
        Assert.AreEqual(AreaBarButtonLook.Hovered, bar.Look(AreaBarButton.Character));
        var press = bar.Press(AreaBarButton.Character);
        CollectionAssert.AreEqual(new[] { 3000 }, press.Sounds.ToArray()); Assert.IsNull(press.Act);
        Assert.AreEqual(AreaBarButtonLook.Down, bar.Look(AreaBarButton.Character));
        var release = bar.Release(AreaBarButton.Character);
        CollectionAssert.AreEqual(new[] { 3001 }, release.Sounds.ToArray()); Assert.AreEqual(AreaBarButton.Character, release.Act);
        Assert.AreEqual(AreaBarButtonLook.Hovered, bar.Look(AreaBarButton.Character));
        var exit = bar.Exit(AreaBarButton.Character);
        Assert.IsTrue(exit.HoverChanged); Assert.IsNull(exit.HoverMessage); Assert.IsNull(bar.HoverMessage);
    }

    [TestMethod]
    public void A_release_away_from_the_held_button_does_nothing_and_others_take_no_pointer_meanwhile()
    {
        var bar = new AreaGameBar();
        bar.Enter(AreaBarButton.Inventory);
        bar.Press(AreaBarButton.Inventory);
        bar.Exit(AreaBarButton.Inventory);
        Assert.AreEqual(AreaBarButtonLook.Idle, bar.Look(AreaBarButton.Inventory), "held, pointer away");
        Assert.AreSame(AreaBarEffect.None, bar.Enter(AreaBarButton.Logbook));
        Assert.AreEqual(AreaBarButtonLook.Idle, bar.Look(AreaBarButton.Logbook));
        bar.Enter(AreaBarButton.Inventory);
        Assert.AreEqual(AreaBarButtonLook.Down, bar.Look(AreaBarButton.Inventory), "held, pointer back");
        bar.Exit(AreaBarButton.Inventory);
        Assert.AreSame(AreaBarEffect.None, bar.Release(null));
        Assert.IsNull(bar.Held);
        Assert.AreSame(AreaBarEffect.None, bar.Release(AreaBarButton.Inventory), "nothing held");
    }

    [TestMethod]
    public void Spells_and_skills_are_toggles_of_one_choice()
    {
        var bar = new AreaGameBar();
        var on = bar.Press(AreaBarButton.Spells);
        CollectionAssert.AreEqual(new[] { 3000 }, on.Sounds.ToArray()); Assert.AreEqual(AreaBarButton.Spells, on.Act);
        Assert.IsTrue(bar.IsOn(AreaBarButton.Spells));
        Assert.AreSame(AreaBarEffect.None, bar.Release(AreaBarButton.Spells), "a toggle acts on the press");
        bar.Enter(AreaBarButton.Spells);
        Assert.AreEqual(AreaBarButtonLook.Down, bar.Look(AreaBarButton.Spells), "an on toggle stays down under the pointer");
        var skills = bar.Press(AreaBarButton.Skills);
        CollectionAssert.AreEqual(new[] { 3000 }, skills.Sounds.ToArray());
        Assert.IsTrue(bar.IsOn(AreaBarButton.Skills)); Assert.IsFalse(bar.IsOn(AreaBarButton.Spells), "the other goes up silently");
        Assert.AreEqual(AreaBarButton.Spells, skills.LetGo, "the host can give it back");
        Assert.IsNull(on.LetGo);
        var off = bar.Press(AreaBarButton.Skills);
        CollectionAssert.AreEqual(new[] { 3001 }, off.Sounds.ToArray()); Assert.AreEqual(AreaBarButton.Skills, off.Act);
        Assert.IsFalse(bar.IsOn(AreaBarButton.Skills));
        bar.SetOn(AreaBarButton.Spells, true);
        Assert.IsTrue(bar.IsOn(AreaBarButton.Spells));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => bar.SetOn(AreaBarButton.Combat, true));
    }

    [TestMethod]
    public void Reset_forgets_the_pointer_and_keeps_the_toggles()
    {
        var bar = new AreaGameBar();
        bar.Press(AreaBarButton.Spells);
        bar.Enter(AreaBarButton.Character);
        bar.Press(AreaBarButton.Character);
        bar.Reset();
        Assert.IsNull(bar.Held); Assert.IsNull(bar.Hovered); Assert.IsNull(bar.HoverMessage);
        Assert.IsTrue(bar.IsOn(AreaBarButton.Spells));
        Assert.AreEqual(AreaBarButtonLook.Idle, bar.Look(AreaBarButton.Character));
        Assert.AreEqual(1004, bar.Enter(AreaBarButton.Logbook).HoverMessage, "nothing held any more");
    }

    [TestMethod]
    public void Texts_fall_back_to_the_projects()
    {
        var texts = AreaGameBarTexts.Project("test");
        Assert.IsFalse(texts.FromGame);
        Assert.AreEqual("Character Editor", texts.Text(1005));
        Assert.AreEqual("Log Book", texts.Text(1004));
        Assert.AreEqual("[1234]", texts.Text(1234));
    }

    [TestMethod]
    public void Interface_art_names_come_from_the_index_text()
    {
        var parsed = MesFile.Parse("{3}{intrface.art}//\n{184}{IntBotom.ART}----\n{185}{ }\n"u8);
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        Assert.AreEqual("intrface.art", AreaInterfaceArt.Name(parsed.File!, 3));
        Assert.AreEqual("IntBotom.ART", AreaInterfaceArt.Name(parsed.File!, 184), "text outside the braces is not part of the name");
        Assert.IsNull(AreaInterfaceArt.Name(parsed.File!, 185), "a blank name");
        Assert.IsNull(AreaInterfaceArt.Name(parsed.File!, 186), "no message");
    }

    /// <summary>The installed game's bar art and hover names read (counts and sizes only).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_bar_art_and_texts_read()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var content = AreaGameBarContent.Load(Path.Combine(root, "modules", "Arcanum.dat"));
        Console.WriteLine(content.Detail);
        Assert.IsTrue(content.ArtReady, content.Detail);
        Assert.AreEqual(AreaGameBarTexts.Count, content.Texts.GameCount, content.Texts.Detail);
        Assert.AreEqual("Character Editor", content.Texts.Text(1005));
        // ARC-UI-1111 (GB8): the clock's arts are read with the sizes the layout assumes (the day strip and the moon pieces).
        Assert.AreEqual((AreaClockLayout.StripWidth, 29), (content.Art[207].Frames[0].Width, content.Art[207].Frames[0].Height));
        for (var art = AreaClockLayout.FirstMoonArt; art < AreaClockLayout.FirstMoonArt + AreaClockLayout.MoonArtCount; art++)
            Assert.AreEqual((AreaClockLayout.MoonWidth, 29), (content.Art[art].Frames[0].Width, content.Art[art].Frames[0].Height), $"moon art {art}");
        Assert.AreEqual((5, 29), (content.Art[AreaClockLayout.NeedleArt].Frames[0].Width, content.Art[AreaClockLayout.NeedleArt].Frames[0].Height));
        Assert.AreEqual((800, 41), (content.Art[185].Frames[0].Width, content.Art[185].Frames[0].Height));
        Assert.AreEqual((800, 159), (content.Art[184].Frames[0].Width, content.Art[184].Frames[0].Height));
        foreach (var number in new[] { 169, 186, 187, 193, 470, 471, 472, 473 }) Assert.AreEqual(3, content.Art[number].Frames.Count, $"{number} has idle, down and hover frames");
        Assert.AreEqual(2, content.Art[137].Frames.Count, "the round buttons have two frames");
    }
}
