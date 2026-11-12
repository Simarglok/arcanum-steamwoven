using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-1160 (E3-a) native observation (stage "sleep", only when named, with --hero, on a map with no town map at the start): the sleep window on the real data. The hero is wounded first. The open decision
    // opens the ground window (eight buttons, texts from sleepUI.mes when it reads); a press hides the buttons and fades the screen out; the first step comes 50 ms of host time later and jumps the clock by exactly an hour; the
    // one-hour rest ends there and the screen fades back in. Until healed goes on step by step (200 ms apart) while the hero has hit-point damage and stops when it is gone; until morning stops at the hour 7 after the
    // number of steps the clock's hour says; the key toggles the window (a second open closes it); a refused open (the dead hero) leaves nothing. ARC_AREA_SLEEP_CAPTURE names a folder for screenshots of the window
    // (open, after the press with the fade half way) for the graphics report. The clock and the hero's damage stay where the rests leave them (the stage runs in its own process).
    private async Task RunAreaSleepSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || !SleepActive || session.QueryHeroLife() is null)
        { GD.Print("ARC-WORLD-1160 native-smoke sleep NONE map=" + _readyMapName + " (needs --hero and an area session)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-1160 sleep smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SLEEP_SMOKE_FAILED");
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SLEEP_CAPTURE");
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        async Task Shot(string name)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await Frames(2);
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng(System.IO.Path.Combine(capture, $"sleep-{name}.png")) == Error.Ok, "the capture " + name + " was written");
        }
        int HpDamage() => session.QueryHeroLife()!.Record.State.HitPointDamage;
        int FatigueDamage() => session.QueryHeroLife()!.Record.State.FatigueDamage;
        long Now() => session.QueryClockSnapshot().GameTimestampMs;
        int Hour() => session.QueryClockSnapshot().Hour;
        var host = 90_000_000L;
        void Tick(long at) { host = at; TickSleepForSelfTest(at); }
        void Wound(int hitPoints, int fatigue)
        {
            var wounded = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = hitPoints, Fatigue = fatigue });
            Require(wounded.Disposition != GameSessionDisposition.Rejected && HpDamage() >= hitPoints && session.QueryHeroLife()!.Status.IsDead == false, $"the hero is wounded: {wounded.Disposition} {wounded.Failure} damage {HpDamage()}");
        }
        // Steps the host clock 50 ms at a time until the window closes (at most `limit` ticks); the log counts the steps that ran.
        int RunUntilClosed(int limit)
        {
            var steps = _restLog.Count;
            for (var i = 0; i < limit && SleepOpenForSelfTest; i++) Tick(host + 50);
            Require(!SleepOpenForSelfTest, $"the rest ended within {limit} ticks (log {string.Join(" | ", _restLog.TakeLast(6))})");
            return _restLog.Count - steps;
        }

        Tick(host);
        Require(!SleepOpenForSelfTest, "no sleep window before the key");
        await Shot("before");
        var heroBefore = session.QueryHeroLife()!.Record.State;

        // ---- the open decision: the ground window, eight labelled buttons ----
        Wound(6, 9);
        OpenSleepForSelfTest();
        if (!SleepOpenForSelfTest) { GD.Print($"ARC-WORLD-1160 native-smoke sleep NONE map={_readyMapName} (the open decision refused: {_sleepStatus ?? "no notice"})"); return; }
        // A sector with a town map that carries the wait marker (the Crash Site start) opens the wait window: seven buttons, no heal (SWB3); any other sector (the wilderness sector 1384,239) the ground window with eight.
        var wait = RestForSelfTest!.Wait; var buttons = wait ? 7 : 8;
        Require(SleepPanelForSelfTest is { } panel && panel.ButtonCountForSelfTest == buttons, $"the {(wait ? "wait" : "ground")} window has {buttons} buttons (buttons={SleepPanelForSelfTest?.ButtonCountForSelfTest})");
        Require(WindowPanelsMatchStack && _interfaceWindows.Stack.Current == Arcanum.World.InterfaceMode.Sleep, "the stack and the panel agree: mode 6 on top");
        Require(AreaSleepStatus().Contains("window open", StringComparison.Ordinal), "the status line says the window is open");
        var labels = Enumerable.Range(0, buttons).Select(i => SleepPanelForSelfTest!.ButtonTextForSelfTest(i)).ToArray();
        GD.Print($"ARC-WORLD-1160 sleep labels game-text={SleepTexts().FromGame} {string.Join(" | ", labels)}");
        Require(labels.All(label => label.Length > 0 && !label.StartsWith('[')), "every button has a label");
        await Shot("open");
        // A second open (the key S) closes it, a third opens it again.
        OpenSleepForSelfTest();
        Require(!SleepOpenForSelfTest && RestForSelfTest is null && WindowPanelsMatchStack, "a second open closes the window");
        OpenSleepForSelfTest();
        Require(SleepOpenForSelfTest, "and a third opens it again");

        // ---- one hour: the press, the 50 ms, the exact jump and the heal ----
        var timeBefore = Now(); var hpBefore = HpDamage(); var fatigueBefore = FatigueDamage();
        Tick(host + 1_000);
        var pressedAt = host;
        SleepPressedForSelfTest(0);
        Require(RestForSelfTest is { Pressed: true, Code: 1 } && !SleepPanelForSelfTest!.ButtonsShown, "the press hides the buttons and takes the code of one hour");
        Tick(pressedAt + 49);
        Require(Now() == timeBefore && RestLogForSelfTest.Count == 0, "no step before 50 ms");
        Tick(pressedAt + AreaRestRun.FirstStepMs);
        Require(Now() == timeBefore + AreaRestRules.StepGameMs, $"the first step jumped the clock by exactly an hour ({Now() - timeBefore} ms)");
        Require(wait ? HpDamage() == hpBefore && FatigueDamage() <= fatigueBefore : HpDamage() < hpBefore && FatigueDamage() < fatigueBefore, $"the step {(wait ? "left the hit points alone in the wait window (the game's own fatigue recovery fires inside the jump)" : "healed the hero")}: hit-point damage {hpBefore} -> {HpDamage()}, fatigue {fatigueBefore} -> {FatigueDamage()}");
        Require(!SleepOpenForSelfTest && RestForSelfTest is null, "a one-hour rest ends at its first step and the window closes");
        Require(RestLogForSelfTest.Count == 1 && RestLogForSelfTest[0].EndsWith("end=Done", StringComparison.Ordinal), "one step, ended Done: " + string.Join(" | ", RestLogForSelfTest));
        Require(_sleepPanel!.FadeForSelfTest > 0, "the fade-in starts from the cover the short rest reached");
        Tick(host + AreaRestFade.DurationMs + 100);
        Require(_sleepPanel.FadeForSelfTest == 0 && !_restFade.Visible(host), "the screen is clear again after the fade-in");
        Report("one-hour", 1);

        // ---- until healed (the ground window only): a step every 200 ms while hit-point damage is left; the wait window has no such button ----
        var healedSteps = 0; var spellEnded = -1;
        if (wait)
        {
            OpenSleepForSelfTest();
            Tick(host + 10);
            SleepPressedForSelfTest(7);
            Require(RestForSelfTest is { Pressed: false } && SleepPanelForSelfTest!.ButtonsShown, "the wait window has no eighth button: a press on it does nothing");
            OpenSleepForSelfTest();
            Require(!SleepOpenForSelfTest, "the key closes it");
            Tick(host + AreaRestFade.DurationMs + 100);
        }
        else
        {
            Wound(10, 12);
            var damageBefore = HpDamage();
            OpenSleepForSelfTest();
            Require(SleepOpenForSelfTest, "the window opens again");
            Tick(host + 10);
            SleepPressedForSelfTest(7);
            Require(RestForSelfTest is { Code: AreaRestRules.UntilHealed }, "the last button is until healed");
            var timeHealed = Now(); var gaps = new List<long>(); var lastStep = -1L; var seen = 0;
            for (var i = 0; i < 4000 && SleepOpenForSelfTest; i++)
            {
                Tick(host + 10);
                if (_restLog.Count > seen) { seen = _restLog.Count; if (lastStep >= 0) gaps.Add(host - lastStep); lastStep = host; }
            }
            Require(!SleepOpenForSelfTest && HpDamage() == 0, $"until healed ended with no hit-point damage: {HpDamage()} (log {string.Join(" | ", RestLogForSelfTest.TakeLast(5))})");
            healedSteps = RestLogForSelfTest.Count;
            Require(healedSteps >= 1 && Now() == timeHealed + healedSteps * AreaRestRules.StepGameMs, $"{healedSteps} steps jumped {(Now() - timeHealed) / AreaRestRules.StepGameMs} hours");
            Require(gaps.All(gap => gap is >= AreaRestRun.StepGapMs and <= AreaRestRun.StepGapMs + 10), $"the steps came 200 ms apart: {string.Join(",", gaps)}");
            Require(RestLogForSelfTest.Take(healedSteps - 1).All(entry => entry.Contains("damage=True", StringComparison.Ordinal)), "every step but the last still had damage");
            GD.Print($"ARC-WORLD-1160 sleep until-healed damage {damageBefore} -> 0 in {healedSteps} steps");
            Report("until-healed", healedSteps);
            Tick(host + AreaRestFade.DurationMs + 100);
        }

        // ---- until morning: stops at the hour 7 ----
        OpenSleepForSelfTest();
        Require(SleepOpenForSelfTest, "the window opens for the last rests");
        Tick(host + 10);
        var hourBefore = Hour(); var expected = (AreaRestRules.MorningHour - hourBefore + 24) % 24; if (expected == 0) expected = 24;
        var morningStart = Now();
        SleepPressedForSelfTest(5);
        Require(RestForSelfTest is { Code: AreaRestRules.UntilMorning }, "the sixth button is until morning");
        var morningSteps = RunUntilClosed(4000);
        Require(Hour() == AreaRestRules.MorningHour && morningSteps == expected && Now() == morningStart + expected * AreaRestRules.StepGameMs,
            $"until morning from hour {hourBefore}: {morningSteps} steps (expected {expected}), hour {Hour()}");
        Report("until-morning", morningSteps);
        Tick(host + AreaRestFade.DurationMs + 100);

        // ---- ARC-MAGIC-1161 (0x57B710 -> 0x457450, ledger UE6): the press ends the sleeper's maintained spells; opening the window does not ----
        if (SpellCasterNow() is not null && _spellRecords is { Length: > 0 } && StartMaintainedSpellForSmoke(session, SessionSpellCaster.Hero) is { } heroSpell)
        {
            Tick(host + AreaRestFade.DurationMs + 100);
            OpenSleepForSelfTest();
            Require(SleepOpenForSelfTest, "the window opens for the spell ending");
            Tick(host + 10);
            Require(session.QuerySpellRuns()[heroSpell.Run] is not null && session.QueryMaintainBar().SlotOf(heroSpell.Run) is not null, "the maintained spell still runs while the window is open (only the press ends it)");
            SleepPressedForSelfTest(0);
            // The ending is queued by the press and told by the next frame pass of the cast host (as the world-map start's); the frame's pass here is the viewer's own, run before the first 50 ms step is due.
            for (var i = 0; i < 20 && session.QuerySpellRuns()[heroSpell.Run] is not null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); TickAreaClock((long)Time.GetTicksMsec()); }
            Require(RestLogForSelfTest.Count == 0 && RestForSelfTest is { Pressed: true }, "no rest step ran yet: the ending is the press's, not the hour jump's");
            Require(session.QuerySpellRuns()[heroSpell.Run] is null && session.QueryMaintainBar().SlotOf(heroSpell.Run) is null,
                $"the press ended the sleeper's maintained spell {heroSpell.Spell.Data.Number} (run {heroSpell.Run}: {session.QuerySpellRuns()[heroSpell.Run]?.Action.ToString() ?? "gone"})");
            RunUntilClosed(100);
            Tick(host + AreaRestFade.DurationMs + 100);
            spellEnded = heroSpell.Spell.Data.Number;
            GD.Print($"ARC-MAGIC-1161 native-smoke sleep-demaintain PASS spell={spellEnded} wait={wait}");
        }
        else GD.Print("ARC-MAGIC-1161 native-smoke sleep-demaintain NONE (no cast host or no maintainable spell on this map)");

        // ---- a replaced session closes the window and clears the cover ----
        OpenSleepForSelfTest();
        Tick(host + 10);
        SleepPressedForSelfTest(4);
        Require(SleepOpenForSelfTest && _restDark, "a 24-hour rest is under way and the screen is dark");
        for (var i = 0; i < 44; i++) Tick(host + 50);
        Require(SleepOpenForSelfTest && _sleepPanel.FadeForSelfTest == 1, $"after 2.2 s the cover is black ({_sleepPanel.FadeForSelfTest}) and the rest goes on");
        await Shot("dark");
        _restSession = null;
        SyncAreaSleep();
        Require(!SleepOpenForSelfTest && !_restDark && _sleepPanel.FadeForSelfTest == 0, "a session that is no longer the shown one closes the window at once and clears the fade");
        Require(WindowPanelsMatchStack, "the stack and the panels agree at the end");

        GD.Print("ARC-WORLD-1160 native-smoke sleep PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Wait = wait, Labels = labels, HeroDamageBefore = new { heroBefore.HitPointDamage, heroBefore.FatigueDamage }, UntilHealedSteps = healedSteps, UntilMorningSteps = morningSteps, SpellEnded = spellEnded, GameText = SleepTexts().FromGame,
        }));

        void Report(string what, int steps) => GD.Print($"ARC-WORLD-1160 sleep rest={what} steps={steps} hour={Hour()} hp-damage={HpDamage()} fatigue-damage={FatigueDamage()}");
    }
}
