using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-051: the in-game bug report. Ctrl+B (no key of the original: B has no binding and a Ctrl letter is silenced, ARC-INPUT-004;
// a function key would need Fn on a Mac) opens it in the WORLD and AREA modes, in play, in the main menu and over any other window.
// The key's moment is kept first: the screen as drawn (before the report window), the last 120 s of the log
// (BugReportLog.Process, which the engine logger fills from the start and the session-event lines below add to) and, in a game, a
// save taken as F7 takes it (the actor's placement, the companions' cells; unlike F7 also under an open window, whose own state, such
// as an open dialog, is not in the save); then the window opens modal over everything (the
// game clock pauses as under the game's own windows, a walk stops, the shade takes every click). Send writes the folder
// (BugReportWriter): beside the .app or the exe of a packaged game, else (or when that folder cannot be written) in the user
// folder's reports; an automated run writes into its isolated folder. Cancel or Escape drops it. No upload.
public partial class TerrainViewerApp
{
    private const string BugReportKeyName = "Ctrl+B";
    private AreaBugReportPanel? _bugReportPanel;
    // The key's moment while the window is open (the PNG lives in a holder only then: ARC-RENDER-003's ready app keeps no byte arrays).
    private sealed record BugReportMoment(BugReportFacts Facts, IReadOnlyList<BugReportLine> Log, long Dropped, DateTime TakenUtc, DateTime TakenLocal,
        SavePicture? Screenshot, string? ScreenshotMissing, BugReportSave? Save, string? SaveMissing);
    private BugReportMoment? _bugReport;
    private BugReportResult? _bugReportResult;
    private object? _bugReportEventStamp;
    // The key whose press closed the report: its release is the report's too, so it reaches no window or game key (Escape acts on release).
    private Key? _bugReportClosingKey;
    // The control that had the keyboard under the report (a window's field or button), given back when it closes.
    private Control? _bugReportFocus;
    private bool BugReportOpen => _bugReportPanel?.IsOpen == true;

    private static bool IsBugReportChord(InputEventKey key) =>
        key is { Pressed: true, Echo: false, CtrlPressed: true, AltPressed: false, MetaPressed: false, ShiftPressed: false }
        && (key.Keycode == Key.B || key.PhysicalKeycode == Key.B);

    /// <summary>First in the WORLD input route: the open report owns every event (keys it does not take go to its text field, the
    /// shade takes the mouse), and Ctrl+B opens it over anything. True when the event is the report's.</summary>
    private bool RouteBugReportInput(InputEvent input)
    {
        if (BugReportOpen)
        {
            if (input is InputEventKey key) HandleBugReportKey(key);
            return true;
        }
        if (_bugReportClosingKey is { } closing && input is InputEventKey { Pressed: false } release
            && (release.Keycode != Key.None ? release.Keycode : release.PhysicalKeycode) == closing)
        {
            _bugReportClosingKey = null;
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (input is InputEventKey { Pressed: true }) _bugReportClosingKey = null;
        if (input is not InputEventKey press || !IsBugReportChord(press)) return false;
        GetViewport().SetInputAsHandled();
        OpenBugReport();
        return true;
    }

    private void HandleBugReportKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            if (_bugReportPanel!.Finished) EndBugReport("closed"); else CancelBugReport();
            _bugReportClosingKey = Key.Escape;
        }
        else if (routed is Key.Enter or Key.KpEnter && (key.CtrlPressed || key.MetaPressed)) { GetViewport().SetInputAsHandled(); SendBugReport(); }
    }

    private void OpenBugReport()
    {
        if (BugReportOpen || _exiting) return;
        var takenUtc = DateTime.UtcNow;
        var log = BugReportLog.Process.Recent();
        var (screenshot, thumbnail, screenshotMissing) = CaptureBugReportScreen();
        var facts = BugReportFactsNow();
        var (save, saveMissing) = CaptureBugReportSave(thumbnail, takenUtc.ToLocalTime());
        _bugReport = new(facts, log, BugReportLog.Process.Dropped, takenUtc, takenUtc.ToLocalTime(), screenshot is null ? null : new(screenshot), screenshotMissing,
            save, saveMissing);
        if (_bugReportPanel is null)
        {
            _bugReportPanel = new AreaBugReportPanel(); AddChild(_bugReportPanel);
            _bugReportPanel.SendRequested += SendBugReport;
            _bugReportPanel.CancelRequested += CancelBugReport;
            _bugReportPanel.CloseRequested += () => EndBugReport("closed");
        }
        _bugReportFocus = GetViewport().GuiGetFocusOwner();
        // A key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("BUG_REPORT");
        _bugReportPanel.Open(string.Join(" ", new[] { screenshotMissing is null ? null : "No screenshot: " + screenshotMissing + ".", saveMissing is null ? null : "No save: " + saveMissing + "." }
            .Where(part => part is not null)));
        GD.Print($"ARC-DEV-051 bug report open screenshot={screenshot?.Length ?? 0} save={save is not null} saveMissing={saveMissing ?? "none"} lines={log.Count}");
        RefreshReadyStatus();
    }

    /// <summary>The frame on screen as a PNG and the save list's 468×300 preview of it; a headless run draws no frame.</summary>
    private (byte[]? Png, byte[]? Thumbnail, string? Missing) CaptureBugReportScreen()
    {
        if (DisplayServer.GetName() == "headless") return (null, null, "a headless run draws no frame");
        var image = GetViewport().GetTexture()?.GetImage();
        if (image is null || image.IsEmpty()) return (null, null, "the frame on screen could not be read");
        var png = image.SavePngToBuffer();
        return png is { Length: > 0 } ? (png, SaveThumbnail(image), null) : (null, SaveThumbnail(image), "the frame could not be encoded as PNG");
    }

    /// <summary>The game saved as F7 saves it, or why there is no save (no game, the main menu, a state F7 refuses).</summary>
    private (BugReportSave? Save, string? Missing) CaptureBugReportSave(byte[]? thumbnail, DateTime takenLocal)
    {
        if (!QuickKeysActive) return (null, "no game session (a diagnostic mode)");
        if (_mainMenu is { Root: AreaMenuWindow.Main }) return (null, "the main menu is open before a game");
        var busy = QuickBusyReason();
        if (busy != QuickBusy.None) return (null, Describe(busy));
        if (_spellGoal is not null) return (null, "a cast is playing (the session saves no cast)");
        if (_spellMissiles.Count > 0) return (null, "a spell missile is flying (the session saves no missile)");
        var grid = (AreaGridSession)_worldActor.Session!;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing);
        if (!RecordAreaCompanions("bug report save")) return (null, "the companions could not be placed: " + _companionNotice);
        var description = "Bug report " + takenLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return BugReportCapture.Save(_areaSession!, placement, session => AreaSaveList.Summary(session, description, SaveLocation()), thumbnail);
    }

    private BugReportFacts BugReportFactsNow()
    {
        var build = ViewerBuild.Current;
        var godot = Engine.GetVersionInfo()["string"].AsString();
        var session = _areaSession;
        var grid = _worldActor.Session as AreaGridSession;
        string hero;
        if (grid is null || _readyArea is null) hero = "no hero on a map";
        else
        {
            var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
            var summary = session is null ? null : AreaSaveList.Summary(session, "", "");
            hero = $"map {CurrentAreaMap} cell ({cell.X},{cell.Y}) facing {grid.State.Facing}, sector ({cell.X >> 6},{cell.Y >> 6}), {grid.State.Phase}"
                + (summary is null ? "" : $"; {summary.HeroName}, level {summary.Level}");
        }
        var combat = session is null ? "no game session"
            : $"combat mode {(HeroInCombatMode ? "on" : "off")}, " + (session.QueryCombatRound() is { } round
                ? $"turn-based round {round.Turn}, {(round.Actor.IsHero ? "the hero's" : "another participant's")} subturn, {round.ActionPoints} action points"
                : "no round (real time)") + $", turn-based setting {(_turnBasedSetting ? "on" : "off")}";
        var other = new List<(string, string)> { ("Windows", BugReportWindows() is { Length: > 0 } windows ? windows : "none (the game world)") };
        if (session is not null)
        {
            var clock = session.QueryClockSnapshot();
            other.Add(("Game time", $"year {clock.Year}, month {clock.Month}, day {clock.DayOfMonth}, {clock.Hour:00}:{clock.Minute:00}:{clock.Second:00}"));
            other.Add(("Session", $"revision {session.Version.Revision}, difficulty {session.Difficulty}"));
        }
        other.Add(("Viewer", $"{ModeName(_readyMode)}" + (_readyMapName is null ? "" : $" --area-map {_readyMapName}")));
        other.Add(("Platform", $"{OS.GetName()} {OS.GetVersion()}, {Engine.GetArchitectureName()}, Godot {godot}, window {GetViewport().GetVisibleRect().Size}"));
        other.Add(("Debug mode", DebugMode ? "on (ARC-DEV-050)" : "off"));
        other.Add(("Report key", BugReportKeyName));
        return new(build.IsPackage ? $"{build.Text} (commit {build.Commit})" : build.Text,
            session?.Rules.Mode.ToString() ?? "no game session",
            _readyArea is null ? "no map shown" : $"{SaveLocation()} (runtime map {CurrentAreaMap})", hero, combat, other);
    }

    /// <summary>The open windows under the report, in a fixed order.</summary>
    private string BugReportWindows() => string.Join(", ", new (bool Open, string Name)[]
    {
        (MainMenuOpen, "main menu"), (HeroDeathCovering, "death screen"), (PaletteOpen, "diagnostic palette"), (ConversationOpen, "dialog"),
        (InventoryOpen, "inventory"), (SheetOpen, "character sheet"), (LogbookOpen, "logbook"), (SchematicsOpen, "schematics"), (BarterOpen, "barter"),
        (ReadingOpen, "reading"), (WorldMapOpen, "world map"), (SaveListOpen, "save list"),
    }.Where(window => window.Open).Select(window => window.Name));

    private int BugReportWindowMask() => (MainMenuOpen ? 1 : 0) | (HeroDeathCovering ? 2 : 0) | (PaletteOpen ? 4 : 0) | (ConversationOpen ? 8 : 0)
        | (InventoryOpen ? 16 : 0) | (SheetOpen ? 32 : 0) | (LogbookOpen ? 64 : 0) | (SchematicsOpen ? 128 : 0) | (BarterOpen ? 256 : 0)
        | (ReadingOpen ? 512 : 0) | (WorldMapOpen ? 1024 : 0) | (SaveListOpen ? 2048 : 0) | (BugReportOpen ? 4096 : 0);

    /// <summary>Per frame: a session event line when the map, the hero's cell or phase, combat or the open windows changed.</summary>
    private void RecordBugReportEvents()
    {
        var session = _areaSession;
        var grid = _worldActor.Session as AreaGridSession;
        var round = session?.QueryCombatRound();
        var map = _readyArea is null ? -1 : CurrentAreaMap;
        var stamp = (session, map, grid?.State.CommittedCell, grid?.State.Phase, session is not null && HeroInCombatMode, round?.Turn, round?.Current,
            BugReportWindowMask());
        if (Equals(stamp, _bugReportEventStamp)) return;
        _bugReportEventStamp = stamp;
        var cell = grid is null ? "none" : grid.Chart.ToWorld(grid.State.CommittedCell) is var world ? $"({world.X},{world.Y}) {grid.State.Phase}" : "";
        BugReportLog.Process.Add(BugReportLineKind.Event, $"session map={map} cell={cell} combat={(stamp.Item5 ? "on" : "off")}"
            + (round is null ? "" : $" round={round.Turn} actor={(round.Actor.IsHero ? "hero" : "npc")}") + $" windows={BugReportWindows()}"
            + (BugReportOpen ? " +bug report" : "") + $" rev={session?.Version.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
    }

    private static BugReportPlaces BugReportPlacesNow()
    {
        // An automated run never writes beside the game or into the player's folder.
        if (ViewerAutomatedStorage.Automated) return new(null, null, System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), BugReportLocation.FolderName));
        return BugReportLocation.Places(OS.HasFeature("steamwoven_package"), OS.GetExecutablePath(),
            ProjectSettings.GlobalizePath("user://" + BugReportLocation.FolderName));
    }

    private void SendBugReport()
    {
        if (!BugReportOpen || _bugReport is not { } moment || _bugReportPanel!.Finished) return;
        var text = _bugReportPanel.Text;
        var result = BugReportWriter.Write(new(text, moment.Facts, moment.Log, moment.Dropped, moment.TakenUtc, moment.TakenLocal, moment.Screenshot?.Png,
            moment.ScreenshotMissing, moment.Save, moment.SaveMissing), BugReportPlacesNow());
        _bugReport = null; _bugReportResult = result;
        GD.Print("ARC-DEV-051 bug report " + System.Text.Json.JsonSerializer.Serialize(new
        {
            result.Saved, result.Folder, result.Fallback, result.FallbackReason, result.Written, result.Failed, TextLength = text.Length, Lines = moment.Log.Count,
        }));
        _bugReportPanel.ShowOutcome(result.Folder is null ? "The report could not be written: " + string.Join("; ", result.Failed)
            : (result.Saved ? "The report is saved in\n" : "The report is INCOMPLETE in\n") + result.Folder
              + (result.Fallback ? $"\n\nThe folder beside the game could not be used ({result.FallbackReason}), so it is in your user folder." : "")
              + (result.Failed.Count == 0 ? "" : "\n\nNot written: " + string.Join("; ", result.Failed)));
        _hud?.SetNotice(result.Status);
    }

    private void CancelBugReport() => EndBugReport("cancelled");

    private void EndBugReport(string how)
    {
        if (_bugReportPanel is null || !_bugReportPanel.IsOpen) return;
        GD.Print("ARC-DEV-051 bug report " + how);
        _bugReportPanel.CloseView(); _bugReport = null;
        if (_bugReportFocus is { } focus && IsInstanceValid(focus) && focus.IsVisibleInTree()) focus.GrabFocus();
        _bugReportFocus = null;
        QuarantineWorld(); _quickKeys.Reset(); RefreshReadyStatus();
    }

    private void SyncBugReport() => RecordBugReportEvents();

    // Native observation (ARC-DEV-051, map battery and ARC_AREA_MAP_SMOKE_ONLY=report): Ctrl+B opens the report over the game and over
    // an open window, pauses the world (modal), keeps the key's moment, and Send writes the folder (the run's isolated reports folder)
    // with report.md naming the build, map, hero, rules and combat, log.txt, a screenshot when a frame is drawn and a save the
    // ordinary quick-load path loads; Escape closes it; a cancelled report writes nothing.
    private async Task RunAreaBugReportSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-051 bug report smoke FAIL: " + why); throw new InvalidOperationException("ARC-DEV-051 bug report smoke: " + why); }
        if (_areaSession is null) { GD.Print("ARC-DEV-051 native-smoke bug report NONE (no game session)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true;
            if (WorldModal && PaletteOpen) ToggleWorldSettings();
            Require(!WorldModal, "no window is open before the report: " + BugReportWindows());
            var reports = BugReportPlacesNow().Fallback;
            var before = System.IO.Directory.Exists(reports) ? System.IO.Directory.GetDirectories(reports).Length : 0;
            // Through the production keyboard route, as the other smokes press keys.
            void Press(Key key, bool ctrl = false)
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, Pressed = false });
            }
            void Chord() => Press(Key.B, ctrl: true);
            void Escape() => Press(Key.Escape);
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            // The published scene is drawn before the key (a stage run right after the load would keep the loading frame).
            await Frames(3);
            GD.Print("ARC-DEV-051 bug report smoke marker before-key");
            Chord();
            await Frames(2);
            Require(BugReportOpen && WorldModal && !_worldGate.IsOpen, "Ctrl+B opened the modal report and closed the actor gate");
            // ARC-DEV-064: the first open of the run fits the screen (it was as tall as one word per line of the hint).
            var screen = GetViewport().GetVisibleRect(); var panel = _bugReportPanel!.PanelRectForSelfTest;
            Require(screen.Encloses(panel), $"the first report window fits the screen: {panel} in {screen}");
            Require(_canvas!.ShowActorGuides == DebugMode, $"the actor frame and anchor cross only in the debug mode: guides={_canvas.ShowActorGuides} debug={DebugMode}");
            Require(_bugReport is { Save: not null }, "the key's moment holds a save: " + _bugReport?.SaveMissing);
            Require(_bugReport!.Log.Any(line => line.Text.Contains("bug report smoke marker before-key", StringComparison.Ordinal)), "the log holds the line before the key");
            var headless = DisplayServer.GetName() == "headless";
            Require(headless ? _bugReport.Screenshot is null : _bugReport.Screenshot is not null, "a screenshot when a frame is drawn: " + _bugReport.ScreenshotMissing);
            _bugReportPanel!.SetTextForSelfTest("Smoke: the report window works");
            _bugReportPanel.PressSendForSelfTest();
            await Frames(1);
            Require(BugReportOpen && _bugReportPanel.Finished && _bugReportResult is { Saved: true, Fallback: false }, "Send wrote the report: " + _bugReportResult?.Status);
            var folder = _bugReportResult!.Folder!;
            Require(folder.StartsWith(reports, StringComparison.Ordinal), "the automated run writes into its isolated reports folder: " + folder);
            var report = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, BugReportWriter.ReportFile));
            foreach (var expected in new[] { "> Smoke: the report window works", "| Build |", "| Rules | " + _areaSession!.Rules.Mode, "| Map | " + SaveLocation(),
                "| Hero | map " + CurrentAreaMap, "| Combat | combat mode " })
                Require(report.Contains(expected, StringComparison.Ordinal), "report.md names " + expected);
            Require(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, BugReportWriter.LogFile)).Contains("bug report smoke marker before-key", StringComparison.Ordinal), "log.txt holds the line");
            Require(headless != System.IO.File.Exists(System.IO.Path.Combine(folder, BugReportWriter.ScreenshotFile)), "screenshot.png when a frame is drawn");
            var library = new GameSaveLibrary(System.IO.Path.Combine(folder, BugReportWriter.SaveFolder));
            var entry = library.List().Entries.SingleOrDefault();
            Require(entry is { Details: GameSaveDetails.Present }, "the save is a listed save of the save list");
            using (var loaded = AreaQuickSave.Load(library.Slot(entry!.Name), _areaSession, _ => null).Owner)
                Require(loaded is not null && loaded.QueryActor().Map.RuntimeMap == CurrentAreaMap, "the ordinary quick-load path loads the report's save");
            // ARC_BUG_REPORT_CAPTURE: a folder to copy the written report into, for a look at it (the run's own folder is deleted at exit).
            if (System.Environment.GetEnvironmentVariable("ARC_BUG_REPORT_CAPTURE") is { Length: > 0 } capture)
                foreach (var file in System.IO.Directory.GetFiles(folder, "*", System.IO.SearchOption.AllDirectories))
                {
                    var target = System.IO.Path.Combine(capture, System.IO.Path.GetRelativePath(folder, file));
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    System.IO.File.Copy(file, target, overwrite: true);
                }
            Escape();
            await Frames(1);
            Require(!BugReportOpen && !WorldModal, "Escape closed the sent report");

            // Over another window (the logbook): the report is above it, and Escape closes only the report.
            Press(Key.L);
            await Frames(1);
            Require(LogbookOpen, "L opened the logbook: " + _logbookStatus);
            Chord();
            await Frames(1);
            Require(BugReportOpen && LogbookOpen && _bugReport!.Facts.Other[0].Value == "logbook", "Ctrl+B opened the report over the logbook");
            Escape();
            await Frames(1);
            Require(!BugReportOpen && LogbookOpen, "Escape cancelled the report and kept the logbook");
            // ARC-DEV-111: with the report gone Escape is the main loop's key again: it opens the menu over the logbook (which stays).
            Escape();
            await Frames(1);
            Require(LogbookOpen && (MainMenuOpen || !QuickKeysActive), "Escape then opened the menu over the logbook");
            if (MainMenuOpen) { Escape(); await Frames(1); }
            Press(Key.L);
            await Frames(1);
            Require(!LogbookOpen && !WorldModal, "L then closed the logbook");
            var after = System.IO.Directory.GetDirectories(reports).Length;
            Require(after == before + 1, $"one folder for the sent report, none for the cancelled one ({before} -> {after})");
            GD.Print("ARC-DEV-051 native-smoke bug report PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Map = CurrentAreaMap, Written = _bugReportResult.Written, Lines = report.Length, Headless = headless,
            }));
        }
        finally
        {
            if (BugReportOpen) EndBugReport("closed by the smoke");
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
