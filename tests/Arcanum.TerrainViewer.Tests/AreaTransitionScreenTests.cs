using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-204 (B16): the black screen of a change of place: the 2 s fades (PD9), the blank flag, the load screen with the clock.</summary>
[TestClass]
public sealed class AreaTransitionScreenTests
{
    [TestMethod]
    public void A_clear_screen_covers_nothing()
    {
        var screen = new AreaTransitionScreen();
        Assert.AreEqual(AreaTransitionShade.Clear, screen.Shade);
        Assert.IsFalse(screen.Covering);
        Assert.AreEqual(0, screen.Alpha);
        Assert.IsFalse(screen.IndicatorShown);
    }

    [TestMethod]
    public void The_fade_out_takes_two_seconds_to_black()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginFadeOut();
        Assert.IsTrue(screen.FadingOut);
        Assert.AreEqual(0, screen.Alpha);
        screen.Advance(1000);
        Assert.AreEqual(.5, screen.Alpha, 1e-9);
        Assert.IsFalse(screen.IsBlack, "half-way is not black");
        screen.Advance(999);
        Assert.IsFalse(screen.IsBlack);
        screen.Advance(1);
        Assert.IsTrue(screen.IsBlack);
        Assert.AreEqual(1, screen.Alpha);
    }

    [TestMethod]
    public void The_fade_in_takes_two_seconds_from_black_to_clear()
    {
        var screen = new AreaTransitionScreen();
        screen.Blank();
        screen.BeginFadeIn();
        Assert.AreEqual(AreaTransitionShade.FadingIn, screen.Shade);
        Assert.AreEqual(1, screen.Alpha);
        screen.Advance(500);
        Assert.AreEqual(.75, screen.Alpha, 1e-9);
        screen.Advance(1500);
        Assert.AreEqual(AreaTransitionShade.Clear, screen.Shade);
        Assert.IsFalse(screen.Covering);
    }

    [TestMethod]
    public void A_fade_in_from_a_clear_screen_has_nothing_to_fade()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginFadeIn();
        Assert.AreEqual(AreaTransitionShade.Clear, screen.Shade);
    }

    [TestMethod]
    public void The_blank_flag_blacks_the_screen_at_once_and_the_ping_gives_the_draw_back()
    {
        var screen = new AreaTransitionScreen();
        screen.Blank();
        Assert.IsTrue(screen.IsBlack);
        Assert.AreEqual(1, screen.Alpha);
        Assert.IsFalse(screen.IndicatorShown, "a blanked screen shows no clock by itself");
        screen.Release();
        Assert.IsFalse(screen.Covering);
    }

    [TestMethod]
    public void A_release_leaves_a_fade_in_running()
    {
        var screen = new AreaTransitionScreen();
        screen.Blank();
        screen.BeginFadeIn();
        screen.Release();
        Assert.AreEqual(AreaTransitionShade.FadingIn, screen.Shade);
    }

    [TestMethod]
    public void A_map_load_shows_black_with_the_clock_until_the_map_is_there()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginLoad();
        Assert.IsTrue(screen.Covering);
        Assert.IsTrue(screen.IndicatorShown);
        Assert.AreEqual(1, screen.Alpha);
        screen.EndLoad();
        Assert.IsFalse(screen.IndicatorShown);
        Assert.IsTrue(screen.IsBlack, "the black stays until the draw is given back");
        screen.Release();
        Assert.IsFalse(screen.Covering);
    }

    [TestMethod]
    public void The_clock_runs_only_while_a_map_loads_and_loops_over_its_frames()
    {
        var screen = new AreaTransitionScreen();
        screen.Advance(5000);
        Assert.AreEqual(0, screen.IndicatorMilliseconds, "no clock runs on a clear screen");
        screen.BeginLoad();
        Assert.AreEqual(0, screen.IndicatorFrame(30, 10));
        screen.Advance(250);
        Assert.AreEqual(2, screen.IndicatorFrame(30, 10));
        screen.Advance(2750);
        Assert.AreEqual(0, screen.IndicatorFrame(30, 10), "30 frames at 10/s loop after 3 s");
        Assert.AreEqual(0, screen.IndicatorFrame(0, 10), "no frames: frame 0");
        Assert.AreEqual(screen.IndicatorFrame(30, AreaTransitionScreen.DefaultFramesPerSecond), screen.IndicatorFrame(30, 0), "a missing rate uses the default");
    }

    [TestMethod]
    public void A_new_load_starts_the_clock_again()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginLoad();
        screen.Advance(400);
        screen.EndLoad();
        screen.BeginLoad();
        Assert.AreEqual(0, screen.IndicatorMilliseconds);
    }

    [TestMethod]
    public void Clear_drops_everything_at_once()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginLoad();
        screen.Advance(300);
        screen.Clear();
        Assert.IsFalse(screen.Covering);
        Assert.AreEqual(0, screen.Alpha);
        Assert.AreEqual(0, screen.IndicatorMilliseconds);
    }

    [TestMethod]
    public void A_fade_out_from_a_fade_in_goes_on_from_its_own_darkness()
    {
        var screen = new AreaTransitionScreen();
        screen.Blank();
        screen.BeginFadeIn();
        screen.Advance(1000);
        Assert.AreEqual(.5, screen.Alpha, 1e-9);
        screen.BeginFadeOut();
        Assert.AreEqual(.5, screen.Alpha, 1e-9, "no jump to clear");
        screen.Advance(1000);
        Assert.IsTrue(screen.IsBlack);
    }

    [TestMethod]
    public void A_bad_time_step_moves_nothing()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginFadeOut();
        screen.Advance(double.NaN);
        screen.Advance(-5);
        screen.Advance(double.PositiveInfinity);
        Assert.AreEqual(0, screen.Alpha);
    }

    [TestMethod]
    public void The_fade_length_is_the_death_slides_fade()
    {
        var screen = new AreaTransitionScreen();
        screen.BeginFadeOut();
        screen.Advance(AreaHeroDeath.FadeTime - 1);
        Assert.IsFalse(screen.IsBlack);
        screen.Advance(1);
        Assert.IsTrue(screen.IsBlack, "the veil fades for the length of the death slides' fade, 2 s (PD9)");
    }
}
