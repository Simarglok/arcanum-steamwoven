using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-WORLD-1160 (E3-a): the sleep window's rules (ledger SWB1–SWB13, TL27): the open decision, the button table, the step's end tests, the chain of steps, the fade and the texts. Synthetic values only.</summary>
[TestClass]
public sealed class AreaRestTests
{
    private static AreaRestFacts Facts(bool dead = false, bool open = false, bool unconscious = false, AreaRestTownMap town = AreaRestTownMap.None, bool combat = false, bool mayLeave = true) =>
        new(dead, open, unconscious, town, combat, mayLeave);

    [TestMethod]
    public void A_free_hero_opens_the_sleep_window_with_eight_buttons()
    {
        var opening = AreaRestRules.Decide(Facts());
        Assert.AreEqual(new AreaRestOpening(AreaRestOpenOutcome.Open, false, null, false), opening);
        Assert.AreEqual(8, AreaRestRules.ButtonCount(opening.Wait));
    }

    [TestMethod]
    public void A_dead_hero_does_nothing_even_with_the_window_open_and_an_unconscious_hero_gets_its_message()
    {
        Assert.AreEqual(AreaRestOpenOutcome.Nothing, AreaRestRules.Decide(Facts(dead: true, open: true, unconscious: true)).Outcome);
        var refused = AreaRestRules.Decide(Facts(unconscious: true, town: AreaRestTownMap.WaitRefused, combat: true, mayLeave: false));
        Assert.AreEqual(new AreaRestOpening(AreaRestOpenOutcome.Refused, false, AreaRestMessages.HeroUnconscious, false), refused);
    }

    [TestMethod]
    public void An_open_window_is_closed_before_anything_else_is_asked()
    {
        Assert.AreEqual(AreaRestOpenOutcome.Close, AreaRestRules.Decide(Facts(open: true, unconscious: true, town: AreaRestTownMap.WaitRefused, combat: true, mayLeave: false)).Outcome);
    }

    [TestMethod]
    public void A_town_map_sector_needs_the_wait_marker_and_then_only_waits()
    {
        var waits = AreaRestRules.Decide(Facts(town: AreaRestTownMap.WaitAllowed));
        Assert.AreEqual(new AreaRestOpening(AreaRestOpenOutcome.Open, true, null, false), waits);
        Assert.AreEqual(7, AreaRestRules.ButtonCount(waits.Wait));
        var refused = AreaRestRules.Decide(Facts(town: AreaRestTownMap.WaitRefused));
        Assert.AreEqual((AreaRestOpenOutcome.Refused, AreaRestMessages.WaitRefused), (refused.Outcome, refused.Message!.Value));
    }

    [TestMethod]
    public void Combat_mode_asks_the_exit_gate_and_leaves_at_once_when_it_allows()
    {
        var refused = AreaRestRules.Decide(Facts(combat: true, mayLeave: false));
        Assert.AreEqual((AreaRestOpenOutcome.Refused, AreaRestMessages.CombatRefused), (refused.Outcome, refused.Message!.Value));
        Assert.IsTrue(AreaRestRules.Decide(Facts(combat: true, mayLeave: true)).LeavesCombatMode);
        // A hero not in combat mode is not asked at all (enemies near do not stop the window), and a town refusal comes before the combat test.
        Assert.AreEqual(AreaRestOpenOutcome.Open, AreaRestRules.Decide(Facts(combat: false, mayLeave: false)).Outcome);
        Assert.AreEqual(AreaRestMessages.WaitRefused, AreaRestRules.Decide(Facts(town: AreaRestTownMap.WaitRefused, combat: true, mayLeave: false)).Message);
    }

    [TestMethod]
    public void The_buttons_are_the_original_table_and_the_wait_window_drops_until_healed()
    {
        int?[] sleep = [.. Enumerable.Range(0, 9).Select(i => AreaRestRules.CodeOf(i, wait: false))];
        CollectionAssert.AreEqual(new int?[] { 1, 2, 4, 8, 24, -1, -2, -3, null }, sleep);
        int?[] wait = [.. Enumerable.Range(0, 8).Select(i => AreaRestRules.CodeOf(i, wait: true))];
        CollectionAssert.AreEqual(new int?[] { 1, 2, 4, 8, 24, -1, -2, null }, wait);
        Assert.IsNull(AreaRestRules.CodeOf(-1, false));
        Assert.AreEqual(3, AreaRestRules.LabelMessage(3, wait: false));
        Assert.AreEqual(12, AreaRestRules.LabelMessage(0, wait: true));
        Assert.AreEqual(18, AreaRestRules.LabelMessage(6, wait: true));
    }

    [TestMethod]
    public void A_step_ends_by_the_length_the_hour_or_the_damage_left_and_a_death_ends_every_rest()
    {
        Assert.AreEqual(AreaRestEnd.Continue, AreaRestRules.AfterStep(2, 5, false, false));
        Assert.AreEqual(AreaRestEnd.Done, AreaRestRules.AfterStep(1, 5, false, true));
        Assert.AreEqual(AreaRestEnd.Continue, AreaRestRules.AfterStep(AreaRestRules.UntilMorning, 6, false, false));
        Assert.AreEqual(AreaRestEnd.Done, AreaRestRules.AfterStep(AreaRestRules.UntilMorning, 7, false, false));
        Assert.AreEqual(AreaRestEnd.Continue, AreaRestRules.AfterStep(AreaRestRules.UntilMorning, 8, false, false), "an hour past 7 goes on until the next 7");
        Assert.AreEqual(AreaRestEnd.Done, AreaRestRules.AfterStep(AreaRestRules.UntilEvening, 20, false, false));
        Assert.AreEqual(AreaRestEnd.Continue, AreaRestRules.AfterStep(AreaRestRules.UntilEvening, 19, false, false));
        Assert.AreEqual(AreaRestEnd.Continue, AreaRestRules.AfterStep(AreaRestRules.UntilHealed, 3, false, true));
        Assert.AreEqual(AreaRestEnd.Done, AreaRestRules.AfterStep(AreaRestRules.UntilHealed, 3, false, false));
        foreach (var code in new[] { 1, 24, -1, -2, -3 })
            Assert.AreEqual(AreaRestEnd.Died, AreaRestRules.AfterStep(code, 7, true, true), $"code {code}");
    }

    [TestMethod]
    public void A_press_adds_a_step_fifty_ms_later_and_each_step_adds_the_next_two_hundred_ms_after_it()
    {
        var run = new AreaRestRun(wait: false);
        Assert.IsFalse(run.Pending);
        Assert.IsFalse(run.Press(8, 100), "the ninth button does not exist");
        Assert.IsTrue(run.Press(2, 1_000));
        Assert.AreEqual((4, 1_050L, true), (run.Code, run.DueMs, run.Pressed));
        Assert.IsFalse(run.TakeDue(1_049));
        Assert.IsTrue(run.TakeDue(1_050));
        Assert.IsFalse(run.Pending, "a taken step is spent until Finish re-adds it");
        // Four hours: steps at 1050 (4 left), 1260 (3), 1460 (2), 1700 (1); the last one ends the chain. A late step moves every later one with it.
        long now = 1_060;
        var steps = 0;
        while (true)
        {
            steps++;
            var end = run.Finish(now, hourAfterJump: 10, sleeperDead: false, sleeperHasHitPointDamage: false);
            if (end != AreaRestEnd.Continue) { Assert.AreEqual(AreaRestEnd.Done, end); break; }
            Assert.AreEqual(now + AreaRestRun.StepGapMs, run.DueMs);
            Assert.IsFalse(run.TakeDue(now + 199)); now += 200;
            Assert.IsTrue(run.TakeDue(now));
        }
        Assert.AreEqual(4, steps);
        Assert.IsFalse(run.Pending);
    }

    [TestMethod]
    public void A_second_press_replaces_the_pending_step_and_clear_forgets_the_chain()
    {
        var run = new AreaRestRun(wait: true);
        Assert.IsTrue(run.Press(0, 0));
        Assert.IsTrue(run.Press(4, 500));
        Assert.AreEqual((24, 550L), (run.Code, run.DueMs));
        Assert.IsFalse(run.Press(7, 600), "the wait window has seven buttons");
        run.Clear();
        Assert.AreEqual((0, false, false), (run.Code, run.Pressed, run.Pending));
    }

    [TestMethod]
    public void A_code_rest_keeps_its_code_until_the_end_test_stops_it()
    {
        var run = new AreaRestRun(wait: false);
        run.Press(5, 0);
        Assert.AreEqual(AreaRestRules.UntilMorning, run.Code);
        Assert.IsTrue(run.TakeDue(50));
        Assert.AreEqual(AreaRestEnd.Continue, run.Finish(50, 3, false, false));
        Assert.AreEqual(AreaRestRules.UntilMorning, run.Code);
        Assert.IsTrue(run.TakeDue(250));
        Assert.AreEqual(AreaRestEnd.Done, run.Finish(250, 7, false, false));
        Assert.AreEqual((0, false), (run.Code, run.Pending));
    }

    [TestMethod]
    public void The_fade_goes_in_forty_eight_whole_steps_over_two_seconds_and_a_stop_fades_in_from_where_it_is()
    {
        var fade = new AreaRestFade();
        Assert.AreEqual(0.0, fade.Alpha(0));
        Assert.IsFalse(fade.Visible(0));
        fade.Out(1_000);
        Assert.AreEqual(0.0, fade.Alpha(1_000));
        Assert.AreEqual(1.0 / 48, fade.Alpha(1_000 + 2000 / 48 + 1), 1e-12);
        Assert.AreEqual(0.5, fade.Alpha(2_000), 1e-12);
        Assert.AreEqual(1.0, fade.Alpha(3_000));
        Assert.AreEqual(1.0, fade.Alpha(9_999));
        fade.Tick(9_999);
        Assert.AreEqual(1.0, fade.Alpha(20_000));
        Assert.IsTrue(fade.Visible(20_000));
        fade.In(20_000);
        Assert.AreEqual(0.5, fade.Alpha(21_000), 1e-12);
        Assert.AreEqual(0.0, fade.Alpha(22_000));
        fade.Tick(22_000);
        Assert.IsFalse(fade.Visible(22_000));
        // A stop before the fade-out finished fades in from the cover it had reached.
        fade.Out(30_000);
        fade.In(31_000);
        Assert.AreEqual(0.5, fade.Alpha(31_000), 1e-12);
        Assert.AreEqual(0.375, fade.Alpha(31_500), 1e-12);
        Assert.AreEqual(0.0, fade.Alpha(33_000));
        fade.Out(40_000); fade.Reset();
        Assert.AreEqual(0.0, fade.Alpha(40_500));
    }

    [TestMethod]
    public void The_texts_are_the_files_or_the_projects_and_the_labels_follow_the_window_kind()
    {
        var project = AreaRestTexts.Project("no game folder");
        Assert.IsFalse(project.FromGame);
        Assert.AreEqual("1 hour", project.Label(0, wait: false));
        Assert.AreEqual("Until healed", project.Label(7, wait: false));
        Assert.AreEqual("Wait until evening", project.Label(6, wait: true));
        Assert.AreEqual("[99]", project.Text(99));
        var game = AreaRestTexts.From(number => number == 3 ? "huit" : null, "synthetic");
        Assert.IsTrue(game.FromGame);
        Assert.AreEqual("huit", game.Label(3, wait: false));
        Assert.AreEqual("1 hour", game.Label(0, wait: false), "a number the file lacks takes the project's text");
        Assert.AreEqual("[21]", AreaRestTexts.Project("x").Text(21));
        Assert.IsTrue(AreaRestTexts.Load("no-such-module").Detail.Contains("not read", StringComparison.Ordinal));
    }
}
