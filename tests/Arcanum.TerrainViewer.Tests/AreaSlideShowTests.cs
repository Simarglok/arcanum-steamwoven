using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-1171 (ledger SS1–SS6): the death's show loop serves the end-game and the credits shows too; the show type picks the script that queues the slides.</summary>
[TestClass]
public sealed class AreaSlideShowTests
{
    private sealed class Host : IAreaDeathHost
    {
        public bool HeroDead { get; set; }
        public bool MenuOpen { get; set; }
        public AreaSlideTable Slides { get; set; } = AreaSlideTable.From(id => id < 60 ? $"s{id}.bmp v{id}.mp3" : null, "test");
        public Dictionary<AreaSlideKind, int[]> Queued { get; } = [];
        public bool VoicePlaying { get; set; }
        public List<string> Calls { get; } = [];

        public IReadOnlyList<int> SlideQueue(AreaSlideKind kind) { Calls.Add("script " + kind); return Queued.GetValueOrDefault(kind) ?? []; }
        public void CloseWindows() => Calls.Add("windows");
        public void CloseMenu() { Calls.Add("menu"); MenuOpen = false; }
        public void Fade(bool toBlack, int milliseconds) => Calls.Add($"fade {(toBlack ? "out" : "in")} {milliseconds}");
        public void BeginSlides() => Calls.Add("music");
        public bool ShowSlide(AreaSlidePair slide) { Calls.Add("show " + slide.Id); return true; }
        public bool StartVoice(AreaSlidePair slide) { Calls.Add("voice " + slide.Id); VoicePlaying = true; return true; }
        public void EndSlide() { Calls.Add("end slide"); VoicePlaying = false; }
        public void EndSlides() => Calls.Add("end slides");
        public string? Reset(out bool lasting) { Calls.Add("reset"); lasting = false; return null; }
        public LifecycleRuleProfile Rules { get; set; } = LifecycleRuleProfile.Steamwoven;
        public string? HeroSave { get; set; }
        public void Ask(string save, string? notice) => Calls.Add("ask " + save);
        public void EndAsk() => Calls.Add("end ask");
        public string? LoadLastSave(string save) { Calls.Add("load " + save); return null; }
        public bool LoadSettled => true;
    }

    private static void ShowAll(AreaHeroDeath show, Host host)
    {
        // Fade out, then each slide: fade in, voice until it ends, fade out.
        show.Tick(AreaHeroDeath.FadeTime, host);
        while (show.Stage is AreaDeathStage.SlideIn or AreaDeathStage.Slide or AreaDeathStage.SlideOut)
        {
            switch (show.Stage)
            {
                case AreaDeathStage.SlideIn or AreaDeathStage.SlideOut: show.Tick(AreaHeroDeath.FadeTime, host); break;
                default: host.VoicePlaying = false; show.Tick(16, host); break;
            }
        }
    }

    [TestMethod]
    public void The_death_takes_the_slides_script_998_queued_and_the_default_only_when_it_queued_none()
    {
        var host = new Host { HeroDead = true };
        host.Queued[AreaSlideKind.Death] = [2];
        var show = new AreaHeroDeath();
        show.Rebase(false);
        show.Tick(16, host);
        show.Tick(AreaHeroDeath.DeathDelay, host);
        CollectionAssert.AreEqual(new[] { 2 }, show.Queue.ToArray(), "SS1: the script's slide, not the default 0");
        Assert.AreEqual(AreaSlideKind.Death, show.Kind);

        var plain = new Host { HeroDead = true };
        var other = new AreaHeroDeath();
        other.Rebase(false);
        other.Tick(16, plain);
        other.Tick(AreaHeroDeath.DeathDelay, plain);
        CollectionAssert.AreEqual(new[] { AreaHeroDeath.DefaultSlide }, other.Queue.ToArray(), "PD4: nothing queued, the default slide 0");
    }

    [TestMethod]
    public void The_end_game_comes_50_ms_after_its_request_runs_script_997_and_resets_with_no_default_slide_and_no_box()
    {
        var host = new Host { HeroSave = "hero save" };
        host.Queued[AreaSlideKind.EndGame] = [1, 3];
        var show = new AreaHeroDeath();
        Assert.IsTrue(show.RequestEndGame());
        Assert.AreEqual((AreaDeathStage.Waiting, AreaSlideKind.EndGame), (show.Stage, show.Kind));
        Assert.IsFalse(show.RequestEndGame(), "one show at a time");
        show.Tick(AreaHeroDeath.EndGameDelay - 1, host);
        Assert.AreEqual(AreaDeathStage.Waiting, show.Stage);
        show.Tick(1, host);
        Assert.AreEqual(AreaDeathStage.FadeOut, show.Stage, "the hero is alive: the end-game event does not check him");
        CollectionAssert.AreEqual(new[] { 1, 3 }, show.Queue.ToArray());
        CollectionAssert.Contains(host.Calls, "windows");
        ShowAll(show, host);
        Assert.AreEqual(AreaDeathStage.Reset, show.Stage, "the game resets as after a death");
        Assert.IsFalse(host.Calls.Any(call => call.StartsWith("ask", StringComparison.Ordinal)), "the Steamwoven load question is the death's alone");
        Assert.AreEqual(2, show.Shown);
        Assert.AreEqual(1, host.Calls.Count(call => call == "reset"));
    }

    [TestMethod]
    public void An_end_game_with_no_slides_still_fades_and_resets_and_an_open_menu_delays_it_300_ms()
    {
        var host = new Host { MenuOpen = true };
        var show = new AreaHeroDeath();
        show.RequestEndGame();
        show.Tick(AreaHeroDeath.EndGameDelay, host);
        Assert.AreEqual(AreaDeathStage.Waiting, show.Stage);
        Assert.AreEqual(AreaHeroDeath.MenuRetryDelay, show.Remaining, "the menu closed and the event comes again (0x57D410 case 11)");
        show.Tick(AreaHeroDeath.MenuRetryDelay, host);
        Assert.AreEqual(AreaDeathStage.FadeOut, show.Stage);
        Assert.AreEqual(0, show.Queue.Count, "no default slide for the end game");
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Reset, show.Stage);
    }

    [TestMethod]
    public void The_credits_show_script_999_slides_then_fade_in_over_the_menu_and_reset_nothing()
    {
        var host = new Host();
        host.Queued[AreaSlideKind.Credits] = [45, 46, 47];
        var show = new AreaHeroDeath();
        Assert.IsTrue(show.StartCredits(host));
        Assert.IsTrue(show.Covering);
        Assert.AreEqual(AreaSlideKind.Credits, show.Kind);
        Assert.IsFalse(show.StartCredits(host), "a show under way refuses a second");
        ShowAll(show, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, show.Stage);
        Assert.AreEqual(3, show.Shown);
        Assert.AreEqual("fade in 2000", host.Calls[^1], "0x569A60 fades the screen back in");
        Assert.IsFalse(host.Calls.Contains("reset"));
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Watching, show.Stage);
        Assert.IsFalse(show.Covering);
    }

    [TestMethod]
    public void Escape_ends_the_whole_credits_and_a_key_only_the_slide()
    {
        var host = new Host();
        host.Queued[AreaSlideKind.Credits] = [45, 46, 47];
        var show = new AreaHeroDeath();
        show.StartCredits(host);
        show.Tick(AreaHeroDeath.FadeTime, host);
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.Slide, show.Stage);
        Assert.IsTrue(show.Input(false, host), "PD7: a key's release ends the slide");
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.SlideIn, show.Stage, "the next slide follows");
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.IsTrue(show.Input(true, host), "Escape ends the show");
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, show.Stage);
        Assert.AreEqual(2, show.Shown);
    }

    [TestMethod]
    public void The_credits_with_nothing_queued_and_a_show_under_way_are_handled()
    {
        var host = new Host();
        var show = new AreaHeroDeath();
        Assert.IsTrue(show.StartCredits(host));
        show.Tick(AreaHeroDeath.FadeTime, host);
        Assert.AreEqual(AreaDeathStage.MenuFadeIn, show.Stage, "no slide queued: the screen fades in again at once");
        show.Tick(AreaHeroDeath.FadeTime, host);
        host.HeroDead = true;
        Assert.IsTrue(show.RequestEndGame());
        Assert.IsFalse(show.StartCredits(host), "a waiting end game refuses the credits");
    }

    [TestMethod]
    public void A_queue_beyond_one_hundred_slides_is_cut_there()
    {
        var host = new Host();
        host.Queued[AreaSlideKind.Credits] = [.. Enumerable.Range(0, 150)];
        var show = new AreaHeroDeath();
        show.StartCredits(host);
        Assert.AreEqual(AreaSlideScripts.MaxQueued, show.Queue.Count);
    }
}
