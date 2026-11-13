using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-1160 (E3-a): the sleep and wait window (interface mode 6) in crash-site-area map mode. The key S and the bar's Sleep button run the original's open decision (0x57B180, ledger SWB1-SWB4, AreaRestRules.Decide):
// nothing for a dead hero, a close for an open window (the key toggles), a message for an unconscious hero, for a town-map sector without the wait marker (townmap.mes, whose entry carries it: waiting only, no heal) and for a hero in
// combat mode that is engaged with him or his party; otherwise the hero leaves combat mode and the window opens. A press on a length (the original's table: 1, 2, 4, 8, 24 hours, until 07:00, until 20:00, until healed) runs script event
// 29 on the hero's followers, fades the screen out and switches the hourly light apply off once (SWB11), and starts the chain of steps (AreaRestRun: the first 50 ms later, each next 200 ms of real time after the one before): a step jumps the game
// clock by an hour (the jump fires the game's due events late, once), heals the hero and his followers (the wait window heals nothing) and tests the end (AreaRestRules.AfterStep); the end, a second S, another window or a changed session
// stop it (the light is applied at once and the screen fades in, 0x57BA70). Nothing in the window reads damage or hostile arrivals (SWB13). The window is not a pause: only the Esc menu pauses, and the steps go on behind it (TL27).
// ARC-WORLD-1162 (E3-d): a click on a bed (scenery of art type 4, sub-type 0x16; the use goal 38's act node, 0x428A10) calls the same decision with the bed (SWB1): no town-map test (a bed is usable in a town), never the wait window,
// the combat-mode exit as before, then the bed's use script on attachment point 1 with the hero as triggerer: the answer "run default" (and a bed with no script) refuses with message 8 (the hero has already left combat mode),
// an occupied bed refuses with message 10, otherwise the hero is the occupant and the window opens with the heal of kind 2 (twice the ground's); the close empties the bed (SWB5, SWB12, SWB15, SWB16).
// Not modelled: the sleeper's sleeping flag and hidden drawing, the bed's occupied frame and the tile shift of a sleeper on the bed's tile (SWB15, SWB16), the random encounter that closes the window (the encounter event is the world journey's E2-c), the maintained-spell ending of the sleeper (UE6, ARC-MAGIC-... #1161) and the original's window art.
public partial class TerrainViewerApp
{
    private AreaSleepPanel? _sleepPanel;
    private string? _sleepStatus;
    private Task<AreaRestTexts>? _sleepTextsTask;
    private string? _sleepTextsModule;
    private AreaRestTexts? _sleepTexts;
    // ARC-UI-1363: the original's window art (565, 293 and the font 229), read once for a module in the background.
    private Task<AreaSleepContent>? _sleepArtTask;
    private AreaSleepContent? _sleepArt;
    // The open window's chain, the session it opened in (a replaced session closes it) and the host time of the last frame (the steps run on it).
    private AreaRestRun? _rest;
    private GameSessionOwner? _restSession;
    // ARC-WORLD-1162: who lies in which bed of the shown map (the hero only, while his window is open) and the bed of the open window.
    private readonly AreaBedOccupancy _bedOccupancy = new();
    private AreaObjectKey? _restBed;
    private readonly AreaRestFade _restFade = new();
    // Once per rest (flag 0x6834E0, SWB11): the screen faded and the hourly light apply switched off.
    private bool _restDark;
    private long _clockHostMs;
    // Native observation: the steps that ran, the hours they jumped and what each healed, for the self-test and the smoke.
    private readonly List<string> _restLog = [];
    private bool SleepOpen => WindowOpen(InterfaceMode.Sleep);
    private bool SleepActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    /// <summary>Starts loading the window's texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaSleep(TerrainViewerArguments arguments)
    {
        if (_sleepTextsModule == arguments.ModulePath) return;
        _sleepTextsModule = arguments.ModulePath; _sleepTexts = null;
        _sleepArt = null; _sleepPanel?.ClearArt();
        var module = arguments.ModulePath;
        _sleepTextsTask = SelfTestBootstrap || module is null ? null : Task.Run(() => AreaRestTexts.Load(module));
        _sleepArtTask = SelfTestBootstrap || module is null ? Task.FromResult(AreaSleepContent.Empty("self-test: no game folder")) : Task.Run(() => AreaSleepContent.Load(module));
    }

    /// <summary>ARC-UI-1363: gives the panel the original's art once it has read (the next opening is drawn in the original's look); a window opened before that, or with art that did not read, is the plain panel.</summary>
    private void AdoptSleepArt()
    {
        if (_sleepPanel is null) return;
        if (_sleepArt is null && _sleepArtTask is { IsCompleted: true } task)
        {
            _sleepArt = task.IsCompletedSuccessfully ? task.Result : AreaSleepContent.Empty("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            GD.Print($"ARC-UI-1363 sleep window art ready={_sleepArt.Ready} {_sleepArt.Detail}");
        }
        if (_sleepArt is not { Ready: true } content || _sleepPanel.ArtSet) return;
        var frames = AreaSleepLayout.ArtNumbers.Where(number => number != AreaSleepLayout.FontArt)
            .ToDictionary(number => number, number => content.Art[number].Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray());
        _sleepPanel.SetArt(frames, new AreaArtFontFace(content.Art[AreaSleepLayout.FontArt].Frames));
    }

    private AreaRestTexts SleepTexts()
    {
        if (_sleepTexts is null && _sleepTextsTask is { IsCompleted: true } task)
        {
            _sleepTexts = task.IsCompletedSuccessfully ? task.Result : AreaRestTexts.Project("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            _sleepTextsTask = null;
            GD.Print($"ARC-WORLD-1160 sleep texts {_sleepTexts.Detail}");
        }
        return _sleepTexts ?? AreaRestTexts.Project(_sleepTextsTask is null ? "no game folder" : "still loading");
    }

    /// <summary>The town map the open decision reads (SWB3): the hero's sector's town-map number, and whether its <c>townmap.mes</c> entry carries the wait marker (the wait table).</summary>
    private AreaRestTownMap RestTownMap()
    {
        var number = PcTownMapNumber();
        if (number <= 0) return AreaRestTownMap.None;
        var entry = AdoptTownMapSource()?.Table.Find(number);
        GD.Print($"ARC-WORLD-1160 town map {number} entry={(entry is null ? "none" : entry.Name)} wait={entry?.Whole.ToString() ?? "unknown (the town-map files are not open)"}");
        return entry is { Whole: true } ? AreaRestTownMap.WaitAllowed : AreaRestTownMap.WaitRefused;
    }

    // The exit gate's facts (0x4B3D90): the attack the hero is playing and the NPCs near him that fight him or his party.
    private SessionCombatExit RestCombatExit() => new()
    {
        AttemptingAttack = AttackPlaying,
        Threats = _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) ? ai.CombatThreats() : [],
    };

    private AreaRestFacts RestFacts(SessionCombatExit exit, bool bed)
    {
        var (dead, unconscious) = HeroDownState();
        var inCombat = HeroInCombatMode;
        // A call with a bed skips the town-map test (SWB3).
        return new(dead, SleepOpen, unconscious, bed || dead || SleepOpen || unconscious ? AreaRestTownMap.None : RestTownMap(), inCombat,
            !inCombat || _areaSession!.QueryCanExitCombatMode(exit), bed);
    }

    /// <summary>The S key, the bar's Sleep button and (ARC-WORLD-1162) a click on a bed: the open decision (SWB1 to SWB5). <paramref name="bed"/> is the used bed or none. Other windows stay open when it refuses, and are closed once it opens (SWB6).</summary>
    private void OpenSleep(AreaObjectKey? bed = null)
    {
        if (!SleepActive) return;
        // The town-map files decide a town sector (SWB3, a call without a bed); while they are not read (or cannot be) the key answers that, not message 20.
        if (bed is null && !SleepOpen && PcTownMapNumber() > 0 && AdoptTownMapSource() is null)
        {
            _sleepStatus = "sleep: the town-map files are not read yet";
            PostMessage(AreaMessageEntry.Text(AreaMessageKind.Message, _sleepStatus), "sleep town map unknown");
            RefreshAreaStatus();
            return;
        }
        var exit = RestCombatExit();
        var opening = AreaRestRules.Decide(RestFacts(exit, bed is not null));
        GD.Print($"ARC-WORLD-1160 sleep open decision={opening.Outcome} wait={opening.Wait} message={opening.Message?.ToString() ?? "-"} leaves-combat={opening.LeavesCombatMode} bed={bed is not null}");
        switch (opening.Outcome)
        {
            case AreaRestOpenOutcome.Nothing: return;
            case AreaRestOpenOutcome.Close: EndSleep(); return;
            case AreaRestOpenOutcome.Refused: SleepNotice(opening.Message!.Value); return;
        }
        var session = _areaSession!;
        // The hero leaves combat mode at once, before the window or another refusal can follow.
        if (opening.LeavesCombatMode)
        {
            var left = session.LeaveHeroCombatMode(session.Version, CombatModeContext(), exit);
            if (!HeroInCombatMode) CancelAreaAttack("left combat mode");
            if (left.Refusal == SessionCombatModeRefusal.UnderAttack) { SleepNotice(AreaRestMessages.CombatRefused); return; }
        }
        // ARC-WORLD-1162 (SWB5, SWB15): a bed's use script answers first (the answer 1 refuses, with the hero already out of combat mode), then the seat (an occupied bed refuses).
        if (bed is { } usedBed)
        {
            if (BedScriptAnswer(session, usedBed) is { } refused) { if (refused.Message is { } message) SleepNotice(message); else BedStatus(refused.Detail!); return; }
            if (!_bedOccupancy.Seat(usedBed, AreaScriptHost.PcCode)) { SleepNotice(AreaRestMessages.BedOccupied); return; }
        }
        if (_sleepPanel is null)
        {
            _sleepPanel = new AreaSleepPanel(); AddChild(_sleepPanel);
            _sleepPanel.ButtonPressed += SleepPressed;
            _sleepPanel.SoundRequested += id => PlayBarSound(id, "sleep button");
        }
        AdoptSleepArt();
        CloseOtherInterfaceWindows();
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        var texts = SleepTexts();
        var rest = new AreaRestRun(opening.Wait);
        var labels = Enumerable.Range(0, AreaRestRules.ButtonCount(opening.Wait)).Select(i => texts.Label(i, opening.Wait)).ToArray();
        _rest = rest; _restSession = session; _restLog.Clear(); _restBed = bed;
        if (!OpenWindowMode(InterfaceMode.Sleep, () => _sleepPanel!.Open(labels, opening.Wait), () => _sleepPanel!.CloseView()))
        {
            // A refused mode change takes the hero out of the bed again (SWB6).
            if (bed is { } seated) _bedOccupancy.Unseat(seated, AreaScriptHost.PcCode);
            _rest = null; _restSession = null; _restBed = null;
            return;
        }
        _sleepStatus = null;
        GD.Print($"ARC-WORLD-1160 sleep open wait={opening.Wait} buttons={labels.Length} texts={(texts.FromGame ? "game" : "project")} bed={bed is not null}");
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-WORLD-1162 (SWB5, 0x441980): the bed's use script with the hero as triggerer. Null when the hero goes on to the seat; otherwise the refusal: the message 8 of the answer 1 (the run-default end and a bed with no script), or the detail
    /// of a run the host does not model (no message of the original's says that, the status line does).
    /// </summary>
    private (int? Message, string? Detail)? BedScriptAnswer(GameSessionOwner session, AreaObjectKey bed)
    {
        if (_readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host || UseObjectRow(bed) is not { } row) return (null, "no script host for the shown map");
        var run = host.RunBed(row, session.Rules.Scripts);
        Trapped(Awarded(host, run.Use));
        foreach (var entry in ScriptLineEntries(run.Use.Printed)) PostMessage(entry, "bed script line");
        GD.Print($"ARC-WORLD-1162 bed script bed={AreaUseScriptHost.EntityId(row)} answer={run.Answer} outcome={run.Use.Outcome} script={run.Use.Run?.Script} detail={run.Use.Detail}");
        return run.Answer switch
        {
            AreaBedAnswer.RunDefault => (AreaRestRules.BedScriptRefusal(true), null),
            AreaBedAnswer.SkipDefault => null,
            _ => (null, "the bed's script is not modelled: " + run.Use.Detail),
        };
    }

    private void BedStatus(string detail)
    {
        _sleepStatus = detail;
        RefreshAreaStatus();
    }

    /// <summary>ARC-WORLD-1162: the use of a bed (the use goal's act, 0x428A10): the use rule's range and access test, then the rest hook with the bed (<see cref="OpenSleep"/>).</summary>
    private void ApplyBedUse(AreaObjectKey bed, AreaStepCell actor)
    {
        var transfers = _mapTransfers!;
        var row = transfers.Object(bed);
        var check = AreaUseRule.Check(transfers.Map, bed, new(row.WorldX!.Value, row.WorldY!.Value), actor);
        GD.Print($"ARC-WORLD-1162 bed use outcome={check.Outcome} distance={check.Distance}");
        if (check.Outcome != AreaUseOutcome.Accepted) { _areaUseStatus = check.Outcome == AreaUseOutcome.TooFar ? $"too far: {check.Distance} cells (use range {AreaUseRule.UseRange})" : $"bed use refused: {check.Outcome} {check.Reason}"; RefreshAreaStatus(); return; }
        StopWorldRoute("ROUTE_REPLACED_BY_USE");
        OpenSleep(bed);
    }

    private void SleepNotice(int message)
    {
        var text = SleepTexts().Text(message);
        _sleepStatus = text;
        PostMessage(AreaMessageEntry.Text(AreaMessageKind.Message, text), "sleep refusal");
        RefreshAreaStatus();
    }

    /// <summary>A left click on a length (SWB8): the pending step is removed and the first is due 50 ms later; the screen fades out once; script event 29 runs on the direct followers; then the sleeper's maintained spells end
    /// (0x457450, ledger UE6, ARC-MAGIC-1161: the sleeper only, no interrupt of its actions, in the wait window too).</summary>
    private void SleepPressed(int button)
    {
        if (!SleepOpen || _rest is not { } rest || _areaSession is not { } session || !ReferenceEquals(_restSession, session)) return;
        if (!rest.Press(button, _clockHostMs)) return;
        _sleepPanel!.HideButtons();
        if (!_restDark) { _restDark = true; _restFade.Out(_clockHostMs); }
        RunLeaderSleeping(session);
        var caster = SpellCasterNow();
        caster?.EndMaintainedSpells(SessionSpellCaster.Hero);
        GD.Print($"ARC-WORLD-1160 sleep press button={button} code={rest.Code} wait={rest.Wait} due={rest.DueMs} maintained-spells-ended={caster is not null} (ARC-MAGIC-1161)");
        RefreshReadyStatus();
    }

    // SWB8 (0x57BAC0): script event 29, leader sleeping, on every direct follower of the sleeper, the hero as the triggerer; its answer decides nothing.
    private void RunLeaderSleeping(GameSessionOwner session)
    {
        if (_readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return;
        foreach (var follower in session.QueryParty().Followers)
        {
            var ran = Awarded(host, host.RunLeaderSleeping(follower.Entity, session.Rules.Scripts));
            if (ran.Run is not null) GD.Print($"ARC-WORLD-1160 leader-sleeping follower={follower.Entity} outcome={ran.Outcome} run={ran.Run.Outcome} detail={ran.Detail}");
        }
    }

    /// <summary>The game's jump of an hour (SWB9, step a): the clock moves, the due events fire once and late, and the ambient light is not applied (SWB11: it is switched off until the stop).</summary>
    private void RestJump(long gameMs)
    {
        if (_areaAmbient is { } driver) { driver.AdvanceMilliseconds(gameMs, AmbientSector()); LogAreaEvents(driver); return; }
        // A map drawn unlit has the frame driver of its session instead (a new driver's first frame admits nothing).
        if (_unlitFrame is null) TickUnlitFrame(_clockHostMs);
        if (_unlitFrame is { } held) { held.Driver.Frame.Jump(gameMs); LogAreaEvents(held.Driver.Frame); }
    }

    /// <summary>Per frame on the host clock (<see cref="TickAreaClock(long)"/>): a due step jumps an hour, heals and tests the end; the cover follows the fade.</summary>
    private void TickSleep(long hostMs)
    {
        _restFade.Tick(hostMs);
        // The death cover freezes the frame (TL25): no hour is jumped under it; the pending step waits.
        if (_rest is { } rest && !WorldFrozenFrame && rest.TakeDue(hostMs) && _areaSession is { } session && ReferenceEquals(_restSession, session))
        {
            try { RestJump(AreaRestRules.StepGameMs); }
            catch { EndSleep(); throw; } // a rejected jump must not leave the window dark and stuck
            var healed = AreaRestParty.Step(session, _aiHost is { } ai && ReferenceEquals(ai.Session, session) ? ai : null, AreaRestRules.HealKind(_restBed is not null), heal: !rest.Wait);
            var hour = session.QueryClockSnapshot().Hour;
            var code = rest.Code;
            var end = rest.Finish(hostMs, hour, healed.SleeperDead, healed.SleeperHasHitPointDamage);
            if (_restLog.Count < 64) _restLog.Add($"code={code} hour={hour} healed={healed.Healed} damage={healed.SleeperHasHitPointDamage} end={end}");
            GD.Print($"ARC-WORLD-1160 sleep step code={code} hour={hour:00} healed={healed.Healed} hp-damage={healed.SleeperHasHitPointDamage} dead={healed.SleeperDead} end={end}");
            if (end != AreaRestEnd.Continue) EndSleep();
        }
        _sleepPanel?.SetFade(_restFade.Alpha(hostMs));
    }

    /// <summary>The stop (SWB11) and the close (SWB12): the pending step is removed, the light is applied at once, the screen fades back in and the window closes.</summary>
    private void EndSleep()
    {
        if (!SleepOpen && _rest is null) return;
        _rest?.Clear();
        // The close takes the hero out of his bed (SWB12, SWB16).
        if (_restBed is { } bed) { _bedOccupancy.Unseat(bed, AreaScriptHost.PcCode); _restBed = null; }
        if (_restDark)
        {
            _restDark = false;
            _restFade.In(_clockHostMs);
            if (_areaAmbient is not null) { PushAreaAmbient(); BindAreaAmbientPalette(); }
        }
        GD.Print("ARC-WORLD-1160 sleep closed");
        if (SleepOpen) CloseWindowMode(InterfaceMode.Sleep);
        _rest = null; _restSession = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a replaced session or a session that cannot show it closes the window; the cover is cleared at once with it (a load closes the window and its events).</summary>
    private void SyncAreaSleep()
    {
        _sleepPanel?.PlaceView();
        if (_rest is not null && (!SleepActive || !ReferenceEquals(_restSession, _areaSession)))
        {
            _restDark = false; _restFade.Reset();
            EndSleep();
            _sleepPanel?.SetFade(0);
        }
    }

    /// <summary>The window's own keys: it takes none (the original's window has no key handler); a controller cancel closes it, as a project key.</summary>
    private void HandleSleepKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.S && !CharacterKeySilenced(key, routed)) OpenSleep();
    }

    private void HandleSleepPulse(IntentFrame intent)
    {
        if (intent.Cancel) EndSleep();
        else if (intent.MenuPressedDirection != 0) _sleepPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _sleepPanel!.Confirm();
    }

    /// <summary>The original key S (both cases) of the in-game key filter opens the window (SWB1) while an area session exists. True when the key belongs to it.</summary>
    private bool RouteSleepKey(WorldKeyDecision decision)
    {
        if (!SleepActive || decision.Key != WorldKey.S) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _sleepStatus = "S ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenSleep();
        return true;
    }

    private string AreaSleepStatus() => !SleepActive ? "" : SleepOpen ? $" | {(_rest?.Wait == true ? "wait" : "sleep")} window open: S closes" + (_rest is { Pressed: true } ? $", {_rest.Code} left" : "")
        : _sleepStatus is null ? "" : " | sleep: " + _sleepStatus;

    // Native observation (self-test and smoke): the window and the chain as they stand.
    internal bool SleepOpenForSelfTest => SleepOpen;
    internal AreaRestRun? RestForSelfTest => _rest;
    internal AreaSleepPanel? SleepPanelForSelfTest => _sleepPanel;
    /// <summary>The window's art content has read and is ready (it reads in the background; false in the self-test).</summary>
    internal bool SleepArtReadyForSelfTest => _sleepArtTask is { IsCompleted: true, IsCompletedSuccessfully: true } task && task.Result.Ready;
    internal IReadOnlyList<string> RestLogForSelfTest => _restLog;
    internal void OpenSleepForSelfTest(AreaObjectKey? bed = null) => OpenSleep(bed);
    internal AreaBedOccupancy BedOccupancyForSelfTest => _bedOccupancy;
    internal string? SleepStatusForSelfTest => _sleepStatus;
    internal void SleepPressedForSelfTest(int button) => SleepPressed(button);
    internal void TickSleepForSelfTest(long hostMs) { _clockHostMs = hostMs; TickSleep(hostMs); }
}
