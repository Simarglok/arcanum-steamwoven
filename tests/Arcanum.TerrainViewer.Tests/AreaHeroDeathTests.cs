using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-026 the hero's death: the delayed event, the menu retry, the slides and the reset (ledger PD1–PD9).</summary>
[TestClass]
public sealed class AreaHeroDeathTests
{
    private sealed class Host : IAreaDeathHost
    {
        public bool HeroDead { get; set; }
        public bool MenuOpen { get; set; }
        public AreaSlideTable Slides { get; set; } = AreaSlideTable.From(id => id == 0 ? "death.bmp DeathSound.mp3" : null, "test");
        public bool VoicePlaying { get; set; }
        public bool PictureReads { get; set; } = true;
        public bool VoiceStarts { get; set; } = true;
        public string? Refusal { get; set; }
        public List<string> Calls { get; } = [];

        public void CloseWindows() => Calls.Add("windows");
        public void CloseMenu() { Calls.Add("menu"); MenuOpen = false; }
        public void Fade(bool toBlack, int milliseconds) => Calls.Add($"fade {(toBlack ? "out" : "in")} {milliseconds}");
        public void BeginSlides() => Calls.Add("music");
        public bool ShowSlide(AreaSlidePair slide) { Calls.Add("show " + slide.Image); return PictureReads; }
        public bool StartVoice(AreaSlidePair slide) { Calls.Add("voice " + slide.Voice); VoicePlaying = VoiceStarts; return VoiceStarts; }
        public void EndSlide() { Calls.Add("end slide"); VoicePlaying = false; }
        public void EndSlides() => Calls.Add("end slides");
        public string? Reset() { Calls.Add("reset"); return Refusal; }
    }

    private static (AreaHeroDeath Death, Host Host) Died()
    {
        var host = new Host();
        var death = new AreaHeroDeath();
        death.Tick(16, host);
        host.HeroDead = true;
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.Waiting, death.Stage);
        return (death, host);
    }

    [TestMethod]
    public void The_slide_message_names_the_picture_and_voice_by_its_first_two_words()
    {
        var table = AreaSlideTable.From(id => id switch { 0 => "death.bmp DeathSound.mp3", 1 => "\tend.bmp\n v1.mp3 extra", 2 => "only.bmp", _ => null }, "t");
        Assert.AreEqual(new AreaSlidePair(0, "slide/death.bmp", "sound/speech/slide/DeathSound.mp3"), table.Pair(0));
        Assert.AreEqual(new AreaSlidePair(1, "slide/end.bmp", "sound/speech/slide/v1.mp3"), table.Pair(1));
        Assert.IsNull(table.Pair(2), "one word is no slide");
        Assert.IsNull(table.Pair(3), "a missing message is no slide");
        Assert.IsNull(table.Pair(4));
        Assert.IsNull(AreaSlideTable.From(_ => new string('a', AreaSlideTable.MaxWord + 1) + " v.mp3", "t").Pair(0), "an overlong word");
        Assert.IsFalse(AreaSlideTable.None("no game").Loaded);
    }

    [TestMethod]
    public void A_death_waits_five_seconds_then_fades_shows_the_default_slide_and_resets()
    {
        var (death, host) = Died();
        death.Tick(AreaHeroDeath.DeathDelay - 32, host);
        Assert.AreEqual(AreaDeathStage.Waiting, death.Stage, "PD2: 5000 ms");
        Assert.IsFalse(death.Covering);
        death.Tick(32, host);
        Assert.AreEqual(AreaDeathStage.FadeOut, death.Stage);
        Assert.IsTrue(death.Covering);
        CollectionAssert.AreEqual(new[] { AreaHeroDeath.DefaultSlide }, death.Queue.ToArray(), "PD4: the default slide 0");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.SlideIn, death.Stage);
        Assert.IsFalse(death.Input(false, host), "input during the fade does nothing (PD7)");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Slide, death.Stage);
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.Slide, death.Stage, "the voice plays");
        host.VoicePlaying = false;
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.SlideOut, death.Stage, "the voice ended the slide");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage);
        death.MenuReady(host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        host.HeroDead = false;
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage);
        Assert.AreEqual(1, death.Shown);
        CollectionAssert.AreEqual(new[]
        {
            "windows", "fade out 2000", "music", "show slide/death.bmp", "fade in 2000", "voice sound/speech/slide/DeathSound.mp3",
            "end slide", "fade out 2000", "end slides", "reset", "fade in 2000",
        }, host.Calls);
    }

    [TestMethod]
    public void An_open_menu_closes_and_the_event_comes_again_after_300_ms()
    {
        var (death, host) = Died();
        host.MenuOpen = true;
        death.Tick(AreaHeroDeath.DeathDelay, host);
        Assert.AreEqual(AreaDeathStage.Waiting, death.Stage);
        Assert.AreEqual(AreaHeroDeath.MenuRetryDelay, death.Remaining);
        CollectionAssert.AreEqual(new[] { "windows", "menu" }, host.Calls, "PD3");
        death.Tick(AreaHeroDeath.MenuRetryDelay, host);
        Assert.AreEqual(AreaDeathStage.FadeOut, death.Stage);
    }

    [TestMethod]
    public void A_hero_alive_again_when_the_event_fires_ends_it()
    {
        var (death, host) = Died();
        host.HeroDead = false;
        death.Tick(AreaHeroDeath.DeathDelay, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage, "PD3: the event checks again");
        Assert.AreEqual(0, host.Calls.Count);
        host.HeroDead = true;
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.Waiting, death.Stage, "a second death starts it again");
    }

    [TestMethod]
    public void Only_a_death_starts_it_not_a_session_whose_hero_is_dead_already()
    {
        var host = new Host { HeroDead = true };
        var death = new AreaHeroDeath();
        death.Rebase(true);
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage);
        // A waiting death keeps waiting over a new session.
        var (waiting, other) = Died();
        waiting.Rebase(false);
        Assert.AreEqual(AreaDeathStage.Waiting, waiting.Stage);
        other.HeroDead = false;
        waiting.Tick(AreaHeroDeath.DeathDelay, other);
        Assert.AreEqual(AreaDeathStage.Watching, waiting.Stage);
    }

    [TestMethod]
    public void A_key_ends_the_slide_and_escape_ends_the_show()
    {
        var host = new Host { Slides = AreaSlideTable.From(id => $"s{id}.bmp v{id}.mp3", "t") };
        var death = new AreaHeroDeath();
        death.Tick(0, host); host.HeroDead = true; death.Tick(0, host);
        death.Tick(AreaHeroDeath.DeathDelay, host);
        death.Tick(AreaHeroDeath.FadeTime, host);
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Slide, death.Stage);
        Assert.IsTrue(death.Input(false, host), "a key ends the slide");
        Assert.AreEqual(AreaDeathStage.SlideOut, death.Stage);
        Assert.IsFalse(host.VoicePlaying, "its voice stopped");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage, "only the default slide is queued");
        // Escape on a slide ends the show at once after its fade out.
        var escape = new AreaHeroDeath();
        var second = new Host();
        escape.Tick(0, second); second.HeroDead = true; escape.Tick(0, second);
        escape.Tick(AreaHeroDeath.DeathDelay, second); escape.Tick(AreaHeroDeath.FadeTime, second); escape.Tick(AreaHeroDeath.FadeTime, second);
        Assert.IsTrue(escape.Input(true, second));
        escape.Tick(AreaHeroDeath.FadeTime, second);
        Assert.AreEqual(AreaDeathStage.Reset, escape.Stage);
        Assert.IsFalse(escape.Input(false, second), "no slide any more");
    }

    [TestMethod]
    public void A_slide_without_a_voice_ends_at_once_and_one_without_a_picture_shows_nothing()
    {
        var (death, host) = Died();
        host.VoiceStarts = false;
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host); death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.SlideOut, death.Stage, "PD7: no voice, no wait");
        var (unread, other) = Died();
        other.PictureReads = false;
        unread.Tick(AreaHeroDeath.DeathDelay, other); unread.Tick(AreaHeroDeath.FadeTime, other);
        Assert.AreEqual(AreaDeathStage.SlideOut, unread.Stage);
        Assert.AreEqual(0, unread.Shown);
        Assert.IsFalse(other.Calls.Any(call => call.StartsWith("voice", StringComparison.Ordinal) || call == "fade in 2000"), "no fade in, no voice");
    }

    [TestMethod]
    public void Without_the_table_or_with_a_missing_pair_it_fades_and_resets()
    {
        var (death, host) = Died();
        host.Slides = AreaSlideTable.None("no game folder");
        death.Tick(AreaHeroDeath.DeathDelay, host);
        Assert.AreEqual(0, death.Queue.Count, "PD4: no table, no default slide");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage);
        CollectionAssert.AreEqual(new[] { "windows", "fade out 2000", "reset" }, host.Calls, "no music without slides");
        var (skipped, other) = Died();
        other.Slides = AreaSlideTable.From(_ => "picture-only.bmp", "t");
        skipped.Tick(AreaHeroDeath.DeathDelay, other); skipped.Tick(AreaHeroDeath.FadeTime, other);
        Assert.AreEqual(AreaDeathStage.Reset, skipped.Stage, "PD5: a slide without a pair is skipped");
        CollectionAssert.AreEqual(new[] { "windows", "fade out 2000", "music", "end slides", "reset" }, other.Calls);
    }

    [TestMethod]
    public void A_refused_reset_is_tried_again_and_the_menu_fades_in_once_it_began()
    {
        var (death, host) = Died();
        host.Slides = AreaSlideTable.None("t");
        host.Refusal = "another map is loading";
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage);
        Assert.AreEqual("another map is loading", death.ResetRefusal);
        death.MenuReady(host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage, "no menu while the reset was refused");
        host.Refusal = null;
        death.Tick(AreaHeroDeath.MenuRetryDelay, host);
        Assert.IsNull(death.ResetRefusal);
        Assert.AreEqual(2, host.Calls.Count(call => call == "reset"));
        death.Tick(10_000, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage, "the screen stays black until the menu is there");
        death.MenuReady(host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        Assert.Throws<ArgumentOutOfRangeException>(() => death.Tick(-1, host));
    }

    [TestMethod]
    public void A_menu_that_opens_inside_the_reset_fades_in()
    {
        var (death, host) = Died();
        host.Slides = AreaSlideTable.None("t");
        host.Refusal = "another map is loading";
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host);
        // The second try opens the menu synchronously (the self-test's scene load does).
        var synchronous = new SynchronousHost(host, death);
        death.Tick(AreaHeroDeath.MenuRetryDelay, synchronous);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        Assert.IsNull(death.ResetRefusal);
    }

    private sealed class SynchronousHost(Host inner, AreaHeroDeath death) : IAreaDeathHost
    {
        public bool HeroDead => inner.HeroDead;
        public bool MenuOpen => inner.MenuOpen;
        public AreaSlideTable Slides => inner.Slides;
        public bool VoicePlaying => inner.VoicePlaying;
        public void CloseWindows() => inner.CloseWindows();
        public void CloseMenu() => inner.CloseMenu();
        public void Fade(bool toBlack, int milliseconds) => inner.Fade(toBlack, milliseconds);
        public void BeginSlides() => inner.BeginSlides();
        public bool ShowSlide(AreaSlidePair slide) => inner.ShowSlide(slide);
        public bool StartVoice(AreaSlidePair slide) => inner.StartVoice(slide);
        public void EndSlide() => inner.EndSlide();
        public void EndSlides() => inner.EndSlides();
        public string? Reset() { death.MenuReady(this); return null; }
    }
}
