using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-026 the hero's death: the delayed event, the menu retry, the slides and the reset (ledger PD1–PD9); ARC-UI-028
/// the Steamwoven box that offers the newest save before the reset.</summary>
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
        public bool Lasting { get; set; }
        public string? Reset(out bool lasting) { Calls.Add("reset"); lasting = Lasting; return Refusal; }
        public LifecycleRuleProfile Rules { get; set; } = LifecycleRuleProfile.Steamwoven;
        public string? HeroSave { get; set; }
        public string? LoadRefusal { get; set; }
        public bool LoadSettled { get; set; } = true;
        public void Ask(string save, string? notice) => Calls.Add($"ask {save}" + (notice is null ? "" : " " + notice));
        public void EndAsk() => Calls.Add("end ask");
        public string? LoadLastSave(string save) { Calls.Add("load " + save); return LoadRefusal; }
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

    /// <summary>A death whose show ended (no slide table: straight to the point of the reset).</summary>
    private static (AreaHeroDeath Death, Host Host) ShowEnded(string? save, LifecycleRuleProfile rules)
    {
        var (death, host) = Died();
        host.Slides = AreaSlideTable.None("t");
        host.HeroSave = save; host.Rules = rules;
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host);
        return (death, host);
    }

    [TestMethod]
    public void Steamwoven_asks_to_load_the_newest_save_and_yes_loads_it_and_fades_in()
    {
        var (death, host) = ShowEnded("slot0003", LifecycleRuleProfile.Steamwoven);
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage, "the box asks instead of the reset");
        Assert.IsTrue(death.Covering);
        Assert.AreEqual("slot0003", death.OfferedSave);
        Assert.IsFalse(death.Input(false, host), "a key is no slide input here");
        death.Tick(60_000, host);
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage, "the box waits for its answer");
        host.LoadSettled = false;
        Assert.IsTrue(death.Answer(true, host));
        Assert.AreEqual(AreaDeathStage.Loading, death.Stage);
        Assert.IsTrue(death.Loaded);
        Assert.IsFalse(death.Answer(true, host), "answered once");
        death.Tick(1000, host);
        Assert.AreEqual(AreaDeathStage.Loading, death.Stage, "black until the loaded game's map is there");
        host.LoadSettled = true; host.HeroDead = false;
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage);
        CollectionAssert.AreEqual(new[] { "windows", "fade out 2000", "ask slot0003", "load slot0003", "end ask", "fade in 2000" }, host.Calls, "no reset");
        // A load that never settles gives up as a reset does.
        var (stuck, other) = ShowEnded("slot0001", LifecycleRuleProfile.Steamwoven);
        other.LoadSettled = false;
        stuck.Answer(true, other);
        stuck.Tick(AreaHeroDeath.ResetTimeout, other);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, stuck.Stage);
        Assert.IsTrue(stuck.LoadAbandoned);
        Assert.IsFalse(death.LoadAbandoned);
    }

    [TestMethod]
    public void A_load_that_brings_no_living_hero_asks_again()
    {
        // The other map failed and the old game came back, or the save's hero is dead: the hero is still dead when it settles.
        var (death, host) = ShowEnded("slot0004", LifecycleRuleProfile.Steamwoven);
        host.LoadSettled = false;
        death.Answer(true, host);
        host.LoadSettled = true;
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage);
        Assert.AreEqual(AreaHeroDeath.NoLivingHero, death.LoadRefusal);
        Assert.IsFalse(death.Loaded);
        Assert.AreEqual("ask slot0004 " + AreaHeroDeath.NoLivingHero, host.Calls[^1]);
        Assert.IsTrue(death.Answer(false, host));
        Assert.IsNull(death.LoadRefusal, "No clears the refusal");
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage);
    }

    [TestMethod]
    public void No_resets_as_the_original_and_a_refused_load_keeps_the_box()
    {
        var (death, host) = ShowEnded("quicksave", LifecycleRuleProfile.Steamwoven);
        host.LoadRefusal = "Death: last save quick load refused: another map is loading";
        Assert.IsTrue(death.Answer(true, host));
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage, "the box stays");
        Assert.AreEqual(host.LoadRefusal, death.LoadRefusal);
        Assert.IsFalse(death.Loaded);
        Assert.IsTrue(death.Answer(false, host));
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage, "No: the original's reset");
        Assert.IsNull(death.OfferedSave);
        death.MenuReady(host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        CollectionAssert.AreEqual(new[]
        {
            "windows", "fade out 2000", "ask quicksave", "load quicksave", "ask quicksave " + host.LoadRefusal, "end ask", "reset", "fade in 2000",
        }, host.Calls);
    }

    [TestMethod]
    public void Original_or_no_save_resets_at_once()
    {
        var (original, host) = ShowEnded("slot0000", LifecycleRuleProfile.Original);
        Assert.AreEqual(AreaDeathStage.Reset, original.Stage, "PD8: Original never asks");
        Assert.IsFalse(original.Answer(true, host));
        CollectionAssert.AreEqual(new[] { "windows", "fade out 2000", "reset" }, host.Calls);
        var (none, other) = ShowEnded(null, LifecycleRuleProfile.Steamwoven);
        Assert.AreEqual(AreaDeathStage.Reset, none.Stage, "nothing to load: the reset");
        CollectionAssert.AreEqual(new[] { "windows", "fade out 2000", "reset" }, other.Calls);
        // Without a save of this hero the slide plays as the original's and the game resets.
        var (shown, slides) = Died();
        shown.Tick(AreaHeroDeath.DeathDelay, slides); shown.Tick(AreaHeroDeath.FadeTime, slides); shown.Tick(AreaHeroDeath.FadeTime, slides);
        Assert.AreEqual(AreaDeathStage.Slide, shown.Stage);
        Assert.IsTrue(shown.Input(true, slides));
        shown.Tick(AreaHeroDeath.FadeTime, slides);
        Assert.AreEqual(AreaDeathStage.Reset, shown.Stage);
        Assert.IsFalse(slides.Calls.Any(call => call.StartsWith("ask", StringComparison.Ordinal)));
    }

    /// <summary>A death of a hero with a save whose slide has just faded in (the box asks over it).</summary>
    private static (AreaHeroDeath Death, Host Host) AskedOverTheSlide(string save)
    {
        var (death, host) = Died();
        host.HeroSave = save;
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host); death.Tick(AreaHeroDeath.FadeTime, host);
        return (death, host);
    }

    [TestMethod]
    public void The_box_asks_over_the_faded_in_slide_and_yes_loads_while_the_slide_fades_out()
    {
        var (death, host) = AskedOverTheSlide("slot0007");
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage, "the box asks as soon as the slide has faded in");
        Assert.AreEqual("slot0007", death.OfferedSave);
        Assert.IsNotNull(death.Slide);
        CollectionAssert.AreEqual(new[] { "show slide/death.bmp", "fade in 2000", "voice sound/speech/slide/DeathSound.mp3", "ask slot0007" },
            host.Calls.TakeLast(4).ToArray(), "the voice plays under the box");
        // Neither the voice's end nor a key ends the slide while the box asks.
        host.VoicePlaying = false;
        death.Tick(10_000, host);
        Assert.IsFalse(death.Input(false, host));
        Assert.AreEqual(AreaDeathStage.Asking, death.Stage);
        host.LoadSettled = false;
        Assert.IsTrue(death.Answer(true, host));
        Assert.AreEqual(AreaDeathStage.Loading, death.Stage);
        CollectionAssert.AreEqual(new[] { "load slot0007", "end ask", "end slide", "fade out 2000" }, host.Calls.TakeLast(4).ToArray(),
            "the save loads while the slide fades to black");
        host.LoadSettled = true; host.HeroDead = false;
        death.Tick(AreaHeroDeath.FadeTime - 16, host);
        Assert.AreEqual(AreaDeathStage.Loading, death.Stage, "the fade to black ends first");
        death.Tick(16, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage);
        CollectionAssert.AreEqual(new[] { "end slides", "fade in 2000" }, host.Calls.TakeLast(2).ToArray(), "the slide's music stops, the loaded game fades in");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage);
        Assert.IsFalse(host.Calls.Contains("reset"));
    }

    [TestMethod]
    public void No_over_the_slide_fades_it_out_and_resets_without_asking_again()
    {
        var (death, host) = AskedOverTheSlide("quicksave");
        Assert.IsTrue(death.Answer(false, host));
        Assert.AreEqual(AreaDeathStage.SlideOut, death.Stage, "the slide fades out as the original's");
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, death.Stage, "then the reset, no second box");
        CollectionAssert.AreEqual(new[] { "end ask", "end slide", "fade out 2000", "end slides", "reset" }, host.Calls.TakeLast(5).ToArray());
        Assert.AreEqual(1, host.Calls.Count(call => call.StartsWith("ask", StringComparison.Ordinal)));
        // A load that brought no living hero asks again over the black screen; No then fades the slide out and resets.
        var (again, other) = AskedOverTheSlide("slot0001");
        again.Answer(true, other);
        again.Tick(AreaHeroDeath.FadeTime, other);
        Assert.AreEqual(AreaDeathStage.Asking, again.Stage);
        Assert.AreEqual("ask slot0001 " + AreaHeroDeath.NoLivingHero, other.Calls[^1]);
        Assert.IsTrue(again.Answer(false, other));
        again.Tick(AreaHeroDeath.FadeTime, other);
        Assert.AreEqual(AreaDeathStage.Reset, again.Stage);
        Assert.AreEqual(2, other.Calls.Count(call => call.StartsWith("ask", StringComparison.Ordinal)));
        // Original never asks over the slide either.
        var (original, plain) = Died();
        plain.HeroSave = "slot0002"; plain.Rules = LifecycleRuleProfile.Original;
        original.Tick(AreaHeroDeath.DeathDelay, plain); original.Tick(AreaHeroDeath.FadeTime, plain); original.Tick(AreaHeroDeath.FadeTime, plain);
        Assert.AreEqual(AreaDeathStage.Slide, original.Stage);
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
    public void A_lasting_refusal_or_two_minutes_without_a_menu_gives_up_and_fades_in()
    {
        var (death, host) = Died();
        host.Slides = AreaSlideTable.None("t");
        host.Refusal = "the launch arguments are not known"; host.Lasting = true;
        death.Tick(AreaHeroDeath.DeathDelay, host); death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, death.Stage, "a lasting refusal gives up at once");
        Assert.IsTrue(death.ResetAbandoned);
        Assert.AreEqual("the launch arguments are not known", death.ResetRefusal);
        Assert.AreEqual(1, host.Calls.Count(call => call == "reset"));
        death.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Watching, death.Stage, "the dead hero stays: a death starts it again, not this one");
        // A passing refusal (a map loading) is tried again until two minutes have gone.
        var (busy, loading) = Died();
        loading.Slides = AreaSlideTable.None("t");
        loading.Refusal = "another map is loading";
        busy.Tick(AreaHeroDeath.DeathDelay, loading); busy.Tick(AreaHeroDeath.FadeTime, loading);
        for (var i = 0; i < 100; i++) busy.Tick(AreaHeroDeath.MenuRetryDelay, loading);
        Assert.AreEqual(AreaDeathStage.Reset, busy.Stage, "30 s of a loading map is waited out");
        busy.Tick(AreaHeroDeath.ResetTimeout, loading);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, busy.Stage);
        Assert.IsTrue(busy.ResetAbandoned);
        // A reset that began but whose menu never comes gives up too.
        var (waiting, other) = Died();
        other.Slides = AreaSlideTable.None("t");
        waiting.Tick(AreaHeroDeath.DeathDelay, other); waiting.Tick(AreaHeroDeath.FadeTime, other);
        waiting.Tick(AreaHeroDeath.ResetTimeout - 1, other);
        Assert.AreEqual(AreaDeathStage.Reset, waiting.Stage);
        waiting.Tick(1, other);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, waiting.Stage);
        Assert.IsTrue(waiting.ResetAbandoned);
        waiting.Stop(true);
        Assert.AreEqual(AreaDeathStage.Watching, waiting.Stage);
        Assert.IsFalse(waiting.Covering);
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
        public string? Reset(out bool lasting) { lasting = false; death.MenuReady(this); return null; }
        public LifecycleRuleProfile Rules => inner.Rules;
        public string? HeroSave => inner.HeroSave;
        public bool LoadSettled => inner.LoadSettled;
        public void Ask(string save, string? notice) => inner.Ask(save, notice);
        public void EndAsk() => inner.EndAsk();
        public string? LoadLastSave(string save) => inner.LoadLastSave(save);
    }
}
