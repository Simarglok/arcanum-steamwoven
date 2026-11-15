using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-2190 (owner QA epic #2185, slice 5): the owner-checks overlay. Ctrl+Q (Q has no binding in the original's WORLD and a Ctrl
// letter is silenced, ARC-INPUT-004) opens it over the game in the AREA and WORLD modes, in play, in the main menu and over any other
// window, but only when the owner-QA flag or marker is on and the QA folder is found (OwnerQaFiles.FindFolder): a normal player build
// has neither, so the key stays free and nothing is drawn. It lists the steps of local/qa/checks.json (the page's snapshot of the
// owner list), the current step first; Same / Differs / Cannot test and a note are appended to local/qa/results.jsonl (the format of
// ARC-DEV-2187, with the build's commit), and `./arcanum qa sync` or the running page turns a new differs into an issue. Send report
// writes the Ctrl+B report of the moment the key was pressed (screenshot, last 120 s of the log, a save), tagged with the step
// (ARC-DEV-2189 `Check:` line), and records its folder name in the step's result. The current step is also written to
// active-check.txt, so the page and Ctrl+B follow it. Ctrl+Shift+Q hides or shows the read-only plate that keeps the current step
// on screen (it draws no original art and takes no click). The window is modal like the bug report's (the game clock pauses).
public partial class TerrainViewerApp
{
    private AreaOwnerQaPanel? _ownerQaPanel;
    private AreaOwnerQaPlate? _ownerQaPlate;
    private OwnerQaSession? _ownerQa;
    private BugReportMoment? _ownerQaMoment;
    private bool _ownerQaPlateOn = true;
    private bool _ownerQaChecked;
    private ulong _ownerQaPollMs;
    private Key? _ownerQaClosingKey;
    private Control? _ownerQaFocus;
    private bool OwnerQaOpen => _ownerQaPanel?.IsOpen == true;

    private static bool IsOwnerQaChord(InputEventKey key, bool shift) =>
        key is { Pressed: true, Echo: false, CtrlPressed: true, AltPressed: false, MetaPressed: false }
        && key.ShiftPressed == shift && (key.Keycode == Key.Q || key.PhysicalKeycode == Key.Q);

    /// <summary>The QA folder when the overlay is on for this run, else null (a normal player build: always null).</summary>
    private static string? OwnerQaFolderNow()
    {
        try
        {
            return OwnerQaFiles.FindFolder([BugReportLocation.LaunchFolder(OS.GetExecutablePath()), ProjectSettings.GlobalizePath("res://")],
                ViewerAutomatedStorage.Automated, System.Environment.GetEnvironmentVariable(OwnerQaFiles.FolderVariable),
                System.Environment.GetEnvironmentVariable(OwnerQaFiles.EnabledVariable));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The session over the QA folder (made once per folder); false when the overlay is off.</summary>
    private bool EnsureOwnerQa()
    {
        var folder = OwnerQaFolderNow();
        if (folder is null) return false;
        if (_ownerQa is null || _ownerQa.Folder != folder)
        {
            var build = ViewerBuild.Current;
            _ownerQa = new OwnerQaSession(folder, build.Commit ?? build.Text);
        }
        if (_ownerQaPlate is null) { _ownerQaPlate = new AreaOwnerQaPlate(); AddChild(_ownerQaPlate); }
        UpdateOwnerQaPlate();
        return true;
    }

    private void UpdateOwnerQaPlate() => _ownerQaPlate?.Set(_ownerQa?.Current, _ownerQaPlateOn);

    /// <summary>Per frame in a game: the plate follows the step of the QA page (a check of the file every two seconds).</summary>
    private void SyncOwnerQa()
    {
        if (!_ownerQaChecked) { _ownerQaChecked = true; EnsureOwnerQa(); return; }
        if (_ownerQa is null || OwnerQaOpen) return;
        var now = Time.GetTicksMsec();
        if (now < _ownerQaPollMs) return;
        _ownerQaPollMs = now + 2000;
        if (_ownerQa.FollowActive()) UpdateOwnerQaPlate();
    }

    /// <summary>After the bug report in the WORLD input route: Ctrl+Q opens the overlay (only when it is on), the open overlay owns every
    /// event, Ctrl+Shift+Q hides or shows the plate. True when the event is the overlay's.</summary>
    private bool RouteOwnerQaInput(InputEvent input)
    {
        if (OwnerQaOpen)
        {
            if (input is InputEventKey key) HandleOwnerQaKey(key);
            return true;
        }
        if (_ownerQaClosingKey is { } closing && input is InputEventKey { Pressed: false } release
            && (release.Keycode != Key.None ? release.Keycode : release.PhysicalKeycode) == closing)
        {
            _ownerQaClosingKey = null;
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (input is InputEventKey { Pressed: true }) _ownerQaClosingKey = null;
        if (input is not InputEventKey press) return false;
        var open = IsOwnerQaChord(press, shift: false);
        if ((!open && !IsOwnerQaChord(press, shift: true)) || !EnsureOwnerQa()) return false;
        GetViewport().SetInputAsHandled();
        if (open) OpenOwnerQa();
        else { _ownerQaPlateOn = !_ownerQaPlateOn; UpdateOwnerQaPlate(); }
        return true;
    }

    private void HandleOwnerQaKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.Escape || IsOwnerQaChord(key, shift: false))
        {
            GetViewport().SetInputAsHandled();
            EndOwnerQa();
            _ownerQaClosingKey = routed;
        }
    }

    private void OpenOwnerQa()
    {
        if (OwnerQaOpen || _exiting || _ownerQa is null) return;
        _ownerQa.Reload();
        // The key's moment, as Ctrl+B keeps it: the screen as drawn (the plate included), the log of the last 120 s, the facts and a save.
        var moment = CaptureBugReportMoment();
        _ownerQaMoment = moment;
        if (_ownerQaPanel is null)
        {
            _ownerQaPanel = new AreaOwnerQaPanel(); AddChild(_ownerQaPanel);
            _ownerQaPanel.MarkRequested += MarkOwnerQa;
            _ownerQaPanel.NextRequested += () => { _ownerQa!.AdvanceToNextOpen(out var problem); AfterOwnerQaStepChange(problem); };
            _ownerQaPanel.StepChosen += id => AfterOwnerQaStepChange(_ownerQa!.SetCurrent(id));
            _ownerQaPanel.FilterChanged += () => _ownerQaPanel!.Refresh(null);
            _ownerQaPanel.SendRequested += SendOwnerQaReport;
            _ownerQaPanel.CloseRequested += EndOwnerQa;
        }
        _ownerQaFocus = GetViewport().GuiGetFocusOwner();
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("OWNER_QA");
        _ownerQaPanel.Open(_ownerQa, string.Join(" ", new[] { moment.ScreenshotMissing is null ? null : "No screenshot: " + moment.ScreenshotMissing + ".",
            moment.SaveMissing is null ? null : "No save: " + moment.SaveMissing + "." }.Where(part => part is not null)));
        GD.Print($"ARC-DEV-2190 owner checks open steps={_ownerQa.Steps.Count} current={_ownerQa.CurrentId ?? "none"} problem={_ownerQa.Problem ?? "none"}");
        RefreshReadyStatus();
    }

    private void AfterOwnerQaStepChange(string? problem)
    {
        _ownerQaPanel!.ClearNote();
        _ownerQaPanel.Refresh(problem is null ? "" : "The current step could not be written for Ctrl+B: " + problem);
        UpdateOwnerQaPlate();
    }

    private void MarkOwnerQa(string status)
    {
        var id = _ownerQa?.CurrentId;
        var problem = _ownerQa!.Mark(status, _ownerQaPanel!.Note, DateTime.UtcNow);
        GD.Print($"ARC-DEV-2190 owner checks mark step={id ?? "none"} status={status} problem={problem ?? "none"}");
        _ownerQaPanel.Refresh(problem is null ? $"{id}: {status} (written to local/qa/results.jsonl; ./arcanum qa sync creates the issue of a difference)" : "Not written: " + problem);
    }

    private void SendOwnerQaReport()
    {
        if (_ownerQa?.CurrentId is not { } id) { _ownerQaPanel!.ShowNotice("Pick a step first: a report is tagged with it."); return; }
        if (_ownerQaMoment is not { } moment) { _ownerQaPanel!.ShowNotice("This moment is already sent: close the window and open it again for a new report."); return; }
        var note = _ownerQaPanel!.Note;
        var result = WriteBugReportMoment(moment, note, id);
        _ownerQaMoment = null;
        _hud?.SetNotice(result.Status);
        if (result.Folder is null) { _ownerQaPanel.ShowNotice("The report could not be written: " + string.Join("; ", result.Failed)); return; }
        var name = System.IO.Path.GetFileName(result.Folder.TrimEnd('/', '\\'));
        var problem = _ownerQa.RecordReport(name, note, DateTime.UtcNow);
        _ownerQaPanel.Refresh((result.Saved ? "Report saved in " : "Report INCOMPLETE in ") + result.Folder
            + (problem is null ? ", its folder name recorded for " + id + "." : ". The result could not be written: " + problem)
            + (result.Failed.Count == 0 ? "" : " Not written: " + string.Join("; ", result.Failed)));
    }

    private void EndOwnerQa()
    {
        if (_ownerQaPanel is null || !_ownerQaPanel.IsOpen) return;
        GD.Print("ARC-DEV-2190 owner checks closed");
        _ownerQaPanel.CloseView(); _ownerQaMoment = null;
        if (_ownerQaFocus is { } focus && IsInstanceValid(focus) && focus.IsVisibleInTree()) focus.GrabFocus();
        _ownerQaFocus = null;
        UpdateOwnerQaPlate();
        QuarantineWorld(); _quickKeys.Reset(); RefreshReadyStatus();
    }

    // Native observation (ARC-DEV-2190, ARC_AREA_MAP_SMOKE_ONLY=report): with a synthetic QA folder (three steps in two sections, the run's
    // isolated storage) Ctrl+Q opens the modal overlay with the first open step current; picking a step writes active-check.txt and moves
    // the plate; Send report writes a report whose report.md carries `Check:` and records its folder name; Same / Differs append results
    // with the build; the plate takes no click and hides on Ctrl+Shift+Q; Escape closes; with the flag off Ctrl+Q does nothing.
    private async Task RunAreaOwnerQaSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-2190 owner checks smoke FAIL: " + why); throw new InvalidOperationException("ARC-DEV-2190 owner checks smoke: " + why); }
        if (_areaSession is null) { GD.Print("ARC-DEV-2190 native-smoke owner-qa NONE (no game session)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var folder = System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), "owner-qa-smoke");
        try
        {
            _worldFocused = true;
            if (WorldModal && PaletteOpen) ToggleWorldSettings();
            Require(!WorldModal, "no window is open before the overlay: " + BugReportWindows());
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, OwnerQaFiles.ChecksFile), System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 1, commit = "smoke123",
                steps = new[]
                {
                    new { id = "ARC-DEV-9001#1", section = "ARC-DEV-9001 - first", step = "Open the first door", expected = "It opens", status = "open" },
                    new { id = "ARC-DEV-9001#2", section = "ARC-DEV-9001 - first", step = "Close the door", expected = "It closes", status = "open" },
                    new { id = "ARC-DEV-9002#1", section = "ARC-DEV-9002 - second", step = "Walk to the well", expected = "The hero walks", status = "same" },
                },
            }));
            var reports = BugReportPlacesNow().Fallback;
            var before = System.IO.Directory.Exists(reports) ? System.IO.Directory.GetDirectories(reports).Length : 0;
            void Press(Key key, bool ctrl = false, bool shift = false)
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, ShiftPressed = shift, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, ShiftPressed = shift, Pressed = false });
            }
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            string[] ResultLines() => System.IO.File.Exists(System.IO.Path.Combine(folder, OwnerQaFiles.ResultsFile))
                ? System.IO.File.ReadAllLines(System.IO.Path.Combine(folder, OwnerQaFiles.ResultsFile)) : [];
            await Frames(3);
            // ARC_OWNER_QA_CAPTURE: a folder for pictures of the window (before = the game with the flag off, overlay, plate), for a look at it.
            var capture = System.Environment.GetEnvironmentVariable("ARC_OWNER_QA_CAPTURE");
            async Task Shot(string name)
            {
                if (string.IsNullOrEmpty(capture) || DisplayServer.GetName() == "headless") return;
                await Frames(2);
                System.IO.Directory.CreateDirectory(capture);
                GetViewport().GetTexture().GetImage()?.SavePng(System.IO.Path.Combine(capture, name + ".png"));
            }
            await Shot("before");
            // The flag is off: the key stays free (no folder is named, the run is automated).
            Press(Key.Q, ctrl: true);
            await Frames(1);
            Require(!OwnerQaOpen && !WorldModal, "Ctrl+Q does nothing while the owner-QA flag is off");
            System.Environment.SetEnvironmentVariable(OwnerQaFiles.FolderVariable, folder);
            Press(Key.Q, ctrl: true);
            await Frames(2);
            Require(OwnerQaOpen && WorldModal && !_worldGate.IsOpen, "Ctrl+Q opened the modal overlay and closed the actor gate");
            var panel = _ownerQaPanel!;
            Require(GetViewport().GetVisibleRect().Encloses(panel.PanelRectForSelfTest), $"the overlay fits the screen: {panel.PanelRectForSelfTest}");
            Require(_ownerQa!.CurrentId == "ARC-DEV-9001#1" && panel.RowCountForSelfTest == 2, $"the first open step is current, open rows only: {_ownerQa.CurrentId} rows={panel.RowCountForSelfTest}");
            Require(panel.RowTextForSelfTest(0).StartsWith("> ARC-DEV-9001#1", StringComparison.Ordinal) && panel.DetailForSelfTest.Contains("Expected: It opens", StringComparison.Ordinal), "the current step is listed first with its text");
            Require(_ownerQaPlate!.IsShown && _ownerQaPlate.TextForSelfTest.Contains("ARC-DEV-9001#1", StringComparison.Ordinal), "the plate shows the current step");
            Require(_ownerQaPlate.MouseFilterForSelfTest == Control.MouseFilterEnum.Ignore, "the plate takes no click");
            await Shot("overlay");
            // A step picked in the list becomes current: Ctrl+B and the page follow active-check.txt.
            panel.PressForSelfTest("all");
            Require(panel.RowCountForSelfTest == 3, "the All filter lists every step: " + panel.RowCountForSelfTest);
            AfterOwnerQaStepChange(_ownerQa.SetCurrent("ARC-DEV-9001#2"));
            Require(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, BugReportCheck.FileName)).Trim() == "ARC-DEV-9001#2", "the current step is written to active-check.txt");
            Require(_ownerQaPlate.TextForSelfTest.Contains("ARC-DEV-9001#2", StringComparison.Ordinal), "the plate follows the current step");
            // Send: the Ctrl+B report of the key's moment tagged with the step, its folder name recorded.
            panel.SetNoteForSelfTest("Smoke: the door stays open");
            panel.PressForSelfTest("send");
            await Frames(1);
            Require(_bugReportResult is { Saved: true, Fallback: false } && _bugReportResult.Folder!.StartsWith(reports, StringComparison.Ordinal), "Send wrote the report into the isolated reports folder: " + _bugReportResult?.Status);
            var sent = _bugReportResult!.Folder!;
            var report = System.IO.File.ReadAllText(System.IO.Path.Combine(sent, BugReportWriter.ReportFile));
            Require(BugReportCheck.ReadReportLine(report) == "ARC-DEV-9001#2" && report.Contains("> Smoke: the door stays open", StringComparison.Ordinal), "report.md carries the Check line and the note");
            var recorded = ResultLines().LastOrDefault() ?? "";
            Require(recorded.Contains("\"step\":\"ARC-DEV-9001#2\"", StringComparison.Ordinal) && recorded.Contains("\"report\":\"" + System.IO.Path.GetFileName(sent) + "\"", StringComparison.Ordinal)
                && recorded.Contains("\"status\":\"open\"", StringComparison.Ordinal), "the report folder name is recorded in the step's result: " + recorded);
            // Differs keeps the report; Same writes its own line; both carry the build.
            panel.PressForSelfTest("differs");
            var differs = ResultLines().Last();
            Require(differs.Contains("\"status\":\"differs\"", StringComparison.Ordinal) && differs.Contains("\"report\":\"" + System.IO.Path.GetFileName(sent) + "\"", StringComparison.Ordinal)
                && differs.Contains("\"build\":", StringComparison.Ordinal) && differs.Contains("\"note\":\"Smoke: the door stays open\"", StringComparison.Ordinal), "Differs is recorded with the note, the report and the build: " + differs);
            _ownerQa.SetCurrent("ARC-DEV-9001#1");
            panel.PressForSelfTest("same");
            var all = ResultLines();
            Require(all.Length == 3 && all[^1].Contains("\"step\":\"ARC-DEV-9001#1\"", StringComparison.Ordinal) && all[^1].Contains("\"status\":\"same\"", StringComparison.Ordinal), "Same is one more line: " + all.Length);
            Require(_ownerQa.ResultOf("ARC-DEV-9001#2")?.Status == "differs", "the latest event of a step wins");
            panel.PressForSelfTest("open");
            Require(panel.RowCountForSelfTest == 1, "the Open filter hides the marked steps (only the current one stays first): " + panel.RowCountForSelfTest);
            // Escape closes; the plate stays, hides on Ctrl+Shift+Q and shows again.
            Press(Key.Escape);
            await Frames(1);
            Require(!OwnerQaOpen && !WorldModal, "Escape closed the overlay");
            Require(_ownerQaPlate.IsShown, "the plate stays after the overlay closes");
            await Shot("plate");
            Press(Key.Q, ctrl: true, shift: true);
            Require(!_ownerQaPlate.IsShown, "Ctrl+Shift+Q hides the plate");
            Press(Key.Q, ctrl: true, shift: true);
            Require(_ownerQaPlate.IsShown, "Ctrl+Shift+Q shows the plate again");
            // Over another window (the logbook): the overlay opens above it and Ctrl+Q closes only the overlay.
            Press(Key.L);
            await Frames(1);
            Require(LogbookOpen, "L opened the logbook: " + _logbookStatus);
            Press(Key.Q, ctrl: true);
            await Frames(1);
            Require(OwnerQaOpen && LogbookOpen, "Ctrl+Q opened the overlay over the logbook");
            Press(Key.Q, ctrl: true);
            await Frames(1);
            Require(!OwnerQaOpen && LogbookOpen, "Ctrl+Q closed the overlay and kept the logbook");
            Press(Key.L);
            await Frames(1);
            Require(!LogbookOpen && !WorldModal, "L then closed the logbook");
            Require(System.IO.Directory.GetDirectories(reports).Length == before + 1, "one report folder for the one Send");
            GD.Print("ARC-DEV-2190 native-smoke owner-qa PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = CurrentAreaMap, Results = all.Length }));
        }
        finally
        {
            if (OwnerQaOpen) EndOwnerQa();
            System.Environment.SetEnvironmentVariable(OwnerQaFiles.FolderVariable, null);
            _ownerQa = null; _ownerQaPlate?.Set(null, false);
            try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true); } catch (System.IO.IOException) { }
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
